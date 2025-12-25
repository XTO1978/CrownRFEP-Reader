using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa una categoría de atleta (ej: Hombre K1, Mujer C1, etc.)
/// </summary>
[Table("categoria")]
public class Category : INotifyPropertyChanged
{
    private bool _isSelectedForMerge;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombreCategoria")]
    public string? NombreCategoria { get; set; }

    [Column("isSystemDefault")]
    public int IsSystemDefault { get; set; }

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
