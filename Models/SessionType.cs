using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un tipo de sesión de entrenamiento
/// </summary>
[Table("tipoSesion")]
public class SessionType
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("TipoSesion")]
    public string? TipoSesion { get; set; }

    [Column("IsSelected")]
    public int IsSelected { get; set; }
}
