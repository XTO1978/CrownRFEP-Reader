using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Services;

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#endif

namespace CrownRFEP_Reader.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    
    // Posiciones actuales de cada video para scrubbing incremental
    private double _currentPosition0;
    private double _currentPosition1;
    private double _currentPosition2;
    
    // Indica si esta página está activa
    private bool _isPageActive;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        AppLog.Info("DashboardPage", "CTOR");

        // Suscribirse a eventos de hover para video preview
        HoverVideoPreviewBehavior.VideoHoverStarted += OnVideoHoverStarted;
        HoverVideoPreviewBehavior.VideoHoverEnded += OnVideoHoverEnded;
        
        // Suscribirse a eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated += OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded += OnScrubEnded;
        
#if MACCATALYST
        // Suscribirse a eventos de teclado
        KeyPressHandler.SpaceBarPressed += OnSpaceBarPressed;
#endif
    }

    ~DashboardPage()
    {
        HoverVideoPreviewBehavior.VideoHoverStarted -= OnVideoHoverStarted;
        HoverVideoPreviewBehavior.VideoHoverEnded -= OnVideoHoverEnded;
        VideoScrubBehavior.ScrubUpdated -= OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded -= OnScrubEnded;
        
#if MACCATALYST
        KeyPressHandler.SpaceBarPressed -= OnSpaceBarPressed;
#endif
    }

#if MACCATALYST
    private void OnSpaceBarPressed(object? sender, EventArgs e)
    {
        // Solo responder si la página está activa
        if (!_isPageActive) return;
        MainThread.BeginInvokeOnMainThread(TogglePlayPause);
    }
#endif

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;

        AppLog.Info(
            "DashboardPage",
            $"OnAppearing | IsVideoLessonsSelected={_viewModel.IsVideoLessonsSelected} | NavStack={Shell.Current?.Navigation?.NavigationStack?.Count} | ModalStack={Shell.Current?.Navigation?.ModalStack?.Count}");

        _ = LogHeartbeatsAsync();
        
        // Limpiar los recuadros de preview al volver a la página
        _viewModel.ClearPreviewVideos();
        
        await _viewModel.LoadDataAsync();

        AppLog.Info("DashboardPage", "OnAppearing finished LoadDataAsync");
    }

    private async Task LogHeartbeatsAsync()
    {
        try
        {
            await Task.Delay(250);
            AppLog.Info("DashboardPage", "Heartbeat +250ms");
            await Task.Delay(750);
            AppLog.Info("DashboardPage", "Heartbeat +1s");
            await Task.Delay(2000);
            AppLog.Info("DashboardPage", "Heartbeat +3s");
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "Heartbeat task error", ex);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isPageActive = false;

        AppLog.Info(
            "DashboardPage",
            $"OnDisappearing | NavStack={Shell.Current?.Navigation?.NavigationStack?.Count} | ModalStack={Shell.Current?.Navigation?.ModalStack?.Count}");
        // Pausar todos los videos al salir
        PauseAllVideos();
    }

    private void OnVideoHoverStarted(object? sender, HoverVideoEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.HoverVideo = e.Video;
        });
    }

    private void OnVideoHoverEnded(object? sender, HoverVideoEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.HoverVideo = null;
        });
    }

    private void OnDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is GestureRecognizer gestureRecognizer && 
            gestureRecognizer.Parent is View view && 
            view.BindingContext is VideoClip video)
        {
            e.Data.Properties["VideoClip"] = video;
        }
    }

    private void OnDropScreen1(object? sender, DropEventArgs e)
    {
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            _viewModel.ParallelVideo1 = video;
        }
    }

    private void OnDropScreen2(object? sender, DropEventArgs e)
    {
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            _viewModel.ParallelVideo2 = video;
        }
    }

    private void OnScrubUpdated(object? sender, VideoScrubEventArgs e)
    {
        if (!_isPageActive) return;
        
        var player = GetPlayerForIndex(e.VideoIndex);
        if (player == null) return;
        
        if (e.IsStart)
        {
            // Guardar posición actual y pausar
            switch (e.VideoIndex)
            {
                case 0: _currentPosition0 = player.Position.TotalMilliseconds; break;
                case 1: _currentPosition1 = player.Position.TotalMilliseconds; break;
                case 2: _currentPosition2 = player.Position.TotalMilliseconds; break;
            }
            player.Pause();
        }
        else
        {
            // Aplicar delta incremental
            ref double currentPos = ref _currentPosition0;
            if (e.VideoIndex == 1) currentPos = ref _currentPosition1;
            else if (e.VideoIndex == 2) currentPos = ref _currentPosition2;
            
            currentPos += e.DeltaMilliseconds;
            
            // Clampear dentro del rango válido
            if (player.Duration != TimeSpan.Zero)
            {
                currentPos = Math.Max(0, Math.Min(currentPos, player.Duration.TotalMilliseconds));
            }
            
            // Solo hacer seek si el video está cargado
            if (player.Duration != TimeSpan.Zero)
            {
                player.SeekTo(TimeSpan.FromMilliseconds(currentPos));
            }
        }
    }

    private void OnScrubEnded(object? sender, VideoScrubEventArgs e)
    {
        if (!_isPageActive) return;
        
        var player = GetPlayerForIndex(e.VideoIndex);
        player?.Pause();
    }

    private PrecisionVideoPlayer? GetPlayerForIndex(int index)
    {
        return index switch
        {
            0 => PreviewPlayerSingle,
            1 => _viewModel.IsHorizontalOrientation ? PreviewPlayer1H : PreviewPlayer1V,
            2 => _viewModel.IsHorizontalOrientation ? PreviewPlayer2H : PreviewPlayer2V,
            _ => null
        };
    }

    // Toggle Play/Pause para todos los videos activos
    public void TogglePlayPause()
    {
        if (_viewModel.IsSingleVideoMode)
        {
            TogglePlayer(PreviewPlayerSingle);
        }
        else
        {
            // Modo paralelo: controlar ambos videos
            if (_viewModel.IsHorizontalOrientation)
            {
                TogglePlayer(PreviewPlayer1H);
                TogglePlayer(PreviewPlayer2H);
            }
            else
            {
                TogglePlayer(PreviewPlayer1V);
                TogglePlayer(PreviewPlayer2V);
            }
        }
    }

    private void TogglePlayer(PrecisionVideoPlayer? player)
    {
        if (player == null) return;

        if (player.IsPlaying)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }
    }

    private void PauseAllVideos()
    {
        PreviewPlayerSingle?.Pause();
        PreviewPlayer1H?.Pause();
        PreviewPlayer2H?.Pause();
        PreviewPlayer1V?.Pause();
        PreviewPlayer2V?.Pause();
    }
}
