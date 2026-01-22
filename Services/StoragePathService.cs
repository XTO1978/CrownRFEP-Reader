using System;
using System.IO;
using System.Threading.Tasks;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio que unifica la estructura de almacenamiento local y remoto.
/// Define rutas consistentes para videos, thumbnails, sesiones, etc.
/// </summary>
public class StoragePathService
{
    private readonly string _localRootPath;
    private const string RemoteRoot = "CrownRFEP"; // Carpeta raíz en Wasabi
    
    // Subcarpetas estándar
    private const string SessionsFolder = "sessions";
    private const string VideosFolder = "videos";
    private const string ThumbnailsFolder = "thumbnails";
    private const string AthletesFolder = "athletes";
    private const string LessonsFolder = "lessons";
    private const string TrashFolder = "trash";
    private const string ExportsFolder = "exports";

    public StoragePathService()
    {
        _localRootPath = Path.Combine(FileSystem.AppDataDirectory, "CrownData");
        EnsureLocalDirectoriesExist();
    }

    /// <summary>
    /// Ruta raíz local
    /// </summary>
    public string LocalRootPath => _localRootPath;

    /// <summary>
    /// Ruta raíz remota (Wasabi)
    /// </summary>
    public string RemoteRootPath => RemoteRoot;

    #region Rutas de Sesiones

    /// <summary>
    /// Obtiene la ruta local de una sesión
    /// </summary>
    public string GetLocalSessionPath(int sessionId)
    {
        return Path.Combine(_localRootPath, SessionsFolder, sessionId.ToString());
    }

    /// <summary>
    /// Obtiene la ruta remota de una sesión
    /// </summary>
    public string GetRemoteSessionPath(int sessionId)
    {
        return $"{SessionsFolder}/{sessionId}";
    }

    /// <summary>
    /// Obtiene la ruta local del archivo JSON de metadatos de sesión
    /// </summary>
    public string GetLocalSessionMetadataPath(int sessionId)
    {
        return Path.Combine(GetLocalSessionPath(sessionId), "session.json");
    }

    /// <summary>
    /// Obtiene la ruta remota del archivo JSON de metadatos de sesión
    /// </summary>
    public string GetRemoteSessionMetadataPath(int sessionId)
    {
        return $"{GetRemoteSessionPath(sessionId)}/session.json";
    }

    #endregion

    #region Rutas de Videos

    /// <summary>
    /// Obtiene la ruta local de un video
    /// </summary>
    public string GetLocalVideoPath(int sessionId, int videoId, string extension = ".mp4")
    {
        var sessionPath = GetLocalSessionPath(sessionId);
        return Path.Combine(sessionPath, VideosFolder, $"{videoId}{extension}");
    }

    /// <summary>
    /// Obtiene la ruta remota de un video
    /// </summary>
    public string GetRemoteVideoPath(int sessionId, int videoId, string extension = ".mp4")
    {
        return $"{GetRemoteSessionPath(sessionId)}/{VideosFolder}/{videoId}{extension}";
    }

    /// <summary>
    /// Obtiene la ruta local de un thumbnail
    /// </summary>
    public string GetLocalThumbnailPath(int sessionId, int videoId)
    {
        var sessionPath = GetLocalSessionPath(sessionId);
        return Path.Combine(sessionPath, ThumbnailsFolder, $"{videoId}.jpg");
    }

    /// <summary>
    /// Obtiene la ruta remota de un thumbnail
    /// </summary>
    public string GetRemoteThumbnailPath(int sessionId, int videoId)
    {
        return $"{GetRemoteSessionPath(sessionId)}/{ThumbnailsFolder}/{videoId}.jpg";
    }

    /// <summary>
    /// Obtiene la ruta remota del archivo de metadatos de un video
    /// </summary>
    public string GetRemoteMetadataPath(int sessionId, int videoId)
    {
        return $"{GetRemoteSessionPath(sessionId)}/metadata/{videoId}.json";
    }

    /// <summary>
    /// Genera un nombre de archivo único para un nuevo video
    /// </summary>
    public string GenerateVideoFileName(int sessionId, int athleteId, DateTime creationDate)
    {
        var timestamp = creationDate.ToString("yyyyMMdd_HHmmss");
        return $"video_{sessionId}_{athleteId}_{timestamp}.mp4";
    }

    #endregion

    #region Rutas de Atletas

    /// <summary>
    /// Obtiene la ruta local de la foto de un atleta
    /// </summary>
    public string GetLocalAthletePhotoPath(int athleteId)
    {
        return Path.Combine(_localRootPath, AthletesFolder, $"{athleteId}.jpg");
    }

    /// <summary>
    /// Obtiene la ruta remota de la foto de un atleta
    /// </summary>
    public string GetRemoteAthletePhotoPath(int athleteId)
    {
        return $"{AthletesFolder}/{athleteId}.jpg";
    }

    #endregion

    #region Rutas de Videolecciones

