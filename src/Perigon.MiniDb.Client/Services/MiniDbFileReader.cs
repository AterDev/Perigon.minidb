using System.Text;
using Perigon.MiniDb;
using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Services;

/// <summary>
/// Reads raw table data from MiniDB (.mds) binary files.
/// Does NOT manage pagination or filter state — that belongs in the ViewModel.
/// </summary>
public static class MiniDbFileReader
{
    private const int FileHeaderSize = 256;
    private const int TableNameBytes = 64;
    private const int TableMetaReservedBytes = 16; // remaining after extent + field meta info
    private const int ExtentRecordGrowth = 1000;
    private const int FieldMetaEntrySize = 80;
    private const int FieldNameBytes = 64;
    private const string MagicNumber = "MDB1";

    private sealed class TableMetadataLite
    {
        public string TableName { get; init; } = string.Empty;
        public int RecordCount { get; init; }
        public int RecordSize { get; init; }
        public long DataStartOffset { get; init; }
        public int ReservedRecordCount { get; init; }
        public long ExtentDirectoryOffset { get; init; }
        public int ExtentCount { get; init; }
        public long FieldMetadataOffset { get; init; }
        public int FieldCount { get; init; }
    }

    private sealed class FieldMetadataLite
    {
        public string Name { get; init; } = string.Empty;
        public FieldTypeCode TypeCode { get; init; }
        public int Size { get; init; }
        public bool IsNullable { get; init; }
    }

