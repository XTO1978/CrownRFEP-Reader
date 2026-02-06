using SQLite;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un clip de video de entrenamiento
/// </summary>
[Table("videoClip")]
public class VideoClip : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _hasTiming;
    private bool _isCurrentlyPlaying;
    private List<Tag>? _tags;
    private List<Tag>? _eventTags;
    private Athlete? _atleta;
    private Session? _session;

    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement]
    [Column("ID")]
    public int Id { get; set; }

    [Column("SessionID")]
    public int SessionId { get; set; }

    [Column("AtletaID")]
    public int AtletaId { get; set; }

    [Column("Section")]
    public int Section { get; set; }

    [Column("CreationDate")]
    public long CreationDate { get; set; }

    /// <summary>
    /// Ruta relativa del video (formato: sessions/{sessionId}/videos/{videoId}.mp4)
    /// Usada tanto para local como para remoto
    /// </summary>
    [Column("clipPath")]
    public string? ClipPath { get; set; }

    /// <summary>
    /// Ruta relativa del thumbnail (formato: sessions/{sessionId}/thumbnails/{videoId}.jpg)
    /// </summary>
    [Column("thumbnailPath")]
    public string? ThumbnailPath { get; set; }

    [Column("comparisonName")]
    public string? ComparisonName { get; set; }

    [Column("clipDuration")]
    public double ClipDuration { get; set; }

    [Column("clipSize")]
    public long ClipSize { get; set; }

    // Papelera (soft-delete)
    [Column("is_deleted")]
    public int IsDeleted { get; set; }

    [Column("deleted_at_utc")]
    public long DeletedAtUtc { get; set; }

    [Column("is_favorite")]
    public int IsFavorite { get; set; }

    // Campos de sincronización remota
    /// <summary>
    /// Indica si el video está sincronizado con el servidor remoto
    /// </summary>
    [Column("is_synced")]
    public int IsSynced { get; set; }

    /// <summary>
    /// Fecha UTC de la última sincronización
    /// </summary>
    [Column("last_sync_utc")]
    public long LastSyncUtc { get; set; }

    /// <summary>
    /// Hash del archivo para verificar integridad
    /// </summary>
    [Column("file_hash")]
    public string? FileHash { get; set; }

    /// <summary>
    /// Origen del video: "local", "remote", "both"
    /// </summary>
    [Column("source")]
    public string? Source { get; set; }

    // Propiedades adicionales del JSON de exportación
    /// <summary>
    /// Indica si es un video de comparación (calculado a partir de ComparisonName)
    /// </summary>
    [Ignore]
    public bool IsComparisonVideo => !string.IsNullOrEmpty(ComparisonName);

    [Ignore]
    public string? BadgeText { get; set; }

    [Ignore]
    public string? BadgeBackgroundColor { get; set; }

    // ===== PROPIEDADES DE SINCRONIZACIÓN PARA UI =====

    /// <summary>
    /// Indica si el video existe localmente
    /// </summary>
    [Ignore]
    public bool IsLocalAvailable => Source == "local" || Source == "both" || !string.IsNullOrEmpty(LocalClipPath);

    /// <summary>
    /// Indica si el video existe en el servidor remoto
    /// </summary>
    [Ignore]
    public bool IsRemoteAvailable => Source == "remote" || Source == "both" || IsSynced == 1;

    [Ignore]
    public bool IsFavoriteFlag
    {
        get => IsFavorite == 1;
        set
        {
            var newValue = value ? 1 : 0;
            if (IsFavorite == newValue) return;
            IsFavorite = newValue;
            OnPropertyChanged(nameof(IsFavoriteFlag));
        }
    }

    /// <summary>
    /// Indica si el video necesita subirse al servidor
    /// </summary>
    [Ignore]
    public bool NeedsUpload => IsLocalAvailable && !IsRemoteAvailable;

    /// <summary>
    /// Indica si el video necesita descargarse del servidor
    /// </summary>
    [Ignore]
    public bool NeedsDownload => IsRemoteAvailable && !IsLocalAvailable;

    /// <summary>
    /// Icono de estado de sincronización para mostrar en UI (SF Symbols)
    /// - Personal (propio del usuario): person.fill
    /// - De organización, descargado (offline): checkmark.icloud.fill
    /// - De organización, solo referencia (cloud): icloud
    /// </summary>
    [Ignore]
    public string SyncStatusIcon
    {
        get
        {
            if (Source == "both")
                return "checkmark.icloud.fill"; // Descargado de organización (disponible offline)
            if (Source == "remote" || (string.IsNullOrEmpty(LocalClipPath) && !string.IsNullOrEmpty(ClipPath)))
                return "icloud"; // Solo referenciado en organización (necesita conexión)
            if (Source == "local" || (IsSynced == 0 && !string.IsNullOrEmpty(LocalClipPath)))
                return "person.fill"; // Propio del usuario (personal)
            return "questionmark.circle"; // Desconocido
        }
    }

    /// <summary>
    /// Color del indicador de sincronización
    /// - Personal: gris discreto (propio, nada que indicar especial)
    /// - Descargado de organización: verde (disponible offline)
    /// - Solo referenciado: azul (solo cloud, necesita conexión)
    /// </summary>
    [Ignore]
    public string SyncStatusColor
    {
        get
        {
            if (Source == "both")
                return "#4CAF50"; // Verde - descargado de organización
            if (Source == "remote" || NeedsDownload)
                return "#2196F3"; // Azul - solo referenciado (cloud)
            if (Source == "local" && IsSynced == 0)
                return "#9E9E9E"; // Gris - personal propio
            return "#9E9E9E"; // Gris - desconocido
        }
    }

    /// <summary>
    /// Texto descriptivo del estado de sincronización
    /// </summary>
    [Ignore]
    public string SyncStatusText
    {
        get
        {
            if (Source == "both")
                return "Descargado de organización";
            if (Source == "remote")
                return "Solo en organización";
            if (Source == "local" && IsSynced == 0)
                return "Vídeo personal";
            if (NeedsUpload)
                return "Pendiente de subir";
            return "Vídeo personal";
        }
    }

    /// <summary>
    /// Indica si se debe mostrar el badge de estado de sincronización.
    /// Solo se muestra para vídeos de organización (referenciados o descargados).
    /// Los vídeos personales propios no muestran badge.
    /// </summary>
    [Ignore]
    public bool ShowSyncBadge => Source == "remote" || Source == "both";

    // Propiedades computadas
    [Ignore]
    public DateTime CreationDateTime => DateTimeOffset.FromUnixTimeSeconds(CreationDate).LocalDateTime;

    /// <summary>
    /// Primera línea de display: APELLIDOS Nombre - Sesión (o ComparisonName para videos comparativos)
    /// </summary>
    [Ignore]
    public string DisplayLine1
    {
        get
        {
            // Si es un video de comparación, mostrar el ComparisonName
            if (IsComparisonVideo && !string.IsNullOrWhiteSpace(ComparisonName))
            {
                return ComparisonName;
            }
            
            var parts = new List<string>();
            
            // Atleta: APELLIDO Nombre
            if (Atleta != null && !string.IsNullOrWhiteSpace(Atleta.Apellido))
            {
                var apellido = Atleta.Apellido.ToUpperInvariant();
                var nombre = Atleta.Nombre ?? "";
                parts.Add($"{apellido} {nombre}".Trim());
            }
            else if (Atleta != null && !string.IsNullOrWhiteSpace(Atleta.Nombre))
            {
                parts.Add(Atleta.Nombre);
            }
            
            // Sesión
            if (Session != null && !string.IsNullOrWhiteSpace(Session.DisplayName))
            {
                parts.Add(Session.DisplayName);
            }
            
            return parts.Count > 0 ? string.Join(" - ", parts) : "";
        }
    }

    /// <summary>
    /// Segunda línea de display: Lugar, fecha y hora
    /// </summary>
    [Ignore]
    public string DisplayLine2
    {
        get
        {
            var parts = new List<string>();
            
            // Lugar
            if (Session != null && !string.IsNullOrWhiteSpace(Session.Lugar))
            {
                parts.Add(Session.Lugar);
            }
            
            // Fecha y hora
            if (CreationDate > 0)
            {
                parts.Add(CreationDateTime.ToString("dd/MM/yyyy HH:mm"));
            }
            
            return parts.Count > 0 ? string.Join(", ", parts) : "";
        }
    }

    [Ignore]
    public string DurationFormatted
    {
        get
        {
            if (ClipDuration <= 0) return "00:00";
            var ts = TimeSpan.FromSeconds(ClipDuration);
            return ts.TotalMinutes >= 1 
                ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}" 
                : $"0:{ts.Seconds:D2}";
        }
    }

    [Ignore]
    public string SizeFormatted
    {
        get
        {
            if (ClipSize <= 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = ClipSize;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    [Column("localClipPath")]
    public string? LocalClipPath { get; set; }

    [Column("localThumbnailPath")]
    public string? LocalThumbnailPath { get; set; }

    /// <summary>
    /// Ruta efectiva de la miniatura, verificando existencia.
    /// Prioridad: LocalThumbnailPath (si existe) > ThumbnailPath > null
    /// </summary>
    [Ignore]
    public string? EffectiveThumbnailPath
    {
        get
        {
            // 1. Si LocalThumbnailPath existe en disco, usarla
            if (!string.IsNullOrWhiteSpace(LocalThumbnailPath) && File.Exists(LocalThumbnailPath))
                return LocalThumbnailPath;

            // 2. Si ThumbnailPath es una URL remota, usarla directamente
            if (!string.IsNullOrWhiteSpace(ThumbnailPath)
                && (ThumbnailPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || ThumbnailPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return ThumbnailPath;
            }
            
            // 3. Si ThumbnailPath es una ruta absoluta que existe, usarla
            if (!string.IsNullOrWhiteSpace(ThumbnailPath) && Path.IsPathRooted(ThumbnailPath) && File.Exists(ThumbnailPath))
                return ThumbnailPath;
            
            // 4. Intentar construir ruta desde CrownData estándar
            var appDataPath = FileSystem.AppDataDirectory;
            var standardPath = Path.Combine(appDataPath, "CrownData", "sessions", SessionId.ToString(), "thumbnails");
            
            // Buscar con nombre normalizado (CROWN{Id}_thumb.jpg)
            var normalizedThumbName = $"CROWN{Id}_thumb.jpg";
            var standardThumbPath = Path.Combine(standardPath, normalizedThumbName);
            if (File.Exists(standardThumbPath))
                return standardThumbPath;
            
            // Buscar con nombre del ThumbnailPath original
            if (!string.IsNullOrWhiteSpace(ThumbnailPath))
            {
                var thumbFileName = Path.GetFileName(ThumbnailPath.Replace('\\', '/'));
                if (!string.IsNullOrWhiteSpace(thumbFileName))
                {
                    var altPath = Path.Combine(standardPath, thumbFileName);
                    if (File.Exists(altPath))
                        return altPath;
                }
            }
            
            // 5. No se encontró miniatura
            return null;
        }
    }

    [Ignore]
    public Athlete? Atleta
    {
        get => _atleta;
        set
        {
            if (ReferenceEquals(_atleta, value))
                return;
            _atleta = value;
            OnPropertyChanged(nameof(Atleta));
            OnPropertyChanged(nameof(DisplayLine1));
        }
    }

    [Ignore]
    public Session? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
                return;
            _session = value;
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(DisplayLine1));
            OnPropertyChanged(nameof(DisplayLine2));
        }
    }

    /// <summary>
    /// Tags asignados al video (TimeStamp == 0)
    /// </summary>
    [Ignore]
    public List<Tag>? Tags
    {
        get => _tags;
        set
        {
            if (ReferenceEquals(_tags, value))
                return;
            _tags = value;
            OnPropertyChanged(nameof(Tags));
            OnPropertyChanged(nameof(HasTags));
            OnPropertyChanged(nameof(TagsSummary));
            OnPropertyChanged(nameof(HasTagsSummary));
        }
    }

    /// <summary>
    /// Tags de eventos del video (TimeStamp > 0)
    /// </summary>
    [Ignore]
    public List<Tag>? EventTags
    {
        get => _eventTags;
        set
        {
            if (ReferenceEquals(_eventTags, value))
                return;
            _eventTags = value;
            OnPropertyChanged(nameof(EventTags));
            OnPropertyChanged(nameof(HasEventTags));
            OnPropertyChanged(nameof(TagsSummary));
            OnPropertyChanged(nameof(HasTagsSummary));
        }
    }

    /// <summary>
    /// Indica si el video tiene tags asignados
    /// </summary>
    [Ignore]
    public bool HasTags => Tags != null && Tags.Count > 0;

    /// <summary>
    /// Indica si el video tiene eventos
    /// </summary>
    [Ignore]
    public bool HasEventTags => EventTags != null && EventTags.Count > 0;

    /// <summary>
    /// Resumen compacto de tags para la galería (máximo 3 + resto)
    /// </summary>
    [Ignore]
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

    /// <summary>
    /// Indica si hay tags para mostrar en resumen
    /// </summary>
    [Ignore]
    public bool HasTagsSummary => !string.IsNullOrWhiteSpace(TagsSummary);

    /// <summary>
    /// Indica si el video tiene sección/tramo asignado
    /// </summary>
    [Ignore]
    public bool HasSection => Section > 0;

    /// <summary>
    /// Indica si el video está seleccionado (para selección múltiple en galería)
    /// </summary>
    [Ignore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }
    
    /// <summary>
    /// Se usa para mostrar un indicador en la miniatura de la galería.
    /// </summary>
    [Ignore]
    public bool HasTiming
    {
        get => _hasTiming;
        set
        {
            if (_hasTiming != value)
            {
                _hasTiming = value;
                OnPropertyChanged(nameof(HasTiming));
            }
        }
    }

    /// <summary>
    /// Indica si el vídeo está siendo reproducido actualmente en el reproductor.
    /// Se usa para resaltar el video activo en la galería del SinglePlayerPage.
    /// </summary>
    [Ignore]
    public bool IsCurrentlyPlaying
    {
        get => _isCurrentlyPlaying;
        set
        {
            if (_isCurrentlyPlaying != value)
            {
                _isCurrentlyPlaying = value;
                OnPropertyChanged(nameof(IsCurrentlyPlaying));
            }
        }
    }

    public void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
