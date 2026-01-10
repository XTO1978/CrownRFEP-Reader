using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using Microsoft.Maui.ApplicationModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para el reproductor de vídeo individual con control preciso frame-by-frame.
/// Incluye sistema de filtrado y playlist para navegar entre videos de una sesión.
/// </summary>
[QueryProperty(nameof(VideoPath), "videoPath")]
public class SinglePlayerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly DatabaseService _databaseService;
    
    private string _videoPath = "";
    private string _videoTitle = "";
    private bool _isPlaying;
    private bool _isMuted = true; // Silenciado por defecto
    private TimeSpan _currentPosition;
    private TimeSpan _duration;
    private double _progress;
    private double _playbackSpeed = 1.0;
    
    // Flag para evitar actualizar Progress mientras el usuario arrastra el slider
    private bool _isDraggingSlider;
    
    // Información del video para el overlay
    private VideoClip? _videoClip;
    private bool _showOverlay = true;
    
    // Sistema de filtrado y playlist
    private List<VideoClip> _sessionVideos = new();
    private List<VideoClip> _filteredPlaylist = new();
    private int _currentPlaylistIndex;
    private bool _showFilters;
    
    // Opciones de filtro
    private ObservableCollection<FilterOption<Athlete>> _athleteOptions = new();
    private ObservableCollection<FilterOption<int>> _sectionOptions = new();
    private ObservableCollection<FilterOption<Category>> _categoryOptions = new();
    
    // Selecciones de filtro
    private FilterOption<Athlete>? _selectedAthlete;
    private FilterOption<int>? _selectedSection;
    private FilterOption<Category>? _selectedCategory;
    
    // Asignación de atleta al video
    private bool _showAthleteAssignPanel;
    private ObservableCollection<Athlete> _allAthletes = new();
    private Athlete? _selectedAthleteToAssign;
    private string _newAthleteName = "";
    private string _newAthleteSurname = "";
    
    // Asignación de sección/tramo
    private bool _showSectionAssignPanel;
    private int _sectionToAssign = 1;
    
    // Asignación de etiquetas
    private bool _showTagsAssignPanel;
    private ObservableCollection<Tag> _allTags = new();
    private ObservableCollection<Tag> _selectedTags = new();
    private string _newTagName = "";
    
    // Eventos de etiquetas con timestamps
    private bool _showTagEventsPanel;
    private ObservableCollection<EventTagDefinition> _allEventTags = new();
    private ObservableCollection<TagEvent> _tagEvents = new();
    private ObservableCollection<TimelineMarker> _timelineMarkers = new();
    private EventTagDefinition? _selectedEventTagToAdd;
    private string _newEventName = "";
    
    // Medidor de tiempo (Split Time)
    private bool _showSplitTimePanel;
    private TimeSpan? _splitStartTime;
    private TimeSpan? _splitEndTime;
    private TimeSpan? _splitDuration;
    private bool _hasSavedSplit;

    // Laps dentro del Split Time (se guardan en execution_timing_events, igual que en Camera)
    private readonly ObservableCollection<ExecutionTimingRow> _splitLapRows = new();
    private readonly List<long> _splitLapMarksMs = new();
    private bool _hasSplitLaps;
    
    // Modo de toma de parciales asistido
    private bool _isAssistedModeEnabled;
    private AssistedLapState _assistedLapState = AssistedLapState.Configuring;
    private readonly ObservableCollection<AssistedLapDefinition> _assistedLaps = new();
    private int _assistedLapCount = 3;
    private int _currentAssistedLapIndex = 0;
    
    // Historial de configuraciones de parciales
    private readonly ObservableCollection<LapConfigHistory> _recentLapConfigs = new();

    public SinglePlayerViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        
        // Comandos de reproducción
        PlayPauseCommand = new Command(TogglePlayPause);
        StopCommand = new Command(Stop);
        SeekBackwardCommand = new Command(() => Seek(-5));
        SeekForwardCommand = new Command(() => Seek(5));
        FrameBackwardCommand = new Command(StepBackward);
        FrameForwardCommand = new Command(StepForward);
        SetSpeedCommand = new Command<string>(SetSpeed);
        ToggleOverlayCommand = new Command(() => ShowOverlay = !ShowOverlay);
        ToggleMuteCommand = new Command(() => IsMuted = !IsMuted);
        
        // Comandos de navegación de playlist
        PreviousVideoCommand = new Command(GoToPreviousVideo, () => CanGoPrevious);
        NextVideoCommand = new Command(GoToNextVideo, () => CanGoNext);
        ToggleFiltersCommand = new Command(() => ShowFilters = !ShowFilters);
        ClearFiltersCommand = new Command(ClearFilters);
        
        // Comandos de asignación de atleta
        ToggleAthleteAssignPanelCommand = new Command(async () => await ToggleAthleteAssignPanelAsync());
        AssignAthleteCommand = new Command(async () => await AssignAthleteAsync(), () => _selectedAthleteToAssign != null);
        CreateAndAssignAthleteCommand = new Command(async () => await CreateAndAssignAthleteAsync(), () => !string.IsNullOrWhiteSpace(_newAthleteName) || !string.IsNullOrWhiteSpace(_newAthleteSurname));
        SelectAthleteToAssignCommand = new Command<Athlete>(SelectAthleteToAssign);
        
        // Comandos de asignación de sección
        ToggleSectionAssignPanelCommand = new Command(() => {
            var wasOpen = ShowSectionAssignPanel;
            // Cerrar todos los paneles primero
            CloseAllPanels();
            // Toggle este panel
            ShowSectionAssignPanel = !wasOpen;
            if (ShowSectionAssignPanel && _videoClip != null)
                SectionToAssign = _videoClip.Section > 0 ? _videoClip.Section : 1;
        });
        AssignSectionCommand = new Command(async () => await AssignSectionAsync());
        DecrementSectionCommand = new Command(() =>
        {
            if (SectionToAssign > 1)
                SectionToAssign -= 1;
        });
        IncrementSectionCommand = new Command(() =>
        {
            if (SectionToAssign < 99)
                SectionToAssign += 1;
        });
        
        // Comandos de asignación de etiquetas
        ToggleTagsAssignPanelCommand = new Command(async () => await ToggleTagsAssignPanelAsync());
        ToggleTagSelectionCommand = new Command<Tag>(ToggleTagSelection);
        SaveTagsCommand = new Command(async () => await SaveTagsAsync());
        CreateAndAddTagCommand = new Command(async () => await CreateAndAddTagAsync(), () => !string.IsNullOrWhiteSpace(_newTagName));
        RemoveAssignedTagCommand = new Command<Tag>(async (t) => await RemoveAssignedTagAsync(t));
        RemoveEventTagCommand = new Command<Tag>(async (t) => await RemoveEventTagAsync(t));
        DeleteTagFromListCommand = new Command<Tag>(async (t) => await DeleteTagFromListAsync(t));
        
        // Comandos de eventos de etiquetas (con timestamps)
        ToggleTagEventsPanelCommand = new Command(async () => await ToggleTagEventsPanelAsync());
        AddTagEventCommand = new Command(async () => await AddTagEventAsync(), () => _selectedEventTagToAdd != null);
        DeleteTagEventCommand = new Command<TagEvent>(async (e) => await DeleteTagEventAsync(e));
        SeekToTagEventCommand = new Command<TagEvent>(SeekToTagEvent);
        SeekToTimelineMarkerCommand = new Command<TimelineMarker>(SeekToTimelineMarker);
        SelectTagToAddCommand = new Command<EventTagDefinition>(SelectTagToAdd);
        CreateAndAddEventCommand = new Command(async () => await CreateAndAddEventAsync(), () => !string.IsNullOrWhiteSpace(_newEventName));
        DeleteEventTagFromListCommand = new Command<EventTagDefinition>(async (t) => await DeleteEventTagFromListAsync(t));
        
        // Comandos de Split Time
        ToggleSplitTimePanelCommand = new Command(ToggleSplitTimePanel);
        SetSplitStartCommand = new Command(SetSplitStart);
        SetSplitEndCommand = new Command(SetSplitEnd);
        AddSplitLapCommand = new Command(AddSplitLap);
        ClearSplitCommand = new Command(ClearSplit);
        SaveSplitCommand = new Command(async () => await SaveSplitAsync(), () => CanSaveSplit);
        
        // Comandos del modo asistido
        ToggleAssistedModeCommand = new Command(ToggleAssistedMode);
        IncrementAssistedLapCountCommand = new Command(() => AssistedLapCount = Math.Min(10, AssistedLapCount + 1));
        DecrementAssistedLapCountCommand = new Command(() => AssistedLapCount = Math.Max(1, AssistedLapCount - 1));
        StartAssistedCaptureCommand = new Command(StartAssistedCapture, () => IsAssistedModeEnabled && AssistedLapState == AssistedLapState.Configuring);
        MarkAssistedPointCommand = new Command(MarkAssistedPoint, () => IsAssistedModeEnabled && AssistedLapState != AssistedLapState.Configuring && AssistedLapState != AssistedLapState.Completed);
        ResetAssistedCaptureCommand = new Command(ResetAssistedCapture);
        ApplyLapConfigCommand = new Command<LapConfigHistory>(ApplyLapConfig);
        
        // Cargar historial de configuraciones
        _ = LoadRecentLapConfigsAsync();
    }

    #region Propiedades

    public string VideoPath
    {
        get => _videoPath;
        set
        {
            var decodedPath = Uri.UnescapeDataString(value ?? "");
            if (_videoPath != decodedPath)
            {
                _videoPath = decodedPath;
                OnPropertyChanged();
                VideoTitle = Path.GetFileNameWithoutExtension(decodedPath);
            }
        }
    }

    public string VideoTitle
    {
        get => _videoTitle;
        set { _videoTitle = value; OnPropertyChanged(); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayPauseIcon));
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MuteIcon));
            }
        }
    }

    public string MuteIcon => IsMuted ? "speaker.slash.fill" : "speaker.wave.2.fill";

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
            RefreshTimelineMarkers();
        }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }
    
    /// <summary>
    /// Flag que indica si el usuario está arrastrando el slider.
    /// Cuando está activo, UpdateProgress() no actualiza Progress para evitar conflictos.
    /// </summary>
    public bool IsDraggingSlider
    {
        get => _isDraggingSlider;
        set => _isDraggingSlider = value;
    }

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            _playbackSpeed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpeedText));
            SpeedChangeRequested?.Invoke(this, value);
        }
    }

    public string CurrentPositionText => $"{CurrentPosition:mm\\:ss\\.ff}";
    public string DurationText => $"{Duration:mm\\:ss\\.ff}";
    public string SpeedText => $"{PlaybackSpeed:0.#}x";
    public string PlayPauseIcon => IsPlaying ? "pause.fill" : "play.fill";

    #endregion

    #region Propiedades del overlay de información

    public VideoClip? VideoClip
    {
        get => _videoClip;
        set
        {
            _videoClip = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVideoInfo));
            OnPropertyChanged(nameof(AthleteName));
            OnPropertyChanged(nameof(SessionName));
            OnPropertyChanged(nameof(SessionPlace));
            OnPropertyChanged(nameof(SessionDate));
            OnPropertyChanged(nameof(SectionText));
            OnPropertyChanged(nameof(VideoDurationText));
            OnPropertyChanged(nameof(VideoSizeText));
            OnPropertyChanged(nameof(HasBadge));
            OnPropertyChanged(nameof(BadgeText));
            OnPropertyChanged(nameof(BadgeColor));
            OnPropertyChanged(nameof(HasTags));
            OnPropertyChanged(nameof(TagsText));
            OnPropertyChanged(nameof(VideoClipTags));
            OnPropertyChanged(nameof(HasCurrentAthlete));
            OnPropertyChanged(nameof(CurrentAthleteText));
            OnPropertyChanged(nameof(HasTagEvents));
            OnPropertyChanged(nameof(TagEventsCountText));
            OnPropertyChanged(nameof(IsComparisonVideo));
            OnPropertyChanged(nameof(VideoAspect));
            
            // Actualizar el path del video si viene en el clip
            if (value?.LocalClipPath != null)
            {
                _videoPath = value.LocalClipPath;
                OnPropertyChanged(nameof(VideoPath));
            }
            
            // Actualizar título
            VideoTitle = value?.Atleta?.NombreCompleto ?? Path.GetFileNameWithoutExtension(_videoPath);
        }
    }

    public bool ShowOverlay
    {
        get => _showOverlay;
        set { _showOverlay = value; OnPropertyChanged(); }
    }

    public bool HasVideoInfo => _videoClip != null;

    /// <summary>
    /// Indica si el video es una comparación (paralelo) para ajustar el modo de aspecto
    /// </summary>
    public bool IsComparisonVideo => _videoClip?.IsComparisonVideo ?? false;
    
    /// <summary>
    /// Aspecto del video: AspectFit para comparaciones, AspectFill para videos normales
    /// </summary>
    public Aspect VideoAspect => IsComparisonVideo ? Aspect.AspectFit : Aspect.AspectFill;

    public string AthleteName => _videoClip?.Atleta?.NombreCompleto ?? "—";
    
    public string SessionName => _videoClip?.Session?.DisplayName ?? "—";
    
    public string SessionPlace => _videoClip?.Session?.Lugar ?? "—";
    
    public string SessionDate => _videoClip?.Session?.FechaDateTime.ToString("dd/MM/yyyy HH:mm") ?? "—";
    
    public string SectionText => _videoClip != null ? $"Sección {_videoClip.Section}" : "—";
    
    public string VideoDurationText => _videoClip?.DurationFormatted ?? "—";
    
    public string VideoSizeText => _videoClip?.SizeFormatted ?? "—";
    
    public bool HasBadge => !string.IsNullOrEmpty(_videoClip?.BadgeText);
    
    public string BadgeText => _videoClip?.BadgeText ?? "";
    
    public bool HasTags => _videoClip?.Tags != null && _videoClip.Tags.Count > 0;
    
    public string TagsText => _videoClip?.Tags != null && _videoClip.Tags.Count > 0
        ? string.Join(", ", _videoClip.Tags.Select(t => t.NombreTag))
        : "";
    
    /// <summary>
    /// Lista de tags asignados del video actual para mostrar en el overlay
    /// </summary>
    public List<Tag>? VideoClipTags => _videoClip?.Tags;

    /// <summary>
    /// Lista de tags de eventos del video actual para mostrar en el overlay
    /// </summary>
    public List<Tag>? VideoClipEventTags => _videoClip?.EventTags;

    /// <summary>
    /// Indica si el video tiene eventos
    /// </summary>
    public bool HasEventTags => _videoClip?.EventTags != null && _videoClip.EventTags.Count > 0;
    
    public Color BadgeColor
    {
        get
        {
            if (string.IsNullOrEmpty(_videoClip?.BadgeBackgroundColor))
                return Colors.Gray;
            
            try
            {
                return Color.FromArgb(_videoClip.BadgeBackgroundColor);
            }
            catch
            {
                return Colors.Gray;
            }
        }
    }

    #endregion

    #region Propiedades de filtrado y playlist

    public bool ShowFilters
    {
        get => _showFilters;
        set { _showFilters = value; OnPropertyChanged(); }
    }

    public ObservableCollection<FilterOption<Athlete>> AthleteOptions
    {
        get => _athleteOptions;
        set { _athleteOptions = value; OnPropertyChanged(); }
    }

    public ObservableCollection<FilterOption<int>> SectionOptions
    {
        get => _sectionOptions;
        set { _sectionOptions = value; OnPropertyChanged(); }
    }

    public ObservableCollection<FilterOption<Category>> CategoryOptions
    {
        get => _categoryOptions;
        set { _categoryOptions = value; OnPropertyChanged(); }
    }

    public FilterOption<Athlete>? SelectedAthlete
    {
        get => _selectedAthlete;
        set
        {
            _selectedAthlete = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public FilterOption<int>? SelectedSection
    {
        get => _selectedSection;
        set
        {
            _selectedSection = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public FilterOption<Category>? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    public int PlaylistCount => _filteredPlaylist.Count;
    
    public int CurrentPlaylistPosition => _filteredPlaylist.Count > 0 ? _currentPlaylistIndex + 1 : 0;
    
    public string PlaylistPositionText => _filteredPlaylist.Count > 0 
        ? $"{CurrentPlaylistPosition} / {PlaylistCount}" 
        : "—";
    
    public bool CanGoPrevious => _currentPlaylistIndex > 0;
    
    public bool CanGoNext => _currentPlaylistIndex < _filteredPlaylist.Count - 1;
    
    public bool HasPlaylist => _filteredPlaylist.Count > 1;

    // Propiedades para asignación de atleta
    public bool ShowAthleteAssignPanel
    {
        get => _showAthleteAssignPanel;
        set { _showAthleteAssignPanel = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Athlete> AllAthletes
    {
        get => _allAthletes;
        set { _allAthletes = value; OnPropertyChanged(); }
    }

    public Athlete? SelectedAthleteToAssign
    {
        get => _selectedAthleteToAssign;
        set
        {
            _selectedAthleteToAssign = value;
            OnPropertyChanged();
            ((Command)AssignAthleteCommand).ChangeCanExecute();
        }
    }

    public string NewAthleteName
    {
        get => _newAthleteName;
        set
        {
            _newAthleteName = value;
            OnPropertyChanged();
            ((Command)CreateAndAssignAthleteCommand).ChangeCanExecute();
        }
    }

    public string NewAthleteSurname
    {
        get => _newAthleteSurname;
        set
        {
            _newAthleteSurname = value;
            OnPropertyChanged();
            ((Command)CreateAndAssignAthleteCommand).ChangeCanExecute();
        }
    }

    public bool HasCurrentAthlete => _videoClip?.AtletaId > 0;

    public string CurrentAthleteText => _videoClip?.Atleta?.NombreCompleto ?? "Sin asignar";

    // Propiedades para asignación de sección
    public bool ShowSectionAssignPanel
    {
        get => _showSectionAssignPanel;
        set { _showSectionAssignPanel = value; OnPropertyChanged(); }
    }

    public int SectionToAssign
    {
        get => _sectionToAssign;
        set { _sectionToAssign = value; OnPropertyChanged(); }
    }

    public string CurrentSectionText => _videoClip?.Section > 0 ? $"Tramo {_videoClip.Section}" : "Sin asignar";

    // Propiedades para asignación de etiquetas
    public bool ShowTagsAssignPanel
    {
        get => _showTagsAssignPanel;
        set { _showTagsAssignPanel = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Tag> AllTags
    {
        get => _allTags;
        set { _allTags = value; OnPropertyChanged(); }
    }

    public ObservableCollection<EventTagDefinition> AllEventTags
    {
        get => _allEventTags;
        set { _allEventTags = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Tag> SelectedTags
    {
        get => _selectedTags;
        set { _selectedTags = value; OnPropertyChanged(); }
    }

    public string NewTagName
    {
        get => _newTagName;
        set
        {
            _newTagName = value;
            OnPropertyChanged();
            ((Command)CreateAndAddTagCommand).ChangeCanExecute();
        }
    }

    public string CurrentTagsText
    {
        get
        {
            if (_videoClip?.Tags == null || _videoClip.Tags.Count == 0)
                return "Sin etiquetas";
            return string.Join(", ", _videoClip.Tags.Select(t => t.NombreTag));
        }
    }

    // Propiedades para eventos de etiquetas con timestamps
    public bool ShowTagEventsPanel
    {
        get => _showTagEventsPanel;
        set { _showTagEventsPanel = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TagEvent> TagEvents
    {
        get => _tagEvents;
        set { _tagEvents = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TimelineMarker> TimelineMarkers
    {
        get => _timelineMarkers;
        set { _timelineMarkers = value; OnPropertyChanged(); }
    }

    public EventTagDefinition? SelectedEventTagToAdd
    {
        get => _selectedEventTagToAdd;
        set
        {
            _selectedEventTagToAdd = value;
            OnPropertyChanged();
            ((Command)AddTagEventCommand).ChangeCanExecute();
        }
    }

    public string NewEventName
    {
        get => _newEventName;
        set
        {
            _newEventName = value;
            OnPropertyChanged();
            ((Command)CreateAndAddEventCommand).ChangeCanExecute();
        }
    }

    public bool HasTagEvents => _tagEvents.Count > 0;

    public string TagEventsCountText => _tagEvents.Count == 1 ? "1 evento" : $"{_tagEvents.Count} eventos";

    // Propiedades para Split Time
    public bool ShowSplitTimePanel
    {
        get => _showSplitTimePanel;
        set { _showSplitTimePanel = value; OnPropertyChanged(); }
    }

    public TimeSpan? SplitStartTime
    {
        get => _splitStartTime;
        set
        {
            _splitStartTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SplitStartTimeText));
            OnPropertyChanged(nameof(HasSplitStart));
            CalculateSplitDuration();
            ((Command)SaveSplitCommand).ChangeCanExecute();
        }
    }

    public TimeSpan? SplitEndTime
    {
        get => _splitEndTime;
        set
        {
            _splitEndTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SplitEndTimeText));
            OnPropertyChanged(nameof(HasSplitEnd));
            CalculateSplitDuration();
            ((Command)SaveSplitCommand).ChangeCanExecute();
        }
    }

    public TimeSpan? SplitDuration
    {
        get => _splitDuration;
        private set
        {
            _splitDuration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SplitDurationText));
            OnPropertyChanged(nameof(HasSplitDuration));
        }
    }

    public bool HasSavedSplit
    {
        get => _hasSavedSplit;
        set { _hasSavedSplit = value; OnPropertyChanged(); }
    }

    public bool HasSplitStart => _splitStartTime.HasValue;
    public bool HasSplitEnd => _splitEndTime.HasValue;
    public bool HasSplitDuration => _splitDuration.HasValue;
    public bool CanSaveSplit => _splitStartTime.HasValue && _splitEndTime.HasValue && _splitDuration.HasValue && _splitDuration.Value.TotalMilliseconds > 0;

    public string SplitStartTimeText => _splitStartTime.HasValue ? $"{_splitStartTime.Value:mm\\:ss\\.ff}" : "--:--:--";
    public string SplitEndTimeText => _splitEndTime.HasValue ? $"{_splitEndTime.Value:mm\\:ss\\.ff}" : "--:--:--";
    public string SplitDurationText => _splitDuration.HasValue ? $"{_splitDuration.Value:mm\\:ss\\.fff}" : "--:--:---";

    public ObservableCollection<ExecutionTimingRow> SplitLapRows => _splitLapRows;

    public bool HasSplitLaps
    {
        get => _hasSplitLaps;
        private set
        {
            if (_hasSplitLaps != value)
            {
                _hasSplitLaps = value;
                OnPropertyChanged();
            }
        }
    }
    
    // ==================== MODO ASISTIDO ====================
    
    /// <summary>Indica si el modo de toma asistida está activado</summary>
    public bool IsAssistedModeEnabled
    {
        get => _isAssistedModeEnabled;
        set
        {
            if (_isAssistedModeEnabled != value)
            {
                _isAssistedModeEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsManualModeEnabled));
                
                // Actualizar estado de los comandos
                ((Command)StartAssistedCaptureCommand).ChangeCanExecute();
                ((Command)MarkAssistedPointCommand).ChangeCanExecute();
                
                if (value)
                {
                    // Al activar modo asistido, asegurarse de que haya parciales
                    if (_assistedLaps.Count == 0 || _assistedLaps.Count != AssistedLapCount)
                    {
                        InitializeAssistedLaps();
                    }
                }
                else
                {
                    // Al desactivar, reiniciar el estado de captura
                    ResetAssistedCapture();
                }
                
                System.Diagnostics.Debug.WriteLine($"IsAssistedModeEnabled cambiado a: {value}");
            }
        }
    }
    
    /// <summary>Indica si el modo manual está activo (inverso del asistido)</summary>
    public bool IsManualModeEnabled => !_isAssistedModeEnabled;
    
    /// <summary>Estado actual del flujo de toma asistida</summary>
    public AssistedLapState AssistedLapState
    {
        get => _assistedLapState;
        set
        {
            if (_assistedLapState != value)
            {
                _assistedLapState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AssistedStateDescription));
                OnPropertyChanged(nameof(AssistedActionButtonText));
                OnPropertyChanged(nameof(CanMarkAssistedPoint));
                OnPropertyChanged(nameof(IsAssistedConfiguring));
                OnPropertyChanged(nameof(IsAssistedCapturing));
                ((Command)StartAssistedCaptureCommand).ChangeCanExecute();
                ((Command)MarkAssistedPointCommand).ChangeCanExecute();
            }
        }
    }
    
    /// <summary>Número de parciales a configurar (1-10)</summary>
    public int AssistedLapCount
    {
        get => _assistedLapCount;
        set
        {
            var clamped = Math.Max(1, Math.Min(10, value));
            if (_assistedLapCount != clamped)
            {
                _assistedLapCount = clamped;
                OnPropertyChanged();
                InitializeAssistedLaps();
            }
        }
    }
    
    /// <summary>Lista de parciales predefinidos</summary>
    public ObservableCollection<AssistedLapDefinition> AssistedLaps => _assistedLaps;
    
    /// <summary>Índice del parcial actual a marcar (0-based)</summary>
    public int CurrentAssistedLapIndex
    {
        get => _currentAssistedLapIndex;
        private set
        {
            _currentAssistedLapIndex = value;
            OnPropertyChanged();
            UpdateCurrentLapHighlight();
        }
    }
    
    /// <summary>Descripción del estado actual para guiar al usuario</summary>
    public string AssistedStateDescription => AssistedLapState switch
    {
        AssistedLapState.Configuring => "Configura los parciales y pulsa 'Iniciar toma'",
        AssistedLapState.WaitingForStart => "▶ Pulsa el botón para marcar el INICIO",
        AssistedLapState.MarkingLaps => CurrentAssistedLapIndex < _assistedLaps.Count 
            ? $"▶ Marca el parcial: {_assistedLaps[CurrentAssistedLapIndex].DisplayName}" 
            : "▶ Pulsa para marcar el FIN",
        AssistedLapState.WaitingForEnd => "▶ Pulsa el botón para marcar el FIN",
        AssistedLapState.Completed => "✓ Toma completada. Guarda o reinicia.",
        _ => ""
    };
    
    /// <summary>Texto del botón de acción según el estado</summary>
    public string AssistedActionButtonText => AssistedLapState switch
    {
        AssistedLapState.WaitingForStart => "INICIO",
        AssistedLapState.MarkingLaps => CurrentAssistedLapIndex < _assistedLaps.Count 
            ? _assistedLaps[CurrentAssistedLapIndex].DisplayName 
            : "FIN",
        AssistedLapState.WaitingForEnd => "FIN",
        _ => "MARCAR"
    };
    
    /// <summary>Indica si se puede marcar un punto en el estado actual</summary>
    public bool CanMarkAssistedPoint => IsAssistedModeEnabled && 
        AssistedLapState != AssistedLapState.Configuring && 
        AssistedLapState != AssistedLapState.Completed;
    
    /// <summary>Indica si está en modo configuración</summary>
    public bool IsAssistedConfiguring => AssistedLapState == AssistedLapState.Configuring;
    
    /// <summary>Indica si está capturando (no configurando ni completado)</summary>
    public bool IsAssistedCapturing => AssistedLapState != AssistedLapState.Configuring && 
        AssistedLapState != AssistedLapState.Completed;
    
    /// <summary>Historial de configuraciones de parciales recientes</summary>
    public ObservableCollection<LapConfigHistory> RecentLapConfigs => _recentLapConfigs;
    
    /// <summary>Indica si hay configuraciones recientes disponibles</summary>
    public bool HasRecentLapConfigs => _recentLapConfigs.Count > 0;

    #endregion

    #region Comandos

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand FrameBackwardCommand { get; }
    public ICommand FrameForwardCommand { get; }
    public ICommand SetSpeedCommand { get; }
    public ICommand ToggleOverlayCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    
    // Comandos de navegación de playlist
    public ICommand PreviousVideoCommand { get; }
    public ICommand NextVideoCommand { get; }
    public ICommand ToggleFiltersCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    
    // Comandos de asignación de atleta
    public ICommand ToggleAthleteAssignPanelCommand { get; }
    public ICommand AssignAthleteCommand { get; }
    public ICommand CreateAndAssignAthleteCommand { get; }
    public ICommand SelectAthleteToAssignCommand { get; }
    
    // Comandos de asignación de sección
    public ICommand ToggleSectionAssignPanelCommand { get; }
    public ICommand AssignSectionCommand { get; }
    public ICommand DecrementSectionCommand { get; }
    public ICommand IncrementSectionCommand { get; }
    
    // Comandos de asignación de etiquetas
    public ICommand ToggleTagsAssignPanelCommand { get; }
    public ICommand ToggleTagSelectionCommand { get; }
    public ICommand SaveTagsCommand { get; }
    public ICommand CreateAndAddTagCommand { get; }
    public ICommand RemoveAssignedTagCommand { get; }
    public ICommand RemoveEventTagCommand { get; }
    public ICommand DeleteTagFromListCommand { get; }
    
    // Comandos de eventos de etiquetas con timestamps
    public ICommand ToggleTagEventsPanelCommand { get; }
    public ICommand AddTagEventCommand { get; }
    public ICommand DeleteTagEventCommand { get; }
    public ICommand SeekToTagEventCommand { get; }
    public ICommand SeekToTimelineMarkerCommand { get; }
    public ICommand SelectTagToAddCommand { get; }
    public ICommand CreateAndAddEventCommand { get; }
    public ICommand DeleteEventTagFromListCommand { get; }
    
    // Comandos de Split Time
    public ICommand ToggleSplitTimePanelCommand { get; }
    public ICommand SetSplitStartCommand { get; }
    public ICommand SetSplitEndCommand { get; }
    public ICommand AddSplitLapCommand { get; }
    public ICommand ClearSplitCommand { get; }
    public ICommand SaveSplitCommand { get; }
    
    // Comandos del modo asistido
    public ICommand ToggleAssistedModeCommand { get; }
    public ICommand IncrementAssistedLapCountCommand { get; }
    public ICommand DecrementAssistedLapCountCommand { get; }
    public ICommand StartAssistedCaptureCommand { get; }
    public ICommand MarkAssistedPointCommand { get; }
    public ICommand ResetAssistedCaptureCommand { get; }
    public ICommand ApplyLapConfigCommand { get; }

    #endregion

    #region Eventos

    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? FrameForwardRequested;
    public event EventHandler? FrameBackwardRequested;
    public event EventHandler<double>? SpeedChangeRequested;
    
    /// <summary>
    /// Se dispara cuando cambia el video actual (navegación en playlist)
    /// </summary>
    public event EventHandler<VideoClip>? VideoChanged;

    #endregion

    #region Métodos públicos

    public void SeekToPosition(double normalizedPosition)
    {
        var newPosition = normalizedPosition * Duration.TotalSeconds;
        SeekRequested?.Invoke(this, newPosition);
    }

    /// <summary>
    /// Inicializa el ViewModel con una playlist de videos (para reproducción secuencial)
    /// </summary>
    public async Task InitializeWithPlaylistAsync(List<VideoClip> playlist, int startIndex = 0)
    {
        if (playlist == null || playlist.Count == 0)
            return;
        
        // Establecer la playlist directamente sin cargar datos de sesión
        _sessionVideos = playlist;
        _filteredPlaylist = playlist.OrderBy(v => v.CreationDate).ToList();
        _currentPlaylistIndex = Math.Max(0, Math.Min(startIndex, _filteredPlaylist.Count - 1));
        
        // Inicializar con el primer video de la playlist
        var firstVideo = _filteredPlaylist[_currentPlaylistIndex];
        await LoadVideoDataAsync(firstVideo);
        VideoClip = firstVideo;
        
        UpdatePlaylistProperties();
        
        // Cargar eventos de etiquetas del video
        await LoadTagEventsAsync();

        // Auto-abrir Split Time si existen laps/timing guardados
        await AutoOpenSplitTimePanelIfHasTimingAsync();
    }

    /// <summary>
    /// Inicializa el ViewModel con un video y carga los datos de filtrado de la sesión
    /// </summary>
    public async Task InitializeWithVideoAsync(VideoClip video)
    {
        // Cargar datos actualizados del video desde la base de datos
        await LoadVideoDataAsync(video);
        
        VideoClip = video;
        
        if (video.SessionId > 0)
        {
            await LoadSessionDataAsync(video.SessionId);
            
            // Encontrar el video actual en la playlist
            _currentPlaylistIndex = _filteredPlaylist.FindIndex(v => v.Id == video.Id);
            if (_currentPlaylistIndex < 0) _currentPlaylistIndex = 0;
            
            UpdatePlaylistProperties();
        }
        
        // Cargar eventos de etiquetas del video
        await LoadTagEventsAsync();

        // Auto-abrir Split Time si existen laps/timing guardados
        await AutoOpenSplitTimePanelIfHasTimingAsync();
    }
    
    /// <summary>
    /// Carga los datos actualizados del video desde la base de datos (atleta, sección, tags, eventos)
    /// </summary>
    private async Task LoadVideoDataAsync(VideoClip video)
    {
        if (video.Id <= 0) return;
        
        try
        {
            // Recargar el video desde la BD para obtener datos actualizados
            var freshVideo = await _databaseService.GetVideoClipByIdAsync(video.Id);
            if (freshVideo != null)
            {
                // Actualizar datos que podrían haber cambiado
                video.AtletaId = freshVideo.AtletaId;
                video.Section = freshVideo.Section;
            }
            
            // Cargar atleta si tiene asignado
            if (video.AtletaId > 0 && video.Atleta == null)
            {
                video.Atleta = await _databaseService.GetAthleteByIdAsync(video.AtletaId);
            }
            else if (video.AtletaId > 0)
            {
                // Recargar atleta para tener datos actualizados
                video.Atleta = await _databaseService.GetAthleteByIdAsync(video.AtletaId);
            }
            
            // Cargar sesión si no está cargada
            if (video.Session == null && video.SessionId > 0)
            {
                video.Session = await _databaseService.GetSessionByIdAsync(video.SessionId);
            }
            
            // Cargar tags asignados (siempre recargar para tener datos actualizados)
            video.Tags = await _databaseService.GetTagsForVideoAsync(video.Id);
            
            // Cargar eventos y construir EventTags (lista de tags únicos de eventos)
            var events = await _databaseService.GetTagEventsForVideoAsync(video.Id);
            var allEventDefs = await _databaseService.GetAllEventTagsAsync();
            var eventDict = allEventDefs.ToDictionary(t => t.Id, t => t);
            
            video.EventTags = events
                .Select(e => e.TagId)
                .Distinct()
                .Where(id => eventDict.ContainsKey(id))
                .Select(id => new Tag
                {
                    Id = id,
                    NombreTag = eventDict[id].Nombre,
                    IsEventTag = true
                })
                .ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al cargar datos del video: {ex.Message}");
        }
    }

    #endregion

    #region Métodos privados

    /// <summary>
    /// Cierra todos los paneles desplegables de la barra de herramientas.
    /// Llamar este método al abrir cualquier panel para asegurar que no haya múltiples paneles abiertos.
    /// </summary>
    public void CloseAllPanels()
    {
        ShowAthleteAssignPanel = false;
        ShowSectionAssignPanel = false;
        ShowTagsAssignPanel = false;
        ShowTagEventsPanel = false;
        ShowSplitTimePanel = false;
        
        // Notificar al code-behind para cerrar paneles gestionados ahí (ej: DrawingTools)
        // Aseguramos hilo UI porque el handler toca elementos visuales.
        if (CloseExternalPanelsRequested is not null)
        {
            if (MainThread.IsMainThread)
            {
                CloseExternalPanelsRequested.Invoke(this, EventArgs.Empty);
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    CloseExternalPanelsRequested?.Invoke(this, EventArgs.Empty);
                });
            }
        }
    }

    /// <summary>
    /// Evento para notificar al code-behind que debe cerrar sus paneles (DrawingTools, VideoLessonOptions, etc.)
    /// </summary>
    public event EventHandler? CloseExternalPanelsRequested;

    /// <summary>
    /// Notifica al Dashboard que un video ha sido actualizado para que refresque solo ese item
    /// </summary>
    private void NotifyVideoClipUpdated()
    {
        if (_videoClip != null && _videoClip.Id > 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MessagingCenter.Send(this, "VideoClipUpdated", _videoClip.Id);
            });
        }
    }

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

    private void SetSpeed(string? speedStr)
    {
        if (double.TryParse(speedStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var speed))
        {
            PlaybackSpeed = speed;
        }
    }

    private void UpdateProgress()
    {
        // No actualizar Progress si el usuario está arrastrando el slider
        // para evitar conflictos con el binding que causa parpadeo
        if (_isDraggingSlider)
            return;
            
        if (Duration.TotalSeconds > 0)
            Progress = CurrentPosition.TotalSeconds / Duration.TotalSeconds;
        else
            Progress = 0;
    }

    private async Task LoadSessionDataAsync(int sessionId)
    {
        try
        {
            // Cargar todos los videos de la sesión
            _sessionVideos = await _databaseService.GetVideoClipsBySessionAsync(sessionId);
            
            // Cargar la sesión para cada video y los tags
            var session = await _databaseService.GetSessionByIdAsync(sessionId);
            var categories = await _databaseService.GetAllCategoriesAsync();
            
            foreach (var video in _sessionVideos)
            {
                video.Session = session;
                if (video.Tags == null)
                    video.Tags = await _databaseService.GetTagsForVideoAsync(video.Id);
            }
            
            // Cargar la configuración de parciales guardada para esta sesión
            await LoadSessionLapConfigAsync(sessionId);
            
            // Extraer opciones únicas para los filtros
            PopulateFilterOptions(categories);
            
            // Aplicar filtros iniciales (sin filtro = todos los videos)
            ApplyFilters();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading session data: {ex.Message}");
        }
    }

    private void PopulateFilterOptions(List<Category> allCategories)
    {
        // Opción "Todos" para cada filtro
        AthleteOptions.Clear();
        AthleteOptions.Add(new FilterOption<Athlete>(null, "Todos los atletas"));
        
        SectionOptions.Clear();
        SectionOptions.Add(new FilterOption<int>(0, "Todas las secciones"));
        
        CategoryOptions.Clear();
        CategoryOptions.Add(new FilterOption<Category>(null, "Todas las categorías"));
        
        // Atletas únicos
        var uniqueAthletes = _sessionVideos
            .Where(v => v.Atleta != null)
            .Select(v => v.Atleta!)
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.NombreCompleto);
        
        foreach (var athlete in uniqueAthletes)
        {
            AthleteOptions.Add(new FilterOption<Athlete>(athlete, athlete.NombreCompleto ?? $"Atleta {athlete.Id}"));
        }
        
        // Secciones únicas
        var uniqueSections = _sessionVideos
            .Select(v => v.Section)
            .Distinct()
            .OrderBy(s => s);
        
        foreach (var section in uniqueSections)
        {
            SectionOptions.Add(new FilterOption<int>(section, $"Sección {section}"));
        }
        
        // Categorías únicas (basadas en los atletas de la sesión)
        var usedCategoryIds = _sessionVideos
            .Where(v => v.Atleta != null)
            .Select(v => v.Atleta!.CategoriaId)
            .Distinct()
            .ToHashSet();
        
        var usedCategories = allCategories
            .Where(c => usedCategoryIds.Contains(c.Id))
            .OrderBy(c => c.NombreCategoria);
        
        foreach (var category in usedCategories)
        {
            CategoryOptions.Add(new FilterOption<Category>(category, category.NombreCategoria ?? $"Categoría {category.Id}"));
        }
        
        // Seleccionar "Todos" por defecto
        SelectedAthlete = AthleteOptions.FirstOrDefault();
        SelectedSection = SectionOptions.FirstOrDefault();
        SelectedCategory = CategoryOptions.FirstOrDefault();
    }

    private void ApplyFilters()
    {
        var filtered = _sessionVideos.AsEnumerable();
        
        // Filtrar por atleta
        if (SelectedAthlete?.Value != null)
        {
            filtered = filtered.Where(v => v.AtletaId == SelectedAthlete.Value.Id);
        }
        
        // Filtrar por sección
        if (SelectedSection?.Value > 0)
        {
            filtered = filtered.Where(v => v.Section == SelectedSection.Value);
        }
        
        // Filtrar por categoría
        if (SelectedCategory?.Value != null)
        {
            filtered = filtered.Where(v => v.Atleta?.CategoriaId == SelectedCategory.Value.Id);
        }
        
        _filteredPlaylist = filtered.OrderBy(v => v.CreationDate).ToList();
        
        // Actualizar índice actual
        if (_videoClip != null)
        {
            _currentPlaylistIndex = _filteredPlaylist.FindIndex(v => v.Id == _videoClip.Id);
            if (_currentPlaylistIndex < 0 && _filteredPlaylist.Count > 0)
            {
                // El video actual no está en la playlist filtrada, ir al primero
                _currentPlaylistIndex = 0;
                _ = NavigateToCurrentPlaylistVideoAsync();
            }
        }
        
        UpdatePlaylistProperties();
    }

    private void ClearFilters()
    {
        SelectedAthlete = AthleteOptions.FirstOrDefault();
        SelectedSection = SectionOptions.FirstOrDefault();
        SelectedCategory = CategoryOptions.FirstOrDefault();
    }

    private void GoToPreviousVideo()
    {
        if (!CanGoPrevious) return;
        
        _currentPlaylistIndex--;
        _ = NavigateToCurrentPlaylistVideoAsync();
    }

    private void GoToNextVideo()
    {
        if (!CanGoNext) return;
        
        _currentPlaylistIndex++;
        _ = NavigateToCurrentPlaylistVideoAsync();
    }

    private async Task NavigateToCurrentPlaylistVideoAsync()
    {
        if (_currentPlaylistIndex < 0 || _currentPlaylistIndex >= _filteredPlaylist.Count)
            return;
        
        var newVideo = _filteredPlaylist[_currentPlaylistIndex];
        
        // Cargar datos actualizados del video desde la base de datos
        await LoadVideoDataAsync(newVideo);
        
        // Actualizar las propiedades del video
        _videoClip = newVideo;
        
        // Notificar cambios de todas las propiedades del video
        OnPropertyChanged(nameof(VideoClip));
        OnPropertyChanged(nameof(HasVideoInfo));
        OnPropertyChanged(nameof(AthleteName));
        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(SessionPlace));
        OnPropertyChanged(nameof(SessionDate));
        OnPropertyChanged(nameof(SectionText));
        OnPropertyChanged(nameof(VideoDurationText));
        OnPropertyChanged(nameof(VideoSizeText));
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(BadgeColor));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(TagsText));
        OnPropertyChanged(nameof(VideoClipTags));
        OnPropertyChanged(nameof(HasCurrentAthlete));
        OnPropertyChanged(nameof(CurrentAthleteText));
        
        // Actualizar path
        if (newVideo.LocalClipPath != null)
        {
            _videoPath = newVideo.LocalClipPath;
            OnPropertyChanged(nameof(VideoPath));
        }
        
        // Actualizar título
        VideoTitle = newVideo.Atleta?.NombreCompleto ?? Path.GetFileNameWithoutExtension(_videoPath);
        
        UpdatePlaylistProperties();
        
        // Cargar eventos de etiquetas del nuevo video
        await LoadTagEventsAsync();

        // Auto-abrir Split Time si existen laps/timing guardados
        await AutoOpenSplitTimePanelIfHasTimingAsync();
        
        // Notificar al view para que recargue el video
        VideoChanged?.Invoke(this, newVideo);
    }

    private void UpdatePlaylistProperties()
    {
        OnPropertyChanged(nameof(PlaylistCount));
        OnPropertyChanged(nameof(CurrentPlaylistPosition));
        OnPropertyChanged(nameof(PlaylistPositionText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(HasPlaylist));
        
        // Actualizar estado de comandos
        ((Command)PreviousVideoCommand).ChangeCanExecute();
        ((Command)NextVideoCommand).ChangeCanExecute();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ===== Métodos de asignación de atleta =====

    private async Task ToggleAthleteAssignPanelAsync()
    {
        var wasOpen = ShowAthleteAssignPanel;
        // Cerrar todos los paneles primero
        CloseAllPanels();
        
        // Si se va a abrir, cargar datos
        if (!wasOpen)
        {
            // Cargar atletas SIEMPRE (MacCatalyst tiene problemas con el Picker si se asigna la colección después)
            var athletes = await _databaseService.GetAllAthletesAsync();
            
            // Limpiar y volver a llenar la colección existente en lugar de reemplazarla
            AllAthletes.Clear();
            foreach (var athlete in athletes.OrderBy(a => a.Apellido).ThenBy(a => a.Nombre))
            {
                // Marcar como seleccionado si es el atleta actualmente asignado al video
                athlete.IsSelected = _videoClip?.AtletaId == athlete.Id && _videoClip.AtletaId > 0;
                
                // Si está seleccionado, también actualizamos la referencia para el botón "Asignar"
                if (athlete.IsSelected)
                {
                    _selectedAthleteToAssign = athlete;
                }
                
                AllAthletes.Add(athlete);
            }
            
            // Si no hay atleta asignado actualmente, limpiar selección previa
            if (_videoClip?.AtletaId <= 0)
            {
                _selectedAthleteToAssign = null;
            }
            
            // Notificar el cambio explícitamente
            OnPropertyChanged(nameof(AllAthletes));
            ((Command)AssignAthleteCommand).ChangeCanExecute();
        }
        
        ShowAthleteAssignPanel = !wasOpen;
    }

    private void SelectAthleteToAssign(Athlete? athlete)
    {
        if (athlete == null) return;
        
        // Deseleccionar todos los atletas
        foreach (var a in AllAthletes)
        {
            a.IsSelected = (a.Id == athlete.Id);
        }
        
        _selectedAthleteToAssign = athlete;
        
        // Forzar refresco visual recreando la colección (BindableLayout no detecta cambios en propiedades de items)
        var currentList = AllAthletes.ToList();
        AllAthletes.Clear();
        foreach (var a in currentList)
        {
            AllAthletes.Add(a);
        }
        
        ((Command)AssignAthleteCommand).ChangeCanExecute();
    }

    private async Task AssignAthleteAsync()
    {
        if (_selectedAthleteToAssign == null || _videoClip == null)
            return;

        try
        {
            // Actualizar el video con el atleta seleccionado
            _videoClip.AtletaId = _selectedAthleteToAssign.Id;
            _videoClip.Atleta = _selectedAthleteToAssign;
            
            await _databaseService.SaveVideoClipAsync(_videoClip);
            
            // Actualizar UI
            OnPropertyChanged(nameof(AthleteName));
            OnPropertyChanged(nameof(HasCurrentAthlete));
            OnPropertyChanged(nameof(CurrentAthleteText));
            VideoTitle = _selectedAthleteToAssign.NombreCompleto;
            
            // Cerrar panel y limpiar selección
            ShowAthleteAssignPanel = false;
            SelectedAthleteToAssign = null;
            
            // Notificar al Dashboard para actualizar la galería
            NotifyVideoClipUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al asignar atleta: {ex.Message}");
        }
    }

    private async Task CreateAndAssignAthleteAsync()
    {
        if (string.IsNullOrWhiteSpace(_newAthleteName) && string.IsNullOrWhiteSpace(_newAthleteSurname))
            return;

        if (_videoClip == null)
            return;

        try
        {
            // Crear nuevo atleta
            var newAthlete = new Athlete
            {
                Nombre = _newAthleteName.Trim(),
                Apellido = _newAthleteSurname.Trim()
            };
            
            // Insertar en la base de datos
            var athleteId = await _databaseService.InsertAthleteAsync(newAthlete);
            newAthlete.Id = athleteId;
            
            // Añadir a la lista local
            AllAthletes.Add(newAthlete);
            
            // Asignar al video
            _videoClip.AtletaId = athleteId;
            _videoClip.Atleta = newAthlete;
            
            await _databaseService.SaveVideoClipAsync(_videoClip);
            
            // Actualizar UI
            OnPropertyChanged(nameof(AthleteName));
            OnPropertyChanged(nameof(HasCurrentAthlete));
            OnPropertyChanged(nameof(CurrentAthleteText));
            VideoTitle = newAthlete.NombreCompleto;
            
            // Cerrar panel y limpiar campos
            ShowAthleteAssignPanel = false;
            NewAthleteName = "";
            NewAthleteSurname = "";
            
            // Notificar al Dashboard para actualizar la galería
            NotifyVideoClipUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al crear atleta: {ex.Message}");
        }
    }

    // ===== Métodos de asignación de sección =====

    private async Task AssignSectionAsync()
    {
        if (_videoClip == null)
            return;

        try
        {
            _videoClip.Section = _sectionToAssign;
            await _databaseService.SaveVideoClipAsync(_videoClip);
            
            // Actualizar UI
            OnPropertyChanged(nameof(SectionText));
            OnPropertyChanged(nameof(CurrentSectionText));
            
            ShowSectionAssignPanel = false;
            
            // Notificar al Dashboard para actualizar la galería
            NotifyVideoClipUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al asignar sección: {ex.Message}");
        }
    }

    // ===== Métodos de asignación de etiquetas =====

    private async Task ToggleTagsAssignPanelAsync()
    {
        var wasOpen = ShowTagsAssignPanel;
        // Cerrar todos los paneles primero
        CloseAllPanels();
        
        // Si se va a abrir, cargar datos
        if (!wasOpen)
        {
            // Cargar todos los tags SIEMPRE (MacCatalyst tiene problemas si se asigna la colección después)
            var tags = await _databaseService.GetAllTagsAsync();
            
            // Limpiar y volver a llenar la colección existente
            AllTags.Clear();
            foreach (var tag in tags.Where(t => !string.IsNullOrEmpty(t.NombreTag)).OrderBy(t => t.NombreTag))
            {
                AllTags.Add(tag);
            }
            
            // Marcar los tags que ya tiene el video
            SelectedTags.Clear();
            if (_videoClip?.Tags != null)
            {
                foreach (var tag in _videoClip.Tags)
                {
                    SelectedTags.Add(tag);
                }
            }
            
            // Actualizar estado visual de todos los tags
            foreach (var tag in AllTags)
            {
                tag.IsSelected = SelectedTags.Any(t => t.Id == tag.Id) ? 1 : 0;
            }
            OnPropertyChanged(nameof(AllTags));
        }
        
        ShowTagsAssignPanel = !wasOpen;
    }

    private void ToggleTagSelection(Tag tag)
    {
        if (tag == null) return;
        
        var existing = SelectedTags.FirstOrDefault(t => t.Id == tag.Id);
        if (existing != null)
        {
            SelectedTags.Remove(existing);
            tag.IsSelected = 0;
        }
        else
        {
            SelectedTags.Add(tag);
            tag.IsSelected = 1;
        }
        
        OnPropertyChanged(nameof(AllTags));
    }

    private async Task SaveTagsAsync()
    {
        if (_videoClip == null)
            return;

        try
        {
            var tagIds = SelectedTags.Select(t => t.Id).ToList();
            await _databaseService.SetVideoTagsAsync(
                _videoClip.Id,
                _videoClip.SessionId,
                _videoClip.AtletaId,
                tagIds);
            
            // Actualizar los tags en el VideoClip local
            _videoClip.Tags = SelectedTags.ToList();
            
            // Actualizar UI del overlay y panel
            OnPropertyChanged(nameof(TagsText));
            OnPropertyChanged(nameof(HasTags));
            OnPropertyChanged(nameof(VideoClipTags));
            OnPropertyChanged(nameof(CurrentTagsText));
            
            ShowTagsAssignPanel = false;
            
            // Notificar al Dashboard para actualizar la galería
            NotifyVideoClipUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al guardar etiquetas: {ex.Message}");
        }
    }

    private async Task CreateAndAddTagAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTagName))
            return;

        try
        {
            // Crear nuevo tag
            var newTag = new Tag
            {
                NombreTag = _newTagName.Trim(),
                IsSelected = 1
            };
            
            // Insertar en la base de datos
            var tagId = await _databaseService.InsertTagAsync(newTag);
            newTag.Id = tagId;
            
            // Añadir a las listas
            AllTags.Add(newTag);
            SelectedTags.Add(newTag);
            
            // Limpiar campo
            NewTagName = "";
            
            OnPropertyChanged(nameof(AllTags));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al crear etiqueta: {ex.Message}");
        }
    }

    /// <summary>
    /// Crea un nuevo evento/etiqueta y lo añade a la lista de eventos del video
    /// </summary>
    private async Task CreateAndAddEventAsync()
    {
        if (string.IsNullOrWhiteSpace(_newEventName) || _videoClip == null)
            return;

        try
        {
            var normalizedName = _newEventName.Trim();

            // Reusar si ya existe el tipo de evento
            var existing = await _databaseService.FindEventTagByNameAsync(normalizedName);
            if (existing != null)
            {
                foreach (var t in AllEventTags)
                    t.IsSelected = false;

                var existingInList = AllEventTags.FirstOrDefault(t => t.Id == existing.Id);
                var toSelect = existingInList ?? existing;
                toSelect.IsSelected = true;
                if (existingInList == null)
                    AllEventTags.Add(toSelect);

                SelectedEventTagToAdd = toSelect;
                await AddTagEventAsync();
                NewEventName = "";
                return;
            }

            // Crear nuevo tipo de evento (catálogo separado)
            var newEventTag = new EventTagDefinition
            {
                Nombre = normalizedName,
                IsSelected = true
            };

            // Insertar en la base de datos
            var eventTagId = await _databaseService.InsertEventTagAsync(newEventTag);
            newEventTag.Id = eventTagId;

            // Añadir a la lista de eventos disponibles
            AllEventTags.Add(newEventTag);
            OnPropertyChanged(nameof(AllEventTags));

            // Seleccionar automáticamente el nuevo tipo de evento para añadir la ocurrencia
            SelectedEventTagToAdd = newEventTag;
            
            // Añadir automáticamente el evento en la posición actual
            await AddTagEventAsync();
            
            // Limpiar campo
            NewEventName = "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al crear evento: {ex.Message}");
        }
    }

    // ===== Métodos de eventos de etiquetas con timestamps =====

    private async Task ToggleTagEventsPanelAsync()
    {
        var wasOpen = ShowTagEventsPanel;
        
        // Cerrar todos los paneles (incluidos externos como DrawingTools)
        CloseAllPanels();
        
        // Si no estaba abierto, cargar datos antes de abrir
        if (!wasOpen)
        {
            // Cargar tipos de evento SIEMPRE (catálogo separado)
            var eventTags = await _databaseService.GetAllEventTagsAsync();

            // Limpiar selección previa
            SelectedEventTagToAdd = null;

            // Limpiar y volver a llenar la colección existente
            // Ordenar: primero los de sistema (penalizaciones), luego el resto alfabéticamente
            AllEventTags.Clear();
            var orderedTags = eventTags
                .Where(t => !string.IsNullOrEmpty(t.Nombre))
                .OrderByDescending(t => t.IsSystem)
                .ThenBy(t => t.Nombre);
            
            foreach (var evt in orderedTags)
            {
                evt.IsSelected = false;
                AllEventTags.Add(evt);
            }

            // Notificar el cambio explícitamente
            OnPropertyChanged(nameof(AllEventTags));
            
            // Cargar eventos del video
            await LoadTagEventsAsync();
        }
        
        ShowTagEventsPanel = !wasOpen;
    }

    private async Task LoadTagEventsAsync()
    {
        if (_videoClip == null) return;
        
        try
        {
            var events = await _databaseService.GetTagEventsForVideoAsync(_videoClip.Id);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TagEvents = new ObservableCollection<TagEvent>(events);
                OnPropertyChanged(nameof(HasTagEvents));
                OnPropertyChanged(nameof(TagEventsCountText));
                RefreshTimelineMarkers();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al cargar eventos: {ex.Message}");
        }
    }

    private void RefreshTimelineMarkers()
    {
        try
        {
            var durationMs = Duration.TotalMilliseconds;
            if (durationMs <= 0 || _tagEvents == null || _tagEvents.Count == 0)
            {
                if (_timelineMarkers.Count > 0)
                    _timelineMarkers.Clear();
                return;
            }

            var markers = _tagEvents
                .Where(e => e.TimestampMs >= 0)
                .Select(e => new TimelineMarker
                {
                    Position = Math.Clamp(e.TimestampMs / durationMs, 0.0, 1.0),
                    TimestampMs = e.TimestampMs,
                    Label = e.TagName
                })
                .ToList();

            _timelineMarkers.Clear();
            foreach (var m in markers)
                _timelineMarkers.Add(m);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al refrescar marcadores de timeline: {ex.Message}");
        }
    }

    private void SeekToTimelineMarker(TimelineMarker? marker)
    {
        if (marker == null)
            return;

        // Posicionar 0,5 segundos antes del punto (o al inicio si es muy temprano)
        var seekMs = Math.Max(0, marker.TimestampMs - 500);
        var seekSeconds = seekMs / 1000.0;

        SeekRequested?.Invoke(this, seekSeconds);
    }

    private void SelectTagToAdd(EventTagDefinition? tag)
    {
        if (tag == null) return;

        // Toggle: si vuelves a tocar el mismo chip, se deselecciona.
        if (_selectedEventTagToAdd?.Id == tag.Id && tag.IsSelected)
        {
            tag.IsSelected = false;
            SelectedEventTagToAdd = null;
            return;
        }

        foreach (var t in AllEventTags)
            t.IsSelected = false;

        tag.IsSelected = true;
        SelectedEventTagToAdd = tag;
    }

    private async Task AddTagEventAsync()
    {
        if (_videoClip == null || _selectedEventTagToAdd == null)
            return;

        try
        {
            // Obtener la posición actual del video en milisegundos
            var timestampMs = (long)CurrentPosition.TotalMilliseconds;
            
            // Añadir el evento a la base de datos
            var inputId = await _databaseService.AddTagEventAsync(
                _videoClip.Id,
                _selectedEventTagToAdd.Id,
                timestampMs,
                _videoClip.SessionId,
                _videoClip.AtletaId);

            // Recargar lista completa (más robusto en UIKit que inserciones incrementales)
            await LoadTagEventsAsync();
            
            // Actualizar tags únicos del video
            await RefreshUniqueTagsAsync();

            // Limpiar selección para evitar re-uso accidental
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_selectedEventTagToAdd != null)
                    _selectedEventTagToAdd.IsSelected = false;

                SelectedEventTagToAdd = null;

                foreach (var t in AllEventTags)
                    t.IsSelected = false;
                
                OnPropertyChanged(nameof(HasTagEvents));
                OnPropertyChanged(nameof(TagEventsCountText));
            });
            
            // Notificar al Dashboard para actualizar la galería
            NotifyVideoClipUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al añadir evento: {ex.Message}");
        }
    }

    private async Task DeleteTagEventAsync(TagEvent? tagEvent)
    {
        if (tagEvent == null || _videoClip == null)
            return;

        try
        {
            await _databaseService.DeleteTagEventAsync(tagEvent.InputId);

            // Recargar lista completa (evita inconsistencias nativas en CollectionView)
            await LoadTagEventsAsync();
            
            // Actualizar tags únicos del video
            await RefreshUniqueTagsAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                OnPropertyChanged(nameof(HasTagEvents));
                OnPropertyChanged(nameof(TagEventsCountText));
            });
            
            // Notificar al Dashboard para actualizar la galería
            NotifyVideoClipUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al eliminar evento: {ex.Message}");
        }
    }

    private void SeekToTagEvent(TagEvent? tagEvent)
    {
        if (tagEvent == null)
            return;
        
        // Posicionar 1 segundo antes del evento (o al inicio si es muy temprano)
        var seekMs = Math.Max(0, tagEvent.TimestampMs - 1000);
        var seekSeconds = seekMs / 1000.0;
        
        SeekRequested?.Invoke(this, seekSeconds);
    }

    private async Task RefreshUniqueTagsAsync()
    {
        if (_videoClip == null) return;
        
        try
        {
            var tags = await _databaseService.GetTagsForVideoAsync(_videoClip.Id);
            _videoClip.Tags = tags;
            
            OnPropertyChanged(nameof(HasTags));
            OnPropertyChanged(nameof(TagsText));
            OnPropertyChanged(nameof(VideoClipTags));
            OnPropertyChanged(nameof(CurrentTagsText));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al refrescar tags: {ex.Message}");
        }
    }

    /// <summary>
    /// Elimina un tag asignado del video (no afecta eventos)
    /// </summary>
    private async Task RemoveAssignedTagAsync(Tag? tag)
    {
        if (tag == null || _videoClip == null)
            return;

        try
        {
            await _databaseService.RemoveTagFromVideoAsync(_videoClip.Id, tag.Id);
            
            // Recargar tags del video
            var tags = await _databaseService.GetTagsForVideoAsync(_videoClip.Id);
            _videoClip.Tags = tags;
            
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                OnPropertyChanged(nameof(HasTags));
                OnPropertyChanged(nameof(TagsText));
                OnPropertyChanged(nameof(VideoClipTags));
                OnPropertyChanged(nameof(CurrentTagsText));
            });
            
            // Notificar al Dashboard para actualizar la galería
            NotifyVideoClipUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al eliminar tag asignado: {ex.Message}");
        }
    }

    /// <summary>
    /// Elimina todos los eventos de un tipo de tag específico del video
    /// </summary>
    private async Task RemoveEventTagAsync(Tag? tag)
    {
        if (tag == null || _videoClip == null)
            return;

        try
        {
            // Eliminar todos los eventos de este tipo de tag del video
            await _databaseService.RemoveEventTagFromVideoAsync(_videoClip.Id, tag.Id);
            
            // Recargar eventos y actualizar EventTags del clip
            await LoadTagEventsAsync();
            
            // Actualizar EventTags del clip manualmente
            var allEvents = await _databaseService.GetTagEventsForVideoAsync(_videoClip.Id);
            var allEventDefs = await _databaseService.GetAllEventTagsAsync();
            var eventDict = allEventDefs.ToDictionary(t => t.Id, t => t);
            
            _videoClip.EventTags = allEvents
                .Select(e => e.TagId)
                .Distinct()
                .Where(id => eventDict.ContainsKey(id))
                .Select(id => new Tag
                {
                    Id = id,
                    NombreTag = eventDict[id].Nombre,
                    IsEventTag = true
                })
                .ToList();
            
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                OnPropertyChanged(nameof(HasEventTags));
                OnPropertyChanged(nameof(VideoClipEventTags));
                OnPropertyChanged(nameof(HasTagEvents));
                OnPropertyChanged(nameof(TagEventsCountText));
            });
            
            // Notificar al Dashboard para actualizar la galería
            NotifyVideoClipUpdated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al eliminar eventos del tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Elimina una etiqueta de la lista AllTags (la borra de la base de datos)
    /// </summary>
    private async Task DeleteTagFromListAsync(Tag? tag)
    {
        if (tag == null)
            return;

        try
        {
            // Eliminar el tag de la base de datos
            await _databaseService.DeleteTagAsync(tag.Id);
            
            // Recargar la lista de tags
            var allTags = await _databaseService.GetAllTagsAsync();
            _allTags.Clear();
            foreach (var t in allTags)
            {
                _allTags.Add(t);
            }
            
            // Recargar datos del video actual si existe
            if (_videoClip != null)
            {
                await LoadVideoDataAsync(_videoClip);
                await LoadTagEventsAsync();
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    OnPropertyChanged(nameof(HasTags));
                    OnPropertyChanged(nameof(VideoClipTags));
                    OnPropertyChanged(nameof(HasEventTags));
                    OnPropertyChanged(nameof(VideoClipEventTags));
                    OnPropertyChanged(nameof(TagsText));
                    OnPropertyChanged(nameof(CurrentTagsText));
                    OnPropertyChanged(nameof(HasTagEvents));
                    OnPropertyChanged(nameof(TagEventsCountText));
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al eliminar tag de la lista: {ex.Message}");
        }
    }

    /// <summary>
    /// Elimina un tipo de evento del catálogo AllEventTags (lo borra de event_tags y sus ocurrencias IsEvent=1)
    /// No se pueden borrar tags de sistema (penalizaciones)
    /// </summary>
    private async Task DeleteEventTagFromListAsync(EventTagDefinition? eventTag)
    {
        if (eventTag == null)
            return;

        // No permitir borrar tags de sistema
        if (eventTag.IsSystem)
        {
            System.Diagnostics.Debug.WriteLine($"No se puede borrar tag de sistema: {eventTag.Nombre}");
            return;
        }

        try
        {
            var deleted = await _databaseService.DeleteEventTagAsync(eventTag.Id);
            
            if (!deleted)
            {
                System.Diagnostics.Debug.WriteLine($"No se pudo borrar el tag: {eventTag.Nombre}");
                return;
            }

            // Recargar catálogo de eventos
            var allEventTags = await _databaseService.GetAllEventTagsAsync();
            _allEventTags.Clear();
            foreach (var t in allEventTags)
                _allEventTags.Add(t);

            if (_videoClip != null)
            {
                await LoadVideoDataAsync(_videoClip);
                await LoadTagEventsAsync();

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    OnPropertyChanged(nameof(HasEventTags));
                    OnPropertyChanged(nameof(VideoClipEventTags));
                    OnPropertyChanged(nameof(HasTagEvents));
                    OnPropertyChanged(nameof(TagEventsCountText));
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al eliminar tipo de evento: {ex.Message}");
        }
    }

    #endregion

    #region Split Time Methods

    private async Task AutoOpenSplitTimePanelIfHasTimingAsync()
    {
        if (_videoClip == null)
            return;

        try
        {
            var timing = await _databaseService.GetExecutionTimingEventsByVideoAsync(_videoClip.Id);
            if (timing.Count <= 0)
                return;

            // Cerrar otros paneles y abrir Split Time
            ShowAthleteAssignPanel = false;
            ShowSectionAssignPanel = false;
            ShowTagsAssignPanel = false;
            ShowTagEventsPanel = false;

            if (!ShowSplitTimePanel)
                ShowSplitTimePanel = true;

            await LoadExistingSplitAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error auto-abriendo Split Time: {ex.Message}");
        }
    }

    private void ToggleSplitTimePanel()
    {
        var wasOpen = ShowSplitTimePanel;
        
        // Cerrar todos los paneles (incluidos externos como DrawingTools)
        CloseAllPanels();
        
        ShowSplitTimePanel = !wasOpen;

        // Al abrir, cargar el split existente si lo hay
        if (ShowSplitTimePanel && _videoClip != null)
        {
            _ = LoadExistingSplitAsync();
            
            // Inicializar los parciales si están vacíos
            if (_assistedLaps.Count == 0)
            {
                InitializeAssistedLaps();
            }
        }
    }

    private void SetSplitStart()
    {
        SplitStartTime = CurrentPosition;

        // Igual que en Camera: al marcar inicio, empezamos nueva serie de laps
        _splitLapMarksMs.Clear();
        RebuildSplitLapRows();
    }

    private void SetSplitEnd()
    {
        SplitEndTime = CurrentPosition;

        // Si hay laps fuera del rango, los filtramos
        FilterLapMarksToRange();
        RebuildSplitLapRows();
    }

    private void AddSplitLap()
    {
        if (_videoClip == null)
            return;

        if (!_splitStartTime.HasValue)
            return;

        var nowMs = (long)CurrentPosition.TotalMilliseconds;
        var startMs = (long)_splitStartTime.Value.TotalMilliseconds;
        if (nowMs < startMs)
            return;

        if (_splitEndTime.HasValue)
        {
            var endMs = (long)_splitEndTime.Value.TotalMilliseconds;
            if (nowMs > endMs)
                return;
        }

        // Evitar duplicados exactos
        if (_splitLapMarksMs.Contains(nowMs))
            return;

        _splitLapMarksMs.Add(nowMs);
        FilterLapMarksToRange();
        RebuildSplitLapRows();
    }

    private void ClearSplit()
    {
        SplitStartTime = null;
        SplitEndTime = null;
        SplitDuration = null;

        _splitLapMarksMs.Clear();
        _splitLapRows.Clear();
        HasSplitLaps = false;
    }

    private void FilterLapMarksToRange()
    {
        if (!_splitStartTime.HasValue)
        {
            _splitLapMarksMs.Clear();
            return;
        }

        var startMs = (long)_splitStartTime.Value.TotalMilliseconds;
        long? endMs = _splitEndTime.HasValue ? (long)_splitEndTime.Value.TotalMilliseconds : null;

        _splitLapMarksMs.RemoveAll(ms => ms < startMs || (endMs.HasValue && ms > endMs.Value));
    }

    private void RebuildSplitLapRows()
    {
        _splitLapMarksMs.Sort();

        _splitLapRows.Clear();

        if (!_splitStartTime.HasValue)
        {
            HasSplitLaps = false;
            return;
        }

        var prev = (long)_splitStartTime.Value.TotalMilliseconds;
        var lapIndex = 0;
        foreach (var mark in _splitLapMarksMs)
        {
            lapIndex++;
            var splitMs = mark - prev;
            if (splitMs < 0) splitMs = 0;
            prev = mark;

            _splitLapRows.Add(new ExecutionTimingRow
            {
                Title = $"Lap {lapIndex}",
                Value = FormatMs(splitMs),
                IsTotal = false
            });
        }

        HasSplitLaps = _splitLapRows.Count > 0;
    }

    private static string FormatMs(long ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }
    
    // ==================== MÉTODOS DEL MODO ASISTIDO ====================
    
    private void ToggleAssistedMode()
    {
        IsAssistedModeEnabled = !IsAssistedModeEnabled;
    }
    
    private void InitializeAssistedLaps()
    {
        _assistedLaps.Clear();
        for (int i = 1; i <= AssistedLapCount; i++)
        {
            _assistedLaps.Add(new AssistedLapDefinition
            {
                Index = i,
                Name = $"P{i}",
                IsCurrent = false
            });
        }
        AssistedLapState = AssistedLapState.Configuring;
        CurrentAssistedLapIndex = 0;
    }
    
    private async void StartAssistedCapture()
    {
        if (!IsAssistedModeEnabled) return;
        
        // Guardar la configuración de parciales para esta sesión
        await SaveSessionLapConfigAsync();
        
        // Limpiar cualquier marcado previo
        ClearSplit();
        foreach (var lap in _assistedLaps)
        {
            lap.MarkedMs = null;
            lap.IsCurrent = false;
        }
        
        // Iniciar el flujo: primero esperamos el INICIO
        AssistedLapState = AssistedLapState.WaitingForStart;
        CurrentAssistedLapIndex = 0;
        
        if (_assistedLaps.Count > 0)
        {
            _assistedLaps[0].IsCurrent = true;
        }
    }
    
    private void MarkAssistedPoint()
    {
        if (!IsAssistedModeEnabled || _videoClip == null) return;
        
        var nowMs = (long)CurrentPosition.TotalMilliseconds;
        
        switch (AssistedLapState)
        {
            case AssistedLapState.WaitingForStart:
                // Marcar inicio
                SplitStartTime = CurrentPosition;
                _splitLapMarksMs.Clear();
                
                if (_assistedLaps.Count > 0)
                {
                    // Pasar a marcar el primer parcial
                    AssistedLapState = AssistedLapState.MarkingLaps;
                    CurrentAssistedLapIndex = 0;
                }
                else
                {
                    // Si no hay parciales, ir directamente a fin
                    AssistedLapState = AssistedLapState.WaitingForEnd;
                }
                break;
                
            case AssistedLapState.MarkingLaps:
                if (CurrentAssistedLapIndex < _assistedLaps.Count)
                {
                    // Marcar el parcial actual
                    var lap = _assistedLaps[CurrentAssistedLapIndex];
                    lap.MarkedMs = nowMs;
                    lap.IsCurrent = false;
                    
                    // Añadir a la lista de laps
                    _splitLapMarksMs.Add(nowMs);
                    RebuildSplitLapRows();
                    
                    // Avanzar al siguiente
                    CurrentAssistedLapIndex++;
                    
                    if (CurrentAssistedLapIndex >= _assistedLaps.Count)
                    {
                        // Ya marcamos todos los parciales, ir a fin
                        AssistedLapState = AssistedLapState.WaitingForEnd;
                    }
                }
                break;
                
            case AssistedLapState.WaitingForEnd:
                // Marcar fin
                SplitEndTime = CurrentPosition;
                AssistedLapState = AssistedLapState.Completed;
                
                // Actualizar filas con los nombres de los parciales asistidos
                RebuildSplitLapRowsWithNames();
                break;
        }
        
        OnPropertyChanged(nameof(AssistedStateDescription));
        OnPropertyChanged(nameof(AssistedActionButtonText));
    }
    
    private void UpdateCurrentLapHighlight()
    {
        for (int i = 0; i < _assistedLaps.Count; i++)
        {
            _assistedLaps[i].IsCurrent = (i == CurrentAssistedLapIndex) && 
                AssistedLapState == AssistedLapState.MarkingLaps;
        }
    }
    
    private void ResetAssistedCapture()
    {
        ClearSplit();
        foreach (var lap in _assistedLaps)
        {
            lap.MarkedMs = null;
            lap.IsCurrent = false;
        }
        AssistedLapState = AssistedLapState.Configuring;
        CurrentAssistedLapIndex = 0;
    }
    
    /// <summary>
    /// Carga la configuración de parciales guardada para una sesión
    /// </summary>
    private async Task LoadSessionLapConfigAsync(int sessionId)
    {
        try
        {
            var config = await _databaseService.GetSessionLapConfigAsync(sessionId);
            if (config != null)
            {
                // Aplicar la configuración guardada
                _assistedLapCount = config.LapCount;
                OnPropertyChanged(nameof(AssistedLapCount));
                
                // Recrear la lista de parciales con los nombres guardados
                _assistedLaps.Clear();
                var names = config.LapNamesList;
                for (int i = 0; i < config.LapCount; i++)
                {
                    _assistedLaps.Add(new AssistedLapDefinition
                    {
                        Index = i + 1,
                        Name = i < names.Count ? names[i] : $"P{i + 1}",
                        IsCurrent = false
                    });
                }
                
                System.Diagnostics.Debug.WriteLine($"Configuración de parciales cargada: {config.LapCount} parciales");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando configuración de parciales: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Guarda la configuración actual de parciales para la sesión
    /// </summary>
    private async Task SaveSessionLapConfigAsync()
    {
        if (_videoClip == null || _videoClip.SessionId <= 0) return;
        
        try
        {
            var lapNames = _assistedLaps.Select(l => l.Name).ToList();
            await _databaseService.SaveSessionLapConfigAsync(_videoClip.SessionId, AssistedLapCount, lapNames);
            System.Diagnostics.Debug.WriteLine($"Configuración de parciales guardada: {AssistedLapCount} parciales");
            
            // Añadir al historial global
            await _databaseService.AddToLapConfigHistoryAsync(AssistedLapCount, lapNames);
            
            // Recargar el historial
            await LoadRecentLapConfigsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error guardando configuración de parciales: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Carga las configuraciones de parciales recientes
    /// </summary>
    private async Task LoadRecentLapConfigsAsync()
    {
        try
        {
            var configs = await _databaseService.GetRecentLapConfigsAsync(3);
            _recentLapConfigs.Clear();
            foreach (var config in configs)
            {
                _recentLapConfigs.Add(config);
            }
            OnPropertyChanged(nameof(HasRecentLapConfigs));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando historial de parciales: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Aplica una configuración del historial
    /// </summary>
    private void ApplyLapConfig(LapConfigHistory? config)
    {
        if (config == null) return;
        
        // Aplicar la configuración
        _assistedLapCount = config.LapCount;
        OnPropertyChanged(nameof(AssistedLapCount));
        
        // Recrear la lista de parciales con los nombres guardados
        _assistedLaps.Clear();
        var names = config.LapNamesList;
        for (int i = 0; i < config.LapCount; i++)
        {
            _assistedLaps.Add(new AssistedLapDefinition
            {
                Index = i + 1,
                Name = i < names.Count ? names[i] : $"P{i + 1}",
                IsCurrent = false
            });
        }
        
        // Asegurarse de estar en modo configuración
        AssistedLapState = AssistedLapState.Configuring;
        CurrentAssistedLapIndex = 0;
    }
    
    /// <summary>
    /// Reconstruye las filas de laps usando los nombres definidos en modo asistido
    /// </summary>
    private void RebuildSplitLapRowsWithNames()
    {
        _splitLapMarksMs.Sort();
        _splitLapRows.Clear();

        if (!_splitStartTime.HasValue)
        {
            HasSplitLaps = false;
            return;
        }

        var prev = (long)_splitStartTime.Value.TotalMilliseconds;
        for (int i = 0; i < _splitLapMarksMs.Count; i++)
        {
            var mark = _splitLapMarksMs[i];
            var splitMs = mark - prev;
            if (splitMs < 0) splitMs = 0;
            prev = mark;

            // Usar el nombre del parcial asistido si existe
            var lapName = (i < _assistedLaps.Count) ? _assistedLaps[i].DisplayName : $"Lap {i + 1}";

            _splitLapRows.Add(new ExecutionTimingRow
            {
                Title = lapName,
                Value = FormatMs(splitMs),
                IsTotal = false
            });
        }

        HasSplitLaps = _splitLapRows.Count > 0;
    }

    private void CalculateSplitDuration()
    {
        if (_splitStartTime.HasValue && _splitEndTime.HasValue)
        {
            var duration = _splitEndTime.Value - _splitStartTime.Value;
            // Permitir duración negativa mostrando valor absoluto para el cálculo
            SplitDuration = duration.TotalMilliseconds >= 0 ? duration : TimeSpan.Zero;
        }
        else
        {
            SplitDuration = null;
        }
    }

    private async Task LoadExistingSplitAsync()
    {
        if (_videoClip == null)
            return;

        try
        {
            // Evitar arrastrar datos de otros vídeos
            ClearSplit();

            var existingSplit = await _databaseService.GetSplitTimeForVideoAsync(_videoClip.Id);
            if (existingSplit != null)
            {
                // Parsear el InputValue que contiene JSON con start/end/duration en ms
                if (!string.IsNullOrEmpty(existingSplit.InputValue))
                {
                    try
                    {
                        var splitData = System.Text.Json.JsonSerializer.Deserialize<SplitTimeData>(existingSplit.InputValue);
                        if (splitData != null)
                        {
                            SplitStartTime = TimeSpan.FromMilliseconds(splitData.StartMs);
                            SplitEndTime = TimeSpan.FromMilliseconds(splitData.EndMs);
                            HasSavedSplit = true;
                        }
                    }
                    catch
                    {
                        // JSON inválido, ignorar
                    }
                }
            }
            else
            {
                HasSavedSplit = false;
            }

            // Cargar laps/inicio/fin desde tabla dedicada (si existe)
            var timing = await _databaseService.GetExecutionTimingEventsByVideoAsync(_videoClip.Id);
            if (timing.Count > 0)
            {
                var start = timing.FirstOrDefault(t => t.Kind == 0);
                var end = timing.FirstOrDefault(t => t.Kind == 2);

                if (start != null)
                    SplitStartTime = TimeSpan.FromMilliseconds(start.ElapsedMilliseconds);
                if (end != null)
                    SplitEndTime = TimeSpan.FromMilliseconds(end.ElapsedMilliseconds);

                _splitLapMarksMs.Clear();
                foreach (var lap in timing.Where(t => t.Kind == 1).OrderBy(t => t.ElapsedMilliseconds))
                    _splitLapMarksMs.Add(lap.ElapsedMilliseconds);

                FilterLapMarksToRange();
                RebuildSplitLapRows();

                // Si hay timing guardado, lo consideramos "guardado"
                HasSavedSplit = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando split existente: {ex.Message}");
        }
    }

    private async Task SaveSplitAsync()
    {
        if (_videoClip == null || !CanSaveSplit)
            return;

        try
        {
            var splitData = new SplitTimeData
            {
                StartMs = (long)_splitStartTime!.Value.TotalMilliseconds,
                EndMs = (long)_splitEndTime!.Value.TotalMilliseconds,
                DurationMs = (long)_splitDuration!.Value.TotalMilliseconds
            };

            var json = System.Text.Json.JsonSerializer.Serialize(splitData);

            await _databaseService.SaveSplitTimeAsync(_videoClip.Id, _videoClip.SessionId, json);

            // Guardar también laps/inicio/fin en tabla dedicada (mismo formato que CameraPage)
            await _databaseService.DeleteExecutionTimingEventsByVideoAsync(_videoClip.Id);

            var sessionId = _videoClip.SessionId;
            var athleteId = _videoClip.AtletaId;
            var sectionId = _videoClip.Section;

            var startMs = splitData.StartMs;
            var endMs = splitData.EndMs;

            var events = new List<ExecutionTimingEvent>();

            events.Add(new ExecutionTimingEvent
            {
                VideoId = _videoClip.Id,
                SessionId = sessionId,
                AthleteId = athleteId,
                SectionId = sectionId,
                Kind = 0,
                ElapsedMilliseconds = startMs,
                SplitMilliseconds = 0,
                LapIndex = 0,
                RunIndex = 0,
                CreatedAtUnixSeconds = DateTimeOffset.Now.ToUnixTimeSeconds(),
            });

            _splitLapMarksMs.Sort();
            var prev = startMs;
            var lapIndex = 0;
            foreach (var lapMs in _splitLapMarksMs)
            {
                if (lapMs <= startMs || lapMs >= endMs)
                    continue;

                lapIndex++;
                var splitMs = lapMs - prev;
                if (splitMs < 0) splitMs = 0;
                prev = lapMs;

                events.Add(new ExecutionTimingEvent
                {
                    VideoId = _videoClip.Id,
                    SessionId = sessionId,
                    AthleteId = athleteId,
                    SectionId = sectionId,
                    Kind = 1,
                    ElapsedMilliseconds = lapMs,
                    SplitMilliseconds = splitMs,
                    LapIndex = lapIndex,
                    RunIndex = 0,
                    CreatedAtUnixSeconds = DateTimeOffset.Now.ToUnixTimeSeconds(),
                });
            }

            // FIN: SplitMs guarda el total (start->end) igual que CameraPage
            events.Add(new ExecutionTimingEvent
            {
                VideoId = _videoClip.Id,
                SessionId = sessionId,
                AthleteId = athleteId,
                SectionId = sectionId,
                Kind = 2,
                ElapsedMilliseconds = endMs,
                SplitMilliseconds = splitData.DurationMs,
                LapIndex = 0,
                RunIndex = 0,
                CreatedAtUnixSeconds = DateTimeOffset.Now.ToUnixTimeSeconds(),
            });

            await _databaseService.InsertExecutionTimingEventsAsync(events);
            HasSavedSplit = true;

            // Opcional: cerrar el panel después de guardar
            // ShowSplitTimePanel = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error guardando split: {ex.Message}");
        }
    }

    #endregion
}

/// <summary>
/// Clase helper para opciones de filtro en dropdowns
/// </summary>
public class FilterOption<T>
{
    public T? Value { get; }
    public string DisplayName { get; }
    
    public FilterOption(T? value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
    
    public override string ToString() => DisplayName;
}
