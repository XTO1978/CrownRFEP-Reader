using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio que gestiona una cola de videos pendientes de subir a Wasabi.
/// Los videos grabados en iOS se añaden automáticamente a esta cola
/// y se suben en segundo plano cuando hay conexión.
/// </summary>
public class VideoUploadQueueService : IDisposable
{
    private readonly SyncService _syncService;
    private readonly ICloudBackendService _cloudService;
    private readonly DatabaseService _databaseService;
    
    private readonly ConcurrentQueue<UploadQueueItem> _uploadQueue = new();
    private readonly SemaphoreSlim _uploadSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    
    private Task? _processingTask;
    private bool _isProcessing;
    private bool _disposed;
    
    /// <summary>
    /// Se dispara cuando cambia el estado de la cola
    /// </summary>
    public event EventHandler<UploadQueueStatusEventArgs>? QueueStatusChanged;
    
    /// <summary>
    /// Se dispara cuando un video se sube exitosamente
    /// </summary>
    public event EventHandler<VideoUploadedEventArgs>? VideoUploaded;
    
    /// <summary>
    /// Se dispara cuando falla la subida de un video
    /// </summary>
    public event EventHandler<VideoUploadFailedEventArgs>? VideoUploadFailed;
    
    /// <summary>
    /// Número de items en la cola pendientes de subir
    /// </summary>
    public int PendingCount => _uploadQueue.Count;
    
    /// <summary>
    /// Indica si hay una subida en progreso
    /// </summary>
    public bool IsUploading => _isProcessing;

    public VideoUploadQueueService(
        SyncService syncService,
        ICloudBackendService cloudService,
        DatabaseService databaseService)
    {
        _syncService = syncService;
        _cloudService = cloudService;
        _databaseService = databaseService;
        
        // Iniciar el procesamiento de la cola
        StartProcessing();
    }

    /// <summary>
    /// Añade un video a la cola de subida.
    /// El video se subirá automáticamente cuando haya conexión.
    /// </summary>
    public void EnqueueVideo(VideoClip video, UploadPriority priority = UploadPriority.Normal)
    {
        if (video == null) return;
        
        var item = new UploadQueueItem
        {
            Video = video,
            Priority = priority,
            EnqueuedAt = DateTime.UtcNow,
            RetryCount = 0
        };
        
        _uploadQueue.Enqueue(item);
        
        System.Diagnostics.Debug.WriteLine($"[UploadQueue] Video {video.Id} añadido a la cola. Pendientes: {PendingCount}");
        
        NotifyQueueStatusChanged();
    }

