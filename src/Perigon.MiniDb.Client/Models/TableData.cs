namespace Perigon.MiniDb.Client.Models;

/// <summary>
/// Decoded table data returned by MiniDbFileReader.
/// </summary>
public sealed class TableData
{
    public List<string> FieldNames { get; init; } = [];
    public List<DynamicRecord> Records { get; init; } = [];

    /// <summary>
    /// Non-null when the reader fell back to raw display mode.
    /// Contains a reason string (e.g. missing field metadata from older engine version).
    /// </summary>
    public string? FallbackReason { get; init; }
}

/// <summary>
/// A single decoded record with field values accessible by name.
/// Supports indexer binding in Avalonia DataGrid: {Binding [FieldName]}.
/// </summary>
public sealed class DynamicRecord(Dictionary<string, string> values)
{
    private readonly Dictionary<string, string> _values = values;

    public string this[string key] => _values.TryGetValue(key, out var v) ? v : string.Empty;
}
