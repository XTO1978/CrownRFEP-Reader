using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views.Controls;
using CrownRFEP_Reader.ViewModels;

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#endif

namespace CrownRFEP_Reader.Views;

public partial class SinglePlayerPage : ContentPage
{
    private readonly SinglePlayerViewModel _viewModel;
    private readonly DatabaseService _databaseService;
    private readonly IVideoLessonRecorder _videoLessonRecorder;

    private bool _isDrawingMode;

    private bool _isVideoLessonMode;
    private bool _isVideoLessonRecording;
    private string? _currentVideoLessonPath;
    private bool _isVideoLessonStartStopInProgress;

    private bool _videoLessonCameraEnabled = true;
    private bool _videoLessonMicEnabled = true;

    private bool _isPageActive;
    private bool _isTextPromptOpen;

    private bool _pipHasUserMoved;
    private double _pipDragStartTranslationX;
    private double _pipDragStartTranslationY;
    
    // Flags para controlar el scrubbing del slider
    private bool _isDraggingSlider;
    private bool _wasPlayingBeforeDrag;
    
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

        SyncVideoLessonUiFromRecorder();
    }

    private void SyncVideoLessonUiFromRecorder()
    {
        _isVideoLessonRecording = _videoLessonRecorder.IsRecording;
        _isVideoLessonMode = _isVideoLessonRecording;

        if (VideoLessonCameraPip != null)
            VideoLessonCameraPip.IsVisible = _isVideoLessonRecording && _videoLessonCameraEnabled;

        if (VideoLessonCameraPreview != null)
            VideoLessonCameraPreview.IsActive = _isVideoLessonRecording && _videoLessonCameraEnabled;

        if (VideoLessonOptionsPanel != null)
            VideoLessonOptionsPanel.IsVisible = !_isVideoLessonRecording && false;

        UpdateVideoLessonToggleVisualState();
        UpdateVideoLessonOptionsVisualState();

        if (_isVideoLessonRecording)
            EnsurePipPositioned();
    }

    private void UpdateVideoLessonToggleVisualState()
    {
        if (VideoLessonRecordButton != null)
            VideoLessonRecordButton.BackgroundColor = _isVideoLessonRecording
                ? Color.FromArgb("#FFFF7043")
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

        // Si no está grabando: primer toque abre opciones, segundo toque inicia.
        if (VideoLessonOptionsPanel != null && !VideoLessonOptionsPanel.IsVisible)
        {
            VideoLessonOptionsPanel.IsVisible = true;
            UpdateVideoLessonOptionsVisualState();
            return;
        }

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

        var shouldShowCamera = _isVideoLessonRecording && _videoLessonCameraEnabled;

        if (VideoLessonCameraPip != null)
            VideoLessonCameraPip.IsVisible = shouldShowCamera;

        if (VideoLessonCameraPreview != null)
            VideoLessonCameraPreview.IsActive = shouldShowCamera;

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

            if (_videoLessonCameraEnabled)
                EnsurePipPositioned();
            UpdateVideoLessonToggleVisualState();
            UpdateVideoLessonOptionsVisualState();
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

            if (VideoLessonOptionsPanel != null)
                VideoLessonOptionsPanel.IsVisible = false;

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

            _currentVideoLessonPath = null;
            return;
        }

        try
        {
            _isVideoLessonStartStopInProgress = true;
            await _videoLessonRecorder.StopAsync();
            _isVideoLessonRecording = false;
            _isVideoLessonMode = false;

            UpdateVideoLessonToggleVisualState();

            if (VideoLessonCameraPip != null)
                VideoLessonCameraPip.IsVisible = false;

            if (VideoLessonCameraPreview != null)
                VideoLessonCameraPreview.IsActive = false;

            if (VideoLessonOptionsPanel != null)
                VideoLessonOptionsPanel.IsVisible = false;

            var sessionId = _viewModel.VideoClip?.SessionId ?? 0;
            if (!string.IsNullOrWhiteSpace(_currentVideoLessonPath) && File.Exists(_currentVideoLessonPath))
            {
                var lesson = new VideoLesson
                {
                    SessionId = sessionId,
                    CreatedAtUtc = DateTime.UtcNow,
                    FilePath = _currentVideoLessonPath,
                    Title = null
                };

                await _databaseService.SaveVideoLessonAsync(lesson);
            }
            else
            {
                await DisplayAlert("Grabación finalizada", "La grabación terminó, pero no se encontró el archivo generado.", "OK");
            }
        }
        catch (Exception ex)
        {
            _isVideoLessonRecording = false;
            _isVideoLessonMode = false;
            UpdateVideoLessonToggleVisualState();
            await DisplayAlert("Error", $"No se pudo detener la grabación: {ex.Message}", "OK");
        }
        finally
        {
            _currentVideoLessonPath = null;
            _isVideoLessonStartStopInProgress = false;
        }
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
        if (RootGrid == null || VideoLessonCameraPip == null)
            return;

        const double inset = 16;

        var containerWidth = RootGrid.Width;
        var containerHeight = RootGrid.Height;
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
        if (RootGrid == null || VideoLessonCameraPip == null)
            return;

        const double inset = 16;

        var containerWidth = RootGrid.Width;
        var containerHeight = RootGrid.Height;
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
        SetupMediaHandlers();

        SyncVideoLessonUiFromRecorder();
        
        // Suscribirse a eventos de scrubbing (trackpad/mouse wheel)
        VideoScrubBehavior.ScrubUpdated += OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded += OnScrubEnded;

    #if MACCATALYST
        KeyPressHandler.DeletePressed += OnDeletePressed;
        KeyPressHandler.BackspacePressed += OnBackspacePressed;
    #endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _isPageActive = false;
        
        // Desuscribirse de eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated -= OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded -= OnScrubEnded;

#if MACCATALYST
        KeyPressHandler.DeletePressed -= OnDeletePressed;
        KeyPressHandler.BackspacePressed -= OnBackspacePressed;
#endif
        
        CleanupResources();
    }

#if MACCATALYST
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
            MediaPlayer.Stop();
            MediaPlayer.Handler?.DisconnectHandler();
        }
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
