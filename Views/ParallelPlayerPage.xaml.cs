using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.ViewModels;

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#elif WINDOWS
using CrownRFEP_Reader.Platforms.Windows;
#endif

namespace CrownRFEP_Reader.Views;

public partial class ParallelPlayerPage : ContentPage
{
    private readonly ParallelPlayerViewModel _viewModel;
    private PrecisionVideoPlayer? _activePlayer1;
    private PrecisionVideoPlayer? _activePlayer2;
    
    // Flags para controlar el scrubbing del slider
    private bool _isDraggingSlider;
    private bool _isDraggingSlider1;
    private bool _isDraggingSlider2;
    private bool _wasPlayingBeforeDrag;
    private bool _wasPlaying1BeforeDrag;
    private bool _wasPlaying2BeforeDrag;

#if WINDOWS
    // Throttle para seeks en Windows (evitar bloqueos)
    private DateTime _lastSeekTime = DateTime.MinValue;
    private DateTime _lastSeekTime1 = DateTime.MinValue;
    private DateTime _lastSeekTime2 = DateTime.MinValue;
    private const int SeekThrottleMs = 50;
#endif
    
    // Flags para controlar el scrubbing con trackpad/mouse
    private bool _isScrubbing1;
    private bool _isScrubbing2;
    private bool _wasPlaying1BeforeScrub;
    private bool _wasPlaying2BeforeScrub;
    private double _currentScrubPosition1;
    private double _currentScrubPosition2;

    public ParallelPlayerPage(ParallelPlayerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Suscribirse a eventos globales del ViewModel (modo simultáneo)
        _viewModel.PlayRequested += OnPlayRequested;
        _viewModel.PauseRequested += OnPauseRequested;
        _viewModel.StopRequested += OnStopRequested;
        _viewModel.SeekRequested += OnSeekRequested;
        _viewModel.FrameForwardRequested += OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested += OnFrameBackwardRequested;

        // Suscribirse a eventos individuales Video 1
        _viewModel.PlayRequested1 += OnPlayRequested1;
        _viewModel.PauseRequested1 += OnPauseRequested1;
        _viewModel.StopRequested1 += OnStopRequested1;
        _viewModel.SeekRequested1 += OnSeekRequested1;
        _viewModel.FrameForwardRequested1 += OnFrameForwardRequested1;
        _viewModel.FrameBackwardRequested1 += OnFrameBackwardRequested1;

        // Suscribirse a eventos individuales Video 2
        _viewModel.PlayRequested2 += OnPlayRequested2;
        _viewModel.PauseRequested2 += OnPauseRequested2;
        _viewModel.StopRequested2 += OnStopRequested2;
        _viewModel.SeekRequested2 += OnSeekRequested2;
        _viewModel.FrameForwardRequested2 += OnFrameForwardRequested2;
        _viewModel.FrameBackwardRequested2 += OnFrameBackwardRequested2;

        // Eventos comunes
        _viewModel.SpeedChangeRequested += OnSpeedChangeRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ModeChanged += OnModeChanged;
        _viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        CleanupResources();
    }

    private void CleanupResources()
    {
        // Desuscribirse de eventos del ViewModel
        _viewModel.PlayRequested -= OnPlayRequested;
        _viewModel.PauseRequested -= OnPauseRequested;
        _viewModel.StopRequested -= OnStopRequested;
        _viewModel.SeekRequested -= OnSeekRequested;
        _viewModel.FrameForwardRequested -= OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested -= OnFrameBackwardRequested;
        
        _viewModel.PlayRequested1 -= OnPlayRequested1;
        _viewModel.PauseRequested1 -= OnPauseRequested1;
        _viewModel.StopRequested1 -= OnStopRequested1;
        _viewModel.SeekRequested1 -= OnSeekRequested1;
        _viewModel.FrameForwardRequested1 -= OnFrameForwardRequested1;
        _viewModel.FrameBackwardRequested1 -= OnFrameBackwardRequested1;
        
        _viewModel.PlayRequested2 -= OnPlayRequested2;
        _viewModel.PauseRequested2 -= OnPauseRequested2;
        _viewModel.StopRequested2 -= OnStopRequested2;
        _viewModel.SeekRequested2 -= OnSeekRequested2;
        _viewModel.FrameForwardRequested2 -= OnFrameForwardRequested2;
        _viewModel.FrameBackwardRequested2 -= OnFrameBackwardRequested2;
        
        _viewModel.SpeedChangeRequested -= OnSpeedChangeRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ModeChanged -= OnModeChanged;
        _viewModel.CloseRequested -= OnCloseRequested;

        // Limpiar handlers de MediaOpened
        if (_activePlayer1 != null)
            _activePlayer1.MediaOpened -= OnMedia1Opened;
        if (_activePlayer2 != null)
            _activePlayer2.MediaOpened -= OnMedia2Opened;

        // Detener todos los reproductores
        StopAllPlayers();

        // Liberar reproductores
        DisposePlayer(MediaPlayer1H);
        DisposePlayer(MediaPlayer2H);
        DisposePlayer(MediaPlayer1V);
        DisposePlayer(MediaPlayer2V);
    }

