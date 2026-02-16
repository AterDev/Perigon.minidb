using System.ComponentModel;
using System.Collections.Specialized;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Perigon.MiniDb.Client.Services;
using Perigon.MiniDb.Client.ViewModels;

namespace Perigon.MiniDb.Client;

public partial class AppHost : ComponentBase, IDisposable
{
    [Inject] public MainViewModel ViewModel { get; set; } = default!;
    [Inject] public ReflectionTableService ReflectionTableService { get; set; } = default!;

    private string _selectedTableName = string.Empty;
    private List<string> _headers = [];

    private string ThemeCssClass => ViewModel.ThemePreference switch
    {
        "Light" => "theme-light",
        "Dark" => "theme-dark",
        _ => "theme-system"
    };

    private DesignThemeModes CurrentThemeMode => ViewModel.ThemePreference switch
    {
        "Light" => DesignThemeModes.Light,
        "Dark" => DesignThemeModes.Dark,
        _ => DesignThemeModes.System
    };

    protected override void OnInitialized()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.TableNames.CollectionChanged += OnViewModelCollectionChanged;
        ViewModel.PagedItems.CollectionChanged += OnViewModelCollectionChanged;

        ViewModel.SetGlassEffectEnabled(true);
        ApplyTheme(ViewModel.ThemePreference);
        _selectedTableName = ViewModel.SelectedTableName ?? string.Empty;
        UpdateHeaders();
    }


    private void OnSelectedTableChanged(ChangeEventArgs args)
    {
        _selectedTableName = args.Value?.ToString() ?? string.Empty;
        ViewModel.SelectedTableName = string.IsNullOrWhiteSpace(_selectedTableName) ? null : _selectedTableName;
        UpdateHeaders();
    }

    private void OnTableSearchChanged(ChangeEventArgs args)
    {
        ViewModel.TableSearchText = args.Value?.ToString() ?? string.Empty;
        UpdateHeaders();
        InvokeAsync(StateHasChanged);
    }

    private void SelectTable(string tableName)
    {
        if (string.Equals(ViewModel.SelectedTableName, tableName, StringComparison.Ordinal))
        {
            ViewModel.RefreshTableCommand.Execute(null);
            UpdateHeaders();
            InvokeAsync(StateHasChanged);
            return;
        }

        _selectedTableName = tableName;
        ViewModel.SelectedTableName = tableName;
        UpdateHeaders();
        InvokeAsync(StateHasChanged);
    }

    private void OnTableListKeyDown(KeyboardEventArgs args)
    {
        if (ViewModel.TableNames.Count == 0)
        {
            return;
        }

        var currentIndex = ViewModel.TableNames.IndexOf(ViewModel.SelectedTableName ?? string.Empty);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        switch (args.Key)
        {
            case "ArrowUp":
                SelectTable(ViewModel.TableNames[Math.Max(0, currentIndex - 1)]);
                break;
            case "ArrowDown":
                SelectTable(ViewModel.TableNames[Math.Min(ViewModel.TableNames.Count - 1, currentIndex + 1)]);
                break;
        }
    }

    private void OnQuickFilterChanged(ChangeEventArgs args)
    {
        ViewModel.QuickFilterText = args.Value?.ToString() ?? string.Empty;
    }

    private void ApplyFilter()
    {
        ViewModel.ApplyFilterCommand.Execute(null);
        UpdateHeaders();
    }

    private void ClearFilter()
    {
        ViewModel.ClearFilterCommand.Execute(null);
        UpdateHeaders();
    }

    private void FirstPage() => ExecutePageAction(ViewModel.FirstPageCommand);
    private void PrevPage() => ExecutePageAction(ViewModel.PreviousPageCommand);
    private void NextPage() => ExecutePageAction(ViewModel.NextPageCommand);
    private void LastPage() => ExecutePageAction(ViewModel.LastPageCommand);

    private void ExecutePageAction(System.Windows.Input.ICommand command)
    {
        command.Execute(null);
        UpdateHeaders();
    }

    private void OnPageSizeChanged(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out var pageSize))
        {
            ViewModel.PageSize = pageSize;
            UpdateHeaders();
        }
    }

    private void ApplyTheme(string theme)
    {
        ViewModel.SetThemePreference(theme);

        if (Microsoft.Maui.Controls.Application.Current is not null)
        {
            Microsoft.Maui.Controls.Application.Current.UserAppTheme = theme switch
            {
                "Light" => Microsoft.Maui.ApplicationModel.AppTheme.Light,
                "Dark" => Microsoft.Maui.ApplicationModel.AppTheme.Dark,
                _ => Microsoft.Maui.ApplicationModel.AppTheme.Unspecified
            };
        }
    }

    private IReadOnlyList<string> GetRowValues(object row)
    {
        return ReflectionTableService.GetRowValues(row, _headers);
    }

    private void UpdateHeaders()
    {
        _headers = [.. ReflectionTableService.GetHeaders(ViewModel.PagedItems.Cast<object>())];
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedConnection)
            or nameof(MainViewModel.SelectedTableName)
            or nameof(MainViewModel.StatusMessage)
            or nameof(MainViewModel.FilterSummary)
            or nameof(MainViewModel.TableNames)
            or nameof(MainViewModel.PagedItems)
            or nameof(MainViewModel.PageSummary)
            or nameof(MainViewModel.IsGlassEffectEnabled)
            or nameof(MainViewModel.LanguagePreference)
            or nameof(MainViewModel.ThemePreference))
        {
            _selectedTableName = ViewModel.SelectedTableName ?? _selectedTableName;
            UpdateHeaders();
            InvokeAsync(StateHasChanged);
        }
    }

    private void OnViewModelCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHeaders();
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.TableNames.CollectionChanged -= OnViewModelCollectionChanged;
        ViewModel.PagedItems.CollectionChanged -= OnViewModelCollectionChanged;
    }
}
