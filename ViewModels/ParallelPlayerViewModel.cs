using CrownRFEP_Reader.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para el reproductor de vídeos paralelos.
/// Soporta dos modos:
/// - Individual: Cada vídeo tiene sus propios controles
/// - Simultáneo: Reproducción sincronizada con controles globales
/// </summary>
public class ParallelPlayerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private VideoClip? _video1;
    private VideoClip? _video2;
    private bool _isHorizontalOrientation;
    private bool _isSimultaneousMode = false; // Por defecto modo individual
    private double _playbackSpeed = 1.0;

    // Estado global (modo simultáneo)
    private bool _isPlaying;
    private TimeSpan _currentPosition;
    private TimeSpan _duration;
    private double _progress;

    // Estado individual video 1
    private bool _isPlaying1;
    private TimeSpan _currentPosition1;
    private TimeSpan _duration1;
    private double _progress1;

    // Estado individual video 2
    private bool _isPlaying2;
    private TimeSpan _currentPosition2;
    private TimeSpan _duration2;
    private double _progress2;

    // Puntos de sincronización (posiciones de referencia al sincronizar)
    private TimeSpan _syncPoint1 = TimeSpan.Zero;
    private TimeSpan _syncPoint2 = TimeSpan.Zero;
    private TimeSpan _syncDuration = TimeSpan.Zero; // Duración efectiva en modo sincronizado

    public ParallelPlayerViewModel()
    {
        // Comandos globales (modo simultáneo)
        PlayPauseCommand = new Command(TogglePlayPause);
        StopCommand = new Command(Stop);
        SeekBackwardCommand = new Command(() => Seek(-5));
        SeekForwardCommand = new Command(() => Seek(5));
        FrameBackwardCommand = new Command(StepBackward);
        FrameForwardCommand = new Command(StepForward);

        // Comandos individuales video 1
        PlayPauseCommand1 = new Command(TogglePlayPause1);
        StopCommand1 = new Command(Stop1);
        SeekBackwardCommand1 = new Command(() => Seek1(-5));
        SeekForwardCommand1 = new Command(() => Seek1(5));
        FrameBackwardCommand1 = new Command(StepBackward1);
        FrameForwardCommand1 = new Command(StepForward1);

        // Comandos individuales video 2
        PlayPauseCommand2 = new Command(TogglePlayPause2);
        StopCommand2 = new Command(Stop2);
        SeekBackwardCommand2 = new Command(() => Seek2(-5));
        SeekForwardCommand2 = new Command(() => Seek2(5));
        FrameBackwardCommand2 = new Command(StepBackward2);
        FrameForwardCommand2 = new Command(StepForward2);

        // Comandos comunes
        SetSpeedCommand = new Command<string>(SetSpeed);
        ToggleOrientationCommand = new Command(ToggleOrientation);
        ToggleModeCommand = new Command(ToggleMode);
        CloseCommand = new Command(async () => await CloseAsync());
    }

    #region Propiedades de vídeos

    public VideoClip? Video1
    {
        get => _video1;
        set { _video1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasVideo1)); }
    }

    public VideoClip? Video2
    {
        get => _video2;
        set { _video2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasVideo2)); }
    }

    public bool HasVideo1 => _video1 != null;
    public bool HasVideo2 => _video2 != null;

    #endregion

    #region Propiedades de orientación y modo

    public bool IsHorizontalOrientation
    {
        get => _isHorizontalOrientation;
        set
        {
            _isHorizontalOrientation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVerticalOrientation));
        }
    }

    public bool IsVerticalOrientation => !_isHorizontalOrientation;

    public bool IsSimultaneousMode
    {
        get => _isSimultaneousMode;
        set
        {
            if (_isSimultaneousMode != value)
            {
                _isSimultaneousMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIndividualMode));
                OnPropertyChanged(nameof(ModeIcon));
                OnPropertyChanged(nameof(ModeText));
                ModeChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsIndividualMode => !_isSimultaneousMode;
    public string ModeIcon => IsSimultaneousMode ? "link.badge.plus" : "link";
    public string ModeText => IsSimultaneousMode ? "Individual" : "Sincronizar";

    #endregion

    #region Estado global (modo simultáneo)

    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon)); }
    }

    public string PlayPauseIcon => IsPlaying ? "pause.fill" : "play.fill";

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            _currentPosition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionText));
            UpdateProgress();
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            _duration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText));
            UpdateProgress();
        }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    public string CurrentPositionText => $"{CurrentPosition:mm\\:ss\\.ff}";
    public string DurationText => $"{Duration:mm\\:ss\\.ff}";

    #endregion

    #region Estado individual video 1

    public bool IsPlaying1
    {
        get => _isPlaying1;
        set { _isPlaying1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon1)); }
    }

    public string PlayPauseIcon1 => IsPlaying1 ? "pause.fill" : "play.fill";

    public TimeSpan CurrentPosition1
    {
        get => _currentPosition1;
        set
        {
            _currentPosition1 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionText1));
            UpdateProgress1();
        }
    }

    public TimeSpan Duration1
    {
        get => _duration1;
        set
        {
            _duration1 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText1));
            UpdateProgress1();
        }
    }

    public double Progress1
    {
        get => _progress1;
        set { _progress1 = value; OnPropertyChanged(); }
    }

    public string CurrentPositionText1 => $"{CurrentPosition1:mm\\:ss\\.ff}";
    public string DurationText1 => $"{Duration1:mm\\:ss\\.ff}";

    #endregion

    #region Estado individual video 2

    public bool IsPlaying2
    {
        get => _isPlaying2;
        set { _isPlaying2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon2)); }
    }

    public string PlayPauseIcon2 => IsPlaying2 ? "pause.fill" : "play.fill";

    public TimeSpan CurrentPosition2
    {
        get => _currentPosition2;
        set
        {
            _currentPosition2 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionText2));
            UpdateProgress2();
        }
    }

    public TimeSpan Duration2
    {
        get => _duration2;
        set
        {
            _duration2 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText2));
            UpdateProgress2();
        }
    }

    public double Progress2
    {
        get => _progress2;
        set { _progress2 = value; OnPropertyChanged(); }
    }

    public string CurrentPositionText2 => $"{CurrentPosition2:mm\\:ss\\.ff}";
    public string DurationText2 => $"{Duration2:mm\\:ss\\.ff}";

    #endregion

    #region Velocidad

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set { _playbackSpeed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); }
    }

    public string SpeedText => $"{PlaybackSpeed:0.##}x";

    #endregion

    #region Comandos globales

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand FrameBackwardCommand { get; }
    public ICommand FrameForwardCommand { get; }

    #endregion

    #region Comandos individuales video 1

    public ICommand PlayPauseCommand1 { get; }
    public ICommand StopCommand1 { get; }
    public ICommand SeekBackwardCommand1 { get; }
    public ICommand SeekForwardCommand1 { get; }
    public ICommand FrameBackwardCommand1 { get; }
    public ICommand FrameForwardCommand1 { get; }

    #endregion

    #region Comandos individuales video 2

    public ICommand PlayPauseCommand2 { get; }
    public ICommand StopCommand2 { get; }
    public ICommand SeekBackwardCommand2 { get; }
    public ICommand SeekForwardCommand2 { get; }
    public ICommand FrameBackwardCommand2 { get; }
    public ICommand FrameForwardCommand2 { get; }

    #endregion

    #region Comandos comunes

    public ICommand SetSpeedCommand { get; }
    public ICommand ToggleOrientationCommand { get; }
    public ICommand ToggleModeCommand { get; }
    public ICommand CloseCommand { get; }

    #endregion

    #region Eventos

    // Eventos globales (modo simultáneo)
    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? FrameForwardRequested;
    public event EventHandler? FrameBackwardRequested;

    // Eventos individuales video 1
    public event EventHandler? PlayRequested1;
    public event EventHandler? PauseRequested1;
    public event EventHandler? StopRequested1;
    public event EventHandler<double>? SeekRequested1;
    public event EventHandler? FrameForwardRequested1;
    public event EventHandler? FrameBackwardRequested1;

    // Eventos individuales video 2
    public event EventHandler? PlayRequested2;
    public event EventHandler? PauseRequested2;
    public event EventHandler? StopRequested2;
    public event EventHandler<double>? SeekRequested2;
    public event EventHandler? FrameForwardRequested2;
    public event EventHandler? FrameBackwardRequested2;

    // Eventos comunes
    public event EventHandler<double>? SpeedChangeRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler<bool>? ModeChanged;
    
    /// <summary>
    /// Se dispara cuando se establece la sincronización con los puntos de referencia.
    /// Los argumentos son (SyncPoint1, SyncPoint2).
    /// </summary>
    public event EventHandler<(TimeSpan SyncPoint1, TimeSpan SyncPoint2)>? SyncPointsEstablished;

    #endregion

    #region Propiedades de sincronización

    /// <summary>
    /// Punto de referencia del vídeo 1 para sincronización
    /// </summary>
    public TimeSpan SyncPoint1
    {
        get => _syncPoint1;
        private set { _syncPoint1 = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Punto de referencia del vídeo 2 para sincronización
    /// </summary>
    public TimeSpan SyncPoint2
    {
        get => _syncPoint2;
        private set { _syncPoint2 = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Duración efectiva en modo sincronizado (la menor de las duraciones restantes)
    /// </summary>
    public TimeSpan SyncDuration
    {
        get => _syncDuration;
        private set { _syncDuration = value; OnPropertyChanged(); }
    }

    #endregion

    #region Métodos públicos

    public void Initialize(VideoClip? video1, VideoClip? video2, bool isHorizontal)
    {
        Video1 = video1;
        Video2 = video2;
        IsHorizontalOrientation = isHorizontal;
        IsSimultaneousMode = false; // Iniciar en modo individual
        ResetAllStates();
    }

    public void SeekToPosition(double position)
    {
        var newPosition = position * Duration.TotalSeconds;
        SeekRequested?.Invoke(this, newPosition);
    }

    public void SeekToPosition1(double position)
    {
        var newPosition = position * Duration1.TotalSeconds;
        SeekRequested1?.Invoke(this, newPosition);
    }

    public void SeekToPosition2(double position)
    {
        var newPosition = position * Duration2.TotalSeconds;
        SeekRequested2?.Invoke(this, newPosition);
    }

    #endregion

    #region Métodos privados - Globales

    private void TogglePlayPause()
    {
        IsPlaying = !IsPlaying;
        if (IsPlaying)
            PlayRequested?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        IsPlaying = false;
        CurrentPosition = TimeSpan.Zero;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Seek(double seconds)
    {
        var newPosition = CurrentPosition.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration.TotalSeconds));
        SeekRequested?.Invoke(this, newPosition);
    }

    private void StepForward()
    {
        IsPlaying = false;
        FrameForwardRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StepBackward()
    {
        IsPlaying = false;
        FrameBackwardRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateProgress()
    {
        if (Duration.TotalSeconds > 0)
            Progress = CurrentPosition.TotalSeconds / Duration.TotalSeconds;
        else
            Progress = 0;
    }

    #endregion

    #region Métodos privados - Video 1

    private void TogglePlayPause1()
    {
        IsPlaying1 = !IsPlaying1;
        if (IsPlaying1)
            PlayRequested1?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested1?.Invoke(this, EventArgs.Empty);
    }

    private void Stop1()
    {
        IsPlaying1 = false;
        CurrentPosition1 = TimeSpan.Zero;
        StopRequested1?.Invoke(this, EventArgs.Empty);
    }

    private void Seek1(double seconds)
    {
        var newPosition = CurrentPosition1.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration1.TotalSeconds));
        SeekRequested1?.Invoke(this, newPosition);
    }

    private void StepForward1()
    {
        IsPlaying1 = false;
        FrameForwardRequested1?.Invoke(this, EventArgs.Empty);
    }

    private void StepBackward1()
    {
        IsPlaying1 = false;
        FrameBackwardRequested1?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateProgress1()
    {
        if (Duration1.TotalSeconds > 0)
            Progress1 = CurrentPosition1.TotalSeconds / Duration1.TotalSeconds;
        else
            Progress1 = 0;
    }

    #endregion

    #region Métodos privados - Video 2

    private void TogglePlayPause2()
    {
        IsPlaying2 = !IsPlaying2;
        if (IsPlaying2)
            PlayRequested2?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested2?.Invoke(this, EventArgs.Empty);
    }

    private void Stop2()
    {
        IsPlaying2 = false;
        CurrentPosition2 = TimeSpan.Zero;
        StopRequested2?.Invoke(this, EventArgs.Empty);
    }

    private void Seek2(double seconds)
    {
        var newPosition = CurrentPosition2.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration2.TotalSeconds));
        SeekRequested2?.Invoke(this, newPosition);
    }

    private void StepForward2()
    {
        IsPlaying2 = false;
        FrameForwardRequested2?.Invoke(this, EventArgs.Empty);
    }

    private void StepBackward2()
    {
        IsPlaying2 = false;
        FrameBackwardRequested2?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateProgress2()
    {
        if (Duration2.TotalSeconds > 0)
            Progress2 = CurrentPosition2.TotalSeconds / Duration2.TotalSeconds;
        else
            Progress2 = 0;
    }

    #endregion

    #region Métodos privados - Comunes

    private void SetSpeed(string? speedStr)
    {
        if (double.TryParse(speedStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var speed))
        {
            PlaybackSpeed = speed;
            SpeedChangeRequested?.Invoke(this, speed);
        }
    }

    private void ToggleOrientation()
    {
        IsHorizontalOrientation = !IsHorizontalOrientation;
    }

    private void ToggleMode()
    {
        // Pausar todo antes de cambiar de modo
        if (IsSimultaneousMode)
        {
            // Saliendo del modo sincronizado → volver a individual
            if (IsPlaying) Stop();
            
            // Resetear los sync points
            SyncPoint1 = TimeSpan.Zero;
            SyncPoint2 = TimeSpan.Zero;
            SyncDuration = TimeSpan.Zero;
        }
        else
        {
            // Entrando al modo sincronizado → capturar posiciones actuales como puntos de referencia
            if (IsPlaying1) 
            {
                PauseRequested1?.Invoke(this, EventArgs.Empty);
                IsPlaying1 = false;
            }
            if (IsPlaying2) 
            {
                PauseRequested2?.Invoke(this, EventArgs.Empty);
                IsPlaying2 = false;
            }
            
            // Guardar las posiciones actuales como puntos de sincronización
            SyncPoint1 = CurrentPosition1;
            SyncPoint2 = CurrentPosition2;
            
            // Calcular la duración efectiva sincronizada
            // Es el mínimo entre lo que queda de cada vídeo desde su punto de sync
            var remaining1 = Duration1 - SyncPoint1;
            var remaining2 = Duration2 - SyncPoint2;
            SyncDuration = remaining1 < remaining2 ? remaining1 : remaining2;
            
            // Inicializar la posición global a 0 (inicio de la sincronización)
            Duration = SyncDuration;
            CurrentPosition = TimeSpan.Zero;
            Progress = 0;
            
            // Notificar los puntos de sincronización establecidos
            SyncPointsEstablished?.Invoke(this, (SyncPoint1, SyncPoint2));
        }

        IsSimultaneousMode = !IsSimultaneousMode;
    }

    private async Task CloseAsync()
    {
        Stop();
        Stop1();
        Stop2();
        CloseRequested?.Invoke(this, EventArgs.Empty);
        await Shell.Current.Navigation.PopAsync();
    }

    private void ResetAllStates()
    {
        IsPlaying = false;
        CurrentPosition = TimeSpan.Zero;
        Progress = 0;
        PlaybackSpeed = 1.0;

        IsPlaying1 = false;
        CurrentPosition1 = TimeSpan.Zero;
        Progress1 = 0;

        IsPlaying2 = false;
        CurrentPosition2 = TimeSpan.Zero;
        Progress2 = 0;
    }

    #endregion

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
