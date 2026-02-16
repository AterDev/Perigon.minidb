using Microsoft.Maui.Controls;
using Perigon.MiniDb.Client.Services;
using Perigon.MiniDb.Client.ViewModels;
using System.ComponentModel;
#if WINDOWS
using WinUIButton = Microsoft.UI.Xaml.Controls.Button;
using WinUIColor = Microsoft.UI.Colors;
using WinUIItemBase = Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase;
using WinUIFlyoutBase = Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase;
using WinUIFlyoutPlacementMode = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode;
using WinUIFlyoutShowOptions = Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions;
using WinUIMenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using WinUIMenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using WinUIMenuFlyoutSubItem = Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem;
using WinUISolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
#endif

namespace Perigon.MiniDb.Client;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly AppMenuActionService _menuActionService;
    private readonly MainViewModel _viewModel;
    private Window? _window;

    public App(MainPage mainPage, AppMenuActionService menuActionService, MainViewModel viewModel)
    {
        InitializeComponent();
        _mainPage = mainPage;
        _menuActionService = menuActionService;
        _viewModel = viewModel;

#if WINDOWS
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        NavigationPage.SetHasNavigationBar(_mainPage, false);
        var navigationPage = new NavigationPage(_mainPage);
        var window = new Window(navigationPage);
        _window = window;

#if WINDOWS
        window.TitleBar = BuildWindowsTitleBar();
#endif

        return window;
    }

#if WINDOWS
    private TitleBar BuildWindowsTitleBar()
    {
        var leading = new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center
        };

        leading.Children.Add(CreateTitleMenuButton(_viewModel.MenuConnection, [
            new TitleMenuNode(_viewModel.MenuManageConnections, AppMenuAction.ManageConnections),
            new TitleMenuNode(_viewModel.BtnDisconnect, AppMenuAction.Disconnect),
            new TitleMenuNode(_viewModel.BtnRefresh, AppMenuAction.RefreshTable)
        ]));

        leading.Children.Add(CreateTitleMenuButton(_viewModel.MenuAppearance, [
            new TitleMenuNode(_viewModel.Localize("主题", "Theme"), Children:
            [
                new TitleMenuNode(_viewModel.MenuSystemTheme, AppMenuAction.ThemeSystem),
                new TitleMenuNode(_viewModel.MenuLightTheme, AppMenuAction.ThemeLight),
                new TitleMenuNode(_viewModel.MenuDarkTheme, AppMenuAction.ThemeDark)
            ]),
            new TitleMenuNode(_viewModel.MenuLanguage, Children:
            [
                new TitleMenuNode(_viewModel.MenuLangZh, AppMenuAction.LanguageZhCn),
                new TitleMenuNode(_viewModel.MenuLangEn, AppMenuAction.LanguageEnUs)
            ])
        ]));

        return new TitleBar
        {
            Title = string.Empty,
            LeadingContent = leading
        };
    }

    private Button CreateTitleMenuButton(string text, IReadOnlyList<TitleMenuNode> items)
    {
        var button = new Button
        {
            Text = text,
            Padding = new Thickness(4, 0),
            FontSize = 13,
            MinimumHeightRequest = 24,
            MinimumWidthRequest = 44,
            BackgroundColor = Colors.Transparent,
            BorderColor = Colors.Transparent
        };

        var attached = false;
        button.HandlerChanged += (_, _) =>
        {
            if (attached)
            {
                return;
            }

            if (button.Handler?.PlatformView is not WinUIButton nativeButton)
            {
                return;
            }

            var flyout = new WinUIMenuFlyout();
            foreach (var item in items)
            {
                flyout.Items.Add(CreateNativeMenuItem(item));
            }

            nativeButton.Background = new WinUISolidColorBrush(WinUIColor.Transparent);
            nativeButton.BorderBrush = new WinUISolidColorBrush(WinUIColor.Transparent);
            nativeButton.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            nativeButton.Padding = new Microsoft.UI.Xaml.Thickness(6, 0, 6, 0);

            WinUIFlyoutBase.SetAttachedFlyout(nativeButton, flyout);
            nativeButton.Click += (_, _) =>
            {
                var options = new WinUIFlyoutShowOptions
                {
                    Placement = WinUIFlyoutPlacementMode.BottomEdgeAlignedLeft
                };
                flyout.ShowAt(nativeButton, options);
            };

            attached = true;
        };

        return button;
    }

    private WinUIItemBase CreateNativeMenuItem(TitleMenuNode node)
    {
        if (node.Children is { Count: > 0 })
        {
            var subItem = new WinUIMenuFlyoutSubItem
            {
                Text = node.Text
            };

            foreach (var child in node.Children)
            {
                subItem.Items.Add(CreateNativeMenuItem(child));
            }

            return subItem;
        }

        var item = new WinUIMenuFlyoutItem
        {
            Text = node.Text
        };

        if (node.Action is AppMenuAction action)
        {
            item.Click += (_, _) => _menuActionService.Request(action);
        }
        else
        {
            item.IsEnabled = false;
        }

        return item;
    }

    private sealed record TitleMenuNode(
        string Text,
        AppMenuAction? Action = null,
        IReadOnlyList<TitleMenuNode>? Children = null);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(MainViewModel.LanguagePreference)
            and not nameof(MainViewModel.MenuConnection)
            and not nameof(MainViewModel.MenuManageConnections)
            and not nameof(MainViewModel.MenuAppearance)
            and not nameof(MainViewModel.MenuSystemTheme)
            and not nameof(MainViewModel.MenuLightTheme)
            and not nameof(MainViewModel.MenuDarkTheme)
            and not nameof(MainViewModel.MenuLangZh)
            and not nameof(MainViewModel.MenuLangEn)
            and not nameof(MainViewModel.MenuLanguage)
            and not nameof(MainViewModel.BtnDisconnect)
            and not nameof(MainViewModel.BtnRefresh))
        {
            return;
        }

        if (_window is null)
        {
            return;
        }

        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            _window.TitleBar = BuildWindowsTitleBar();
        });
    }
#endif
}

