namespace Perigon.MiniDb.Client.Services;

public sealed class LocalizationService
{
    public bool IsEnglish(string? languagePreference)
    {
        return !string.IsNullOrWhiteSpace(languagePreference)
            && languagePreference.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    public string Localize(string? languagePreference, string zh, string en)
    {
        return IsEnglish(languagePreference) ? en : zh;
    }
}
