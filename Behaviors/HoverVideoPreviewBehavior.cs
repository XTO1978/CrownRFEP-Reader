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
    private CancellationTokenSource? _hoverCts;
    private bool _isHovering;

#if MACCATALYST
    private UIHoverGestureRecognizer? _hoverRecognizer;
#endif

    // Evento estático para comunicar con el page
    public static event EventHandler<HoverVideoEventArgs>? VideoHoverStarted;
    public static event EventHandler<HoverVideoEventArgs>? VideoHoverEnded;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);

        bindable.HandlerChanged += OnHandlerChanged;

        // Fallback: PointerGestureRecognizer
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => OnPointerEntered(bindable);
        pointer.PointerExited += (_, _) => OnPointerExited(bindable);
        bindable.GestureRecognizers.Add(pointer);

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
                switch (gesture.State)
                {
                    case UIGestureRecognizerState.Began:
                        OnPointerEntered(view);
                        break;
                    case UIGestureRecognizerState.Ended:
                    case UIGestureRecognizerState.Cancelled:
                        OnPointerExited(view);
                        break;
                }
            });
            platformView.AddGestureRecognizer(_hoverRecognizer);
        }
#endif
    }

    private async void OnPointerEntered(View bindable)
    {
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
                VideoHoverStarted?.Invoke(this, new HoverVideoEventArgs(video, bindable));
            }
        }
        catch (TaskCanceledException)
        {
            // Hover cancelado
        }
    }

    private void OnPointerExited(View bindable)
    {
        _isHovering = false;
        _hoverCts?.Cancel();

        var video = FindVideoClip(bindable);
        if (video != null)
        {
            VideoHoverEnded?.Invoke(this, new HoverVideoEventArgs(video, bindable));
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
}

public class HoverVideoEventArgs : EventArgs
{
    public VideoClip Video { get; }
    public View SourceView { get; }

    public HoverVideoEventArgs(VideoClip video, View sourceView)
    {
        Video = video;
        SourceView = sourceView;
    }
}
