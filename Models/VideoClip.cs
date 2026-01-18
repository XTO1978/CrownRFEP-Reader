using SQLite;
using System.ComponentModel;
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

    [Column("clipPath")]
    public string? ClipPath { get; set; }

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

    [Ignore]
    public Athlete? Atleta { get; set; }

    [Ignore]
    public Session? Session { get; set; }

    /// <summary>
    /// Tags asignados al video (TimeStamp == 0)
    /// </summary>
    [Ignore]
    public List<Tag>? Tags { get; set; }

    /// <summary>
    /// Tags de eventos del video (TimeStamp > 0)
    /// </summary>
    [Ignore]
    public List<Tag>? EventTags { get; set; }

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
                        .Where(n => !string.IsNullOrWhiteSpace(n)));
            }
            if (EventTags != null)
            {
                names.AddRange(
                    EventTags.Where(t => t != null)
                        .Select(t => t!.DisplayText)
                        .Where(n => !string.IsNullOrWhiteSpace(n)));
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
    private List<Tag>? _tags;
    private List<Tag>? _eventTags;
        set
        {
            if (_isSelected != value)
            {
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
            }
        }
    }

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

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
