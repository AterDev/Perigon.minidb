using System.Collections;
using System.Reflection;
using System.Text;

namespace Perigon.MiniDb.Client.Services;

public class TableDataStateService
{
    private const int FileHeaderSize = 256;
    private const int TableMetaSize = 128;
    private const int TableNameBytes = 64;
    private const int ExtentRecordGrowth = 1000;
    private const string MagicNumber = "MDB1";

    private List<PropertyInfo> _entityProperties = [];
    private List<object> _allEntities = [];
    private List<object> _filteredEntities = [];

    public sealed class RawTableRecord
    {
        public int Id { get; init; }
        public bool IsDeleted { get; init; }
        public string PayloadHex { get; init; } = string.Empty;
        public string PayloadUtf8Preview { get; init; } = string.Empty;
    }

    private sealed class TableMetadataLite
    {
        public string TableName { get; init; } = string.Empty;
        public int RecordCount { get; init; }
        public int RecordSize { get; init; }
        public long DataStartOffset { get; init; }
        public int ReservedRecordCount { get; init; }
        public long ExtentDirectoryOffset { get; init; }
        public int ExtentCount { get; init; }
    }

    public Type? CurrentEntityType { get; private set; }
    public string? SelectedFilterField { get; private set; }
    public string FilterValue { get; private set; } = string.Empty;
    public int PageSize { get; private set; } = 25;
    public int PageIndex { get; private set; } = 1;

