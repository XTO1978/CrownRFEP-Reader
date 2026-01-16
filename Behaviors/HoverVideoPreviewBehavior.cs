using CrownRFEP_Reader.Models;
using Microsoft.Maui.Controls;

#if MACCATALYST
using UIKit;
#endif

namespace CrownRFEP_Reader.Behaviors;

/// <summary>
/// Behavior que notifica cuando hay hover sobre un video item.
/// Dispara eventos que el code-behind puede manejar para mostrar un MediaElement flotante.
/// </summary>
public class HoverVideoPreviewBehavior : Behavior<View>
{
    private const int HoverDelayMs = 200;
    public static bool HoverEnabled { get; set; } = true;
    private CancellationTokenSource? _hoverCts;
    private bool _isHovering;

    // Última posición del puntero dentro del SourceView (si el sistema la proporciona)
    private Point? _lastPointerLocationInSourceView;

#if MACCATALYST
    private UIHoverGestureRecognizer? _hoverRecognizer;
#endif

    // Evento estático para comunicar con el page
    public static event EventHandler<HoverVideoEventArgs>? VideoHoverStarted;
    public static event EventHandler<HoverVideoEventArgs>? VideoHoverEnded;
    public static event EventHandler<HoverVideoEventArgs>? VideoHoverMoved;

    private Point? _lastSentPointerLocation;
    private DateTime _lastSentPointerAtUtc = DateTime.MinValue;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);

        bindable.HandlerChanged += OnHandlerChanged;

#if WINDOWS
        // PointerGestureRecognizer solo para Windows (iOS no lo soporta)
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, e) =>
        {
            try { _lastPointerLocationInSourceView = e.GetPosition(bindable); } catch { _lastPointerLocationInSourceView = null; }
            OnPointerEntered(bindable);
        };
        pointer.PointerMoved += (_, e) =>
        {
            try { _lastPointerLocationInSourceView = e.GetPosition(bindable); } catch { _lastPointerLocationInSourceView = null; }
            TryRaiseMoved(bindable);
        };
        pointer.PointerExited += (_, _) =>
        {
            _lastPointerLocationInSourceView = null;
            OnPointerExited(bindable);
        };
        bindable.GestureRecognizers.Add(pointer);
#endif

        TryAttachPlatformHover(bindable);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = null;

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
        if (sender is View v)
            TryAttachPlatformHover(v);
    }

    private void TryAttachPlatformHover(View view)
    {
#if MACCATALYST
        if (view.Handler?.PlatformView is UIView platformView && _hoverRecognizer == null)
        {
            _hoverRecognizer = new UIHoverGestureRecognizer(gesture =>
            {
                try
                {
                    var pt = gesture.LocationInView(platformView);
                    _lastPointerLocationInSourceView = new Point(pt.X, pt.Y);
                }
                catch
                {
                    _lastPointerLocationInSourceView = null;
                }

                switch (gesture.State)
                {
                    case UIGestureRecognizerState.Began:
                        OnPointerEntered(view);
                        break;
                    case UIGestureRecognizerState.Changed:
                        TryRaiseMoved(view);
                        break;
                    case UIGestureRecognizerState.Ended:
                    case UIGestureRecognizerState.Cancelled:
                        OnPointerExited(view);
                        break;
                }
            });

            // Importante en MacCatalyst: no cancelar clicks/taps del control.
            _hoverRecognizer.CancelsTouchesInView = false;
            _hoverRecognizer.DelaysTouchesBegan = false;
            _hoverRecognizer.DelaysTouchesEnded = false;

            platformView.AddGestureRecognizer(_hoverRecognizer);
        }
#endif
    }

    private async void OnPointerEntered(View bindable)
    {
        if (!HoverEnabled)
            return;

        if (_isHovering) return;
        _isHovering = true;

        _hoverCts?.Cancel();
        _hoverCts = new CancellationTokenSource();
        var token = _hoverCts.Token;

        try
        {
            // Esperar 200ms antes de iniciar la reproducción
            await Task.Delay(HoverDelayMs, token);

            if (token.IsCancellationRequested) return;

            // Obtener el VideoClip del BindingContext (puede estar en un ancestro)
            var video = FindVideoClip(bindable);
            if (video != null)
            {
                VideoHoverStarted?.Invoke(this, new HoverVideoEventArgs(video, bindable, _lastPointerLocationInSourceView));
            }
        }
        catch (TaskCanceledException)
        {
            // Hover cancelado
        }
    }

    private void OnPointerExited(View bindable)
    {
        if (!HoverEnabled)
        {
            _isHovering = false;
            _hoverCts?.Cancel();
        }

        _isHovering = false;
        _hoverCts?.Cancel();

        _lastSentPointerLocation = null;
        _lastSentPointerAtUtc = DateTime.MinValue;

        var video = FindVideoClip(bindable);
        if (video != null)
        {
            VideoHoverEnded?.Invoke(this, new HoverVideoEventArgs(video, bindable, _lastPointerLocationInSourceView));
        }
    }

    /// <summary>
    /// Busca el VideoClip en el BindingContext del elemento o sus ancestros.
    /// </summary>
    private static VideoClip? FindVideoClip(View view)
    {
        Element? current = view;
        while (current != null)
        {
            if (current.BindingContext is VideoClip video)
            {
                return video;
            }
            current = current.Parent;
        }
        return null;
    }

    private void TryRaiseMoved(View bindable)
    {
        if (!HoverEnabled)
            return;

        if (!_isHovering)
            return;

        if (_lastPointerLocationInSourceView is not Point pointer)
            return;

        // Throttle: como máximo ~60fps y solo si se mueve de verdad
        var now = DateTime.UtcNow;
        if ((now - _lastSentPointerAtUtc).TotalMilliseconds < 16)
            return;

        if (_lastSentPointerLocation is Point last
            && Math.Abs(pointer.X - last.X) < 1
            && Math.Abs(pointer.Y - last.Y) < 1)
            return;

        var video = FindVideoClip(bindable);
        if (video == null)
            return;

        _lastSentPointerLocation = pointer;
        _lastSentPointerAtUtc = now;
        VideoHoverMoved?.Invoke(this, new HoverVideoEventArgs(video, bindable, pointer));
    }
}

public class HoverVideoEventArgs : EventArgs
{
    public VideoClip Video { get; }
    public View SourceView { get; }
    public Point? PointerLocationInSourceView { get; }

    public HoverVideoEventArgs(VideoClip video, View sourceView, Point? pointerLocationInSourceView)
    {
        Video = video;
        SourceView = sourceView;
        PointerLocationInSourceView = pointerLocationInSourceView;
    }
}
