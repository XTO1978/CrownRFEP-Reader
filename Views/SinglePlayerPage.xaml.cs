using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class SinglePlayerPage : ContentPage
{
    private readonly SinglePlayerViewModel _viewModel;
    
    // Flags para controlar el scrubbing del slider
    private bool _isDraggingSlider;
    private bool _wasPlayingBeforeDrag;

    public SinglePlayerPage(SinglePlayerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Suscribirse a eventos del ViewModel
        _viewModel.PlayRequested += OnPlayRequested;
        _viewModel.PauseRequested += OnPauseRequested;
        _viewModel.StopRequested += OnStopRequested;
        _viewModel.SeekRequested += OnSeekRequested;
        _viewModel.FrameForwardRequested += OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested += OnFrameBackwardRequested;
        _viewModel.SpeedChangeRequested += OnSpeedChangeRequested;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SetupMediaHandlers();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CleanupResources();
    }

    private void SetupMediaHandlers()
    {
        if (MediaPlayer != null)
        {
            MediaPlayer.MediaOpened -= OnMediaOpened;
            MediaPlayer.MediaOpened += OnMediaOpened;
            MediaPlayer.PositionChanged -= OnPositionChanged;
            MediaPlayer.PositionChanged += OnPositionChanged;
            MediaPlayer.MediaEnded -= OnMediaEnded;
            MediaPlayer.MediaEnded += OnMediaEnded;
        }
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
        _viewModel.SpeedChangeRequested -= OnSpeedChangeRequested;

        // Desuscribirse de eventos del reproductor
        if (MediaPlayer != null)
        {
            MediaPlayer.MediaOpened -= OnMediaOpened;
            MediaPlayer.PositionChanged -= OnPositionChanged;
            MediaPlayer.MediaEnded -= OnMediaEnded;
            MediaPlayer.Stop();
            MediaPlayer.Handler?.DisconnectHandler();
        }
    }

    #region Eventos del reproductor

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        if (sender is PrecisionVideoPlayer player && player.Duration > TimeSpan.Zero)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _viewModel.Duration = player.Duration;
            });
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (_isDraggingSlider) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.CurrentPosition = position;
        });
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.IsPlaying = false;
        });
    }

    #endregion

    #region Handlers del ViewModel

    private void OnPlayRequested(object? sender, EventArgs e)
    {
        MediaPlayer?.Play();
    }

    private void OnPauseRequested(object? sender, EventArgs e)
    {
        MediaPlayer?.Pause();
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        MediaPlayer?.Stop();
    }

    private void OnSeekRequested(object? sender, double seconds)
    {
        var position = TimeSpan.FromSeconds(seconds);
        MediaPlayer?.SeekTo(position);
        _viewModel.CurrentPosition = position;
    }

    private void OnFrameForwardRequested(object? sender, EventArgs e)
    {
        MediaPlayer?.StepForward();
    }

    private void OnFrameBackwardRequested(object? sender, EventArgs e)
    {
        MediaPlayer?.StepBackward();
    }

    private void OnSpeedChangeRequested(object? sender, double speed)
    {
        if (MediaPlayer != null)
        {
            MediaPlayer.Speed = speed;
        }
    }

    #endregion

    #region Slider handlers

    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        _isDraggingSlider = true;
        _wasPlayingBeforeDrag = _viewModel.IsPlaying;
        if (_wasPlayingBeforeDrag)
        {
            MediaPlayer?.Pause();
            _viewModel.IsPlaying = false;
        }
    }

    private void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDraggingSlider) return;

        // Seek en tiempo real mientras se arrastra
        var position = TimeSpan.FromSeconds(e.NewValue * _viewModel.Duration.TotalSeconds);
        MediaPlayer?.SeekTo(position);
    }

    private void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        _isDraggingSlider = false;
        _viewModel.SeekToPosition(_viewModel.Progress);
        
        // Mantener el reproductor en pausa mostrando el frame seleccionado
        // El usuario debe pulsar play manualmente para reanudar
    }

    #endregion
}
