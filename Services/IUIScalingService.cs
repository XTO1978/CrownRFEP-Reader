namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio para adaptar la UI a diferentes tamaños y densidades de pantalla.
/// </summary>
public interface IUIScalingService
{
    /// <summary>
    /// Factor de escala aplicado a la UI (1.0 = pantalla de referencia 1920x1080 16").
    /// </summary>
    double ScaleFactor { get; }

    /// <summary>
    /// Ancho de pantalla en píxeles efectivos.
    /// </summary>
    double ScreenWidth { get; }

    /// <summary>
    /// Alto de pantalla en píxeles efectivos.
    /// </summary>
    double ScreenHeight { get; }

    /// <summary>
    /// DPI del sistema.
    /// </summary>
    double SystemDpi { get; }

    /// <summary>
    /// Categoría de tamaño de pantalla detectada.
    /// </summary>
    ScreenSizeCategory SizeCategory { get; }

    /// <summary>
    /// Escala un valor según el factor de escala actual.
    /// </summary>
    double Scale(double value);

    /// <summary>
    /// Escala un valor de fuente según el factor de escala actual.
    /// </summary>
    double ScaleFont(double baseFontSize);

    /// <summary>
    /// Escala un Thickness según el factor de escala actual.
    /// </summary>
    Thickness ScaleThickness(Thickness baseThickness);

    /// <summary>
    /// Evento disparado cuando cambia la escala (ej: al cambiar de monitor).
    /// </summary>
    event EventHandler? ScaleChanged;

    /// <summary>
    /// Actualiza la información de escala según la pantalla actual.
    /// </summary>
    void UpdateScale();
}

/// <summary>
/// Categorías de tamaño de pantalla.
/// </summary>
public enum ScreenSizeCategory
{
    /// <summary>
    /// Pantallas pequeñas (menos de 1366x768).
    /// </summary>
    Small,

    /// <summary>
    /// Pantallas medianas/estándar (1366x768 a 1920x1080).
    /// </summary>
    Medium,

    /// <summary>
    /// Pantallas grandes (1920x1080 a 2560x1440).
    /// </summary>
    Large,

    /// <summary>
    /// Pantallas extra grandes/4K (más de 2560x1440).
    /// </summary>
    ExtraLarge
}
