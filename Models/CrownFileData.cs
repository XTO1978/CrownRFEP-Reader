using System.Text.Json.Serialization;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa los datos contenidos en un archivo .crown
/// </summary>
public class CrownFileData
{
    [JsonPropertyName("ExportMetadata")]
    public ExportMetadata? ExportMetadata { get; set; }

    [JsonPropertyName("Session")]
    public SessionJson? Session { get; set; }

    [JsonPropertyName("VideoClips")]
    public List<VideoClipJson>? VideoClips { get; set; }

    [JsonPropertyName("Athletes")]
    public List<AthleteJson>? Athletes { get; set; }

    [JsonPropertyName("Categories")]
    public List<CategoryJson>? Categories { get; set; }

    [JsonPropertyName("Inputs")]
    public List<InputJson>? Inputs { get; set; }

    [JsonPropertyName("ExecutionTimingEvents")]
    public List<ExecutionTimingEventJson>? ExecutionTimingEvents { get; set; }

    [JsonPropertyName("Valoraciones")]
    public List<ValoracionJson>? Valoraciones { get; set; }
}

public class ExportMetadata
{
    [JsonPropertyName("ExportDate")]
    public DateTime ExportDate { get; set; }

    [JsonPropertyName("AppVersion")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("ExportVersion")]
    public string? ExportVersion { get; set; }

    [JsonPropertyName("SessionId")]
    public int SessionId { get; set; }

    [JsonPropertyName("SessionName")]
    public string? SessionName { get; set; }

    [JsonPropertyName("ExportedBy")]
    public string? ExportedBy { get; set; }

    [JsonPropertyName("DeviceInfo")]
    public string? DeviceInfo { get; set; }
}

public class SessionJson
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("Fecha")]
    public DateTime Fecha { get; set; }

    [JsonPropertyName("Lugar")]
    public string? Lugar { get; set; }

    [JsonPropertyName("TipoSesion")]
    public string? TipoSesion { get; set; }

    [JsonPropertyName("NombreSesion")]
    public string? NombreSesion { get; set; }

    [JsonPropertyName("PathSesion")]
    public string? PathSesion { get; set; }

    [JsonPropertyName("Participantes")]
    public string? Participantes { get; set; }

    [JsonPropertyName("Coach")]
    public string? Coach { get; set; }

    [JsonPropertyName("IsMerged")]
    public bool IsMerged { get; set; }

    [JsonPropertyName("VideoCount")]
    public int VideoCount { get; set; }

    [JsonPropertyName("IsSelected")]
    public bool IsSelected { get; set; }
}

public class VideoClipJson
{
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    [JsonPropertyName("SessionID")]
    public int SessionId { get; set; }

    [JsonPropertyName("AtletaID")]
    public int AtletaId { get; set; }

    [JsonPropertyName("Section")]
    public int Section { get; set; }

    [JsonPropertyName("CreationDate")]
    public long CreationDate { get; set; }

    [JsonPropertyName("ClipPath")]
    public string? ClipPath { get; set; }

    [JsonPropertyName("ThumbnailPath")]
    public string? ThumbnailPath { get; set; }

    [JsonPropertyName("ComparisonName")]
    public string? ComparisonName { get; set; }

    [JsonPropertyName("ClipDuration")]
    public double ClipDuration { get; set; }

    [JsonPropertyName("ClipSize")]
    public long ClipSize { get; set; }

    [JsonPropertyName("IsComparisonVideo")]
    public bool IsComparisonVideo { get; set; }

    [JsonPropertyName("BadgeText")]
    public string? BadgeText { get; set; }

    [JsonPropertyName("BadgeBackgroundColor")]
    public string? BadgeBackgroundColor { get; set; }
}

