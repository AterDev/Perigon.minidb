using System.Collections.Concurrent;
using System.Reflection;

namespace Perigon.MiniDb.Client.Services;

public sealed class ReflectionTableService
{
    private readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();
    private static readonly HashSet<string> HiddenColumns =
    [
        "RawHex"
    ];

    private static readonly HashSet<string> TrailingColumns =
    [
        "RawText"
    ];

    public IReadOnlyList<string> GetHeaders(IEnumerable<object> rows)
    {
        var first = rows.FirstOrDefault();
        if (first is null)
        {
            return [];
        }

        if (first is IReadOnlyDictionary<string, object?> readOnlyMap)
        {
            return GetPreferredHeaders(readOnlyMap.Keys);
        }

        if (first is IDictionary<string, object?> map)
        {
            return GetPreferredHeaders(map.Keys);
        }

        return GetProperties(first.GetType())
            .Select(p => p.Name)
            .ToList();
    }

    public IReadOnlyList<string> GetRowValues(object row, IReadOnlyList<string> headers)
    {
        if (row is IReadOnlyDictionary<string, object?> readOnlyMap)
        {
            return headers.Select(header => readOnlyMap.TryGetValue(header, out var value) ? value?.ToString() ?? string.Empty : string.Empty).ToList();
        }

        if (row is IDictionary<string, object?> map)
        {
            return headers.Select(header => map.TryGetValue(header, out var value) ? value?.ToString() ?? string.Empty : string.Empty).ToList();
        }

        var properties = GetProperties(row.GetType());
        var propertyMap = properties.ToDictionary(p => p.Name, p => p);

        var values = new List<string>(headers.Count);
        foreach (var header in headers)
        {
            if (!propertyMap.TryGetValue(header, out var property))
            {
                values.Add(string.Empty);
                continue;
            }

            values.Add(property.GetValue(row)?.ToString() ?? string.Empty);
        }

        return values;
    }

    private PropertyInfo[] GetProperties(Type type)
    {
        return _propertyCache.GetOrAdd(type, static t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
             .Where(p => p.CanRead)
             .ToArray());
    }

    private static IReadOnlyList<string> GetPreferredHeaders(IEnumerable<string> sourceHeaders)
    {
        var all = sourceHeaders
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Where(header => !HiddenColumns.Contains(header))
            .ToList();

        if (all.Count == 0)
        {
            return [];
        }

        var result = new List<string>(all.Count);

        if (all.Remove("Id"))
        {
            result.Add("Id");
        }

        var trailing = all.Where(TrailingColumns.Contains).ToList();
        foreach (var tail in trailing)
        {
            all.Remove(tail);
        }

        result.AddRange(all);
        result.AddRange(trailing);

        return result;
    }
}
