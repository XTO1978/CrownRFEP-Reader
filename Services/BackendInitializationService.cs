using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio responsable de inicializar la conexión con el backend al arrancar la app
/// y mantener sincronizada la galería de la organización para atletas y entrenadores.
/// </summary>
public class BackendInitializationService
{
    private readonly ICloudBackendService _cloudBackendService;
    private readonly SyncService _syncService;
    private readonly DatabaseService _databaseService;
    private readonly VideoUploadQueueService? _uploadQueueService;
    private readonly StatusBarService? _statusBarService;
    
    private const string LastGallerySyncKey = "LastGallerySyncUtc";
    private const int SyncIntervalMinutes = 15; // Intervalo mínimo entre sincronizaciones
    private static readonly TimeSpan BackendRetryInterval = TimeSpan.FromSeconds(15);
    
    private bool _isInitialized;
    private bool _isBackendAvailable;
    private bool _isCheckingBackend;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    
    public bool IsBackendAvailable => _isBackendAvailable;
    public bool IsInitialized => _isInitialized;
    
    /// <summary>
    /// Evento que se dispara cuando cambia el estado del backend
    /// </summary>
    public event EventHandler<BackendStatusChangedEventArgs>? BackendStatusChanged;

    public BackendInitializationService(
        ICloudBackendService cloudBackendService,
        SyncService syncService,
        DatabaseService databaseService,
        VideoUploadQueueService? uploadQueueService = null,
        StatusBarService? statusBarService = null)
    {
        _cloudBackendService = cloudBackendService;
        _syncService = syncService;
        _databaseService = databaseService;
        _uploadQueueService = uploadQueueService;
        _statusBarService = statusBarService;
    }

