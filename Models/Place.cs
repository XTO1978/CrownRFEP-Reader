using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un lugar de entrenamiento
/// </summary>
[Table("lugar")]
public class Place
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("nombreLugar")]
    public string? NombreLugar { get; set; }

    [Column("IsSelected")]
    public int IsSelected { get; set; }
}
