using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Perigon.MiniDb.Client.Models;

public class FilterCondition : INotifyPropertyChanged
{
    private string _field = string.Empty;
    private string _operator = "Contains";
    private string _operatorDisplay = "Contains";
    private string _value = string.Empty;
    private string _valueTo = string.Empty;

    public string Field
    {
        get => _field;
        set
        {
            if (_field == value)
            {
                return;
            }

            _field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string Operator
    {
        get => _operator;
        set
        {
            if (_operator == value)
            {
                return;
            }

            _operator = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string OperatorDisplay
    {
        get => _operatorDisplay;
        set
        {
            if (_operatorDisplay == value)
            {
                return;
            }

            _operatorDisplay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string ValueTo
    {
        get => _valueTo;
        set
        {
            if (_valueTo == value)
            {
                return;
            }

            _valueTo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string DisplayText => Operator == "Between"
        ? $"{Field} {OperatorDisplay} {Value} ~ {ValueTo}"
        : $"{Field} {OperatorDisplay} {Value}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
