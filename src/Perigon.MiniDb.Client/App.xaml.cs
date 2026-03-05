using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Microsoft.Extensions.DependencyInjection;
using Perigon.MiniDb.Client.Services;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private static readonly Uri ZhCnUri = new("avares://Perigon.MiniDb.Client/Resources/Localization/Strings.axaml");
    private static readonly Uri EnUsUri = new("avares://Perigon.MiniDb.Client/Resources/Localization/Strings.en-US.axaml");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Swaps the localization resource dictionary at runtime.
    /// All {DynamicResource Key} bindings update automatically.
    /// </summary>
    public static void SwitchLanguage(string cultureName)
    {
        if (Current is not App app)
            return;

        var targetUri = cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? EnUsUri : ZhCnUri;
        var newDict = new ResourceInclude(targetUri) { Source = targetUri };

        var merged = app.Resources.MergedDictionaries;
        // Find and replace the existing Strings dictionary
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i] is ResourceInclude ri && ri.Source is not null
                && (ri.Source == ZhCnUri || ri.Source == EnUsUri))
            {
                merged[i] = newDict;
                return;
            }
        }

        // Fallback: add if not found
        merged.Add(newDict);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DatabaseConnectionService>();
        services.AddSingleton<ClientSettingsService>();
        services.AddSingleton<MainViewModelV2>();
    }
}

