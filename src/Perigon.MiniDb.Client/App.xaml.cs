using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Perigon.MiniDb.Client.Services;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

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

        EnsureSampleDatabase();

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources/Localization");
        services.AddSingleton<DatabaseConnectionService>();
        services.AddSingleton<ClientSettingsService>();
        services.AddSingleton<MainViewModel>();
    }

    private static void EnsureSampleDatabase()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Perigon.MiniDb.Sample");

        Directory.CreateDirectory(appDataPath);
        var sampleDbPath = Path.Combine(appDataPath, "sample.mds");

        if (!File.Exists(sampleDbPath))
        {
            try
            {
                Sample.SampleDbContext.CreateSampleDatabaseAsync(sampleDbPath).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create sample database: {ex.Message}");
            }
        }
    }
}

