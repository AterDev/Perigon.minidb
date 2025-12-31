using System.IO;
using System.Windows;
using Microsoft.Win32;
using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Views;

public partial class ConnectionDialog : Window
{
    public DatabaseConnection? Connection { get; private set; }

    public ConnectionDialog(DatabaseConnection? existingConnection = null)
    {
        InitializeComponent();

        if (existingConnection != null)
        {
            NameTextBox.Text = existingConnection.Name;
            PathTextBox.Text = existingConnection.Path;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MiniDB files (*.mds)|*.mds|All files (*.*)|*.*",
            CheckFileExists = false
        };

        if (dialog.ShowDialog() is true)
        {
            PathTextBox.Text = dialog.FileName;
            
            // If name is empty, suggest a name from the file
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                NameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            MessageBox.Show("Please enter a connection name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(PathTextBox.Text))
        {
            MessageBox.Show("Please enter or select a database path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var path = PathTextBox.Text.Trim();
        
        // Validate path format
        try
        {
            // Check if path is rooted (not relative)
            if (!Path.IsPathRooted(path))
            {
                MessageBox.Show("Please enter an absolute file path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate path doesn't contain invalid characters
            var invalidChars = Path.GetInvalidPathChars();
            if (path.Any(c => invalidChars.Contains(c)))
            {
                MessageBox.Show("The path contains invalid characters.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get directory and check if it's valid
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                MessageBox.Show("Please enter a valid file path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Invalid path: {ex.Message}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Connection = new DatabaseConnection
        {
            Name = NameTextBox.Text.Trim(),
            Path = path
        };

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
