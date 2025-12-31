using System.IO;
using System.Windows;

namespace Perigon.MiniDb.Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create sample database if it doesn't exist
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Perigon.MiniDb.Sample");
        
        Directory.CreateDirectory(appDataPath);
        var sampleDbPath = Path.Combine(appDataPath, "sample.mds");

        if (!File.Exists(sampleDbPath))
        {
            try
            {
                await Sample.SampleDbContext.CreateSampleDatabaseAsync(sampleDbPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create sample database: {ex.Message}", "Warning", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

