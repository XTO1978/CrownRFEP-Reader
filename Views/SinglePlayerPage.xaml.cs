using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views.Controls;
using CrownRFEP_Reader.ViewModels;

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#elif WINDOWS
using CrownRFEP_Reader.Platforms.Windows;
#endif

namespace CrownRFEP_Reader.Views;

public partial class SinglePlayerPage : ContentPage
{
    private readonly SinglePlayerViewModel _viewModel;
    private readonly DatabaseService _databaseService;
    private readonly IVideoLessonRecorder _videoLessonRecorder;

    private ReplayKitCameraPreview? VideoLessonCameraPreview;
    private WebcamPreview? VideoLessonWebcamPreview;

    private bool _isDrawingMode;

    private bool _isVideoLessonMode;
    private bool _isVideoLessonRecording;
    private string? _currentVideoLessonPath;
    private bool _isVideoLessonStartStopInProgress;

    private bool _videoLessonCameraEnabled = true;
    private bool _videoLessonMicEnabled = true;

    // Timer para el cronómetro de grabación
    private System.Timers.Timer? _recordingTimer;
    private DateTime _recordingStartTime;

    private bool _isPageActive;
    private bool _isTextPromptOpen;

    private bool _pipHasUserMoved;
    private double _pipDragStartTranslationX;
    private double _pipDragStartTranslationY;
    
    // Flags para controlar el scrubbing del slider
    private bool _isDraggingSlider;
    private bool _wasPlayingBeforeDrag;

#if WINDOWS
    // Throttle para seeks en Windows (evitar bloqueos)
    private DateTime _lastSeekTime = DateTime.MinValue;
    private const int SeekThrottleMs = 50;
#endif
    
    // Flags para controlar el scrubbing con trackpad/mouse
    private bool _isScrubbing;
    private bool _wasPlayingBeforeScrub;
    private double _currentScrubPosition;

    private static readonly Color DefaultInkColor = Color.FromArgb("#FFFF7043");
    private const float DefaultInkThickness = 3f;
    private const float DefaultTextSize = 16f;

    public SinglePlayerPage(SinglePlayerViewModel viewModel, DatabaseService databaseService, IVideoLessonRecorder videoLessonRecorder)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _databaseService = databaseService;
        _videoLessonRecorder = videoLessonRecorder;

        AppLog.Info("SinglePlayerPage", "CTOR");

#if WINDOWS
        // En Windows: controles de reproducción como overlay en Row 0, anclados abajo
        if (PlayerControlsBorder != null)
        {
            Grid.SetRow(PlayerControlsBorder, 0);
            PlayerControlsBorder.VerticalOptions = LayoutOptions.End;
        }
#else
        // En otras plataformas: controles en Row 1 separados del video
        if (PlayerControlsBorder != null)
        {
            Grid.SetRow(PlayerControlsBorder, 1);
            PlayerControlsBorder.VerticalOptions = LayoutOptions.Fill;
        }
#endif

        if (RootGrid != null)
            RootGrid.SizeChanged += OnRootGridSizeChanged;

        // Suscribirse a eventos del ViewModel
        _viewModel.PlayRequested += OnPlayRequested;
        _viewModel.PauseRequested += OnPauseRequested;
        _viewModel.StopRequested += OnStopRequested;
        _viewModel.SeekRequested += OnSeekRequested;
        _viewModel.FrameForwardRequested += OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested += OnFrameBackwardRequested;
        _viewModel.SpeedChangeRequested += OnSpeedChangeRequested;
        _viewModel.VideoChanged += OnVideoChanged;

        if (AnalysisCanvas != null)
            AnalysisCanvas.TextRequested += OnAnalysisCanvasTextRequested;

        InitializeVideoLessonCameraPreview();

        SyncVideoLessonUiFromRecorder();
    }

    private void InitializeVideoLessonCameraPreview()
    {
        if (VideoLessonCameraHost == null)
            return;

        // Evita instanciar el control incorrecto en plataformas donde no hay handler.
        // Importante: no referenciar/crear WebcamPreview desde XAML en MacCatalyst.
#if MACCATALYST
        VideoLessonCameraPreview = new ReplayKitCameraPreview
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsActive = false
        };

        VideoLessonCameraHost.Content = VideoLessonCameraPreview;
#elif WINDOWS
        VideoLessonWebcamPreview = new WebcamPreview
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsActive = false
        };

        VideoLessonCameraHost.Content = VideoLessonWebcamPreview;
#else
        VideoLessonCameraHost.Content = null;
