using CrownRFEP_Reader.Models;
using Microsoft.Maui.Controls;

#if IOS
using UIKit;
using Foundation;
#endif

namespace CrownRFEP_Reader.Behaviors;

/// <summary>
/// Behavior para iOS que muestra una previsualización del video con pulsación larga.
/// Simula el gesto "Peek" de iOS para previsualizar contenido.
/// </summary>
public class LongPressVideoPreviewBehavior : Behavior<View>
{
    private const double MinimumPressDuration = 0.25; // 250ms para activar (rápido)

#if IOS
    private UILongPressGestureRecognizer? _longPressRecognizer;
#endif

    // Evento estático para comunicar con el page
    public static event EventHandler<LongPressVideoEventArgs>? VideoLongPressStarted;
    public static event EventHandler<LongPressVideoEventArgs>? VideoLongPressEnded;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.HandlerChanged += OnHandlerChanged;
        TryAttachLongPress(bindable);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;

#if IOS
        if (_longPressRecognizer != null && bindable.Handler?.PlatformView is UIView view)
        {
            view.RemoveGestureRecognizer(_longPressRecognizer);
            _longPressRecognizer.Dispose();
            _longPressRecognizer = null;
        }
#endif

        base.OnDetachingFrom(bindable);
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is View v)
            TryAttachLongPress(v);
    }

    private void TryAttachLongPress(View view)
    {
#if IOS
        if (view.Handler?.PlatformView is UIView platformView && _longPressRecognizer == null)
        {
            _longPressRecognizer = new UILongPressGestureRecognizer(gesture =>
            {
                switch (gesture.State)
                {
                    case UIGestureRecognizerState.Began:
                        OnLongPressStarted(view, gesture);
                        break;
                    case UIGestureRecognizerState.Ended:
                    case UIGestureRecognizerState.Cancelled:
                    case UIGestureRecognizerState.Failed:
                        OnLongPressEnded(view);
                        break;
                }
            });

            _longPressRecognizer.MinimumPressDuration = MinimumPressDuration;
            // Permitir que otros gestos funcionen
            _longPressRecognizer.CancelsTouchesInView = false;
            _longPressRecognizer.DelaysTouchesEnded = false;

            platformView.AddGestureRecognizer(_longPressRecognizer);
        }
#endif
    }

#if IOS
    private void OnLongPressStarted(View bindable, UILongPressGestureRecognizer gesture)
    {
        var video = FindVideoClip(bindable);
        if (video != null)
        {
            // Obtener la posición del gesto para posicionar el popup
            var location = gesture.LocationInView(gesture.View);
            var position = new Point(location.X, location.Y);
            
            VideoLongPressStarted?.Invoke(this, new LongPressVideoEventArgs(video, bindable, position));
        }
    }
#endif

    private void OnLongPressEnded(View bindable)
    {
        var video = FindVideoClip(bindable);
        if (video != null)
        {
            VideoLongPressEnded?.Invoke(this, new LongPressVideoEventArgs(video, bindable, Point.Zero));
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

public class LongPressVideoEventArgs : EventArgs
{
    public VideoClip Video { get; }
    public View SourceView { get; }
    public Point PressPosition { get; }

    public LongPressVideoEventArgs(VideoClip video, View sourceView, Point pressPosition)
    {
        Video = video;
        SourceView = sourceView;
        PressPosition = pressPosition;
    }
}
