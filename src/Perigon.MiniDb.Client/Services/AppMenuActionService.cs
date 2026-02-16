namespace Perigon.MiniDb.Client.Services;

public enum AppMenuAction
{
    ManageConnections,
    ResetView,
    Connect,
    Disconnect,
    RefreshTable,
    ThemeSystem,
    ThemeLight,
    ThemeDark,
    LanguageZhCn,
    LanguageEnUs
}

public sealed class AppMenuActionService
{
    public event Action<AppMenuAction>? ActionRequested;

    public void Request(AppMenuAction action)
    {
        ActionRequested?.Invoke(action);
    }
}
