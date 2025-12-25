namespace CrownRFEP_Reader.Services;

/// <summary>
/// Implementación por defecto de IUIScalingService que no aplica escalado.
/// Se usa en plataformas donde no se necesita escalado adaptativo (ej: MacCatalyst).
/// </summary>
public class DefaultUIScalingService : IUIScalingService
{
    public double ScaleFactor => 1.0;
    public double ScreenWidth => 1920;
    public double ScreenHeight => 1080;
    public double SystemDpi => 96;
    public ScreenSizeCategory SizeCategory => ScreenSizeCategory.Medium;

    public event EventHandler? ScaleChanged;

    public double Scale(double value) => value;

    public double ScaleFont(double baseFontSize) => baseFontSize;

    public Thickness ScaleThickness(Thickness baseThickness) => baseThickness;

    public void UpdateScale()
    {
        // No-op para plataformas sin escalado adaptativo
    }
}
