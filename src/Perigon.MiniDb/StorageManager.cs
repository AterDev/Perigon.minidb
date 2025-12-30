using System.Buffers;
using System.Collections.Frozen;
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
internal class StorageManager
{
    private const int FILE_HEADER_SIZE = 256;
    private const int TABLE_META_SIZE = 128;
    private const string MAGIC_NUMBER = "MDB1";
    private const short VERSION = 1;

    private readonly string _filePath;
    private readonly FileWriteQueue _writeQueue;
    private readonly Dictionary<string, TableMetadata> _tables = [];
    private FrozenDictionary<Type, EntityMetadata> _entityMetadataCache = FrozenDictionary<Type, EntityMetadata>.Empty;

    public StorageManager(string filePath, FileWriteQueue writeQueue)
    {
        _filePath = filePath;
        _writeQueue = writeQueue;
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
        Span<byte> magicBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(MAGIC_NUMBER, magicBytes);
        writer.Write(magicBytes);
        writer.Write(VERSION);
        writer.Write((short)tableTypes.Count);

        Span<byte> reserved = stackalloc byte[248];
        reserved.Clear();
        writer.Write(reserved);

        long currentOffset = FILE_HEADER_SIZE + (tableTypes.Count * TABLE_META_SIZE);

        // Build metadata cache
        var metadataBuilder = new Dictionary<Type, EntityMetadata>();

        // Write table metadata
        foreach (var kvp in tableTypes)
        {
            var metadata = EntityMetadata.Create(kvp.Value);
            metadataBuilder[kvp.Value] = metadata;

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

        // Freeze the metadata cache after initialization
        _entityMetadataCache = metadataBuilder.ToFrozenDictionary();
    }

    private void WriteTableMetadata(BinaryWriter writer, TableMetadata metadata)
    {
        Span<byte> nameBuffer = stackalloc byte[64];
        nameBuffer.Clear();

        int bytesWritten = Encoding.UTF8.GetBytes(metadata.TableName, nameBuffer);
        if (bytesWritten > 64)
        {
            throw new InvalidOperationException(
                $"Table name '{metadata.TableName}' exceeds the 64-byte limit in UTF-8 encoding.");
        }

        writer.Write(nameBuffer);
        writer.Write(metadata.RecordCount);
        writer.Write(metadata.RecordSize);
        writer.Write(metadata.DataStartOffset);

        Span<byte> reserved = stackalloc byte[48];
        reserved.Clear();
        writer.Write(reserved);
    }

    private void LoadDatabase()
    {
        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file);

        // Read file header
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != MAGIC_NUMBER)
            throw new InvalidDataException("Invalid database file format");

        var version = reader.ReadInt16();
        if (version != VERSION)
            throw new InvalidDataException($"Unsupported database version: {version}. Expected version: {VERSION}.");

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

