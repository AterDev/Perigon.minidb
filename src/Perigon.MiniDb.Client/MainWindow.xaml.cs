using System.IO;
using System.Windows;
using Microsoft.Win32;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private async void CreateSampleDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "MiniDB files (*.mds)|*.mds|All files (*.*)|*.*",
            DefaultExt = ".mds",
            FileName = "sample.mds"
        };

        if (dialog.ShowDialog() is true)
        {
            try
            {
                await Sample.SampleDbContext.CreateSampleDatabaseAsync(dialog.FileName);
                MessageBox.Show(
                    $"Sample database created successfully at:\n{dialog.FileName}\n\n" +
                    "The database contains sample Products and Categories tables with test data.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create sample database: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Perigon MiniDB Manager\n\n" +
            "Version 1.0.0\n\n" +
            "A lightweight database management tool for Perigon MiniDB.\n\n" +
            "Built with WPF on .NET 10.0",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}