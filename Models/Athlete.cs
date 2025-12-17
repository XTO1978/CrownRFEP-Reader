using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un atleta
/// </summary>
[Table("Atleta")]
public class Athlete
{
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
    public bool IsSelected { get; set; }

    [Ignore]
    public string NombreCompleto => $"{Apellido} {Nombre}".Trim();

    [Ignore]
    public string? CategoriaNombre { get; set; }

    [Ignore]
    public string ChipDisplayString => string.IsNullOrEmpty(CategoriaNombre) 
        ? NombreCompleto 
        : $"{NombreCompleto} @ {CategoriaNombre}";
}
