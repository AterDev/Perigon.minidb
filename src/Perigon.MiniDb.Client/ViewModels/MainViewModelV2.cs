using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Avalonia;
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

    private CancellationTokenSource? _statusResetCts;
    private List<string> _allTableNames = [];

    /// <summary>
    /// All records from the current table (unfiltered).
    /// </summary>
    private List<DynamicRecord> _allRecords = [];

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
    [NotifyPropertyChangedFor(nameof(IsChinese))]
    [NotifyPropertyChangedFor(nameof(IsEnglish))]
    [NotifyPropertyChangedFor(nameof(ConnectionBadge))]
    [NotifyPropertyChangedFor(nameof(SelectedConnectionDisplay))]
    [NotifyPropertyChangedFor(nameof(SelectedConnectionPathDisplay))]
    [NotifyPropertyChangedFor(nameof(EmptyStateMessage))]
    [NotifyPropertyChangedFor(nameof(TableTitle))]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    [NotifyPropertyChangedFor(nameof(LabelTableCount))]
    [NotifyPropertyChangedFor(nameof(LabelFilteredCount))]
    [NotifyPropertyChangedFor(nameof(LabelRawCount))]
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

    /// <summary>
    /// Records displayed in the DataGrid (filtered view).
    /// DataGrid uses its built-in virtual scrolling — no manual pagination needed.
    /// </summary>
    public ObservableCollection<DynamicRecord> DisplayRecords { get; } = new();

    /// <summary>
    /// Current field names for dynamic DataGrid column generation.
    /// Code-behind listens for changes to regenerate columns.
    /// </summary>
    [ObservableProperty]
    private List<string> _currentFieldNames = [];

    private string ReadyStatus => T(AppStrings.Keys.StatusReady);

    public bool IsDisconnected => !IsConnected;

    public string TableTitle => string.IsNullOrWhiteSpace(SelectedTableName)
        ? T(AppStrings.Keys.LabelNotSelectedTable)
        : Tf(AppStrings.Keys.FormatTableTitle, SelectedTableName);
    public string FilterSummary => Tf(AppStrings.Keys.FormatFilterSummary, DisplayRecords.Count, _allRecords.Count);
    public string ConnectionBadge => IsConnected ? T(AppStrings.Keys.StatusConnected) : T(AppStrings.Keys.StatusDisconnected);
    public string SelectedConnectionDisplay => SelectedConnection?.Name ?? T(AppStrings.Keys.LabelNoSelectedConnection);
    public string SelectedConnectionPathDisplay => SelectedConnection?.Path ?? T(AppStrings.Keys.LabelAddOrSelectConnectionFirst);
    public int TableCount => TableNames.Count;
    public int RawCount => _allRecords.Count;
    public bool HasData => DisplayRecords.Count > 0;
    public bool HasNoData => !HasData;

    public string EmptyStateMessage => string.IsNullOrWhiteSpace(SelectedTableName)
        ? T(AppStrings.Keys.LabelSelectTableToView)
        : T(AppStrings.Keys.LabelNoDataForFilter);

    public string LabelTableCount => Tf(AppStrings.Keys.FormatTableCount, TableCount);
    public string LabelFilteredCount => Tf(AppStrings.Keys.FormatFilteredCount, DisplayRecords.Count);
    public string LabelRawCount => Tf(AppStrings.Keys.FormatRawCount, RawCount);

    public bool IsChinese => !LanguagePreference.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    public bool IsEnglish => LanguagePreference.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public MainViewModelV2()
        : this(new DatabaseConnectionService(), new ClientSettingsService())
    {
    }

    public MainViewModelV2(
        DatabaseConnectionService connectionService,
        ClientSettingsService settingsService)
    {
        _connectionService = connectionService;
        _settingsService = settingsService;

        var settings = _settingsService.Load();
        _themePreference = settings.ThemePreference;
        _isGlassEffectEnabled = settings.EnableGlassEffect;
        _languagePreference = settings.LanguagePreference;

        ApplyCulture(_languagePreference);
        App.SwitchLanguage(_languagePreference);

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
        App.SwitchLanguage(value);
        SetStatus(ReadyStatus, StatusLevel.Neutral, autoReset: false);
        SaveSettings();
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

            var tableNames = MiniDbFileReader.GetTableNames(dbPath, out var tableError);
            if (tableError is not null)
            {
                ShowError(Tf(AppStrings.Keys.MessageConnectionFailed, tableError));
                IsConnected = false;
                return;
            }

            var invalidTables = MiniDbFileReader.ValidateFieldMetadata(dbPath);
            if (invalidTables is not null)
            {
                SelectedConnection.LastConnectionError = T(AppStrings.Keys.MessageUnsupportedFileFormat);
                _connectionService.SaveConnections();
                ShowError(Tf(AppStrings.Keys.MessageUnsupportedFileFormatDetail, invalidTables));
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
        DisplayRecords.Clear();
        _allRecords.Clear();

        SelectedTableName = null;
        SelectedFilterField = null;
        FilterValue = string.Empty;
        IsConnected = false;

        ShowSuccess(T(AppStrings.Keys.MessageDisconnected));

        NotifyDataStateChanged();
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
        Debug.WriteLine($"[MiniDb] LoadTableData called. Connection={SelectedConnection?.Path}, Table={SelectedTableName}");

        if (string.IsNullOrWhiteSpace(SelectedConnection?.Path) || string.IsNullOrWhiteSpace(SelectedTableName))
        {
            Debug.WriteLine("[MiniDb] LoadTableData: skipped (no connection or table)");
            _allRecords.Clear();
            FilterFields.Clear();
            CurrentFieldNames = [];
            RefreshDisplayRecords();
            return;
        }

        try
        {
            var dbPath = Path.GetFullPath(SelectedConnection.Path);
            var tableData = MiniDbFileReader.LoadTableData(dbPath, SelectedTableName, out var error);
            if (error is not null)
            {
                Debug.WriteLine($"[MiniDb] LoadTableData error: {error}");
                ShowError(Tf(AppStrings.Keys.MessageReadTableDataFailed, error));
                return;
            }

            _allRecords = tableData.Records;
            CurrentFieldNames = tableData.FieldNames;

            Debug.WriteLine($"[MiniDb] LoadTableData: loaded {_allRecords.Count} records, {CurrentFieldNames.Count} fields for table '{SelectedTableName}'");
            Debug.WriteLine($"[MiniDb]   Fields: {string.Join(", ", CurrentFieldNames)}");

            FilterFields.Clear();
            foreach (var fieldName in tableData.FieldNames)
            {
                FilterFields.Add(fieldName);
            }

            SelectedFilterField = FilterFields.FirstOrDefault();
            FilterValue = string.Empty;
            RefreshDisplayRecords();

            if (tableData.FallbackReason is not null)
            {
                var reason = tableData.FallbackReason switch
                {
                    "NoFieldMetadata" => T(AppStrings.Keys.MessageNoFieldMetadata),
                    "FieldSizeMismatch" => T(AppStrings.Keys.MessageFieldSizeMismatch),
                    _ => tableData.FallbackReason
                };
                ShowWarning(reason);
            }
            else
            {
                ShowSuccess(Tf(AppStrings.Keys.MessageLoadedTableWithCount, SelectedTableName, _allRecords.Count));
            }
        }
        catch (Exception ex)
        {
            ShowError(Tf(AppStrings.Keys.MessageLoadTableDataFailed, ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyFilter))]
    private void ApplyFilter()
    {
        RefreshDisplayRecords();
        ShowSuccess(Tf(AppStrings.Keys.MessageFilterCompleted, DisplayRecords.Count, _allRecords.Count));
    }

    [RelayCommand(CanExecute = nameof(CanClearFilter))]
    private void ClearFilter()
    {
        FilterValue = string.Empty;
        RefreshDisplayRecords();
        ShowInfo(T(AppStrings.Keys.MessageFilterCleared));
    }

    private void RefreshDisplayRecords()
    {
        var filtered = GetFilteredRecords();

        DisplayRecords.Clear();
        foreach (var record in filtered)
        {
            DisplayRecords.Add(record);
        }

        Debug.WriteLine($"[MiniDb] RefreshDisplayRecords: {DisplayRecords.Count} records in DisplayRecords (filtered from {_allRecords.Count})");

        NotifyDataStateChanged();
        RaiseCommandStates();
    }

    private List<DynamicRecord> GetFilteredRecords()
    {
        if (_allRecords.Count == 0 || string.IsNullOrWhiteSpace(SelectedFilterField) || string.IsNullOrWhiteSpace(FilterValue))
        {
            return _allRecords;
        }

        var searchText = FilterValue.Trim();
        var fieldName = SelectedFilterField;
        return _allRecords.Where(r => r[fieldName].Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void NotifyDataStateChanged()
    {
        OnPropertyChanged(nameof(RawCount));
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

    private static string T(string key)
    {
        if (Application.Current is not { } app) return key;
        return app.Resources.TryGetResource(key, app.ActualThemeVariant, out var val) && val is string s ? s : key;
    }

    private static string Tf(string key, params object[] args)
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


}
