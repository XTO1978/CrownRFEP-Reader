using System.ComponentModel;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Item seleccionable para filtros con selección múltiple
/// </summary>
public class FilterItem<T> : INotifyPropertyChanged
{
    private bool _isSelected;

    public T Value { get; set; }
    public string DisplayName { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SelectionChanged;

    public FilterItem(T value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

/// <summary>
/// Item de filtro para lugares
/// </summary>
public class PlaceFilterItem : FilterItem<string>
{
    public PlaceFilterItem(string place) : base(place, place) { }
}

/// <summary>
/// Item de filtro para atletas
/// </summary>
public class AthleteFilterItem : FilterItem<Athlete>
{
    public AthleteFilterItem(Athlete athlete) : base(athlete, athlete.NombreCompleto ?? $"Atleta {athlete.Id}") { }
}

/// <summary>
/// Item de filtro para secciones
/// </summary>
public class SectionFilterItem : FilterItem<int>
{
    public SectionFilterItem(int section) : base(section, $"Sección {section}") { }
}

/// <summary>
/// Item de filtro para tags
/// </summary>
public class TagFilterItem : FilterItem<Tag>
{
    public TagFilterItem(Tag tag) : base(tag, tag.NombreTag ?? $"Tag {tag.Id}") { }
}
