using System.Buffers;
using System.Collections.Frozen;
using System.Security.Cryptography;
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
    public int ReservedRecordCount { get; set; }
    /// <summary>
    /// The index of this table in the file header metadata section.
    /// This ensures consistent offset calculations across reloads.
    /// </summary>
    public int TableIndex { get; set; }
    public long SchemaOffset { get; set; }
    public int SchemaLength { get; set; }
}

/// <summary>
/// Manage file I/O with fixed-length binary format
/// </summary>
internal class StorageManager
{
    private const int FILE_HEADER_SIZE = 256;
    private const int TABLE_META_SIZE = 128;
    private const int EXTENT_RECORD_GROWTH = 1000;
    private const string MAGIC_NUMBER = "MDB1";
    private const short CURRENT_VERSION = 2;
    private const short MIN_SUPPORTED_VERSION = 1;
    private const int GLOBAL_WRITE_VERSION_OFFSET = 8; // 4(Magic) + 2(Version) + 2(TableCount)
    private const int HEADER_RESERVED_BYTES = 248;
    private const int HEADER_RESERVED_REMAINING_BYTES = HEADER_RESERVED_BYTES - sizeof(long);
    private const int EXTENT_DIRECTORY_OFFSET_SIZE = sizeof(long);
    private const int EXTENT_COUNT_SIZE = sizeof(int);
    private const int SCHEMA_OFFSET_SIZE = sizeof(long);
    private const int SCHEMA_LENGTH_SIZE = sizeof(int);
    private const int TABLE_META_RESERVED_REMAINING_BYTES_V2 = 40
        - EXTENT_DIRECTORY_OFFSET_SIZE
        - EXTENT_COUNT_SIZE
        - SCHEMA_OFFSET_SIZE
        - SCHEMA_LENGTH_SIZE;
    private const int TABLE_META_RESERVED_REMAINING_BYTES_V1 = 40
        - EXTENT_DIRECTORY_OFFSET_SIZE
        - EXTENT_COUNT_SIZE;

    private readonly string _filePath;
    private readonly FileWriteQueue _writeQueue;
    private readonly Dictionary<string, TableMetadata> _tables = [];
    private readonly Dictionary<string, List<long>> _tableExtentStarts = [];
    private readonly Dictionary<string, long> _tableExtentDirectoryOffsets = [];
    private readonly Dictionary<string, PersistedTableSchema> _tableSchemas = [];
    private FrozenDictionary<Type, EntityMetadata> _entityMetadataCache = FrozenDictionary<Type, EntityMetadata>.Empty;
    private long _knownGlobalWriteVersion;

    public StorageManager(string filePath, FileWriteQueue writeQueue)
    {
        _filePath = filePath;
        _writeQueue = writeQueue;
    }

    public void Initialize(Dictionary<string, Type> tableTypes)
    {
        ExecuteWithFileLock(() =>
        {
            if (File.Exists(_filePath))
            {
                LoadDatabase();
            }
            else
            {
                CreateDatabase(tableTypes);
            }
        });
    }

