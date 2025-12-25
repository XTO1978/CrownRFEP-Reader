using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un tipo de sesión de entrenamiento
/// </summary>
[Table("tipoSesion")]
public class SessionType : INotifyPropertyChanged
{
    private bool _isSelectedForMerge;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("TipoSesion")]
    public string? TipoSesion { get; set; }

    [Column("IsSelected")]
    public int IsSelected { get; set; }

    /// <summary>
    /// Propiedad para selección en la UI de fusión (no persistida)
    /// </summary>
    [Ignore]
    public bool IsSelectedForMerge
    {
        get => _isSelectedForMerge;
        set
        {
            if (_isSelectedForMerge != value)
            {
                _isSelectedForMerge = value;
                OnPropertyChanged();
            }
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
