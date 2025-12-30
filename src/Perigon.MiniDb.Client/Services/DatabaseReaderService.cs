using System.Buffers;
using System.IO;
using System.Text;

namespace Perigon.MiniDb.Client.Services;

/// <summary>
/// Service to read database metadata without requiring entity type definitions
/// </summary>
public class DatabaseReaderService
{
    private const int FILE_HEADER_SIZE = 256;
    private const int TABLE_META_SIZE = 128;
    private const string MAGIC_NUMBER = "MDB1";

    public class TableMetadata
    {
        public string TableName { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public int RecordSize { get; set; }
        public long DataStartOffset { get; set; }
    }

    public class FieldInfo
    {
        public string Name { get; set; } = string.Empty;
        public Type Type { get; set; } = typeof(object);
        public int Size { get; set; }
        public int Offset { get; set; }
    }

    /// <summary>
    /// Read all table names from a database file
    /// </summary>
    public static List<string> ReadTableNames(string filePath)
    {
        var tables = new List<string>();

        if (!File.Exists(filePath))
            return tables;

        try
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(file);

            // Read file header
            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != MAGIC_NUMBER)
                throw new InvalidDataException("Invalid database file format");

            var version = reader.ReadInt16();
            var tableCount = reader.ReadInt16();
            reader.ReadBytes(248); // Skip reserved

            // Read table metadata
            for (int i = 0; i < tableCount; i++)
            {
                var nameBytes = reader.ReadBytes(64);
                var tableName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
                reader.ReadBytes(64); // Skip the rest of metadata

                if (!string.IsNullOrWhiteSpace(tableName))
                    tables.Add(tableName);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading table names: {ex.Message}");
        }

        return tables;
    }

    /// <summary>
    /// Read table metadata from a database file
    /// </summary>
    public static TableMetadata? ReadTableMetadata(string filePath, string tableName)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(file);

            // Read file header
            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != MAGIC_NUMBER)
                return null;

            var version = reader.ReadInt16();
            var tableCount = reader.ReadInt16();
            reader.ReadBytes(248); // Skip reserved

            // Read table metadata
            for (int i = 0; i < tableCount; i++)
            {
                var nameBytes = reader.ReadBytes(64);
                var currentTableName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
                var recordCount = reader.ReadInt32();
                var recordSize = reader.ReadInt32();
                var dataStartOffset = reader.ReadInt64();
                reader.ReadBytes(48); // Skip reserved

                if (currentTableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                {
                    return new TableMetadata
                    {
                        TableName = currentTableName,
                        RecordCount = recordCount,
                        RecordSize = recordSize,
                        DataStartOffset = dataStartOffset
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading table metadata: {ex.Message}");
        }

        return null;
    }
}