    public async Task<List<T>> LoadTableAsync<T>(string tableName, CancellationToken cancellationToken = default) where T : class, IMicroEntity, new()
    {
        var result = new List<T>();
        if (!_tables.TryGetValue(tableName, out var tableMetadata))
            return result;

        if (tableMetadata.RecordCount == 0)
            return result;

        var entityMetadata = GetOrCreateEntityMetadata(typeof(T));
        byte[]? rentedBuffer = null;

        try
        {
            await using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, useAsync: true);
            file.Seek(tableMetadata.DataStartOffset, SeekOrigin.Begin);

            rentedBuffer = ArrayPool<byte>.Shared.Rent(tableMetadata.RecordSize);
            var buffer = rentedBuffer.AsMemory(0, tableMetadata.RecordSize);

            for (int i = 0; i < tableMetadata.RecordCount; i++)
            {
                await file.ReadExactlyAsync(buffer, cancellationToken);

                // Check IsDeleted flag
                if (buffer.Span[0] == 0)
                {
                    var entity = DeserializeRecord<T>(buffer.Span, entityMetadata);
                    result.Add(entity);
                }
            }

            return result;
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    public async Task SaveChangesAsync<T>(string tableName, List<T> added, List<T> modified, List<T> deleted,
        CancellationToken cancellationToken = default) where T : class, IMicroEntity
    {
        // Queue the write operation to ensure single-threaded file access
        await _writeQueue.QueueWriteAsync(async () =>
        {
            await SaveChangesInternalAsync(tableName, added, modified, deleted, cancellationToken);
        }, cancellationToken);
    }

    private async Task SaveChangesInternalAsync<T>(string tableName, List<T> added, List<T> modified, List<T> deleted,
        CancellationToken cancellationToken = default) where T : class, IMicroEntity
    {
        var tableMetadata = _tables[tableName];
        var entityMetadata = GetOrCreateEntityMetadata(typeof(T));

        await using var file = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
            bufferSize: 4096, useAsync: true);

        // Handle added records
        foreach (var entity in added)
        {
            var buffer = SerializeRecord(entity, entityMetadata);
            file.Seek(tableMetadata.DataStartOffset + (tableMetadata.RecordCount * tableMetadata.RecordSize), SeekOrigin.Begin);
            await file.WriteAsync(buffer, cancellationToken);
            tableMetadata.RecordCount++;
        }

        // Handle modified records
        foreach (var entity in modified)
        {
            var id = entity.Id;
            var buffer = SerializeRecord(entity, entityMetadata);
            long offset = tableMetadata.DataStartOffset + ((id - 1) * tableMetadata.RecordSize);
            file.Seek(offset, SeekOrigin.Begin);
            await file.WriteAsync(buffer, cancellationToken);
        }

        // Handle deleted records (soft delete)
        foreach (var entity in deleted)
        {
            var id = entity.Id;
            long offset = tableMetadata.DataStartOffset + ((id - 1) * tableMetadata.RecordSize);
            file.Seek(offset, SeekOrigin.Begin);
            await file.WriteAsync(new byte[] { 1 }, cancellationToken); // Set IsDeleted flag
        }

        // Ensure data is written to disk
        await file.FlushAsync(cancellationToken);

        // Update table metadata in the same file stream
        await UpdateTableMetadataAsync(tableName, file, cancellationToken);
        await file.FlushAsync(cancellationToken);
    }

    private async Task UpdateTableMetadataAsync(string tableName, FileStream file, CancellationToken cancellationToken = default)
    {
        var tableMetadata = _tables[tableName];
        var tableIndex = _tables.Keys.ToList().IndexOf(tableName);
        long metadataOffset = FILE_HEADER_SIZE + (tableIndex * TABLE_META_SIZE);

        file.Seek(metadataOffset, SeekOrigin.Begin);

        await using var memoryStream = new MemoryStream();
        await using var writer = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true);
        WriteTableMetadata(writer, tableMetadata);