#endif
    }

    private void SyncVideoLessonUiFromRecorder()
    {
        _isVideoLessonRecording = _videoLessonRecorder.IsRecording;
        _isVideoLessonMode = _isVideoLessonRecording;

        if (VideoLessonCameraPip != null)
            VideoLessonCameraPip.IsVisible = _isVideoLessonRecording && _videoLessonCameraEnabled;

        if (VideoLessonCameraPreview != null)
            VideoLessonCameraPreview.IsActive = _isVideoLessonRecording && _videoLessonCameraEnabled;

        if (VideoLessonWebcamPreview != null)
            VideoLessonWebcamPreview.IsActive = _isVideoLessonRecording && _videoLessonCameraEnabled;

        // VideoLessonOptionsPanel siempre visible

        UpdateVideoLessonToggleVisualState();
        UpdateVideoLessonOptionsVisualState();

        if (_isVideoLessonRecording)
            EnsurePipPositioned();
    }

    private void UpdateVideoLessonToggleVisualState()
    {
        if (VideoLessonRecordButton != null)
            VideoLessonRecordButton.BackgroundColor = _isVideoLessonRecording
                ? Color.FromArgb("#FFFF3B30")
                : Color.FromArgb("#FF2A2A2A");

        if (VideoLessonRecordIcon != null)
            VideoLessonRecordIcon.SymbolName = _isVideoLessonRecording ? "stop.fill" : "record.circle";
    }

    private void UpdateVideoLessonOptionsVisualState()
    {
        if (VideoLessonCameraToggle != null)
            VideoLessonCameraToggle.BackgroundColor = _videoLessonCameraEnabled ? Color.FromArgb("#33FF7043") : Color.FromArgb("#FF2A2A2A");

        if (VideoLessonCameraToggleIcon != null)
            VideoLessonCameraToggleIcon.SymbolName = _videoLessonCameraEnabled ? "video.fill" : "video.slash";

        if (VideoLessonMicToggle != null)
            VideoLessonMicToggle.BackgroundColor = _videoLessonMicEnabled ? Color.FromArgb("#33FF7043") : Color.FromArgb("#FF2A2A2A");

        if (VideoLessonMicToggleIcon != null)
            VideoLessonMicToggleIcon.SymbolName = _videoLessonMicEnabled ? "mic.fill" : "mic.slash";
    }

    private void OnRootGridSizeChanged(object? sender, EventArgs e)
    {
        if (!_isPageActive)
            return;

        if (VideoLessonCameraPip?.IsVisible == true)
        {
            if (!_pipHasUserMoved)
                PositionPipTopCenter();
            else
                ClampPipWithinBounds();
        }
    }

    private async void OnToggleVideoLessonTapped(object? sender, TappedEventArgs e)
    {
        if (_isVideoLessonStartStopInProgress)
            return;

        // Si ya está grabando, el botón siempre hace STOP.
        if (_videoLessonRecorder.IsRecording)
        {
            _isVideoLessonRecording = true;
            await StopVideoLessonRecordingAsync();
            return;
        }

        // Iniciar grabación directamente (las opciones siempre están visibles)
        await StartVideoLessonRecordingAsync();
    }

    private void OnVideoLessonCameraToggleTapped(object? sender, TappedEventArgs e)
    {
        _videoLessonCameraEnabled = !_videoLessonCameraEnabled;
        ApplyVideoLessonRuntimeOptions();
    }

    private void OnVideoLessonMicToggleTapped(object? sender, TappedEventArgs e)
    {
        _videoLessonMicEnabled = !_videoLessonMicEnabled;
        ApplyVideoLessonRuntimeOptions();
    }

    private void ApplyVideoLessonRuntimeOptions()
    {
        UpdateVideoLessonOptionsVisualState();

#if MACCATALYST
        if (_videoLessonRecorder is CrownRFEP_Reader.Platforms.MacCatalyst.ReplayKitVideoLessonRecorder replayKit)
            replayKit.SetOptions(cameraEnabled: _videoLessonCameraEnabled, microphoneEnabled: _videoLessonMicEnabled);
#endif

#if WINDOWS
        if (_videoLessonRecorder is CrownRFEP_Reader.Platforms.Windows.WindowsVideoLessonRecorder windowsRecorder)
            windowsRecorder.SetOptions(cameraEnabled: _videoLessonCameraEnabled, microphoneEnabled: _videoLessonMicEnabled);
#endif

        var shouldShowCamera = _isVideoLessonRecording && _videoLessonCameraEnabled;

        if (VideoLessonCameraPip != null)
            VideoLessonCameraPip.IsVisible = shouldShowCamera;

        if (VideoLessonCameraPreview != null)
            VideoLessonCameraPreview.IsActive = shouldShowCamera;

        if (VideoLessonWebcamPreview != null)
            VideoLessonWebcamPreview.IsActive = shouldShowCamera;

        if (shouldShowCamera)
            EnsurePipPositioned();
    }

    private async Task StartVideoLessonRecordingAsync()
    {
        if (_videoLessonRecorder.IsRecording)
        {
            SyncVideoLessonUiFromRecorder();
            return;
        }

        var sessionId = _viewModel.VideoClip?.SessionId ?? 0;
        if (sessionId <= 0)
        {
            await DisplayAlert("Sin sesión", "No se pudo determinar la sesión para guardar la videolección.", "OK");
            return;
        }

        try
        {
            _isVideoLessonStartStopInProgress = true;

            // Aplicar opciones antes de iniciar
            ApplyVideoLessonRuntimeOptions();

            var dir = Path.Combine(FileSystem.AppDataDirectory, "videolecciones");
            Directory.CreateDirectory(dir);

            var fileName = $"videoleccion_s{sessionId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp4";
            var path = Path.Combine(dir, fileName);
            _currentVideoLessonPath = path;

            await _videoLessonRecorder.StartAsync(path);
            _isVideoLessonRecording = true;
            _isVideoLessonMode = true;

            if (VideoLessonOptionsPanel != null)
                VideoLessonOptionsPanel.IsVisible = true;

            if (VideoLessonCameraPip != null)
                VideoLessonCameraPip.IsVisible = _videoLessonCameraEnabled;

            if (VideoLessonCameraPreview != null)
                VideoLessonCameraPreview.IsActive = _videoLessonCameraEnabled;

            if (VideoLessonWebcamPreview != null)
                VideoLessonWebcamPreview.IsActive = _videoLessonCameraEnabled;

            if (_videoLessonCameraEnabled)
                EnsurePipPositioned();

            // En Windows a veces el layout todavía no está listo; re-posicionar tras un tick
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await Task.Delay(75);
                    if (_isVideoLessonRecording && _videoLessonCameraEnabled)
                        EnsurePipPositioned();
                }
                catch
                {
                    // Ignorar
                }
            });
            UpdateVideoLessonToggleVisualState();
            UpdateVideoLessonOptionsVisualState();
            StartRecordingTimer();
        }
        catch (Exception ex)
        {
            _currentVideoLessonPath = null;
            _isVideoLessonRecording = false;
            _isVideoLessonMode = false;
            UpdateVideoLessonToggleVisualState();

            if (VideoLessonCameraPip != null)
                VideoLessonCameraPip.IsVisible = false;

            if (VideoLessonCameraPreview != null)
                VideoLessonCameraPreview.IsActive = false;

            if (VideoLessonWebcamPreview != null)
                VideoLessonWebcamPreview.IsActive = false;

            // VideoLessonOptionsPanel siempre visible
            StopRecordingTimer();

            await DisplayAlert("Error", $"No se pudo iniciar la grabación: {ex.Message}", "OK");
        }
        finally
        {
            _isVideoLessonStartStopInProgress = false;
        }
    }

    private async Task StopVideoLessonRecordingAsync()
    {
        if (!_videoLessonRecorder.IsRecording)
        {
            _isVideoLessonRecording = false;
            _isVideoLessonMode = false;
            UpdateVideoLessonToggleVisualState();

            if (VideoLessonCameraPip != null)
                VideoLessonCameraPip.IsVisible = false;

            if (VideoLessonCameraPreview != null)
                VideoLessonCameraPreview.IsActive = false;

            if (VideoLessonWebcamPreview != null)
                VideoLessonWebcamPreview.IsActive = false;

            StopRecordingTimer();
            _currentVideoLessonPath = null;
            return;
        }

        try
        {
            _isVideoLessonStartStopInProgress = true;
            var path = _currentVideoLessonPath;
            AppLog.Info("SinglePlayerPage", $"StopVideoLessonRecordingAsync: stopping recorder | path='{path}'");
            await _videoLessonRecorder.StopAsync();
            _isVideoLessonRecording = false;
            _isVideoLessonMode = false;

            UpdateVideoLessonToggleVisualState();

            if (VideoLessonCameraPip != null)
                VideoLessonCameraPip.IsVisible = false;

            if (VideoLessonCameraPreview != null)
                VideoLessonCameraPreview.IsActive = false;

            if (VideoLessonWebcamPreview != null)
                VideoLessonWebcamPreview.IsActive = false;

            // VideoLessonOptionsPanel siempre visible
            StopRecordingTimer();

            var sessionId = _viewModel.VideoClip?.SessionId ?? 0;
            if (!string.IsNullOrWhiteSpace(path)
                && await WaitForVideoLessonFileReadyAsync(path).ConfigureAwait(false))
            {
                long size = 0;
                try { size = new FileInfo(path).Length; } catch { }
                AppLog.Info("SinglePlayerPage", $"StopVideoLessonRecordingAsync: file ready | size={size} | path='{path}'");

                var lesson = new VideoLesson
                {
                    SessionId = sessionId,
                    CreatedAtUtc = DateTime.UtcNow,
                    FilePath = path,
                    Title = null
                };

                var savedId = await _databaseService.SaveVideoLessonAsync(lesson);
                AppLog.Info("SinglePlayerPage", $"StopVideoLessonRecordingAsync: saved VideoLesson | id={savedId} | sessionId={sessionId}");
            }
            else
            {
                AppLog.Info("SinglePlayerPage", $"StopVideoLessonRecordingAsync: file NOT ready or missing | path='{path}' | exists={(path != null && File.Exists(path))}");
                await DisplayAlert("Grabación finalizada", "La grabación terminó, pero no se encontró el archivo generado.", "OK");
            }
        }
        catch (Exception ex)
        {
            _isVideoLessonRecording = false;
            _isVideoLessonMode = false;
            UpdateVideoLessonToggleVisualState();
            StopRecordingTimer();
            await DisplayAlert("Error", $"No se pudo detener la grabación: {ex.Message}", "OK");
        }
        finally
        {
            _currentVideoLessonPath = null;
            _isVideoLessonStartStopInProgress = false;
        }
    }

    private void StartRecordingTimer()
    {
        _recordingStartTime = DateTime.Now;
        
        if (RecordingTimerBorder != null)
            RecordingTimerBorder.IsVisible = true;
        
        if (RecordingTimerLabel != null)
            RecordingTimerLabel.Text = "00:00";

        _recordingTimer?.Stop();
        _recordingTimer?.Dispose();
        
        _recordingTimer = new System.Timers.Timer(1000);
        _recordingTimer.Elapsed += (s, e) =>
        {
            if (!_isPageActive) return;
            
            var elapsed = DateTime.Now - _recordingStartTime;
            var formatted = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_isPageActive || RecordingTimerLabel == null) return;
                RecordingTimerLabel.Text = formatted;
            });
        };
        _recordingTimer.Start();
    }

    private void StopRecordingTimer()
    {
        _recordingTimer?.Stop();
        _recordingTimer?.Dispose();
        _recordingTimer = null;

        if (RecordingTimerBorder != null)
            RecordingTimerBorder.IsVisible = false;
        
        if (RecordingTimerLabel != null)
            RecordingTimerLabel.Text = "00:00";
    }

    private void EnsurePipPositioned()
    {
        if (VideoLessonCameraPip == null)
            return;

        if (!_pipHasUserMoved)
            PositionPipTopCenter();
        else
            ClampPipWithinBounds();
    }

    private void PositionPipTopCenter()
    {
        if (VideoLessonCameraPip == null)
            return;

        const double inset = 16;

        var container = VideoLessonCameraPip.Parent as VisualElement;
        var containerWidth = container?.Width ?? RootGrid?.Width ?? 0;
        var containerHeight = container?.Height ?? RootGrid?.Height ?? 0;
        if (containerWidth <= 0 || containerHeight <= 0)
            return;

        var pipWidth = VideoLessonCameraPip.Width;
        if (pipWidth <= 0)
            pipWidth = VideoLessonCameraPip.WidthRequest;

        var pipHeight = VideoLessonCameraPip.Height;
        if (pipHeight <= 0)
            pipHeight = VideoLessonCameraPip.HeightRequest;

        var x = (containerWidth - pipWidth) / 2.0;
        var y = inset;

        VideoLessonCameraPip.TranslationX = x;
        VideoLessonCameraPip.TranslationY = y;

        ClampPipWithinBounds();
    }

    private void ClampPipWithinBounds()
    {
        if (VideoLessonCameraPip == null)
            return;

        const double inset = 16;

        var container = VideoLessonCameraPip.Parent as VisualElement;
        var containerWidth = container?.Width ?? RootGrid?.Width ?? 0;
        var containerHeight = container?.Height ?? RootGrid?.Height ?? 0;
        if (containerWidth <= 0 || containerHeight <= 0)
            return;

        var pipWidth = VideoLessonCameraPip.Width;
        if (pipWidth <= 0)
            pipWidth = VideoLessonCameraPip.WidthRequest;

        var pipHeight = VideoLessonCameraPip.Height;
        if (pipHeight <= 0)
            pipHeight = VideoLessonCameraPip.HeightRequest;

        var minX = inset;
        var minY = inset;
        var maxX = Math.Max(minX, containerWidth - pipWidth - inset);
        var maxY = Math.Max(minY, containerHeight - pipHeight - inset);

        var clampedX = Math.Clamp(VideoLessonCameraPip.TranslationX, minX, maxX);
        var clampedY = Math.Clamp(VideoLessonCameraPip.TranslationY, minY, maxY);

        VideoLessonCameraPip.TranslationX = clampedX;
        VideoLessonCameraPip.TranslationY = clampedY;
    }

    private void OnVideoLessonPipPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (VideoLessonCameraPip == null)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _pipDragStartTranslationX = VideoLessonCameraPip.TranslationX;
                _pipDragStartTranslationY = VideoLessonCameraPip.TranslationY;
                break;

            case GestureStatus.Running:
            {
                var newX = _pipDragStartTranslationX + e.TotalX;
                var newY = _pipDragStartTranslationY + e.TotalY;

                VideoLessonCameraPip.TranslationX = newX;
                VideoLessonCameraPip.TranslationY = newY;
                ClampPipWithinBounds();
                break;
            }

            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                _pipHasUserMoved = true;
                ClampPipWithinBounds();
                break;
        }
    }

    // Los handlers Start/Stop ya no se usan (se mantiene la grabación como toggle en OnToggleVideoLessonTapped)

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;

        AppLog.Info(
            "SinglePlayerPage",
            $"OnAppearing | IsRecording={_videoLessonRecorder.IsRecording} | VideoPath='{_viewModel.VideoPath}' | NavStack={Shell.Current?.Navigation?.NavigationStack?.Count} | ModalStack={Shell.Current?.Navigation?.ModalStack?.Count}");
        SetupMediaHandlers();

        SyncVideoLessonUiFromRecorder();
        
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
        KeyPressHandler.DeletePressed += OnDeletePressed;
        KeyPressHandler.BackspacePressed += OnBackspacePressed;
    #endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        AppLog.Info(
            "SinglePlayerPage",
            $"OnDisappearing BEGIN | IsRecording={_videoLessonRecorder.IsRecording} | HasMediaPlayer={(MediaPlayer != null)} | NavStack={Shell.Current?.Navigation?.NavigationStack?.Count} | ModalStack={Shell.Current?.Navigation?.ModalStack?.Count}");

        _isPageActive = false;
        
        // Desuscribirse de eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated -= OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded -= OnScrubEnded;

