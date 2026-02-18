namespace Perigon.MiniDb.Client.Models;

public sealed class RawTableRecord
{
    public int Id { get; init; }
    public bool IsDeleted { get; init; }
    public string PayloadHex { get; init; } = string.Empty;
    public string PayloadUtf8Preview { get; init; } = string.Empty;
}
