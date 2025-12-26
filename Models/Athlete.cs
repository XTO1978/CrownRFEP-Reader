using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un atleta
/// </summary>
[Table("Atleta")]
public class Athlete : INotifyPropertyChanged
{
    private bool _isSelected;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string? Nombre { get; set; }

    [Column("surname")]
    public string? Apellido { get; set; }

    [Column("category")]
    public int Category { get; set; }

    [Column("categoriaId")]
    public int CategoriaId { get; set; }

    [Column("favorite")]
    public int Favorite { get; set; }

    [Column("isSystemDefault")]
    public int IsSystemDefault { get; set; }

    [Column("IsSelected")]
    public int IsSelectedDb { get; set; }

    // Propiedades computadas
    [Ignore]
    public bool IsFavorite => Favorite == 1;

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

    [Ignore]
    public string NombreCompleto
    {
        get
        {
            static string Normalize(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return string.Empty;

                // Trim y colapsar espacios múltiples
                var parts = value
                    .Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return string.Join(' ', parts);
            }

            var apellido = Normalize(Apellido);
            if (!string.IsNullOrEmpty(apellido))
                apellido = apellido.ToUpperInvariant();

            var nombre = Normalize(Nombre);

            if (string.IsNullOrEmpty(apellido))
                return nombre;
            if (string.IsNullOrEmpty(nombre))
                return apellido;

            return $"{apellido} {nombre}";
        }
    }

    [Ignore]
    public string? CategoriaNombre { get; set; }

    [Ignore]
    public string ChipDisplayString => string.IsNullOrEmpty(CategoriaNombre) 
        ? NombreCompleto 
        : $"{NombreCompleto} @ {CategoriaNombre}";

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
