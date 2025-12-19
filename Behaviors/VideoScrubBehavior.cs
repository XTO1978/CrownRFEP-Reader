using System.Diagnostics;

#if MACCATALYST
using UIKit;
using Foundation;
#endif

namespace CrownRFEP_Reader.Behaviors;

/// <summary>
/// Behavior para manejar scrubbing de video mediante scroll (trackpad) o arrastre (mouse)
/// </summary>
public class VideoScrubBehavior : Behavior<View>
{
    private View? _attachedView;
    private double _lastScrollX;
    private DateTime _lastSeekTime = DateTime.MinValue;
    private const int ThrottleMs = 50; // Limitar seeks a 20 por segundo

    /// <summary>
    /// Evento que se dispara cuando el usuario hace scrub
    /// </summary>
    public static event EventHandler<VideoScrubEventArgs>? ScrubUpdated;

    /// <summary>
    /// Evento que se dispara cuando el scrubbing termina
    /// </summary>
    public static event EventHandler<VideoScrubEventArgs>? ScrubEnded;

    /// <summary>
    /// Identificador del video (1 = video 1, 2 = video 2, 0 = single)
    /// </summary>
    public int VideoIndex { get; set; }

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _attachedView = bindable;
        
#if MACCATALYST
        bindable.HandlerChanged += OnHandlerChanged;
#endif
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);
        
#if MACCATALYST
        bindable.HandlerChanged -= OnHandlerChanged;
#endif
        
        _attachedView = null;
    }

#if MACCATALYST
    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_attachedView?.Handler?.PlatformView is UIView uiView)
        {
            // Añadir reconocedor de scroll (trackpad con dos dedos)
            var scrollRecognizer = new UIPanGestureRecognizer(HandleScrollGesture);
            scrollRecognizer.MinimumNumberOfTouches = 2;
            scrollRecognizer.MaximumNumberOfTouches = 2;
            uiView.AddGestureRecognizer(scrollRecognizer);
            
            // Añadir reconocedor de pan (mouse con click)
            var panRecognizer = new UIPanGestureRecognizer(HandlePanGesture);
            panRecognizer.MinimumNumberOfTouches = 1;
            panRecognizer.MaximumNumberOfTouches = 1;
            panRecognizer.AllowedScrollTypesMask = UIScrollTypeMask.Discrete | UIScrollTypeMask.Continuous;
            uiView.AddGestureRecognizer(panRecognizer);
        }
    }

    private void HandleScrollGesture(UIPanGestureRecognizer recognizer)
    {
        HandleGesture(recognizer);
    }

    private void HandlePanGesture(UIPanGestureRecognizer recognizer)
    {
        HandleGesture(recognizer);
    }

    private void HandleGesture(UIPanGestureRecognizer recognizer)
    {
        var translation = recognizer.TranslationInView(recognizer.View);
        
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                _lastScrollX = 0;
                // Notificar inicio (para pausar el video)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScrubUpdated?.Invoke(this, new VideoScrubEventArgs(VideoIndex, 0, true));
                });
                break;
                
            case UIGestureRecognizerState.Changed:
                // Throttle para evitar demasiadas llamadas
                var now = DateTime.Now;
                if ((now - _lastSeekTime).TotalMilliseconds < ThrottleMs)
                    return;
                
                _lastSeekTime = now;
                
                // Calcular delta desde última posición
                var deltaX = translation.X - _lastScrollX;
                _lastScrollX = translation.X;
                
                // Notificar el cambio (sensibilidad: 1 pixel = 5ms)
                var deltaMs = deltaX * 5;
                
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScrubUpdated?.Invoke(this, new VideoScrubEventArgs(VideoIndex, deltaMs, false));
                });
                break;
                
            case UIGestureRecognizerState.Ended:
            case UIGestureRecognizerState.Cancelled:
                // Notificar fin del scrubbing
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScrubEnded?.Invoke(this, new VideoScrubEventArgs(VideoIndex, 0, false));
                });
                break;
        }
    }
#endif
}

/// <summary>
/// Argumentos del evento de scrubbing
/// </summary>
public class VideoScrubEventArgs : EventArgs
{
    public int VideoIndex { get; }
    public double DeltaMilliseconds { get; }
    public bool IsStart { get; }

    public VideoScrubEventArgs(int videoIndex, double deltaMs, bool isStart)
    {
        VideoIndex = videoIndex;
        DeltaMilliseconds = deltaMs;
        IsStart = isStart;
    }
}
