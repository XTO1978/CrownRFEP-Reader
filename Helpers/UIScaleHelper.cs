using CrownRFEP_Reader.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Helpers;

/// <summary>
/// Helper estático para acceder al escalado de UI desde cualquier parte de la aplicación.
/// Proporciona valores escalados comunes y notifica cambios.
/// </summary>
public sealed class UIScaleHelper : INotifyPropertyChanged
{
    private static readonly Lazy<UIScaleHelper> _instance = new(() => new UIScaleHelper());
    public static UIScaleHelper Instance => _instance.Value;

    private IUIScalingService? _scalingService;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    private UIScaleHelper()
    {
    }

    /// <summary>
    /// Inicializa el helper con el servicio de escalado.
    /// Debe llamarse al iniciar la aplicación.
    /// </summary>
    public void Initialize(IUIScalingService scalingService)
    {
        if (_scalingService != null)
        {
            _scalingService.ScaleChanged -= OnScaleChanged;
        }

        _scalingService = scalingService;
        _scalingService.ScaleChanged += OnScaleChanged;
        
        NotifyAllPropertiesChanged();
    }

    private void OnScaleChanged(object? sender, EventArgs e)
    {
        NotifyAllPropertiesChanged();
    }

    private void NotifyAllPropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Scale Factor Properties

    public double ScaleFactor => _scalingService?.ScaleFactor ?? 1.0;
    public ScreenSizeCategory SizeCategory => _scalingService?.SizeCategory ?? ScreenSizeCategory.Medium;

    #endregion

    #region Scaled Font Sizes

    // Tamaños de fuente escalados (valores más compactos para Windows)
    public double FontSizeCaption => _scalingService?.ScaleFont(9) ?? 9;
    public double FontSizeSmall => _scalingService?.ScaleFont(10) ?? 10;
    public double FontSizeBody => _scalingService?.ScaleFont(12) ?? 12;
    public double FontSizeSubtitle => _scalingService?.ScaleFont(13) ?? 13;
    public double FontSizeTitle => _scalingService?.ScaleFont(16) ?? 16;
    public double FontSizeHeader => _scalingService?.ScaleFont(20) ?? 20;
    public double FontSizeDisplay => _scalingService?.ScaleFont(26) ?? 26;
    public double FontSizeLarge => _scalingService?.ScaleFont(40) ?? 40;

    #endregion

    #region Scaled Common Sizes

    // Tamaños de iconos escalados (más compactos)
    public double IconSizeSmall => Scale(14);
    public double IconSizeMedium => Scale(20);
    public double IconSizeLarge => Scale(26);
    public double IconSizeXLarge => Scale(40);

    // Tamaños de botones escalados (más compactos)
    public double ButtonMinHeight => Scale(36);
    public double ButtonMinWidth => Scale(72);
    public double ButtonPadding => Scale(10);

    // Espaciado escalado (más compacto)
    public double SpacingXSmall => Scale(3);
    public double SpacingSmall => Scale(6);
    public double SpacingMedium => Scale(10);
    public double SpacingLarge => Scale(14);
    public double SpacingXLarge => Scale(20);
    public double SpacingXXLarge => Scale(28);

    // Bordes redondeados escalados
    public double CornerRadiusSmall => Scale(3);
    public double CornerRadiusMedium => Scale(6);
    public double CornerRadiusLarge => Scale(10);
    public double CornerRadiusXLarge => Scale(14);

    // Tamaños de panel escalados (más compactos)
    public double SidebarWidth => Scale(240);
    public double TopBarHeight => Scale(48);
    public double StatusBarHeight => Scale(26);

    // Tamaños de thumbnails escalados
    public double ThumbnailSmall => Scale(64);
    public double ThumbnailMedium => Scale(100);
    public double ThumbnailLarge => Scale(150);

    // Tamaños de tarjetas escalados
    public double CardMinWidth => Scale(160);
    public double CardMaxWidth => Scale(340);
    public double CardPadding => Scale(12);

    #endregion

    #region Scaled Thickness (Margins/Padding)

    public Thickness MarginSmall => ScaleThickness(new Thickness(8));
    public Thickness MarginMedium => ScaleThickness(new Thickness(12));
    public Thickness MarginLarge => ScaleThickness(new Thickness(16));
    public Thickness MarginXLarge => ScaleThickness(new Thickness(24));

    public Thickness PaddingSmall => ScaleThickness(new Thickness(8));
    public Thickness PaddingMedium => ScaleThickness(new Thickness(12));
    public Thickness PaddingLarge => ScaleThickness(new Thickness(16));
    public Thickness PaddingXLarge => ScaleThickness(new Thickness(24));

    public Thickness PagePadding => ScaleThickness(new Thickness(24, 16, 24, 16));
    public Thickness CardMargin => ScaleThickness(new Thickness(8));

    #endregion

    #region Helper Methods

    /// <summary>
    /// Escala un valor según el factor de escala actual.
    /// </summary>
    public double Scale(double value)
    {
        return _scalingService?.Scale(value) ?? value;
    }

    /// <summary>
    /// Escala un tamaño de fuente según el factor de escala actual.
    /// </summary>
    public double ScaleFont(double baseFontSize)
    {
        return _scalingService?.ScaleFont(baseFontSize) ?? baseFontSize;
    }

    /// <summary>
    /// Escala un Thickness según el factor de escala actual.
    /// </summary>
    public Thickness ScaleThickness(Thickness baseThickness)
    {
        return _scalingService?.ScaleThickness(baseThickness) ?? baseThickness;
    }

    /// <summary>
    /// Devuelve el número de columnas recomendadas para una cuadrícula según el tamaño de pantalla.
    /// </summary>
    public int GetRecommendedGridColumns(double itemMinWidth = 200)
    {
        var screenWidth = _scalingService?.ScreenWidth ?? 1920;
        var scaleFactor = _scalingService?.ScaleFactor ?? 1.0;
        
        // Calcular columnas basadas en el ancho efectivo
        int columns = (int)(screenWidth / (itemMinWidth * scaleFactor));
        return Math.Max(1, Math.Min(columns, 6)); // Entre 1 y 6 columnas
    }

    /// <summary>
    /// Indica si la pantalla actual es considerada pequeña.
    /// </summary>
    public bool IsSmallScreen => SizeCategory == ScreenSizeCategory.Small;

    /// <summary>
    /// Indica si la pantalla actual es considerada grande o extra grande.
    /// </summary>
    public bool IsLargeScreen => SizeCategory >= ScreenSizeCategory.Large;

    #endregion
}
