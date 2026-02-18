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
    /// Recupera el perfil de usuario desde el servidor y actualiza CurrentUserRole.
    /// </summary>
    Task RefreshUserProfileAsync();

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
/// Obtiene la configuración compartida de la organización (sidebar, smart folders, etc.).
/// </summary>
Task<OrgConfigResult> GetOrgConfigAsync();

/// <summary>
/// Guarda la configuración compartida de la organización.
/// Solo roles con permisos de escritura (admin, org_admin, coach).
/// </summary>
Task<OrgConfigSaveResult> SaveOrgConfigAsync(OrgConfig config);

/// <summary>
/// Obtiene la lista de videos/archivos nuevos o actualizados desde la última sincronización.
/// Útil para mantener la galería actualizada para atletas y entrenadores.
/// </summary>
Task<GallerySyncResult> CheckForGalleryUpdatesAsync(DateTime? lastSyncTime = null);

/// <summary>
/// Obtiene todas las sesiones del equipo desde el backend.
/// </summary>
Task<RemoteSessionListResult> GetRemoteSessionsAsync(DateTime? since = null);

/// <summary>
/// Sube/actualiza una sesión al backend (upsert por localSessionId + deviceId).
/// </summary>
Task<RemoteSessionSyncResult> SyncSessionToRemoteAsync(RemoteSessionPayload session);

/// <summary>
/// Sube múltiples sesiones al backend en una sola petición.
/// </summary>
Task<RemoteSessionBatchResult> SyncSessionsBatchAsync(List<RemoteSessionPayload> sessions);

/// <summary>
/// Elimina (soft-delete) una sesión en el backend.
/// </summary>
Task<bool> DeleteRemoteSessionAsync(int remoteSessionId);

/// <summary>
/// Eliminación en cascada: borra archivos S3 de la sesión + hard-delete en la BD del backend.
/// </summary>
Task<CascadeDeleteResult> DeleteRemoteSessionCascadeAsync(int remoteSessionId);
}

/// <summary>
/// Resultado de la eliminación en cascada de una sesión remota.
/// </summary>
public record CascadeDeleteResult(
    bool Success,
    string? ErrorMessage = null,
    int DeletedFiles = 0
);

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

/// <summary>
/// Configuración compartida de la organización.
/// Se almacena como org-config.json en Wasabi.
/// </summary>
public class OrgConfig
{
    public int Version { get; set; }
    public string? UpdatedAt { get; set; }
    public OrgConfigAuthor? UpdatedBy { get; set; }
    public List<OrgSmartFolder> SmartFolders { get; set; } = new();
    public OrgSidebarSections SidebarSections { get; set; } = new();
}

public class OrgConfigAuthor
{
    public int UserId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
}

public class OrgSmartFolder
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string MatchMode { get; set; } = "All";
    public string Icon { get; set; } = "folder";
    public string IconColor { get; set; } = "#FF9800";
    public List<OrgSmartFolderCriterion> Criteria { get; set; } = new();
}

public class OrgSmartFolderCriterion
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Value2 { get; set; }
}

public class OrgSidebarSections
{
    public bool GalleryVisible { get; set; } = true;
    public bool VideoLessonsVisible { get; set; } = true;
    public bool TrashVisible { get; set; } = true;
    public bool SessionsVisible { get; set; } = true;
    public bool SmartFoldersVisible { get; set; } = true;
}

public record OrgConfigResult(
    bool Success,
    string? ErrorMessage = null,
    OrgConfig? Config = null
);

public record OrgConfigSaveResult(
    bool Success,
    string? ErrorMessage = null,
    int Version = 0,
    string? UpdatedAt = null
);

/// <summary>
/// Payload para enviar datos de sesión al backend.
/// </summary>
public class RemoteSessionPayload
{
    public int LocalSessionId { get; set; }
    public string? DeviceId { get; set; }
    public string? SessionName { get; set; }
    public string? Place { get; set; }
    public string? Coach { get; set; }
    public string? SessionType { get; set; }
    public long SessionDateUtc { get; set; }
    public string? Participants { get; set; }
    public int IsMerged { get; set; }
    public string? Icon { get; set; }
    public string? IconColor { get; set; }
    public int VideoCount { get; set; }
}

/// <summary>
/// Sesión tal como viene del backend.
/// </summary>
public class RemoteSessionDto
{
    public int Id { get; set; }
    public string? TeamId { get; set; }
    public int LocalSessionId { get; set; }
    public string? DeviceId { get; set; }
    public string? SessionName { get; set; }
    public string? Place { get; set; }
    public string? Coach { get; set; }
    public string? SessionType { get; set; }
    public long SessionDateUtc { get; set; }
    public string? Participants { get; set; }
    public int IsMerged { get; set; }
    public string? Icon { get; set; }
    public string? IconColor { get; set; }
    public int VideoCount { get; set; }
    public int? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public int IsDeleted { get; set; }
    public string? DeletedAt { get; set; }
}

/// <summary>
/// Resultado de consulta de sesiones remotas.
/// </summary>
public record RemoteSessionListResult(
    bool Success,
    string? ErrorMessage = null,
    List<RemoteSessionDto>? Sessions = null,
    int Count = 0
);

/// <summary>
/// Resultado de sincronizar una sesión al backend.
/// </summary>
public record RemoteSessionSyncResult(
    bool Success,
    string? ErrorMessage = null,
    RemoteSessionDto? Session = null,
    bool IsNew = false
);

/// <summary>
/// Resultado de sincronización batch de sesiones.
/// </summary>
public record RemoteSessionBatchResult(
    bool Success,
    string? ErrorMessage = null,
    int Created = 0,
    int Updated = 0,
    int Total = 0,
    List<RemoteSessionBatchItem>? Results = null
);

public record RemoteSessionBatchItem(
    int LocalSessionId,
    int RemoteId,
    bool IsNew
);
