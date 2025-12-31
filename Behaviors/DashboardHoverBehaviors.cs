using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;
using Microsoft.Maui.Controls;

#if MACCATALYST
using UIKit;
#endif

namespace CrownRFEP_Reader.Behaviors;

internal static class HoverHelpers
{
    public static Color ResolveRestColor()
    {
        // Usamos el mismo fondo del Dashboard para que los items parezcan transparentes,
        // pero sin dejar ver el highlight/hover nativo del contenedor (UICollectionViewCell) en MacCatalyst.
        return Color.FromArgb("#FF121212");
    }

    public static Color ResolveHoverColor()
    {
        if (Application.Current?.Resources.TryGetValue("Gray600", out var value) == true && value is Color c)
            return c;

        return Color.FromArgb("#FF3A3A3A");
    }

    public static DashboardViewModel? FindDashboardViewModel(Element start)
    {
        Element? current = start;
        while (current != null)
        {
            if (current is Page page && page.BindingContext is DashboardViewModel vm)
                return vm;

            current = current.Parent;
        }

        return null;
    }
}

public class DashboardSessionItemHoverBehavior : Behavior<Border>
{
#if !MACCATALYST
    private PointerGestureRecognizer? _pointerRecognizer;
#endif

#if MACCATALYST
    private UIHoverGestureRecognizer? _hoverRecognizer;
#endif

    protected override void OnAttachedTo(Border bindable)
    {
        base.OnAttachedTo(bindable);

        bindable.HandlerChanged += OnHandlerChanged;
        
#if !MACCATALYST
        // Fallback: PointerGestureRecognizer solo para Windows y otras plataformas (NO MacCatalyst)
        _pointerRecognizer = new PointerGestureRecognizer();
        _pointerRecognizer.PointerEntered += (_, _) => ApplyHover(bindable);
        _pointerRecognizer.PointerExited += (_, _) => RemoveHover(bindable);
        bindable.GestureRecognizers.Add(_pointerRecognizer);
#endif
        
        TryAttachPlatformHover(bindable);
    }

    protected override void OnDetachingFrom(Border bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        
#if !MACCATALYST
        if (_pointerRecognizer != null)
        {
            bindable.GestureRecognizers.Remove(_pointerRecognizer);
            _pointerRecognizer = null;
        }
#endif

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
            TryAttachPlatformHover(b);
    }

    private bool IsSelected(Border border)
    {
        if (border.BindingContext is not Session session)
            return false;

        var vm = HoverHelpers.FindDashboardViewModel(border);
        return vm?.SelectedSession == session;
    }

    private void ApplyHover(Border border)
    {
        // Aplicar color de hover
        border.BackgroundColor = HoverHelpers.ResolveHoverColor();
    }

    private void RemoveHover(Border border)
    {
        // Si está seleccionado, mantener color de selección
        if (IsSelected(border))
        {
            border.BackgroundColor = Color.FromArgb("#FF3A3A3A");
            return;
        }

        // Si no está seleccionado, restaurar al color base del Dashboard (simula transparencia)
        border.BackgroundColor = HoverHelpers.ResolveRestColor();
    }

    private void TryAttachPlatformHover(Border bindable)
    {
#if MACCATALYST
        if (_hoverRecognizer != null)
            return;

        if (bindable.Handler?.PlatformView is not UIView view)
            return;

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
                    ApplyHover(bindable);
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

public class DashboardAllGalleryHoverBehavior : Behavior<Border>
{
#if !MACCATALYST
    private PointerGestureRecognizer? _pointerRecognizer;
#endif

#if MACCATALYST
    private UIHoverGestureRecognizer? _hoverRecognizer;
#endif

    protected override void OnAttachedTo(Border bindable)
    {
        base.OnAttachedTo(bindable);

        bindable.HandlerChanged += OnHandlerChanged;
        
#if !MACCATALYST
        // Fallback: PointerGestureRecognizer solo para Windows y otras plataformas (NO MacCatalyst)
        _pointerRecognizer = new PointerGestureRecognizer();
        _pointerRecognizer.PointerEntered += (_, _) => ApplyHover(bindable);
        _pointerRecognizer.PointerExited += (_, _) => RemoveHover(bindable);
        bindable.GestureRecognizers.Add(_pointerRecognizer);
#endif
        
        TryAttachPlatformHover(bindable);
    }

    protected override void OnDetachingFrom(Border bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        
#if !MACCATALYST
        if (_pointerRecognizer != null)
        {
            bindable.GestureRecognizers.Remove(_pointerRecognizer);
            _pointerRecognizer = null;
        }
#endif

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
            TryAttachPlatformHover(b);
    }

    private bool IsSelected(Border border)
    {
        var vm = HoverHelpers.FindDashboardViewModel(border);
        return vm?.IsAllGallerySelected == true;
    }

    private void ApplyHover(Border border)
    {
        // Aplicar color de hover
        border.BackgroundColor = HoverHelpers.ResolveHoverColor();
    }

    private void RemoveHover(Border border)
    {
        // Si está seleccionado, mantener color de selección
        if (IsSelected(border))
        {
            border.BackgroundColor = Color.FromArgb("#FF3A3A3A");
            return;
        }

        // Si no está seleccionado, restaurar al color base del Dashboard (simula transparencia)
        border.BackgroundColor = HoverHelpers.ResolveRestColor();
    }

    private void TryAttachPlatformHover(Border bindable)
    {
#if MACCATALYST
        if (_hoverRecognizer != null)
            return;

        if (bindable.Handler?.PlatformView is not UIView view)
            return;

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
                    ApplyHover(bindable);
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