#if MACCATALYST || WINDOWS
        KeyPressHandler.SpaceBarPressed -= OnSpaceBarPressed;
        KeyPressHandler.ArrowLeftPressed -= OnArrowLeftPressed;
        KeyPressHandler.ArrowRightPressed -= OnArrowRightPressed;
        KeyPressHandler.DeletePressed -= OnDeletePressed;
        KeyPressHandler.BackspacePressed -= OnBackspacePressed;
#endif
        
        try
        {
            CleanupResources();
        }
        catch (Exception ex)
        {
            AppLog.Error("SinglePlayerPage", "OnDisappearing: CleanupResources threw", ex);
        }

        AppLog.Info("SinglePlayerPage", "OnDisappearing END");
    }

#if MACCATALYST || WINDOWS
    private void OnSpaceBarPressed(object? sender, EventArgs e)
    {
        if (!_isPageActive || _isTextPromptOpen)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.PlayPauseCommand.Execute(null);
        });
    }

    private void OnArrowLeftPressed(object? sender, EventArgs e)
    {
        if (!_isPageActive || _isTextPromptOpen)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.FrameBackwardCommand.Execute(null);
        });
    }

    private void OnArrowRightPressed(object? sender, EventArgs e)
    {
        if (!_isPageActive || _isTextPromptOpen)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.FrameForwardCommand.Execute(null);
        });
    }

    private void OnDeletePressed(object? sender, EventArgs e)
    {
        if (!_isPageActive || !_isDrawingMode || _isTextPromptOpen)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (AnalysisCanvas?.HasSelection == true)
                AnalysisCanvas.DeleteSelected();
        });
    }

    private void OnBackspacePressed(object? sender, EventArgs e)
    {
        if (!_isPageActive || !_isDrawingMode || _isTextPromptOpen)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (AnalysisCanvas?.HasSelection == true)
                AnalysisCanvas.DeleteSelected();
        });
    }
