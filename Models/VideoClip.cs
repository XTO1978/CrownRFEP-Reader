using SQLite;
using System.ComponentModel;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un clip de video de entrenamiento
/// </summary>
[Table("videoClip")]
public class VideoClip : INotifyPropertyChanged
{
    private bool _isSelected;

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

    // Propiedades adicionales del JSON de exportación
    [Ignore]
    public bool IsComparisonVideo { get; set; }

    [Ignore]
    public string? BadgeText { get; set; }

    [Ignore]
    public string? BadgeBackgroundColor { get; set; }

    // Propiedades computadas
    [Ignore]
    public DateTime CreationDateTime => DateTimeOffset.FromUnixTimeSeconds(CreationDate).LocalDateTime;

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
    /// Tags asociados al video (cargados desde la tabla inputs)
    /// </summary>
    [Ignore]
    public List<Tag>? Tags { get; set; }

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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
