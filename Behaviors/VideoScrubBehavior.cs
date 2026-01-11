using System.Diagnostics;

#if MACCATALYST
using UIKit;
using Foundation;
#endif

#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
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
    /// Sensibilidad del scrubbing: milisegundos a avanzar/retroceder por cada pixel de arrastre horizontal.
    /// </summary>
    public double MillisecondsPerPixel { get; set; } = 5;

#if WINDOWS
    private UIElement? _winElement;
    private bool _isWinScrubbing;
    private uint _winPointerId;
    private double _winLastX;
#endif

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

    #if WINDOWS
        bindable.HandlerChanged += OnHandlerChanged;
        TryAttachWindowsPointerHandlers();
    #endif
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);
        
#if MACCATALYST
        bindable.HandlerChanged -= OnHandlerChanged;
#endif

    #if WINDOWS
        bindable.HandlerChanged -= OnHandlerChanged;
        DetachWindowsPointerHandlers();
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
                
                // Notificar el cambio
                var deltaMs = deltaX * MillisecondsPerPixel;
                
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

#if WINDOWS
    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        TryAttachWindowsPointerHandlers();
    }

    private void TryAttachWindowsPointerHandlers()
    {
        if (_attachedView?.Handler?.PlatformView is not UIElement element)
            return;

        if (ReferenceEquals(_winElement, element))
            return;

        DetachWindowsPointerHandlers();

        _winElement = element;
        _winElement.PointerPressed += OnWinPointerPressed;
        _winElement.PointerMoved += OnWinPointerMoved;
        _winElement.PointerReleased += OnWinPointerReleased;
        _winElement.PointerCanceled += OnWinPointerReleased;
        _winElement.PointerCaptureLost += OnWinPointerCaptureLost;
    }

    private void DetachWindowsPointerHandlers()
    {
        if (_winElement == null)
            return;

        _winElement.PointerPressed -= OnWinPointerPressed;
        _winElement.PointerMoved -= OnWinPointerMoved;
        _winElement.PointerReleased -= OnWinPointerReleased;
        _winElement.PointerCanceled -= OnWinPointerReleased;
        _winElement.PointerCaptureLost -= OnWinPointerCaptureLost;
        _winElement = null;

        _isWinScrubbing = false;
        _winPointerId = 0;
        _winLastX = 0;
    }

    private void OnWinPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (_winElement == null)
                return;

            var point = e.GetCurrentPoint(_winElement);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            _isWinScrubbing = true;
            _winPointerId = e.Pointer.PointerId;
            _winLastX = point.Position.X;
            _lastSeekTime = DateTime.MinValue;
            _lastScrollX = 0;

            _winElement.CapturePointer(e.Pointer);
            e.Handled = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScrubUpdated?.Invoke(this, new VideoScrubEventArgs(VideoIndex, 0, true));
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoScrubBehavior] Win PointerPressed error: {ex}");
        }
    }

    private void OnWinPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isWinScrubbing || _winElement == null)
            return;

        if (e.Pointer.PointerId != _winPointerId)
            return;

        var point = e.GetCurrentPoint(_winElement);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        // Throttle para evitar demasiadas llamadas
        var now = DateTime.Now;
        if ((now - _lastSeekTime).TotalMilliseconds < ThrottleMs)
            return;

        _lastSeekTime = now;

        var currentX = point.Position.X;
        var deltaX = currentX - _winLastX;
        _winLastX = currentX;

        // Sensibilidad configurable
        var deltaMs = deltaX * MillisecondsPerPixel;

        e.Handled = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScrubUpdated?.Invoke(this, new VideoScrubEventArgs(VideoIndex, deltaMs, false));
        });
    }

    private void OnWinPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isWinScrubbing)
            return;

        if (e.Pointer.PointerId != _winPointerId)
            return;

        FinishWindowsScrub();
        e.Handled = true;
    }

    private void OnWinPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isWinScrubbing)
            return;

        FinishWindowsScrub();
    }

    private void FinishWindowsScrub()
    {
        _isWinScrubbing = false;
        _winPointerId = 0;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScrubEnded?.Invoke(this, new VideoScrubEventArgs(VideoIndex, 0, false));
        });
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
