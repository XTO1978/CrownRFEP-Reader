using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Define un tipo de evento (catálogo) para marcar timestamps en un vídeo.
/// Se almacena separado de las etiquetas generales.
/// </summary>
[Table("event_tags")]
public class EventTagDefinition : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombre")]
    public string? Nombre { get; set; }

    /// <summary>
    /// Indica si es un tag de sistema (penalizaciones, etc.) que no puede ser borrado.
    /// </summary>
    [Column("is_system")]
    public bool IsSystem { get; set; }

    /// <summary>
    /// Valor en segundos de la penalización (solo para tags de sistema de penalización).
    /// 0 = no es penalización.
    /// </summary>
    [Column("penalty_seconds")]
    public int PenaltySeconds { get; set; }

    [Ignore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Nombre para mostrar (incluye indicador de penalización si aplica)
    /// </summary>
    [Ignore]
    public string DisplayName => IsSystem && PenaltySeconds > 0 
        ? $"⚠️ {Nombre}" 
        : Nombre ?? "";

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
