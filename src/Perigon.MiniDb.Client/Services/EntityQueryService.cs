using System.Globalization;
using System.Reflection;
using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Services;

public sealed class EntityQueryService
{
    public List<PropertyInfo> GetReadableProperties(Type entityType)
    {
        return entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToList();
    }

    public List<string> GetFilterFields(IReadOnlyList<PropertyInfo> properties)
    {
        return properties.Select(p => p.Name).ToList();
    }

    public List<object> ApplyFilter(
        IReadOnlyList<object> allEntities,
        IReadOnlyList<PropertyInfo> entityProperties,
        IReadOnlyList<FilterCondition> activeConditions)
    {
        if (activeConditions.Count == 0)
        {
            return [.. allEntities];
        }

        return allEntities
            .Where(entity => activeConditions.All(condition => EvaluateEntity(entity, entityProperties, condition)))
            .ToList();
    }

    public List<object> ApplyQuickFilter(
        IReadOnlyList<object> allEntities,
        string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [.. allEntities];
        }

        return allEntities
            .Where(entity => MatchKeyword(entity, keyword))
            .ToList();
    }

    private static bool MatchKeyword(object entity, string keyword)
    {
        if (entity is IReadOnlyDictionary<string, object?> readOnlyMap)
        {
            return readOnlyMap.Values.Any(value =>
                !string.IsNullOrWhiteSpace(value?.ToString())
                && value!.ToString()!.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (entity is IDictionary<string, object?> map)
        {
            return map.Values.Any(value =>
                !string.IsNullOrWhiteSpace(value?.ToString())
                && value!.ToString()!.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var properties = entity.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

        return properties.Any(property =>
        {
            var valueText = property.GetValue(entity)?.ToString();
            return !string.IsNullOrWhiteSpace(valueText)
                   && valueText.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        });
    }

    public (List<object> Items, int PageIndex, int TotalPages) Paginate(IReadOnlyList<object> entities, int pageIndex, int pageSize)
    {
        var normalizedPageSize = Math.Max(1, pageSize);
        var totalCount = entities.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / normalizedPageSize));

        var normalizedPageIndex = pageIndex;
        if (normalizedPageIndex > totalPages)
        {
            normalizedPageIndex = totalPages;
        }

        if (normalizedPageIndex < 1)
        {
            normalizedPageIndex = 1;
        }

        var items = entities
            .Skip((normalizedPageIndex - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();

        return (items, normalizedPageIndex, totalPages);
    }

    private static bool EvaluateEntity(object entity, IReadOnlyList<PropertyInfo> entityProperties, FilterCondition condition)
    {
        var property = entityProperties.FirstOrDefault(p => p.Name == condition.Field);
        if (property is null)
        {
            return false;
        }

        var value = property.GetValue(entity);
        var effectiveType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (condition.Operator == "Contains")
        {
            var text = value?.ToString() ?? string.Empty;
            return text.Contains(condition.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        if (!TryConvertFromString(condition.Value, effectiveType, out var convertedValue))
        {
            throw new InvalidOperationException($"筛选值 '{condition.Value}' 无法转换为 {effectiveType.Name}。");
        }

        if (condition.Operator == "Between")
        {
            if (!TryConvertFromString(condition.ValueTo, effectiveType, out var upperBound))
            {
                throw new InvalidOperationException($"筛选上限 '{condition.ValueTo}' 无法转换为 {effectiveType.Name}。");
            }

            return Compare(value, convertedValue, effectiveType) >= 0
                   && Compare(value, upperBound, effectiveType) <= 0;
        }

        return condition.Operator switch
        {
            "Equals" => Compare(value, convertedValue, effectiveType) == 0,
            "NotEquals" => Compare(value, convertedValue, effectiveType) != 0,
            "GreaterThan" => Compare(value, convertedValue, effectiveType) > 0,
            "GreaterOrEqual" => Compare(value, convertedValue, effectiveType) >= 0,
            "LessThan" => Compare(value, convertedValue, effectiveType) < 0,
            "LessOrEqual" => Compare(value, convertedValue, effectiveType) <= 0,
            _ => false
        };
    }

    private static int Compare(object? left, object? right, Type type)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (type.IsEnum)
        {
            var leftValue = Convert.ToInt64(left, CultureInfo.InvariantCulture);
            var rightValue = Convert.ToInt64(right, CultureInfo.InvariantCulture);
            return leftValue.CompareTo(rightValue);
        }

        if (left is IComparable comparable)
        {
            return comparable.CompareTo(right);
        }

        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvertFromString(string input, Type targetType, out object? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            if (Nullable.GetUnderlyingType(targetType) is not null || !targetType.IsValueType)
            {
                value = null;
                return true;
            }

            return false;
        }

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (effectiveType == typeof(string))
            {
                value = input;
                return true;
            }

            if (effectiveType == typeof(DateTime))
            {
                if (DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var dateTime))
                {
                    value = dateTime;
                    return true;
                }

                return false;
            }

            if (effectiveType == typeof(Guid))
            {
                if (Guid.TryParse(input, out var guid))
                {
                    value = guid;
                    return true;
                }

                return false;
            }

            if (effectiveType == typeof(bool))
            {
                if (bool.TryParse(input, out var boolValue))
                {
                    value = boolValue;
                    return true;
                }

                return false;
            }

            if (effectiveType.IsEnum)
            {
                value = Enum.Parse(effectiveType, input, ignoreCase: true);
                return true;
            }

            value = Convert.ChangeType(input, effectiveType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}
