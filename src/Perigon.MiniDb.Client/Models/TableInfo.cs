namespace Perigon.MiniDb.Client.Models;

/// <summary>
/// Represents metadata about a table in the database
/// </summary>
public class TableInfo
{
    public string Name { get; set; } = string.Empty;
    public int RecordCount { get; set; }
}
