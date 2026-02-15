using System.IO;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Button? _maxRestoreButton;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        ApplyTheme(_viewModel.ThemePreference);
        ApplyGlassEffect(_viewModel.IsGlassEffectEnabled);
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsGlassEffectEnabled))
            {
                ApplyGlassEffect(_viewModel.IsGlassEffectEnabled);
            }
        };

        _maxRestoreButton = this.FindControl<Button>("MaxRestoreButton");
        UpdateCaptionButtons();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OpenConnectionManager_Click(object? sender, RoutedEventArgs e)
    {
        var manager = new ConnectionManagerWindow(_viewModel);
        manager.RequestedThemeVariant = RequestedThemeVariant;
        manager.Show(this);
    }

    private void TitleBarDragRegion_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleBarDragRegion_DoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleWindowState();
    }

    private void MinimizeWindow_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        UpdateCaptionButtons();
    }

    private void ToggleMaximizeRestore_Click(object? sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindow_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        if (_maxRestoreButton is not null)
        {
            _maxRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }
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
        if (!string.IsNullOrWhiteSpace(path))
        {
            _viewModel.NewConnectionPath = path;

            if (string.IsNullOrWhiteSpace(_viewModel.NewConnectionName))
            {
                _viewModel.NewConnectionName = Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    private async void CreateSampleDatabase_Click(object? sender, RoutedEventArgs e)
    {
        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _viewModel.Localize("创建示例数据库", "Create sample database"),
            SuggestedFileName = "sample.mds",
            FileTypeChoices =
            [
                new FilePickerFileType("MiniDB")
                {
                    Patterns = ["*.mds"],
                    AppleUniformTypeIdentifiers = ["public.data"],
                    MimeTypes = ["application/octet-stream"]
                }
            ]
        });

        if (target is null)
        {
            return;
        }

        var filePath = target.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _viewModel.StatusMessage = _viewModel.Localize("无法获取目标文件路径。", "Unable to get target file path.");
            return;
        }

        try
        {
            await Sample.SampleDbContext.CreateSampleDatabaseAsync(filePath);
            _viewModel.StatusMessage = _viewModel.LocalizeFormat("示例数据库已创建：{0}", "Sample database created: {0}", filePath);
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _viewModel.LocalizeFormat("创建示例数据库失败：{0}", "Failed to create sample database: {0}", ex.Message);
        }
    }

    private void SwitchToChinese_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetLanguagePreference("zh-CN");
    }

    private void SwitchToEnglish_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetLanguagePreference("en-US");
    }

    private void OpenRepository_Click(object? sender, RoutedEventArgs e)
    {
        const string url = "https://github.com/AterDev/Perigon.minidb";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            _viewModel.StatusMessage = _viewModel.Localize("已在浏览器中打开仓库地址。", "Repository opened in browser.");
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _viewModel.LocalizeFormat("打开仓库地址失败：{0}", "Failed to open repository: {0}", ex.Message);
        }
    }

    private void OpenIssues_Click(object? sender, RoutedEventArgs e)
    {
        const string url = "https://github.com/AterDev/Perigon.minidb/issues";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            _viewModel.StatusMessage = _viewModel.Localize("已打开问题反馈页面。", "Issues page opened.");
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _viewModel.LocalizeFormat("打开问题反馈页面失败：{0}", "Failed to open issues page: {0}", ex.Message);
        }
    }

    private void UseLightTheme_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetThemePreference("Light");
        ApplyTheme("Light");
        ApplyGlassEffect(_viewModel.IsGlassEffectEnabled);
    }

    private void UseDarkTheme_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetThemePreference("Dark");
        ApplyTheme("Dark");
        ApplyGlassEffect(_viewModel.IsGlassEffectEnabled);
    }

    private void UseSystemTheme_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetThemePreference("System");
        ApplyTheme("System");
        ApplyGlassEffect(_viewModel.IsGlassEffectEnabled);
    }

    private void ResetViewPreferences_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.ResetViewPreferences();
        ApplyTheme(_viewModel.ThemePreference);
        ApplyGlassEffect(_viewModel.IsGlassEffectEnabled);
    }

    private static void ApplyTheme(string preference)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = preference switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    private void ApplyGlassEffect(bool enabled)
    {
        var isDark = IsDarkThemeActive();

        if (!enabled)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            Background = isDark
                ? new SolidColorBrush(Color.FromArgb(255, 30, 30, 34))
                : new SolidColorBrush(Color.FromArgb(255, 248, 249, 251));
            return;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TransparencyLevelHint =
            [
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur
            ];

            Background = isDark
                ? new SolidColorBrush(Color.FromArgb(180, 30, 30, 34))
                : new SolidColorBrush(Color.FromArgb(214, 252, 252, 254));
            return;
        }

        TransparencyLevelHint = [WindowTransparencyLevel.Blur, WindowTransparencyLevel.None];
        Background = isDark
            ? new SolidColorBrush(Color.FromArgb(220, 30, 30, 34))
            : new SolidColorBrush(Color.FromArgb(235, 249, 249, 252));
    }

    private bool IsDarkThemeActive()
    {
        if (_viewModel.ThemePreference == "Dark")
        {
            return true;
        }

        if (_viewModel.ThemePreference == "Light")
        {
            return false;
        }

        var appVariant = Application.Current?.ActualThemeVariant;
        return appVariant == ThemeVariant.Dark;
    }
}