public class AthleteJson
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("Nombre")]
    public string? Nombre { get; set; }

    [JsonPropertyName("Apellido")]
    public string? Apellido { get; set; }

    [JsonPropertyName("Categoria")]
    public int Categoria { get; set; }

    [JsonPropertyName("CategoriaId")]
    public int CategoriaId { get; set; }

    [JsonPropertyName("Favorite")]
    public int Favorite { get; set; }

    [JsonPropertyName("IsFavorite")]
    public bool IsFavorite { get; set; }

    [JsonPropertyName("CategoriaNombre")]
    public string? CategoriaNombre { get; set; }

    [JsonPropertyName("NombreCompleto")]
    public string? NombreCompleto { get; set; }

    [JsonPropertyName("ChipDisplayString")]
    public string? ChipDisplayString { get; set; }

    [JsonPropertyName("IsSystemDefault")]
    public bool IsSystemDefault { get; set; }

    [JsonPropertyName("IsSelected")]
    public bool IsSelected { get; set; }
}

public class CategoryJson
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("NombreCategoria")]
    public string? NombreCategoria { get; set; }

    [JsonPropertyName("IsSystemDefault")]
    public bool IsSystemDefault { get; set; }
}

public class InputJson
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("SessionID")]
    public int SessionId { get; set; }

    [JsonPropertyName("VideoID")]
    public int VideoId { get; set; }

    [JsonPropertyName("AthleteID")]
    public int AthleteId { get; set; }

    [JsonPropertyName("CategoriaID")]
    public int CategoriaId { get; set; }

    [JsonPropertyName("InputTypeID")]
    public int InputTypeId { get; set; }

    [JsonPropertyName("InputDateTime")]
    public long InputDateTime { get; set; }

    [JsonPropertyName("InputValue")]
    public string? InputValue { get; set; }

    [JsonPropertyName("TimeStamp")]
    public long TimeStamp { get; set; }
    
    [JsonPropertyName("InputTypeObj")]
    public InputTypeJson? InputTypeObj { get; set; }
}

public class InputTypeJson
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }
    
    [JsonPropertyName("TipoInput")]
    public string? TipoInput { get; set; }
}

public class ValoracionJson
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("SessionID")]
    public int SessionId { get; set; }

    [JsonPropertyName("AthleteID")]
    public int AthleteId { get; set; }

    [JsonPropertyName("InputTypeID")]
    public int InputTypeId { get; set; }

    [JsonPropertyName("InputDateTime")]
    public long InputDateTime { get; set; }

    [JsonPropertyName("InputValue")]
    public int InputValue { get; set; }

    [JsonPropertyName("TimeStamp")]
    public string? TimeStamp { get; set; }
}

/// <summary>
/// Evento de cronometraje de ejecución (Inicio/Lap/Fin) para exportación JSON
/// </summary>
public class ExecutionTimingEventJson
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("VideoID")]
    public int VideoId { get; set; }

    [JsonPropertyName("SessionID")]
    public int SessionId { get; set; }

    [JsonPropertyName("AthleteID")]
    public int AthleteId { get; set; }

    [JsonPropertyName("SectionID")]
    public int SectionId { get; set; }

    /// <summary>
    /// 0 = Inicio, 1 = Lap, 2 = Fin
    /// </summary>
    [JsonPropertyName("Kind")]
    public int Kind { get; set; }

    /// <summary>
    /// Tiempo absoluto desde el inicio de la grabación (ms)
    /// </summary>
    [JsonPropertyName("ElapsedMs")]
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Tiempo parcial desde el último hito (inicio/lap anterior) (ms)
    /// </summary>
    [JsonPropertyName("SplitMs")]
    public long SplitMilliseconds { get; set; }

    /// <summary>
    /// Índice de lap dentro de una ejecución (1..N). Para Inicio/Fin será 0.
    /// </summary>
    [JsonPropertyName("LapIndex")]
    public int LapIndex { get; set; }

    /// <summary>
    /// Índice de ejecución dentro del vídeo (0..N). Permite varias ejecuciones.
    /// </summary>
    [JsonPropertyName("RunIndex")]
    public int RunIndex { get; set; }

    [JsonPropertyName("CreatedAt")]
    public long CreatedAtUnixSeconds { get; set; }
}
