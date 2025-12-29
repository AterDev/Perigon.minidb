using System.Text;

namespace Perigon.MiniDb;

/// <summary>
/// Table metadata stored in file header
/// </summary>
public class TableMetadata
{
    public string TableName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int RecordSize { get; set; }
    public long DataStartOffset { get; set; }
}

/// <summary>
/// Manage file I/O with fixed-length binary format
/// </summary>
public class StorageManager
{
    private const int FILE_HEADER_SIZE = 256;
    private const int TABLE_META_SIZE = 128;
    private const string MAGIC_NUMBER = "MDB1";
    private const short VERSION = 1;

    private readonly string _filePath;
    private readonly Dictionary<string, TableMetadata> _tables = new();
    private readonly Dictionary<Type, EntityMetadata> _entityMetadataCache = new();

    public StorageManager(string filePath)
    {
        _filePath = filePath;
    }

    public void Initialize(Dictionary<string, Type> tableTypes)
    {
        if (File.Exists(_filePath))
        {
            LoadDatabase();
        }
        else
        {
            CreateDatabase(tableTypes);
        }
    }

    private void CreateDatabase(Dictionary<string, Type> tableTypes)
    {
        using var file = new FileStream(_filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(file);

        // Write file header
        writer.Write(Encoding.ASCII.GetBytes(MAGIC_NUMBER));
        writer.Write(VERSION);
        writer.Write((short)tableTypes.Count);
        writer.Write(new byte[248]); // Reserved

        long currentOffset = FILE_HEADER_SIZE + (tableTypes.Count * TABLE_META_SIZE);

        // Write table metadata
        foreach (var kvp in tableTypes)
        {
            var metadata = EntityMetadata.Create(kvp.Value);
            _entityMetadataCache[kvp.Value] = metadata;

            var tableMetadata = new TableMetadata
            {
                TableName = kvp.Key,
                RecordCount = 0,
                RecordSize = metadata.RecordSize,
                DataStartOffset = currentOffset
            };
            _tables[kvp.Key] = tableMetadata;

            WriteTableMetadata(writer, tableMetadata);
        }
    }

    private void WriteTableMetadata(BinaryWriter writer, TableMetadata metadata)
    {
        var nameBytes = Encoding.UTF8.GetBytes(metadata.TableName);
        writer.Write(nameBytes);
        writer.Write(new byte[64 - nameBytes.Length]); // Pad to 64 bytes
        writer.Write(metadata.RecordCount);
        writer.Write(metadata.RecordSize);
        writer.Write(metadata.DataStartOffset);
        writer.Write(new byte[48]); // Reserved (128 total - 64 name - 4 count - 4 size - 8 offset = 48)
    }

    private void LoadDatabase()
    {
        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(file);

        // Read file header
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != MAGIC_NUMBER)
            throw new InvalidDataException("Invalid database file format");

        var version = reader.ReadInt16();
        if (version != VERSION)
            throw new InvalidDataException($"Unsupported database version: {version}");

        var tableCount = reader.ReadInt16();
        reader.ReadBytes(248); // Skip reserved

        // Read table metadata
        for (int i = 0; i < tableCount; i++)
        {
            var nameBytes = reader.ReadBytes(64);
            var tableName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
            var recordCount = reader.ReadInt32();
            var recordSize = reader.ReadInt32();
            var dataStartOffset = reader.ReadInt64();
            reader.ReadBytes(48); // Skip reserved

            _tables[tableName] = new TableMetadata
            {
                TableName = tableName,
                RecordCount = recordCount,
                RecordSize = recordSize,
                DataStartOffset = dataStartOffset
            };
        }
    }

    public List<T> LoadTable<T>(string tableName) where T : new()
    {
        var result = new List<T>();
        if (!_tables.ContainsKey(tableName))
            return result;

        var tableMetadata = _tables[tableName];
        if (tableMetadata.RecordCount == 0)
            return result;

        var entityMetadata = GetOrCreateEntityMetadata(typeof(T));

        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
        file.Seek(tableMetadata.DataStartOffset, SeekOrigin.Begin);

        for (int i = 0; i < tableMetadata.RecordCount; i++)
        {
            var buffer = new byte[tableMetadata.RecordSize];
            file.Read(buffer, 0, tableMetadata.RecordSize);

            // Check IsDeleted flag
            if (buffer[0] == 0)
            {
                var entity = DeserializeRecord<T>(buffer, entityMetadata);
                result.Add(entity);
            }
        }

        return result;
    }

    public void SaveChanges<T>(string tableName, List<T> added, List<T> modified, List<T> deleted)
    {
        var tableMetadata = _tables[tableName];
        var entityMetadata = GetOrCreateEntityMetadata(typeof(T));

        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite);

        // Handle added records
        foreach (var entity in added)
        {
            var buffer = SerializeRecord(entity, entityMetadata);
            file.Seek(tableMetadata.DataStartOffset + tableMetadata.RecordCount * tableMetadata.RecordSize, SeekOrigin.Begin);
            file.Write(buffer, 0, buffer.Length);
            tableMetadata.RecordCount++;
        }

        // Handle modified records
        foreach (var entity in modified)
        {
            var id = GetEntityId(entity);
            var buffer = SerializeRecord(entity, entityMetadata);
            long offset = tableMetadata.DataStartOffset + (id - 1) * tableMetadata.RecordSize;
            file.Seek(offset, SeekOrigin.Begin);
            file.Write(buffer, 0, buffer.Length);
        }

