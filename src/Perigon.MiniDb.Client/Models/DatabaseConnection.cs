using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Perigon.MiniDb.Client.Models;

/// <summary>
/// Represents a database connection configuration
/// </summary>
public class DatabaseConnection : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private DateTime? _lastConnectedAt;
    private string? _lastConnectionError;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public string Path
    {
        get => _path;
        set
        {
            if (_path != value)
            {
                _path = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? LastConnectedAt
    {
        get => _lastConnectedAt;
        set
        {
            if (_lastConnectedAt != value)
            {
                _lastConnectedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public string? LastConnectionError
    {
        get => _lastConnectionError;
        set
        {
            if (_lastConnectionError != value)
            {
                _lastConnectionError = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