#endif

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
        AppLog.Info("SinglePlayerPage", "CleanupResources BEGIN");

        // Si el usuario sale con una videolección en curso, detenerla aquí
        // (si no, ffmpeg sigue ejecutándose y el MP4 queda sin finalizar).
        if (_videoLessonRecorder.IsRecording)
        {
            var pathToSave = _currentVideoLessonPath;
            var sessionIdToSave = _viewModel.VideoClip?.SessionId ?? 0;

            AppLog.Info("SinglePlayerPage", $"CleanupResources: leaving while recording | path='{pathToSave}' | sessionId={sessionIdToSave}");

            _ = Task.Run(async () =>
            {
                try
                {
                    await _videoLessonRecorder.StopAsync().ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(pathToSave)
                        && sessionIdToSave > 0
                        && await WaitForVideoLessonFileReadyAsync(pathToSave).ConfigureAwait(false))
                    {
                        long size = 0;
                        try { size = new FileInfo(pathToSave).Length; } catch { }
                        AppLog.Info("SinglePlayerPage", $"CleanupResources: file ready | size={size} | path='{pathToSave}'");

                        var lesson = new VideoLesson
                        {
                            SessionId = sessionIdToSave,
                            CreatedAtUtc = DateTime.UtcNow,
                            FilePath = pathToSave,
                            Title = null
                        };
                        var savedId = await _databaseService.SaveVideoLessonAsync(lesson).ConfigureAwait(false);
                        AppLog.Info("SinglePlayerPage", $"CleanupResources: saved VideoLesson | id={savedId} | sessionId={sessionIdToSave}");
                    }
                    else
                    {
                        AppLog.Info("SinglePlayerPage", $"CleanupResources: file NOT ready or missing | path='{pathToSave}' | exists={(pathToSave != null && File.Exists(pathToSave))}");
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("SinglePlayerPage", "CleanupResources: stop videolección failed", ex);
                }
            });

            // Evitar que la UI piense que sigue grabando
            _isVideoLessonRecording = false;
            _isVideoLessonMode = false;
            _isVideoLessonStartStopInProgress = false;
            _currentVideoLessonPath = null;
        }

        if (RootGrid != null)
            RootGrid.SizeChanged -= OnRootGridSizeChanged;

        // Detener el cronómetro de grabación
        StopRecordingTimer();

        // Desactivar el preview de cámara antes de seguir (evita callbacks nativos tardíos)
        try
        {
            if (VideoLessonCameraPreview != null)
                VideoLessonCameraPreview.IsActive = false;
            if (VideoLessonWebcamPreview != null)
                VideoLessonWebcamPreview.IsActive = false;
        }
        catch (Exception ex)
        {
            AppLog.Error("SinglePlayerPage", "CleanupResources: VideoLessonCameraPreview.IsActive=false threw", ex);
        }

        // Desuscribirse de eventos del ViewModel
        _viewModel.PlayRequested -= OnPlayRequested;
        _viewModel.PauseRequested -= OnPauseRequested;
        _viewModel.StopRequested -= OnStopRequested;
        _viewModel.SeekRequested -= OnSeekRequested;
        _viewModel.FrameForwardRequested -= OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested -= OnFrameBackwardRequested;
        _viewModel.SpeedChangeRequested -= OnSpeedChangeRequested;
        _viewModel.VideoChanged -= OnVideoChanged;

        if (AnalysisCanvas != null)
            AnalysisCanvas.TextRequested -= OnAnalysisCanvasTextRequested;

        // Desuscribirse de eventos del reproductor
        if (MediaPlayer != null)
        {
            MediaPlayer.MediaOpened -= OnMediaOpened;
            MediaPlayer.PositionChanged -= OnPositionChanged;
            MediaPlayer.MediaEnded -= OnMediaEnded;

#if WINDOWS
            try
            {
                // Notificar al handler que debe detenerse inmediatamente
                // Esto detiene el timer y desuscribe eventos nativos antes de que
                // la navegación pueda causar problemas
                AppLog.Info("SinglePlayerPage", "CleanupResources: calling MediaPlayer.PrepareForCleanup()");
                MediaPlayer.PrepareForCleanup();
            }
            catch (Exception ex)
            {
                AppLog.Error("SinglePlayerPage", "CleanupResources: PrepareForCleanup threw", ex);
            }
#endif

            try
            {
                AppLog.Info("SinglePlayerPage", "CleanupResources: calling MediaPlayer.Stop()");
                MediaPlayer.Stop();
                AppLog.Info("SinglePlayerPage", "CleanupResources: MediaPlayer.Stop() returned");
            }
            catch (Exception ex)
            {
                AppLog.Error("SinglePlayerPage", "CleanupResources: MediaPlayer.Stop() threw", ex);
            }

#if WINDOWS
            try
            {
                // En WinUI esto ayuda a evitar callbacks tardíos tras navegar atrás.
                MediaPlayer.Source = null;
            }
            catch (Exception ex)
            {
                AppLog.Error("SinglePlayerPage", "CleanupResources: clearing MediaPlayer.Source threw", ex);
            }
#endif

            // NOTA: NO llamar a DisconnectHandler() en Windows durante la navegación
            // ya que corrompe el Frame de navegación y causa crash (0x80004004 E_ABORT)
            // WinUI limpia automáticamente los handlers cuando la página se descarga
        }

        AppLog.Info("SinglePlayerPage", "CleanupResources END");
    }

    private static async Task<bool> WaitForVideoLessonFileReadyAsync(string path, int minBytes = 1024, int timeoutMs = 2500)
    {
        var start = Environment.TickCount;

        while (Environment.TickCount - start < timeoutMs)
        {
            try
            {
                if (!File.Exists(path))
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length < minBytes)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    continue;
                }

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return stream.Length >= minBytes;
            }
            catch
            {
                // El archivo puede estar aún en escritura/bloqueado; reintentar.
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        return File.Exists(path) && new FileInfo(path).Length >= minBytes;
    }

    private void OnToggleDrawingToolsTapped(object? sender, TappedEventArgs e)
    {
        _isDrawingMode = !_isDrawingMode;

        if (DrawingToolsPanel != null)
            DrawingToolsPanel.IsVisible = _isDrawingMode;

        if (AnalysisCanvas != null)
            AnalysisCanvas.InputTransparent = !_isDrawingMode;

        // Cuando dibujamos, evitamos que el overlay de scrub intercepte gestos.
        if (ScrubOverlay != null)
            ScrubOverlay.InputTransparent = _isDrawingMode;

        // Por defecto: herramienta de trazo al activar
        if (_isDrawingMode && AnalysisCanvas != null)
        {
            SetSelectedInkColor(DefaultInkColor);
            SetSelectedInkThickness(DefaultInkThickness);
            SetSelectedTextSize(DefaultTextSize);
            SetSelectedTool(AnalysisDrawingTool.Stroke);
        }

        if (!_isDrawingMode && InkOptionsPanel != null)
            InkOptionsPanel.IsVisible = false;
    }

    private void OnSelectStrokeToolTapped(object? sender, TappedEventArgs e)
        => SetSelectedTool(AnalysisDrawingTool.Stroke);

    private void OnSelectTextToolTapped(object? sender, TappedEventArgs e)
        => SetSelectedTool(AnalysisDrawingTool.Text);

    private void OnSelectShapeToolTapped(object? sender, TappedEventArgs e)
        => SetSelectedTool(AnalysisDrawingTool.Shape);

    private void OnSelectShapeRectTapped(object? sender, TappedEventArgs e)
        => SetSelectedShape(AnalysisShapeType.Rectangle);

    private void OnSelectShapeCircleTapped(object? sender, TappedEventArgs e)
        => SetSelectedShape(AnalysisShapeType.Circle);

    private void OnSelectShapeLineTapped(object? sender, TappedEventArgs e)
        => SetSelectedShape(AnalysisShapeType.Line);

    private void OnSelectShapeArrowTapped(object? sender, TappedEventArgs e)
        => SetSelectedShape(AnalysisShapeType.Arrow);

    private void SetSelectedTool(AnalysisDrawingTool tool)
    {
        if (AnalysisCanvas == null)
            return;

        AnalysisCanvas.Tool = tool;

        if (ShapeOptionsRow != null)
            ShapeOptionsRow.IsVisible = tool == AnalysisDrawingTool.Shape;

        if (tool == AnalysisDrawingTool.Shape)
        {
            // Por defecto: rectángulo
            SetSelectedShape(AnalysisCanvas.ShapeType);
        }

        // UI mínima: resaltar el botón activo
        if (ToolStrokeButton != null)
            ToolStrokeButton.BackgroundColor = tool == AnalysisDrawingTool.Stroke ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (ToolTextButton != null)
            ToolTextButton.BackgroundColor = tool == AnalysisDrawingTool.Text ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (ToolShapeButton != null)
            ToolShapeButton.BackgroundColor = tool == AnalysisDrawingTool.Shape ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
    }

    private void OnToggleInkOptionsTapped(object? sender, TappedEventArgs e)
    {
        if (!_isDrawingMode)
            return;

        if (InkOptionsPanel != null)
            InkOptionsPanel.IsVisible = !InkOptionsPanel.IsVisible;
    }

    private void OnSelectInkOrangeTapped(object? sender, TappedEventArgs e)
        => SetSelectedInkColor(Color.FromArgb("#FFFF7043"));

    private void OnSelectInkWhiteTapped(object? sender, TappedEventArgs e)
        => SetSelectedInkColor(Colors.White);

    private void OnSelectInkBlueTapped(object? sender, TappedEventArgs e)
        => SetSelectedInkColor(Color.FromArgb("#FF6DDDFF"));

    private void OnSelectInkGreenTapped(object? sender, TappedEventArgs e)
        => SetSelectedInkColor(Color.FromArgb("#FF66BB6A"));

    private void SetSelectedInkColor(Color color)
    {
        if (AnalysisCanvas == null)
            return;

        AnalysisCanvas.InkColor = color;

        if (InkColorSwatch != null)
            InkColorSwatch.BackgroundColor = color;

        // Resaltado mínimo del color activo
        if (InkColorOrangeButton != null)
            InkColorOrangeButton.BackgroundColor = color == Color.FromArgb("#FFFF7043") ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (InkColorWhiteButton != null)
            InkColorWhiteButton.BackgroundColor = color == Colors.White ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (InkColorBlueButton != null)
            InkColorBlueButton.BackgroundColor = color == Color.FromArgb("#FF6DDDFF") ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (InkColorGreenButton != null)
            InkColorGreenButton.BackgroundColor = color == Color.FromArgb("#FF66BB6A") ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
    }

    private void OnSelectInkThickness2Tapped(object? sender, TappedEventArgs e)
        => SetSelectedInkThickness(2f);

    private void OnSelectInkThickness4Tapped(object? sender, TappedEventArgs e)
        => SetSelectedInkThickness(4f);

    private void OnSelectInkThickness6Tapped(object? sender, TappedEventArgs e)
        => SetSelectedInkThickness(6f);

    private void SetSelectedInkThickness(float thickness)
    {
        if (AnalysisCanvas == null)
            return;

        AnalysisCanvas.InkThickness = thickness;

        if (InkThickness2Button != null)
            InkThickness2Button.BackgroundColor = Math.Abs(thickness - 2f) < 0.01f ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (InkThickness4Button != null)
            InkThickness4Button.BackgroundColor = Math.Abs(thickness - 4f) < 0.01f ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (InkThickness6Button != null)
            InkThickness6Button.BackgroundColor = Math.Abs(thickness - 6f) < 0.01f ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
    }

    private void OnSelectTextSizeSmallTapped(object? sender, TappedEventArgs e)
        => SetSelectedTextSize(12f);

    private void OnSelectTextSizeMediumTapped(object? sender, TappedEventArgs e)
        => SetSelectedTextSize(16f);

    private void OnSelectTextSizeLargeTapped(object? sender, TappedEventArgs e)
        => SetSelectedTextSize(22f);

    private void SetSelectedTextSize(float size)
    {
        if (AnalysisCanvas == null)
            return;

        AnalysisCanvas.TextSize = size;

        if (TextSizeSmallButton != null)
            TextSizeSmallButton.BackgroundColor = Math.Abs(size - 12f) < 0.01f ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (TextSizeMediumButton != null)
            TextSizeMediumButton.BackgroundColor = Math.Abs(size - 16f) < 0.01f ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (TextSizeLargeButton != null)
            TextSizeLargeButton.BackgroundColor = Math.Abs(size - 22f) < 0.01f ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
    }

    private void SetSelectedShape(AnalysisShapeType shape)
    {
        if (AnalysisCanvas == null)
            return;

        AnalysisCanvas.ShapeType = shape;

        if (ShapeRectButton != null)
            ShapeRectButton.BackgroundColor = shape == AnalysisShapeType.Rectangle ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (ShapeCircleButton != null)
            ShapeCircleButton.BackgroundColor = shape == AnalysisShapeType.Circle ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (ShapeLineButton != null)
            ShapeLineButton.BackgroundColor = shape == AnalysisShapeType.Line ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
        if (ShapeArrowButton != null)
            ShapeArrowButton.BackgroundColor = shape == AnalysisShapeType.Arrow ? Color.FromArgb("#FFFF7043") : Color.FromArgb("#FF2A2A2A");
    }

    private async void OnAnalysisCanvasTextRequested(object? sender, TextRequestedEventArgs e)
    {
        if (!_isDrawingMode || AnalysisCanvas == null)
            return;

        _isTextPromptOpen = true;
        var text = await DisplayPromptAsync("Texto", "Introduce el texto", "OK", "Cancelar");
        _isTextPromptOpen = false;
        if (string.IsNullOrWhiteSpace(text))
            return;

        AnalysisCanvas.AddText(e.Position, text);
    }

    private void OnClearDrawingTapped(object? sender, TappedEventArgs e)
    {
        if (!_isDrawingMode)
            return;

        AnalysisCanvas?.ClearAll();
    }

    #region Eventos del reproductor

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        
        if (sender is PrecisionVideoPlayer player && player.Duration > TimeSpan.Zero)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_isPageActive) return;
                _viewModel.Duration = player.Duration;
            });
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (!_isPageActive || _isDraggingSlider || _isScrubbing)
        {
            System.Diagnostics.Debug.WriteLine($"[SinglePlayerPage] OnPositionChanged SKIPPED: _isPageActive={_isPageActive}, _isDraggingSlider={_isDraggingSlider}, _isScrubbing={_isScrubbing}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[SinglePlayerPage] OnPositionChanged: {position}");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isPageActive || _isDraggingSlider) return;
            _viewModel.CurrentPosition = position;
            
            // Actualizar el slider directamente (sin binding para evitar conflictos)
            if (ProgressSlider != null && _viewModel.Duration.TotalSeconds > 0)
            {
                var progress = position.TotalSeconds / _viewModel.Duration.TotalSeconds;
                ProgressSlider.Value = progress;
            }
        });
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isPageActive) return;
            _viewModel.IsPlaying = false;
        });
    }

    #endregion

    #region Handlers del ViewModel

    private void OnPlayRequested(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        
        // Resetear flags de drag/scrub por si quedaron atascados
        // (puede ocurrir en MacCatalyst si el DragCompleted no se dispara)
        if (_isDraggingSlider || _isScrubbing)
        {
            System.Diagnostics.Debug.WriteLine($"[SinglePlayerPage] OnPlayRequested: Resetting drag/scrub flags (isDragging={_isDraggingSlider}, isScrubbing={_isScrubbing})");
            _isDraggingSlider = false;
            _isScrubbing = false;
            _viewModel.IsDraggingSlider = false; // También resetear en el ViewModel
        }
        
        MediaPlayer?.Play();
    }

    private void OnPauseRequested(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        MediaPlayer?.Pause();
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        MediaPlayer?.Stop();
    }

    private void OnSeekRequested(object? sender, double seconds)
    {
        if (!_isPageActive) return;
        var position = TimeSpan.FromSeconds(seconds);
        MediaPlayer?.SeekTo(position);
        _viewModel.CurrentPosition = position;
    }

    private void OnFrameForwardRequested(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        MediaPlayer?.StepForward();
    }

    private void OnFrameBackwardRequested(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        MediaPlayer?.StepBackward();
    }

    private void OnSpeedChangeRequested(object? sender, double speed)
    {
        if (!_isPageActive || MediaPlayer == null) return;
        MediaPlayer.Speed = speed;
    }

    #endregion

    #region Slider handlers

    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        _isDraggingSlider = true;
        _viewModel.IsDraggingSlider = true; // Notificar al ViewModel para evitar actualizar Progress
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

        // Calcular la posición objetivo
        var position = TimeSpan.FromSeconds(e.NewValue * _viewModel.Duration.TotalSeconds);
        
        // Actualizar el texto de tiempo
        _viewModel.CurrentPosition = position;
        
#if WINDOWS
        // En Windows, throttle seeks para evitar bloqueos
        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime).TotalMilliseconds >= SeekThrottleMs)
        {
            _lastSeekTime = now;
            MediaPlayer?.SeekTo(position);
        }
#else
        // En MacCatalyst/iOS: hacer seek para mostrar el frame actual
        // El parpadeo ya no ocurre porque eliminamos el binding del slider
        MediaPlayer?.SeekTo(position);
#endif
    }

    private void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        // Primero hacer el seek al frame deseado
        var position = TimeSpan.FromSeconds(ProgressSlider.Value * _viewModel.Duration.TotalSeconds);
        MediaPlayer?.SeekTo(position);
        
        // Después desactivar los flags para que Progress se actualice normalmente
        _isDraggingSlider = false;
        _viewModel.IsDraggingSlider = false;
        
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
        if (!_isPageActive) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isPageActive) return;
            
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
