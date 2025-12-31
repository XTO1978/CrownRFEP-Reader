using Microsoft.Maui.Controls;

#if MACCATALYST
using UIKit;
#endif

namespace CrownRFEP_Reader.Behaviors;

/// <summary>
/// Behavior simple de hover para los items de la barra lateral.
/// Solo aplica un color de fondo al pasar el puntero, sin interferir con la selección.
/// </summary>
public class SidebarHoverBehavior : Behavior<Border>
{
    private bool _isHovering;
    private bool _didApplyHover; // Indica si nosotros aplicamos el hover

#if MACCATALYST
    private UIHoverGestureRecognizer? _hoverRecognizer;
#endif

    private static Color HoverColor => Color.FromArgb("#FF404040"); // Gray600
    private static Color SelectionColor => Color.FromArgb("#FF2A2A2A"); // Más oscuro que hover

    protected override void OnAttachedTo(Border bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.HandlerChanged += OnHandlerChanged;
        TryAttachHover(bindable);
    }

    protected override void OnDetachingFrom(Border bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;

#if MACCATALYST
        if (_hoverRecognizer != null && bindable.Handler?.PlatformView is UIView view)
        {
            view.RemoveGestureRecognizer(_hoverRecognizer);
            _hoverRecognizer.Dispose();
            _hoverRecognizer = null;
        }
#endif

        base.OnDetachingFrom(bindable);
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Border b)
            TryAttachHover(b);
    }

    private bool IsSelected(Border border)
    {
        // Comparar el color actual con el de selección
        var bg = border.BackgroundColor;
        if (bg == null) return false;
        
        // Comparar por ARGB hex para evitar problemas de referencia
        return bg.ToArgbHex() == SelectionColor.ToArgbHex();
    }

    private void TryAttachHover(Border bindable)
    {
#if MACCATALYST
        if (_hoverRecognizer != null)
            return;

        if (bindable.Handler?.PlatformView is not UIView view)
            return;

        view.UserInteractionEnabled = true;

        // Desactivar cualquier efecto de hover nativo
        foreach (var interaction in view.Interactions.OfType<UIPointerInteraction>().ToList())
            view.RemoveInteraction(interaction);

        _hoverRecognizer = new UIHoverGestureRecognizer(recognizer =>
        {
            switch (recognizer.State)
            {
                case UIGestureRecognizerState.Began:
                case UIGestureRecognizerState.Changed:
                    if (!_isHovering)
                    {
                        _isHovering = true;
                        ApplyHover(bindable);
                    }
                    break;

                case UIGestureRecognizerState.Ended:
                case UIGestureRecognizerState.Cancelled:
                case UIGestureRecognizerState.Failed:
                    _isHovering = false;
                    RemoveHover(bindable);
                    break;
            }
        });

        _hoverRecognizer.CancelsTouchesInView = false;
        _hoverRecognizer.DelaysTouchesBegan = false;
        _hoverRecognizer.DelaysTouchesEnded = false;

        view.AddGestureRecognizer(_hoverRecognizer);
#endif
    }

    private void ApplyHover(Border border)
    {
        // No aplicar hover si está seleccionado
        if (IsSelected(border))
        {
            _didApplyHover = false;
            return;
        }

        border.BackgroundColor = HoverColor;
        _didApplyHover = true;
    }

    private void RemoveHover(Border border)
    {
        // Solo quitar hover si nosotros lo aplicamos
        if (_didApplyHover)
        {
            border.BackgroundColor = Colors.Transparent;
            _didApplyHover = false;
        }
    }
}

