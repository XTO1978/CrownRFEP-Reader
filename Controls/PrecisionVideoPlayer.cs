using System.ComponentModel;

namespace CrownRFEP_Reader.Controls;

/// <summary>
/// Control de reproducción de vídeo con precisión frame-by-frame.
/// Usa AVPlayer nativo en MacCatalyst/iOS para control preciso.
/// </summary>
public class PrecisionVideoPlayer : View, INotifyPropertyChanged
{
    // Flag para evitar bucles de actualización de posición
    private bool _isUpdatingPositionFromHandler;

    #region Bindable Properties

    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(string),
        typeof(PrecisionVideoPlayer),
        null,
        propertyChanged: OnSourceChanged);

    public static readonly BindableProperty PositionProperty = BindableProperty.Create(
        nameof(Position),
        typeof(TimeSpan),
        typeof(PrecisionVideoPlayer),
        TimeSpan.Zero,
        BindingMode.TwoWay,
        propertyChanged: OnPositionChanged);

    public static readonly BindableProperty DurationProperty = BindableProperty.Create(
        nameof(Duration),
        typeof(TimeSpan),
        typeof(PrecisionVideoPlayer),
        TimeSpan.Zero,
        BindingMode.OneWayToSource);

    public static readonly BindableProperty IsPlayingProperty = BindableProperty.Create(
        nameof(IsPlaying),
        typeof(bool),
        typeof(PrecisionVideoPlayer),
        false,
        BindingMode.TwoWay);

    public static readonly BindableProperty SpeedProperty = BindableProperty.Create(
        nameof(Speed),
        typeof(double),
        typeof(PrecisionVideoPlayer),
        1.0,
        propertyChanged: OnSpeedChanged);

    public static readonly BindableProperty IsMutedProperty = BindableProperty.Create(
        nameof(IsMuted),
        typeof(bool),
        typeof(PrecisionVideoPlayer),
        false,
        propertyChanged: OnIsMutedChanged);

    public static readonly BindableProperty AspectProperty = BindableProperty.Create(
        nameof(Aspect),
        typeof(Aspect),
        typeof(PrecisionVideoPlayer),
        Aspect.AspectFit,
        propertyChanged: OnAspectChanged);

    #endregion

    #region Properties

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public TimeSpan Position
    {
        get => (TimeSpan)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public double Speed
    {
        get => (double)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    public Aspect Aspect
    {
        get => (Aspect)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    /// <summary>
    /// Frame rate del vídeo (detectado automáticamente, por defecto 30fps)
    /// </summary>
    public double FrameRate { get; internal set; } = 30.0;

    /// <summary>
    /// Duración de un frame en segundos
    /// </summary>
    public double FrameDuration => 1.0 / FrameRate;

    #endregion

    #region Events

    public event EventHandler? MediaOpened;
    public event EventHandler? MediaEnded;
    public event EventHandler<TimeSpan>? PositionChanged;

    internal void RaiseMediaOpened() => MediaOpened?.Invoke(this, EventArgs.Empty);
    internal void RaiseMediaEnded() => MediaEnded?.Invoke(this, EventArgs.Empty);
    internal void RaisePositionChanged(TimeSpan position) => PositionChanged?.Invoke(this, position);

    #endregion

    #region Commands

    /// <summary>
    /// Reproduce el vídeo
    /// </summary>
    public void Play()
    {
        PlayRequested?.Invoke(this, EventArgs.Empty);
        IsPlaying = true;
    }

    /// <summary>
    /// Pausa el vídeo
    /// </summary>
    public void Pause()
    {
        PauseRequested?.Invoke(this, EventArgs.Empty);
        IsPlaying = false;
    }

    /// <summary>
    /// Detiene el vídeo y vuelve al inicio
    /// </summary>
    public void Stop()
    {
        StopRequested?.Invoke(this, EventArgs.Empty);
        IsPlaying = false;
    }

    /// <summary>
    /// Avanza un frame
    /// </summary>
    public void StepForward()
    {
        StepForwardRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Retrocede un frame
    /// </summary>
    public void StepBackward()
    {
        StepBackwardRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Busca una posición específica con precisión de frame
    /// </summary>
    public void SeekTo(TimeSpan position)
    {
        SeekRequested?.Invoke(this, position);
    }

    // Eventos internos para comunicación con el handler
    internal event EventHandler? PlayRequested;
    internal event EventHandler? PauseRequested;
    internal event EventHandler? StopRequested;
    internal event EventHandler? StepForwardRequested;
    internal event EventHandler? StepBackwardRequested;
    internal event EventHandler<TimeSpan>? SeekRequested;

    #endregion

    #region Property Changed Handlers

    private static void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PrecisionVideoPlayer player)
        {
            player.SourceChangedInternal?.Invoke(player, (string?)newValue);
        }
    }

    private static void OnPositionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // Solo hacer seek si el cambio viene del binding (no del handler)
        if (bindable is PrecisionVideoPlayer player && newValue is TimeSpan position)
        {
            // Evitar bucle: no disparar si la actualización viene del handler
            if (!player._isUpdatingPositionFromHandler)
            {
                player.PositionChangedFromBinding?.Invoke(player, position);
            }
        }
    }

    private static void OnSpeedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PrecisionVideoPlayer player && newValue is double speed)
        {
            player.SpeedChangedInternal?.Invoke(player, speed);
        }
    }

    private static void OnIsMutedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PrecisionVideoPlayer player && newValue is bool muted)
        {
            player.MutedChangedInternal?.Invoke(player, muted);
        }
    }

    private static void OnAspectChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PrecisionVideoPlayer player && newValue is Aspect aspect)
        {
            player.AspectChangedInternal?.Invoke(player, aspect);
        }
    }

    // Eventos internos para el handler
    internal event EventHandler<string?>? SourceChangedInternal;
    internal event EventHandler<TimeSpan>? PositionChangedFromBinding;
    internal event EventHandler<double>? SpeedChangedInternal;
    internal event EventHandler<bool>? MutedChangedInternal;
    internal event EventHandler<Aspect>? AspectChangedInternal;

    #endregion

    #region Internal Methods (called by handler)

    /// <summary>
    /// Evento para notificar al handler que debe prepararse para limpieza.
    /// El handler debe detener timers y desuscribirse de eventos nativos.
    /// </summary>
    internal event EventHandler? PrepareForCleanupRequested;

    /// <summary>
    /// Prepara el control para limpieza antes de navegación.
    /// Llama a este método en OnDisappearing para evitar callbacks tardíos.
    /// </summary>
    public void PrepareForCleanup()
    {
        PrepareForCleanupRequested?.Invoke(this, EventArgs.Empty);
    }

    internal void UpdatePosition(TimeSpan position)
    {
        // Actualiza la posición sin disparar el evento de cambio desde binding
        _isUpdatingPositionFromHandler = true;
        SetValue(PositionProperty, position);
        _isUpdatingPositionFromHandler = false;
        RaisePositionChanged(position);
    }

    internal void UpdateDuration(TimeSpan duration)
    {
        SetValue(DurationProperty, duration);
    }

    internal void UpdateFrameRate(double frameRate)
    {
        if (frameRate > 0)
            FrameRate = frameRate;
    }

    #endregion
}
