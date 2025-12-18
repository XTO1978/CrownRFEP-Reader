using Microsoft.Maui.Controls;

namespace CrownRFEP_Reader.Views.Controls;

/// <summary>
/// Contenedor que mantiene una proporción (aspect ratio) fija.
/// El alto se calcula automáticamente basándose en el ancho y la proporción.
/// Usa SizeAllocated para ajustarse dinámicamente.
/// </summary>
public class AspectRatioContainer : ContentView
{
    public static readonly BindableProperty AspectRatioProperty =
        BindableProperty.Create(
            nameof(AspectRatio),
            typeof(double),
            typeof(AspectRatioContainer),
            16.0 / 9.0, // Valor por defecto: 16:9
            propertyChanged: OnAspectRatioChanged);

    /// <summary>
    /// Proporción ancho/alto. Por ejemplo: 16/9 = 1.777, 4/3 = 1.333
    /// </summary>
    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    private static void OnAspectRatioChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AspectRatioContainer container)
        {
            container.UpdateHeight();
        }
    }

    public AspectRatioContainer()
    {
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        UpdateHeight();
    }

    private void UpdateHeight()
    {
        if (Width > 0 && AspectRatio > 0)
        {
            var newHeight = Width / AspectRatio;
            if (Math.Abs(HeightRequest - newHeight) > 1)
            {
                HeightRequest = newHeight;
            }
        }
    }
}