    private void DisposePlayer(PrecisionVideoPlayer? player)
    {
        if (player != null)
        {
            player.Stop();
#if !MACCATALYST
            player.Handler?.DisconnectHandler();
#endif
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateActiveMediaElements();
        SetupMediaOpenedHandlers();
        
        // Suscribirse a eventos de scrubbing (trackpad/mouse wheel)
        VideoScrubBehavior.ScrubUpdated += OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded += OnScrubEnded;

#if MACCATALYST || WINDOWS
#if WINDOWS
        KeyPressHandler.EnsureAttached();
#endif
        KeyPressHandler.SpaceBarPressed += OnSpaceBarPressed;
        KeyPressHandler.ArrowLeftPressed += OnArrowLeftPressed;
        KeyPressHandler.ArrowRightPressed += OnArrowRightPressed;
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Desuscribirse de eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated -= OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded -= OnScrubEnded;

#if MACCATALYST || WINDOWS
        KeyPressHandler.SpaceBarPressed -= OnSpaceBarPressed;
        KeyPressHandler.ArrowLeftPressed -= OnArrowLeftPressed;
        KeyPressHandler.ArrowRightPressed -= OnArrowRightPressed;
#endif
        
        CleanupResources();
    }

#if MACCATALYST || WINDOWS
    private void OnSpaceBarPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // En modo simultáneo usar el comando global, en modo individual usar el del player seleccionado
            if (_viewModel.IsSimultaneousMode)
                _viewModel.PlayPauseCommand.Execute(null);
            else if (_viewModel.SelectedPlayer == 1)
                _viewModel.PlayPauseCommand1.Execute(null);
            else
                _viewModel.PlayPauseCommand2.Execute(null);
        });
    }

    private void OnArrowLeftPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // En modo simultáneo usar el comando global, en modo individual usar el del player seleccionado
            if (_viewModel.IsSimultaneousMode)
                _viewModel.FrameBackwardCommand.Execute(null);
            else if (_viewModel.SelectedPlayer == 1)
                _viewModel.FrameBackwardCommand1.Execute(null);
            else
                _viewModel.FrameBackwardCommand2.Execute(null);
        });
    }

    private void OnArrowRightPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // En modo simultáneo usar el comando global, en modo individual usar el del player seleccionado
            if (_viewModel.IsSimultaneousMode)
                _viewModel.FrameForwardCommand.Execute(null);
            else if (_viewModel.SelectedPlayer == 1)
                _viewModel.FrameForwardCommand1.Execute(null);
            else
                _viewModel.FrameForwardCommand2.Execute(null);
        });
    }
