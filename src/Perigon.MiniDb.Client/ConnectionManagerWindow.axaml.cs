using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Perigon.MiniDb.Client.Resources.Localization;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

public partial class ConnectionManagerWindow : Window
{
    private readonly MainViewModelV2 _viewModel;

    public ConnectionManagerWindow()
        : this(App.Services?.GetService<MainViewModelV2>() ?? new MainViewModelV2())
    {
    }

    public ConnectionManagerWindow(MainViewModelV2 viewModel)
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
            Title = _viewModel.Localize(AppStrings.Keys.DialogSelectMiniDbFile),
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
