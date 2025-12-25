using CrownRFEP_Reader.Helpers;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Behaviors;

/// <summary>
/// Behavior que aplica escalado automático a elementos de la UI según el tamaño de pantalla.
/// Se adjunta a ContentPage o Layout para escalar recursivamente sus elementos.
/// </summary>
public class AdaptiveScalingBehavior : Behavior<VisualElement>
{
    private VisualElement? _attachedElement;
    private IUIScalingService? _scalingService;

    /// <summary>
    /// Indica si se debe escalar los tamaños de fuente.
    /// </summary>
    public bool ScaleFonts { get; set; } = true;

    /// <summary>
    /// Indica si se debe escalar márgenes y padding.
    /// </summary>
    public bool ScaleSpacing { get; set; } = true;

    /// <summary>
    /// Indica si se debe escalar tamaños explícitos (Width/HeightRequest).
    /// </summary>
    public bool ScaleSizes { get; set; } = true;

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        _attachedElement = bindable;

        if (bindable.Handler?.MauiContext?.Services != null)
        {
            _scalingService = bindable.Handler.MauiContext.Services.GetService<IUIScalingService>();
            ApplyScaling(bindable);
            
            if (_scalingService != null)
            {
                _scalingService.ScaleChanged += OnScaleChanged;
            }
        }
        else
        {
            bindable.HandlerChanged += OnHandlerChanged;
        }
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        base.OnDetachingFrom(bindable);
        
        if (_scalingService != null)
        {
            _scalingService.ScaleChanged -= OnScaleChanged;
        }
        
        bindable.HandlerChanged -= OnHandlerChanged;
        _attachedElement = null;
        _scalingService = null;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_attachedElement?.Handler?.MauiContext?.Services != null)
        {
            _scalingService = _attachedElement.Handler.MauiContext.Services.GetService<IUIScalingService>();
            ApplyScaling(_attachedElement);
            
            if (_scalingService != null)
            {
                _scalingService.ScaleChanged += OnScaleChanged;
            }
            
            _attachedElement.HandlerChanged -= OnHandlerChanged;
        }
    }

    private void OnScaleChanged(object? sender, EventArgs e)
    {
        if (_attachedElement != null)
        {
            _attachedElement.Dispatcher.Dispatch(() => ApplyScaling(_attachedElement));
        }
    }

    private void ApplyScaling(VisualElement element)
    {
        if (_scalingService == null || _scalingService.ScaleFactor == 1.0)
            return;

        try
        {
            ApplyScalingRecursive(element);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdaptiveScaling] Error applying scaling: {ex.Message}");
        }
    }

    private void ApplyScalingRecursive(VisualElement element)
    {
        if (_scalingService == null)
            return;

        // Escalar fuentes
        if (ScaleFonts && element is Label label && label.FontSize > 0)
        {
            // Solo escalar si no usa un style dinámico
            if (!IsUsingDynamicStyle(label))
            {
                label.FontSize = _scalingService.ScaleFont(label.FontSize);
            }
        }
        else if (ScaleFonts && element is Button button && button.FontSize > 0)
        {
            button.FontSize = _scalingService.ScaleFont(button.FontSize);
        }
        else if (ScaleFonts && element is Entry entry && entry.FontSize > 0)
        {
            entry.FontSize = _scalingService.ScaleFont(entry.FontSize);
        }
        else if (ScaleFonts && element is Editor editor && editor.FontSize > 0)
        {
            editor.FontSize = _scalingService.ScaleFont(editor.FontSize);
        }

        // Escalar márgenes (Margin está en View, no en VisualElement)
        if (ScaleSpacing && element is View view && view.Margin != default)
        {
            view.Margin = _scalingService.ScaleThickness(view.Margin);
        }

        // Escalar padding para layouts
        if (ScaleSpacing && element is Layout layout && layout.Padding != default)
        {
            layout.Padding = _scalingService.ScaleThickness(layout.Padding);
        }

        // Escalar tamaños explícitos
        if (ScaleSizes)
        {
            if (element.WidthRequest > 0 && element.WidthRequest != double.PositiveInfinity)
            {
                element.WidthRequest = _scalingService.Scale(element.WidthRequest);
            }
            if (element.HeightRequest > 0 && element.HeightRequest != double.PositiveInfinity)
            {
                element.HeightRequest = _scalingService.Scale(element.HeightRequest);
            }
            if (element.MinimumWidthRequest > 0)
            {
                element.MinimumWidthRequest = _scalingService.Scale(element.MinimumWidthRequest);
            }
            if (element.MinimumHeightRequest > 0)
            {
                element.MinimumHeightRequest = _scalingService.Scale(element.MinimumHeightRequest);
            }
        }

        // Escalar spacing de layouts
        if (ScaleSpacing)
        {
            if (element is StackLayout stackLayout && stackLayout.Spacing > 0)
            {
                stackLayout.Spacing = _scalingService.Scale(stackLayout.Spacing);
            }
            else if (element is HorizontalStackLayout hStack && hStack.Spacing > 0)
            {
                hStack.Spacing = _scalingService.Scale(hStack.Spacing);
            }
            else if (element is VerticalStackLayout vStack && vStack.Spacing > 0)
            {
                vStack.Spacing = _scalingService.Scale(vStack.Spacing);
            }
            else if (element is FlexLayout flexLayout)
            {
                // FlexLayout no tiene Spacing directo pero podemos ajustar en los hijos
            }
            else if (element is Grid grid)
            {
                // Escalar row/column spacing
                if (grid.RowSpacing > 0)
                    grid.RowSpacing = _scalingService.Scale(grid.RowSpacing);
                if (grid.ColumnSpacing > 0)
                    grid.ColumnSpacing = _scalingService.Scale(grid.ColumnSpacing);
            }
        }

        // Recursión en hijos
        if (element is IVisualTreeElement visualTree)
        {
            foreach (var child in visualTree.GetVisualChildren())
            {
                if (child is VisualElement childElement)
                {
                    ApplyScalingRecursive(childElement);
                }
            }
        }
    }

    private bool IsUsingDynamicStyle(VisualElement element)
    {
        // Verificar si el elemento usa un estilo definido en recursos
        return element.Style != null;
    }
}