    /// <summary>
    /// Añade un video por su ID a la cola de subida.
    /// </summary>
    public async Task EnqueueVideoByIdAsync(int videoId, UploadPriority priority = UploadPriority.Normal)
    {
        try
        {
            var video = await _databaseService.GetVideoClipByIdAsync(videoId);
            if (video != null)
            {
                EnqueueVideo(video, priority);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[UploadQueue] Video {videoId} no encontrado en la base de datos");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UploadQueue] Error cargando video {videoId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Inicia el procesamiento en segundo plano de la cola
    /// </summary>
    private void StartProcessing()
    {
        if (_processingTask != null && !_processingTask.IsCompleted)
            return;
            
        _processingTask = Task.Run(async () => await ProcessQueueAsync(_cts.Token));
    }

    /// <summary>
    /// Procesa la cola de subida en segundo plano
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Esperar hasta que haya algo en la cola
                while (_uploadQueue.IsEmpty && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                
                if (cancellationToken.IsCancellationRequested)
                    break;
                
                // Verificar autenticación
                if (!_cloudService.IsAuthenticated)
                {
                    System.Diagnostics.Debug.WriteLine("[UploadQueue] No autenticado, esperando...");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    continue;
                }
                
                // Verificar conectividad
                var healthCheck = await _cloudService.CheckHealthAsync();
                if (!healthCheck.IsHealthy)
                {
                    System.Diagnostics.Debug.WriteLine("[UploadQueue] Backend no disponible, esperando...");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    continue;
                }
                
                // Procesar el siguiente item
                if (_uploadQueue.TryDequeue(out var item))
                {
                    await ProcessUploadItemAsync(item, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UploadQueue] Error en el procesamiento: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Procesa un item individual de la cola
    /// </summary>
    private async Task ProcessUploadItemAsync(UploadQueueItem item, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;
            
        await _uploadSemaphore.WaitAsync(cancellationToken);
        _isProcessing = true;
        NotifyQueueStatusChanged();
        
        try
        {
            System.Diagnostics.Debug.WriteLine($"[UploadQueue] Subiendo video {item.Video.Id}...");
            
            var progress = new Progress<double>(p =>
            {
                System.Diagnostics.Debug.WriteLine($"[UploadQueue] Video {item.Video.Id}: {p:P0}");
            });
            
            var result = await _syncService.UploadVideoAsync(item.Video, progress);
            
            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[UploadQueue] Video {item.Video.Id} subido exitosamente");
                VideoUploaded?.Invoke(this, new VideoUploadedEventArgs(item.Video, result.RemotePath ?? ""));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[UploadQueue] Error subiendo video {item.Video.Id}: {result.ErrorMessage}");
                
                // Reintentar si no se han agotado los intentos
                item.RetryCount++;
                if (item.RetryCount < 3)
                {
                    System.Diagnostics.Debug.WriteLine($"[UploadQueue] Reintentando video {item.Video.Id} (intento {item.RetryCount + 1}/3)");
                    _uploadQueue.Enqueue(item);
                }
                else
                {
                    VideoUploadFailed?.Invoke(this, new VideoUploadFailedEventArgs(
                        item.Video, 
                        result.ErrorMessage ?? "Error desconocido",
                        item.RetryCount
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UploadQueue] Excepción subiendo video {item.Video.Id}: {ex.Message}");
            
            item.RetryCount++;
            if (item.RetryCount < 3)
            {
                _uploadQueue.Enqueue(item);
            }
            else
            {
                VideoUploadFailed?.Invoke(this, new VideoUploadFailedEventArgs(
                    item.Video, 
                    ex.Message,
                    item.RetryCount
                ));
            }
        }
        finally
        {
            _isProcessing = false;
            _uploadSemaphore.Release();
            NotifyQueueStatusChanged();
        }
    }

    /// <summary>
    /// Fuerza el procesamiento inmediato de la cola
    /// </summary>
    public void ProcessNow()
    {
        // Si el hilo de procesamiento está dormido, esto lo despertará
        StartProcessing();
    }

    /// <summary>
    /// Obtiene el estado actual de la cola
    /// </summary>
    public UploadQueueStatus GetStatus()
    {
        return new UploadQueueStatus
        {
            PendingCount = PendingCount,
            IsUploading = IsUploading,
            IsAuthenticated = _cloudService.IsAuthenticated
        };
    }

    private void NotifyQueueStatusChanged()
    {
        QueueStatusChanged?.Invoke(this, new UploadQueueStatusEventArgs(GetStatus()));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _cts.Cancel();
        _cts.Dispose();
        _uploadSemaphore.Dispose();
    }
}

/// <summary>
/// Prioridad de subida
/// </summary>
public enum UploadPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

/// <summary>
/// Item en la cola de subida
/// </summary>
public class UploadQueueItem
{
    public required VideoClip Video { get; init; }
    public UploadPriority Priority { get; init; }
    public DateTime EnqueuedAt { get; init; }
    public int RetryCount { get; set; }
}

/// <summary>
/// Estado de la cola de subida
/// </summary>
public class UploadQueueStatus
{
    public int PendingCount { get; init; }
    public bool IsUploading { get; init; }
    public bool IsAuthenticated { get; init; }
}

/// <summary>
/// Args para el evento de cambio de estado de la cola
/// </summary>
public class UploadQueueStatusEventArgs : EventArgs
{
    public UploadQueueStatus Status { get; }
    
    public UploadQueueStatusEventArgs(UploadQueueStatus status)
    {
        Status = status;
    }
}

/// <summary>
/// Args para el evento de video subido exitosamente
/// </summary>
public class VideoUploadedEventArgs : EventArgs
{
    public VideoClip Video { get; }
    public string RemotePath { get; }
    
    public VideoUploadedEventArgs(VideoClip video, string remotePath)
    {
        Video = video;
        RemotePath = remotePath;
    }
}

/// <summary>
/// Args para el evento de fallo de subida
/// </summary>
public class VideoUploadFailedEventArgs : EventArgs
{
    public VideoClip Video { get; }
    public string ErrorMessage { get; }
    public int RetryCount { get; }
    
    public VideoUploadFailedEventArgs(VideoClip video, string errorMessage, int retryCount)
    {
        Video = video;
        ErrorMessage = errorMessage;
        RetryCount = retryCount;
    }
}