    private void CreateDatabase(Dictionary<string, Type> tableTypes)
    {
        using var file = new FileStream(_filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(file);

        // Write file header
        Span<byte> magicBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(MAGIC_NUMBER, magicBytes);
        writer.Write(magicBytes);
        writer.Write(CURRENT_VERSION);
        writer.Write((short)tableTypes.Count);

        // Global write version (for lazy metadata refresh across processes)
        writer.Write(0L);

        Span<byte> reserved = stackalloc byte[HEADER_RESERVED_REMAINING_BYTES];
        reserved.Clear();
        writer.Write(reserved);

        _knownGlobalWriteVersion = 0;

        // Build metadata cache and persisted schema bytes
        var metadataBuilder = new Dictionary<Type, EntityMetadata>();
        var schemaBytesByTable = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        long schemaSectionSize = 0;
        foreach (var kvp in tableTypes)
        {
            var metadata = EntityMetadata.Create(kvp.Value);
            metadataBuilder[kvp.Value] = metadata;

            var persistedSchema = metadata.ToPersistedSchema();
            var schemaBytes = SerializeTableSchema(persistedSchema);
            schemaBytesByTable[kvp.Key] = schemaBytes;
            schemaSectionSize += schemaBytes.Length;
            _tableSchemas[kvp.Key] = persistedSchema;
        }

        long schemaCursor = FILE_HEADER_SIZE + (tableTypes.Count * TABLE_META_SIZE);
        long currentOffset = schemaCursor + schemaSectionSize;

        // Write table metadata
        int tableIndex = 0;
        foreach (var kvp in tableTypes)
        {
            var metadata = metadataBuilder[kvp.Value];
            var schemaBytes = schemaBytesByTable[kvp.Key];

            var tableMetadata = new TableMetadata
            {
                TableName = kvp.Key,
                RecordCount = 0,
                RecordSize = metadata.RecordSize,
                DataStartOffset = currentOffset,
                ReservedRecordCount = EXTENT_RECORD_GROWTH,
                TableIndex = tableIndex,
                SchemaOffset = schemaCursor,
                SchemaLength = schemaBytes.Length
            };
            _tables[kvp.Key] = tableMetadata;
            _tableExtentStarts[kvp.Key] = [currentOffset];
            _tableExtentDirectoryOffsets[kvp.Key] = 0;

            WriteTableMetadata(writer, tableMetadata);
            tableIndex++;
            schemaCursor += schemaBytes.Length;
            currentOffset += metadata.RecordSize * tableMetadata.ReservedRecordCount;
        }

        // Write schema section immediately after table metadata section
        foreach (var kvp in tableTypes)
        {
            var table = _tables[kvp.Key];
            file.Seek(table.SchemaOffset, SeekOrigin.Begin);
            writer.Write(schemaBytesByTable[kvp.Key]);
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
        writer.Write(metadata.ReservedRecordCount);
        writer.Write(metadata.TableIndex);

        var extentDirectoryOffset = _tableExtentDirectoryOffsets.GetValueOrDefault(metadata.TableName, 0L);
        var extentCount = _tableExtentStarts.TryGetValue(metadata.TableName, out var starts)
            ? starts.Count
            : 1;
        writer.Write(extentDirectoryOffset);
        writer.Write(extentCount);
        writer.Write(metadata.SchemaOffset);
        writer.Write(metadata.SchemaLength);

        Span<byte> reserved = stackalloc byte[TABLE_META_RESERVED_REMAINING_BYTES_V2];
        reserved.Clear();
        writer.Write(reserved);
    }

    private void LoadDatabase()
    {
        _tables.Clear();
        _tableExtentStarts.Clear();
        _tableExtentDirectoryOffsets.Clear();
        _tableSchemas.Clear();

        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file);

        // Read file header
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != MAGIC_NUMBER)
            throw new InvalidDataException("Invalid database file format");

        var version = reader.ReadInt16();
        if (version < MIN_SUPPORTED_VERSION || version > CURRENT_VERSION)
            throw new InvalidDataException($"Unsupported database version: {version}. Supported versions: {MIN_SUPPORTED_VERSION}..{CURRENT_VERSION}.");

        var tableCount = reader.ReadInt16();
        _knownGlobalWriteVersion = reader.ReadInt64();
        reader.ReadBytes(HEADER_RESERVED_REMAINING_BYTES); // Skip remaining reserved

        var pendingExtentMeta = new List<(string TableName, long DirectoryOffset, int ExtentCount)>();

        // Read table metadata
        for (int i = 0; i < tableCount; i++)
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

            reader.ReadBytes(version >= 2 ? TABLE_META_RESERVED_REMAINING_BYTES_V2 : TABLE_META_RESERVED_REMAINING_BYTES_V1); // Skip reserved

            _tables[tableName] = new TableMetadata
            {
                TableName = tableName,
                RecordCount = recordCount,
                RecordSize = recordSize,
                DataStartOffset = dataStartOffset,
                ReservedRecordCount = reservedRecordCount > 0 ? reservedRecordCount : EXTENT_RECORD_GROWTH,
                TableIndex = tableIndex,
                SchemaOffset = schemaOffset,
                SchemaLength = schemaLength
            };

            pendingExtentMeta.Add((tableName, extentDirectoryOffset, extentCount));
        }

        foreach (var (tableName, directoryOffset, extentCount) in pendingExtentMeta)
        {
            _tableExtentDirectoryOffsets[tableName] = directoryOffset;

            if (extentCount > 1 && directoryOffset > 0)
            {
                _tableExtentStarts[tableName] = ReadExtentStarts(reader, directoryOffset, extentCount);
            }
            else
            {
                _tableExtentStarts[tableName] = [_tables[tableName].DataStartOffset];
            }
        }

