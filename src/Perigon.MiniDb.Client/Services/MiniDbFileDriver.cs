using System.Text;

namespace Perigon.MiniDb.Client.Services;

public sealed class MiniDbFileDriver
{
    private const int HeaderReservedRemainingBytes = 240;
    private const int TableMetaReservedRemainingBytesV1 = 28;
    private const int TableMetaReservedRemainingBytesV2 = 16;
    private const int ExtentRecordGrowth = 1000;
    private const string MagicNumber = "MDB1";
    private const short MinSupportedVersion = 1;
    private const short MaxSupportedVersion = 2;

    public MiniDbDriverSession Open(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException("MiniDb file not found.", normalizedPath);
        }

        var fileMetadata = ReadMetadata(normalizedPath);
        var schemas = ReadEmbeddedSchemas(normalizedPath, fileMetadata.Tables);
        ValidateSchemaCoverage(fileMetadata, schemas);

        return new MiniDbDriverSession(normalizedPath, fileMetadata.Version, fileMetadata.Tables, schemas);
    }

    private static void ValidateSchemaCoverage(DriverFileMetadata fileMetadata, IReadOnlyDictionary<string, MiniDbTableSchema> schemas)
    {
        if (fileMetadata.Version < 2)
        {
            throw new InvalidDataException($"Unsupported MiniDb version: {fileMetadata.Version}. This client requires schema-enabled database files.");
        }

        foreach (var table in fileMetadata.Tables.Values)
        {
            if (table.SchemaOffset <= 0 || table.SchemaLength <= 0)
            {
                throw new InvalidDataException($"Table '{table.TableName}' has no embedded schema. Not a valid schema-enabled database file.");
            }

            if (!schemas.TryGetValue(table.TableName, out var schema) || schema.Fields.Count == 0)
            {
                throw new InvalidDataException($"Table '{table.TableName}' schema is missing or invalid. Not a valid database file.");
            }
        }
    }

    private static Dictionary<string, MiniDbTableSchema> ReadEmbeddedSchemas(
        string filePath,
        IReadOnlyDictionary<string, DriverTableMetadata> metadata)
    {
        var result = new Dictionary<string, MiniDbTableSchema>(StringComparer.Ordinal);

        using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file);

        foreach (var table in metadata.Values)
        {
            if (table.SchemaOffset <= 0 || table.SchemaLength <= 0)
            {
                continue;
            }

            file.Seek(table.SchemaOffset, SeekOrigin.Begin);
            var bytes = reader.ReadBytes(table.SchemaLength);
            if (bytes.Length != table.SchemaLength)
            {
                continue;
            }

            var schema = DeserializeEmbeddedSchema(bytes, table.TableName);
            if (schema is not null)
            {
                result[table.TableName] = schema;
            }
        }

        return result;
    }

    private static MiniDbTableSchema? DeserializeEmbeddedSchema(byte[] bytes, string tableName)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var schemaVersion = reader.ReadInt32();
            if (schemaVersion != 1)
            {
                return null;
            }

            var fieldCount = reader.ReadInt32();
            var fields = new List<MiniDbFieldSchema>(Math.Max(0, fieldCount));

            for (var i = 0; i < fieldCount; i++)
            {
                var nameLen = reader.ReadInt16();
                var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLen));
                var typeCode = reader.ReadByte();
                _ = reader.ReadInt32(); // offset
                var size = reader.ReadInt32();
                var maxLength = reader.ReadInt32();
                var isPrimaryKey = reader.ReadBoolean();
                var isNullable = reader.ReadBoolean();

                if (isPrimaryKey || string.Equals(name, "Id", StringComparison.Ordinal))
                {
                    continue;
                }

                fields.Add(new MiniDbFieldSchema
                {
                    Name = name,
                    Type = MapFieldType(typeCode),
                    Size = size,
                    Nullable = isNullable,
                    MaxLength = maxLength
                });
            }

            return new MiniDbTableSchema
            {
                Name = tableName,
                Fields = fields
            };
        }
        catch
        {
            return null;
        }
    }

    private static string MapFieldType(byte code)
    {
        return code switch
        {
            1 => "int",
            2 => "bool",
            3 => "decimal",
            4 => "datetime",
            5 => "string",
            6 => "enum",
            _ => "string"
        };
    }

    private static DriverFileMetadata ReadMetadata(string filePath)
    {
        using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != MagicNumber)
        {
            throw new InvalidDataException("Invalid MiniDb file format.");
        }

        var version = reader.ReadInt16();
        if (version < MinSupportedVersion || version > MaxSupportedVersion)
        {
            throw new InvalidDataException($"Unsupported MiniDb version: {version}.");
        }

        var tableCount = reader.ReadInt16();
        _ = reader.ReadInt64(); // Global write version
        reader.ReadBytes(HeaderReservedRemainingBytes);

        var pendingExtentMeta = new List<(string TableName, long ExtentDirectoryOffset, int ExtentCount)>();
        var metadata = new Dictionary<string, DriverTableMetadata>(StringComparer.Ordinal);

        for (var i = 0; i < tableCount; i++)
        {
            var nameBytes = reader.ReadBytes(64);
            var tableName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
            var recordCount = reader.ReadInt32();
            var recordSize = reader.ReadInt32();
            var dataStartOffset = reader.ReadInt64();
            var reservedRecordCount = reader.ReadInt32();
            var tableIndex = reader.ReadInt32();
            var extentDirectoryOffset = reader.ReadInt64();
            var extentCount = reader.ReadInt32();
            long schemaOffset = 0;
            int schemaLength = 0;
            if (version >= 2)
            {
                schemaOffset = reader.ReadInt64();
                schemaLength = reader.ReadInt32();
            }

            reader.ReadBytes(version >= 2 ? TableMetaReservedRemainingBytesV2 : TableMetaReservedRemainingBytesV1);

            metadata[tableName] = new DriverTableMetadata(
                tableName,
                recordCount,
                recordSize,
                dataStartOffset,
                reservedRecordCount > 0 ? reservedRecordCount : ExtentRecordGrowth,
                tableIndex,
                extentDirectoryOffset,
                extentCount > 0 ? extentCount : 1,
                [],
                schemaOffset,
                schemaLength);

            pendingExtentMeta.Add((tableName, extentDirectoryOffset, extentCount));
        }

        foreach (var (tableName, extentDirectoryOffset, extentCount) in pendingExtentMeta)
        {
            var current = metadata[tableName];
            var starts = extentCount > 1 && extentDirectoryOffset > 0
                ? ReadExtentStarts(reader, extentDirectoryOffset, extentCount)
                : [current.DataStartOffset];

            metadata[tableName] = current with { ExtentStarts = starts };
        }

        return new DriverFileMetadata(version, metadata);
    }

    private static IReadOnlyList<long> ReadExtentStarts(BinaryReader reader, long directoryOffset, int extentCount)
    {
        reader.BaseStream.Seek(directoryOffset, SeekOrigin.Begin);
        var persistedCount = reader.ReadInt32();
        if (persistedCount != extentCount)
        {
            throw new InvalidDataException($"Extent directory mismatch. metadata={extentCount}, directory={persistedCount}.");
        }

        var starts = new List<long>(extentCount);
        for (var i = 0; i < extentCount; i++)
        {
            starts.Add(reader.ReadInt64());
        }

        return starts;
    }
}

