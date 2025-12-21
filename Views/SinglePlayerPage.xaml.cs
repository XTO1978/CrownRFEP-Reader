using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class SinglePlayerPage : ContentPage
{
    private readonly SinglePlayerViewModel _viewModel;
    
    // Flags para controlar el scrubbing del slider
    private bool _isDraggingSlider;
    private bool _wasPlayingBeforeDrag;
    
    // Flags para controlar el scrubbing con trackpad/mouse
    private bool _isScrubbing;
    private bool _wasPlayingBeforeScrub;
    private double _currentScrubPosition;

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
        _viewModel.VideoChanged += OnVideoChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SetupMediaHandlers();
        
        // Suscribirse a eventos de scrubbing (trackpad/mouse wheel)
        VideoScrubBehavior.ScrubUpdated += OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded += OnScrubEnded;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Desuscribirse de eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated -= OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded -= OnScrubEnded;
        
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
        _viewModel.VideoChanged -= OnVideoChanged;

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

    #region Scrubbing con trackpad/mouse wheel

    private void OnScrubUpdated(object? sender, VideoScrubEventArgs e)
    {
        // Solo procesar si es nuestro video (index 0)
        if (e.VideoIndex != 0) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (e.IsStart)
            {
                // Inicio del scrubbing: pausar si está reproduciendo
                _isScrubbing = true;
                _wasPlayingBeforeScrub = _viewModel.IsPlaying;
                _currentScrubPosition = _viewModel.CurrentPosition.TotalMilliseconds;
                
                if (_wasPlayingBeforeScrub)
                {
                    MediaPlayer?.Pause();
                    _viewModel.IsPlaying = false;
                }
            }
            else
            {
                // Durante el scrubbing: actualizar posición
                _currentScrubPosition += e.DeltaMilliseconds;
                
                // Limitar a los bordes del video
                var maxMs = _viewModel.Duration.TotalMilliseconds;
                _currentScrubPosition = Math.Max(0, Math.Min(_currentScrubPosition, maxMs));
                
                var newPosition = TimeSpan.FromMilliseconds(_currentScrubPosition);
                MediaPlayer?.SeekTo(newPosition);
                _viewModel.CurrentPosition = newPosition;
            }
        });
    }

    private void OnScrubEnded(object? sender, VideoScrubEventArgs e)
    {
        // Solo procesar si es nuestro video (index 0)
        if (e.VideoIndex != 0) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _isScrubbing = false;
            
            // Reanudar reproducción si estaba reproduciendo antes
            if (_wasPlayingBeforeScrub)
            {
                MediaPlayer?.Play();
                _viewModel.IsPlaying = true;
            }
        });
    }

    #endregion

    #region Navegación de playlist

    private void OnVideoChanged(object? sender, VideoClip newVideo)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Detener reproducción actual
            _viewModel.IsPlaying = false;
            MediaPlayer?.Stop();
            
            // Resetear posición
            _viewModel.CurrentPosition = TimeSpan.Zero;
            _viewModel.Duration = TimeSpan.Zero;
            
            // Cargar nuevo video (el binding de Source ya está actualizado)
            if (!string.IsNullOrEmpty(newVideo.LocalClipPath) && MediaPlayer != null)
            {
                MediaPlayer.Source = newVideo.LocalClipPath;
            }
        });
    }

    #endregion

    #region Dropdown handlers

    private async void OnAthleteDropdownTapped(object? sender, TappedEventArgs e)
    {
        var options = _viewModel.AthleteOptions.ToArray();
        if (options.Length == 0) return;
        
        var result = await DisplayActionSheet(
            "Seleccionar atleta", 
            "Cancelar", 
            null, 
            options.Select(o => o.DisplayName).ToArray());
        
        if (!string.IsNullOrEmpty(result) && result != "Cancelar")
        {
            var selected = options.FirstOrDefault(o => o.DisplayName == result);
            if (selected != null)
                _viewModel.SelectedAthlete = selected;
        }
    }

    private async void OnSectionDropdownTapped(object? sender, TappedEventArgs e)
    {
        var options = _viewModel.SectionOptions.ToArray();
        if (options.Length == 0) return;
        
        var result = await DisplayActionSheet(
            "Seleccionar sección", 
            "Cancelar", 
            null, 
            options.Select(o => o.DisplayName).ToArray());
        
        if (!string.IsNullOrEmpty(result) && result != "Cancelar")
        {
            var selected = options.FirstOrDefault(o => o.DisplayName == result);
            if (selected != null)
                _viewModel.SelectedSection = selected;
        }
    }

    private async void OnCategoryDropdownTapped(object? sender, TappedEventArgs e)
    {
        var options = _viewModel.CategoryOptions.ToArray();
        if (options.Length == 0) return;
        
        var result = await DisplayActionSheet(
            "Seleccionar categoría", 
            "Cancelar", 
            null, 
            options.Select(o => o.DisplayName).ToArray());
        
        if (!string.IsNullOrEmpty(result) && result != "Cancelar")
        {
            var selected = options.FirstOrDefault(o => o.DisplayName == result);
            if (selected != null)
                _viewModel.SelectedCategory = selected;
        }
    }

    #endregion
}
