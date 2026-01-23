using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Interfaz para el servicio de backend en la nube.
/// El backend maneja autenticación y firma de URLs - las credenciales de Wasabi
/// nunca se exponen en el cliente.
/// </summary>
public interface ICloudBackendService
{
    /// <summary>
    /// Indica si el usuario está autenticado.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Nombre del usuario autenticado.
    /// </summary>
    string? CurrentUserName { get; }

    /// <summary>
    /// Nombre del equipo/organización.
    /// </summary>
    string? TeamName { get; }

    /// <summary>
    /// Rol del usuario autenticado.
    /// </summary>
    string? CurrentUserRole { get; }

    /// <summary>
    /// URL base actual del backend.
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    /// Actualiza la URL base del backend (se persiste en preferencias).
    /// </summary>
    void UpdateBaseUrl(string baseUrl);

    /// <summary>
    /// Autentica al usuario con email y contraseña.
    /// </summary>
    Task<AuthResult> LoginAsync(string email, string password);

    /// <summary>
    /// Cierra la sesión del usuario.
    /// </summary>
    Task LogoutAsync();

    /// <summary>
    /// Refresca el token de autenticación si es necesario.
    /// </summary>
    Task<bool> RefreshTokenIfNeededAsync();

    /// <summary>
    /// Obtiene la lista de archivos en una carpeta del equipo.
    /// </summary>
    Task<CloudFileListResult> ListFilesAsync(string folderPath = "", int maxItems = 100, string? continuationToken = null);

    /// <summary>
    /// Obtiene una URL prefirmada para descargar un archivo.
    /// </summary>
    Task<PresignedUrlResult> GetDownloadUrlAsync(string filePath, int expirationMinutes = 60);

    /// <summary>
    /// Obtiene una URL prefirmada para subir un archivo.
    /// </summary>
    Task<PresignedUrlResult> GetUploadUrlAsync(string filePath, string contentType, int expirationMinutes = 60);

    /// <summary>
    /// Notifica al backend que una subida se ha completado.
    /// </summary>
    Task<bool> ConfirmUploadAsync(string filePath, long fileSize);

    /// <summary>
    /// Elimina un archivo (lo mueve a papelera).
    /// </summary>
    Task<bool> DeleteFileAsync(string filePath);

    /// <summary>
    /// Obtiene información del equipo y cuota de almacenamiento.
    /// </summary>
    Task<TeamInfoResult> GetTeamInfoAsync();

/// <summary>
/// Verifica si el backend está disponible y funcionando.
/// </summary>
Task<BackendHealthResult> CheckHealthAsync();

/// <summary>
/// Obtiene la lista de videos/archivos nuevos o actualizados desde la última sincronización.
/// Útil para mantener la galería actualizada para atletas y entrenadores.
/// </summary>
Task<GallerySyncResult> CheckForGalleryUpdatesAsync(DateTime? lastSyncTime = null);
}

/// <summary>
/// Resultado del health check del backend.
/// </summary>
public record BackendHealthResult(
    bool IsHealthy,
    string? ErrorMessage = null,
    string? Version = null,
    DateTime? ServerTime = null
);

/// <summary>
/// Resultado de la verificación de actualizaciones de galería.
/// </summary>
public record GallerySyncResult(
    bool Success,
    string? ErrorMessage = null,
    int NewFilesCount = 0,
    int UpdatedFilesCount = 0,
    List<CloudFileInfo>? NewFiles = null,
    List<CloudFileInfo>? UpdatedFiles = null,
    DateTime? LastCheckedUtc = null
);
public record AuthResult(
    bool Success,
    string? ErrorMessage = null,
    string? UserName = null,
    string? TeamName = null,
    string? AccessToken = null,
    DateTime? ExpiresAt = null,
    string? Role = null
);

/// <summary>
/// Resultado de listado de archivos.
/// </summary>
public record CloudFileListResult(
    bool Success,
    string? ErrorMessage = null,
    List<CloudFileInfo>? Files = null,
    string? ContinuationToken = null,
    bool HasMore = false
);

/// <summary>
/// Información de un archivo en la nube.
/// </summary>
public record CloudFileInfo(
    string Key,
    string Name,
    long Size,
    DateTime LastModified,
    bool IsFolder,
    string? ContentType = null,
    string? ThumbnailUrl = null
);

/// <summary>
/// Resultado de URL prefirmada.
/// </summary>
public record PresignedUrlResult(
    bool Success,
    string? ErrorMessage = null,
    string? Url = null,
    DateTime? ExpiresAt = null,
    Dictionary<string, string>? Headers = null
);

/// <summary>
/// Información del equipo.
/// </summary>
public record TeamInfoResult(
    bool Success,
    string? ErrorMessage = null,
    string? TeamName = null,
    long StorageUsedBytes = 0,
    long StorageLimitBytes = 0,
    int TotalFiles = 0,
    List<TeamMemberInfo>? Members = null
);

/// <summary>
/// Información de un miembro del equipo.
/// </summary>
public record TeamMemberInfo(
    string Id,
    string Name,
    string Email,
    string Role
);
