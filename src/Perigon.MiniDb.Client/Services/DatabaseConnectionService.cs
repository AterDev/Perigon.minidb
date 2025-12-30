using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Services;

/// <summary>
/// Service to manage database connections
/// </summary>
public class DatabaseConnectionService
{
    private readonly string _configPath;
    private const string ConfigFileName = "database-connections.json";

    public ObservableCollection<DatabaseConnection> Connections { get; private set; }

    public DatabaseConnectionService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Perigon.MiniDb.Client");

        Directory.CreateDirectory(appDataPath);
        _configPath = Path.Combine(appDataPath, ConfigFileName);
        Connections = new ObservableCollection<DatabaseConnection>();
        LoadConnections();
    }

    /// <summary>
    /// Load saved connections from configuration file
    /// </summary>
    public void LoadConnections()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var connections = JsonSerializer.Deserialize<List<DatabaseConnection>>(json);
                if (connections != null)
                {
                    Connections.Clear();
                    foreach (var connection in connections)
                    {
                        Connections.Add(connection);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash the application
            System.Diagnostics.Debug.WriteLine($"Error loading connections: {ex.Message}");
        }
    }

    /// <summary>
    /// Save connections to configuration file
    /// </summary>
    public void SaveConnections()
    {
        try
        {
            var json = JsonSerializer.Serialize(Connections, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save connections: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Add a new database connection
    /// </summary>
    public void AddConnection(DatabaseConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Name))
            throw new ArgumentException("Connection name cannot be empty");

        if (string.IsNullOrWhiteSpace(connection.Path))
            throw new ArgumentException("Connection path cannot be empty");

        if (Connections.Any(c => c.Name.Equals(connection.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A connection with the name '{connection.Name}' already exists");

        Connections.Add(connection);
        SaveConnections();
    }

    /// <summary>
    /// Update an existing database connection
    /// </summary>
    public void UpdateConnection(DatabaseConnection oldConnection, DatabaseConnection newConnection)
    {
        if (string.IsNullOrWhiteSpace(newConnection.Name))
            throw new ArgumentException("Connection name cannot be empty");

        if (string.IsNullOrWhiteSpace(newConnection.Path))
            throw new ArgumentException("Connection path cannot be empty");

        var index = Connections.IndexOf(oldConnection);
        if (index >= 0)
        {
            // Ensure the new name does not conflict with any other existing connection
            for (int i = 0; i < Connections.Count; i++)
            {
                if (i == index)
                    continue;

                if (Connections[i].Name.Equals(newConnection.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"A connection with the name '{newConnection.Name}' already exists");
            }

            Connections[index] = newConnection;
            SaveConnections();
        }
    }

    /// <summary>
    /// Remove a database connection
    /// </summary>
    public void RemoveConnection(DatabaseConnection connection)
    {
        Connections.Remove(connection);
        SaveConnections();
    }
}
