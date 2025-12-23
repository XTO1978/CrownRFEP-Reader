using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa el diario de una sesión con notas y valoraciones del usuario
/// </summary>
[Table("session_diary")]
public class SessionDiary
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>ID de la sesión asociada</summary>
    [Column("session_id")]
    [Indexed]
    public int SessionId { get; set; }

    /// <summary>ID del atleta (usuario) que escribe el diario</summary>
    [Column("athlete_id")]
    [Indexed]
    public int AthleteId { get; set; }

    /// <summary>Valoración física (1-5)</summary>
    [Column("valoracion_fisica")]
    public int ValoracionFisica { get; set; }

    /// <summary>Valoración mental (1-5)</summary>
    [Column("valoracion_mental")]
    public int ValoracionMental { get; set; }

    /// <summary>Valoración técnica (1-5)</summary>
    [Column("valoracion_tecnica")]
    public int ValoracionTecnica { get; set; }

    /// <summary>Notas libres de la sesión</summary>
    [Column("notas")]
    public string? Notas { get; set; }

    /// <summary>Fecha de creación (Unix timestamp)</summary>
    [Column("created_at")]
    public long CreatedAt { get; set; }

    /// <summary>Fecha de última modificación (Unix timestamp)</summary>
    [Column("updated_at")]
    public long UpdatedAt { get; set; }

    // Propiedades computadas
    [Ignore]
    public DateTime CreatedAtLocal => DateTimeOffset.FromUnixTimeSeconds(CreatedAt).LocalDateTime;

    [Ignore]
    public DateTime UpdatedAtLocal => DateTimeOffset.FromUnixTimeSeconds(UpdatedAt).LocalDateTime;

    [Ignore]
    public bool HasValoraciones => ValoracionFisica > 0 || ValoracionMental > 0 || ValoracionTecnica > 0;

    [Ignore]
    public bool HasNotas => !string.IsNullOrWhiteSpace(Notas);

    [Ignore]
    public double PromedioValoraciones => HasValoraciones 
        ? (ValoracionFisica + ValoracionMental + ValoracionTecnica) / 3.0 
        : 0;
}