    /// <summary>
    /// Read all table names from a .mds file.
    /// </summary>
    public static List<string> GetTableNames(string filePath, out string? error)
    {
        error = null;

        try
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: false);
            return ReadAllTableMetadata(reader)
                .OrderBy(m => m.TableName)
                .Select(m => m.TableName)
                .ToList();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return [];
        }
    }

    /// <summary>
    /// Validate that all tables in the file have field metadata.
    /// Returns null if valid, or an error message if any table lacks metadata.
    /// </summary>
    public static string? ValidateFieldMetadata(string filePath)
    {
        try
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: false);
            var allMeta = ReadAllTableMetadata(reader);

            var tablesWithoutMetadata = allMeta
                .Where(m => m.FieldMetadataOffset <= 0 || m.FieldCount <= 0)
                .Select(m => m.TableName)
                .ToList();

            if (tablesWithoutMetadata.Count > 0)
            {
                return string.Join(", ", tablesWithoutMetadata);
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Load decoded table data with field names and typed values.
    /// Falls back to raw hex/UTF-8 display for old files without field metadata.
    /// </summary>
    public static TableData LoadTableData(string filePath, string tableName, out string? error)
    {
        error = null;

        try
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: false);

            var allMeta = ReadAllTableMetadata(reader);
            var metadata = allMeta.FirstOrDefault(m => string.Equals(m.TableName, tableName, StringComparison.Ordinal));
            if (metadata is null)
            {
                error = $"Table not found: {tableName}";
                return new TableData();
            }

            var fields = ReadFieldMetadata(reader, metadata);

            if (fields.Count == 0)
            {
                // Old file without field metadata — show raw data
                return ReadAsRawData(reader, metadata, fallbackReason: "NoFieldMetadata");
            }

            // Validate field sizes match record layout before attempting decoded read
            var expectedPayloadSize = fields.Sum(f => f.Size);
            var actualPayloadSize = metadata.RecordSize - 5; // subtract IsDeleted(1) + Id(4)
            if (expectedPayloadSize != actualPayloadSize)
            {
                // Field metadata doesn't match record size — fall back to raw display
                return ReadAsRawData(reader, metadata, fallbackReason: "FieldSizeMismatch");
            }

            return ReadAsDecodedData(reader, metadata, fields);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return new TableData();
        }
    }

    private static List<TableMetadataLite> ReadAllTableMetadata(BinaryReader reader)
    {
        reader.BaseStream.Seek(0, SeekOrigin.Begin);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (!string.Equals(magic, MagicNumber, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid MiniDB file header.");
        }

        _ = reader.ReadInt16(); // version
        var tableCount = reader.ReadInt16();
        _ = reader.ReadInt64(); // global write version

        reader.BaseStream.Seek(FileHeaderSize, SeekOrigin.Begin);

        var result = new List<TableMetadataLite>(tableCount);

        for (var i = 0; i < tableCount; i++)
        {
            var nameBytes = reader.ReadBytes(TableNameBytes);
            var tableName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
            var recordCount = reader.ReadInt32();
            var recordSize = reader.ReadInt32();
            var dataStartOffset = reader.ReadInt64();
            var reservedRecordCount = reader.ReadInt32();
            _ = reader.ReadInt32(); // tableIndex
            var extentDirectoryOffset = reader.ReadInt64();
            var extentCount = reader.ReadInt32();
            var fieldMetadataOffset = reader.ReadInt64();
            var fieldCount = reader.ReadInt32();
            reader.ReadBytes(TableMetaReservedBytes); // remaining reserved

            result.Add(new TableMetadataLite
            {
                TableName = tableName,
                RecordCount = recordCount,
                RecordSize = recordSize,
                DataStartOffset = dataStartOffset,
                ReservedRecordCount = reservedRecordCount,
                ExtentDirectoryOffset = extentDirectoryOffset,
                ExtentCount = extentCount <= 0 ? 1 : extentCount,
                FieldMetadataOffset = fieldMetadataOffset,
                FieldCount = fieldCount
            });
        }

        return result;
    }

    private static List<FieldMetadataLite> ReadFieldMetadata(BinaryReader reader, TableMetadataLite metadata)
    {
        if (metadata.FieldMetadataOffset <= 0 || metadata.FieldCount <= 0)
        {
            return [];
        }

        // Validate the offset is within file bounds
        var requiredBytes = (long)metadata.FieldCount * FieldMetaEntrySize;
        if (metadata.FieldMetadataOffset + requiredBytes > reader.BaseStream.Length)
        {
            return [];
        }

        reader.BaseStream.Seek(metadata.FieldMetadataOffset, SeekOrigin.Begin);
        var fields = new List<FieldMetadataLite>(metadata.FieldCount);

        for (var i = 0; i < metadata.FieldCount; i++)
        {
            var nameBytes = reader.ReadBytes(FieldNameBytes);
            var name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
            var typeCode = (FieldTypeCode)reader.ReadInt32();
            var size = reader.ReadInt32();
            var isNullable = reader.ReadByte() == 1;
            reader.ReadBytes(7); // reserved

            fields.Add(new FieldMetadataLite
            {
                Name = name,
                TypeCode = typeCode,
                Size = size,
                IsNullable = isNullable
            });
        }

        return fields;
    }

    private static TableData ReadAsRawData(BinaryReader reader, TableMetadataLite metadata, string? fallbackReason = null)
    {
        if (metadata.RecordSize < 5 || metadata.RecordCount <= 0)
        {
            return new TableData
            {
                FieldNames = ["Id", "PayloadHex", "PayloadUtf8Preview"],
                Records = [],
                FallbackReason = fallbackReason
            };
        }

        var extentStarts = ReadExtentStarts(reader, metadata);
        var extentCaps = GetExtentCapacities(metadata.ReservedRecordCount, extentStarts.Count);

        var records = new List<DynamicRecord>(metadata.RecordCount);
        var buffer = new byte[metadata.RecordSize];

        for (var id = 1; id <= metadata.RecordCount; id++)
        {
            var offset = GetRecordOffset(id, metadata.RecordSize, extentStarts, extentCaps);
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            var read = reader.BaseStream.Read(buffer, 0, buffer.Length);
            if (read != buffer.Length) break;

            if (buffer[0] == 1) continue; // IsDeleted

            var recordId = BitConverter.ToInt32(buffer, 1);
            var payload = buffer.AsSpan(5).ToArray();

            records.Add(new DynamicRecord(new Dictionary<string, string>
            {
                ["Id"] = recordId.ToString(),
                ["PayloadHex"] = BuildHexPreview(payload),
                ["PayloadUtf8Preview"] = BuildUtf8Preview(payload)
            }));
        }

        return new TableData
        {
            FieldNames = ["Id", "PayloadHex", "PayloadUtf8Preview"],
            Records = records,
            FallbackReason = fallbackReason
        };
    }

    private static TableData ReadAsDecodedData(BinaryReader reader, TableMetadataLite metadata, List<FieldMetadataLite> fields)
    {
        if (metadata.RecordSize < 5 || metadata.RecordCount <= 0)
        {
            var emptyFieldNames = new List<string>(fields.Count + 1) { "Id" };
            emptyFieldNames.AddRange(fields.Select(f => f.Name));
            return new TableData { FieldNames = emptyFieldNames, Records = [] };
        }

        var extentStarts = ReadExtentStarts(reader, metadata);
        var extentCaps = GetExtentCapacities(metadata.ReservedRecordCount, extentStarts.Count);

        var fieldNames = new List<string>(fields.Count + 1) { "Id" };
        fieldNames.AddRange(fields.Select(f => f.Name));

        var records = new List<DynamicRecord>(metadata.RecordCount);
        var buffer = new byte[metadata.RecordSize];

        for (var id = 1; id <= metadata.RecordCount; id++)
        {
            var offset = GetRecordOffset(id, metadata.RecordSize, extentStarts, extentCaps);
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            var read = reader.BaseStream.Read(buffer, 0, buffer.Length);
            if (read != buffer.Length) break;

            if (buffer[0] == 1) continue; // IsDeleted

            var recordId = BitConverter.ToInt32(buffer, 1);
            var values = new Dictionary<string, string>(fields.Count + 1)
            {
                ["Id"] = recordId.ToString()
            };

            var pos = 5; // skip IsDeleted(1) + Id(4)
            foreach (var field in fields)
            {
                var span = buffer.AsSpan(pos, field.Size);
                values[field.Name] = DecodeFieldValue(span, field);
                pos += field.Size;
            }

            records.Add(new DynamicRecord(values));
        }

        return new TableData
        {
            FieldNames = fieldNames,
            Records = records
        };
    }

    private static string DecodeFieldValue(ReadOnlySpan<byte> buffer, FieldMetadataLite field)
    {
        var offset = 0;
        if (field.IsNullable)
        {
            if (buffer[0] == 1) return string.Empty; // null
            offset = 1;
        }

        var data = buffer[offset..field.Size];

        return field.TypeCode switch
        {
            FieldTypeCode.Int32 => BitConverter.ToInt32(data).ToString(),
            FieldTypeCode.Boolean => (data[0] != 0).ToString(),
            FieldTypeCode.Decimal => DecodeDecimal(data).ToString(),
            FieldTypeCode.DateTime => new DateTime(BitConverter.ToInt64(data), DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss"),
            FieldTypeCode.String => DecodeString(data),
            FieldTypeCode.Enum => BitConverter.ToInt32(data).ToString(),
            _ => BuildHexPreview(data.ToArray())
        };
    }

    private static decimal DecodeDecimal(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16) return 0m;
        Span<int> bits = stackalloc int[4];
        for (var i = 0; i < 4; i++)
            bits[i] = BitConverter.ToInt32(data[(i * 4)..]);
        return new decimal(bits);
    }

    private static string DecodeString(ReadOnlySpan<byte> data)
    {
        var length = data.IndexOf((byte)0);
        if (length < 0) length = data.Length;
        return Encoding.UTF8.GetString(data[..length]);
    }

    private static List<long> ReadExtentStarts(BinaryReader reader, TableMetadataLite metadata)
    {
        if (metadata.ExtentCount <= 1 || metadata.ExtentDirectoryOffset <= 0)
        {
            return [metadata.DataStartOffset];
        }

        // Validate directory offset is within file bounds
        if (metadata.ExtentDirectoryOffset >= reader.BaseStream.Length)
        {
            return [metadata.DataStartOffset];
        }

        reader.BaseStream.Seek(metadata.ExtentDirectoryOffset, SeekOrigin.Begin);
        var count = reader.ReadInt32();
        if (count <= 0)
        {
            return [metadata.DataStartOffset];
        }

        var starts = new List<long>(count);
        for (var i = 0; i < count; i++)
        {
            starts.Add(reader.ReadInt64());
        }

        return starts;
    }

    private static List<int> GetExtentCapacities(int reservedRecordCount, int extentCount)
    {
        if (extentCount <= 1)
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

    private static long GetRecordOffset(int recordId, int recordSize, IReadOnlyList<long> extentStarts, IReadOnlyList<int> extentCaps)
    {
        var index = recordId - 1;
        var running = 0;

        for (var i = 0; i < extentStarts.Count; i++)
        {
            var cap = extentCaps[i];
            if (index < running + cap)
            {
                var offsetInExtent = index - running;
                return extentStarts[i] + ((long)offsetInExtent * recordSize);
            }

            running += cap;
        }

        throw new InvalidOperationException($"Cannot map record Id {recordId} to extent.");
    }

    private static string BuildHexPreview(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        const int maxBytes = 64;
        var slice = payload.Take(maxBytes).ToArray();
        var hex = Convert.ToHexString(slice);
        return payload.Length > maxBytes ? $"{hex}..." : hex;
    }

    private static string BuildUtf8Preview(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        var text = Encoding.UTF8.GetString(payload).TrimEnd('\0');
        text = new string(text.Where(ch => !char.IsControl(ch) || ch == '\t' || ch == '\n' || ch == '\r').ToArray());

        const int maxChars = 80;
        return text.Length > maxChars ? $"{text[..maxChars]}..." : text;
    }
}
