namespace CrownRFEP_Reader.Views.Controls;

/// <summary>
/// Control MAUI para mostrar el preview de webcam en Windows.
/// Actualmente es un placeholder - la funcionalidad de cámara PiP
/// se puede implementar en una iteración futura usando MediaCapture.
/// </summary>
public sealed class WebcamPreview : View
{
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive),
        typeof(bool),
        typeof(WebcamPreview),
        false);

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
}
