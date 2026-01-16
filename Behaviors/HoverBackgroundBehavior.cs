using Microsoft.Maui.Controls;

#if MACCATALYST
using UIKit;
#endif

namespace CrownRFEP_Reader.Behaviors;

public class HoverBackgroundBehavior : Behavior<View>
{
    public static bool HoverEnabled { get; set; } = true;
    private Color? _originalBackground;
    private bool _isHovering;
    private bool _isApplyingHover;

#if MACCATALYST
    private UIHoverGestureRecognizer? _hoverRecognizer;
#endif

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);

        bindable.HandlerChanged += OnHandlerChanged;
        bindable.PropertyChanged += OnBindablePropertyChanged;

#if !MACCATALYST
        // Fallback: PointerGestureRecognizer solo para Windows y otras plataformas (NO MacCatalyst)
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => ApplyHover(bindable);
        pointer.PointerExited += (_, _) => RemoveHover(bindable);
        bindable.GestureRecognizers.Add(pointer);
#endif

        TryAttachPlatformHover(bindable);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        bindable.PropertyChanged -= OnBindablePropertyChanged;

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

    private void OnBindablePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VisualElement.BackgroundColor))
            return;

        if (sender is not VisualElement element)
            return;

        // Si el fondo cambia por VSM/Triggers mientras estamos en hover, queremos restaurar a ese nuevo valor.
        if (_isHovering && !_isApplyingHover)
            _originalBackground = element.BackgroundColor;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is View v)
            TryAttachPlatformHover(v);
    }

    private static Color ResolveHoverColor()
    {
        if (Application.Current?.Resources.TryGetValue("Gray600", out var value) == true && value is Color c)
            return c;

        // Fallback sutil.
        return Color.FromArgb("#FF3A3A3A");
    }

    private void ApplyHover(VisualElement element)
    {
        if (!HoverEnabled)
            return;

        if (_originalBackground == null)
            _originalBackground = element.BackgroundColor;

        _isHovering = true;
        _isApplyingHover = true;
        element.BackgroundColor = ResolveHoverColor();
        _isApplyingHover = false;
    }

    private void RemoveHover(VisualElement element)
    {
        _isHovering = false;

        if (_originalBackground != null)
        {
            _isApplyingHover = true;
            element.BackgroundColor = _originalBackground;
            _isApplyingHover = false;
            _originalBackground = null;
        }
        else
        {
            _isApplyingHover = true;
            element.BackgroundColor = Colors.Transparent;
            _isApplyingHover = false;
        }
    }

    private void TryAttachPlatformHover(View bindable)
    {
#if MACCATALYST
        if (_hoverRecognizer != null)
            return;

        if (bindable.Handler?.PlatformView is not UIView view)
            return;

        // Habilita interacción de puntero en MacCatalyst/iPadOS.
        view.UserInteractionEnabled = true;
        
        // Desactivar el efecto de pointer/hover automático de UIKit
        foreach (var interaction in view.Interactions.ToArray())
        {
            if (interaction is UIPointerInteraction)
            {
                view.RemoveInteraction(interaction);
            }
        }

        _hoverRecognizer = new UIHoverGestureRecognizer(recognizer =>
        {
            switch (recognizer.State)
            {
                case UIGestureRecognizerState.Began:
                case UIGestureRecognizerState.Changed:
                    if (HoverEnabled)
                        ApplyHover(bindable);
                    else
                        RemoveHover(bindable);
                    break;

                case UIGestureRecognizerState.Ended:
                case UIGestureRecognizerState.Cancelled:
                case UIGestureRecognizerState.Failed:
                    RemoveHover(bindable);
                    break;
            }
        });

        // Importante en MacCatalyst: no cancelar clicks/taps del control.
        _hoverRecognizer.CancelsTouchesInView = false;
        _hoverRecognizer.DelaysTouchesBegan = false;
        _hoverRecognizer.DelaysTouchesEnded = false;

        view.AddGestureRecognizer(_hoverRecognizer);
#endif
    }
}
