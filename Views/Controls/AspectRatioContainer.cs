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
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // Forzar actualización cuando el control se carga
        UpdateHeight();
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        UpdateHeight();
    }

    private void UpdateHeight()
    {
        // Si el ancho es 0 o negativo, intentar usar el ancho del padre
        var actualWidth = Width;
        
        if (actualWidth <= 0 && Parent is View parentView)
        {
            actualWidth = parentView.Width;
        }
        
        if (actualWidth > 0 && AspectRatio > 0)
        {
            var newHeight = actualWidth / AspectRatio;
            
            // Establecer un mínimo razonable de altura (100px)
            if (newHeight < 100)
            {
                newHeight = 100;
            }
            
            if (Math.Abs(HeightRequest - newHeight) > 1)
            {
                HeightRequest = newHeight;
            }
        }
        else
        {
            // Si no podemos calcular, establecer altura por defecto
            if (HeightRequest < 100)
            {
                HeightRequest = 150; // Altura por defecto razonable para una miniatura
            }
        }
    }

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        // Si tenemos un ancho válido, calcular la altura basada en el aspect ratio
        if (widthConstraint > 0 && !double.IsInfinity(widthConstraint) && AspectRatio > 0)
        {
            var calculatedHeight = widthConstraint / AspectRatio;
            
            // Establecer HeightRequest si es necesario
            if (Math.Abs(HeightRequest - calculatedHeight) > 1)
            {
                HeightRequest = calculatedHeight;
            }
            
            return new Size(widthConstraint, calculatedHeight);
        }
        
        return base.MeasureOverride(widthConstraint, heightConstraint);
    }
}
