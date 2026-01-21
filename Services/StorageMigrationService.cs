using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio para migrar archivos locales existentes a la nueva estructura unificada.
/// </summary>
public class StorageMigrationService
{
    private readonly StoragePathService _pathService;
    private readonly DatabaseService _databaseService;
    private readonly StatusBarService? _statusBarService;

    public StorageMigrationService(
        StoragePathService pathService,
        DatabaseService databaseService,
        StatusBarService? statusBarService = null)
    {
        _pathService = pathService;
        _databaseService = databaseService;
        _statusBarService = statusBarService;
    }

    /// <summary>
    /// Ejecuta la migración completa de archivos existentes a la nueva estructura.
    /// </summary>
    public async Task<MigrationResult> MigrateAllAsync(IProgress<(int current, int total, string message)>? progress = null)
    {
        var result = new MigrationResult();

        try
        {
            _statusBarService?.LogDatabaseInfo("Iniciando migración de almacenamiento...");

            // 1. Migrar videos de sesiones
            var videos = await _databaseService.GetAllVideoClipsAsync();
            result.TotalVideos = videos.Count;

            for (int i = 0; i < videos.Count; i++)
            {
                var video = videos[i];
                progress?.Report((i + 1, videos.Count, $"Migrando video {i + 1} de {videos.Count}..."));

                try
                {
                    var migrated = await MigrateVideoClipAsync(video);
                    if (migrated)
                    {
                        result.MigratedVideos++;
                    }
                    else
                    {
                        result.SkippedVideos++;
                    }
                }
                catch (Exception ex)
                {
                    result.FailedVideos++;
                    result.Errors.Add($"Video {video.Id}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[Migration] Error migrando video {video.Id}: {ex.Message}");
                }
            }

            // 2. Migrar thumbnails
            await MigrateThumbnailsAsync(videos, result);

            result.Success = result.FailedVideos == 0;
            _statusBarService?.LogDatabaseSuccess($"Migración completada: {result.MigratedVideos} videos migrados");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Error general: {ex.Message}");
            _statusBarService?.LogDatabaseError($"Error en migración: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Migra un VideoClip individual a la nueva estructura.
    /// </summary>
    private async Task<bool> MigrateVideoClipAsync(VideoClip video)
    {
        // Determinar la ruta actual del archivo
        var currentPath = video.LocalClipPath;
        if (string.IsNullOrEmpty(currentPath))
        {
            currentPath = _pathService.ToAbsoluteLocalPath(video.ClipPath ?? "");
        }

        // Si no existe el archivo, no hay nada que migrar
        if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
        {
            System.Diagnostics.Debug.WriteLine($"[Migration] Video {video.Id}: archivo no encontrado en {currentPath}");
            return false;
        }

        // Calcular la nueva ruta
        var newPath = _pathService.GetLocalVideoPath(video.SessionId, video.Id);

        // Si ya está en la ubicación correcta, no hacer nada
        if (NormalizePath(currentPath) == NormalizePath(newPath))
        {
            System.Diagnostics.Debug.WriteLine($"[Migration] Video {video.Id}: ya en ubicación correcta");
            return false;
        }

        // Asegurar que existe el directorio destino
        _pathService.EnsureSessionDirectoryExists(video.SessionId);

        // Mover el archivo
        System.Diagnostics.Debug.WriteLine($"[Migration] Video {video.Id}: {currentPath} -> {newPath}");

        // Si el destino ya existe, verificar si es el mismo archivo
        if (File.Exists(newPath))
        {
            var sourceInfo = new FileInfo(currentPath);
            var destInfo = new FileInfo(newPath);

            if (sourceInfo.Length == destInfo.Length)
            {
                // Probablemente es el mismo archivo, eliminar el origen
                File.Delete(currentPath);
            }
            else
            {
                // Archivos diferentes, renombrar el destino
                var backupPath = newPath + ".bak";
                File.Move(newPath, backupPath);
                File.Move(currentPath, newPath);
            }
        }
        else
        {
            File.Move(currentPath, newPath);
        }

        // Actualizar la base de datos
        video.LocalClipPath = newPath;
        video.ClipPath = _pathService.ToRelativePath(newPath);
        video.Source = "local";
        video.IsSynced = 0;
        await _databaseService.UpdateVideoClipAsync(video);

        return true;
    }

    /// <summary>
    /// Migra los thumbnails a la nueva estructura.
    /// </summary>
    private async Task MigrateThumbnailsAsync(List<VideoClip> videos, MigrationResult result)
    {
        foreach (var video in videos)
        {
            try
            {
                var currentThumbPath = video.LocalThumbnailPath;
                if (string.IsNullOrEmpty(currentThumbPath))
                {
                    currentThumbPath = _pathService.ToAbsoluteLocalPath(video.ThumbnailPath ?? "");
                }

                if (string.IsNullOrEmpty(currentThumbPath) || !File.Exists(currentThumbPath))
                {
                    continue;
                }

                var newThumbPath = _pathService.GetLocalThumbnailPath(video.SessionId, video.Id);

                if (NormalizePath(currentThumbPath) == NormalizePath(newThumbPath))
                {
                    continue;
                }

                // Asegurar directorio
                var thumbDir = Path.GetDirectoryName(newThumbPath);
                if (!string.IsNullOrEmpty(thumbDir) && !Directory.Exists(thumbDir))
                {
                    Directory.CreateDirectory(thumbDir);
                }

                // Mover thumbnail
                if (!File.Exists(newThumbPath))
                {
                    File.Move(currentThumbPath, newThumbPath);
                }
                else
                {
                    File.Delete(currentThumbPath);
                }

                // Actualizar DB
                video.LocalThumbnailPath = newThumbPath;
                video.ThumbnailPath = _pathService.ToRelativePath(newThumbPath);
                await _databaseService.UpdateVideoClipAsync(video);

                result.MigratedThumbnails++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Thumbnail {video.Id}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Verifica si hay archivos pendientes de migración.
    /// </summary>
    public async Task<int> GetPendingMigrationCountAsync()
    {
        var count = 0;
        var videos = await _databaseService.GetAllVideoClipsAsync();

        foreach (var video in videos)
        {
            var currentPath = video.LocalClipPath;
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = _pathService.ToAbsoluteLocalPath(video.ClipPath ?? "");
            }

            if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
            {
                continue;
            }

            var expectedPath = _pathService.GetLocalVideoPath(video.SessionId, video.Id);
            if (NormalizePath(currentPath) != NormalizePath(expectedPath))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Limpia carpetas vacías después de la migración.
    /// </summary>
    public void CleanupEmptyDirectories(string rootPath)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(rootPath))
            {
                CleanupEmptyDirectories(dir);

                if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                {
                    Directory.Delete(dir);
                    System.Diagnostics.Debug.WriteLine($"[Migration] Eliminada carpeta vacía: {dir}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Migration] Error limpiando directorios: {ex.Message}");
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
    }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public int TotalVideos { get; set; }
    public int MigratedVideos { get; set; }
    public int SkippedVideos { get; set; }
    public int FailedVideos { get; set; }
    public int MigratedThumbnails { get; set; }
    public List<string> Errors { get; set; } = new();

    public override string ToString()
    {
        return $"Migración: {MigratedVideos}/{TotalVideos} videos, {MigratedThumbnails} thumbnails, {FailedVideos} errores";
    }
}
