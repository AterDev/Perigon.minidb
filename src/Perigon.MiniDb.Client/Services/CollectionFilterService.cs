using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Services;

public sealed class CollectionFilterService
{
    public ConnectionFilterResult FilterConnections(
        IReadOnlyList<DatabaseConnection> sourceConnections,
        string searchText,
        DatabaseConnection? currentSelection,
        bool isConnected)
    {
        var filtered = sourceConnections
            .Where(connection => string.IsNullOrWhiteSpace(searchText)
                || connection.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || connection.Path.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        DatabaseConnection? resolvedSelection = currentSelection;
        if (currentSelection is not null && !filtered.Contains(currentSelection))
        {
            resolvedSelection = isConnected ? currentSelection : filtered.FirstOrDefault();
        }

        if (!isConnected && resolvedSelection is null)
        {
            resolvedSelection = filtered.FirstOrDefault();
        }

        return new ConnectionFilterResult(filtered, resolvedSelection);
    }

    public TableFilterResult FilterTableNames(
        IReadOnlyList<string> sourceTableNames,
        string searchText,
        string? currentSelection)
    {
        var filtered = sourceTableNames
            .Where(name => string.IsNullOrWhiteSpace(searchText)
                || name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        string? resolvedSelection = currentSelection;
        if (!string.IsNullOrWhiteSpace(currentSelection) && !filtered.Contains(currentSelection))
        {
            resolvedSelection = filtered.FirstOrDefault();
        }

        return new TableFilterResult(filtered, resolvedSelection);
    }
}

public sealed record ConnectionFilterResult(
    IReadOnlyList<DatabaseConnection> FilteredConnections,
    DatabaseConnection? ResolvedSelection);

public sealed record TableFilterResult(
    IReadOnlyList<string> FilteredTableNames,
    string? ResolvedSelection);
