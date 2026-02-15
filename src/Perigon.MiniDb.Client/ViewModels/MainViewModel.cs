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
using Perigon.MiniDb;
using Perigon.MiniDb.Client.Helpers;
using Perigon.MiniDb.Client.Models;
using Perigon.MiniDb.Client.Services;

namespace Perigon.MiniDb.Client.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private static readonly string[] _filterOperatorKeys =
    [
        "Contains",
        "Equals",
        "NotEquals",
        "GreaterThan",
        "GreaterOrEqual",
        "LessThan",
        "LessOrEqual",
        "Between"
    ];

    private readonly DatabaseConnectionService _connectionService;
    private readonly ClientSettingsService _settingsService;
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
    private MiniDbContext? _currentContext;
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

    private Type? _currentEntityType;
    private List<PropertyInfo> _entityProperties = [];
    private List<string> _allTableNames = [];
    private List<object> _allTableEntities = [];
    private List<object> _filteredEntities = [];

    private string? _selectedFilterField;
    private string _selectedFilterOperator = "Contains";
    private FilterOperatorOption? _selectedFilterOperatorOption;
    private string _filterValue = string.Empty;
    private string _filterValueTo = string.Empty;
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
    public bool IsChinese => !LanguagePreference.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    public bool IsEnglish => LanguagePreference.StartsWith("en", StringComparison.OrdinalIgnoreCase);
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
    {
        _connectionService = new DatabaseConnectionService();
        _settingsService = new ClientSettingsService();

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

        try
        {
            var dbPath = Path.GetFullPath(SelectedConnection.Path);
            if (!File.Exists(dbPath))
            {
                SelectedConnection.LastConnectionError = L("数据库文件不存在。", "Database file not found.");
                _connectionService.SaveConnections();
                StatusMessage = L($"数据库文件不存在：{dbPath}", $"Database file not found: {dbPath}");
                return;
            }

            MiniDbConfiguration.AddDbContext<Sample.SampleDbContext>(o => o.UseMiniDb(dbPath));
            _currentContext?.Dispose();
            _currentContext = new Sample.SampleDbContext();

            IsConnected = true;
            SelectedConnection.LastConnectedAt = DateTime.Now;
            SelectedConnection.LastConnectionError = null;
            _connectionService.SaveConnections();

            LoadTableNames();
            StatusMessage = L($"已连接：{SelectedConnection.Name}", $"Connected: {SelectedConnection.Name}");
        }
        catch (Exception ex)
        {
            SelectedConnection.LastConnectionError = ex.Message;
            _connectionService.SaveConnections();
            StatusMessage = L($"连接失败：{ex.Message}", $"Connection failed: {ex.Message}");
            _currentContext?.Dispose();
            _currentContext = null;
            IsConnected = false;
        }
    }

    private void Disconnect()
    {
        var lastPath = SelectedConnection?.Path;

        _currentContext?.Dispose();
        _currentContext = null;

        if (!string.IsNullOrWhiteSpace(lastPath))
        {
            try
            {
                MiniDbContext.ReleaseSharedCache(lastPath);
            }
            catch (Exception ex)
            {
                StatusMessage = L($"释放共享缓存失败：{ex.Message}", $"Failed to release shared cache: {ex.Message}");
            }
        }

        TableNames.Clear();
        _allTableNames.Clear();
        FilterFields.Clear();
        FilterConditions.Clear();
        PagedItems.Clear();
        _allTableEntities.Clear();
        _filteredEntities.Clear();
        _entityProperties = [];
        _currentEntityType = null;

        SelectedTableName = null;
        SelectedFilterCondition = null;
        IsConnected = false;
        StatusMessage = L("已断开连接。", "Disconnected.");

        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasNoData));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(SelectedConnectionDisplay));
        OnPropertyChanged(nameof(SelectedConnectionPathDisplay));
        OnPropertyChanged(nameof(RawCount));
    }

    private void LoadTableNames()
    {
        if (_currentContext is null)
        {
            return;
        }

        var dbSetProperties = _currentContext.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .OrderBy(p => p.Name)
            .ToList();

        _allTableNames = dbSetProperties.Select(p => p.Name).ToList();
        ApplyTableFilter();

        SelectedTableName = TableNames.FirstOrDefault();
    }

    private void LoadTableData()
    {
        if (_currentContext is null || string.IsNullOrWhiteSpace(SelectedTableName))
        {
            PagedItems.Clear();
            return;
        }

        try
        {
            var property = _currentContext.GetType().GetProperty(SelectedTableName);
            if (property is null)
            {
                StatusMessage = L($"找不到表：{SelectedTableName}", $"Table not found: {SelectedTableName}");
                return;
            }

            var dbSet = property.GetValue(_currentContext) as IEnumerable;
            if (dbSet is null)
            {
                StatusMessage = L($"读取表数据失败：{SelectedTableName}", $"Failed to read table data: {SelectedTableName}");
                return;
            }

            _currentEntityType = property.PropertyType.GetGenericArguments()[0];
            _entityProperties = _currentEntityType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToList();

            _allTableEntities = dbSet.Cast<object>().ToList();
            _filteredEntities = [.. _allTableEntities];

            FilterFields.Clear();
            foreach (var propertyInfo in _entityProperties)
            {
                FilterFields.Add(propertyInfo.Name);
            }

            SelectedFilterField = FilterFields.FirstOrDefault();
            FilterConditions.Clear();
            SelectedFilterCondition = null;
            PageIndex = 1;
            ApplyPagination();

            StatusMessage = L($"已加载 '{SelectedTableName}'，共 {_allTableEntities.Count} 条记录。", $"Loaded '{SelectedTableName}' with {_allTableEntities.Count} records.");
            OnPropertyChanged(nameof(RawCount));
        }
        catch (Exception ex)
        {
            StatusMessage = L($"加载表数据失败：{ex.Message}", $"Failed to load table data: {ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        if (_allTableEntities.Count == 0)
        {
            return;
        }

        var activeConditions = GetActiveConditions();
        if (activeConditions.Count == 0)
        {
            _filteredEntities = [.. _allTableEntities];
            PageIndex = 1;
            ApplyPagination();
            StatusMessage = L("未设置筛选条件，已显示全部数据。", "No filter conditions set. Showing all data.");
            return;
        }

        try
        {
            _filteredEntities = _allTableEntities
                .Where(entity => activeConditions.All(condition => EvaluateEntity(entity, condition)))
                .ToList();

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

        var condition = new FilterCondition
        {
            Field = SelectedFilterField,
            Operator = SelectedFilterOperator,
            OperatorDisplay = GetOperatorDisplay(SelectedFilterOperator),
            Value = FilterValue,
            ValueTo = FilterValueTo
        };

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
        var conditions = FilterConditions.ToList();

        if (!string.IsNullOrWhiteSpace(SelectedFilterField) &&
            !string.IsNullOrWhiteSpace(FilterValue) &&
            !conditions.Any(c =>
                c.Field == SelectedFilterField &&
                c.Operator == SelectedFilterOperator &&
                c.Value == FilterValue &&
                c.ValueTo == FilterValueTo))
        {
            conditions.Add(new FilterCondition
            {
                Field = SelectedFilterField,
                Operator = SelectedFilterOperator,
                OperatorDisplay = GetOperatorDisplay(SelectedFilterOperator),
                Value = FilterValue,
                ValueTo = FilterValueTo
            });
        }

        return conditions
            .Where(c => !string.IsNullOrWhiteSpace(c.Field))
            .ToList();
    }

    private bool EvaluateEntity(object entity, FilterCondition condition)
    {
        var property = _entityProperties.FirstOrDefault(p => p.Name == condition.Field);
        if (property is null)
        {
            return false;
        }

        var value = property.GetValue(entity);
        var effectiveType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (condition.Operator == "Contains")
        {
            var text = value?.ToString() ?? string.Empty;
            return text.Contains(condition.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        if (!TryConvertFromString(condition.Value, effectiveType, out var convertedValue))
        {
            throw new InvalidOperationException($"筛选值 '{condition.Value}' 无法转换为 {effectiveType.Name}。");
        }

        if (condition.Operator == "Between")
        {
            if (!TryConvertFromString(condition.ValueTo, effectiveType, out var upperBound))
            {
                throw new InvalidOperationException($"筛选上限 '{condition.ValueTo}' 无法转换为 {effectiveType.Name}。");
            }

            return Compare(value, convertedValue, effectiveType) >= 0
                   && Compare(value, upperBound, effectiveType) <= 0;
        }

        return condition.Operator switch
        {
            "Equals" => Compare(value, convertedValue, effectiveType) == 0,
            "NotEquals" => Compare(value, convertedValue, effectiveType) != 0,
            "GreaterThan" => Compare(value, convertedValue, effectiveType) > 0,
            "GreaterOrEqual" => Compare(value, convertedValue, effectiveType) >= 0,
            "LessThan" => Compare(value, convertedValue, effectiveType) < 0,
            "LessOrEqual" => Compare(value, convertedValue, effectiveType) <= 0,
            _ => false
        };
    }

    private static int Compare(object? left, object? right, Type type)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (type.IsEnum)
        {
            var leftValue = Convert.ToInt64(left, CultureInfo.InvariantCulture);
            var rightValue = Convert.ToInt64(right, CultureInfo.InvariantCulture);
            return leftValue.CompareTo(rightValue);
        }

        if (left is IComparable comparable)
        {
            return comparable.CompareTo(right);
        }

        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvertFromString(string input, Type targetType, out object? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            if (Nullable.GetUnderlyingType(targetType) is not null || !targetType.IsValueType)
            {
                value = null;
                return true;
            }

            return false;
        }

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (effectiveType == typeof(string))
            {
                value = input;
                return true;
            }

            if (effectiveType == typeof(DateTime))
            {
                if (DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var dateTime))
                {
                    value = dateTime;
                    return true;
                }

                return false;
            }

            if (effectiveType == typeof(Guid))
            {
                if (Guid.TryParse(input, out var guid))
                {
                    value = guid;
                    return true;
                }

                return false;
            }

            if (effectiveType == typeof(bool))
            {
                if (bool.TryParse(input, out var boolValue))
                {
                    value = boolValue;
                    return true;
                }

                return false;
            }

            if (effectiveType.IsEnum)
            {
                value = Enum.Parse(effectiveType, input, ignoreCase: true);
                return true;
            }

            value = Convert.ChangeType(input, effectiveType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private void ApplyPagination()
    {
        if (_filteredEntities.Count == 0)
        {
            PagedItems.Clear();
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(RawCount));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageSummary));
            OnPropertyChanged(nameof(FilterSummary));
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(HasNoData));
            OnPropertyChanged(nameof(EmptyStateMessage));
            RaiseCommandStates();
            return;
        }

        if (PageIndex > TotalPages)
        {
            PageIndex = TotalPages;
        }

        if (PageIndex < 1)
        {
            PageIndex = 1;
        }

        var items = _filteredEntities
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        PagedItems.Clear();
        foreach (var item in items)
        {
            PagedItems.Add(item);
        }

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
        return LanguagePreference.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? en : zh;
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
        foreach (var key in _filterOperatorKeys)
        {
            FilterOperators.Add(new FilterOperatorOption
            {
                Key = key,
                Display = GetOperatorDisplay(key)
            });
        }

        SelectedFilterOperatorOption = FilterOperators.FirstOrDefault(option => option.Key == currentKey)
                                     ?? FilterOperators.FirstOrDefault();
    }

    private string GetOperatorDisplay(string key)
    {
        return key switch
        {
            "Contains" => L("包含", "Contains"),
            "Equals" => L("等于", "Equals"),
            "NotEquals" => L("不等于", "Not equals"),
            "GreaterThan" => L("大于", "Greater than"),
            "GreaterOrEqual" => L("大于等于", "Greater or equal"),
            "LessThan" => L("小于", "Less than"),
            "LessOrEqual" => L("小于等于", "Less or equal"),
            "Between" => L("区间", "Between"),
            _ => key
        };
    }

    private void RefreshConditionOperatorDisplay()
    {
        foreach (var condition in FilterConditions)
        {
            condition.OperatorDisplay = GetOperatorDisplay(condition.Operator);
        }
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

    private void UpdateStatusTone(string message)
    {
        var lower = message.ToLowerInvariant();

        var isError = lower.Contains("失败")
                      || lower.Contains("错误")
                      || lower.Contains("invalid")
                      || lower.Contains("error")
                      || lower.Contains("不存在")
                      || lower.Contains("无效");

        var isWarning = !isError &&
                        (lower.Contains("警告")
                         || lower.Contains("warning")
                         || lower.Contains("锁定")
                         || lower.Contains("注意"));

        var isSuccess = !isError && !isWarning &&
                        (lower.Contains("已")
                         || lower.Contains("成功")
                         || lower.Contains("完成")
                         || lower.Contains("success")
                         || lower.Contains("connected")
                         || lower.Contains("disconnected")
                         || lower.Contains("loaded")
                         || lower.Contains("opened")
                         || lower.Contains("created")
                         || lower.Contains("updated"));

        IsStatusError = isError;
        IsStatusWarning = isWarning;
        IsStatusSuccess = isSuccess;
    }
}