        // Handle deleted records (soft delete)
        foreach (var entity in deleted)
        {
            var id = GetEntityId(entity);
            long offset = tableMetadata.DataStartOffset + (id - 1) * tableMetadata.RecordSize;
            file.Seek(offset, SeekOrigin.Begin);
            file.WriteByte(1); // Set IsDeleted flag
        }

        // Update table metadata
        UpdateTableMetadata(tableName);
    }

    private void UpdateTableMetadata(string tableName)
    {
        var tableMetadata = _tables[tableName];
        var tableIndex = _tables.Keys.ToList().IndexOf(tableName);
        long metadataOffset = FILE_HEADER_SIZE + tableIndex * TABLE_META_SIZE;

        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite);
        file.Seek(metadataOffset, SeekOrigin.Begin);
        using var writer = new BinaryWriter(file);
        WriteTableMetadata(writer, tableMetadata);
    }

    private byte[] SerializeRecord<T>(T entity, EntityMetadata metadata)
    {
        var buffer = new byte[metadata.RecordSize];
        int offset = 0;

        // IsDeleted flag (always 0 for new/modified records)
        buffer[offset++] = 0;

        foreach (var field in metadata.Fields)
        {
            var value = field.Property.GetValue(entity);
            WriteField(buffer, offset, value, field.Property.PropertyType, field.Size);
            offset += field.Size;
        }

        return buffer;
    }

    private T DeserializeRecord<T>(byte[] buffer, EntityMetadata metadata) where T : new()
    {
        var entity = new T();
        int offset = 1; // Skip IsDeleted

        foreach (var field in metadata.Fields)
        {
            var value = ReadField(buffer, offset, field.Property.PropertyType, field.Size);
            field.Property.SetValue(entity, value);
            offset += field.Size;
        }

        return entity;
    }

    private void WriteField(byte[] buffer, int offset, object? value, Type type, int size)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        bool isNullable = Nullable.GetUnderlyingType(type) != null;
        bool isNull = value == null;

        if (isNullable)
        {
            buffer[offset] = isNull ? (byte)1 : (byte)0;
            offset += 1;
        }

        if (isNull)
            return;

        if (underlyingType == typeof(string))
        {
            var str = (string?)value ?? "";
            var bytes = Encoding.UTF8.GetBytes(str);
            int copyLength = Math.Min(bytes.Length, size);
            
            // Ensure we don't split UTF-8 multi-byte characters
            if (copyLength < bytes.Length && copyLength > 0)
            {
                // Scan backwards to find a valid UTF-8 character boundary
                while (copyLength > 0 && (bytes[copyLength] & 0xC0) == 0x80)
                {
                    copyLength--;
                }
            }
            
            Array.Copy(bytes, 0, buffer, offset, copyLength);
        }
        else if (underlyingType == typeof(int))
        {
            BitConverter.GetBytes((int)value).CopyTo(buffer, offset);
        }
        else if (underlyingType == typeof(bool))
        {
            buffer[offset] = (bool)value ? (byte)1 : (byte)0;
        }
        else if (underlyingType == typeof(decimal))
        {
            var bits = decimal.GetBits((decimal)value);
            for (int i = 0; i < 4; i++)
            {
                BitConverter.GetBytes(bits[i]).CopyTo(buffer, offset + i * 4);
            }
        }
        else if (underlyingType == typeof(DateTime))
        {
            var utcTime = ((DateTime)value).ToUniversalTime();
            BitConverter.GetBytes(utcTime.Ticks).CopyTo(buffer, offset);
        }
    }

    private object? ReadField(byte[] buffer, int offset, Type type, int size)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        bool isNullable = Nullable.GetUnderlyingType(type) != null;

        if (isNullable)
        {
            bool isNull = buffer[offset] == 1;
            offset += 1;
            if (isNull)
                return null;
        }

        if (underlyingType == typeof(string))
        {
            int length = 0;
            for (int i = 0; i < size && buffer[offset + i] != 0; i++)
                length++;
            return Encoding.UTF8.GetString(buffer, offset, length);
        }
        else if (underlyingType == typeof(int))
        {
            return BitConverter.ToInt32(buffer, offset);
        }
        else if (underlyingType == typeof(bool))
        {
            return buffer[offset] != 0;
        }
        else if (underlyingType == typeof(decimal))
        {
            int[] bits = new int[4];
            for (int i = 0; i < 4; i++)
                bits[i] = BitConverter.ToInt32(buffer, offset + i * 4);
            return new decimal(bits);
        }
        else if (underlyingType == typeof(DateTime))
        {
            long ticks = BitConverter.ToInt64(buffer, offset);
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        return null;
    }

    private int GetEntityId(object entity)
    {
        var idProperty = entity.GetType().GetProperty("Id");
        if (idProperty == null || idProperty.PropertyType != typeof(int))
            throw new InvalidOperationException("Entity must have an 'Id' property of type int");

        return (int)idProperty.GetValue(entity)!;
    }

    private EntityMetadata GetOrCreateEntityMetadata(Type type)
    {
        if (!_entityMetadataCache.ContainsKey(type))
        {
            _entityMetadataCache[type] = EntityMetadata.Create(type);
        }
        return _entityMetadataCache[type];
    }

    public TableMetadata? GetTableMetadata(string tableName)
    {
        return _tables.ContainsKey(tableName) ? _tables[tableName] : null;
    }
}
