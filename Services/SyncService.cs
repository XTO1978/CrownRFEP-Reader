using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CrownRFEP_Reader.Models;

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

            // Obtener ruta local absoluta
            var localPath = _pathService.ToAbsoluteLocalPath(video.ClipPath ?? video.LocalClipPath ?? "");
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

            // Generar ruta remota
            var remotePath = video.ClipPath ?? _pathService.GetRemoteVideoPath(video.SessionId, video.Id);

            progress?.Report(0.1);

            // Obtener URL firmada para descargar
            var signResult = await _cloudService.GetDownloadUrlAsync(remotePath);

            if (!signResult.Success || string.IsNullOrEmpty(signResult.Url))
            {
                result.Success = false;
                result.ErrorMessage = signResult.ErrorMessage ?? "No se pudo obtener URL de descarga";
                return result;
            }

            progress?.Report(0.2);

            // Preparar ruta local
            _pathService.EnsureSessionDirectoryExists(video.SessionId);
            var localPath = _pathService.GetLocalVideoPath(video.SessionId, video.Id);

            // Descargar el archivo
            var response = await _httpClient.GetAsync(signResult.Url);
            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"Error al descargar: {response.StatusCode}";
                return result;
            }

            progress?.Report(0.5);

            var fileBytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(localPath, fileBytes);

            progress?.Report(0.9);

            // Actualizar rutas en DB
            video.LocalClipPath = localPath;
            video.ClipPath = _pathService.ToRelativePath(localPath);
            video.IsSynced = 1;
            video.LastSyncUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            video.Source = "both";
            video.ClipSize = fileBytes.Length;
            await _databaseService.UpdateVideoClipAsync(video);

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

            var localThumbPath = _pathService.ToAbsoluteLocalPath(video.ThumbnailPath ?? video.LocalThumbnailPath ?? "");
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