#endif

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParallelPlayerViewModel.IsHorizontalOrientation))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var currentPosition = _viewModel.CurrentPosition;
                var currentPosition1 = _viewModel.CurrentPosition1;
                var currentPosition2 = _viewModel.CurrentPosition2;
                var wasPlaying = _viewModel.IsPlaying;
                var wasPlaying1 = _viewModel.IsPlaying1;
                var wasPlaying2 = _viewModel.IsPlaying2;

                StopAllPlayers();
                UpdateActiveMediaElements();
                SetupMediaOpenedHandlers();

                // Restaurar posiciones según el modo
                if (_viewModel.IsSimultaneousMode)
                {
                    if (currentPosition > TimeSpan.Zero)
                        SeekBothToPosition(currentPosition.TotalSeconds);
                    if (wasPlaying)
                        PlayBoth();
                }
                else
                {
                    if (currentPosition1 > TimeSpan.Zero)
                        SeekPlayer1ToPosition(currentPosition1.TotalSeconds);
                    if (currentPosition2 > TimeSpan.Zero)
                        SeekPlayer2ToPosition(currentPosition2.TotalSeconds);
                    if (wasPlaying1)
                        _activePlayer1?.Play();
                    if (wasPlaying2)
                        _activePlayer2?.Play();
                }
            });
        }
    }

    private void OnModeChanged(object? sender, bool isSimultaneous)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StopAllPlayers();
            
            if (isSimultaneous)
            {
                // Cambio a modo sincronizado: los sync points ya están establecidos en el ViewModel
                // Los reproductores se mantienen en sus posiciones actuales (los sync points)
                // La posición global empieza en 0 (relativo al punto de sincronización)
                
                // Asegurar que cada reproductor está en su sync point
                _activePlayer1?.SeekTo(_viewModel.SyncPoint1);
                _activePlayer2?.SeekTo(_viewModel.SyncPoint2);
            }
            else
            {
                // Cambio a modo individual: convertir posición global a posiciones absolutas
                var absolutePos1 = _viewModel.SyncPoint1 + _viewModel.CurrentPosition;
                var absolutePos2 = _viewModel.SyncPoint2 + _viewModel.CurrentPosition;
                
                _viewModel.CurrentPosition1 = absolutePos1;
                _viewModel.CurrentPosition2 = absolutePos2;
                
                // Seek a las posiciones absolutas
                _activePlayer1?.SeekTo(absolutePos1);
                _activePlayer2?.SeekTo(absolutePos2);
            }
        });
    }

    private void UpdateActiveMediaElements()
    {
        if (_viewModel.IsHorizontalOrientation)
        {
            _activePlayer1 = MediaPlayer1H;
            _activePlayer2 = MediaPlayer2H;
        }
        else
        {
            _activePlayer1 = MediaPlayer1V;
            _activePlayer2 = MediaPlayer2V;
        }
    }

    private void SetupMediaOpenedHandlers()
    {
        if (_activePlayer1 != null)
        {
            _activePlayer1.MediaOpened -= OnMedia1Opened;
            _activePlayer1.MediaOpened += OnMedia1Opened;
            _activePlayer1.PositionChanged -= OnPlayer1PositionChanged;
            _activePlayer1.PositionChanged += OnPlayer1PositionChanged;
        }
        if (_activePlayer2 != null)
        {
            _activePlayer2.MediaOpened -= OnMedia2Opened;
            _activePlayer2.MediaOpened += OnMedia2Opened;
            _activePlayer2.PositionChanged -= OnPlayer2PositionChanged;
            _activePlayer2.PositionChanged += OnPlayer2PositionChanged;
        }
    }

    private void OnMedia1Opened(object? sender, EventArgs e)
    {
        if (sender is PrecisionVideoPlayer player && player.Duration > TimeSpan.Zero)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _viewModel.Duration1 = player.Duration;
                // En modo simultáneo, usar la duración más corta
                if (_viewModel.IsSimultaneousMode)
                    UpdateGlobalDuration();
            });
        }
    }

    private void OnMedia2Opened(object? sender, EventArgs e)
    {
        if (sender is PrecisionVideoPlayer player && player.Duration > TimeSpan.Zero)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _viewModel.Duration2 = player.Duration;
                if (_viewModel.IsSimultaneousMode)
                    UpdateGlobalDuration();
            });
        }
    }

    private void OnPlayer1PositionChanged(object? sender, TimeSpan position)
    {
        if (_isDraggingSlider || _isDraggingSlider1) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isDraggingSlider || _isDraggingSlider1) return;
            
            if (_viewModel.IsSimultaneousMode)
            {
                // En modo sincronizado, la posición global es relativa al sync point
                var relativePosition = position - _viewModel.SyncPoint1;
                if (relativePosition < TimeSpan.Zero)
                    relativePosition = TimeSpan.Zero;
                _viewModel.CurrentPosition = relativePosition;
                
                // Actualizar slider global directamente
                if (_viewModel.Duration.TotalSeconds > 0)
                {
                    ProgressSlider.Value = relativePosition.TotalSeconds / _viewModel.Duration.TotalSeconds;
                }
            }
            else
            {
                _viewModel.CurrentPosition1 = position;
                
                // Actualizar sliders de video 1 directamente
                if (_viewModel.Duration1.TotalSeconds > 0)
                {
                    var progress = position.TotalSeconds / _viewModel.Duration1.TotalSeconds;
                    ProgressSlider1H.Value = progress;
                    ProgressSlider1V.Value = progress;
                }
            }
        });
    }

    private void OnPlayer2PositionChanged(object? sender, TimeSpan position)
    {
        if (_isDraggingSlider || _isDraggingSlider2) return;
        
        if (!_viewModel.IsSimultaneousMode)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isDraggingSlider || _isDraggingSlider2) return;
                
                _viewModel.CurrentPosition2 = position;
                
                // Actualizar sliders de video 2 directamente
                if (_viewModel.Duration2.TotalSeconds > 0)
                {
                    var progress = position.TotalSeconds / _viewModel.Duration2.TotalSeconds;
                    ProgressSlider2H.Value = progress;
                    ProgressSlider2V.Value = progress;
                }
            });
        }
    }

    private void UpdateGlobalDuration()
    {
        // Usar la duración más corta para reproducción sincronizada
        if (_viewModel.Duration1 > TimeSpan.Zero && _viewModel.Duration2 > TimeSpan.Zero)
        {
            _viewModel.Duration = _viewModel.Duration1 < _viewModel.Duration2 
                ? _viewModel.Duration1 
                : _viewModel.Duration2;
        }
        else if (_viewModel.Duration1 > TimeSpan.Zero)
        {
            _viewModel.Duration = _viewModel.Duration1;
        }
        else if (_viewModel.Duration2 > TimeSpan.Zero)
        {
            _viewModel.Duration = _viewModel.Duration2;
        }
    }

    #region Handlers globales (modo simultáneo)

    private void OnPlayRequested(object? sender, EventArgs e)
    {
        PlayBoth();
        SyncPlayers();
    }

    private void OnPauseRequested(object? sender, EventArgs e)
    {
        _activePlayer1?.Pause();
        _activePlayer2?.Pause();
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        StopAllPlayers();
        // Volver al inicio de la sincronización (posición relativa 0 = sync points)
        SeekBothToPosition(0);
    }

    private void OnSeekRequested(object? sender, double seconds)
    {
        SeekBothToPosition(seconds);
    }

    private void OnFrameForwardRequested(object? sender, EventArgs e)
    {
        _activePlayer1?.StepForward();
        _activePlayer2?.StepForward();
    }

    private void OnFrameBackwardRequested(object? sender, EventArgs e)
    {
        _activePlayer1?.StepBackward();
        _activePlayer2?.StepBackward();
    }

    #endregion

    #region Handlers individuales Video 1

    private void OnPlayRequested1(object? sender, EventArgs e)
    {
        _activePlayer1?.Play();
    }

    private void OnPauseRequested1(object? sender, EventArgs e)
    {
        _activePlayer1?.Pause();
    }

    private void OnStopRequested1(object? sender, EventArgs e)
    {
        _activePlayer1?.Stop();
        SeekPlayer1ToPosition(0);
    }

    private void OnSeekRequested1(object? sender, double seconds)
    {
        SeekPlayer1ToPosition(seconds);
    }

    private void OnFrameForwardRequested1(object? sender, EventArgs e)
    {
        _activePlayer1?.StepForward();
    }

    private void OnFrameBackwardRequested1(object? sender, EventArgs e)
    {
        _activePlayer1?.StepBackward();
    }

    #endregion

    #region Handlers individuales Video 2

    private void OnPlayRequested2(object? sender, EventArgs e)
    {
        _activePlayer2?.Play();
    }

    private void OnPauseRequested2(object? sender, EventArgs e)
    {
        _activePlayer2?.Pause();
    }

    private void OnStopRequested2(object? sender, EventArgs e)
    {
        _activePlayer2?.Stop();
        SeekPlayer2ToPosition(0);
    }

    private void OnSeekRequested2(object? sender, double seconds)
    {
        SeekPlayer2ToPosition(seconds);
    }

    private void OnFrameForwardRequested2(object? sender, EventArgs e)
    {
        _activePlayer2?.StepForward();
    }

    private void OnFrameBackwardRequested2(object? sender, EventArgs e)
    {
        _activePlayer2?.StepBackward();
    }

    #endregion

    #region Handlers comunes

    private void OnSpeedChangeRequested(object? sender, double speed)
    {
        SetPlaybackSpeed(speed);
    }

    #endregion

    #region Métodos de reproducción

    private void PlayBoth()
    {
        _activePlayer1?.Play();
        _activePlayer2?.Play();
    }

    private void StopAllPlayers()
    {
        MediaPlayer1H?.Stop();
        MediaPlayer2H?.Stop();
        MediaPlayer1V?.Stop();
        MediaPlayer2V?.Stop();
    }

    private void SeekBothToPosition(double seconds)
    {
        var relativePosition = TimeSpan.FromSeconds(seconds);
        
        // En modo sincronizado, aplicar los sync points como offset
        var absolutePos1 = _viewModel.SyncPoint1 + relativePosition;
        var absolutePos2 = _viewModel.SyncPoint2 + relativePosition;

        _activePlayer1?.SeekTo(absolutePos1);
        _activePlayer2?.SeekTo(absolutePos2);

        _viewModel.CurrentPosition = relativePosition;
    }

    private void SeekPlayer1ToPosition(double seconds)
    {
        var position = TimeSpan.FromSeconds(seconds);
        _activePlayer1?.SeekTo(position);
        _viewModel.CurrentPosition1 = position;
    }

    private void SeekPlayer2ToPosition(double seconds)
    {
        var position = TimeSpan.FromSeconds(seconds);
        _activePlayer2?.SeekTo(position);
        _viewModel.CurrentPosition2 = position;
    }

    private void SetPlaybackSpeed(double speed)
    {
        if (_activePlayer1 != null)
            _activePlayer1.Speed = speed;
        if (_activePlayer2 != null)
            _activePlayer2.Speed = speed;
    }

    private void SyncPlayers()
    {
        // Sincronizar el segundo reproductor con el primero
        if (_activePlayer1 != null && _activePlayer2 != null)
        {
            var position = _activePlayer1.Position;
            _activePlayer2.SeekTo(position);
        }
    }

    #endregion

    #region Slider handlers

    // --- Slider global (modo simultáneo) ---
    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        _isDraggingSlider = true;
        _viewModel.IsDraggingSlider = true;
        _wasPlayingBeforeDrag = _viewModel.IsPlaying;
        if (_wasPlayingBeforeDrag)
        {
            _activePlayer1?.Pause();
            _activePlayer2?.Pause();
            _viewModel.IsPlaying = false;
        }
    }

    private void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDraggingSlider) return;
        
        // Calcular la posición objetivo
        var relativePosition = TimeSpan.FromSeconds(e.NewValue * _viewModel.Duration.TotalSeconds);
        _viewModel.CurrentPosition = relativePosition;
        
        // Aplicar los sync points como offset para cada reproductor
        var absolutePos1 = _viewModel.SyncPoint1 + relativePosition;
        var absolutePos2 = _viewModel.SyncPoint2 + relativePosition;
        
