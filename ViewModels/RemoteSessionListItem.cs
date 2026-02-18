using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.ViewModels;

public sealed class RemoteSessionListItem : INotifyPropertyChanged
{
    public int SessionId { get; }
    public string Title { get; }
    public string? Place { get; }
    public DateTime SessionDate { get; }
    public string? Coach { get; }
    public int VideoCount { get; }
    public DateTime LastModified { get; }

    // ── Propiedades calculadas para la tarjeta estilo Apple Music ──

    public string PlaceDateText
    {
        get
        {
            var place = string.IsNullOrWhiteSpace(Place) ? "—" : Place;
            return $"{place} · {SessionDate:dd/MM}";
        }
    }

    public string CoachText
        => string.IsNullOrWhiteSpace(Coach) ? "—" : Coach;

    /// <summary>Día del mes en formato grande (estilo calendario).</summary>
    public string DayText => SessionDate.Day.ToString();

    /// <summary>Mes abreviado en mayúsculas (ENE, FEB…).</summary>
    public string MonthText => SessionDate.ToString("MMM").ToUpperInvariant();

    /// <summary>Año (solo las 2 últimas cifras).</summary>
    public string YearText => SessionDate.ToString("yy");

    /// <summary>Hora de la sesión.</summary>
    public string TimeText => SessionDate.ToString("HH:mm");

    /// <summary>Lugar formateado (o vacío si no hay).</summary>
    public string PlaceDisplay => string.IsNullOrWhiteSpace(Place) ? "" : Place;

    /// <summary>Indica si hay lugar definido.</summary>
    public bool HasPlace => !string.IsNullOrWhiteSpace(Place);

    /// <summary>Texto descriptivo de nº de videos.</summary>
    public string VideoCountText => VideoCount.ToString();

    /// <summary>Color de fondo según un hash del SessionId para dar variedad visual.</summary>
    public Color CardBackgroundColor => _cardColors[Math.Abs(SessionId) % _cardColors.Length];

    /// <summary>Color de acento ligeramente más claro para el badge.</summary>
    public Color CardAccentColor => _cardAccentColors[Math.Abs(SessionId) % _cardAccentColors.Length];

    /// <summary>Color vivo del lugar — mismo tono que el overlay pero más chillón.</summary>
    public Color CardPlaceTextColor => _cardVividColors[Math.Abs(SessionId) % _cardVividColors.Length];

    // Paleta inspirada en Apple Music — tonos oscuros vivos
    private static readonly Color[] _cardColors =
    [
        Color.FromArgb("#1C1C3A"),  // indigo profundo
        Color.FromArgb("#2A1A2E"),  // púrpura oscuro
        Color.FromArgb("#1A2A2A"),  // teal oscuro
        Color.FromArgb("#2A2215"),  // cálido oscuro
        Color.FromArgb("#1A1A30"),  // azul noche
        Color.FromArgb("#2D1A1A"),  // rojo oscuro
        Color.FromArgb("#1A2D1A"),  // verde bosque
        Color.FromArgb("#2A1A30"),  // violeta oscuro
    ];

    private static readonly Color[] _cardAccentColors =
    [
        Color.FromArgb("#3A3A6A"),
        Color.FromArgb("#4A2A4E"),
        Color.FromArgb("#2A4A4A"),
        Color.FromArgb("#4A4225"),
        Color.FromArgb("#3A3A60"),
        Color.FromArgb("#5D3A3A"),
        Color.FromArgb("#3A5D3A"),
        Color.FromArgb("#4A3A60"),
    ];

    // Versiones vividas/brillantes de los mismos tonos para texto de impacto
    private static readonly Color[] _cardVividColors =
    [
        Color.FromArgb("#7B7BFF"),  // indigo vivo
        Color.FromArgb("#D175E0"),  // púrpura vivo
        Color.FromArgb("#50E8D0"),  // teal vivo
        Color.FromArgb("#F0C850"),  // dorado cálido
        Color.FromArgb("#6090FF"),  // azul eléctrico
        Color.FromArgb("#FF6B6B"),  // rojo coral
        Color.FromArgb("#60E880"),  // verde lima
        Color.FromArgb("#C080FF"),  // violeta brillante
    ];

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private string? _thumbnailSource;
    /// <summary>URL firmada o path local del thumbnail representativo de la sesión.</summary>
    public string? ThumbnailSource
    {
        get => _thumbnailSource;
        set
        {
            if (_thumbnailSource != value)
            {
                _thumbnailSource = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    /// <summary>True si hay un thumbnail disponible para mostrar como fondo.</summary>
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailSource);

    public RemoteSessionListItem(int sessionId, string title, string? place, DateTime sessionDate, string? coach, int videoCount, DateTime lastModified)
    {
        SessionId = sessionId;
        Title = title;
        Place = place;
        SessionDate = sessionDate;
        Coach = coach;
        VideoCount = videoCount;
        LastModified = lastModified;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
