using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.ViewModels;

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#endif

namespace CrownRFEP_Reader.Views;

public partial class QuadPlayerPage : ContentPage
{
    private readonly QuadPlayerViewModel _viewModel;
    
    // Posiciones actuales para scrubbing
    private double _currentScrubPosition1;
    private double _currentScrubPosition2;
    private double _currentScrubPosition3;
    private double _currentScrubPosition4;
    
    // Flags para detectar arrastre de sliders
    private bool _isDragging1;
    private bool _isDragging2;
    private bool _isDragging3;
    private bool _isDragging4;

    public QuadPlayerPage(QuadPlayerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Suscribirse a eventos globales del ViewModel
        _viewModel.PlayRequested += OnPlayRequested;
        _viewModel.PauseRequested += OnPauseRequested;
        _viewModel.StopRequested += OnStopRequested;
        _viewModel.SeekRequested += OnSeekRequested;
        _viewModel.FrameForwardRequested += OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested += OnFrameBackwardRequested;
        _viewModel.SpeedChanged += OnSpeedChanged;
        _viewModel.SyncRequested += OnSyncRequested;

        // Suscribirse a eventos individuales
        _viewModel.PlayRequested1 += OnPlayRequested1;
        _viewModel.PauseRequested1 += OnPauseRequested1;
        _viewModel.SeekRequested1 += OnSeekRequested1;
        _viewModel.PlayRequested2 += OnPlayRequested2;
        _viewModel.PauseRequested2 += OnPauseRequested2;
        _viewModel.SeekRequested2 += OnSeekRequested2;
        _viewModel.PlayRequested3 += OnPlayRequested3;
        _viewModel.PauseRequested3 += OnPauseRequested3;
        _viewModel.SeekRequested3 += OnSeekRequested3;
        _viewModel.PlayRequested4 += OnPlayRequested4;
        _viewModel.PauseRequested4 += OnPauseRequested4;
        _viewModel.SeekRequested4 += OnSeekRequested4;

        // Configurar slider
        ProgressSlider.DragStarted += OnSliderDragStarted;
        ProgressSlider.DragCompleted += OnSliderDragCompleted;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SetupMediaOpenedHandlers();
        
        // Suscribirse a eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated += OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded += OnScrubEnded;

#if MACCATALYST
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

#if MACCATALYST
        KeyPressHandler.SpaceBarPressed -= OnSpaceBarPressed;
        KeyPressHandler.ArrowLeftPressed -= OnArrowLeftPressed;
        KeyPressHandler.ArrowRightPressed -= OnArrowRightPressed;
#endif
        
        CleanupResources();
    }

#if MACCATALYST
    private void OnSpaceBarPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => _viewModel.PlayPauseCommand.Execute(null));
    }

    private void OnArrowLeftPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => _viewModel.FrameBackwardCommand.Execute(null));
    }

    private void OnArrowRightPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => _viewModel.FrameForwardCommand.Execute(null));
    }
