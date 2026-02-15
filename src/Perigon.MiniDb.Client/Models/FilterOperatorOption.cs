namespace Perigon.MiniDb.Client.Models;

public class FilterOperatorOption
{
    public required string Key { get; init; }
    public required string Display { get; init; }

    public override string ToString()
    {
        return Display;
    }
}
