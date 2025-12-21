using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un input/dato de entrenamiento
/// </summary>
[Table("input")]
public class Input
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("SessionID")]
    public int SessionId { get; set; }

    [Column("VideoID")]
    public int VideoId { get; set; }

    [Column("AthleteID")]
    public int AthleteId { get; set; }

    [Column("CategoriaID")]
    public int CategoriaId { get; set; }

    [Column("InputTypeID")]
    public int InputTypeId { get; set; }

    [Column("InputDateTime")]
    public long InputDateTime { get; set; }

    [Column("InputValue")]
    public string? InputValue { get; set; }

    [Column("TimeStamp")]
    public long TimeStamp { get; set; }

    /// <summary>
    /// Indica si este input es un evento (1) o una asignación de tag (0).
    /// - Eventos: Importados de .crown o creados con AddTagEventAsync, tienen timestamp significativo.
    /// - Asignaciones: Creados desde el panel de etiquetas, no tienen timestamp significativo.
    /// </summary>
    [Column("IsEvent")]
    public int IsEvent { get; set; }

    // Propiedades computadas
    [Ignore]
    public DateTime InputDateTimeLocal => DateTimeOffset.FromUnixTimeSeconds(InputDateTime).LocalDateTime;

    /// <summary>
    /// Versión booleana de IsEvent para uso más sencillo en código
    /// </summary>
    [Ignore]
    public bool IsEventBool
    {
        get => IsEvent == 1;
        set => IsEvent = value ? 1 : 0;
    }
}
