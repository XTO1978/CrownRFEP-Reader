using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa una etiqueta para organizar contenido
/// </summary>
[Table("tags")]
public class Tag : INotifyPropertyChanged
{
    private int _isSelected;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombreTag")]
    public string? NombreTag { get; set; }

    [Column("IsSelected")]
    public int IsSelected 
    { 
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSelectedBool));
            }
        }
    }
    
    /// <summary>
    /// Versión booleana de IsSelected para bindings más sencillos
    /// </summary>
    [Ignore]
    public bool IsSelectedBool
    {
        get => _isSelected == 1;
        set => IsSelected = value ? 1 : 0;
    }

    /// <summary>
    /// Indica si este tag proviene de un evento (TimeStamp > 0) vs asignación directa
    /// </summary>
    [Ignore]
    public bool IsEventTag { get; set; }

    /// <summary>
    /// Cantidad de veces que este evento aparece en un video (para mostrar "2x etiqueta")
    /// </summary>
    [Ignore]
    public int EventCount { get; set; } = 1;

    /// <summary>
    /// Texto a mostrar en el badge: "2x etiqueta" si hay múltiples, o solo "etiqueta" si hay uno
    /// </summary>
    [Ignore]
    public string DisplayText => EventCount > 1 ? $"{EventCount}x {NombreTag}" : NombreTag ?? "";

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
