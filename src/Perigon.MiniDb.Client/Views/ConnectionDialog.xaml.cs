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
            Filter = "MiniDB files (*.mdb)|*.mdb|All files (*.*)|*.*",
            CheckFileExists = false
        };

        if (dialog.ShowDialog() == true)
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

        Connection = new DatabaseConnection
        {
            Name = NameTextBox.Text.Trim(),
            Path = PathTextBox.Text.Trim()
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