        if (version >= 2)
        {
            foreach (var table in _tables.Values)
            {
                if (table.SchemaOffset <= 0 || table.SchemaLength <= 0)
                {
                    continue;
                }

                file.Seek(table.SchemaOffset, SeekOrigin.Begin);
                var schemaBytes = reader.ReadBytes(table.SchemaLength);
                if (schemaBytes.Length != table.SchemaLength)
                {
                    throw new InvalidDataException($"Failed to read full schema bytes for table '{table.TableName}'.");
                }

                _tableSchemas[table.TableName] = DeserializeTableSchema(schemaBytes);
            }
        }
    }

    public async Task<List<T>> LoadTableAsync<T>(string tableName, CancellationToken cancellationToken = default) where T : class, IMicroEntity, new()
    {
        var result = new List<T>();
        if (!_tables.TryGetValue(tableName, out var tableMetadata))
            return result;

        if (tableMetadata.RecordCount == 0)
            return result;

        var entityMetadata = GetOrCreateEntityMetadata(typeof(T), tableName);
        if (tableMetadata.RecordSize != entityMetadata.RecordSize)
        {
            throw new InvalidDataException(
                $"Schema mismatch for table '{tableName}': file RecordSize={tableMetadata.RecordSize}, expected RecordSize={entityMetadata.RecordSize} for entity '{typeof(T).FullName}'.");
        }
        byte[]? rentedBuffer = null;

        try
        {
            await using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, useAsync: true);

            rentedBuffer = ArrayPool<byte>.Shared.Rent(tableMetadata.RecordSize);
            var buffer = rentedBuffer.AsMemory(0, tableMetadata.RecordSize);

            for (int id = 1; id <= tableMetadata.RecordCount; id++)
            {
                var recordOffset = GetRecordOffset(tableName, id);
                file.Seek(recordOffset, SeekOrigin.Begin);
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
        await ExecuteWithFileLockAsync(async () =>
        {
            // Refresh metadata only when another process has written new changes.
            EnsureMetadataUpToDateUnderFileLock();

            var tableMetadata = _tables[tableName];
            var entityMetadata = GetOrCreateEntityMetadata(typeof(T), tableName);
            if (tableMetadata.RecordSize != entityMetadata.RecordSize)
            {
                throw new InvalidDataException(
                    $"Schema mismatch for table '{tableName}': file RecordSize={tableMetadata.RecordSize}, expected RecordSize={entityMetadata.RecordSize} for entity '{typeof(T).FullName}'.");
            }

            await using var file = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
                bufferSize: 4096, useAsync: true);

            // Handle added records
            foreach (var entity in added)
            {
                await EnsureCapacityForAppendAsync(tableName, file, cancellationToken);

                // Assign Id at write time to ensure uniqueness across multiple processes.
                entity.Id = tableMetadata.RecordCount + 1;
                var writeOffset = GetRecordOffset(tableName, entity.Id);

                var buffer = SerializeRecord(entity, entityMetadata);
                file.Seek(writeOffset, SeekOrigin.Begin);
                await file.WriteAsync(buffer, cancellationToken);
                tableMetadata.RecordCount++;
            }

            // Handle modified records
            foreach (var entity in modified)
            {
                var id = entity.Id;
                if (id <= 0 || id > tableMetadata.RecordCount)
                {
                    throw new InvalidOperationException(
                        $"Cannot modify entity with Id {id} in table '{tableName}': valid Id range is 1..{tableMetadata.RecordCount}.");
                }

                var buffer = SerializeRecord(entity, entityMetadata);
                long offset = GetRecordOffset(tableName, id);
                file.Seek(offset, SeekOrigin.Begin);
                await file.WriteAsync(buffer, cancellationToken);
            }

            // Handle deleted records (soft delete)
            foreach (var entity in deleted)
            {
                var id = entity.Id;
                if (id <= 0 || id > tableMetadata.RecordCount)
                {
                    throw new InvalidOperationException(
                        $"Cannot delete entity with Id {id} in table '{tableName}': valid Id range is 1..{tableMetadata.RecordCount}.");
                }

                long offset = GetRecordOffset(tableName, id);
                file.Seek(offset, SeekOrigin.Begin);
                await file.WriteAsync(new byte[] { 1 }, cancellationToken); // Set IsDeleted flag
            }

            // Ensure data is written to disk
            await file.FlushAsync(cancellationToken);

            // Update table metadata in the same file stream
            await UpdateTableMetadataAsync(tableName, file, cancellationToken);
            _knownGlobalWriteVersion++;
            await UpdateGlobalWriteVersionAsync(file, _knownGlobalWriteVersion, cancellationToken);
            await file.FlushAsync(cancellationToken);
        }, cancellationToken);
    }

    private static List<long> ReadExtentStarts(BinaryReader reader, long directoryOffset, int extentCount)
    {
        reader.BaseStream.Seek(directoryOffset, SeekOrigin.Begin);
        var persistedCount = reader.ReadInt32();
        if (persistedCount != extentCount)
        {
            throw new InvalidDataException($"Extent directory count mismatch. Metadata={extentCount}, directory={persistedCount}.");
        }

        var starts = new List<long>(extentCount);
        for (int i = 0; i < extentCount; i++)
        {
            starts.Add(reader.ReadInt64());
        }

        return starts;
    }

    private static byte[] SerializeTableSchema(PersistedTableSchema schema)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(schema.Version);
        writer.Write(schema.Fields.Count);
        foreach (var field in schema.Fields)
        {
            var nameBytes = Encoding.UTF8.GetBytes(field.Name);
            writer.Write((short)nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write((byte)field.Type);
            writer.Write(field.Offset);
            writer.Write(field.Size);
            writer.Write(field.MaxLength);
            writer.Write(field.IsPrimaryKey);
            writer.Write(field.IsNullable);
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static PersistedTableSchema DeserializeTableSchema(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var schema = new PersistedTableSchema
        {
            Version = reader.ReadInt32()
        };

        var fieldCount = reader.ReadInt32();
        for (var i = 0; i < fieldCount; i++)
        {
            var nameLength = reader.ReadInt16();
            var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
            var type = (SchemaFieldType)reader.ReadByte();
            var offset = reader.ReadInt32();
            var size = reader.ReadInt32();
            var maxLength = reader.ReadInt32();
            var isPrimaryKey = reader.ReadBoolean();
            var isNullable = reader.ReadBoolean();

            schema.Fields.Add(new PersistedFieldSchema
            {
                Name = name,
                Type = type,
                Offset = offset,
                Size = size,
                MaxLength = maxLength,
                IsPrimaryKey = isPrimaryKey,
                IsNullable = isNullable
            });
        }

        return schema;
    }

    private long GetRecordOffset(string tableName, int id)
    {
        var table = _tables[tableName];
        var starts = _tableExtentStarts.GetValueOrDefault(tableName);
        if (starts is null || starts.Count == 0)
        {
            throw new InvalidOperationException($"No extent metadata found for table '{tableName}'.");
        }

        var capacities = GetExtentCapacities(table.ReservedRecordCount, starts.Count);
        var index = id - 1;
        var running = 0;

        for (int i = 0; i < starts.Count; i++)
        {
            var cap = capacities[i];
            if (index < running + cap)
            {
                var offsetInExtent = index - running;
                return starts[i] + ((long)offsetInExtent * table.RecordSize);
            }

            running += cap;
        }

        throw new InvalidOperationException(
            $"Cannot map Id {id} to extent for table '{tableName}'. RecordCount={table.RecordCount}, Reserved={table.ReservedRecordCount}.");
    }

    private static List<int> GetExtentCapacities(int reservedRecordCount, int extentCount)
    {
        if (extentCount <= 0)
        {
            throw new InvalidOperationException("Extent count must be positive.");
        }

        if (extentCount == 1)
        {
            return [Math.Max(1, reservedRecordCount)];
        }

        var firstCapacity = reservedRecordCount - ((extentCount - 1) * EXTENT_RECORD_GROWTH);
        if (firstCapacity <= 0)
        {
            firstCapacity = EXTENT_RECORD_GROWTH;
        }

        var capacities = new List<int>(extentCount) { firstCapacity };
        for (int i = 1; i < extentCount; i++)
        {
            capacities.Add(EXTENT_RECORD_GROWTH);
        }

        return capacities;
    }

    private void EnsureMetadataUpToDateUnderFileLock()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var currentVersion = ReadGlobalWriteVersion();
        if (currentVersion == _knownGlobalWriteVersion)
        {
            return;
        }

        RefreshTableMetadataFromFile();
    }

    private long ReadGlobalWriteVersion()
    {
        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != MAGIC_NUMBER)
            throw new InvalidDataException("Invalid database file format");

        var version = reader.ReadInt16();
        if (version < MIN_SUPPORTED_VERSION || version > CURRENT_VERSION)
            throw new InvalidDataException($"Unsupported database version: {version}. Supported versions: {MIN_SUPPORTED_VERSION}..{CURRENT_VERSION}.");

        _ = reader.ReadInt16(); // tableCount
        return reader.ReadInt64();
    }

    private void RefreshTableMetadataFromFile()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != MAGIC_NUMBER)
            throw new InvalidDataException("Invalid database file format");

        var version = reader.ReadInt16();
        if (version < MIN_SUPPORTED_VERSION || version > CURRENT_VERSION)
            throw new InvalidDataException($"Unsupported database version: {version}. Supported versions: {MIN_SUPPORTED_VERSION}..{CURRENT_VERSION}.");

        var tableCount = reader.ReadInt16();
        _knownGlobalWriteVersion = reader.ReadInt64();
        reader.ReadBytes(HEADER_RESERVED_REMAINING_BYTES); // Skip remaining reserved

        var latest = new Dictionary<string, TableMetadata>(tableCount, StringComparer.Ordinal);
        var pendingExtentMeta = new List<(string TableName, long DirectoryOffset, int ExtentCount)>();
        for (int i = 0; i < tableCount; i++)
        {
            var nameBytes = reader.ReadBytes(64);
            var name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
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

            reader.ReadBytes(version >= 2 ? TABLE_META_RESERVED_REMAINING_BYTES_V2 : TABLE_META_RESERVED_REMAINING_BYTES_V1); // Skip reserved

            latest[name] = new TableMetadata
            {
                TableName = name,
                RecordCount = recordCount,
                RecordSize = recordSize,
                DataStartOffset = dataStartOffset,
                ReservedRecordCount = reservedRecordCount > 0 ? reservedRecordCount : EXTENT_RECORD_GROWTH,
                TableIndex = tableIndex,
                SchemaOffset = schemaOffset,
                SchemaLength = schemaLength
            };

            pendingExtentMeta.Add((name, extentDirectoryOffset, extentCount));
        }

        _tableExtentDirectoryOffsets.Clear();
        _tableExtentStarts.Clear();
        foreach (var (tableName, directoryOffset, extentCount) in pendingExtentMeta)
        {
            _tableExtentDirectoryOffsets[tableName] = directoryOffset;
            if (extentCount > 1 && directoryOffset > 0)
            {
                _tableExtentStarts[tableName] = ReadExtentStarts(reader, directoryOffset, extentCount);
            }
            else
            {
                _tableExtentStarts[tableName] = [latest[tableName].DataStartOffset];
            }
        }

        _tableSchemas.Clear();
        if (version >= 2)
        {
            foreach (var table in latest.Values)
            {
                if (table.SchemaOffset <= 0 || table.SchemaLength <= 0)
                {
                    continue;
                }

                file.Seek(table.SchemaOffset, SeekOrigin.Begin);
                var schemaBytes = reader.ReadBytes(table.SchemaLength);
                if (schemaBytes.Length != table.SchemaLength)
                {
                    throw new InvalidDataException($"Failed to read full schema bytes for table '{table.TableName}'.");
                }

                _tableSchemas[table.TableName] = DeserializeTableSchema(schemaBytes);
            }
        }

        foreach (var (name, metadata) in latest)
        {
            _tables[name] = metadata;
        }
    }

    private void ExecuteWithFileLock(Action action)
    {
        using var semaphore = new Semaphore(1, 1, GetFileLockName(_filePath));
        bool lockTaken = false;
        try
        {
            lockTaken = WaitForFileLock(semaphore, CancellationToken.None);
            action();
        }
        finally
        {
            if (lockTaken)
            {
                semaphore.Release();
            }
        }
    }

    private async Task ExecuteWithFileLockAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        using var semaphore = new Semaphore(1, 1, GetFileLockName(_filePath));
        bool lockTaken = false;
        try
        {
            lockTaken = WaitForFileLock(semaphore, cancellationToken);
            await action().ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
            {
                semaphore.Release();
            }
        }
    }

    private static bool WaitForFileLock(Semaphore semaphore, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (semaphore.WaitOne(TimeSpan.FromMilliseconds(100)))
                {
                    return true;
                }
            }
            catch (SemaphoreFullException)
            {
                // Defensive handling: recreate acquisition loop on unexpected semaphore state.
            }
        }
    }

    private static string GetFileLockName(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        var token = Convert.ToHexString(hash[..16]);
        return $"PerigonMiniDbLock_{token}";
    }

    private async Task EnsureCapacityForAppendAsync(string tableName, FileStream file, CancellationToken cancellationToken)
    {
        var tableMetadata = _tables[tableName];
        if (tableMetadata.RecordCount < tableMetadata.ReservedRecordCount)
        {
            return;
        }

        // Multi-extent growth: append a new extent at EOF, do not move other tables.
        int growByRecords = EXTENT_RECORD_GROWTH;
        long growByBytes = (long)growByRecords * tableMetadata.RecordSize;

        long newExtentStart = file.Length;
        file.SetLength(newExtentStart + growByBytes);

        if (!_tableExtentStarts.TryGetValue(tableName, out var starts))
        {
            starts = [tableMetadata.DataStartOffset];
            _tableExtentStarts[tableName] = starts;
        }

        starts.Add(newExtentStart);

        tableMetadata.ReservedRecordCount += growByRecords;
        PersistExtentDirectory(tableName, file);

        await UpdateTableMetadataAsync(tableName, file, cancellationToken);
    }

    private void PersistExtentDirectory(string tableName, FileStream file)
    {
        if (!_tableExtentStarts.TryGetValue(tableName, out var starts) || starts.Count <= 1)
        {
            _tableExtentDirectoryOffsets[tableName] = 0;
            return;
        }

        long directoryOffset = file.Length;
        file.Seek(directoryOffset, SeekOrigin.Begin);
        using var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
        writer.Write(starts.Count);
        foreach (var start in starts)
        {
            writer.Write(start);
        }

        _tableExtentDirectoryOffsets[tableName] = directoryOffset;
    }

    private static async Task UpdateGlobalWriteVersionAsync(FileStream file, long version, CancellationToken cancellationToken)
    {
        file.Seek(GLOBAL_WRITE_VERSION_OFFSET, SeekOrigin.Begin);
        var buffer = BitConverter.GetBytes(version);
        await file.WriteAsync(buffer, cancellationToken);
    }

    private async Task UpdateTableMetadataAsync(string tableName, FileStream file, CancellationToken cancellationToken = default)
    {
        var tableMetadata = _tables[tableName];
        long metadataOffset = FILE_HEADER_SIZE + (tableMetadata.TableIndex * TABLE_META_SIZE);

        file.Seek(metadataOffset, SeekOrigin.Begin);

        await using var memoryStream = new MemoryStream();
        await using var writer = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true);
        WriteTableMetadata(writer, tableMetadata);

        memoryStream.Seek(0, SeekOrigin.Begin);
        await memoryStream.CopyToAsync(file, cancellationToken);
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
        else if (underlyingType.IsEnum)
        {
            if (dataSpan.Length < 4)
            {
                throw new InvalidOperationException($"Enum field storage size is invalid. Expected at least 4 bytes, got {dataSpan.Length} bytes.");
            }

            var enumValue64 = Convert.ToInt64(value);
            if (enumValue64 < int.MinValue || enumValue64 > int.MaxValue)
            {
                throw new InvalidOperationException($"Enum value '{enumValue64}' is outside Int32 range and is not supported by MiniDb.");
            }

            var enumValue = (int)enumValue64;
            BitConverter.TryWriteBytes(dataSpan, enumValue);
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
        else if (underlyingType.IsEnum)
        {
            if (dataSpan.Length < 4)
            {
                throw new InvalidOperationException($"Enum field storage size is invalid. Expected at least 4 bytes, got {dataSpan.Length} bytes.");
            }

            // Read int value and convert to enum
            int intValue = BitConverter.ToInt32(dataSpan);
            return Enum.ToObject(underlyingType, intValue);
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
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                throw new InvalidDataException(
                    $"Invalid DateTime ticks value '{ticks}' while reading field of type '{type.FullName}'. The database file may be corrupted or the entity schema may have changed.");
            }

            return new DateTime(ticks, DateTimeKind.Utc);
        }

        return null;
    }

    private EntityMetadata GetOrCreateEntityMetadata(Type type, string? tableName = null)
    {
        if (_entityMetadataCache.TryGetValue(type, out var metadata))
        {
            return metadata;
        }

        if (!string.IsNullOrWhiteSpace(tableName)
            && _tableSchemas.TryGetValue(tableName, out var persistedSchema)
            && persistedSchema.Version == EntityMetadata.CurrentSchemaVersion)
        {
            metadata = EntityMetadata.CreateFromSchema(type, persistedSchema);
        }
        else
        {
            metadata = EntityMetadata.Create(type);
        }

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