    /// <summary>
    /// Inicializa la conexión con el backend y verifica actualizaciones.
    /// Debe llamarse al arrancar la aplicación.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            AppLog.Info("BackendInit", "Ya inicializado, omitiendo...");
            return;
        }

        AppLog.Info("BackendInit", "🚀 Iniciando verificación del backend...");
        StartBackendMonitor();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await CheckBackendOnceAsync(runPostConnectTasks: true);

            _isInitialized = true;
            stopwatch.Stop();
            AppLog.Info("BackendInit", $"✅ Inicialización completada en {stopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            AppLog.Error("BackendInit", "Error durante inicialización", ex);
            _isBackendAvailable = false;
            _isInitialized = true;
            OnBackendStatusChanged(false, ex.Message);
            _statusBarService?.UpdateBackendStatus(isAvailable: false, errorMessage: ex.Message, isChecking: false);
        }
    }

    /// <summary>
    /// Fuerza una comprobación inmediata del backend (sin reiniciar estado de inicialización).
    /// </summary>
    public async Task ForceCheckAsync()
    {
        await CheckBackendOnceAsync(runPostConnectTasks: true);
    }

    private void StartBackendMonitor()
    {
        if (_monitorTask != null && !_monitorTask.IsCompleted)
            return;

        _monitorCts = new CancellationTokenSource();
        _monitorTask = Task.Run(async () => await MonitorBackendAsync(_monitorCts.Token));
    }

    private async Task MonitorBackendAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckBackendOnceAsync(runPostConnectTasks: false);
            }
            catch (Exception ex)
            {
                AppLog.Warn("BackendInit", $"Error monitorizando backend: {ex.Message}");
            }

            try
            {
                await Task.Delay(BackendRetryInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckBackendOnceAsync(bool runPostConnectTasks)
    {
        if (_isCheckingBackend)
            return;

        _isCheckingBackend = true;
        _statusBarService?.UpdateBackendStatus(isAvailable: _isBackendAvailable, errorMessage: null, isChecking: true);

        try
        {
            var healthResult = await _cloudBackendService.CheckHealthAsync();
            var previousAvailability = _isBackendAvailable;
            _isBackendAvailable = healthResult.IsHealthy;

            if (!healthResult.IsHealthy)
            {
                AppLog.Warn("BackendInit", $"⚠️ Backend no disponible: {healthResult.ErrorMessage}");
                OnBackendStatusChanged(false, healthResult.ErrorMessage);
                _statusBarService?.UpdateBackendStatus(isAvailable: false, errorMessage: healthResult.ErrorMessage, isChecking: false);
                return;
            }

            AppLog.Info("BackendInit", $"✅ Backend disponible (v{healthResult.Version})");
            OnBackendStatusChanged(true, null);
            _statusBarService?.UpdateBackendStatus(isAvailable: true, errorMessage: null, isChecking: false);

            if (!previousAvailability || runPostConnectTasks)
            {
                await RunPostConnectTasksAsync();
            }
        }
        finally
        {
            _isCheckingBackend = false;
        }
    }

    private async Task RunPostConnectTasksAsync()
    {
        if (_cloudBackendService.IsAuthenticated)
        {
            AppLog.Info("BackendInit", $"👤 Usuario autenticado: {_cloudBackendService.CurrentUserName} ({_cloudBackendService.TeamName})");

            if (ShouldSyncGallery())
            {
                await SyncGalleryAsync();
            }
            else
            {
                AppLog.Info("BackendInit", "⏳ Sincronización reciente, omitiendo...");
            }

            // Sincronizar sesiones locales con el backend para replicación entre dispositivos
            _ = SyncLocalSessionsToBackendAsync();

#if IOS
            if (_uploadQueueService != null)
            {
                await EnqueuePendingUploadsAsync();
                _uploadQueueService.ProcessNow();
                AppLog.Info("BackendInit", "📤 Cola de subida activada");
            }
#endif
        }
        else
        {
            AppLog.Info("BackendInit", "👤 Usuario no autenticado, omitiendo sincronización de galería");
        }
    }

    /// <summary>
    /// Sincroniza todas las sesiones locales con el backend para que estén disponibles
    /// para otros dispositivos de la organización.
    /// </summary>
    public async Task SyncLocalSessionsToBackendAsync()
    {
        try
        {
            AppLog.Info("BackendInit", "📋 Sincronizando sesiones locales con el backend...");
            var result = await _syncService.SyncAllSessionsToBackendAsync();
            if (result != null && result.Success)
            {
                AppLog.Info("BackendInit", $"📋 Sesiones sincronizadas: {result.Created} nuevas, {result.Updated} actualizadas");
            }
            else if (result != null)
            {
                AppLog.Warn("BackendInit", $"📋 Error sincronizando sesiones: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("BackendInit", "Error sincronizando sesiones", ex);
        }
    }

    /// <summary>
    /// Verifica si hay actualizaciones disponibles en la galería de la organización.
    /// </summary>
    public async Task<GallerySyncResult> CheckForUpdatesAsync()
    {
        if (!_cloudBackendService.IsAuthenticated)
        {
            return new GallerySyncResult(false, "No autenticado");
        }

        var lastSync = GetLastGallerySyncTime();
        return await _cloudBackendService.CheckForGalleryUpdatesAsync(lastSync);
    }

    /// <summary>
    /// Sincroniza la galería de la organización descargando archivos nuevos/actualizados.
    /// </summary>
    public async Task<GallerySyncResult> SyncGalleryAsync()
    {
        if (!_cloudBackendService.IsAuthenticated)
        {
            AppLog.Warn("BackendInit", "No se puede sincronizar galería: usuario no autenticado");
            return new GallerySyncResult(false, "No autenticado");
        }

        AppLog.Info("BackendInit", "🔄 Iniciando sincronización de galería...");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Obtener última fecha de sincronización
            var lastSync = GetLastGallerySyncTime();
            AppLog.Info("BackendInit", $"Última sincronización: {lastSync?.ToString("g") ?? "Nunca"}");

            // Verificar actualizaciones
            var result = await _cloudBackendService.CheckForGalleryUpdatesAsync(lastSync);

            if (!result.Success)
            {
                AppLog.Warn("BackendInit", $"Error al verificar actualizaciones: {result.ErrorMessage}");
                return result;
            }

            var totalUpdates = result.NewFilesCount + result.UpdatedFilesCount;
            
            if (totalUpdates == 0)
            {
                AppLog.Info("BackendInit", "✅ Galería al día, no hay actualizaciones");
                SaveLastGallerySyncTime(DateTime.UtcNow);
                return result;
            }

            AppLog.Info("BackendInit", $"📥 Encontrados {result.NewFilesCount} archivos nuevos, {result.UpdatedFilesCount} actualizados");

            // TODO: Aquí se pueden descargar los archivos nuevos/actualizados
            // Por ahora solo registramos que hay actualizaciones disponibles
            // La descarga real se puede hacer bajo demanda o en un proceso de fondo

            // Para archivos .crown y metadatos, podríamos descargarlos automáticamente
            // Para videos, solo descargar thumbnails y dejar los videos bajo demanda

            /*
            // Ejemplo de descarga automática de metadatos:
            if (result.NewFiles != null)
            {
                foreach (var file in result.NewFiles.Where(f => f.Name.EndsWith(".json")))
                {
                    await DownloadMetadataFileAsync(file);
                }
            }
            */

            SaveLastGallerySyncTime(DateTime.UtcNow);
            stopwatch.Stop();
            AppLog.Info("BackendInit", $"✅ Sincronización completada en {stopwatch.ElapsedMilliseconds}ms");

            return result;
        }
        catch (Exception ex)
        {
            AppLog.Error("BackendInit", "Error durante sincronización de galería", ex);
            return new GallerySyncResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Fuerza una resincronización completa de la galería.
    /// </summary>
    public async Task<GallerySyncResult> ForceSyncGalleryAsync()
    {
        // Limpiar la fecha de última sincronización para forzar descarga completa
        Preferences.Remove(LastGallerySyncKey);
        return await SyncGalleryAsync();
    }

    private bool ShouldSyncGallery()
    {
        var lastSync = GetLastGallerySyncTime();
        if (!lastSync.HasValue)
        {
            return true; // Nunca sincronizado
        }

        var timeSinceLastSync = DateTime.UtcNow - lastSync.Value;
        return timeSinceLastSync.TotalMinutes >= SyncIntervalMinutes;
    }

    private DateTime? GetLastGallerySyncTime()
    {
        try
        {
            var lastSyncStr = Preferences.Get(LastGallerySyncKey, string.Empty);
            if (!string.IsNullOrEmpty(lastSyncStr) && DateTime.TryParse(lastSyncStr, out var lastSync))
            {
                return lastSync;
            }
        }
        catch { }
        return null;
    }

    private void SaveLastGallerySyncTime(DateTime time)
    {
        try
        {
            Preferences.Set(LastGallerySyncKey, time.ToString("O"));
        }
        catch (Exception ex)
        {
            AppLog.Warn("BackendInit", $"Error guardando fecha de sincronización: {ex.Message}");
        }
    }

    private void OnBackendStatusChanged(bool isAvailable, string? error)
    {
        BackendStatusChanged?.Invoke(this, new BackendStatusChangedEventArgs(isAvailable, error));
    }

    /// <summary>
    /// Encola los videos locales que no han sido sincronizados todavía.
    /// Se llama al iniciar la app para reintentar subidas pendientes.
    /// </summary>
    private async Task EnqueuePendingUploadsAsync()
    {
        if (_uploadQueueService == null) return;

        try
        {
            // Obtener videos locales no sincronizados
            var pendingVideos = await _databaseService.GetUnsyncedVideoClipsAsync();
            
            if (pendingVideos == null || pendingVideos.Count == 0)
            {
                AppLog.Info("BackendInit", "📤 No hay videos pendientes de subir");
                return;
            }

            AppLog.Info("BackendInit", $"📤 Encolando {pendingVideos.Count} videos pendientes de subir");

            foreach (var video in pendingVideos)
            {
                _uploadQueueService.EnqueueVideo(video, UploadPriority.Normal);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("BackendInit", "Error encolando videos pendientes", ex);
        }
    }
}

/// <summary>
/// Argumentos del evento de cambio de estado del backend.
/// </summary>
public class BackendStatusChangedEventArgs : EventArgs
{
    public bool IsAvailable { get; }
    public string? ErrorMessage { get; }

    public BackendStatusChangedEventArgs(bool isAvailable, string? errorMessage)
    {
        IsAvailable = isAvailable;
        ErrorMessage = errorMessage;
    }
}
