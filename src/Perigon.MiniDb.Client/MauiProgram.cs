using CommunityToolkit.Maui;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.FluentUI.AspNetCore.Components;
using Perigon.MiniDb.Client.Services;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddFluentUIComponents(); 

        builder.Services.AddSingleton<DatabaseConnectionService>();
        builder.Services.AddSingleton<ClientSettingsService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<AppMenuActionService>();
        builder.Services.AddSingleton<DesktopFilePickerService>();
        builder.Services.AddSingleton<ConnectionSessionService>();
        builder.Services.AddSingleton<ReflectionTableService>();
        builder.Services.AddSingleton<EntityQueryService>();
        builder.Services.AddSingleton<CollectionFilterService>();
        builder.Services.AddSingleton<LocalizationService>();
        builder.Services.AddSingleton<StatusToneService>();
        builder.Services.AddSingleton<FilterConditionService>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }
}
