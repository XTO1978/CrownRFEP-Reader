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
    private string _sessionName = string.Empty;
    private string _athleteName = string.Empty;
    private string _place = string.Empty;
    private int _sessionId;
    private int _videoId;
    private long _size;
    private DateTime _lastModified;
    private int _section;
    private List<Tag>? _tags;
    private List<Tag>? _eventTags;
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
    /// Nombre de la sesión mostrado en la UI
    /// </summary>
    public string SessionName
    {
        get => _sessionName;
        set => SetProperty(ref _sessionName, value);
    }

    /// <summary>
    /// Nombre del atleta asignado (ej: "GARCÍA Pablo")
    /// </summary>
    public string AthleteName
    {
        get => _athleteName;
        set
        {
            if (SetProperty(ref _athleteName, value))
            {
                OnPropertyChanged(nameof(HasAthleteName));
            }
        }
    }

    /// <summary>
    /// Indica si el video tiene un atleta asignado
    /// </summary>
    public bool HasAthleteName => !string.IsNullOrWhiteSpace(AthleteName);

    /// <summary>
    /// Lugar de la sesión
    /// </summary>
    public string Place
    {
        get => _place;
        set
        {
            if (SetProperty(ref _place, value))
            {
                OnPropertyChanged(nameof(HasPlace));
            }
        }
    }

    /// <summary>
    /// Indica si hay un lugar asignado a la sesión
    /// </summary>
    public bool HasPlace => !string.IsNullOrWhiteSpace(Place);

    /// <summary>
    /// Hora formateada (solo HH:mm) extraída de LastModified
    /// </summary>
    public string TimeFormatted => LastModified.ToString("HH:mm");

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
    /// Tramo/Sección del video
    /// </summary>
    public int Section
    {
        get => _section;
        set => SetProperty(ref _section, value);
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
        set
        {
            if (SetProperty(ref _lastModified, value))
            {
                OnPropertyChanged(nameof(TimeFormatted));
            }
        }
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
    /// Indica si el video está disponible localmente (tiene archivo descargado)
    /// </summary>
    public bool IsLocallyAvailable
    {
        get => _isLocallyAvailable;
        set
        {
            if (SetProperty(ref _isLocallyAvailable, value))
            {
                OnPropertyChanged(nameof(IsDownloadedLocally));
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
        set
        {
            if (SetProperty(ref _linkedLocalVideo, value))
            {
                OnPropertyChanged(nameof(IsAddedToLibrary));
                OnPropertyChanged(nameof(IsDownloadedLocally));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(EffectiveThumbnailSource));
            }
        }
    }

    /// <summary>
    /// El video está registrado en la biblioteca personal (referencia o descargado)
    /// </summary>
    public bool IsAddedToLibrary => LinkedLocalVideo != null;

    /// <summary>
    /// El video tiene una copia local descargada (Source == "both")
    /// </summary>
    public bool IsDownloadedLocally =>
        LinkedLocalVideo != null
        && string.Equals(LinkedLocalVideo.Source, "both", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tags asignadas al video (no eventos)
    /// </summary>
    public List<Tag>? Tags
    {
        get => _tags;
        set
        {
            if (ReferenceEquals(_tags, value)) return;
            _tags = value;
            OnPropertyChanged(nameof(Tags));
            OnPropertyChanged(nameof(HasTagsSummary));
            OnPropertyChanged(nameof(TagsSummary));
        }
    }

    /// <summary>
    /// Tags de eventos del video
    /// </summary>
    public List<Tag>? EventTags
    {
        get => _eventTags;
        set
        {
            if (ReferenceEquals(_eventTags, value)) return;
            _eventTags = value;
            OnPropertyChanged(nameof(EventTags));
            OnPropertyChanged(nameof(HasTagsSummary));
            OnPropertyChanged(nameof(TagsSummary));
        }
    }

    public string TagsSummary
    {
        get
        {
            var names = new List<string>();
            if (Tags != null)
            {
                names.AddRange(
                    Tags.Where(t => t != null)
                        .Select(t => t!.NombreTag)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!));
            }
            if (EventTags != null)
            {
                names.AddRange(
                    EventTags.Where(t => t != null)
                        .Select(t => t!.DisplayText)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!));
            }

            if (names.Count == 0)
                return string.Empty;

            var shown = names.Take(3).ToList();
            var remaining = names.Count - shown.Count;
            var summary = string.Join(", ", shown);
            return remaining > 0 ? $"{summary} +{remaining}" : summary;
        }
    }

    public bool HasTagsSummary => !string.IsNullOrWhiteSpace(TagsSummary);

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
    /// Nombre de sesión extraído del path (fallback)
    /// </summary>
    public string SessionNameFallback => $"Sesión {SessionId}";

    /// <summary>
    /// Color de estado: verde=descargado, naranja=solo referencia, azul=solo nube
    /// </summary>
    public string StatusColor
    {
        get
        {
            if (IsDownloadedLocally) return "#4CAF50";   // Verde: descargado
            if (IsAddedToLibrary) return "#FF9800";       // Naranja: referencia en personal
            return "#2196F3";                              // Azul: solo en nube de org
        }
    }

    /// <summary>
    /// SF Symbol de estado:
    /// - checkmark.icloud.fill  → descargado localmente
    /// - person.crop.circle.badge.plus → añadido a biblioteca personal (referencia)
    /// - icloud → solo en la nube de la organización
    /// </summary>
    public string StatusIcon
    {
        get
        {
            if (IsDownloadedLocally) return "checkmark.icloud.fill";
            if (IsAddedToLibrary) return "person.crop.circle.badge.plus";
            return "icloud";
        }
    }

    /// <summary>
    /// Texto de estado para tooltip / accesibilidad
    /// </summary>
    public string StatusText
    {
        get
        {
            if (IsDownloadedLocally) return "Descargado";
            if (IsAddedToLibrary) return "En tu biblioteca";
            return "Solo en organización";
        }
    }

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
            LocalPath = linkedLocal?.LocalClipPath,
            SessionName = $"Sesión {sessionId}"
        };
    }
}
