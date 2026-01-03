using SQLite;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Evento de cronometraje de ejecución (Inicio/Lap/Fin) asociado a un vídeo.
/// Se guarda separado de event_tags/input para que no aparezca como "evento marcado" en SinglePlayer.
/// </summary>
[Table("execution_timing_events")]
public class ExecutionTimingEvent
{
    [PrimaryKey, AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Column("VideoID")]
    public int VideoId { get; set; }

    [Column("SessionID")]
    public int SessionId { get; set; }

    [Column("AthleteID")]
    public int AthleteId { get; set; }

    [Column("SectionID")]
    public int SectionId { get; set; }

    /// <summary>
    /// 0 = Inicio, 1 = Lap, 2 = Fin
    /// </summary>
    [Column("Kind")]
    public int Kind { get; set; }

    /// <summary>
    /// Tiempo absoluto desde el inicio de la grabación (ms)
    /// </summary>
    [Column("ElapsedMs")]
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Tiempo parcial desde el último hito (inicio/lap anterior) (ms)
    /// </summary>
    [Column("SplitMs")]
    public long SplitMilliseconds { get; set; }

    /// <summary>
    /// Índice de lap dentro de una ejecución (1..N). Para Inicio/Fin será 0.
    /// </summary>
    [Column("LapIndex")]
    public int LapIndex { get; set; }

    /// <summary>
    /// Índice de ejecución dentro del vídeo (0..N). Permite varias ejecuciones.
    /// </summary>
    [Column("RunIndex")]
    public int RunIndex { get; set; }

    [Column("CreatedAt")]
    public long CreatedAtUnixSeconds { get; set; }
}
