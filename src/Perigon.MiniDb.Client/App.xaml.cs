using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Perigon.MiniDb.Client;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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

