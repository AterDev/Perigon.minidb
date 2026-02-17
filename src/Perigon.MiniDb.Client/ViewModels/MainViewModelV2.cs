using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Perigon.MiniDb.Client.Models;
using Perigon.MiniDb.Client.Resources.Localization;
using Perigon.MiniDb.Client.Services;

namespace Perigon.MiniDb.Client.ViewModels;

public partial class MainViewModelV2 : ObservableObject
{
    private enum StatusLevel
    {
        Neutral,
        Success,
        Warning,
        Error
    }

    private readonly DatabaseConnectionService _connectionService;
    private readonly ClientSettingsService _settingsService;
    private readonly TableDataStateService _tableDataState;
    private static readonly ResourceManager ResourceManager =
        new("Perigon.MiniDb.Client.Resources.Localization.AppStrings", typeof(MainViewModelV2).Assembly);

    private CancellationTokenSource? _statusResetCts;
    private List<string> _allTableNames = [];
    private static readonly string[] XamlLocalizationKeys =
    [
        AppStrings.Keys.AppTitle,
        AppStrings.Keys.MenuConnection,
        AppStrings.Keys.MenuManageConnections,
        AppStrings.Keys.ButtonConnect,
        AppStrings.Keys.ButtonDisconnect,
        AppStrings.Keys.MenuAppearance,
        AppStrings.Keys.MenuLightTheme,
        AppStrings.Keys.MenuDarkTheme,
        AppStrings.Keys.MenuSystemTheme,
        AppStrings.Keys.MenuLanguage,
        AppStrings.Keys.MenuHelp,
        AppStrings.Keys.MenuOpenRepo,
        AppStrings.Keys.MenuOpenIssues,
        AppStrings.Keys.SectionTables,
        AppStrings.Keys.LabelSearchTableWatermark,
        AppStrings.Keys.LabelFilterWatermark,
        AppStrings.Keys.LabelNoDataTitle,
        AppStrings.Keys.LabelNoConnection,
        AppStrings.Keys.ButtonApply,
        AppStrings.Keys.ButtonClear,
        AppStrings.Keys.ButtonFirstPage,
        AppStrings.Keys.ButtonPrevPage,
        AppStrings.Keys.ButtonNextPage,
        AppStrings.Keys.ButtonLastPage,
        AppStrings.Keys.WindowConnectionManagerTitle,
        AppStrings.Keys.SectionConnectionConfig,
        AppStrings.Keys.LabelSearchConnectionWatermark,
        AppStrings.Keys.LabelConnectionNameWatermark,
        AppStrings.Keys.LabelDbPathWatermark,
        AppStrings.Keys.ButtonBrowse,
        AppStrings.Keys.ButtonAdd,
        AppStrings.Keys.ButtonUpdate,
        AppStrings.Keys.ButtonDelete,
        AppStrings.Keys.ButtonClose
    ];