#if WINDOWS
        // En Windows, hacer seek en tiempo real con throttle
        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime).TotalMilliseconds >= SeekThrottleMs)
        {
            _lastSeekTime = now;
            _activePlayer1?.SeekTo(absolutePos1);
            _activePlayer2?.SeekTo(absolutePos2);
        }
#else
        // En MacCatalyst/iOS: hacer seek para mostrar el frame actual
        _activePlayer1?.SeekTo(absolutePos1);
        _activePlayer2?.SeekTo(absolutePos2);
#endif
    }

    private void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        // Primero hacer el seek final
        var relativePosition = TimeSpan.FromSeconds(ProgressSlider.Value * _viewModel.Duration.TotalSeconds);
        var absolutePos1 = _viewModel.SyncPoint1 + relativePosition;
        var absolutePos2 = _viewModel.SyncPoint2 + relativePosition;
        _activePlayer1?.SeekTo(absolutePos1);
        _activePlayer2?.SeekTo(absolutePos2);
        
        // Después desactivar los flags
        _isDraggingSlider = false;
        _viewModel.IsDraggingSlider = false;
    }

    // --- Slider video 1 (modo individual) ---
    private void OnSlider1DragStarted(object? sender, EventArgs e)
    {
        _isDraggingSlider1 = true;
        _viewModel.IsDraggingSlider1 = true;
        _wasPlaying1BeforeDrag = _viewModel.IsPlaying1;
        if (_wasPlaying1BeforeDrag)
        {
            _activePlayer1?.Pause();
            _viewModel.IsPlaying1 = false;
        }
    }

    private void OnSlider1ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDraggingSlider1) return;
        
        var position = TimeSpan.FromSeconds(e.NewValue * _viewModel.Duration1.TotalSeconds);
        _viewModel.CurrentPosition1 = position;
