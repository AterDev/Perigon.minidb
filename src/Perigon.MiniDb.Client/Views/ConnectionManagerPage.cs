using System.ComponentModel;
using System.IO;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Perigon.MiniDb.Client.Services;
using Perigon.MiniDb.Client.ViewModels;
using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Views;

public sealed class ConnectionManagerPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly DesktopFilePickerService _filePickerService;

    private readonly Label _nameLabel;
    private readonly Label _pathLabel;
    private readonly Label _connectionsLabel;
    private readonly Button _browseButton;
    private readonly CollectionView _connectionsList;

    public ConnectionManagerPage(MainViewModel viewModel, DesktopFilePickerService filePickerService)
    {
        _viewModel = viewModel;
        _filePickerService = filePickerService;

        BindingContext = _viewModel;
        BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#0F172A")
            : Color.FromArgb("#F8FAFC");
        Padding = new Thickness(0);

        _nameLabel = new Label { VerticalOptions = LayoutOptions.Center };
        _pathLabel = new Label { VerticalOptions = LayoutOptions.Center };
        _connectionsLabel = new Label { VerticalOptions = LayoutOptions.Center };

        var nameEntry = new Entry { ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
        nameEntry.SetBinding(Entry.TextProperty, nameof(MainViewModel.NewConnectionName), mode: BindingMode.TwoWay);

        var pathEntry = new Entry { ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
        pathEntry.SetBinding(Entry.TextProperty, nameof(MainViewModel.NewConnectionPath), mode: BindingMode.TwoWay);

        _browseButton = new Button { WidthRequest = 110, HeightRequest = 34 };
        _browseButton.Clicked += OnBrowseClicked;

        var addButton = new Button { HeightRequest = 36 };
        addButton.SetBinding(Button.TextProperty, nameof(MainViewModel.BtnAdd));
        addButton.SetBinding(Button.CommandProperty, nameof(MainViewModel.AddConnectionCommand));

        var updateButton = new Button { HeightRequest = 36 };
        updateButton.SetBinding(Button.TextProperty, nameof(MainViewModel.BtnUpdate));
        updateButton.SetBinding(Button.CommandProperty, nameof(MainViewModel.EditConnectionCommand));

        var deleteButton = new Button { HeightRequest = 36 };
        deleteButton.SetBinding(Button.TextProperty, nameof(MainViewModel.BtnDelete));
        deleteButton.SetBinding(Button.CommandProperty, nameof(MainViewModel.DeleteConnectionCommand));

        _connectionsList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            HeightRequest = 220,
            ItemTemplate = new DataTemplate(() =>
            {
                var button = new Button
                {
                    HorizontalOptions = LayoutOptions.Fill,
                    HeightRequest = 36,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                button.SetBinding(Button.TextProperty, nameof(DatabaseConnection.Name));
                button.Clicked += OnConnectionQuickOpenClicked;
                return button;
            })
        };
        _connectionsList.SetBinding(ItemsView.ItemsSourceProperty, nameof(MainViewModel.Connections));

        var root = new Grid
        {
            Padding = new Thickness(14)
        };

        var nameGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(GridLength.Star)
            }
        };
        nameGrid.Children.Add(_nameLabel);
        Grid.SetColumn(_nameLabel, 0);
        nameGrid.Children.Add(nameEntry);
        Grid.SetColumn(nameEntry, 1);

        var pathGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        pathGrid.Children.Add(_pathLabel);
        Grid.SetColumn(_pathLabel, 0);
        pathGrid.Children.Add(pathEntry);
        Grid.SetColumn(pathEntry, 1);
        pathGrid.Children.Add(_browseButton);
        Grid.SetColumn(_browseButton, 2);

        var listHeader = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                _connectionsLabel
            }
        };

        root.Children.Add(new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    nameGrid,
                    pathGrid,
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { addButton, updateButton, deleteButton }
                    },
                    listHeader,
                    _connectionsList
                }
            }
        });
        Content = root;

        ApplyLocalization();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private async void OnBrowseClicked(object? sender, EventArgs e)
    {
        try
        {
            _viewModel.StatusMessage = _viewModel.Localize("正在打开文件选择器...", "Opening file picker...");
            var path = await _filePickerService.PickMiniDbFileAsync();
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
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _viewModel.Localize($"浏览文件失败：{ex.Message}", $"Browse failed: {ex.Message}");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.LanguagePreference)
            or nameof(MainViewModel.MenuManageConnections)
            or nameof(MainViewModel.BtnBrowse)
            or nameof(MainViewModel.MenuLangZh)
            or nameof(MainViewModel.MenuLangEn))
        {
            MainThread.BeginInvokeOnMainThread(ApplyLocalization);
        }
    }

    private void ApplyLocalization()
    {
        Title = _viewModel.MenuManageConnections;
        _nameLabel.Text = _viewModel.Localize("名称", "Name");
        _pathLabel.Text = _viewModel.Localize("路径", "Path");
        _connectionsLabel.Text = _viewModel.Localize("连接列表（下方点击即连接）", "Connections (click below to connect)");
        _browseButton.Text = _viewModel.BtnBrowse;
    }

    private void OnConnectionQuickOpenClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: DatabaseConnection connection })
        {
            return;
        }

        _viewModel.SelectConnection(connection);
        var opened = _viewModel.OpenSelectedConnection();
        if (!opened)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Window;
            if (window is not null)
            {
                Application.Current?.CloseWindow(window);
            }
        });
    }
}
