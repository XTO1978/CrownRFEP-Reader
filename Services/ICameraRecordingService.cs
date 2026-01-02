namespace CrownRFEP_Reader.Services;

/// <summary>
/// Interfaz para el servicio de cámara de alta precisión para grabación de sesiones
/// </summary>
public interface ICameraRecordingService
{
    /// <summary>
    /// Indica si la cámara está disponible en este dispositivo
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Indica si se está grabando actualmente
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Indica si la cámara está en preview
    /// </summary>
    bool IsPreviewing { get; }

    /// <summary>
    /// Factor de zoom actual (1.0 = sin zoom)
    /// </summary>
    double CurrentZoom { get; }

    /// <summary>
    /// Factor de zoom mínimo disponible
    /// </summary>
    double MinZoom { get; }

    /// <summary>
    /// Factor de zoom máximo disponible
    /// </summary>
    double MaxZoom { get; }

    /// <summary>
    /// Evento disparado cuando cambia el estado de grabación
    /// </summary>
    event EventHandler<bool>? RecordingStateChanged;

    /// <summary>
    /// Evento disparado cuando cambia el zoom
    /// </summary>
    event EventHandler<double>? ZoomChanged;

    /// <summary>
    /// Inicializa la cámara y comienza el preview
    /// </summary>
    Task<bool> StartPreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detiene el preview de la cámara
    /// </summary>
    Task StopPreviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicia la grabación de video
    /// </summary>
    /// <param name="outputFilePath">Ruta donde guardar el video</param>
    Task StartRecordingAsync(string outputFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detiene la grabación y guarda el archivo
    /// </summary>
    /// <returns>Ruta del archivo grabado</returns>
    Task<string?> StopRecordingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia el zoom de la cámara
    /// </summary>
    /// <param name="zoomFactor">Factor de zoom (1.0 = sin zoom)</param>
    Task SetZoomAsync(double zoomFactor);

    /// <summary>
    /// Cambia entre cámara frontal y trasera
    /// </summary>
    Task SwitchCameraAsync();

    /// <summary>
    /// Obtiene el handle nativo de la vista de preview (para binding con control MAUI)
    /// </summary>
    object? GetPreviewHandle();

    /// <summary>
    /// Libera todos los recursos
    /// </summary>
    Task DisposeAsync();
}

/// <summary>
/// Implementación stub para plataformas no soportadas
/// </summary>
public class NullCameraRecordingService : ICameraRecordingService
{
    public bool IsAvailable => false;
    public bool IsRecording => false;
    public bool IsPreviewing => false;
    public double CurrentZoom => 1.0;
    public double MinZoom => 1.0;
    public double MaxZoom => 1.0;

    public event EventHandler<bool>? RecordingStateChanged;
    public event EventHandler<double>? ZoomChanged;

    public Task<bool> StartPreviewAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task StopPreviewAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StartRecordingAsync(string outputFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<string?> StopRecordingAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task SetZoomAsync(double zoomFactor) => Task.CompletedTask;
    public Task SwitchCameraAsync() => Task.CompletedTask;
    public object? GetPreviewHandle() => null;
    public Task DisposeAsync() => Task.CompletedTask;
}