    public int RawCount => _allEntities.Count;
    public int TotalCount => _filteredEntities.Count;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize)));
    public bool HasData => TotalCount > 0;

    public IReadOnlyList<string> FilterFields => _entityProperties.Select(p => p.Name).ToList();

    public static List<string> GetTableNamesFromFile(string filePath, out string? error)
    {
        error = null;

        try
        {
            return ReadAllTableMetadata(filePath)
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

    public bool TryLoadRawTable(string filePath, string tableName, out string? error)
    {
        error = null;

        try
        {
            var metadata = ReadAllTableMetadata(filePath)
                .FirstOrDefault(m => string.Equals(m.TableName, tableName, StringComparison.Ordinal));

            if (metadata is null)
            {
                error = $"Table not found: {tableName}";
                return false;
            }

            var entities = ReadRawTableRecords(filePath, metadata)
                .Where(record => !record.IsDeleted)
                .Cast<object>()
                .ToList();

            CurrentEntityType = typeof(RawTableRecord);
            _entityProperties = CurrentEntityType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToList();

            _allEntities = entities;
            _filteredEntities = [.. _allEntities];
            SelectedFilterField = FilterFields.FirstOrDefault();
            FilterValue = string.Empty;
            PageIndex = 1;

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Reset()
    {
        _entityProperties = [];
        _allEntities = [];
        _filteredEntities = [];
        CurrentEntityType = null;
        SelectedFilterField = null;
        FilterValue = string.Empty;
        PageIndex = 1;
        PageSize = 25;
    }

    public void SetPageSize(int pageSize)
    {
        PageSize = pageSize <= 0 ? 25 : pageSize;
        PageIndex = 1;
        EnsurePageRange();
    }

    public void SetPageIndex(int pageIndex)
    {
        PageIndex = pageIndex;
        EnsurePageRange();
    }

    public void SetEntities(IEnumerable<object> entities)
    {
        _allEntities = entities.ToList();
        _filteredEntities = [.. _allEntities];
        PageIndex = 1;
        EnsurePageRange();
    }

    public void SetFilteredEntities(IEnumerable<object> entities)
    {
        _filteredEntities = entities.ToList();
        EnsurePageRange();
    }

    public void SetFilterField(string? fieldName)
    {
        SelectedFilterField = fieldName;
    }

    public void SetFilterValue(string value)
    {
        FilterValue = value;
    }

    public void ApplyFilter()
    {
        if (_allEntities.Count == 0 || string.IsNullOrWhiteSpace(SelectedFilterField) || string.IsNullOrWhiteSpace(FilterValue))
        {
            _filteredEntities = [.. _allEntities];
            PageIndex = 1;
            return;
        }

        var filterField = SelectedFilterField;
        var searchText = FilterValue.Trim();

        _filteredEntities = _allEntities
            .Where(entity => MatchFieldContains(entity, filterField!, searchText))
            .ToList();

        PageIndex = 1;
        EnsurePageRange();
    }

    public void ClearFilter()
    {
        FilterValue = string.Empty;
        _filteredEntities = [.. _allEntities];
        PageIndex = 1;
        EnsurePageRange();
    }

    public bool GoFirstPage()
    {
        if (PageIndex <= 1)
        {
            return false;
        }

        PageIndex = 1;
        return true;
    }

    public bool GoPreviousPage()
    {
        if (PageIndex <= 1)
        {
            return false;
        }

        PageIndex--;
        return true;
    }

    public bool GoNextPage()
    {
        if (PageIndex >= TotalPages)
        {
            return false;
        }

        PageIndex++;
        return true;
    }

    public bool GoLastPage()
    {
        if (PageIndex >= TotalPages)
        {
            return false;
        }

        PageIndex = TotalPages;
        return true;
    }

    public IReadOnlyList<object> GetCurrentPageItems()
    {
        EnsurePageRange();
        return _filteredEntities
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToList();
    }

    public bool CanFirstPage() => PageIndex > 1;
    public bool CanPreviousPage() => PageIndex > 1;
    public bool CanNextPage() => PageIndex < TotalPages;
    public bool CanLastPage() => PageIndex < TotalPages;

    private static List<TableMetadataLite> ReadAllTableMetadata(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Database file not found.", filePath);
        }

        using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: false);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (!string.Equals(magic, MagicNumber, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid MiniDB file header.");
        }

        _ = reader.ReadInt16(); // version
        var tableCount = reader.ReadInt16();
        _ = reader.ReadInt64(); // global write version

        file.Seek(FileHeaderSize, SeekOrigin.Begin);

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
            reader.ReadBytes(28); // reserved

            result.Add(new TableMetadataLite
            {
                TableName = tableName,
                RecordCount = recordCount,
                RecordSize = recordSize,
                DataStartOffset = dataStartOffset,
                ReservedRecordCount = reservedRecordCount,
                ExtentDirectoryOffset = extentDirectoryOffset,
                ExtentCount = extentCount <= 0 ? 1 : extentCount
            });
        }

        return result;
    }

    private static List<RawTableRecord> ReadRawTableRecords(string filePath, TableMetadataLite metadata)
    {
        if (metadata.RecordSize < 5)
        {
            throw new InvalidDataException($"Invalid record size for table '{metadata.TableName}'.");
        }

        using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: false);

        var extentStarts = ReadExtentStarts(reader, metadata);
        var extentCaps = GetExtentCapacities(metadata.ReservedRecordCount, extentStarts.Count);

        var result = new List<RawTableRecord>(metadata.RecordCount);
        var buffer = new byte[metadata.RecordSize];

        for (var id = 1; id <= metadata.RecordCount; id++)
        {
            var offset = GetRecordOffset(id, metadata.RecordSize, extentStarts, extentCaps);
            file.Seek(offset, SeekOrigin.Begin);

            var read = file.Read(buffer, 0, buffer.Length);
            if (read != buffer.Length)
            {
                throw new EndOfStreamException($"Unexpected EOF while reading table '{metadata.TableName}', record {id}.");
            }

            var isDeleted = buffer[0] == 1;
            var recordId = BitConverter.ToInt32(buffer, 1);
            var payload = buffer.AsSpan(5).ToArray();

            result.Add(new RawTableRecord
            {
                Id = recordId,
                IsDeleted = isDeleted,
                PayloadHex = BuildHexPreview(payload),
                PayloadUtf8Preview = BuildUtf8Preview(payload)
            });
        }

        return result;
    }

    private static List<long> ReadExtentStarts(BinaryReader reader, TableMetadataLite metadata)
    {
        if (metadata.ExtentCount <= 1 || metadata.ExtentDirectoryOffset <= 0)
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

    private bool MatchFieldContains(object entity, string fieldName, string searchText)
    {
        var property = _entityProperties.FirstOrDefault(p => p.Name == fieldName);
        if (property is null)
        {
            return false;
        }

        var value = property.GetValue(entity);
        var text = value?.ToString() ?? string.Empty;
        return text.Contains(searchText, StringComparison.OrdinalIgnoreCase);
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

    private void EnsurePageRange()
    {
        if (PageIndex > TotalPages)
        {
            PageIndex = TotalPages;
        }

        if (PageIndex < 1)
        {
            PageIndex = 1;
        }
    }
}
