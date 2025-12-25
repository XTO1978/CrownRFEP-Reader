using System.ComponentModel;
using Microsoft.Maui.Controls.Xaml;

namespace CrownRFEP_Reader.Markup;

/// <summary>
/// Extensión de markup XAML para escalar valores según la pantalla actual.
/// Uso: WidthRequest="{markup:Scaled Value=200}"
/// </summary>
[ContentProperty(nameof(Value))]
public class ScaledExtension : IMarkupExtension<double>
{
    /// <summary>
    /// Valor base a escalar.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Indica si es un valor de fuente (usa escala de fuente más conservadora).
    /// </summary>
    public bool IsFont { get; set; }

    public double ProvideValue(IServiceProvider serviceProvider)
    {
        var scalingService = GetScalingService();
        if (scalingService == null)
            return Value;

        return IsFont ? scalingService.ScaleFont(Value) : scalingService.Scale(Value);
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
    {
        return ProvideValue(serviceProvider);
    }

    private static Services.IUIScalingService? GetScalingService()
    {
        try
        {
            return Application.Current?.Handler?.MauiContext?.Services
                .GetService<Services.IUIScalingService>();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Extensión de markup XAML para escalar Thickness según la pantalla actual.
/// Uso: Margin="{markup:ScaledThickness Left=10, Top=5, Right=10, Bottom=5}"
/// </summary>
public class ScaledThicknessExtension : IMarkupExtension<Thickness>
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }

    /// <summary>
    /// Valor uniforme para todos los lados.
    /// </summary>
    public double Uniform
    {
        set
        {
            Left = Top = Right = Bottom = value;
        }
    }

    public Thickness ProvideValue(IServiceProvider serviceProvider)
    {
        var scalingService = GetScalingService();
        var baseThickness = new Thickness(Left, Top, Right, Bottom);
        
        if (scalingService == null)
            return baseThickness;

        return scalingService.ScaleThickness(baseThickness);
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
    {
        return ProvideValue(serviceProvider);
    }

    private static Services.IUIScalingService? GetScalingService()
    {
        try
        {
            return Application.Current?.Handler?.MauiContext?.Services
                .GetService<Services.IUIScalingService>();
        }
        catch
        {
            return null;
        }
    }
}