#if WINDOWS
        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime1).TotalMilliseconds >= SeekThrottleMs)
        {
            _lastSeekTime1 = now;
            _activePlayer1?.SeekTo(position);
        }
#else
        // En MacCatalyst/iOS: hacer seek para mostrar el frame actual
        _activePlayer1?.SeekTo(position);
#endif
    }

    private void OnSlider1DragCompleted(object? sender, EventArgs e)
    {
        // Primero hacer el seek final
        var slider = _viewModel.IsHorizontalOrientation ? ProgressSlider1H : ProgressSlider1V;
        var position = TimeSpan.FromSeconds(slider.Value * _viewModel.Duration1.TotalSeconds);
        _activePlayer1?.SeekTo(position);
        
        // Después desactivar los flags
        _isDraggingSlider1 = false;
        _viewModel.IsDraggingSlider1 = false;
    }

    // --- Slider video 2 (modo individual) ---
    private void OnSlider2DragStarted(object? sender, EventArgs e)
    {
        _isDraggingSlider2 = true;
        _viewModel.IsDraggingSlider2 = true;
        _wasPlaying2BeforeDrag = _viewModel.IsPlaying2;
        if (_wasPlaying2BeforeDrag)
        {
            _activePlayer2?.Pause();
            _viewModel.IsPlaying2 = false;
        }
    }

    private void OnSlider2ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDraggingSlider2) return;
        
        var position = TimeSpan.FromSeconds(e.NewValue * _viewModel.Duration2.TotalSeconds);
        _viewModel.CurrentPosition2 = position;
