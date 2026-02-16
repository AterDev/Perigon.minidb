using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Services;

public sealed class FilterConditionService(LocalizationService localizationService)
{
    private static readonly string[] OperatorKeys =
    [
        "Contains",
        "Equals",
        "NotEquals",
        "GreaterThan",
        "GreaterOrEqual",
        "LessThan",
        "LessOrEqual",
        "Between"
    ];

    public IReadOnlyList<FilterOperatorOption> BuildOperatorOptions(string languagePreference, string currentKey)
    {
        _ = currentKey;

        return OperatorKeys
            .Select(key => new FilterOperatorOption
            {
                Key = key,
                Display = GetOperatorDisplay(key, languagePreference)
            })
            .ToList();
    }

    public string GetOperatorDisplay(string key, string languagePreference)
    {
        return key switch
        {
            "Contains" => localizationService.Localize(languagePreference, "包含", "Contains"),
            "Equals" => localizationService.Localize(languagePreference, "等于", "Equals"),
            "NotEquals" => localizationService.Localize(languagePreference, "不等于", "Not equals"),
            "GreaterThan" => localizationService.Localize(languagePreference, "大于", "Greater than"),
            "GreaterOrEqual" => localizationService.Localize(languagePreference, "大于等于", "Greater or equal"),
            "LessThan" => localizationService.Localize(languagePreference, "小于", "Less than"),
            "LessOrEqual" => localizationService.Localize(languagePreference, "小于等于", "Less or equal"),
            "Between" => localizationService.Localize(languagePreference, "区间", "Between"),
            _ => key
        };
    }

    public FilterCondition CreateCondition(
        string field,
        string filterOperator,
        string value,
        string valueTo,
        string languagePreference)
    {
        return new FilterCondition
        {
            Field = field,
            Operator = filterOperator,
            OperatorDisplay = GetOperatorDisplay(filterOperator, languagePreference),
            Value = value,
            ValueTo = valueTo
        };
    }

    public List<FilterCondition> GetActiveConditions(
        IEnumerable<FilterCondition> existingConditions,
        string? selectedFilterField,
        string selectedFilterOperator,
        string filterValue,
        string filterValueTo,
        string languagePreference)
    {
        var conditions = existingConditions.ToList();

        if (!string.IsNullOrWhiteSpace(selectedFilterField) &&
            !string.IsNullOrWhiteSpace(filterValue) &&
            !conditions.Any(c =>
                c.Field == selectedFilterField &&
                c.Operator == selectedFilterOperator &&
                c.Value == filterValue &&
                c.ValueTo == filterValueTo))
        {
            conditions.Add(CreateCondition(selectedFilterField, selectedFilterOperator, filterValue, filterValueTo, languagePreference));
        }

        return conditions
            .Where(c => !string.IsNullOrWhiteSpace(c.Field))
            .ToList();
    }

    public void RefreshConditionOperatorDisplay(IEnumerable<FilterCondition> conditions, string languagePreference)
    {
        foreach (var condition in conditions)
        {
            condition.OperatorDisplay = GetOperatorDisplay(condition.Operator, languagePreference);
        }
    }
}