public sealed record DriverFileMetadata(
    short Version,
    IReadOnlyDictionary<string, DriverTableMetadata> Tables);

public sealed class MiniDbDriverSession(
    string filePath,
    short fileVersion,
    IReadOnlyDictionary<string, DriverTableMetadata> tableMetadata,
    IReadOnlyDictionary<string, MiniDbTableSchema> tableSchemas)
{
    private const int ExtentRecordGrowth = 1000;

    public string FilePath { get; } = filePath;
    public short FileVersion { get; } = fileVersion;
    public IReadOnlyDictionary<string, DriverTableMetadata> TableMetadata { get; } = tableMetadata;
    public IReadOnlyDictionary<string, MiniDbTableSchema> TableSchemas { get; } = tableSchemas;

    public IReadOnlyList<string> GetTableNames()
    {
        return TableMetadata.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<Dictionary<string, object?>> ReadTableRows(string tableName)
    {
        if (!TableMetadata.TryGetValue(tableName, out var metadata) || metadata.RecordCount <= 0)
        {
            return [];
        }

        if (!TableSchemas.TryGetValue(tableName, out var tableSchema) || tableSchema.Fields.Count == 0)
        {
            throw new InvalidDataException($"Table '{tableName}' has no valid schema. This database file is not supported by this client.");
        }

        var rows = new List<Dictionary<string, object?>>(metadata.RecordCount);
        using var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file);

        for (var id = 1; id <= metadata.RecordCount; id++)
        {
            var recordOffset = GetRecordOffset(metadata, id);
            file.Seek(recordOffset, SeekOrigin.Begin);
            var buffer = reader.ReadBytes(metadata.RecordSize);
            if (buffer.Length != metadata.RecordSize)
            {
                break;
            }

            var isDeleted = buffer[0] == 1;
            if (isDeleted)
            {
                continue;
            }

            var entityId = BitConverter.ToInt32(buffer, 1);
            var payload = buffer.AsSpan(5).ToArray();
            rows.Add(ReadStructuredRow(entityId, payload, tableSchema, tableName));
        }

        return rows;
    }

    public bool HasSchemaForTable(string tableName)
    {
        return TableSchemas.TryGetValue(tableName, out var schema) && schema.Fields.Count > 0;
    }

    public int GetSchemaFieldCount(string tableName)
    {
        return TableSchemas.TryGetValue(tableName, out var schema) ? schema.Fields.Count : 0;
    }

    private static Dictionary<string, object?> ReadStructuredRow(int entityId, byte[] payload, MiniDbTableSchema schema, string tableName)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = entityId
        };

        var offset = 0;
        foreach (var field in schema.Fields)
        {
            if (field.Size <= 0 || offset + field.Size > payload.Length)
            {
                break;
            }

            var fieldSpan = payload.AsSpan(offset, field.Size);
            row[field.Name] = ReadFieldValue(fieldSpan, field);
            offset += field.Size;
        }

        if (offset != payload.Length)
        {
            throw new InvalidDataException($"Table '{tableName}' row payload does not match embedded schema size. The database file may be corrupted.");
        }

        return row;
    }

    private static object? ReadFieldValue(ReadOnlySpan<byte> fieldSpan, MiniDbFieldSchema field)
    {
        var effectiveSpan = fieldSpan;
        if (field.Nullable)
        {
            if (fieldSpan.Length == 0)
            {
                return null;
            }

            if (fieldSpan[0] == 1)
            {
                return null;
            }

            effectiveSpan = fieldSpan[1..];
        }

        var type = field.Type.Trim();
        if (type.Equals("string", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeTextPayload(effectiveSpan.ToArray());
        }

        if (type.Equals("int", StringComparison.OrdinalIgnoreCase) && effectiveSpan.Length >= 4)
        {
            return BitConverter.ToInt32(effectiveSpan);
        }

        if (type.Equals("bool", StringComparison.OrdinalIgnoreCase) && effectiveSpan.Length >= 1)
        {
            return effectiveSpan[0] != 0;
        }

        if (type.Equals("datetime", StringComparison.OrdinalIgnoreCase) && effectiveSpan.Length >= 8)
        {
            var ticks = BitConverter.ToInt64(effectiveSpan);
            if (ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks)
            {
                return new DateTime(ticks, DateTimeKind.Utc);
            }

            return null;
        }

        if (type.Equals("decimal", StringComparison.OrdinalIgnoreCase) && effectiveSpan.Length >= 16)
        {
            Span<int> bits = stackalloc int[4];
            for (var i = 0; i < 4; i++)
            {
                bits[i] = BitConverter.ToInt32(effectiveSpan[(i * 4)..]);
            }

            return new decimal(bits);
        }

        if (type.Equals("enum", StringComparison.OrdinalIgnoreCase) && effectiveSpan.Length >= 4)
        {
            return BitConverter.ToInt32(effectiveSpan);
        }

        return Convert.ToHexString(effectiveSpan.ToArray());
    }

    private static string DecodeTextPayload(byte[] payload)
    {
        var text = Encoding.UTF8.GetString(payload).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    private static long GetRecordOffset(DriverTableMetadata metadata, int id)
    {
        var starts = metadata.ExtentStarts;
        if (starts.Count == 0)
        {
            throw new InvalidOperationException($"No extent metadata for table '{metadata.TableName}'.");
        }

        var capacities = GetExtentCapacities(metadata.ReservedRecordCount, starts.Count);
        var index = id - 1;
        var running = 0;

        for (var i = 0; i < starts.Count; i++)
        {
            var cap = capacities[i];
            if (index < running + cap)
            {
                var offsetInExtent = index - running;
                return starts[i] + ((long)offsetInExtent * metadata.RecordSize);
            }

            running += cap;
        }

        throw new InvalidOperationException($"Cannot map record Id={id} for table '{metadata.TableName}'.");
    }

    private static List<int> GetExtentCapacities(int reservedRecordCount, int extentCount)
    {
        if (extentCount <= 0)
        {
            return [Math.Max(1, reservedRecordCount)];
        }

        if (extentCount == 1)
        {
            return [Math.Max(1, reservedRecordCount)];
        }

        var firstCapacity = reservedRecordCount - ((extentCount - 1) * ExtentRecordGrowth);
        if (firstCapacity <= 0)
        {
            firstCapacity = ExtentRecordGrowth;
        }

        var capacities = new List<int>(extentCount) { firstCapacity };
        for (var i = 1; i < extentCount; i++)
        {
            capacities.Add(ExtentRecordGrowth);
        }

        return capacities;
    }
}

public sealed record DriverTableMetadata(
    string TableName,
    int RecordCount,
    int RecordSize,
    long DataStartOffset,
    int ReservedRecordCount,
    int TableIndex,
    long ExtentDirectoryOffset,
    int ExtentCount,
    IReadOnlyList<long> ExtentStarts,
    long SchemaOffset,
    int SchemaLength);

public sealed class MiniDbTableSchema
{
    public string Name { get; set; } = string.Empty;
    public List<MiniDbFieldSchema> Fields { get; set; } = [];
}

public sealed class MiniDbFieldSchema
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public int Size { get; set; }
    public bool Nullable { get; set; }
    public int MaxLength { get; set; }
}