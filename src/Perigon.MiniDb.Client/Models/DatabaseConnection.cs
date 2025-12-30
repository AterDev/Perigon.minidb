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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
