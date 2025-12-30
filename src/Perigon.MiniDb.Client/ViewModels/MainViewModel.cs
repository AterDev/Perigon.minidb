using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Perigon.MiniDb;
using Perigon.MiniDb.Client.Helpers;
using Perigon.MiniDb.Client.Models;
using Perigon.MiniDb.Client.Services;

namespace Perigon.MiniDb.Client.ViewModels;

/// <summary>
/// Main ViewModel for the application
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly DatabaseConnectionService _connectionService;
    private DatabaseConnection? _selectedConnection;
    private string? _selectedTableName;
    private DataTable? _tableData;
    private MiniDbContext? _currentContext;
    private bool _isConnected;
    private string _statusMessage = "Ready";
    private bool _isSaving = false;

    public ObservableCollection<DatabaseConnection> Connections => _connectionService.Connections;
    public ObservableCollection<string> TableNames { get; } = new();

    public DatabaseConnection? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            if (_selectedConnection != value)
            {
                // Disconnect from current database when selection changes
                if (IsConnected)
                {
                    Disconnect();
                }
                _selectedConnection = value;
                OnPropertyChanged();
            }
        }
    }

    public string? SelectedTableName
    {
        get => _selectedTableName;
        set
        {
            if (_selectedTableName != value)
            {
                _selectedTableName = value;
                OnPropertyChanged();
                LoadTableData();
            }
        }
    }

    public DataTable? TableData
    {
        get => _tableData;
        set
        {
            if (_tableData != value)
            {
                _tableData = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand AddConnectionCommand { get; }
    public ICommand EditConnectionCommand { get; }
    public ICommand DeleteConnectionCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshTableCommand { get; }
    public ICommand SaveChangesCommand { get; }

    public MainViewModel()
    {
        _connectionService = new DatabaseConnectionService();

        AddConnectionCommand = new RelayCommand(_ => AddConnection());
        EditConnectionCommand = new RelayCommand(_ => EditConnection(), _ => SelectedConnection != null);
        DeleteConnectionCommand = new RelayCommand(_ => DeleteConnection(), _ => SelectedConnection != null);
        ConnectCommand = new RelayCommand(_ => Connect(), _ => SelectedConnection != null && !IsConnected);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
        RefreshTableCommand = new RelayCommand(_ => LoadTableData(), _ => IsConnected && !string.IsNullOrEmpty(SelectedTableName));
        SaveChangesCommand = new RelayCommand(async _ => await SaveChangesAsync(), _ => IsConnected && !_isSaving);
    }

    private void AddConnection()
    {
        var dialog = new Views.ConnectionDialog();
        if (dialog.ShowDialog() is true && dialog.Connection != null)
        {
            try
            {
                _connectionService.AddConnection(dialog.Connection);
                StatusMessage = $"Connection '{dialog.Connection.Name}' added successfully";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add connection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void EditConnection()
    {
        if (SelectedConnection == null) return;

        var dialog = new Views.ConnectionDialog(SelectedConnection);
        if (dialog.ShowDialog() is true && dialog.Connection != null)
        {
            try
            {
                _connectionService.UpdateConnection(SelectedConnection, dialog.Connection);
                SelectedConnection = dialog.Connection;
                StatusMessage = $"Connection '{dialog.Connection.Name}' updated successfully";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update connection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void DeleteConnection()
    {
        if (SelectedConnection == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the connection '{SelectedConnection.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var name = SelectedConnection.Name;
                _connectionService.RemoveConnection(SelectedConnection);
                StatusMessage = $"Connection '{name}' deleted successfully";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete connection: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Connect()
    {
        if (SelectedConnection == null) return;

        try
        {
            if (!File.Exists(SelectedConnection.Path))
            {
                MessageBox.Show($"Database file not found: {SelectedConnection.Path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Try to connect with timeout handling
            try
            {
                // Use SampleDbContext for demonstration
                // In a real application, this would need to dynamically load entity types
                MiniDbConfiguration.AddDbContext<Sample.SampleDbContext>(o => o.UseMiniDb(SelectedConnection.Path));
                _currentContext = new Sample.SampleDbContext();
                LoadTableNames();
                IsConnected = true;
                StatusMessage = $"Connected to '{SelectedConnection.Name}'";
            }
            catch (IOException)
            {
                MessageBox.Show(
                    "The database file is locked by another process. Please close the other application and try again.",
                    "Database Locked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to connect: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _currentContext?.Dispose();
            _currentContext = null;
        }
    }

    private void Disconnect()
    {
        try
        {
            // Check for unsaved changes
            if (TableData != null && TableData.GetChanges() != null)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save them before disconnecting?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                {
                    return; // User cancelled disconnect
                }
                else if (result == MessageBoxResult.Yes)
                {
                    // Save changes before disconnecting
                    SaveChangesAsync().Wait();
                }
            }

            _currentContext?.Dispose();
            _currentContext = null;
            TableNames.Clear();
            TableData = null;
            SelectedTableName = null;
            IsConnected = false;
            StatusMessage = "Disconnected";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during disconnect: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadTableNames()
    {
        if (_currentContext == null) return;

        TableNames.Clear();

        try
        {
            // Use reflection to get all DbSet properties
            var dbSetProperties = _currentContext.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsGenericType &&
                           p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

            foreach (var prop in dbSetProperties)
            {
                TableNames.Add(prop.Name);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading table names: {ex.Message}";
        }
    }

    private void LoadTableData()
    {
        if (_currentContext == null || string.IsNullOrEmpty(SelectedTableName))
        {
            TableData = null;
            return;
        }

        try
        {
            // Get the DbSet property by name
            var property = _currentContext.GetType().GetProperty(SelectedTableName);
            if (property == null)
            {
                StatusMessage = $"Table '{SelectedTableName}' not found";
                return;
            }

            var dbSet = property.GetValue(_currentContext);
            if (dbSet == null)
            {
                StatusMessage = $"Failed to get data for table '{SelectedTableName}'";
                return;
            }

            // Get the generic type of the DbSet
            var entityType = property.PropertyType.GetGenericArguments()[0];

            // Get all entities from the DbSet (it's IEnumerable)
            var entities = ((System.Collections.IEnumerable)dbSet).Cast<object>().ToList();

            // Create DataTable
            var dataTable = new DataTable(SelectedTableName);

            if (entities.Count > 0)
            {
                // Get properties from entity type
                var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => !p.GetCustomAttributes<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>().Any())
                    .ToList();

                // Add columns
                foreach (var prop in properties)
                {
                    var columnType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    dataTable.Columns.Add(prop.Name, columnType);
                }

                // Add rows
                foreach (var entity in entities)
                {
                    var row = dataTable.NewRow();
                    foreach (var prop in properties)
                    {
                        var value = prop.GetValue(entity);
                        row[prop.Name] = value ?? DBNull.Value;
                    }
                    dataTable.Rows.Add(row);
                }
            }

            TableData = dataTable;
            StatusMessage = $"Loaded {entities.Count} records from '{SelectedTableName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading table data: {ex.Message}";
            MessageBox.Show($"Failed to load table data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveChangesAsync()
    {
        if (_currentContext == null || TableData == null || string.IsNullOrEmpty(SelectedTableName))
            return;

        if (_isSaving)
            return;

        _isSaving = true;
        try
        {
            // Get the DbSet property
            var property = _currentContext.GetType().GetProperty(SelectedTableName);
            if (property == null) return;

            var dbSet = property.GetValue(_currentContext);
            if (dbSet == null) return;

            var entityType = property.PropertyType.GetGenericArguments()[0];
            var entities = ((System.Collections.IEnumerable)dbSet).Cast<object>().ToList();

            // Get the Update method from DbSet
            var updateMethod = property.PropertyType.GetMethod("Update");
            if (updateMethod == null) return;

            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => !p.GetCustomAttributes<System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute>().Any())
                .ToList();

            // Check if entity type has Id property
            var idProperty = entityType.GetProperty("Id");
            if (idProperty == null)
            {
                MessageBox.Show("Entity type does not have an 'Id' property.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Update entities based on modified rows
            foreach (DataRow row in TableData.Rows)
            {
                if (row.RowState == DataRowState.Modified)
                {
                    if (!TableData.Columns.Contains("Id"))
                        continue;

                    var idValue = row["Id"];
                    var entity = entities.FirstOrDefault(e =>
                    {
                        var idProp = e.GetType().GetProperty("Id");
                        return idProp?.GetValue(e)?.Equals(idValue) == true;
                    });

                    if (entity != null)
                    {
                        // Update entity properties from DataRow
                        foreach (var prop in properties)
                        {
                            if (prop.Name != "Id" && TableData.Columns.Contains(prop.Name))
                            {
                                var value = row[prop.Name];
                                
                                // Determine the effective target type (handle nullable value types)
                                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                                try
                                {
                                    if (value == DBNull.Value)
                                    {
                                        // Only assign null to reference types or nullable value types
                                        if (!targetType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) != null)
                                        {
                                            prop.SetValue(entity, null);
                                        }
                                        // For non-nullable value types, skip assignment to avoid runtime exceptions
                                    }
                                    else
                                    {
                                        object? convertedValue = value;

                                        // Handle enums explicitly
                                        if (targetType.IsEnum)
                                        {
                                            // Try to parse from string or underlying value
                                            convertedValue = Enum.Parse(targetType, value.ToString()!, ignoreCase: true);
                                        }
                                        else if (!targetType.IsAssignableFrom(value.GetType()))
                                        {
                                            // Try to convert primitive/convertible types
                                            convertedValue = Convert.ChangeType(value, targetType);
                                        }

                                        prop.SetValue(entity, convertedValue);
                                    }
                                }
                                catch (Exception convEx)
                                {
                                    // Log or skip individual property conversion failures
                                    System.Diagnostics.Debug.WriteLine($"Failed to convert property {prop.Name}: {convEx.Message}");
                                }
                            }
                        }

                        // Call Update method
                        updateMethod.Invoke(dbSet, new[] { entity });
                    }
                }
            }

            // Save changes to database
            await _currentContext.SaveChangesAsync();
            TableData.AcceptChanges();
            StatusMessage = $"Changes saved successfully to '{SelectedTableName}'";
        }
        catch (IOException)
        {
            StatusMessage = "Database file is locked";
            MessageBox.Show(
                "The database file is locked by another process. Please close the other application and try again.",
                "Database Locked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving changes: {ex.Message}";
            MessageBox.Show($"Failed to save changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isSaving = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