    /// <summary>
    /// Obtiene la ruta local de una videolección
    /// </summary>
    public string GetLocalLessonPath(int lessonId)
    {
        return Path.Combine(_localRootPath, LessonsFolder, $"{lessonId}.mp4");
    }

    /// <summary>
    /// Obtiene la ruta remota de una videolección
    /// </summary>
    public string GetRemoteLessonPath(int lessonId)
    {
        return $"{LessonsFolder}/{lessonId}.mp4";
    }

    /// <summary>
    /// Obtiene la ruta local del thumbnail de una videolección
    /// </summary>
    public string GetLocalLessonThumbnailPath(int lessonId)
    {
        return Path.Combine(_localRootPath, LessonsFolder, "thumbnails", $"{lessonId}.jpg");
    }

    /// <summary>
    /// Obtiene la ruta remota del thumbnail de una videolección
    /// </summary>
    public string GetRemoteLessonThumbnailPath(int lessonId)
    {
        return $"{LessonsFolder}/thumbnails/{lessonId}.jpg";
    }

    #endregion

    #region Papelera

    /// <summary>
    /// Obtiene la ruta local de la papelera
    /// </summary>
    public string GetLocalTrashPath()
    {
        return Path.Combine(_localRootPath, TrashFolder);
    }

    /// <summary>
    /// Obtiene la ruta local de un video en la papelera
    /// </summary>
    public string GetLocalTrashVideoPath(int videoId, string extension = ".mp4")
    {
        return Path.Combine(GetLocalTrashPath(), $"{videoId}{extension}");
    }

    /// <summary>
    /// Obtiene la ruta remota de un video en la papelera
    /// </summary>
    public string GetRemoteTrashVideoPath(int videoId, string extension = ".mp4")
    {
        return $"{TrashFolder}/{videoId}{extension}";
    }

    #endregion

    #region Exportaciones

    /// <summary>
    /// Obtiene la ruta local para exportaciones
    /// </summary>
    public string GetLocalExportsPath()
    {
        return Path.Combine(_localRootPath, ExportsFolder);
    }

    /// <summary>
    /// Obtiene la ruta remota para exportaciones
    /// </summary>
    public string GetRemoteExportsPath()
    {
        return ExportsFolder;
    }

    #endregion

    #region Utilidades

    /// <summary>
    /// Asegura que los directorios locales existen
    /// </summary>
    public void EnsureLocalDirectoriesExist()
    {
        var directories = new[]
        {
            _localRootPath,
            Path.Combine(_localRootPath, SessionsFolder),
            Path.Combine(_localRootPath, AthletesFolder),
            Path.Combine(_localRootPath, LessonsFolder),
            Path.Combine(_localRootPath, LessonsFolder, "thumbnails"),
            Path.Combine(_localRootPath, TrashFolder),
            Path.Combine(_localRootPath, ExportsFolder)
        };

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }

    /// <summary>
    /// Asegura que la carpeta de una sesión existe
    /// </summary>
    public void EnsureSessionDirectoryExists(int sessionId)
    {
        var sessionPath = GetLocalSessionPath(sessionId);
        var videosPath = Path.Combine(sessionPath, VideosFolder);
        var thumbsPath = Path.Combine(sessionPath, ThumbnailsFolder);

        if (!Directory.Exists(sessionPath))
            Directory.CreateDirectory(sessionPath);
        if (!Directory.Exists(videosPath))
            Directory.CreateDirectory(videosPath);
        if (!Directory.Exists(thumbsPath))
            Directory.CreateDirectory(thumbsPath);
    }

    /// <summary>
    /// Convierte una ruta local a relativa (para almacenar en DB)
    /// </summary>
    public string ToRelativePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return string.Empty;

        if (absolutePath.StartsWith(_localRootPath))
        {
            return absolutePath.Substring(_localRootPath.Length).TrimStart(Path.DirectorySeparatorChar);
        }

        return absolutePath;
    }

    /// <summary>
    /// Convierte una ruta relativa a absoluta local
    /// </summary>
    public string ToAbsoluteLocalPath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return string.Empty;

        // Si ya es absoluta, devolverla
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        return Path.Combine(_localRootPath, relativePath);
    }

    /// <summary>
    /// Obtiene la extensión de un archivo de video
    /// </summary>
    public static string GetVideoExtension(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return ".mp4";

        var ext = Path.GetExtension(filePath);
        return string.IsNullOrEmpty(ext) ? ".mp4" : ext.ToLowerInvariant();
    }

    /// <summary>
    /// Verifica si un archivo existe localmente
    /// </summary>
    public bool LocalFileExists(string relativePath)
    {
        var absolutePath = ToAbsoluteLocalPath(relativePath);
        return File.Exists(absolutePath);
    }

    /// <summary>
    /// Obtiene el tamaño de un archivo local
    /// </summary>
    public long GetLocalFileSize(string relativePath)
    {
        var absolutePath = ToAbsoluteLocalPath(relativePath);
        if (File.Exists(absolutePath))
        {
            return new FileInfo(absolutePath).Length;
        }
        return 0;
    }

    #endregion
}