    private int _pageSize = 25;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedConnectionDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedConnectionPathDisplay))]
    private DatabaseConnection? _selectedConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TableTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateMessage))]
    private string? _selectedTableName;

    [ObservableProperty]
    private string _connectionSearchText = string.Empty;

    [ObservableProperty]
    private string _tableSearchText = string.Empty;

    [ObservableProperty]
    private string? _selectedFilterField;

    [ObservableProperty]
    private string _filterValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyPropertyChangedFor(nameof(ConnectionBadge))]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private bool _isStatusWarning;

    [ObservableProperty]
    private bool _isStatusSuccess;

    [ObservableProperty]
    private string _statusMessage = "准备就绪";

    [ObservableProperty]
    private string _languagePreference = "zh-CN";

    [ObservableProperty]
    private string _newConnectionName = string.Empty;

    [ObservableProperty]
    private string _newConnectionPath = string.Empty;

    [ObservableProperty]
    private bool _isGlassEffectEnabled = true;

    [ObservableProperty]
    private string _themePreference = "System";

    public ObservableCollection<DatabaseConnection> Connections { get; } = new();
    public ObservableCollection<string> TableNames { get; } = new();
    public ObservableCollection<string> FilterFields { get; } = new();
    public ObservableCollection<int> PageSizeOptions { get; } = [10, 25, 50, 100];
    public ObservableCollection<TableDataStateService.RawTableRecord> PagedRecords { get; } = new();

    private string ReadyStatus => T(AppStrings.Keys.StatusReady);

    public bool IsDisconnected => !IsConnected;

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (SetProperty(ref _pageSize, value))
            {
                _tableDataState.SetPageSize(value);
                RefreshPagedItems();
            }
        }
    }

    public int PageIndex => _tableDataState.PageIndex;
    public int TotalCount => _tableDataState.TotalCount;
    public int TotalPages => _tableDataState.TotalPages;
    public string TableTitle => string.IsNullOrWhiteSpace(SelectedTableName)
        ? T(AppStrings.Keys.LabelNotSelectedTable)
        : Tf(AppStrings.Keys.FormatTableTitle, SelectedTableName);
    public string PageSummary => Tf(AppStrings.Keys.FormatPageSummary, PageIndex, TotalPages, PageSize);
    public string FilterSummary => Tf(AppStrings.Keys.FormatFilterSummary, PagedRecords.Count, TotalCount);
    public string ConnectionBadge => IsConnected ? T(AppStrings.Keys.StatusConnected) : T(AppStrings.Keys.StatusDisconnected);
    public string SelectedConnectionDisplay => SelectedConnection?.Name ?? T(AppStrings.Keys.LabelNoSelectedConnection);
    public string SelectedConnectionPathDisplay => SelectedConnection?.Path ?? T(AppStrings.Keys.LabelAddOrSelectConnectionFirst);
    public int TableCount => TableNames.Count;
    public int RawCount => _tableDataState.RawCount;
    public bool HasData => PagedRecords.Count > 0;
    public bool HasNoData => !HasData;

    public string EmptyStateMessage => string.IsNullOrWhiteSpace(SelectedTableName)
        ? T(AppStrings.Keys.LabelSelectTableToView)
        : T(AppStrings.Keys.LabelNoDataForFilter);

    public string LabelTableCount => Tf(AppStrings.Keys.FormatTableCount, TableCount);
    public string LabelFilteredCount => Tf(AppStrings.Keys.FormatFilteredCount, TotalCount);
    public string LabelRawCount => Tf(AppStrings.Keys.FormatRawCount, RawCount);

    public string MenuConnectionText => T(AppStrings.Keys.MenuConnection);
    public string MenuManageConnectionsText => T(AppStrings.Keys.MenuManageConnections);
    public string MenuAppearanceText => T(AppStrings.Keys.MenuAppearance);
    public string MenuLightThemeText => T(AppStrings.Keys.MenuLightTheme);
    public string MenuDarkThemeText => T(AppStrings.Keys.MenuDarkTheme);
    public string MenuSystemThemeText => T(AppStrings.Keys.MenuSystemTheme);
    public string MenuLanguageText => T(AppStrings.Keys.MenuLanguage);
    public string MenuHelpText => T(AppStrings.Keys.MenuHelp);
    public string MenuOpenRepoText => T(AppStrings.Keys.MenuOpenRepo);
    public string MenuOpenIssuesText => T(AppStrings.Keys.MenuOpenIssues);

    public string this[string key] => T(key);

    public bool IsChinese => !LanguagePreference.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    public bool IsEnglish => LanguagePreference.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public MainViewModelV2()
        : this(new DatabaseConnectionService(), new ClientSettingsService(), new TableDataStateService())
    {
    }

    public MainViewModelV2(
        DatabaseConnectionService connectionService,
        ClientSettingsService settingsService,
        TableDataStateService tableDataState)
    {
        _connectionService = connectionService;
        _settingsService = settingsService;
        _tableDataState = tableDataState;

        var settings = _settingsService.Load();
        _themePreference = settings.ThemePreference;
        _isGlassEffectEnabled = settings.EnableGlassEffect;
        _languagePreference = settings.LanguagePreference;

        ApplyCulture(_languagePreference);
        _tableDataState.SetPageSize(_pageSize);

        TableNames.CollectionChanged += OnTableNamesChanged;
        _connectionService.Connections.CollectionChanged += (_, _) => ApplyConnectionFilter();

        ApplyConnectionFilter();
        SetStatus(ReadyStatus, StatusLevel.Neutral, autoReset: false);

        SelectedConnection = _connectionService.GetMostRecentlyUsedConnection() ?? Connections.FirstOrDefault();
    }

    partial void OnSelectedConnectionChanged(DatabaseConnection? value)
    {
        if (IsConnected)
        {
            Disconnect();
        }

        if (value is not null)
        {
            NewConnectionName = value.Name;
            NewConnectionPath = value.Path;
        }
        RaiseCommandStates();
    }

    partial void OnSelectedTableNameChanged(string? value)
    {
        LoadTableData();
        RaiseCommandStates();
    }

    partial void OnConnectionSearchTextChanged(string value)
    {
        ApplyConnectionFilter();
    }

    partial void OnTableSearchTextChanged(string value)
    {
        ApplyTableFilter();
    }

    partial void OnLanguagePreferenceChanged(string value)
    {
        ApplyCulture(value);
        RaiseLocalizationChanged();
        SaveSettings();
    }

    partial void OnSelectedFilterFieldChanged(string? value)
    {
        _tableDataState.SetFilterField(value);
    }

    partial void OnFilterValueChanged(string value)
    {
        _tableDataState.SetFilterValue(value);
    }

    partial void OnNewConnectionNameChanged(string value)
    {
        RaiseCommandStates();
    }

    partial void OnNewConnectionPathChanged(string value)
    {
        RaiseCommandStates();
    }

    partial void OnIsGlassEffectEnabledChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnThemePreferenceChanged(string value)
    {
        SaveSettings();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        RaiseCommandStates();
    }

    partial void OnStatusMessageChanged(string value)
    {
        ScheduleStatusReset(value);
    }

    private async void ScheduleStatusReset(string message)
    {
        if (string.Equals(message, ReadyStatus, StringComparison.Ordinal))
        {
            return;
        }

        _statusResetCts?.Cancel();
        _statusResetCts?.Dispose();

        _statusResetCts = new CancellationTokenSource();
        var token = _statusResetCts.Token;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            SetStatus(ReadyStatus, StatusLevel.Neutral, autoReset: false);
        }
    }

    public void SetThemePreference(string preference)
    {
        ThemePreference = preference;
    }

    public void SetGlassEffectEnabled(bool enabled)
    {
        IsGlassEffectEnabled = enabled;
    }

    public void SetLanguagePreference(string preference)
    {
        LanguagePreference = preference;
        ShowSuccess(T(AppStrings.Keys.StatusLanguageChanged));
    }

    public string Localize(string key)
    {
        return T(key);
    }

    public string LocalizeFormat(string key, params object[] args)
    {
        return Tf(key, args);
    }

    public void ShowInfo(string message)
    {
        SetStatus(message, StatusLevel.Neutral);
    }

    public void ShowSuccess(string message)
    {
        SetStatus(message, StatusLevel.Success);
    }

    public void ShowWarning(string message)
    {
        SetStatus(message, StatusLevel.Warning);
    }

    public void ShowError(string message)
    {
        SetStatus(message, StatusLevel.Error);
    }

    public void ResetViewPreferences()
    {
        ThemePreference = "System";
        IsGlassEffectEnabled = true;
        LanguagePreference = "zh-CN";
        ConnectionSearchText = string.Empty;
        TableSearchText = string.Empty;
        PageSize = 25;
    }

    public bool OpenSelectedConnection()
    {
        if (SelectedConnection is null)
        {
            ShowWarning(T(AppStrings.Keys.MessageSelectConnectionFirst));
            return false;
        }

        Connect();
        return IsConnected;
    }

    private bool CanAddConnection() =>
        !string.IsNullOrWhiteSpace(NewConnectionName) && !string.IsNullOrWhiteSpace(NewConnectionPath);

    private bool CanEditConnection() => SelectedConnection is not null && CanAddConnection();
    private bool CanDeleteConnection() => SelectedConnection is not null;
    private bool CanConnect() => SelectedConnection is not null && !IsConnected;
    private bool CanDisconnect() => IsConnected;
    private bool CanRefreshTable() => IsConnected && !string.IsNullOrWhiteSpace(SelectedTableName);
    private bool CanApplyFilter() => CanRefreshTable();
    private bool CanClearFilter() => CanRefreshTable();
    private bool CanFirstPage() => _tableDataState.CanFirstPage();
    private bool CanPreviousPage() => _tableDataState.CanPreviousPage();
    private bool CanNextPage() => _tableDataState.CanNextPage();
    private bool CanLastPage() => _tableDataState.CanLastPage();

    [RelayCommand(CanExecute = nameof(CanAddConnection))]
    private void AddConnection()
    {
        try
        {
            var connection = new DatabaseConnection
            {
                Name = NewConnectionName.Trim(),
                Path = Path.GetFullPath(NewConnectionPath.Trim())
            };

            _connectionService.AddConnection(connection);
            SelectedConnection = connection;
            ShowSuccess(Tf(AppStrings.Keys.MessageConnectionAdded, connection.Name));
        }
        catch (Exception ex)
        {
            ShowError(Tf(AppStrings.Keys.MessageAddConnectionFailed, ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditConnection))]
    private void EditConnection()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        try
        {
            var updated = new DatabaseConnection
            {
                Name = NewConnectionName.Trim(),
                Path = Path.GetFullPath(NewConnectionPath.Trim()),
                LastConnectedAt = SelectedConnection.LastConnectedAt,
                LastConnectionError = SelectedConnection.LastConnectionError
            };

            _connectionService.UpdateConnection(SelectedConnection, updated);
            SelectedConnection = updated;
            ShowSuccess(Tf(AppStrings.Keys.MessageConnectionUpdated, updated.Name));
        }
        catch (Exception ex)
        {
            ShowError(Tf(AppStrings.Keys.MessageUpdateConnectionFailed, ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteConnection))]
    private void DeleteConnection()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        try
        {
            var name = SelectedConnection.Name;
            _connectionService.RemoveConnection(SelectedConnection);
            SelectedConnection = Connections.FirstOrDefault();
            ShowSuccess(Tf(AppStrings.Keys.MessageConnectionDeleted, name));
        }
        catch (Exception ex)
        {
            ShowError(Tf(AppStrings.Keys.MessageDeleteConnectionFailed, ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        try
        {
            var dbPath = Path.GetFullPath(SelectedConnection.Path);
            if (!File.Exists(dbPath))
            {
                SelectedConnection.LastConnectionError = T(AppStrings.Keys.MessageDatabaseFileNotFound);
                _connectionService.SaveConnections();
                ShowWarning(Tf(AppStrings.Keys.MessageDatabaseFileNotFoundWithPath, dbPath));
                return;
            }

            var tableNames = TableDataStateService.GetTableNamesFromFile(dbPath, out var tableError);
            if (tableError is not null)
            {
                ShowError(Tf(AppStrings.Keys.MessageConnectionFailed, tableError));
                IsConnected = false;
                return;
            }

            IsConnected = true;
            SelectedConnection.LastConnectedAt = DateTime.Now;
            SelectedConnection.LastConnectionError = null;
            _connectionService.SaveConnections();

            LoadTableNames(tableNames);
            ShowSuccess(Tf(AppStrings.Keys.MessageConnectedTo, SelectedConnection.Name));
        }
        catch (Exception ex)
        {
            SelectedConnection.LastConnectionError = ex.Message;
            _connectionService.SaveConnections();
            ShowError(Tf(AppStrings.Keys.MessageConnectionFailed, ex.Message));
            IsConnected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        TableNames.Clear();
        _allTableNames.Clear();
        FilterFields.Clear();
        PagedRecords.Clear();

        _tableDataState.Reset();
        _tableDataState.SetPageSize(PageSize);

        SelectedTableName = null;
        SelectedFilterField = null;
        FilterValue = string.Empty;
        IsConnected = false;

        ShowSuccess(T(AppStrings.Keys.MessageDisconnected));

        NotifyPagingStateChanged();
    }

    private void LoadTableNames(List<string> tableNames)
    {
        _allTableNames = tableNames;
        ApplyTableFilter();

        SelectedTableName = TableNames.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshTable))]
    private void RefreshTable()
    {
        LoadTableData();
    }

    private void LoadTableData()
    {
        if (string.IsNullOrWhiteSpace(SelectedConnection?.Path) || string.IsNullOrWhiteSpace(SelectedTableName))
        {
            _tableDataState.Reset();
            _tableDataState.SetPageSize(PageSize);
            FilterFields.Clear();
            RefreshPagedItems();
            return;
        }

        try
        {
            var dbPath = Path.GetFullPath(SelectedConnection.Path);
            if (!_tableDataState.TryLoadRawTable(dbPath, SelectedTableName, out var error))
            {
                ShowError(Tf(AppStrings.Keys.MessageReadTableDataFailed, error ?? string.Empty));
                return;
            }

            FilterFields.Clear();
            foreach (var field in _tableDataState.FilterFields)
            {
                FilterFields.Add(field);
            }

            SelectedFilterField = _tableDataState.SelectedFilterField;
            FilterValue = string.Empty;
            RefreshPagedItems();

            ShowSuccess(Tf(AppStrings.Keys.MessageLoadedTableWithCount, SelectedTableName, RawCount));
        }
        catch (Exception ex)
        {
            ShowError(Tf(AppStrings.Keys.MessageLoadTableDataFailed, ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyFilter))]
    private void ApplyFilter()
    {
        if (RawCount == 0)
        {
            return;
        }

        _tableDataState.SetFilterField(SelectedFilterField);
        _tableDataState.SetFilterValue(FilterValue);
        _tableDataState.ApplyFilter();

        RefreshPagedItems();
        ShowSuccess(Tf(AppStrings.Keys.MessageFilterCompleted, TotalCount, RawCount));
    }

    [RelayCommand(CanExecute = nameof(CanClearFilter))]
    private void ClearFilter()
    {
        FilterValue = string.Empty;
        _tableDataState.ClearFilter();

        RefreshPagedItems();
        ShowInfo(T(AppStrings.Keys.MessageFilterCleared));
    }

    [RelayCommand(CanExecute = nameof(CanFirstPage))]
    private void FirstPage()
    {
        if (_tableDataState.GoFirstPage())
        {
            RefreshPagedItems();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private void PreviousPage()
    {
        if (_tableDataState.GoPreviousPage())
        {
            RefreshPagedItems();
        }
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private void NextPage()
    {
        if (_tableDataState.GoNextPage())
        {
            RefreshPagedItems();
        }
    }

    [RelayCommand(CanExecute = nameof(CanLastPage))]
    private void LastPage()
    {
        if (_tableDataState.GoLastPage())
        {
            RefreshPagedItems();
        }
    }

    private void RefreshPagedItems()
    {
        var pageItems = _tableDataState.GetCurrentPageItems();

        PagedRecords.Clear();
        foreach (var item in pageItems)
        {
            if (item is TableDataStateService.RawTableRecord record)
            {
                PagedRecords.Add(record);
            }
        }

        NotifyPagingStateChanged();

        RaiseCommandStates();
    }

    private void NotifyPagingStateChanged()
    {
        OnPropertyChanged(nameof(PageIndex));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(RawCount));
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasNoData));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(LabelFilteredCount));
        OnPropertyChanged(nameof(LabelRawCount));
    }

    private void RaiseCommandStates()
    {
        AddConnectionCommand.NotifyCanExecuteChanged();
        EditConnectionCommand.NotifyCanExecuteChanged();
        DeleteConnectionCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        RefreshTableCommand.NotifyCanExecuteChanged();
        ApplyFilterCommand.NotifyCanExecuteChanged();
        ClearFilterCommand.NotifyCanExecuteChanged();
        FirstPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }

    private void OnTableNamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TableCount));
        OnPropertyChanged(nameof(LabelTableCount));
    }

    private void SaveSettings()
    {
        _settingsService.Save(new ClientSettings
        {
            ThemePreference = ThemePreference,
            EnableGlassEffect = IsGlassEffectEnabled,
            LanguagePreference = LanguagePreference
        });
    }

    private void ApplyConnectionFilter()
    {
        var selected = SelectedConnection;

        var filtered = _connectionService.Connections
            .Where(connection => string.IsNullOrWhiteSpace(ConnectionSearchText)
                || connection.Name.Contains(ConnectionSearchText, StringComparison.OrdinalIgnoreCase)
                || connection.Path.Contains(ConnectionSearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Connections.Clear();
        foreach (var connection in filtered)
        {
            Connections.Add(connection);
        }

        if (selected is not null && Connections.Contains(selected))
        {
            SelectedConnection = selected;
        }
        else if (!IsConnected && SelectedConnection is not null && !Connections.Contains(SelectedConnection))
        {
            SelectedConnection = Connections.FirstOrDefault();
        }
        else if (!IsConnected && SelectedConnection is null)
        {
            SelectedConnection = Connections.FirstOrDefault();
        }
    }

    private void ApplyTableFilter()
    {
        var selected = SelectedTableName;

        var filtered = _allTableNames
            .Where(name => string.IsNullOrWhiteSpace(TableSearchText)
                || name.Contains(TableSearchText, StringComparison.OrdinalIgnoreCase))
            .ToList();

        TableNames.Clear();
        foreach (var table in filtered)
        {
            TableNames.Add(table);
        }

        if (!string.IsNullOrWhiteSpace(selected) && TableNames.Contains(selected))
        {
            SelectedTableName = selected;
        }
        else if (!string.IsNullOrWhiteSpace(SelectedTableName) && !TableNames.Contains(SelectedTableName))
        {
            SelectedTableName = TableNames.FirstOrDefault();
        }
    }

    private void SetStatus(string message, StatusLevel level, bool autoReset = true)
    {
        StatusMessage = message;
        IsStatusError = level == StatusLevel.Error;
        IsStatusWarning = level == StatusLevel.Warning;
        IsStatusSuccess = level == StatusLevel.Success;

        if (!autoReset)
        {
            _statusResetCts?.Cancel();
        }
    }

    private string T(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    private string Tf(string key, params object[] args)
    {
        var format = T(key);
        return string.Format(CultureInfo.CurrentCulture, format, args);
    }

    private static void ApplyCulture(string preference)
    {
        var cultureName = string.IsNullOrWhiteSpace(preference) ? "zh-CN" : preference;
        var culture = new CultureInfo(cultureName);

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private void RaiseLocalizationChanged()
    {
        OnPropertyChanged("Item[]");
        foreach (var key in XamlLocalizationKeys)
        {
            OnPropertyChanged($"Item[{key}]");
        }
        OnPropertyChanged(nameof(MenuConnectionText));
        OnPropertyChanged(nameof(MenuManageConnectionsText));
        OnPropertyChanged(nameof(MenuAppearanceText));
        OnPropertyChanged(nameof(MenuLightThemeText));
        OnPropertyChanged(nameof(MenuDarkThemeText));
        OnPropertyChanged(nameof(MenuSystemThemeText));
        OnPropertyChanged(nameof(MenuLanguageText));
        OnPropertyChanged(nameof(MenuHelpText));
        OnPropertyChanged(nameof(MenuOpenRepoText));
        OnPropertyChanged(nameof(MenuOpenIssuesText));
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(ConnectionBadge));
        OnPropertyChanged(nameof(SelectedConnectionDisplay));
        OnPropertyChanged(nameof(SelectedConnectionPathDisplay));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(TableTitle));
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(LabelTableCount));
        OnPropertyChanged(nameof(LabelFilteredCount));
        OnPropertyChanged(nameof(LabelRawCount));
        SetStatus(ReadyStatus, StatusLevel.Neutral, autoReset: false);
    }
}
