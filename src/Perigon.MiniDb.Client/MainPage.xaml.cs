using System;
using System.ComponentModel;
using Microsoft.Maui.Controls;
using Perigon.MiniDb.Client.Services;
using Perigon.MiniDb.Client.ViewModels;
using Perigon.MiniDb.Client.Views;
#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
#endif

namespace Perigon.MiniDb.Client;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly DesktopFilePickerService _filePickerService;
    private readonly AppMenuActionService _menuActionService;
    private Window? _connectionManagerWindow;

    public MainPage(MainViewModel viewModel, DesktopFilePickerService filePickerService, AppMenuActionService menuActionService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _filePickerService = filePickerService;
        _menuActionService = menuActionService;

#if WINDOWS
        MenuBarItems.Clear();
#endif

        _menuActionService.ActionRequested += OnMenuActionRequested;

        ApplyLocalizedMenuText();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnMenuActionRequested(AppMenuAction action)
    {
        await ExecuteNativeMenuActionAsync(action);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.LanguagePreference)
            or nameof(MainViewModel.MenuConnection)
            or nameof(MainViewModel.MenuManageConnections)
            or nameof(MainViewModel.BtnDisconnect)
            or nameof(MainViewModel.BtnRefresh)
            or nameof(MainViewModel.MenuAppearance)
            or nameof(MainViewModel.MenuSystemTheme)
            or nameof(MainViewModel.MenuLightTheme)
            or nameof(MainViewModel.MenuDarkTheme)
            or nameof(MainViewModel.MenuLanguage)
            or nameof(MainViewModel.MenuLangZh)
            or nameof(MainViewModel.MenuLangEn))
        {
            MainThread.BeginInvokeOnMainThread(ApplyLocalizedMenuText);
        }
    }

    private void ApplyLocalizedMenuText()
    {
        ConnectionMenu.Text = _viewModel.MenuConnection;
        ManageConnectionsMenuItem.Text = _viewModel.MenuManageConnections;
        DisconnectMenuItem.Text = _viewModel.BtnDisconnect;
        RefreshTableMenuItem.Text = _viewModel.BtnRefresh;

        SettingsMenu.Text = _viewModel.MenuAppearance;
        ThemeSubMenu.Text = _viewModel.Localize("主题", "Theme");
        ThemeSystemMenuItem.Text = _viewModel.MenuSystemTheme;
        ThemeLightMenuItem.Text = _viewModel.MenuLightTheme;
        ThemeDarkMenuItem.Text = _viewModel.MenuDarkTheme;
        LanguageSubMenu.Text = _viewModel.MenuLanguage;
        LanguageZhMenuItem.Text = _viewModel.MenuLangZh;
        LanguageEnMenuItem.Text = _viewModel.MenuLangEn;
    }

    private async void ManageConnections_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.ManageConnections);
    }

    private async void ResetView_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.ResetView);
    }

    private async void Disconnect_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.Disconnect);
    }

    private async void RefreshTable_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.RefreshTable);
    }

    private async void ThemeSystem_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.ThemeSystem);
    }

    private async void ThemeLight_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.ThemeLight);
    }

    private async void ThemeDark_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.ThemeDark);
    }

    private async void LanguageZh_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.LanguageZhCn);
    }

    private async void LanguageEn_Clicked(object? sender, EventArgs e)
    {
        await ExecuteNativeMenuActionAsync(AppMenuAction.LanguageEnUs);
    }

    private async Task ExecuteNativeMenuActionAsync(AppMenuAction action)
    {
        switch (action)
        {
            case AppMenuAction.ManageConnections:
                await OpenConnectionManagerAsync();
                break;
            case AppMenuAction.ResetView:
                _viewModel.ResetViewPreferences();
                ApplyTheme(_viewModel.ThemePreference);
                break;
            case AppMenuAction.Connect:
                _viewModel.ConnectCommand.Execute(null);
                break;
            case AppMenuAction.Disconnect:
                _viewModel.DisconnectCommand.Execute(null);
                break;
            case AppMenuAction.RefreshTable:
                _viewModel.RefreshTableCommand.Execute(null);
                break;
            case AppMenuAction.ThemeSystem:
                ApplyTheme("System");
                break;
            case AppMenuAction.ThemeLight:
                ApplyTheme("Light");
                break;
            case AppMenuAction.ThemeDark:
                ApplyTheme("Dark");
                break;
            case AppMenuAction.LanguageZhCn:
                _viewModel.SetLanguagePreference("zh-CN");
                break;
            case AppMenuAction.LanguageEnUs:
                _viewModel.SetLanguagePreference("en-US");
                break;
        }
    }

    private async Task OpenConnectionManagerAsync()
    {
        if (_connectionManagerWindow is not null)
        {
            _viewModel.StatusMessage = _viewModel.Localize("连接管理窗口已打开。", "Connection manager window is already open.");
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var page = new ConnectionManagerPage(_viewModel, _filePickerService);
            var window = new Window(page)
            {
                Title = _viewModel.MenuManageConnections,
                Width = 920,
                Height = 560
            };

#if WINDOWS
            window.HandlerChanged += OnConnectionManagerWindowHandlerChanged;
#endif

            window.Destroying += (_, _) => _connectionManagerWindow = null;
            _connectionManagerWindow = window;
            Application.Current?.OpenWindow(window);
        });
    }

#if WINDOWS
    private void OnConnectionManagerWindowHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not Window window || window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);

        if (displayArea is null)
        {
            return;
        }

        var x = displayArea.WorkArea.X + Math.Max(0, (displayArea.WorkArea.Width - appWindow.Size.Width) / 2);
        var y = displayArea.WorkArea.Y + Math.Max(0, (displayArea.WorkArea.Height - appWindow.Size.Height) / 2);
        appWindow.Move(new PointInt32(x, y));

        window.HandlerChanged -= OnConnectionManagerWindowHandlerChanged;
    }
#endif

    private void ApplyTheme(string theme)
    {
        _viewModel.SetThemePreference(theme);
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.UserAppTheme = theme switch
        {
            "Light" => Microsoft.Maui.ApplicationModel.AppTheme.Light,
            "Dark" => Microsoft.Maui.ApplicationModel.AppTheme.Dark,
            _ => Microsoft.Maui.ApplicationModel.AppTheme.Unspecified
        };
    }
}
