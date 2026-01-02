using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para la página de cámara de grabación de sesiones
/// </summary>
public class CameraViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ICameraRecordingService _cameraService;
    private readonly DatabaseService _databaseService;
    private readonly Stopwatch _stopwatch = new();
    private IDispatcherTimer? _timer;
    private bool _disposed;

    private CameraRecordingSession _currentSession = new();
    private bool _isRecording;
    private bool _isPreviewing;
    private bool _isInitializing;
    private string _elapsedTimeDisplay = "00:00.00";
    private double _zoomFactor = 1.0;
    private double _minZoom = 1.0;
    private double _maxZoom = 10.0;
    private Athlete? _selectedAthlete;
    private RiverSection? _selectedSection;
    private Tag? _selectedTag;
    private bool _showAthleteSelector;
    private bool _showSectionSelector;
    private string? _errorMessage;
    private bool _athletesLoaded;
    private bool _tagsLoaded;
    private bool _sectionsLoaded;
    private bool _isLevelMonitoring;
    private DisplayRotation _displayRotation = DisplayRotation.Rotation0;
    private double _levelAngleDegrees;
    private bool _isLevel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CameraViewModel(ICameraRecordingService cameraService, DatabaseService databaseService)
    {
        _cameraService = cameraService;
        _databaseService = databaseService;

        // Inicializar comandos
        StartPreviewCommand = new Command(async () => await StartPreviewAsync());
        StopPreviewCommand = new Command(async () => await StopPreviewAsync());
        StartRecordingCommand = new Command(async () => await StartRecordingAsync(), () => IsPreviewing && !IsRecording);
        StopRecordingCommand = new Command(async () => await StopRecordingAsync(), () => IsRecording);
        ToggleRecordingCommand = new Command(async () => await ToggleRecordingAsync());
        AddLapCommand = new Command(AddLap, () => IsRecording);
        AddTagCommand = new Command<Tag>(AddTag, _ => IsRecording);
        AddPenalty2sCommand = new Command(AddPenalty2s, () => IsRecording);
        AddPenalty50sCommand = new Command(AddPenalty50s, () => IsRecording);
        AddPointOfInterestCommand = new Command(AddPointOfInterest, () => IsRecording);
        SelectAthleteCommand = new Command<Athlete>(SelectAthlete);
        SelectSectionCommand = new Command<RiverSection>(SelectSection);
        MarkExecutionStartCommand = new Command(MarkExecutionStart, () => IsRecording);
        MarkExecutionEndCommand = new Command(MarkExecutionEnd, () => IsRecording);
        SwitchCameraCommand = new Command(async () => await SwitchCameraAsync());
        CloseCommand = new Command(async () => await CloseAsync());
        ToggleAthleteSelectorCommand = new Command(async () => await ToggleAthleteSelectorAsync());
        ToggleSectionSelectorCommand = new Command(() => ShowSectionSelector = !ShowSectionSelector);
        SetZoom1xCommand = new Command(() => ZoomFactor = 1.0);
        SetZoom2xCommand = new Command(() => ZoomFactor = 2.0);
    }

    #region Properties

    public CameraRecordingSession CurrentSession
    {
        get => _currentSession;
        set
        {
            if (_currentSession != value)
            {
                _currentSession = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (_isRecording != value)
            {
                _isRecording = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RecordButtonColor));
                OnPropertyChanged(nameof(RecordButtonIcon));
                UpdateCommandStates();
            }
        }
    }

    public bool IsPreviewing
    {
        get => _isPreviewing;
        private set
        {
            if (_isPreviewing != value)
            {
                _isPreviewing = value;
                OnPropertyChanged();
                UpdateCommandStates();
            }
        }
    }

    public bool IsInitializing
    {
        get => _isInitializing;
        private set
        {
            if (_isInitializing != value)
            {
                _isInitializing = value;
                OnPropertyChanged();
            }
        }
    }

    public string ElapsedTimeDisplay
    {
        get => _elapsedTimeDisplay;
        private set
        {
            if (_elapsedTimeDisplay != value)
            {
                _elapsedTimeDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    public double ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            if (Math.Abs(_zoomFactor - value) > 0.001)
            {
                _zoomFactor = Math.Clamp(value, MinZoom, MaxZoom);
                OnPropertyChanged();
                _ = _cameraService.SetZoomAsync(_zoomFactor);
                CurrentSession.CurrentZoomFactor = _zoomFactor;
            }
        }
    }

    public double MinZoom
    {
        get => _minZoom;
        private set
        {
            if (Math.Abs(_minZoom - value) > 0.001)
            {
                _minZoom = value;
                OnPropertyChanged();
            }
        }
    }

    public double MaxZoom
    {
        get => _maxZoom;
        private set
        {
            if (Math.Abs(_maxZoom - value) > 0.001)
            {
                _maxZoom = value;
                OnPropertyChanged();
            }
        }
    }

    public Athlete? SelectedAthlete
    {
        get => _selectedAthlete;
        set
        {
            if (_selectedAthlete != value)
            {
                _selectedAthlete = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedAthleteName));
            }
        }
    }

    public string SelectedAthleteName => SelectedAthlete?.NombreCompleto ?? "Sin deportista";

    public RiverSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (_selectedSection != value)
            {
                _selectedSection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedSectionName));
            }
        }
    }

    public string SelectedSectionName => SelectedSection?.Name ?? "Sin sección";

    public Tag? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (_selectedTag != value)
            {
                _selectedTag = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowAthleteSelector
    {
        get => _showAthleteSelector;
        set
        {
            if (_showAthleteSelector != value)
            {
                _showAthleteSelector = value;
                OnPropertyChanged();
                if (value)
                {
                    ShowSectionSelector = false;
                }
            }
        }
    }

    public bool ShowSectionSelector
    {
        get => _showSectionSelector;
        set
        {
            if (_showSectionSelector != value)
            {
                _showSectionSelector = value;
                OnPropertyChanged();
                if (value)
                {
                    ShowAthleteSelector = false;
                }
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage != value)
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Ángulo del nivel (línea del horizonte) en grados. 0 = horizontal.
    /// </summary>
    public double LevelAngleDegrees
    {
        get => _levelAngleDegrees;
        private set
        {
            if (Math.Abs(_levelAngleDegrees - value) > 0.05)
            {
                _levelAngleDegrees = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLevel
    {
        get => _isLevel;
        private set
        {
            if (_isLevel != value)
            {
                _isLevel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LevelIndicatorColor));
            }
        }
    }

    public string LevelIndicatorColor => IsLevel ? "#FFB300" : "#6DDDFF";

    public string RecordButtonColor => IsRecording ? "#FF3B30" : "#FFFFFF";
    public string RecordButtonIcon => IsRecording ? "stop.fill" : "record.circle";

    public ObservableCollection<Athlete> Athletes => CurrentSession.Participants;
    public ObservableCollection<RiverSection> Sections => CurrentSession.Sections;
    public ObservableCollection<Tag> Tags => CurrentSession.AvailableTags;
    public ObservableCollection<RecordingEvent> Events => CurrentSession.Events;

    public int LapCount => CurrentSession.LapCount;

    public bool IsCameraAvailable => _cameraService.IsAvailable;

    /// <summary>
    /// Handle nativo del preview de cámara para conectar con el control visual
    /// </summary>
    private object? _cameraPreviewHandle;
    public object? CameraPreviewHandle
    {
        get => _cameraPreviewHandle;
        set
        {
            if (_cameraPreviewHandle != value)
            {
                _cameraPreviewHandle = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion
    #region Commands

    public ICommand StartPreviewCommand { get; }
    public ICommand StopPreviewCommand { get; }
    public ICommand StartRecordingCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand ToggleRecordingCommand { get; }
    public ICommand AddLapCommand { get; }
    public ICommand AddTagCommand { get; }
    public ICommand AddPenalty2sCommand { get; }
    public ICommand AddPenalty50sCommand { get; }
    public ICommand AddPointOfInterestCommand { get; }
    public ICommand SelectAthleteCommand { get; }
    public ICommand SelectSectionCommand { get; }
    public ICommand MarkExecutionStartCommand { get; }
    public ICommand MarkExecutionEndCommand { get; }
    public ICommand SwitchCameraCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ToggleAthleteSelectorCommand { get; }
    public ICommand ToggleSectionSelectorCommand { get; }
    public ICommand SetZoom1xCommand { get; }
    public ICommand SetZoom2xCommand { get; }

    #endregion

    #region Methods

    private async Task ToggleAthleteSelectorAsync()
    {
        try
        {
            var shouldOpen = !ShowAthleteSelector;
            if (shouldOpen)
            {
                await EnsureAthletesLoadedAsync();
            }

            ShowAthleteSelector = shouldOpen;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error cargando deportistas: {ex.Message}";
            AppLog.Error(nameof(CameraViewModel), "Error toggling athlete selector", ex);
        }
    }

    private async Task EnsureAthletesLoadedAsync(bool forceReload = false)
    {
        if (_athletesLoaded && !forceReload)
            return;

        var athletes = await _databaseService.GetAllAthletesAsync();
        var sortedAthletes = athletes.OrderBy(a => a.NombreCompleto).ToList();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            CurrentSession.Participants.Clear();
            foreach (var athlete in sortedAthletes)
                CurrentSession.Participants.Add(athlete);

            OnPropertyChanged(nameof(Athletes));
        });

        _athletesLoaded = true;
    }

    private async Task EnsureTagsLoadedAsync(bool forceReload = false)
    {
        if (_tagsLoaded && !forceReload)
            return;

        var tags = await _databaseService.GetAllTagsAsync();
        var tagsList = tags.ToList();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            CurrentSession.AvailableTags.Clear();
            foreach (var tag in tagsList)
                CurrentSession.AvailableTags.Add(tag);

            OnPropertyChanged(nameof(Tags));
        });

        _tagsLoaded = true;
    }

    private Task EnsureSectionsLoadedAsync(bool forceReload = false)
    {
        if (_sectionsLoaded && !forceReload)
            return Task.CompletedTask;

        // Crear secciones por defecto
        var defaultSections = new List<RiverSection>
        {
            new RiverSection { Id = 1, Name = "Salida", Order = 1, Color = "#4CAF50" },
            new RiverSection { Id = 2, Name = "Tramo 1", Order = 2, Color = "#2196F3" },
            new RiverSection { Id = 3, Name = "Tramo 2", Order = 3, Color = "#FF9800" },
            new RiverSection { Id = 4, Name = "Tramo 3", Order = 4, Color = "#9C27B0" },
            new RiverSection { Id = 5, Name = "Llegada", Order = 5, Color = "#F44336" }
        };

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (CurrentSession.Sections.Count == 0 || forceReload)
            {
                CurrentSession.Sections.Clear();
                foreach (var section in defaultSections)
                    CurrentSession.Sections.Add(section);

                OnPropertyChanged(nameof(Sections));
            }
        });

        _sectionsLoaded = true;
        return Task.CompletedTask;
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // Esperar un poco para asegurar que las vistas estén listas
            await Task.Delay(100);

            await EnsureAthletesLoadedAsync();
            await EnsureTagsLoadedAsync();
            await EnsureSectionsLoadedAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error cargando datos: {ex.Message}";
            AppLog.Error(nameof(CameraViewModel), "Error loading data", ex);
        }
    }

    public async Task InitializeAsync()
    {
        IsInitializing = true;
        ErrorMessage = null;

        try
        {
            if (!_cameraService.IsAvailable)
            {
                ErrorMessage = "La cámara no está disponible en este dispositivo";
                return;
            }

            MinZoom = _cameraService.MinZoom;
            MaxZoom = _cameraService.MaxZoom;
            ZoomFactor = 1.0;

            StartLevelMonitoring();

            await StartPreviewAsync();
            
            // Cargar datos después de que la vista esté lista
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error inicializando cámara: {ex.Message}";
            AppLog.Error(nameof(CameraViewModel), "Error initializing camera", ex);
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private void StartLevelMonitoring()
    {
        if (_isLevelMonitoring)
            return;

        _isLevelMonitoring = true;
        try
        {
            _displayRotation = DeviceDisplay.MainDisplayInfo.Rotation;
            DeviceDisplay.MainDisplayInfoChanged += OnMainDisplayInfoChanged;

            if (Accelerometer.Default.IsSupported)
            {
                Accelerometer.Default.ReadingChanged += OnAccelerometerReadingChanged;
                Accelerometer.Default.Start(SensorSpeed.Game);
            }
        }
        catch
        {
            // Si el sensor falla, no bloqueamos la cámara.
        }
    }

    private void StopLevelMonitoring()
    {
        if (!_isLevelMonitoring)
            return;

        _isLevelMonitoring = false;
        try
        {
            DeviceDisplay.MainDisplayInfoChanged -= OnMainDisplayInfoChanged;

            if (Accelerometer.Default.IsSupported)
            {
                Accelerometer.Default.ReadingChanged -= OnAccelerometerReadingChanged;
                Accelerometer.Default.Stop();
            }
        }
        catch
        {
            // Ignorar.
        }
    }

    private void OnMainDisplayInfoChanged(object? sender, DisplayInfoChangedEventArgs e)
    {
        _displayRotation = e.DisplayInfo.Rotation;
    }

    private void OnAccelerometerReadingChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        // Proyección de la gravedad en el plano de la pantalla (x,y) y rotación según orientación actual.
        var x = e.Reading.Acceleration.X;
        var y = e.Reading.Acceleration.Y;

        var (gx, gy) = RotateToScreen(x, y, _displayRotation);

        // Ángulo del vector gravedad respecto al eje X de pantalla.
        var gravityAngle = Math.Atan2(gy, gx) * 180.0 / Math.PI;
        var horizonAngle = gravityAngle + 90.0;

        // Normalizar a (-180, 180]
        horizonAngle = ((horizonAngle + 180.0) % 360.0) - 180.0;

        // Mantenerlo cercano a horizontal para evitar saltos visuales.
        if (horizonAngle > 90.0) horizonAngle -= 180.0;
        if (horizonAngle < -90.0) horizonAngle += 180.0;

        var isLevelNow = Math.Abs(horizonAngle) <= 1.0;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            LevelAngleDegrees = horizonAngle;
            IsLevel = isLevelNow;
        });
    }

    private static (double x, double y) RotateToScreen(double x, double y, DisplayRotation rotation)
    {
        return rotation switch
        {
            DisplayRotation.Rotation90 => (y, -x),
            DisplayRotation.Rotation180 => (-x, -y),
            DisplayRotation.Rotation270 => (-y, x),
            _ => (x, y)
        };
    }

    /// <summary>
    /// Configura la sesión con los parámetros de navegación
    /// </summary>
    public void SetSessionInfo(int sessionId, string? sessionName, string? sessionType, string? place, DateTime date)
    {
        CurrentSession.DatabaseSessionId = sessionId > 0 ? sessionId : null;
        CurrentSession.SessionName = sessionName ?? $"Sesión {DateTime.Now:dd/MM/yyyy HH:mm}";
        CurrentSession.SessionType = sessionType ?? "Entrenamiento";
        CurrentSession.Place = place;
        CurrentSession.StartTime = date != default ? date : DateTime.Now;
        
        OnPropertyChanged(nameof(CurrentSession));
    }

    /// <summary>
    /// Indica si la grabación se asociará a una sesión existente
    /// </summary>
    public bool HasDatabaseSession => CurrentSession.DatabaseSessionId.HasValue;

    /// <summary>
    /// Nombre de la sesión para mostrar en la UI
    /// </summary>
    public string SessionDisplayName => CurrentSession.SessionName;

    private async Task StartPreviewAsync()
    {
        try
        {
            ErrorMessage = null;
            var success = await _cameraService.StartPreviewAsync();
            IsPreviewing = success;

            if (success)
            {
                // Obtener el handle del preview y notificar a la UI
                CameraPreviewHandle = _cameraService.GetPreviewHandle();
                AppLog.Info(nameof(CameraViewModel), $"Preview iniciado, handle: {CameraPreviewHandle?.GetType().Name ?? "null"}");
            }
            else
            {
                ErrorMessage = "No se pudo iniciar la vista previa de la cámara";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error iniciando preview: {ex.Message}";
            AppLog.Error(nameof(CameraViewModel), "Error starting preview", ex);
        }
    }

    private async Task StopPreviewAsync()
    {
        try
        {
            await _cameraService.StopPreviewAsync();
            IsPreviewing = false;
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(CameraViewModel), "Error stopping preview", ex);
        }
    }

    private async Task StartRecordingAsync()
    {
        if (IsRecording) return;

        try
        {
            ErrorMessage = null;

            // Limpiar eventos de grabaciones anteriores
            CurrentSession.Events.Clear();

            // Generar ruta del archivo
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"CrownSession_{timestamp}.mp4";
            var outputPath = Path.Combine(FileSystem.AppDataDirectory, "Recordings", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            CurrentSession.VideoFilePath = outputPath;
            CurrentSession.StartTime = DateTime.Now;

            await _cameraService.StartRecordingAsync(outputPath);

            IsRecording = true;
            CurrentSession.IsRecording = true;

            // Agregar evento de inicio
            CurrentSession.AddEvent(RecordingEventType.Start);

            // Iniciar cronómetro
            StartTimer();

            AppLog.Info(nameof(CameraViewModel), $"Recording started: {outputPath}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error iniciando grabación: {ex.Message}";
            AppLog.Error(nameof(CameraViewModel), "Error starting recording", ex);
        }
    }

    private async Task StopRecordingAsync()
    {
        if (!IsRecording) return;

        try
        {
            // Agregar evento de fin
            CurrentSession.AddEvent(RecordingEventType.Stop);

            // Detener cronómetro
            StopTimer();

            // Detener grabación
            var filePath = await _cameraService.StopRecordingAsync();

            CurrentSession.EndTime = DateTime.Now;
            IsRecording = false;
            CurrentSession.IsRecording = false;

            AppLog.Info(nameof(CameraViewModel), $"Recording stopped: {filePath}");

            // Guardar el video en la base de datos
            if (!string.IsNullOrEmpty(filePath) && CurrentSession.DatabaseSessionId.HasValue)
            {
                await SaveVideoToDatabase(filePath);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error deteniendo grabación: {ex.Message}";
            AppLog.Error(nameof(CameraViewModel), "Error stopping recording", ex);
        }
    }

    /// <summary>
    /// Guarda el video grabado en la base de datos
    /// </summary>
    private async Task SaveVideoToDatabase(string filePath)
    {
        try
        {
            if (!CurrentSession.DatabaseSessionId.HasValue)
            {
                AppLog.Warn(nameof(CameraViewModel), "No database session ID, cannot save video");
                return;
            }

            // Obtener información del archivo
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                AppLog.Error(nameof(CameraViewModel), $"Video file not found: {filePath}");
                return;
            }

            // Crear el VideoClip
            var videoClip = new VideoClip
            {
                SessionId = CurrentSession.DatabaseSessionId.Value,
                AtletaId = SelectedAthlete?.Id ?? 0,
                Section = SelectedSection?.Id ?? 0,
                CreationDate = DateTimeOffset.Now.ToUnixTimeSeconds(),
                LocalClipPath = filePath,
                ClipDuration = CurrentSession.ElapsedMilliseconds / 1000.0,
                ClipSize = fileInfo.Length
            };

            // Generar thumbnail
            var thumbnailPath = await GenerateThumbnailAsync(filePath);
            if (!string.IsNullOrEmpty(thumbnailPath))
            {
                videoClip.LocalThumbnailPath = thumbnailPath;
            }

            // Guardar en la base de datos
            var videoId = await _databaseService.InsertVideoClipAsync(videoClip);

            // Guardar eventos con timestamp en las mismas tablas que SinglePlayer:
            // - Catálogo en event_tags (EventTagDefinition)
            // - Ocurrencias en input (IsEvent=1)
            await PersistEventTagsToDatabaseAsync(videoId, videoClip);
            
            AppLog.Info(nameof(CameraViewModel), $"Video saved to database: ID={videoId}, Session={CurrentSession.DatabaseSessionId}, Path={filePath}");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(CameraViewModel), $"Error saving video to database: {ex.Message}", ex);
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlert("Error", $"No se pudo guardar el video: {ex.Message}", "OK");
            });
        }
    }

    private async Task PersistEventTagsToDatabaseAsync(int videoId, VideoClip videoClip)
    {
        try
        {
            // Mismo flujo que SinglePlayer: AddTagEventAsync -> tabla input (IsEvent=1) referenciando event_tags.
            // En cámara, persistimos solo eventos que representan marcadores útiles en timeline.
            var sessionId = videoClip.SessionId;
            var fallbackAthleteId = videoClip.AtletaId;

            if (videoId <= 0)
                return;

            var eventTagIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<int, int>? systemPenaltyIdBySeconds = null;

            foreach (var ev in CurrentSession.Events)
            {
                if (ev.EventType is RecordingEventType.Start or RecordingEventType.Stop or RecordingEventType.AthleteChange or RecordingEventType.SectionChange)
                    continue;

                var rawName = ev.EventType switch
                {
                    RecordingEventType.Lap => "Lap",
                    RecordingEventType.ExecutionStart => "Inicio ejecución",
                    RecordingEventType.ExecutionEnd => "Fin ejecución",
                    RecordingEventType.Tag => ev.TagName ?? ev.Label ?? "Evento",
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(rawName))
                    continue;

                var normalizedName = rawName.Trim();

                int? penaltySeconds = null;
                if (normalizedName.StartsWith("+2", StringComparison.OrdinalIgnoreCase))
                    penaltySeconds = 2;
                else if (normalizedName.StartsWith("+50", StringComparison.OrdinalIgnoreCase))
                    penaltySeconds = 50;

                if (penaltySeconds.HasValue)
                {
                    systemPenaltyIdBySeconds ??= (await _databaseService.GetSystemEventTagsAsync())
                        .Where(t => t.IsSystem && t.PenaltySeconds > 0)
                        .GroupBy(t => t.PenaltySeconds)
                        .ToDictionary(g => g.Key, g => g.First().Id);

                    if (systemPenaltyIdBySeconds.TryGetValue(penaltySeconds.Value, out var systemId) && systemId > 0)
                    {
                        normalizedName = penaltySeconds.Value == 2 ? "+2" : "+50";
                        eventTagIdByName[normalizedName] = systemId;
                    }
                }

                if (!eventTagIdByName.TryGetValue(normalizedName, out var eventTagId) || eventTagId <= 0)
                {
                    var existing = await _databaseService.FindEventTagByNameAsync(normalizedName);
                    if (existing != null)
                    {
                        eventTagId = existing.Id;
                    }
                    else
                    {
                        var created = new EventTagDefinition { Nombre = normalizedName };
                        eventTagId = await _databaseService.InsertEventTagAsync(created);
                    }

                    if (eventTagId > 0)
                        eventTagIdByName[normalizedName] = eventTagId;
                }

                if (eventTagId <= 0)
                    continue;

                var athleteId = ev.AthleteId ?? fallbackAthleteId;
                var timestampMs = ev.ElapsedMilliseconds;
                if (timestampMs < 0)
                    timestampMs = 0;

                await _databaseService.AddTagEventAsync(
                    videoId,
                    eventTagId,
                    timestampMs,
                    sessionId,
                    athleteId);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn(nameof(CameraViewModel), $"Error persisting event tags: {ex.Message}");
        }
    }

    /// <summary>
    /// Genera un thumbnail para el video
    /// </summary>
    private async Task<string?> GenerateThumbnailAsync(string videoPath)
    {
        try
        {
            // Crear directorio para thumbnails si no existe
            var thumbnailDir = Path.Combine(FileSystem.AppDataDirectory, "Thumbnails");
            if (!Directory.Exists(thumbnailDir))
            {
                Directory.CreateDirectory(thumbnailDir);
            }

            var thumbnailPath = Path.Combine(thumbnailDir, $"{Path.GetFileNameWithoutExtension(videoPath)}_thumb.jpg");

            // Intentar generar thumbnail usando el servicio de thumbnails si existe
            var thumbnailService = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<Services.ThumbnailService>();
            if (thumbnailService != null)
            {
                var success = await thumbnailService.GenerateThumbnailAsync(videoPath, thumbnailPath);
                if (success)
                {
                    return thumbnailPath;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warn(nameof(CameraViewModel), $"Could not generate thumbnail: {ex.Message}");
            return null;
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
            await StopRecordingAsync();
        else
            await StartRecordingAsync();
    }

    private void AddLap()
    {
        if (!IsRecording) return;

        CurrentSession.AddLap();
        OnPropertyChanged(nameof(LapCount));

        AppLog.Info(nameof(CameraViewModel), $"Lap added at {CurrentSession.FormattedElapsedTime}");
    }

    private void AddTag(Tag? tag)
    {
        if (!IsRecording || tag == null) return;

        CurrentSession.AddTag(tag);

        AppLog.Info(nameof(CameraViewModel), $"Tag added: {tag.NombreTag} at {CurrentSession.FormattedElapsedTime}");
    }

    private void AddPenalty2s()
    {
        if (!IsRecording) return;

        var penaltyTag = new Tag { NombreTag = "+2s" };
        CurrentSession.AddTag(penaltyTag);

        AppLog.Info(nameof(CameraViewModel), $"Penalty +2s added at {CurrentSession.FormattedElapsedTime}");
    }

    private void AddPenalty50s()
    {
        if (!IsRecording) return;

        var penaltyTag = new Tag { NombreTag = "+50s" };
        CurrentSession.AddTag(penaltyTag);

        AppLog.Info(nameof(CameraViewModel), $"Penalty +50s added at {CurrentSession.FormattedElapsedTime}");
    }

    private void AddPointOfInterest()
    {
        if (!IsRecording) return;

        var poiTag = new Tag { NombreTag = "POI" };
        CurrentSession.AddTag(poiTag);

        AppLog.Info(nameof(CameraViewModel), $"Point of interest added at {CurrentSession.FormattedElapsedTime}");
    }

    private void SelectAthlete(Athlete? athlete)
    {
        if (athlete == null) return;

        SelectedAthlete = athlete;
        
        if (IsRecording)
        {
            CurrentSession.ChangeAthlete(athlete);
        }
        else
        {
            CurrentSession.CurrentAthleteId = athlete.Id;
            CurrentSession.CurrentAthleteName = athlete.NombreCompleto;
        }

        ShowAthleteSelector = false;
        AppLog.Info(nameof(CameraViewModel), $"Athlete selected: {athlete.NombreCompleto}");
    }

    private void SelectSection(RiverSection? section)
    {
        if (section == null) return;

        // Deseleccionar otras secciones
        foreach (var s in Sections)
            s.IsSelected = false;

        section.IsSelected = true;
        SelectedSection = section;

        if (IsRecording)
        {
            CurrentSession.ChangeSection(section);
        }
        else
        {
            CurrentSession.CurrentSectionId = section.Id;
            CurrentSession.CurrentSectionName = section.Name;
        }

        ShowSectionSelector = false;
        AppLog.Info(nameof(CameraViewModel), $"Section selected: {section.Name}");
    }

    private void MarkExecutionStart()
    {
        if (!IsRecording) return;

        CurrentSession.AddEvent(RecordingEventType.ExecutionStart);
        AppLog.Info(nameof(CameraViewModel), $"Execution start marked at {CurrentSession.FormattedElapsedTime}");
    }

    private void MarkExecutionEnd()
    {
        if (!IsRecording) return;

        CurrentSession.AddEvent(RecordingEventType.ExecutionEnd);
        AppLog.Info(nameof(CameraViewModel), $"Execution end marked at {CurrentSession.FormattedElapsedTime}");
    }

    private async Task SwitchCameraAsync()
    {
        try
        {
            await _cameraService.SwitchCameraAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(CameraViewModel), "Error switching camera", ex);
        }
    }

    private async Task CloseAsync()
    {
        if (IsRecording)
        {
            // TODO: Preguntar si desea guardar la grabación
            await StopRecordingAsync();
        }

        await StopPreviewAsync();
        await Shell.Current.GoToAsync("..");
    }

    private void StartTimer()
    {
        _stopwatch.Restart();

        _timer = Application.Current?.Dispatcher.CreateTimer();
        if (_timer != null)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(10);
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
    }

    private void StopTimer()
    {
        _timer?.Stop();
        if (_timer != null)
            _timer.Tick -= OnTimerTick;
        _timer = null;
        _stopwatch.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var elapsed = _stopwatch.ElapsedMilliseconds;
        CurrentSession.ElapsedMilliseconds = elapsed;
        ElapsedTimeDisplay = CurrentSession.FormattedElapsedTime;
    }

    private void UpdateCommandStates()
    {
        (StartRecordingCommand as Command)?.ChangeCanExecute();
        (StopRecordingCommand as Command)?.ChangeCanExecute();
        (AddLapCommand as Command)?.ChangeCanExecute();
        (MarkExecutionStartCommand as Command)?.ChangeCanExecute();
        (MarkExecutionEndCommand as Command)?.ChangeCanExecute();
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopLevelMonitoring();
        StopTimer();
        _ = _cameraService.DisposeAsync();
    }

    /// <summary>
    /// Libera los recursos de manera asíncrona, esperando a que se complete
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            StopLevelMonitoring();
            StopTimer();
            
            // Limpiar el preview handle
            CameraPreviewHandle = null;
            
            // Esperar a que el servicio de cámara se libere completamente
            await _cameraService.DisposeAsync();
            
            AppLog.Info(nameof(CameraViewModel), "Camera resources disposed");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(CameraViewModel), "Error disposing camera resources", ex);
        }
    }

    #endregion
}
