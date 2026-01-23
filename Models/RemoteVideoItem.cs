using System.ComponentModel;
using System.Runtime.CompilerServices;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un video almacenado remotamente en la nube (Wasabi).
/// Se usa para mostrar en la Galería General remota.
/// </summary>
public class RemoteVideoItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private string _key = string.Empty;
    private string _fileName = string.Empty;
    private int _sessionId;
    private int _videoId;
    private long _size;
    private DateTime _lastModified;
    private string? _downloadUrl;
    private string? _thumbnailUrl;
    private bool _isLocallyAvailable;
    private string? _localPath;
    private bool _isDownloading;
    private double _downloadProgress;
    private VideoClip? _linkedLocalVideo;
    private bool _isSelected;

    /// <summary>
    /// Indica si el video está seleccionado (para acciones en lote)
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Clave completa del archivo en S3 (ej: "CrownRFEP/sessions/11/videos/67.mp4")
    /// </summary>
    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    /// <summary>
    /// Nombre del archivo (ej: "67.mp4")
    /// </summary>
    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    /// <summary>
    /// ID de la sesión extraído de la ruta
    /// </summary>
    public int SessionId
    {
        get => _sessionId;
        set => SetProperty(ref _sessionId, value);
    }

    /// <summary>
    /// ID del video extraído de la ruta
    /// </summary>
    public int VideoId
    {
        get => _videoId;
        set => SetProperty(ref _videoId, value);
    }

    /// <summary>
    /// Tamaño del archivo en bytes
    /// </summary>
    public long Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    /// <summary>
    /// Fecha de última modificación
    /// </summary>
    public DateTime LastModified
    {
        get => _lastModified;
        set => SetProperty(ref _lastModified, value);
    }

    /// <summary>
    /// URL de descarga temporal (firmada)
    /// </summary>
    public string? DownloadUrl
    {
        get => _downloadUrl;
        set => SetProperty(ref _downloadUrl, value);
    }

    /// <summary>
    /// URL de thumbnail (si existe)
    /// </summary>
    public string? ThumbnailUrl
    {
        get => _thumbnailUrl;
        set
        {
            if (SetProperty(ref _thumbnailUrl, value))
            {
                OnPropertyChanged(nameof(EffectiveThumbnailSource));
            }
        }
    }

    /// <summary>
    /// Fuente efectiva de miniatura (local si existe, si no URL remota)
    /// </summary>
    public string? EffectiveThumbnailSource
        => LinkedLocalVideo?.EffectiveThumbnailPath ?? ThumbnailUrl;

    /// <summary>
    /// Indica si el video está disponible localmente
    /// </summary>
    public bool IsLocallyAvailable
    {
        get => _isLocallyAvailable;
        set
        {
            if (SetProperty(ref _isLocallyAvailable, value))
            {
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>
    /// Ruta local si está descargado
    /// </summary>
    public string? LocalPath
    {
        get => _localPath;
        set => SetProperty(ref _localPath, value);
    }

    /// <summary>
    /// Indica si está descargándose
    /// </summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    /// <summary>
    /// Progreso de descarga (0-1)
    /// </summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    /// <summary>
    /// Información del VideoClip local si existe
    /// </summary>
    public VideoClip? LinkedLocalVideo
    {
        get => _linkedLocalVideo;
        set => SetProperty(ref _linkedLocalVideo, value);
    }

    /// <summary>
    /// Tamaño formateado para mostrar (ej: "12.5 MB")
    /// </summary>
    public string SizeFormatted
    {
        get
        {
            if (Size < 1024)
                return $"{Size} B";
            if (Size < 1024 * 1024)
                return $"{Size / 1024.0:F1} KB";
            if (Size < 1024 * 1024 * 1024)
                return $"{Size / (1024.0 * 1024.0):F1} MB";
            return $"{Size / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    /// <summary>
    /// Nombre de sesión extraído del path
    /// </summary>
    public string SessionName => $"Sesión {SessionId}";

    /// <summary>
    /// Color de estado: verde si local, azul si solo remoto
    /// </summary>
    public string StatusColor => IsLocallyAvailable ? "#4CAF50" : "#2196F3";

    /// <summary>
    /// Icono de estado
    /// </summary>
    public string StatusIcon => IsLocallyAvailable ? "checkmark.circle.fill" : "icloud";

    /// <summary>
    /// Texto de estado
    /// </summary>
    public string StatusText => IsLocallyAvailable ? "Disponible en personal" : "Solo en organización";

    /// <summary>
    /// Crea un RemoteVideoItem desde un CloudFileInfo
    /// </summary>
    public static RemoteVideoItem FromCloudFile(CloudFileInfo file, VideoClip? linkedLocal = null)
    {
        // Extraer IDs del path: sessions/{sessionId}/videos/{videoId}.mp4
        var parts = file.Key.Split('/');
        int sessionId = 0;
        int videoId = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "sessions" && i + 1 < parts.Length)
            {
                int.TryParse(parts[i + 1], out sessionId);
            }
            if (parts[i] == "videos" && i + 1 < parts.Length)
            {
                var fileName = parts[i + 1];
                var videoIdStr = Path.GetFileNameWithoutExtension(fileName);
                int.TryParse(videoIdStr, out videoId);
            }
        }

        return new RemoteVideoItem
        {
            Key = file.Key,
            FileName = file.Name,
            SessionId = sessionId,
            VideoId = videoId,
            Size = file.Size,
            LastModified = file.LastModified,
            ThumbnailUrl = file.ThumbnailUrl,
            LinkedLocalVideo = linkedLocal,
            IsLocallyAvailable = linkedLocal != null,
            LocalPath = linkedLocal?.LocalClipPath
        };
    }
}
