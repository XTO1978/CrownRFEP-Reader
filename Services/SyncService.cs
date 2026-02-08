using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CrownRFEP_Reader.Models;
using Microsoft.Maui.Storage;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio de sincronización entre almacenamiento local y remoto (Wasabi vía backend).
/// </summary>
public class SyncService
{
    private readonly StoragePathService _pathService;
    private readonly ICloudBackendService _cloudService;
    private readonly DatabaseService _databaseService;
    private readonly HttpClient _httpClient;
    private readonly HashSet<int> _sessionMetadataUploaded = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SyncService(
        StoragePathService pathService,
        ICloudBackendService cloudService,
        DatabaseService databaseService)
    {
        _pathService = pathService;
        _cloudService = cloudService;
        _databaseService = databaseService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    /// <summary>
    /// Sube un video al servidor remoto
    /// </summary>
    public async Task<SyncResult> UploadVideoAsync(VideoClip video, IProgress<double>? progress = null)
    {
        var result = new SyncResult { VideoId = video.Id };

        try
        {
            if (!_cloudService.IsAuthenticated)
            {
                result.Success = false;
                result.ErrorMessage = "No autenticado en el servidor";
                return result;
            }

            // Obtener ruta local absoluta.
            // Priorizar LocalClipPath (ruta absoluta de extracción) sobre ClipPath (ruta relativa que puede no resolver
            // correctamente para archivos importados desde .crown).
            var localPath = ResolveLocalVideoPath(video);
            if (!File.Exists(localPath))
            {
                result.Success = false;
                result.ErrorMessage = $"Archivo local no encontrado: {localPath}";
                return result;
            }

            // Generar ruta remota
            var remotePath = _pathService.GetRemoteVideoPath(video.SessionId, video.Id);

            progress?.Report(0.1);

            // Obtener URL firmada para subir (pasamos la ruta completa)
            var signResult = await _cloudService.GetUploadUrlAsync(remotePath, "video/mp4");

            if (!signResult.Success || string.IsNullOrEmpty(signResult.Url))
            {
                result.Success = false;
                result.ErrorMessage = signResult.ErrorMessage ?? "No se pudo obtener URL de subida";
                return result;
            }

            progress?.Report(0.2);

            // Subir el archivo
            var fileBytes = await File.ReadAllBytesAsync(localPath);
            var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");

            // Añadir headers adicionales si los hay
            if (signResult.Headers != null)
            {
                foreach (var header in signResult.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            progress?.Report(0.5);

            var response = await _httpClient.PutAsync(signResult.Url, content);

            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"Error al subir: {response.StatusCode}";
                return result;
            }

            progress?.Report(0.9);

            // Actualizar estado de sincronización en DB
            video.IsSynced = 1;
            video.LastSyncUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            video.Source = "both";
            await _databaseService.UpdateVideoClipAsync(video);

            // Subir metadatos asociados
            await UploadVideoMetadataAsync(video);

            // Subir metadatos de la sesión (session.json)
            await UploadSessionMetadataAsync(video.SessionId);

            // Subir thumbnail si existe
            await UploadThumbnailAsync(video);

            progress?.Report(1.0);

            result.Success = true;
            result.RemotePath = remotePath;
            System.Diagnostics.Debug.WriteLine($"[Sync] Video {video.Id} subido correctamente a {remotePath}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[Sync] Error subiendo video {video.Id}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Sube una videolección al servidor remoto (S3) en la carpeta lessons/{lessonId}.mp4
    /// </summary>
    public async Task<bool> UploadVideoLessonAsync(VideoLesson lesson, IProgress<double>? progress = null)
    {
        try
        {
            if (!_cloudService.IsAuthenticated)
            {
                System.Diagnostics.Debug.WriteLine("[Sync] UploadVideoLessonAsync: no autenticado");
                return false;
            }

            if (string.IsNullOrWhiteSpace(lesson.FilePath) || !File.Exists(lesson.FilePath))
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] UploadVideoLessonAsync: archivo no encontrado: {lesson.FilePath}");
                return false;
            }

            var remotePath = _pathService.GetRemoteLessonPath(lesson.Id);
            progress?.Report(0.1);

            var signResult = await _cloudService.GetUploadUrlAsync(remotePath, "video/mp4");
            if (!signResult.Success || string.IsNullOrEmpty(signResult.Url))
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] UploadVideoLessonAsync: no se pudo obtener URL de subida: {signResult.ErrorMessage}");
                return false;
            }

            progress?.Report(0.2);

            var fileBytes = await File.ReadAllBytesAsync(lesson.FilePath);
            var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

            if (signResult.Headers != null)
            {
                foreach (var header in signResult.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            progress?.Report(0.5);

            var response = await _httpClient.PutAsync(signResult.Url, content);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] UploadVideoLessonAsync: error HTTP {response.StatusCode}");
                return false;
            }

            progress?.Report(1.0);
            System.Diagnostics.Debug.WriteLine($"[Sync] Videolección {lesson.Id} subida a {remotePath}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Error subiendo videolección {lesson.Id}: {ex.Message}");
            return false;
        }
    }

    private async Task UploadSessionMetadataAsync(int sessionId)
    {
        if (_sessionMetadataUploaded.Contains(sessionId)) return;

        try
        {
            var session = await _databaseService.GetSessionByIdAsync(sessionId);
            if (session == null) return;

            string? coachName = session.Coach;
            if (string.IsNullOrWhiteSpace(coachName) && !string.IsNullOrWhiteSpace(_cloudService.CurrentUserName))
            {
                coachName = _cloudService.CurrentUserName;
            }
            if (string.IsNullOrWhiteSpace(coachName))
            {
                var profile = await _databaseService.GetUserProfileAsync();
                coachName = profile?.NombreCompleto;
                if (string.IsNullOrWhiteSpace(coachName))
                {
                    coachName = profile?.Nombre;
                }

                if (!string.IsNullOrWhiteSpace(coachName))
                {
                    session.Coach = coachName;
                    await _databaseService.SaveSessionAsync(session);
                }
            }

            var metadata = new SessionMetadataPayload
            {
                SessionId = session.Id,
                SessionName = session.NombreSesion ?? session.DisplayName,
                Place = session.Lugar,
                Coach = coachName ?? session.Coach,
                SessionType = session.TipoSesion,
                SessionDateUtc = session.Fecha
            };

            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var remotePath = _pathService.GetRemoteSessionMetadataPath(sessionId);
            var signResult = await _cloudService.GetUploadUrlAsync(remotePath, "application/json");

            if (!signResult.Success || string.IsNullOrWhiteSpace(signResult.Url))
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] No se pudo obtener URL de subida para metadatos de sesión {sessionId}");
                return;
            }

            if (signResult.Headers != null)
            {
                foreach (var header in signResult.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var response = await _httpClient.PutAsync(signResult.Url, content);
            if (response.IsSuccessStatusCode)
            {
                _sessionMetadataUploaded.Add(sessionId);
                System.Diagnostics.Debug.WriteLine($"[Sync] Metadatos de sesión {sessionId} subidos: {remotePath}");

                // También sincronizar la sesión al backend DB para replicación entre dispositivos
                _ = SyncSessionToBackendAsync(sessionId);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Error subiendo metadatos de sesión {sessionId}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Error subiendo metadatos de sesión {sessionId}: {ex.Message}");
        }
    }

    private sealed class SessionMetadataPayload
    {
        public int SessionId { get; set; }
        public string? SessionName { get; set; }
        public string? Place { get; set; }
        public string? Coach { get; set; }
        public string? SessionType { get; set; }
        public long SessionDateUtc { get; set; }
    }

    /// <summary>
    /// Sincroniza los metadatos de una sesión con la base de datos del backend.
    /// Esto permite que otros dispositivos de la organización vean la sesión.
    /// </summary>
    public async Task<bool> SyncSessionToBackendAsync(int sessionId)
    {
        try
        {
            if (!_cloudService.IsAuthenticated) return false;

            var session = await _databaseService.GetSessionByIdAsync(sessionId);
            if (session == null) return false;

            var videoCount = (await _databaseService.GetVideoClipsBySessionAsync(sessionId)).Count;

            var payload = new RemoteSessionPayload
            {
                LocalSessionId = session.Id,
                DeviceId = await GetDeviceIdAsync(),
                SessionName = session.NombreSesion ?? session.DisplayName,
                Place = session.Lugar,
                Coach = session.Coach,
                SessionType = session.TipoSesion,
                SessionDateUtc = session.Fecha,
                Participants = session.Participantes,
                IsMerged = session.IsMerged,
                Icon = session.Icon,
                IconColor = session.IconColor,
                VideoCount = videoCount
            };

            var result = await _cloudService.SyncSessionToRemoteAsync(payload);
            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Sesión {sessionId} sincronizada al backend (remoteId={result.Session?.Id}, isNew={result.IsNew})");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Error sincronizando sesión {sessionId} al backend: {result.ErrorMessage}");
            }

            return result.Success;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Error en SyncSessionToBackendAsync({sessionId}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sincroniza todas las sesiones locales con el backend en una sola operación batch.
    /// </summary>
    public async Task<RemoteSessionBatchResult?> SyncAllSessionsToBackendAsync()
    {
        try
        {
            if (!_cloudService.IsAuthenticated) return null;

            var sessions = await _databaseService.GetAllSessionsAsync();
            if (sessions == null || sessions.Count == 0) return null;

            var deviceId = await GetDeviceIdAsync();
            var payloads = new List<RemoteSessionPayload>();

            foreach (var session in sessions)
            {
                if (session.IsDeleted == 1) continue;

                var videoCount = (await _databaseService.GetVideoClipsBySessionAsync(session.Id)).Count;

                payloads.Add(new RemoteSessionPayload
                {
                    LocalSessionId = session.Id,
                    DeviceId = deviceId,
                    SessionName = session.NombreSesion ?? session.DisplayName,
                    Place = session.Lugar,
                    Coach = session.Coach,
                    SessionType = session.TipoSesion,
                    SessionDateUtc = session.Fecha,
                    Participants = session.Participantes,
                    IsMerged = session.IsMerged,
                    Icon = session.Icon,
                    IconColor = session.IconColor,
                    VideoCount = videoCount
                });
            }

            if (payloads.Count == 0) return null;

            var result = await _cloudService.SyncSessionsBatchAsync(payloads);
            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Batch session sync: {result.Created} creadas, {result.Updated} actualizadas de {result.Total}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Error en batch session sync: {result.ErrorMessage}");
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Error en SyncAllSessionsToBackendAsync: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> GetDeviceIdAsync()
    {
        try
        {
            var stored = await SecureStorage.GetAsync("CloudBackend_DeviceId");
            if (!string.IsNullOrWhiteSpace(stored))
                return stored;
        }
        catch { }

        var fallback = Preferences.Get("CloudBackend_DeviceId", string.Empty);
        return string.IsNullOrWhiteSpace(fallback) ? "unknown" : fallback;
    }

    /// <summary>
    /// Descarga un video del servidor remoto
    /// </summary>
    public async Task<SyncResult> DownloadVideoAsync(VideoClip video, IProgress<double>? progress = null)
    {
        var result = new SyncResult { VideoId = video.Id };

        try
        {
            if (!_cloudService.IsAuthenticated)
            {
                result.Success = false;
                result.ErrorMessage = "No autenticado en el servidor";
                return result;
            }

            // Generar ruta remota: usar ClipPath si existe, sino reconstruir desde IDs
            var remotePath = video.ClipPath ?? _pathService.GetRemoteVideoPath(video.SessionId, video.Id);
            var canonicalPath = _pathService.GetRemoteVideoPath(video.SessionId, video.Id);

            System.Diagnostics.Debug.WriteLine($"[Sync] Descarga video {video.Id}: ClipPath='{video.ClipPath}', remotePath='{remotePath}', canonical='{canonicalPath}', Source='{video.Source}'");

            progress?.Report(0.1);

            // Intentar descargar con remotePath primero, luego con canonicalPath si falla
            var downloadResult = await TryDownloadFromPathAsync(remotePath, progress);

            // Si falla con 404 y tenemos una ruta canónica diferente, intentar con ella
            if (!downloadResult.Success && downloadResult.StatusCode == System.Net.HttpStatusCode.NotFound
                && !string.Equals(remotePath, canonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Ruta '{remotePath}' no encontrada, reintentando con ruta canónica '{canonicalPath}'");
                downloadResult = await TryDownloadFromPathAsync(canonicalPath, progress);
            }

            if (!downloadResult.Success)
            {
                result.Success = false;
                result.ErrorMessage = downloadResult.ErrorMessage;
                System.Diagnostics.Debug.WriteLine($"[Sync] Error descargando video {video.Id}: {downloadResult.ErrorMessage}");
                return result;
            }

            progress?.Report(0.5);

            // Preparar ruta local
            _pathService.EnsureSessionDirectoryExists(video.SessionId);
            var localPath = _pathService.GetLocalVideoPath(video.SessionId, video.Id);

            await File.WriteAllBytesAsync(localPath, downloadResult.FileBytes!);

            progress?.Report(0.9);

            // Actualizar rutas en DB
            video.LocalClipPath = localPath;
            video.ClipPath = _pathService.ToRelativePath(localPath);
            video.IsSynced = 1;
            video.LastSyncUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            video.Source = "both";
            video.ClipSize = downloadResult.FileBytes!.Length;
            await _databaseService.UpdateVideoClipAsync(video);

            // Notificar cambios en UI
            video.OnPropertyChanged(nameof(video.Source));
            video.OnPropertyChanged(nameof(video.IsLocalAvailable));
            video.OnPropertyChanged(nameof(video.SyncStatusIcon));
            video.OnPropertyChanged(nameof(video.SyncStatusColor));
            video.OnPropertyChanged(nameof(video.SyncStatusText));
            video.OnPropertyChanged(nameof(video.ShowSyncBadge));

            // Descargar y aplicar metadatos asociados
            await DownloadAndApplyVideoMetadataAsync(video);

            // Descargar thumbnail si existe
            await DownloadThumbnailAsync(video);

            progress?.Report(1.0);

            result.Success = true;
            result.LocalPath = localPath;
            System.Diagnostics.Debug.WriteLine($"[Sync] Video {video.Id} descargado correctamente a {localPath}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[Sync] Error descargando video {video.Id}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Intenta descargar un archivo desde una ruta remota específica
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage, byte[]? FileBytes, System.Net.HttpStatusCode? StatusCode)> TryDownloadFromPathAsync(
        string remotePath, IProgress<double>? progress)
    {
        try
        {
            var signResult = await _cloudService.GetDownloadUrlAsync(remotePath);

            if (!signResult.Success || string.IsNullOrEmpty(signResult.Url))
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Error obteniendo URL para '{remotePath}': {signResult.ErrorMessage}");
                // Si el error contiene "no encontrado" o "not found", indicar 404 para activar fallback
                var isNotFound = signResult.ErrorMessage?.Contains("no encontrado", StringComparison.OrdinalIgnoreCase) == true
                    || signResult.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true;
                return (false, signResult.ErrorMessage ?? "No se pudo obtener URL de descarga", null,
                    isNotFound ? System.Net.HttpStatusCode.NotFound : null);
            }

            System.Diagnostics.Debug.WriteLine($"[Sync] URL firmada obtenida para '{remotePath}': {signResult.Url?.Substring(0, Math.Min(signResult.Url?.Length ?? 0, 120))}...");

            progress?.Report(0.2);

            var response = await _httpClient.GetAsync(signResult.Url);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] HTTP {response.StatusCode} descargando '{remotePath}'");
                return (false, $"Error al descargar: {response.StatusCode} (ruta: {remotePath})", null, response.StatusCode);
            }

            var fileBytes = await response.Content.ReadAsByteArrayAsync();
            System.Diagnostics.Debug.WriteLine($"[Sync] Descargados {fileBytes.Length} bytes para '{remotePath}'");
            return (true, null, fileBytes, response.StatusCode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Excepción descargando '{remotePath}': {ex.Message}");
            return (false, $"Error: {ex.Message}", null, null);
        }
    }

    /// <summary>
    /// Sube el thumbnail de un video
    /// </summary>
    public async Task<SyncResult> UploadThumbnailAsync(VideoClip video)
    {
        var result = new SyncResult { VideoId = video.Id };

        try
        {
            if (!_cloudService.IsAuthenticated)
            {
                result.Success = false;
                result.ErrorMessage = "No autenticado";
                return result;
            }

            // Priorizar LocalThumbnailPath (ruta absoluta real) sobre ThumbnailPath.
            var localThumbPath = ResolveLocalThumbnailPath(video);
            if (!File.Exists(localThumbPath))
            {
                result.Success = false;
                result.ErrorMessage = "Thumbnail local no encontrado";
                return result;
            }

            var remotePath = _pathService.GetRemoteThumbnailPath(video.SessionId, video.Id);

            var signResult = await _cloudService.GetUploadUrlAsync(remotePath, "image/jpeg");

            if (!signResult.Success || string.IsNullOrEmpty(signResult.Url))
            {
                result.Success = false;
                result.ErrorMessage = signResult.ErrorMessage ?? "No se pudo obtener URL de subida";
                return result;
            }

            var fileBytes = await File.ReadAllBytesAsync(localThumbPath);
            var content = new ByteArrayContent(fileBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

            var response = await _httpClient.PutAsync(signResult.Url, content);

            result.Success = response.IsSuccessStatusCode;
            result.RemotePath = remotePath;

            if (!result.Success)
            {
                result.ErrorMessage = $"Error: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Descarga el thumbnail de un video remoto
    /// </summary>
    public async Task<SyncResult> DownloadThumbnailAsync(VideoClip video)
    {
        var result = new SyncResult { VideoId = video.Id };

        try
        {
            if (!_cloudService.IsAuthenticated)
            {
                result.Success = false;
                result.ErrorMessage = "No autenticado";
                return result;
            }

            var remotePath = _pathService.GetRemoteThumbnailPath(video.SessionId, video.Id);
            var signResult = await _cloudService.GetDownloadUrlAsync(remotePath);

            if (!signResult.Success || string.IsNullOrEmpty(signResult.Url))
            {
                // No hay thumbnail remoto, no es un error crítico
                result.Success = true;
                return result;
            }

            var response = await _httpClient.GetAsync(signResult.Url);
            if (!response.IsSuccessStatusCode)
            {
                result.Success = true; // No es crítico
                return result;
            }

            var fileBytes = await response.Content.ReadAsByteArrayAsync();
            var localThumbPath = _pathService.GetLocalThumbnailPath(video.SessionId, video.Id);

            // Asegurar directorio
            var dir = Path.GetDirectoryName(localThumbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllBytesAsync(localThumbPath, fileBytes);

            // Actualizar video con ruta del thumbnail
            video.LocalThumbnailPath = localThumbPath;
            video.ThumbnailPath = _pathService.ToRelativePath(localThumbPath);
            await _databaseService.UpdateVideoClipAsync(video);

            result.Success = true;
            result.LocalPath = localThumbPath;
            System.Diagnostics.Debug.WriteLine($"[Sync] Thumbnail {video.Id} descargado correctamente");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[Sync] Error descargando thumbnail {video.Id}: {ex.Message}");
        }

        return result;
    }

    // ==================== SINCRONIZACIÓN DE METADATOS ====================

    /// <summary>
    /// Construye el objeto VideoSyncData con todos los metadatos asociados a un video
    /// </summary>
    public async Task<VideoSyncData> BuildVideoSyncDataAsync(VideoClip video)
    {
        var syncData = new VideoSyncData
        {
            Version = 1,
            VideoId = video.Id,
            SessionId = video.SessionId,
            Video = VideoClipSyncData.FromVideoClip(video),
            SyncedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Obtener atleta asignado
        if (video.AtletaId > 0)
        {
            var athlete = await _databaseService.GetAthleteByIdAsync(video.AtletaId);
            syncData.Athlete = AthleteSyncData.FromAthlete(athlete);
        }

        // Obtener inputs (eventos y etiquetas)
        var inputs = await _databaseService.GetInputsForVideoAsync(video.Id);
        var inputSync = inputs.Select(InputSyncData.FromInput).ToList();

        // Para eventos, rellenar InputValue con el nombre del tag de evento
        if (inputSync.Count > 0)
        {
            var eventTagMap = new Dictionary<int, string>();
            try
            {
                var eventTags = await _databaseService.GetAllEventTagsAsync();
                eventTagMap = eventTags
                    .Where(t => t.Id > 0 && !string.IsNullOrWhiteSpace(t.Nombre))
                    .ToDictionary(t => t.Id, t => t.Nombre!);
            }
            catch
            {
                // Ignorar si no se pueden cargar tags de evento
            }

            foreach (var input in inputSync)
            {
                if (input.IsEvent == 1 && string.IsNullOrWhiteSpace(input.InputValue))
                {
                    if (eventTagMap.TryGetValue(input.InputTypeId, out var eventName))
                    {
                        input.InputValue = eventName;
                    }
                }
            }
        }

        syncData.Inputs = inputSync;

        // Obtener eventos de timing/cronometraje
        var timingEvents = await _databaseService.GetExecutionTimingEventsByVideoAsync(video.Id);
        syncData.TimingEvents = timingEvents.Select(TimingEventSyncData.FromEvent).ToList();

        // Obtener tags únicos utilizados
        var tagIds = inputs.Where(i => i.InputTypeId > 0).Select(i => i.InputTypeId).Distinct().ToList();
        if (tagIds.Count > 0)
        {
            var allTags = await _databaseService.GetAllTagsAsync();
            syncData.Tags = allTags.Where(t => tagIds.Contains(t.Id)).Select(TagSyncData.FromTag).ToList();
        }

        return syncData;
    }

    /// <summary>
    /// Sube los metadatos de un video al servidor remoto
    /// </summary>
    public async Task<SyncResult> UploadVideoMetadataAsync(VideoClip video)
    {
        var result = new SyncResult { VideoId = video.Id };

        try
        {
            if (!_cloudService.IsAuthenticated)
            {
                result.Success = false;
                result.ErrorMessage = "No autenticado";
                return result;
            }

            // Construir datos de sincronización
            var syncData = await BuildVideoSyncDataAsync(video);

            // Serializar a JSON
            var jsonContent = JsonSerializer.Serialize(syncData, _jsonOptions);
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);

            // Generar ruta remota para el archivo de metadatos
            var remotePath = _pathService.GetRemoteMetadataPath(video.SessionId, video.Id);

            var signResult = await _cloudService.GetUploadUrlAsync(remotePath, "application/json");

            if (!signResult.Success || string.IsNullOrEmpty(signResult.Url))
            {
                result.Success = false;
                result.ErrorMessage = signResult.ErrorMessage ?? "No se pudo obtener URL de subida para metadatos";
                return result;
            }

            var content = new ByteArrayContent(jsonBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var response = await _httpClient.PutAsync(signResult.Url, content);

            result.Success = response.IsSuccessStatusCode;
            result.RemotePath = remotePath;

            if (result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Metadatos video {video.Id} subidos: {syncData.Inputs.Count} inputs, {syncData.TimingEvents.Count} timing events");
            }
            else
            {
                result.ErrorMessage = $"Error subiendo metadatos: {response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[Sync] Error subiendo metadatos video {video.Id}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Descarga y aplica los metadatos de un video desde el servidor remoto
    /// </summary>
    public async Task<SyncResult> DownloadAndApplyVideoMetadataAsync(VideoClip video)
    {
        var result = new SyncResult { VideoId = video.Id };

        try
        {
            if (!_cloudService.IsAuthenticated)
            {
                result.Success = false;
                result.ErrorMessage = "No autenticado";
                return result;
            }

            var remotePath = _pathService.GetRemoteMetadataPath(video.SessionId, video.Id);
            var signResult = await _cloudService.GetDownloadUrlAsync(remotePath);

            if (!signResult.Success || string.IsNullOrEmpty(signResult.Url))
            {
                // No hay metadatos remotos, no es un error crítico
                System.Diagnostics.Debug.WriteLine($"[Sync] No hay metadatos remotos para video {video.Id}");
                result.Success = true;
                return result;
            }

            var response = await _httpClient.GetAsync(signResult.Url);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Error descargando metadatos: {response.StatusCode}");
                result.Success = true; // No es crítico
                return result;
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var syncData = JsonSerializer.Deserialize<VideoSyncData>(jsonContent, _jsonOptions);

            if (syncData == null)
            {
                result.Success = true;
                return result;
            }

            // Aplicar los metadatos al sistema local
            await ApplyVideoMetadataAsync(video, syncData);

            result.Success = true;
            System.Diagnostics.Debug.WriteLine($"[Sync] Metadatos video {video.Id} aplicados: {syncData.Inputs.Count} inputs, {syncData.TimingEvents.Count} timing events");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            System.Diagnostics.Debug.WriteLine($"[Sync] Error descargando metadatos video {video.Id}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Aplica los metadatos sincronizados a la base de datos local
    /// </summary>
    private async Task ApplyVideoMetadataAsync(VideoClip video, VideoSyncData syncData)
    {
        // 1. Actualizar datos del video si hay cambios
        if (syncData.Video != null)
        {
            if (!string.IsNullOrEmpty(syncData.Video.ComparisonName))
                video.ComparisonName = syncData.Video.ComparisonName;
            if (syncData.Video.Section > 0)
                video.Section = syncData.Video.Section;
            
            await _databaseService.UpdateVideoClipAsync(video);
        }

        // 2. Sincronizar atleta si no existe localmente
        if (syncData.Athlete != null && syncData.Athlete.Id > 0)
        {
            var localAthlete = await _databaseService.GetAthleteByIdAsync(syncData.Athlete.Id);
            if (localAthlete == null)
            {
                // Crear atleta localmente
                var newAthlete = new Athlete
                {
                    Id = syncData.Athlete.Id,
                    Nombre = syncData.Athlete.Nombre ?? "",
                    Apellido = syncData.Athlete.Apellido ?? "",
                    Category = syncData.Athlete.Category,
                    CategoriaId = syncData.Athlete.CategoriaId,
                    Favorite = syncData.Athlete.Favorite
                };
                await _databaseService.SaveAthleteAsync(newAthlete);
            }
            
            // Asignar atleta al video si no está asignado
            if (video.AtletaId <= 0)
            {
                video.AtletaId = syncData.Athlete.Id;
                await _databaseService.UpdateVideoClipAsync(video);
            }
        }

        // 3. Sincronizar inputs (eventos y etiquetas)
        // Primero, obtener inputs locales existentes para evitar duplicados
        var localInputs = await _databaseService.GetInputsForVideoAsync(video.Id);
        var localInputTimestamps = new HashSet<long>(localInputs.Select(i => i.TimeStamp));

        foreach (var inputData in syncData.Inputs)
        {
            // Solo añadir si no existe un input con el mismo timestamp
            if (!localInputTimestamps.Contains(inputData.Timestamp))
            {
                var input = inputData.ToInput();
                // Actualizar IDs locales
                input.Id = 0; // Nuevo ID
                input.VideoId = video.Id;
                input.SessionId = video.SessionId;
                
                await _databaseService.SaveInputAsync(input);
            }
        }

        // 4. Sincronizar eventos de timing
        // Primero eliminar eventos existentes para evitar duplicados
        await _databaseService.DeleteExecutionTimingEventsByVideoAsync(video.Id);

        var newTimingEvents = syncData.TimingEvents.Select(t =>
        {
            var evt = t.ToEvent();
            evt.Id = 0; // Nuevo ID
            evt.VideoId = video.Id;
            evt.SessionId = video.SessionId;
            return evt;
        }).ToList();

        if (newTimingEvents.Count > 0)
        {
            await _databaseService.InsertExecutionTimingEventsAsync(newTimingEvents);
        }

        System.Diagnostics.Debug.WriteLine($"[Sync] Metadatos aplicados: {syncData.Inputs.Count} inputs, {newTimingEvents.Count} timing events");
    }

    /// <summary>
    /// Lista los archivos remotos de una sesión
    /// </summary>
    public async Task<List<RemoteFileInfo>> ListRemoteSessionFilesAsync(int sessionId)
    {
        var files = new List<RemoteFileInfo>();

        try
        {
            if (!_cloudService.IsAuthenticated)
                return files;

            var prefix = _pathService.GetRemoteSessionPath(sessionId);
            var result = await _cloudService.ListFilesAsync(prefix);

            if (result.Success && result.Files != null)
            {
                foreach (var file in result.Files)
                {
                    files.Add(new RemoteFileInfo
                    {
                        Key = file.Key,
                        Name = file.Name,
                        Size = file.Size,
                        LastModified = file.LastModified,
                        IsFolder = file.IsFolder
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Error listando archivos remotos: {ex.Message}");
        }

        return files;
    }

    /// <summary>
    /// Resuelve la ruta local real de un video.
    /// Prioriza LocalClipPath (ruta absoluta de extracción/.crown) sobre ClipPath.
    /// </summary>
    private string ResolveLocalVideoPath(VideoClip video)
    {
        // 1) LocalClipPath suele ser una ruta absoluta real (extraída de .crown, grabada, etc.)
        if (!string.IsNullOrEmpty(video.LocalClipPath))
        {
            if (Path.IsPathRooted(video.LocalClipPath) && File.Exists(video.LocalClipPath))
                return video.LocalClipPath;
        }

        // 2) ClipPath puede ser relativa al media root – resolverla
        if (!string.IsNullOrEmpty(video.ClipPath))
        {
            var resolved = _pathService.ToAbsoluteLocalPath(video.ClipPath);
            if (File.Exists(resolved))
                return resolved;
        }

        // 3) Fallback: intentar LocalClipPath sin verificar existencia (para que el mensaje de error sea útil)
        return !string.IsNullOrEmpty(video.LocalClipPath)
            ? video.LocalClipPath
            : _pathService.ToAbsoluteLocalPath(video.ClipPath ?? "");
    }

    /// <summary>
    /// Resuelve la ruta local real de un thumbnail.
    /// Prioriza LocalThumbnailPath sobre ThumbnailPath.
    /// </summary>
    private string ResolveLocalThumbnailPath(VideoClip video)
    {
        if (!string.IsNullOrEmpty(video.LocalThumbnailPath))
        {
            if (Path.IsPathRooted(video.LocalThumbnailPath) && File.Exists(video.LocalThumbnailPath))
                return video.LocalThumbnailPath;
        }

        if (!string.IsNullOrEmpty(video.ThumbnailPath))
        {
            var resolved = _pathService.ToAbsoluteLocalPath(video.ThumbnailPath);
            if (File.Exists(resolved))
                return resolved;
        }

        return !string.IsNullOrEmpty(video.LocalThumbnailPath)
            ? video.LocalThumbnailPath
            : _pathService.ToAbsoluteLocalPath(video.ThumbnailPath ?? "");
    }

    /// <summary>
    /// Sincroniza todos los videos pendientes de una sesión
    /// </summary>
    public async Task<BatchSyncResult> SyncSessionAsync(int sessionId, SyncDirection direction, IProgress<(int current, int total, string message)>? progress = null)
    {
        var result = new BatchSyncResult();

        try
        {
            var videos = await _databaseService.GetVideoClipsBySessionAsync(sessionId);
            result.TotalCount = videos.Count;

            for (int i = 0; i < videos.Count; i++)
            {
                var video = videos[i];
                progress?.Report((i + 1, videos.Count, $"Sincronizando video {i + 1} de {videos.Count}..."));

                SyncResult syncResult;
                if (direction == SyncDirection.Upload)
                {
                    syncResult = await UploadVideoAsync(video);
                }
                else
                {
                    syncResult = await DownloadVideoAsync(video);
                }

                if (syncResult.Success)
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.FailedCount++;
                    result.Errors.Add($"Video {video.Id}: {syncResult.ErrorMessage}");
                }
            }

            result.Success = result.FailedCount == 0;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }
}

public class SyncResult
{
    public int VideoId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LocalPath { get; set; }
    public string? RemotePath { get; set; }
}

public class BatchSyncResult
{
    public bool Success { get; set; }
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class RemoteFileInfo
{
    public string? Key { get; set; }
    public string? Name { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsFolder { get; set; }
}

public enum SyncDirection
{
    Upload,
    Download
}
