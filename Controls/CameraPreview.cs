namespace CrownRFEP_Reader.Controls;

/// <summary>
/// Control de MAUI para mostrar el preview de la cámara.
/// El handler específico de plataforma conectará el preview layer nativo.
/// </summary>
public class CameraPreview : View
{
    public static readonly BindableProperty PreviewHandleProperty = BindableProperty.Create(
        nameof(PreviewHandle),
        typeof(object),
        typeof(CameraPreview),
        null,
        propertyChanged: OnPreviewHandleChanged);

    public object? PreviewHandle
    {
        get => GetValue(PreviewHandleProperty);
        set => SetValue(PreviewHandleProperty, value);
    }

    private static void OnPreviewHandleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CameraPreview preview)
        {
            preview.Handler?.UpdateValue(nameof(PreviewHandle));
        }
    }
}
