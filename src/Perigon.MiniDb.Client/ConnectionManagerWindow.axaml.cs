using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

public partial class ConnectionManagerWindow : Window
{
    private readonly MainViewModel _viewModel;

    public ConnectionManagerWindow()
        : this(new MainViewModel())
    {
    }

    public ConnectionManagerWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void BrowseConnectionPath_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _viewModel.Localize("选择 MiniDB 文件", "Select MiniDB file"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("MiniDB")
                {
                    Patterns = ["*.mds"],
                    AppleUniformTypeIdentifiers = ["public.data"],
                    MimeTypes = ["application/octet-stream"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _viewModel.NewConnectionPath = path;
        if (string.IsNullOrWhiteSpace(_viewModel.NewConnectionName))
        {
            _viewModel.NewConnectionName = Path.GetFileNameWithoutExtension(path);
        }
    }

    private void OpenSelectedConnection_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.OpenSelectedConnection())
        {
            Close();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
