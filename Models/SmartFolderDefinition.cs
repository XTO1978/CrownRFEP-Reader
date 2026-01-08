using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

public sealed class SmartFolderDefinition : INotifyPropertyChanged
{
    private string _name = "";
    private string _matchMode = "All";
    private string _icon = "folder";
    private string _iconColor = "#FF888888";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// "All" = AND, "Any" = OR
    /// </summary>
    public string MatchMode
    {
        get => _matchMode;
        set
        {
            if (_matchMode == value) return;
            _matchMode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// SF Symbol name for the folder icon
    /// </summary>
    public string Icon
    {
        get => _icon;
        set
        {
            if (_icon == value) return;
            _icon = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Hex color for the folder icon
    /// </summary>
    public string IconColor
    {
        get => _iconColor;
        set
        {
            if (_iconColor == value) return;
            _iconColor = value;
            OnPropertyChanged();
        }
    }

    public List<SmartFolderCriterion> Criteria { get; set; } = new();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class SmartFolderCriterion : INotifyPropertyChanged
{
    private string _field = "Deportista";
    private string _operator = "Contiene";
    private string _value = "";
    private string _value2 = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Field
    {
        get => _field;
        set
        {
            if (_field == value) return;
            _field = value;
            OnPropertyChanged();
            RefreshOperatorsForField();
            OnPropertyChanged(nameof(ShowSecondValue));
        }
    }

    public string Operator
    {
        get => _operator;
        set
        {
            if (_operator == value) return;
            _operator = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowSecondValue));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public string Value2
    {
        get => _value2;
        set
        {
            if (_value2 == value) return;
            _value2 = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> AvailableOperators { get; } = new();

    public bool ShowSecondValue => Field == "Fecha" && Operator == "Entre";

    public SmartFolderCriterion()
    {
        RefreshOperatorsForField();
    }

    private void RefreshOperatorsForField()
    {
        AvailableOperators.Clear();

        if (Field == "Fecha")
        {
            AvailableOperators.Add("Desde");
            AvailableOperators.Add("Hasta");
            AvailableOperators.Add("Entre");

            if (!AvailableOperators.Contains(Operator))
                Operator = "Desde";

            return;
        }

        AvailableOperators.Add("Es");
        AvailableOperators.Add("Contiene");

        if (!AvailableOperators.Contains(Operator))
            Operator = "Contiene";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
