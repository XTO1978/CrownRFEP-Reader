using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa una sesión de entrenamiento
/// </summary>
[Table("sesion")]
public class Session
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("fecha")]
    public long Fecha { get; set; }

    [Column("lugar")]
    public string? Lugar { get; set; }

    [Column("tipoSesion")]
    public string? TipoSesion { get; set; }

    [Column("nombreSesion")]
    public string? NombreSesion { get; set; }

    [Column("pathSesion")]
    public string? PathSesion { get; set; }

    [Column("participantes")]
    public string? Participantes { get; set; }

    [Column("coach")]
    public string? Coach { get; set; }

    [Column("isMerged")]
    public int IsMerged { get; set; }

    // Propiedades computadas
    [Ignore]
    public DateTime FechaDateTime => DateTimeOffset.FromUnixTimeSeconds(Fecha).LocalDateTime;

    [Ignore]
    public string DisplayName => string.IsNullOrEmpty(NombreSesion) 
        ? $"Sesión {Id}" 
        : NombreSesion;

    [Ignore]
    public List<string> ParticipantesList => 
        string.IsNullOrEmpty(Participantes) 
            ? new List<string>() 
            : Participantes.Split(',').Select(p => p.Trim()).ToList();

    [Ignore]
    public int VideoCount { get; set; }

    [Ignore]
    public bool IsSelected { get; set; }
}
