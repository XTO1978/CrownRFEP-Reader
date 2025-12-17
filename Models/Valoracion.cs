using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa una valoración de entrenamiento
/// </summary>
[Table("valoracion")]
public class Valoracion
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("SessionID")]
    public int SessionId { get; set; }

    [Column("AthleteID")]
    public int AthleteId { get; set; }

    [Column("InputTypeID")]
    public int InputTypeId { get; set; }

    [Column("InputDateTime")]
    public long InputDateTime { get; set; }

    [Column("InputValue")]
    public int InputValue { get; set; }

    [Column("TimeStamp")]
    public string? TimeStamp { get; set; }

    // Propiedades computadas
    [Ignore]
    public DateTime InputDateTimeLocal => DateTimeOffset.FromUnixTimeSeconds(InputDateTime).LocalDateTime;
}
