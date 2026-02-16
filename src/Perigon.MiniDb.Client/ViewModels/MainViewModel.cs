using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Input;
using Perigon.MiniDb.Client.Helpers;
using Perigon.MiniDb.Client.Models;
using Perigon.MiniDb.Client.Services;

namespace Perigon.MiniDb.Client.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly DatabaseConnectionService _connectionService;
    private readonly ClientSettingsService _settingsService;
    private readonly ConnectionSessionService _connectionSessionService;
    private readonly EntityQueryService _entityQueryService;
    private readonly CollectionFilterService _collectionFilterService;
    private readonly LocalizationService _localizationService;
    private readonly StatusToneService _statusToneService;
    private readonly FilterConditionService _filterConditionService;
    private readonly RelayCommand _addConnectionCommand;
    private readonly RelayCommand _editConnectionCommand;
    private readonly RelayCommand _deleteConnectionCommand;
    private readonly RelayCommand _connectCommand;
    private readonly RelayCommand _disconnectCommand;
    private readonly RelayCommand _refreshTableCommand;
    private readonly RelayCommand _applyFilterCommand;
    private readonly RelayCommand _clearFilterCommand;
    private readonly RelayCommand _addFilterConditionCommand;
    private readonly RelayCommand _removeFilterConditionCommand;
    private readonly RelayCommand _firstPageCommand;
    private readonly RelayCommand _previousPageCommand;
    private readonly RelayCommand _nextPageCommand;
    private readonly RelayCommand _lastPageCommand;
    private CancellationTokenSource? _statusResetCts;

    private DatabaseConnection? _selectedConnection;
    private string? _selectedTableName;
    private string _connectionSearchText = string.Empty;
    private string _tableSearchText = string.Empty;
    private bool _isConnected;
    private string _statusMessage = "准备就绪";
    private bool _isStatusError;
    private bool _isStatusWarning;
    private bool _isStatusSuccess;
    private string _languagePreference = "zh-CN";
    private string _newConnectionName = string.Empty;
    private string _newConnectionPath = string.Empty;
    private bool _isGlassEffectEnabled = true;
    private string _themePreference = "System";
    private string _developerDiagnostics = string.Empty;
    private readonly bool _isDeveloperDiagnosticsEnabled = IsDeveloperDiagnosticsEnabledByDefault();

    private List<string> _allTableNames = [];
    private List<object> _allTableEntities = [];
    private List<object> _filteredEntities = [];

    private string? _selectedFilterField;
    private string _selectedFilterOperator = "Contains";
    private FilterOperatorOption? _selectedFilterOperatorOption;
    private string _filterValue = string.Empty;
    private string _filterValueTo = string.Empty;
    private string _quickFilterText = string.Empty;
    private FilterCondition? _selectedFilterCondition;

    private int _pageSize = 25;
    private int _pageIndex = 1;

    public ObservableCollection<DatabaseConnection> Connections { get; } = new();
    public ObservableCollection<string> TableNames { get; } = new();
    public ObservableCollection<string> FilterFields { get; } = new();
    public ObservableCollection<FilterOperatorOption> FilterOperators { get; } = new();

    public ObservableCollection<int> PageSizeOptions { get; } = [10, 25, 50, 100];
    public ObservableCollection<object> PagedItems { get; } = new();
    public ObservableCollection<FilterCondition> FilterConditions { get; } = new();

    public DatabaseConnection? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (_selectedConnection == value)
            {
                return;
            }

            if (IsConnected)
            {
                Disconnect();
            }

            _selectedConnection = value;
            if (value is not null)
            {
                NewConnectionName = value.Name;
                NewConnectionPath = value.Path;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedConnectionDisplay));
            OnPropertyChanged(nameof(SelectedConnectionPathDisplay));
            RaiseCommandStates();
        }
    }

    public string? SelectedTableName
    {
        get => _selectedTableName;
        set
        {
            if (_selectedTableName == value)
            {
                return;
            }

            _selectedTableName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TableTitle));
            OnPropertyChanged(nameof(EmptyStateMessage));

            LoadTableData();
            RaiseCommandStates();
        }
    }

    public string ConnectionSearchText
    {
        get => _connectionSearchText;
        set
        {
            if (_connectionSearchText == value)
            {
                return;
            }

            _connectionSearchText = value;
            OnPropertyChanged();
            ApplyConnectionFilter();
        }
    }

    public string TableSearchText
    {
        get => _tableSearchText;
        set
        {
            if (_tableSearchText == value)
            {
                return;
            }

            _tableSearchText = value;
            OnPropertyChanged();
            ApplyTableFilter();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDisconnected));
            OnPropertyChanged(nameof(ConnectionBadge));
            RaiseCommandStates();
        }
    }

    public bool IsDisconnected => !IsConnected;

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
            UpdateStatusTone(value);
            ScheduleStatusReset(value);
        }
    }

    private string ReadyStatus => L("准备就绪", "Ready.");

    public bool IsStatusError
    {
        get => _isStatusError;
        private set
        {
            if (_isStatusError == value)
            {
                return;
            }

            _isStatusError = value;
            OnPropertyChanged();
        }
    }

    public bool IsStatusWarning
    {
        get => _isStatusWarning;
        private set
        {
            if (_isStatusWarning == value)
            {
                return;
            }

            _isStatusWarning = value;
            OnPropertyChanged();
        }
    }

    public bool IsStatusSuccess
    {
        get => _isStatusSuccess;
        private set
        {
            if (_isStatusSuccess == value)
            {
                return;
            }

            _isStatusSuccess = value;
            OnPropertyChanged();
        }
    }

    public string NewConnectionName
    {
        get => _newConnectionName;
        set
        {
            if (_newConnectionName == value)
            {
                return;
            }

            _newConnectionName = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public string NewConnectionPath
    {
        get => _newConnectionPath;
        set
        {
            if (_newConnectionPath == value)
            {
                return;
            }

            _newConnectionPath = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public bool IsGlassEffectEnabled
    {
        get => _isGlassEffectEnabled;
        set
        {
            if (_isGlassEffectEnabled == value)
            {
                return;
            }

            _isGlassEffectEnabled = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string LanguagePreference
    {
        get => _languagePreference;
        set
        {
            if (_languagePreference == value)
            {
                return;
            }

            _languagePreference = value;
            OnPropertyChanged();
            RaiseLocalizationChanged();
            SaveSettings();
        }
    }

    public string ThemePreference
    {
        get => _themePreference;
        set
        {
            if (_themePreference == value)
            {
                return;
            }

            _themePreference = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string? SelectedFilterField
    {
        get => _selectedFilterField;
        set
        {
            if (_selectedFilterField == value)
            {
                return;
            }

            _selectedFilterField = value;
            OnPropertyChanged();
        }
    }

    public string SelectedFilterOperator
    {
        get => _selectedFilterOperator;
        set
        {
            if (_selectedFilterOperator == value)
            {
                return;
            }

            _selectedFilterOperator = value;
            OnPropertyChanged();

            var selected = FilterOperators.FirstOrDefault(option => option.Key == value);
            if (selected is not null && !ReferenceEquals(_selectedFilterOperatorOption, selected))
            {
                _selectedFilterOperatorOption = selected;
                OnPropertyChanged(nameof(SelectedFilterOperatorOption));
            }
        }
    }

    public FilterOperatorOption? SelectedFilterOperatorOption
    {
        get => _selectedFilterOperatorOption;
        set
        {
            if (ReferenceEquals(_selectedFilterOperatorOption, value))
            {
                return;
            }

            _selectedFilterOperatorOption = value;
            OnPropertyChanged();

            if (value is not null && value.Key != SelectedFilterOperator)
            {
                SelectedFilterOperator = value.Key;
            }
        }
    }

    public string FilterValue
    {
        get => _filterValue;
        set
        {
            if (_filterValue == value)
            {
                return;
            }

            _filterValue = value;
            OnPropertyChanged();
        }
    }

    public string FilterValueTo
    {
        get => _filterValueTo;
        set
        {
            if (_filterValueTo == value)
            {
                return;
            }

            _filterValueTo = value;
            OnPropertyChanged();
        }
    }

    public string QuickFilterText
    {
        get => _quickFilterText;
        set
        {
            if (_quickFilterText == value)
            {
                return;
            }

            _quickFilterText = value;
            OnPropertyChanged();
        }
    }

    public FilterCondition? SelectedFilterCondition
    {
        get => _selectedFilterCondition;
        set
        {
            if (_selectedFilterCondition == value)
            {
                return;
            }

            _selectedFilterCondition = value;
            OnPropertyChanged();

            if (value is not null)
            {
                SelectedFilterField = value.Field;
                SelectedFilterOperator = value.Operator;
                SelectedFilterOperatorOption = FilterOperators.FirstOrDefault(option => option.Key == value.Operator);
                FilterValue = value.Value;
                FilterValueTo = value.ValueTo;
            }

            RaiseCommandStates();
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (_pageSize == value)
            {
                return;
            }

            _pageSize = value;
            OnPropertyChanged();

            _pageIndex = 1;
            ApplyPagination();
        }
    }

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (_pageIndex == value)
            {
                return;
            }

            _pageIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageSummary));
            RaiseCommandStates();
        }
    }

    public int TotalCount => _filteredEntities.Count;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize)));
    public string TableTitle => string.IsNullOrWhiteSpace(SelectedTableName) ? "未选择表" : $"表：{SelectedTableName}";
    public string PageSummary => $"第 {PageIndex}/{TotalPages} 页，每页 {PageSize} 条";
    public string FilterSummary => $"显示 {PagedItems.Count} 条（筛选后 {TotalCount} 条）";
    public string ConditionSummary => FilterConditions.Count == 0
        ? L("当前无筛选条件", "No filter conditions")
        : L($"已添加 {FilterConditions.Count} 个筛选条件（AND）", $"{FilterConditions.Count} AND conditions added");
    public string ConnectionBadge => IsConnected ? L("已连接", "Connected") : L("未连接", "Disconnected");
    public string SelectedConnectionDisplay => SelectedConnection?.Name ?? L("未选择连接", "No connection selected");
    public string SelectedConnectionPathDisplay => SelectedConnection?.Path ?? L("请先添加或选择连接", "Add or select a connection first");
    public int TableCount => TableNames.Count;
    public int RawCount => _allTableEntities.Count;
    public bool HasData => PagedItems.Count > 0;
    public bool HasNoData => !HasData;
    public string EmptyStateMessage => string.IsNullOrWhiteSpace(SelectedTableName)
        ? L("选择左侧表后查看数据", "Select a table on the left to view data")
        : L("当前筛选条件下无数据", "No data for current filter conditions");
    public string AppTitle => L("Perigon MiniDB 管理器", "Perigon MiniDB Manager");
    public string MenuHelp => L("帮助", "Help");
    public string MenuConnection => L("连接", "Connection");
    public string MenuManageConnections => L("管理连接...", "Manage connections...");
    public string MenuAppearance => L("外观", "Appearance");
    public string MenuLightTheme => L("浅色", "Light");
    public string MenuDarkTheme => L("深色", "Dark");
    public string MenuSystemTheme => L("跟随系统", "Follow system");
    public string MenuToggleGlass => L("启用毛玻璃", "Enable glass effect");
    public string MenuOpenRepo => L("打开仓库地址", "Open repository");
    public string MenuOpenIssues => L("打开问题反馈", "Open issues");
    public string MenuLanguage => L("语言", "Language");
    public string MenuLangZh => "中文";
    public string MenuLangEn => "English";
    public bool IsChinese => !_localizationService.IsEnglish(LanguagePreference);
    public bool IsEnglish => _localizationService.IsEnglish(LanguagePreference);
    public string SectionConnectionConfig => L("连接配置", "Connection Config");
    public string SectionSessionActions => L("会话操作", "Session Actions");
    public string SectionAppearance => L("外观", "Appearance");
    public string SectionConnectionsAndTables => L("连接与表", "Connections & Tables");
    public string SectionTables => L("表", "Tables");
    public string LabelTableCount => L($"共 {TableCount} 张表", $"{TableCount} tables");
    public string LabelFilteredCount => L($"筛选后 {TotalCount}", $"Filtered {TotalCount}");
    public string LabelRawCount => L($"原始 {RawCount} 条", $"Raw {RawCount}");
    public string LabelNoDataTitle => L("暂无可显示数据", "No data to display");
    public string LabelConnectionNameWatermark => L("连接名称", "Connection name");
    public string LabelDbPathWatermark => L("数据库文件路径 (*.mds)", "Database file path (*.mds)");
    public string LabelSearchConnectionWatermark => L("搜索连接", "Search connections");
    public string LabelSearchTableWatermark => L("搜索表", "Search tables");
    public string LabelFilterValueWatermark => L("筛选值", "Filter value");
    public string LabelFilterValueToWatermark => L("区间上限(仅Between)", "Upper bound (Between)");
    public string LabelGridFilterWatermark => L("输入关键字（匹配整行）", "Type keyword (match entire row)");
    public string BtnBrowse => L("浏览文件", "Browse");
    public string BtnAdd => L("添加", "Add");
    public string BtnUpdate => L("更新", "Update");
    public string BtnDelete => L("删除", "Delete");
    public string BtnConnect => L("连接", "Connect");
    public string BtnDisconnect => L("断开", "Disconnect");
    public string BtnCreateSample => L("创建示例库", "Create sample DB");
    public string BtnRefresh => L("刷新", "Refresh");
    public string BtnLight => L("浅色", "Light");
    public string BtnDark => L("深色", "Dark");
    public string BtnSystem => L("系统", "System");
    public string BtnResetView => L("重置视图", "Reset view");
    public string BtnAddCondition => L("添加条件", "Add condition");
    public string BtnRemoveCondition => L("移除条件", "Remove condition");
    public string BtnApply => L("查询", "Apply");
    public string BtnClear => L("清空", "Clear");
    public string BtnFirstPage => L("首页", "First");
    public string BtnPrevPage => L("上一页", "Prev");
    public string BtnNextPage => L("下一页", "Next");
    public string BtnLastPage => L("末页", "Last");
    public string ToggleGlass => L("启用毛玻璃", "Enable glass effect");
    public string LabelNoConnection => L("请先在“连接 > 管理连接”中打开数据库", "Open a database from Connection > Manage connections first");
    public bool IsDeveloperDiagnosticsEnabled => _isDeveloperDiagnosticsEnabled;
    public string DeveloperDiagnostics
    {
        get => _developerDiagnostics;
        private set
        {
            if (_developerDiagnostics == value)
            {
                return;
            }

            _developerDiagnostics = value;
            OnPropertyChanged();
        }
    }

    public ICommand AddConnectionCommand => _addConnectionCommand;
    public ICommand EditConnectionCommand => _editConnectionCommand;
    public ICommand DeleteConnectionCommand => _deleteConnectionCommand;
    public ICommand ConnectCommand => _connectCommand;
    public ICommand DisconnectCommand => _disconnectCommand;
    public ICommand RefreshTableCommand => _refreshTableCommand;
    public ICommand ApplyFilterCommand => _applyFilterCommand;
    public ICommand ClearFilterCommand => _clearFilterCommand;
    public ICommand AddFilterConditionCommand => _addFilterConditionCommand;
    public ICommand RemoveFilterConditionCommand => _removeFilterConditionCommand;
    public ICommand FirstPageCommand => _firstPageCommand;
    public ICommand PreviousPageCommand => _previousPageCommand;
    public ICommand NextPageCommand => _nextPageCommand;
    public ICommand LastPageCommand => _lastPageCommand;

    public MainViewModel()
        : this(new DatabaseConnectionService(), new ClientSettingsService(), new ConnectionSessionService(), new EntityQueryService(), new CollectionFilterService(), new LocalizationService(), new StatusToneService(), new FilterConditionService(new LocalizationService()))
    {
    }

    public MainViewModel(
        DatabaseConnectionService connectionService,
        ClientSettingsService settingsService,
        ConnectionSessionService connectionSessionService,
        EntityQueryService entityQueryService,
        CollectionFilterService collectionFilterService,
        LocalizationService localizationService,
        StatusToneService statusToneService,
        FilterConditionService filterConditionService)
    {
        _connectionService = connectionService;
        _settingsService = settingsService;
        _connectionSessionService = connectionSessionService;
        _entityQueryService = entityQueryService;
        _collectionFilterService = collectionFilterService;
        _localizationService = localizationService;
        _statusToneService = statusToneService;
        _filterConditionService = filterConditionService;

        var settings = _settingsService.Load();
        _themePreference = settings.ThemePreference;
        _isGlassEffectEnabled = settings.EnableGlassEffect;
        _languagePreference = settings.LanguagePreference;

        _addConnectionCommand = new RelayCommand(_ => AddConnection(), _ => CanAddOrUpdateConnection());
        _editConnectionCommand = new RelayCommand(_ => EditConnection(), _ => SelectedConnection is not null && CanAddOrUpdateConnection());
        _deleteConnectionCommand = new RelayCommand(_ => DeleteConnection(), _ => SelectedConnection is not null);
        _connectCommand = new RelayCommand(_ => Connect(), _ => SelectedConnection is not null && !IsConnected);
        _disconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
        _refreshTableCommand = new RelayCommand(_ => LoadTableData(), _ => IsConnected && !string.IsNullOrWhiteSpace(SelectedTableName));
        _applyFilterCommand = new RelayCommand(_ => ApplyFilter(), _ => IsConnected && !string.IsNullOrWhiteSpace(SelectedTableName));
        _clearFilterCommand = new RelayCommand(_ => ClearFilter(), _ => IsConnected && !string.IsNullOrWhiteSpace(SelectedTableName));
        _addFilterConditionCommand = new RelayCommand(_ => AddFilterCondition(), _ => IsConnected && !string.IsNullOrWhiteSpace(SelectedTableName));
        _removeFilterConditionCommand = new RelayCommand(_ => RemoveFilterCondition(), _ => FilterConditions.Count > 0);
        _firstPageCommand = new RelayCommand(_ => GoFirstPage(), _ => PageIndex > 1);
        _previousPageCommand = new RelayCommand(_ => GoPreviousPage(), _ => PageIndex > 1);
        _nextPageCommand = new RelayCommand(_ => GoNextPage(), _ => PageIndex < TotalPages);
        _lastPageCommand = new RelayCommand(_ => GoLastPage(), _ => PageIndex < TotalPages);

        TableNames.CollectionChanged += OnTableNamesChanged;
        FilterConditions.CollectionChanged += OnFilterConditionsChanged;
        _connectionService.Connections.CollectionChanged += (_, _) => ApplyConnectionFilter();

        BuildFilterOperatorOptions();
        ApplyConnectionFilter();

        _statusMessage = ReadyStatus;
        UpdateStatusTone(_statusMessage);

        SelectedConnection = _connectionService.GetMostRecentlyUsedConnection() ?? Connections.FirstOrDefault();
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
            StatusMessage = ReadyStatus;
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
        UpdateDeveloperDiagnostics(SelectedTableName);
        StatusMessage = L("语言已切换", "Language changed");
    }

    public string Localize(string zh, string en)
    {
        return L(zh, en);
    }

    public string LocalizeFormat(string zhFormat, string enFormat, params object[] args)
    {
        var format = L(zhFormat, enFormat);
        return string.Format(CultureInfo.CurrentCulture, format, args);
    }

    public void ResetViewPreferences()
    {
        ThemePreference = "System";
        IsGlassEffectEnabled = true;
        LanguagePreference = "zh-CN";
        ConnectionSearchText = string.Empty;
        TableSearchText = string.Empty;
        QuickFilterText = string.Empty;
        PageSize = 25;
    }

    public bool OpenSelectedConnection()
    {
        if (SelectedConnection is null)
        {
            StatusMessage = Localize("请先选择一个连接。", "Please select a connection first.");
            return false;
        }

        Connect();
        return IsConnected;
    }

    public void SelectConnection(DatabaseConnection? connection)
    {
        SelectedConnection = connection;
    }

    public DatabaseConnection? FindConnectionByName(string name)
    {
        return Connections.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanAddOrUpdateConnection()
    {
        return !string.IsNullOrWhiteSpace(NewConnectionName) && !string.IsNullOrWhiteSpace(NewConnectionPath);
    }

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
            StatusMessage = L($"连接 '{connection.Name}' 已添加。", $"Connection '{connection.Name}' added.");
        }
        catch (Exception ex)
        {
            StatusMessage = L($"添加连接失败：{ex.Message}", $"Failed to add connection: {ex.Message}");
        }
    }

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
            StatusMessage = L($"连接 '{updated.Name}' 已更新。", $"Connection '{updated.Name}' updated.");
        }
        catch (Exception ex)
        {
            StatusMessage = L($"更新连接失败：{ex.Message}", $"Failed to update connection: {ex.Message}");
        }
    }

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
            StatusMessage = L($"连接 '{name}' 已删除。", $"Connection '{name}' deleted.");
        }
        catch (Exception ex)
        {
            StatusMessage = L($"删除连接失败：{ex.Message}", $"Failed to delete connection: {ex.Message}");
        }
    }

    private void Connect()
    {
        if (SelectedConnection is null)
        {
            return;
        }

        var result = _connectionSessionService.OpenConnection(SelectedConnection);
        if (!result.IsSuccess)
        {
            if (result.IsFileNotFound)
            {
                SelectedConnection.LastConnectionError = L("数据库文件不存在。", "Database file not found.");
                _connectionService.SaveConnections();
                var missingPath = result.DatabasePath ?? SelectedConnection.Path;
                StatusMessage = L($"数据库文件不存在：{missingPath}", $"Database file not found: {missingPath}");
                UpdateDeveloperDiagnostics(SelectedTableName);
                return;
            }

            if (result.ErrorKind is ConnectionOpenErrorKind.InvalidDatabaseFile or ConnectionOpenErrorKind.UnsupportedVersion)
            {
                var errorText = result.ErrorKind switch
                {
                    ConnectionOpenErrorKind.UnsupportedVersion => L("数据库文件版本不受支持。", "Unsupported database file version."),
                    _ => L("不是有效的数据库文件。", "Not a valid database file.")
                };

                SelectedConnection.LastConnectionError = errorText;
                _connectionService.SaveConnections();
                IsConnected = false;
                StatusMessage = errorText;
                UpdateDeveloperDiagnostics(SelectedTableName);
                return;
            }

            SelectedConnection.LastConnectionError = result.ErrorMessage ?? L("未知错误", "Unknown error");
            _connectionService.SaveConnections();
            IsConnected = false;
            StatusMessage = L($"连接失败：{SelectedConnection.LastConnectionError}", $"Connection failed: {SelectedConnection.LastConnectionError}");
            UpdateDeveloperDiagnostics(SelectedTableName);
            return;
        }

        IsConnected = true;
        SelectedConnection.LastConnectedAt = DateTime.Now;
        SelectedConnection.LastConnectionError = null;
        _connectionService.SaveConnections();

        LoadTableNames(result.TableNames);
        UpdateDeveloperDiagnostics(SelectedTableName);
        StatusMessage = L($"已连接：{SelectedConnection.Name}", $"Connected: {SelectedConnection.Name}");
    }

    private void Disconnect()
    {
        var releaseError = _connectionSessionService.CloseConnection();

        if (!string.IsNullOrWhiteSpace(releaseError))
        {
            StatusMessage = L($"释放共享缓存失败：{releaseError}", $"Failed to release shared cache: {releaseError}");
        }

        TableNames.Clear();
        _allTableNames.Clear();
        FilterFields.Clear();
        FilterConditions.Clear();
        PagedItems.Clear();
        _allTableEntities.Clear();
        _filteredEntities.Clear();

        SelectedTableName = null;
        SelectedFilterCondition = null;
        IsConnected = false;
        UpdateDeveloperDiagnostics(null);
        StatusMessage = L("已断开连接。", "Disconnected.");

        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasNoData));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(SelectedConnectionDisplay));
        OnPropertyChanged(nameof(SelectedConnectionPathDisplay));
        OnPropertyChanged(nameof(RawCount));
    }

    private void LoadTableNames(IReadOnlyList<string> tableNames)
    {
        _allTableNames = [.. tableNames];
        ApplyTableFilter();

        SelectedTableName = TableNames.FirstOrDefault();
    }

    private void LoadTableData()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(SelectedTableName))
        {
            PagedItems.Clear();
            return;
        }

        try
        {
            var rows = _connectionSessionService.ReadTableRows(SelectedTableName);
            _allTableEntities = rows.Cast<object>().ToList();
            _filteredEntities = [.. _allTableEntities];

            FilterFields.Clear();
            SelectedFilterField = null;
            FilterConditions.Clear();
            SelectedFilterCondition = null;
            QuickFilterText = string.Empty;
            PageIndex = 1;
            ApplyPagination();

            UpdateDeveloperDiagnostics(SelectedTableName);
            StatusMessage = L($"已加载 '{SelectedTableName}'，共 {_allTableEntities.Count} 条记录。", $"Loaded '{SelectedTableName}' with {_allTableEntities.Count} records.");
            OnPropertyChanged(nameof(RawCount));
        }
        catch (Exception ex)
        {
            UpdateDeveloperDiagnostics(SelectedTableName);
            StatusMessage = L($"加载表数据失败：{ex.Message}", $"Failed to load table data: {ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        if (_allTableEntities.Count == 0)
        {
            return;
        }

        var keyword = QuickFilterText.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _filteredEntities = [.. _allTableEntities];
            PageIndex = 1;
            ApplyPagination();
            StatusMessage = L("未设置筛选关键字，已显示全部数据。", "No filter keyword set. Showing all data.");
            return;
        }

        try
        {
            _filteredEntities = _entityQueryService.ApplyQuickFilter(_allTableEntities, keyword);

            PageIndex = 1;
            ApplyPagination();
            StatusMessage = L($"筛选完成：{_filteredEntities.Count}/{_allTableEntities.Count} 条。", $"Filter completed: {_filteredEntities.Count}/{_allTableEntities.Count}.");
        }
        catch (Exception ex)
        {
            StatusMessage = L($"筛选失败：{ex.Message}", $"Filter failed: {ex.Message}");
        }
    }

    private void ClearFilter()
    {
        QuickFilterText = string.Empty;
        FilterValue = string.Empty;
        FilterValueTo = string.Empty;
        FilterConditions.Clear();
        SelectedFilterCondition = null;
        _filteredEntities = [.. _allTableEntities];
        PageIndex = 1;
        ApplyPagination();
        OnPropertyChanged(nameof(ConditionSummary));
        StatusMessage = L("已清空筛选条件。", "Filter conditions cleared.");
    }

    private void AddFilterCondition()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilterField))
        {
            StatusMessage = L("请先选择筛选字段。", "Please select a filter field first.");
            return;
        }

        var condition = _filterConditionService.CreateCondition(
            SelectedFilterField,
            SelectedFilterOperator,
            FilterValue,
            FilterValueTo,
            LanguagePreference);

        FilterConditions.Add(condition);
        SelectedFilterCondition = condition;
        OnPropertyChanged(nameof(ConditionSummary));
        RaiseCommandStates();
    }

    private void RemoveFilterCondition()
    {
        if (FilterConditions.Count == 0)
        {
            return;
        }

        var target = SelectedFilterCondition ?? FilterConditions.Last();
        FilterConditions.Remove(target);
        SelectedFilterCondition = FilterConditions.LastOrDefault();
        OnPropertyChanged(nameof(ConditionSummary));
        RaiseCommandStates();
    }

    private List<FilterCondition> GetActiveConditions()
    {
        return _filterConditionService.GetActiveConditions(
            FilterConditions,
            SelectedFilterField,
            SelectedFilterOperator,
            FilterValue,
            FilterValueTo,
            LanguagePreference);
    }

    private void ApplyPagination()
    {
        var pagination = _entityQueryService.Paginate(_filteredEntities, PageIndex, PageSize);
        PageIndex = pagination.PageIndex;

        PagedItems.Clear();
        foreach (var item in pagination.Items)
        {
            PagedItems.Add(item);
        }

        OnPropertyChanged(nameof(PagedItems));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RawCount));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasNoData));
        OnPropertyChanged(nameof(EmptyStateMessage));
        RaiseCommandStates();
    }

    private void GoFirstPage()
    {
        if (PageIndex <= 1)
        {
            return;
        }

        PageIndex = 1;
        ApplyPagination();
    }

    private void GoPreviousPage()
    {
        if (PageIndex <= 1)
        {
            return;
        }

        PageIndex--;
        ApplyPagination();
    }

    private void GoNextPage()
    {
        if (PageIndex >= TotalPages)
        {
            return;
        }

        PageIndex++;
        ApplyPagination();
    }

    private void GoLastPage()
    {
        if (PageIndex >= TotalPages)
        {
            return;
        }

        PageIndex = TotalPages;
        ApplyPagination();
    }

    private void RaiseCommandStates()
    {
        _addConnectionCommand.RaiseCanExecuteChanged();
        _editConnectionCommand.RaiseCanExecuteChanged();
        _deleteConnectionCommand.RaiseCanExecuteChanged();
        _connectCommand.RaiseCanExecuteChanged();
        _disconnectCommand.RaiseCanExecuteChanged();
        _refreshTableCommand.RaiseCanExecuteChanged();
        _applyFilterCommand.RaiseCanExecuteChanged();
        _clearFilterCommand.RaiseCanExecuteChanged();
        _addFilterConditionCommand.RaiseCanExecuteChanged();
        _removeFilterConditionCommand.RaiseCanExecuteChanged();
        _firstPageCommand.RaiseCanExecuteChanged();
        _previousPageCommand.RaiseCanExecuteChanged();
        _nextPageCommand.RaiseCanExecuteChanged();
        _lastPageCommand.RaiseCanExecuteChanged();
    }

    private void OnTableNamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TableCount));
    }

    private void OnFilterConditionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ConditionSummary));
        RaiseCommandStates();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

    private string L(string zh, string en)
    {
        return _localizationService.Localize(LanguagePreference, zh, en);
    }

    private void RaiseLocalizationChanged()
    {
        BuildFilterOperatorOptions();
        RefreshConditionOperatorDisplay();

        OnPropertyChanged(nameof(AppTitle));
        OnPropertyChanged(nameof(MenuHelp));
        OnPropertyChanged(nameof(MenuConnection));
        OnPropertyChanged(nameof(MenuManageConnections));
        OnPropertyChanged(nameof(MenuAppearance));
        OnPropertyChanged(nameof(MenuLightTheme));
        OnPropertyChanged(nameof(MenuDarkTheme));
        OnPropertyChanged(nameof(MenuSystemTheme));
        OnPropertyChanged(nameof(MenuToggleGlass));
        OnPropertyChanged(nameof(MenuOpenRepo));
        OnPropertyChanged(nameof(MenuOpenIssues));
        OnPropertyChanged(nameof(MenuLanguage));
        OnPropertyChanged(nameof(MenuLangZh));
        OnPropertyChanged(nameof(MenuLangEn));
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(SectionConnectionConfig));
        OnPropertyChanged(nameof(SectionSessionActions));
        OnPropertyChanged(nameof(SectionAppearance));
        OnPropertyChanged(nameof(SectionConnectionsAndTables));
        OnPropertyChanged(nameof(SectionTables));
        OnPropertyChanged(nameof(LabelTableCount));
        OnPropertyChanged(nameof(LabelFilteredCount));
        OnPropertyChanged(nameof(LabelRawCount));
        OnPropertyChanged(nameof(LabelNoDataTitle));
        OnPropertyChanged(nameof(LabelConnectionNameWatermark));
        OnPropertyChanged(nameof(LabelDbPathWatermark));
        OnPropertyChanged(nameof(LabelSearchConnectionWatermark));
        OnPropertyChanged(nameof(LabelSearchTableWatermark));
        OnPropertyChanged(nameof(LabelFilterValueWatermark));
        OnPropertyChanged(nameof(LabelFilterValueToWatermark));
        OnPropertyChanged(nameof(LabelGridFilterWatermark));
        OnPropertyChanged(nameof(BtnBrowse));
        OnPropertyChanged(nameof(BtnAdd));
        OnPropertyChanged(nameof(BtnUpdate));
        OnPropertyChanged(nameof(BtnDelete));
        OnPropertyChanged(nameof(BtnConnect));
        OnPropertyChanged(nameof(BtnDisconnect));
        OnPropertyChanged(nameof(BtnCreateSample));
        OnPropertyChanged(nameof(BtnRefresh));
        OnPropertyChanged(nameof(BtnLight));
        OnPropertyChanged(nameof(BtnDark));
        OnPropertyChanged(nameof(BtnSystem));
        OnPropertyChanged(nameof(BtnResetView));
        OnPropertyChanged(nameof(BtnAddCondition));
        OnPropertyChanged(nameof(BtnRemoveCondition));
        OnPropertyChanged(nameof(BtnApply));
        OnPropertyChanged(nameof(BtnClear));
        OnPropertyChanged(nameof(BtnFirstPage));
        OnPropertyChanged(nameof(BtnPrevPage));
        OnPropertyChanged(nameof(BtnNextPage));
        OnPropertyChanged(nameof(BtnLastPage));
        OnPropertyChanged(nameof(ToggleGlass));
        OnPropertyChanged(nameof(LabelNoConnection));
        OnPropertyChanged(nameof(ConnectionBadge));
        OnPropertyChanged(nameof(SelectedConnectionDisplay));
        OnPropertyChanged(nameof(SelectedConnectionPathDisplay));
        OnPropertyChanged(nameof(ConditionSummary));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(TableTitle));
    }

    private void BuildFilterOperatorOptions()
    {
        var currentKey = SelectedFilterOperator;

        FilterOperators.Clear();
        foreach (var option in _filterConditionService.BuildOperatorOptions(LanguagePreference, currentKey))
        {
            FilterOperators.Add(option);
        }

        SelectedFilterOperatorOption = FilterOperators.FirstOrDefault(option => option.Key == currentKey)
                                     ?? FilterOperators.FirstOrDefault();
    }

    private void RefreshConditionOperatorDisplay()
    {
        _filterConditionService.RefreshConditionOperatorDisplay(FilterConditions, LanguagePreference);
    }

    private void ApplyConnectionFilter()
    {
        var result = _collectionFilterService.FilterConnections(
            _connectionService.Connections,
            ConnectionSearchText,
            SelectedConnection,
            IsConnected);

        Connections.Clear();
        foreach (var connection in result.FilteredConnections)
        {
            Connections.Add(connection);
        }

        if (!ReferenceEquals(SelectedConnection, result.ResolvedSelection))
        {
            SelectedConnection = result.ResolvedSelection;
        }
    }

    private void ApplyTableFilter()
    {
        var result = _collectionFilterService.FilterTableNames(
            _allTableNames,
            TableSearchText,
            SelectedTableName);

        TableNames.Clear();
        foreach (var table in result.FilteredTableNames)
        {
            TableNames.Add(table);
        }

        OnPropertyChanged(nameof(TableNames));
        OnPropertyChanged(nameof(TableCount));

        if (SelectedTableName != result.ResolvedSelection)
        {
            SelectedTableName = result.ResolvedSelection;
        }
    }

    private void UpdateStatusTone(string message)
    {
        var tone = _statusToneService.Resolve(message);
        IsStatusError = tone == StatusTone.Error;
        IsStatusWarning = tone == StatusTone.Warning;
        IsStatusSuccess = tone == StatusTone.Success;
    }

    private void UpdateDeveloperDiagnostics(string? tableName)
    {
        if (!IsDeveloperDiagnosticsEnabled)
        {
            DeveloperDiagnostics = string.Empty;
            return;
        }

        var diagnostics = _connectionSessionService.GetDiagnostics(tableName);
        if (!diagnostics.IsConnected)
        {
            DeveloperDiagnostics = L("诊断：未连接会话。", "Diagnostics: no active session.");
            return;
        }

        var selectedTable = diagnostics.SelectedTable ?? "-";
        var textZh = $"诊断：v{diagnostics.FileVersion}；表 {diagnostics.TableCount}；Schema表 {diagnostics.SchemaTableCount}；当前表 {selectedTable}；命中Schema {diagnostics.HasSchemaForSelectedTable}；字段数 {diagnostics.SelectedTableSchemaFieldCount}；回退模式 false";
        var textEn = $"Diagnostics: v{diagnostics.FileVersion}; tables={diagnostics.TableCount}; schemaTables={diagnostics.SchemaTableCount}; selected={selectedTable}; hasSchema={diagnostics.HasSchemaForSelectedTable}; fields={diagnostics.SelectedTableSchemaFieldCount}; fallback=false";
        DeveloperDiagnostics = L(textZh, textEn);
    }

    private static bool IsDeveloperDiagnosticsEnabledByDefault()
    {
#if DEBUG
        return true;
#else
        var raw = Environment.GetEnvironmentVariable("PERIGON_MINIDB_CLIENT_DIAGNOSTICS");
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
#endif
    }
}
