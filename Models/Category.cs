using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa una categoría de atleta (ej: Hombre K1, Mujer C1, etc.)
/// </summary>
[Table("categoria")]
public class Category
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombreCategoria")]
    public string? NombreCategoria { get; set; }

    [Column("isSystemDefault")]
    public int IsSystemDefault { get; set; }
}