#if WINDOWS
        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime2).TotalMilliseconds >= SeekThrottleMs)
        {
            _lastSeekTime2 = now;
            _activePlayer2?.SeekTo(position);
        }
#else
        // En MacCatalyst/iOS: hacer seek para mostrar el frame actual
        _activePlayer2?.SeekTo(position);
#endif
    }

    private void OnSlider2DragCompleted(object? sender, EventArgs e)
    {
        // Primero hacer el seek final
        var slider = _viewModel.IsHorizontalOrientation ? ProgressSlider2H : ProgressSlider2V;
        var position = TimeSpan.FromSeconds(slider.Value * _viewModel.Duration2.TotalSeconds);
        _activePlayer2?.SeekTo(position);
        
        // Después desactivar los flags
        _isDraggingSlider2 = false;
        _viewModel.IsDraggingSlider2 = false;
    }

    #endregion

    #region Scrubbing con trackpad/mouse wheel

    private void OnScrubUpdated(object? sender, VideoScrubEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.VideoIndex == 1)
            {
                HandleScrubVideo1(e);
            }
            else if (e.VideoIndex == 2)
            {
                HandleScrubVideo2(e);
            }
        });
    }

    private void HandleScrubVideo1(VideoScrubEventArgs e)
    {
        if (e.IsStart)
        {
            // Inicio del scrubbing: pausar si está reproduciendo
            _isScrubbing1 = true;
            _wasPlaying1BeforeScrub = _viewModel.IsSimultaneousMode ? _viewModel.IsPlaying : _viewModel.IsPlaying1;
            _currentScrubPosition1 = _viewModel.CurrentPosition1.TotalMilliseconds;
            
            if (_wasPlaying1BeforeScrub)
            {
                _activePlayer1?.Pause();
                if (_viewModel.IsSimultaneousMode)
                {
                    _activePlayer2?.Pause();
                    _viewModel.IsPlaying = false;
                }
                else
                {
                    _viewModel.IsPlaying1 = false;
                }
            }
        }
        else
        {
            // Durante el scrubbing: actualizar posición
            _currentScrubPosition1 += e.DeltaMilliseconds;
            
            // Limitar a los bordes del video
            var maxMs = _viewModel.Duration1.TotalMilliseconds;
            _currentScrubPosition1 = Math.Max(0, Math.Min(_currentScrubPosition1, maxMs));
            
            var newPosition = TimeSpan.FromMilliseconds(_currentScrubPosition1);
            _activePlayer1?.SeekTo(newPosition);
            _viewModel.CurrentPosition1 = newPosition;
            
            // En modo simultáneo, mover también el video 2
            if (_viewModel.IsSimultaneousMode)
            {
                _activePlayer2?.SeekTo(newPosition);
                _viewModel.CurrentPosition2 = newPosition;
                _viewModel.CurrentPosition = newPosition;
            }
        }
    }

    private void HandleScrubVideo2(VideoScrubEventArgs e)
    {
        if (e.IsStart)
        {
            // Inicio del scrubbing: pausar si está reproduciendo
            _isScrubbing2 = true;
            _wasPlaying2BeforeScrub = _viewModel.IsSimultaneousMode ? _viewModel.IsPlaying : _viewModel.IsPlaying2;
            _currentScrubPosition2 = _viewModel.CurrentPosition2.TotalMilliseconds;
            
            if (_wasPlaying2BeforeScrub)
            {
                _activePlayer2?.Pause();
                if (_viewModel.IsSimultaneousMode)
                {
                    _activePlayer1?.Pause();
                    _viewModel.IsPlaying = false;
                }
                else
                {
                    _viewModel.IsPlaying2 = false;
                }
            }
        }
        else
        {
            // Durante el scrubbing: actualizar posición
            _currentScrubPosition2 += e.DeltaMilliseconds;
            
            // Limitar a los bordes del video
            var maxMs = _viewModel.Duration2.TotalMilliseconds;
            _currentScrubPosition2 = Math.Max(0, Math.Min(_currentScrubPosition2, maxMs));
            
            var newPosition = TimeSpan.FromMilliseconds(_currentScrubPosition2);
            _activePlayer2?.SeekTo(newPosition);
            _viewModel.CurrentPosition2 = newPosition;
            
            // En modo simultáneo, mover también el video 1
            if (_viewModel.IsSimultaneousMode)
            {
                _activePlayer1?.SeekTo(newPosition);
                _viewModel.CurrentPosition1 = newPosition;
                _viewModel.CurrentPosition = newPosition;
            }
        }
    }

    private void OnScrubEnded(object? sender, VideoScrubEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.VideoIndex == 1)
            {
                _isScrubbing1 = false;
                
                // Reanudar reproducción si estaba reproduciendo antes
                if (_wasPlaying1BeforeScrub)
                {
                    if (_viewModel.IsSimultaneousMode)
                    {
                        _activePlayer1?.Play();
                        _activePlayer2?.Play();
                        _viewModel.IsPlaying = true;
                    }
                    else
                    {
                        _activePlayer1?.Play();
                        _viewModel.IsPlaying1 = true;
                    }
                }
            }
            else if (e.VideoIndex == 2)
            {
                _isScrubbing2 = false;
                
                // Reanudar reproducción si estaba reproduciendo antes
                if (_wasPlaying2BeforeScrub)
                {
                    if (_viewModel.IsSimultaneousMode)
                    {
                        _activePlayer1?.Play();
                        _activePlayer2?.Play();
                        _viewModel.IsPlaying = true;
                    }
                    else
                    {
                        _activePlayer2?.Play();
                        _viewModel.IsPlaying2 = true;
                    }
                }
            }
        });
    }

    #endregion
}
