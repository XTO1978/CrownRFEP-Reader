using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa una etiqueta para organizar contenido
/// </summary>
[Table("tags")]
public class Tag
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombreTag")]
    public string? NombreTag { get; set; }

    [Column("IsSelected")]
    public int IsSelected { get; set; }
}
