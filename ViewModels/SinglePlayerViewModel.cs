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
    private TimeSpan _currentPosition;
    private TimeSpan _duration;
    private double _progress;
    private double _playbackSpeed = 1.0;
    
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
            // Cerrar otros paneles si se va a abrir este
            if (!ShowSectionAssignPanel)
            {
                ShowAthleteAssignPanel = false;
                ShowTagsAssignPanel = false;
                ShowTagEventsPanel = false;
            }
            ShowSectionAssignPanel = !ShowSectionAssignPanel;
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
        ClearSplitCommand = new Command(ClearSplit);
        SaveSplitCommand = new Command(async () => await SaveSplitAsync(), () => CanSaveSplit);
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

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            _currentPosition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionText));
            UpdateProgress();
            System.Diagnostics.Debug.WriteLine($"[SinglePlayerVM] CurrentPosition={value}, Progress={_progress}");
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            System.Diagnostics.Debug.WriteLine($"[SinglePlayerVM] Duration SET: {value}");
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
    public ICommand ClearSplitCommand { get; }
    public ICommand SaveSplitCommand { get; }

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
    /// Notifica al Dashboard que un video ha sido actualizado para que refresque solo ese item
    /// </summary>
    private void NotifyVideoClipUpdated()
    {
        if (_videoClip != null && _videoClip.Id > 0)
        {
            MessagingCenter.Send(this, "VideoClipUpdated", _videoClip.Id);
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
        // Cerrar otros paneles si se va a abrir este
        if (!ShowAthleteAssignPanel)
        {
            ShowSectionAssignPanel = false;
            ShowTagsAssignPanel = false;
            ShowTagEventsPanel = false;
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
        
        ShowAthleteAssignPanel = !ShowAthleteAssignPanel;
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
        // Cerrar otros paneles si se va a abrir este
        if (!ShowTagsAssignPanel)
        {
            ShowAthleteAssignPanel = false;
            ShowSectionAssignPanel = false;
            ShowTagEventsPanel = false;
            
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
        
        ShowTagsAssignPanel = !ShowTagsAssignPanel;
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
        // Cerrar otros paneles si se va a abrir este
        if (!ShowTagEventsPanel)
        {
            ShowAthleteAssignPanel = false;
            ShowSectionAssignPanel = false;
            ShowTagsAssignPanel = false;
            
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
        
        ShowTagEventsPanel = !ShowTagEventsPanel;
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

    private void ToggleSplitTimePanel()
    {
        // Cerrar otros paneles si se va a abrir este
        if (!ShowSplitTimePanel)
        {
            ShowAthleteAssignPanel = false;
            ShowSectionAssignPanel = false;
            ShowTagsAssignPanel = false;
            ShowTagEventsPanel = false;
        }
        ShowSplitTimePanel = !ShowSplitTimePanel;

        // Al abrir, cargar el split existente si lo hay
        if (ShowSplitTimePanel && _videoClip != null)
        {
            _ = LoadExistingSplitAsync();
        }
    }

    private void SetSplitStart()
    {
        SplitStartTime = CurrentPosition;
    }

    private void SetSplitEnd()
    {
        SplitEndTime = CurrentPosition;
    }

    private void ClearSplit()
    {
        SplitStartTime = null;
        SplitEndTime = null;
        SplitDuration = null;
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