#endif

    private void CleanupResources()
    {
        // Desuscribirse de eventos globales del ViewModel
        _viewModel.PlayRequested -= OnPlayRequested;
        _viewModel.PauseRequested -= OnPauseRequested;
        _viewModel.StopRequested -= OnStopRequested;
        _viewModel.SeekRequested -= OnSeekRequested;
        _viewModel.FrameForwardRequested -= OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested -= OnFrameBackwardRequested;
        _viewModel.SpeedChanged -= OnSpeedChanged;
        _viewModel.SyncRequested -= OnSyncRequested;

        // Desuscribirse de eventos individuales
        _viewModel.PlayRequested1 -= OnPlayRequested1;
        _viewModel.PauseRequested1 -= OnPauseRequested1;
        _viewModel.SeekRequested1 -= OnSeekRequested1;
        _viewModel.PlayRequested2 -= OnPlayRequested2;
        _viewModel.PauseRequested2 -= OnPauseRequested2;
        _viewModel.SeekRequested2 -= OnSeekRequested2;
        _viewModel.PlayRequested3 -= OnPlayRequested3;
        _viewModel.PauseRequested3 -= OnPauseRequested3;
        _viewModel.SeekRequested3 -= OnSeekRequested3;
        _viewModel.PlayRequested4 -= OnPlayRequested4;
        _viewModel.PauseRequested4 -= OnPauseRequested4;
        _viewModel.SeekRequested4 -= OnSeekRequested4;

        // Limpiar handlers de MediaOpened
        MediaPlayer1.MediaOpened -= OnMediaOpened;
        MediaPlayer2.MediaOpened -= OnMediaOpened;
        MediaPlayer3.MediaOpened -= OnMediaOpened;
        MediaPlayer4.MediaOpened -= OnMediaOpened;

        // Detener todos los reproductores
        StopAllPlayers();
    }

    private void SetupMediaOpenedHandlers()
    {
        MediaPlayer1.MediaOpened += OnMediaOpened;
        MediaPlayer2.MediaOpened += OnMediaOpened;
        MediaPlayer3.MediaOpened += OnMediaOpened;
        MediaPlayer4.MediaOpened += OnMediaOpened;
        
        // Configurar timer para actualizar posición
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += OnTimerTick;
        timer.Start();
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Actualizar duraciones individuales
            if (sender == MediaPlayer1 && MediaPlayer1.Duration > TimeSpan.Zero)
                _viewModel.Duration1 = MediaPlayer1.Duration;
            else if (sender == MediaPlayer2 && MediaPlayer2.Duration > TimeSpan.Zero)
                _viewModel.Duration2 = MediaPlayer2.Duration;
            else if (sender == MediaPlayer3 && MediaPlayer3.Duration > TimeSpan.Zero)
                _viewModel.Duration3 = MediaPlayer3.Duration;
            else if (sender == MediaPlayer4 && MediaPlayer4.Duration > TimeSpan.Zero)
                _viewModel.Duration4 = MediaPlayer4.Duration;

            // Usar la duración más larga entre todos los videos para el modo global
            var maxDuration = TimeSpan.Zero;
            if (MediaPlayer1.Duration > maxDuration) maxDuration = MediaPlayer1.Duration;
            if (MediaPlayer2.Duration > maxDuration) maxDuration = MediaPlayer2.Duration;
            if (MediaPlayer3.Duration > maxDuration) maxDuration = MediaPlayer3.Duration;
            if (MediaPlayer4.Duration > maxDuration) maxDuration = MediaPlayer4.Duration;
            
            _viewModel.UpdateDurationFromPage(maxDuration);
        });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Actualizar posiciones individuales siempre (excepto cuando se está arrastrando el slider)
            if (_viewModel.HasVideo1 && !_isDragging1)
                _viewModel.CurrentPosition1 = MediaPlayer1.Position;
            if (_viewModel.HasVideo2 && !_isDragging2)
                _viewModel.CurrentPosition2 = MediaPlayer2.Position;
            if (_viewModel.HasVideo3 && !_isDragging3)
                _viewModel.CurrentPosition3 = MediaPlayer3.Position;
            if (_viewModel.HasVideo4 && !_isDragging4)
                _viewModel.CurrentPosition4 = MediaPlayer4.Position;

            // Actualizar posición global solo si está en modo simultáneo y reproduciendo
            if (!_viewModel.IsPlaying) return;
            
            // Usar la posición del primer video disponible
            var position = TimeSpan.Zero;
            if (_viewModel.HasVideo1 && MediaPlayer1.Position > TimeSpan.Zero)
                position = MediaPlayer1.Position;
            else if (_viewModel.HasVideo2 && MediaPlayer2.Position > TimeSpan.Zero)
                position = MediaPlayer2.Position;
            else if (_viewModel.HasVideo3 && MediaPlayer3.Position > TimeSpan.Zero)
                position = MediaPlayer3.Position;
            else if (_viewModel.HasVideo4 && MediaPlayer4.Position > TimeSpan.Zero)
                position = MediaPlayer4.Position;
            
            _viewModel.UpdatePositionFromPage(position);
        });
    }

    #region Slider Handling

    private bool _wasPlayingBeforeDrag;

    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        _wasPlayingBeforeDrag = _viewModel.IsPlaying;
        PauseAllPlayers();
    }

    private void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        var position = ProgressSlider.Value * _viewModel.Duration.TotalSeconds;
        SeekAllToPosition(position);
        
        if (_wasPlayingBeforeDrag)
            PlayAllPlayers();
    }

    #endregion

    #region Scrub Handling

    private void OnScrubUpdated(object? sender, VideoScrubEventArgs e)
    {
        // En modo simultáneo, mover todos los reproductores
        if (_viewModel.IsSimultaneousMode)
        {
            OnScrubUpdatedSimultaneous(e);
            return;
        }
        
        // Modo individual: mover solo el reproductor correspondiente
        var player = GetPlayerForIndex(e.VideoIndex);
        if (player == null) return;

        if (e.IsStart)
        {
            // Guardar posición actual
            switch (e.VideoIndex)
            {
                case 1: _currentScrubPosition1 = player.Position.TotalMilliseconds; break;
                case 2: _currentScrubPosition2 = player.Position.TotalMilliseconds; break;
                case 3: _currentScrubPosition3 = player.Position.TotalMilliseconds; break;
                case 4: _currentScrubPosition4 = player.Position.TotalMilliseconds; break;
            }
            player.Pause();
        }
        else
        {
            // Aplicar delta
            ref double currentPos = ref _currentScrubPosition1;
            if (e.VideoIndex == 2) currentPos = ref _currentScrubPosition2;
            else if (e.VideoIndex == 3) currentPos = ref _currentScrubPosition3;
            else if (e.VideoIndex == 4) currentPos = ref _currentScrubPosition4;

            currentPos += e.DeltaMilliseconds;

            if (player.Duration != TimeSpan.Zero)
            {
                currentPos = Math.Max(0, Math.Min(currentPos, player.Duration.TotalMilliseconds));
                player.SeekTo(TimeSpan.FromMilliseconds(currentPos));
            }
        }
    }

    private void OnScrubUpdatedSimultaneous(VideoScrubEventArgs e)
    {
        if (e.IsStart)
        {
            // Guardar posiciones actuales de todos
            if (_viewModel.HasVideo1) _currentScrubPosition1 = MediaPlayer1.Position.TotalMilliseconds;
            if (_viewModel.HasVideo2) _currentScrubPosition2 = MediaPlayer2.Position.TotalMilliseconds;
            if (_viewModel.HasVideo3) _currentScrubPosition3 = MediaPlayer3.Position.TotalMilliseconds;
            if (_viewModel.HasVideo4) _currentScrubPosition4 = MediaPlayer4.Position.TotalMilliseconds;
            
            // Pausar todos
            PauseAllPlayers();
            _viewModel.SetPlayingState(false);
        }
        else
        {
            // Aplicar delta a todos los reproductores
            if (_viewModel.HasVideo1 && MediaPlayer1.Duration != TimeSpan.Zero)
            {
                _currentScrubPosition1 += e.DeltaMilliseconds;
                _currentScrubPosition1 = Math.Max(0, Math.Min(_currentScrubPosition1, MediaPlayer1.Duration.TotalMilliseconds));
                MediaPlayer1.SeekTo(TimeSpan.FromMilliseconds(_currentScrubPosition1));
            }
            if (_viewModel.HasVideo2 && MediaPlayer2.Duration != TimeSpan.Zero)
            {
                _currentScrubPosition2 += e.DeltaMilliseconds;
                _currentScrubPosition2 = Math.Max(0, Math.Min(_currentScrubPosition2, MediaPlayer2.Duration.TotalMilliseconds));
                MediaPlayer2.SeekTo(TimeSpan.FromMilliseconds(_currentScrubPosition2));
            }
            if (_viewModel.HasVideo3 && MediaPlayer3.Duration != TimeSpan.Zero)
            {
                _currentScrubPosition3 += e.DeltaMilliseconds;
                _currentScrubPosition3 = Math.Max(0, Math.Min(_currentScrubPosition3, MediaPlayer3.Duration.TotalMilliseconds));
                MediaPlayer3.SeekTo(TimeSpan.FromMilliseconds(_currentScrubPosition3));
            }
            if (_viewModel.HasVideo4 && MediaPlayer4.Duration != TimeSpan.Zero)
            {
                _currentScrubPosition4 += e.DeltaMilliseconds;
                _currentScrubPosition4 = Math.Max(0, Math.Min(_currentScrubPosition4, MediaPlayer4.Duration.TotalMilliseconds));
                MediaPlayer4.SeekTo(TimeSpan.FromMilliseconds(_currentScrubPosition4));
            }
            
            // Actualizar posición global en el ViewModel
            _viewModel.UpdatePositionFromPage(TimeSpan.FromMilliseconds(_currentScrubPosition1));
        }
    }

    private void OnScrubEnded(object? sender, VideoScrubEventArgs e)
    {
        // En modo simultáneo, pausar todos
        if (_viewModel.IsSimultaneousMode)
        {
            PauseAllPlayers();
            _viewModel.SetPlayingState(false);
            return;
        }
        
        // Modo individual: pausar solo el correspondiente
        var player = GetPlayerForIndex(e.VideoIndex);
        player?.Pause();
    }

    private PrecisionVideoPlayer? GetPlayerForIndex(int index)
    {
        return index switch
        {
            1 => MediaPlayer1,
            2 => MediaPlayer2,
            3 => MediaPlayer3,
            4 => MediaPlayer4,
            _ => null
        };
    }

    #endregion

    #region ViewModel Event Handlers

    private void OnPlayRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(PlayAllPlayers);
    }

    private void OnPauseRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(PauseAllPlayers);
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StopAllPlayers();
            SeekAllToPosition(0);
        });
    }

    private void OnSeekRequested(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => SeekAllToPosition(position));
    }

    private void OnFrameForwardRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PauseAllPlayers();
            StepAllForward();
        });
    }

    private void OnFrameBackwardRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PauseAllPlayers();
            StepAllBackward();
        });
    }

    private void OnSpeedChanged(object? sender, double speed)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MediaPlayer1.Speed = speed;
            MediaPlayer2.Speed = speed;
            MediaPlayer3.Speed = speed;
            MediaPlayer4.Speed = speed;
        });
    }

    private void OnSyncRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Sincronizar todos los videos a la posición del primero disponible
            var position = TimeSpan.Zero;
            if (_viewModel.HasVideo1)
                position = MediaPlayer1.Position;
            else if (_viewModel.HasVideo2)
                position = MediaPlayer2.Position;
            else if (_viewModel.HasVideo3)
                position = MediaPlayer3.Position;
            else if (_viewModel.HasVideo4)
                position = MediaPlayer4.Position;

            SeekAllToPosition(position.TotalSeconds);
        });
    }

    #endregion

    #region Player Control Methods

    private void PlayAllPlayers()
    {
        if (_viewModel.HasVideo1) MediaPlayer1.Play();
        if (_viewModel.HasVideo2) MediaPlayer2.Play();
        if (_viewModel.HasVideo3) MediaPlayer3.Play();
        if (_viewModel.HasVideo4) MediaPlayer4.Play();
    }

    private void PauseAllPlayers()
    {
        MediaPlayer1.Pause();
        MediaPlayer2.Pause();
        MediaPlayer3.Pause();
        MediaPlayer4.Pause();
    }

    private void StopAllPlayers()
    {
        MediaPlayer1.Stop();
        MediaPlayer2.Stop();
        MediaPlayer3.Stop();
        MediaPlayer4.Stop();
    }

    private void SeekAllToPosition(double seconds)
    {
        var position = TimeSpan.FromSeconds(seconds);
        if (_viewModel.HasVideo1) MediaPlayer1.SeekTo(position);
        if (_viewModel.HasVideo2) MediaPlayer2.SeekTo(position);
        if (_viewModel.HasVideo3) MediaPlayer3.SeekTo(position);
        if (_viewModel.HasVideo4) MediaPlayer4.SeekTo(position);
    }

    private void StepAllForward()
    {
        if (_viewModel.HasVideo1) MediaPlayer1.StepForward();
        if (_viewModel.HasVideo2) MediaPlayer2.StepForward();
        if (_viewModel.HasVideo3) MediaPlayer3.StepForward();
        if (_viewModel.HasVideo4) MediaPlayer4.StepForward();
    }

    private void StepAllBackward()
    {
        if (_viewModel.HasVideo1) MediaPlayer1.StepBackward();
        if (_viewModel.HasVideo2) MediaPlayer2.StepBackward();
        if (_viewModel.HasVideo3) MediaPlayer3.StepBackward();
        if (_viewModel.HasVideo4) MediaPlayer4.StepBackward();
    }

    #endregion

    #region Individual Player Event Handlers

    // Video 1
    private void OnPlayRequested1(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer1.Play());
    }

    private void OnPauseRequested1(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer1.Pause());
    }

    private void OnSeekRequested1(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer1.SeekTo(TimeSpan.FromSeconds(position)));
    }

    // Video 2
    private void OnPlayRequested2(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer2.Play());
    }

    private void OnPauseRequested2(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer2.Pause());
    }

    private void OnSeekRequested2(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer2.SeekTo(TimeSpan.FromSeconds(position)));
    }

    // Video 3
    private void OnPlayRequested3(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer3.Play());
    }

    private void OnPauseRequested3(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer3.Pause());
    }

    private void OnSeekRequested3(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer3.SeekTo(TimeSpan.FromSeconds(position)));
    }

    // Video 4
    private void OnPlayRequested4(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer4.Play());
    }

    private void OnPauseRequested4(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer4.Pause());
    }

    private void OnSeekRequested4(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer4.SeekTo(TimeSpan.FromSeconds(position)));
    }

    #endregion

    #region Individual Slider Handlers

    private bool _wasPlaying1BeforeDrag;
    private bool _wasPlaying2BeforeDrag;
    private bool _wasPlaying3BeforeDrag;
    private bool _wasPlaying4BeforeDrag;

    // Slider 1
    private void OnProgressSlider1DragStarted(object? sender, EventArgs e)
    {
        _isDragging1 = true;
        _wasPlaying1BeforeDrag = _viewModel.IsPlaying1;
        MediaPlayer1.Pause();
        _viewModel.IsPlaying1 = false;
    }

    private void OnProgressSlider1DragCompleted(object? sender, EventArgs e)
    {
        _isDragging1 = false;
        var position = ProgressSlider1.Value * _viewModel.Duration1.TotalSeconds;
        MediaPlayer1.SeekTo(TimeSpan.FromSeconds(position));
        
        if (_wasPlaying1BeforeDrag)
        {
            MediaPlayer1.Play();
            _viewModel.IsPlaying1 = true;
        }
    }

    // Slider 2
    private void OnProgressSlider2DragStarted(object? sender, EventArgs e)
    {
        _isDragging2 = true;
        _wasPlaying2BeforeDrag = _viewModel.IsPlaying2;
        MediaPlayer2.Pause();
        _viewModel.IsPlaying2 = false;
    }

    private void OnProgressSlider2DragCompleted(object? sender, EventArgs e)
    {
        _isDragging2 = false;
        var position = ProgressSlider2.Value * _viewModel.Duration2.TotalSeconds;
        MediaPlayer2.SeekTo(TimeSpan.FromSeconds(position));
        
        if (_wasPlaying2BeforeDrag)
        {
            MediaPlayer2.Play();
            _viewModel.IsPlaying2 = true;
        }
    }

    // Slider 3
    private void OnProgressSlider3DragStarted(object? sender, EventArgs e)
    {
        _isDragging3 = true;
        _wasPlaying3BeforeDrag = _viewModel.IsPlaying3;
        MediaPlayer3.Pause();
        _viewModel.IsPlaying3 = false;
    }

    private void OnProgressSlider3DragCompleted(object? sender, EventArgs e)
    {
        _isDragging3 = false;
        var position = ProgressSlider3.Value * _viewModel.Duration3.TotalSeconds;
        MediaPlayer3.SeekTo(TimeSpan.FromSeconds(position));
        
        if (_wasPlaying3BeforeDrag)
        {
            MediaPlayer3.Play();
            _viewModel.IsPlaying3 = true;
        }
    }

    // Slider 4
    private void OnProgressSlider4DragStarted(object? sender, EventArgs e)
    {
        _isDragging4 = true;
        _wasPlaying4BeforeDrag = _viewModel.IsPlaying4;
        MediaPlayer4.Pause();
        _viewModel.IsPlaying4 = false;
    }

    private void OnProgressSlider4DragCompleted(object? sender, EventArgs e)
    {
        _isDragging4 = false;
        var position = ProgressSlider4.Value * _viewModel.Duration4.TotalSeconds;
        MediaPlayer4.SeekTo(TimeSpan.FromSeconds(position));
        
        if (_wasPlaying4BeforeDrag)
        {
            MediaPlayer4.Play();
            _viewModel.IsPlaying4 = true;
        }
    }

    // ValueChanged handlers - seek mientras se arrastra
    private void OnProgressSlider1ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDragging1) return;
        var position = e.NewValue * _viewModel.Duration1.TotalSeconds;
        MediaPlayer1.SeekTo(TimeSpan.FromSeconds(position));
        _viewModel.CurrentPosition1 = TimeSpan.FromSeconds(position);
    }

    private void OnProgressSlider2ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDragging2) return;
        var position = e.NewValue * _viewModel.Duration2.TotalSeconds;
        MediaPlayer2.SeekTo(TimeSpan.FromSeconds(position));
        _viewModel.CurrentPosition2 = TimeSpan.FromSeconds(position);
    }

    private void OnProgressSlider3ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDragging3) return;
        var position = e.NewValue * _viewModel.Duration3.TotalSeconds;
        MediaPlayer3.SeekTo(TimeSpan.FromSeconds(position));
        _viewModel.CurrentPosition3 = TimeSpan.FromSeconds(position);
    }

    private void OnProgressSlider4ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDragging4) return;
        var position = e.NewValue * _viewModel.Duration4.TotalSeconds;
        MediaPlayer4.SeekTo(TimeSpan.FromSeconds(position));
        _viewModel.CurrentPosition4 = TimeSpan.FromSeconds(position);
    }

    #endregion
}