        memoryStream.Seek(0, SeekOrigin.Begin);
        await memoryStream.CopyToAsync(file, cancellationToken);
    }

    private void UpdateTableMetadata(string tableName)
    {
        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite);
        UpdateTableMetadata(tableName, file);
    }

    private void UpdateTableMetadata(string tableName, FileStream file)
    {
        var tableMetadata = _tables[tableName];
        var tableIndex = _tables.Keys.ToList().IndexOf(tableName);
        long metadataOffset = FILE_HEADER_SIZE + (tableIndex * TABLE_META_SIZE);

        file.Seek(metadataOffset, SeekOrigin.Begin);
        using var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
        WriteTableMetadata(writer, tableMetadata);
    }

    private byte[] SerializeRecord<T>(T entity, EntityMetadata metadata) where T : IMicroEntity
    {
        var buffer = new byte[metadata.RecordSize];
        var span = buffer.AsSpan();

        // IsDeleted flag (always 0 for new/modified records)
        span[0] = 0;
        int offset = 1;
        
        // Write Id (4 bytes)
        BitConverter.TryWriteBytes(span[offset..], entity.Id);
        offset += 4;

        foreach (var field in metadata.Fields)
        {
            var value = field.Property.GetValue(entity);
            WriteField(span[offset..], value, field.Property.PropertyType, field.Size);
            offset += field.Size;
        }

        return buffer;
    }

    private T DeserializeRecord<T>(ReadOnlySpan<byte> buffer, EntityMetadata metadata) where T : class, IMicroEntity, new()
    {
        var entity = new T();
        int offset = 1; // Skip IsDeleted
        
        // Read Id (4 bytes)
        entity.Id = BitConverter.ToInt32(buffer[offset..]);
        offset += 4;

        foreach (var field in metadata.Fields)
        {
            var value = ReadField(buffer[offset..], field.Property.PropertyType, field.Size);
            field.Property.SetValue(entity, value);
            offset += field.Size;
        }

        return entity;
    }

    private void WriteField(Span<byte> buffer, object? value, Type type, int size)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        bool isNullable = Nullable.GetUnderlyingType(type) != null;
        bool isNull = value == null;

        int offset = 0;
        if (isNullable)
        {
            buffer[0] = isNull ? (byte)1 : (byte)0;
            offset = 1;
        }

        if (value == null)
            return;

        var dataSpan = buffer[offset..];
        int dataSize = size - offset; // Adjust size to account for nullable byte

        if (underlyingType == typeof(string))
        {
            var str = (string)value;

            // Truncate string if it's too long for the buffer
            // Use binary search to find the maximum number of characters that fit in the buffer
            int maxChars = str.Length;
            if (Encoding.UTF8.GetByteCount(str) > dataSize)
            {
                // Binary search to find the maximum number of characters that fit
                int low = 0, high = str.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    if (Encoding.UTF8.GetByteCount(str.AsSpan(0, mid)) <= dataSize)
                        low = mid;
                    else
                        high = mid - 1;
                }
                maxChars = low;
            }

            int bytesWritten = Encoding.UTF8.GetBytes(str.AsSpan(0, maxChars), dataSpan);

            // Ensure we don't split UTF-8 multi-byte characters at the boundary
            // Check if we truncated and the last byte indicates a multi-byte character
            if (bytesWritten > 0 && maxChars < str.Length && (dataSpan[bytesWritten - 1] & 0x80) != 0)
            {
                // Scan backwards to find a valid UTF-8 character boundary
                while (bytesWritten > 0 && (dataSpan[bytesWritten - 1] & 0xC0) == 0x80)
                {
                    bytesWritten--;
                }
            }

            // Clear remaining bytes
            if (bytesWritten < dataSize)
            {
                dataSpan[bytesWritten..dataSize].Clear();
            }
        }
        else if (underlyingType == typeof(int))
        {
            BitConverter.TryWriteBytes(dataSpan, (int)value);
        }
        else if (underlyingType == typeof(bool))
        {
            dataSpan[0] = (bool)value ? (byte)1 : (byte)0;
        }
        else if (underlyingType == typeof(decimal))
        {
            Span<int> bits = stackalloc int[4];
            decimal.GetBits((decimal)value, bits);
            for (int i = 0; i < 4; i++)
            {
                BitConverter.TryWriteBytes(dataSpan[(i * 4)..], bits[i]);
            }
        }
        else if (underlyingType == typeof(DateTime))
        {
            var dt = (DateTime)value;
            DateTime utcTime = dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };

            BitConverter.TryWriteBytes(dataSpan, utcTime.Ticks);
        }
    }

    private object? ReadField(ReadOnlySpan<byte> buffer, Type type, int size)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        bool isNullable = Nullable.GetUnderlyingType(type) != null;

        int offset = 0;
        if (isNullable)
        {
            bool isNull = buffer[0] == 1;
            offset = 1;
            if (isNull)
                return null;
        }

        // The remaining data starts after the nullable byte (if any)
        var dataSpan = buffer[offset..size];

        if (underlyingType == typeof(string))
        {
            int length = dataSpan.IndexOf((byte)0);
            if (length < 0) length = dataSpan.Length;
            return Encoding.UTF8.GetString(dataSpan[..length]);
        }
        else if (underlyingType == typeof(int))
        {
            return BitConverter.ToInt32(dataSpan);
        }
        else if (underlyingType == typeof(bool))
        {
            return dataSpan[0] != 0;
        }
        else if (underlyingType == typeof(decimal))
        {
            if (dataSpan.Length < 16)
            {
                throw new InvalidOperationException($"Insufficient data for decimal field: expected 16 bytes, got {dataSpan.Length} bytes");
            }
            Span<int> bits = stackalloc int[4];
            for (int i = 0; i < 4; i++)
                bits[i] = BitConverter.ToInt32(dataSpan[(i * 4)..]);
            return new decimal(bits);
        }
        else if (underlyingType == typeof(DateTime))
        {
            long ticks = BitConverter.ToInt64(dataSpan);
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        return null;
    }

    private EntityMetadata GetOrCreateEntityMetadata(Type type)
    {
        if (_entityMetadataCache.TryGetValue(type, out var metadata))
        {
            return metadata;
        }

        metadata = EntityMetadata.Create(type);

        // Rebuild frozen dictionary with new entry
        var builder = _entityMetadataCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        builder[type] = metadata;
        _entityMetadataCache = builder.ToFrozenDictionary();

        return metadata;
    }

    public TableMetadata? GetTableMetadata(string tableName)
    {
        return _tables.TryGetValue(tableName, out var tableMetadata) ? tableMetadata : null;
    }
}
