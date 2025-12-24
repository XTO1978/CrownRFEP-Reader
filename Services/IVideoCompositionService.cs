namespace CrownRFEP_Reader.Services;

/// <summary>
/// Parámetros para la exportación de video compuesto
/// </summary>
public class ParallelVideoExportParams
{
    /// <summary>
    /// Ruta del video 1
    /// </summary>
    public required string Video1Path { get; set; }

    /// <summary>
    /// Ruta del video 2
    /// </summary>
    public required string Video2Path { get; set; }

    /// <summary>
    /// Posición de inicio del video 1
    /// </summary>
    public TimeSpan Video1StartPosition { get; set; }

    /// <summary>
    /// Posición de inicio del video 2
    /// </summary>
    public TimeSpan Video2StartPosition { get; set; }

    /// <summary>
    /// True = horizontal (lado a lado), False = vertical (arriba/abajo)
    /// </summary>
    public bool IsHorizontalLayout { get; set; }

    /// <summary>
    /// Ruta donde guardar el video exportado
    /// </summary>
    public required string OutputPath { get; set; }

    /// <summary>
    /// Duración máxima de la exportación (null = hasta que termine el más corto)
    /// </summary>
    public TimeSpan? MaxDuration { get; set; }

    // Datos para overlay Video 1
    public string? Video1AthleteName { get; set; }
    public string? Video1Category { get; set; }
    public int? Video1Section { get; set; }
    public string? Video1Time { get; set; }
    public string? Video1Penalties { get; set; }

    // Datos para overlay Video 2
    public string? Video2AthleteName { get; set; }
    public string? Video2Category { get; set; }
    public int? Video2Section { get; set; }
    public string? Video2Time { get; set; }
    public string? Video2Penalties { get; set; }
}

/// <summary>
/// Resultado de la exportación
/// </summary>
public class VideoExportResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public long FileSizeBytes { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Servicio para composición y exportación de videos en paralelo
/// </summary>
public interface IVideoCompositionService
{
    /// <summary>
    /// Exporta dos videos en paralelo con overlays de información
    /// </summary>
    Task<VideoExportResult> ExportParallelVideosAsync(
        ParallelVideoExportParams parameters,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica si el servicio está disponible en la plataforma actual
    /// </summary>
    bool IsAvailable { get; }
}
