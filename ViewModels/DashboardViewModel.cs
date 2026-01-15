using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// Modelo que combina una sesión con su diario asociado para la vista del calendario
/// </summary>
public class SessionTypeOption : INotifyPropertyChanged
{
    private bool _isSelected;
    
    public string Name { get; set; } = "";
    
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class SessionWithDiary : INotifyPropertyChanged
{
    private SessionDiary? _diary;
    private int _editValoracionFisica = 3;
    private int _editValoracionMental = 3;
    private int _editValoracionTecnica = 3;
    private string _editNotas = "";
    private ObservableCollection<VideoClip> _videos = new();

    public Session Session { get; set; } = null!;
    
    /// <summary>Vídeos de la sesión (máximo 6 para la mini galería)</summary>
    public ObservableCollection<VideoClip> Videos
    {
        get => _videos;
        set
        {
            _videos = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVideos));
            OnPropertyChanged(nameof(VideoCount));
            OnPropertyChanged(nameof(ExtraVideosCount));
            OnPropertyChanged(nameof(HasExtraVideos));
        }
    }
    
    public bool HasVideos => Videos.Count > 0;
    public int VideoCount { get; set; }
    public int ExtraVideosCount => Math.Max(0, VideoCount - 6);
    public bool HasExtraVideos => ExtraVideosCount > 0;
    
    public SessionDiary? Diary
    {
        get => _diary;
        set
        {
            _diary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiary));
            OnPropertyChanged(nameof(HasValoraciones));
            // Inicializar valores de edición desde el diario existente
            if (_diary != null)
            {
                EditValoracionFisica = _diary.ValoracionFisica > 0 ? _diary.ValoracionFisica : 3;
                EditValoracionMental = _diary.ValoracionMental > 0 ? _diary.ValoracionMental : 3;
                EditValoracionTecnica = _diary.ValoracionTecnica > 0 ? _diary.ValoracionTecnica : 3;
                EditNotas = _diary.Notas ?? "";
            }
        }
    }

    public bool HasDiary => Diary != null;
    public bool HasValoraciones => Diary != null && 
        (Diary.ValoracionFisica > 0 || Diary.ValoracionMental > 0 || Diary.ValoracionTecnica > 0);
    public bool HasNotas => Diary != null && !string.IsNullOrWhiteSpace(Diary.Notas);

    // Propiedades para el formulario de edición
    public int EditValoracionFisica
    {
        get => _editValoracionFisica;
        set { _editValoracionFisica = value; OnPropertyChanged(); }
    }

    public int EditValoracionMental
    {
        get => _editValoracionMental;
        set { _editValoracionMental = value; OnPropertyChanged(); }
    }

    public int EditValoracionTecnica
    {
        get => _editValoracionTecnica;
        set { _editValoracionTecnica = value; OnPropertyChanged(); }
    }

    public string EditNotas
    {
        get => _editNotas;
        set { _editNotas = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Modelo para los iconos del picker con estado de selección
/// </summary>
public class IconPickerItem : INotifyPropertyChanged
{
    private bool _isSelected;
    
    public string Name { get; set; } = "";
    
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// ViewModel para la página principal / Dashboard
/// </summary>
public class DashboardViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private readonly ITrashService _trashService;
        private int _trashItemCount;
    private readonly CrownFileService _crownFileService;
    private readonly StatisticsService _statisticsService;
    private readonly ThumbnailService _thumbnailService;
    private readonly IHealthKitService _healthKitService;
    private readonly ITableExportService _tableExportService;
    private readonly ImportProgressService? _importProgressService;

    private DashboardStats? _stats;
    private Session? _selectedSession;
    private bool _isAllGallerySelected;
    private bool _isVideoLessonsSelected;
    private bool _isDiaryViewSelected;
    private bool _isUserLibraryExpanded = true;
    private bool _isSessionsListExpanded = true;
    private bool _isLoadingSelectedSessionVideos;

    private GridLength _mainPanelWidth = new(2.5, GridUnitType.Star);
    private GridLength _rightPanelWidth = new(1.2, GridUnitType.Star);
    private GridLength _rightSplitterWidth = new(8);
    private string _importProgressText = "";

    // Importación en segundo plano
    private bool _isBackgroundImporting;
    private int _backgroundImportProgress;
    private string _backgroundImportText = "";
    private string _importingSessionName = "";
    private string _importingCurrentFile = "";

    private readonly Dictionary<string, bool> _sessionGroupExpandedState = new();

    // Vista Diario (calendario)
    private DateTime _selectedDiaryDate = DateTime.Today;
    private List<SessionDiary> _diaryEntriesForMonth = new();
    private List<Session> _sessionsForMonth = new();
    private List<DailyWellness> _wellnessDataForMonth = new();
    private SessionDiary? _selectedDateDiary;
    private ObservableCollection<SessionWithDiary> _selectedDateSessionsWithDiary = new();
    private int _importProgressValue;
    private bool _isImporting;

    // Nueva sesión manual
    private bool _isAddingNewSession;
    private string _newSessionName = "";
    private string _newSessionType = "Gimnasio";
    private string _newSessionLugar = "";
    private bool _showNewSessionSidebarPopup;

    // Carpetas inteligentes (galería)
    private const string SmartFoldersPreferencesKey = "SmartFolders";
    private bool _showSmartFolderSidebarPopup;
    private string _newSmartFolderName = "";
    private string _newSmartFolderMatchMode = "All"; // All=AND, Any=OR
    private int _newSmartFolderLiveMatchCount;
    private SmartFolderDefinition? _activeSmartFolder;
    private List<VideoClip>? _smartFolderFilteredVideosCache;

    // Evita que al limpiar filtros se disparen ApplyFiltersAsync múltiples veces.
    private bool _suppressFilterSelectionChanged;
    // Versión para descartar cálculos de filtros fuera de orden (race conditions).
    private int _filtersVersion;

    // Popup de personalización de icono/color
    private bool _showIconColorPickerPopup;
    private SmartFolderDefinition? _iconColorPickerTargetSmartFolder;
    private SessionRow? _iconColorPickerTargetSession;

    // HealthKit / Datos de Salud (solo iOS) y Bienestar manual
    private DailyHealthData? _selectedDateHealthData;
    private bool _isHealthKitAuthorized;
    private bool _isLoadingHealthData;
    
    // Bienestar diario (entrada manual)
    private DailyWellness? _selectedDateWellness;
    private bool _isEditingWellness;
    private double? _wellnessSleepHours;
    private int? _wellnessSleepQuality;
    private int? _wellnessRecoveryFeeling;
    private int? _wellnessMuscleFatigue;
    private int? _wellnessMoodRating;
    private int? _wellnessRestingHeartRate;
    private int? _wellnessHRV;
    private string? _wellnessNotes;

    // Pestañas columna derecha
    private bool _isStatsTabSelected;
    private bool _isCrudTechTabSelected = true;
    private bool _isDiaryTabSelected;
    private bool _isQuickAnalysisIsolatedMode;

    private int _videoGalleryColumnSpan = DeviceInfo.Current.Platform == DevicePlatform.iOS ? 3 : 4;

    // Diario de sesión
    private SessionDiary? _currentSessionDiary;
    private bool _isEditingDiary;
    private int _diaryValoracionFisica = 3;
    private int _diaryValoracionMental = 3;
    private int _diaryValoracionTecnica = 3;
    private string _diaryNotas = "";
    private double _avgValoracionFisica;
    private double _avgValoracionMental;
    private double _avgValoracionTecnica;
    private int _avgValoracionCount;
    private int _selectedEvolutionPeriod = 1; // 0=Semana, 1=Mes, 2=Año, 3=Todo
    private ObservableCollection<SessionDiary> _valoracionEvolution = new();
    
    // Datos para el gráfico de líneas múltiples
    private ObservableCollection<int> _evolutionFisicaValues = new();
    private ObservableCollection<int> _evolutionMentalValues = new();
    private ObservableCollection<int> _evolutionTecnicaValues = new();
    private ObservableCollection<string> _evolutionLabels = new();

    // Selección múltiple de videos
    private bool _isMultiSelectMode;
    private bool _isSelectAllActive;
    private readonly HashSet<int> _selectedVideoIds = new();

    // Modo de análisis: único, paralelo o cuádruple (paralelo por defecto)
    private bool _isSingleVideoMode = false;
    private bool _isQuadVideoMode = false;
    
    // Orientación análisis paralelo (horizontal por defecto)
    private bool _isHorizontalOrientation = true;

    // Vídeos para análisis paralelo/cuádruple
    private VideoClip? _parallelVideo1;
    private VideoClip? _parallelVideo2;
    private VideoClip? _parallelVideo3;
    private VideoClip? _parallelVideo4;
    private bool _isPreviewMode;

    // Preview readiness: usamos esto para mantener la miniatura visible
    // hasta que el PrecisionVideoPlayer haya abierto el medio.
    private bool _isPreviewPlayer1Ready;
    private bool _isPreviewPlayer2Ready;
    private bool _isPreviewPlayer3Ready;
    private bool _isPreviewPlayer4Ready;

    // Flag para indicar si hay actualizaciones de estadísticas pendientes
    private bool _hasStatsUpdatePending;
    private readonly HashSet<int> _modifiedVideoIds = new();

    // Lazy loading
    private const int PageSize = 40;
    private int _currentPage;
    private bool _hasMoreVideos;
    private bool _isLoadingMore;
    private List<VideoClip>? _allVideosCache; // Cache para paginación
    private List<VideoClip>? _filteredVideosCache; // Cache de videos filtrados
    private List<Input>? _allInputsCache; // Cache de inputs para filtrado por tag
    private List<Tag>? _allTagsCache; // Cache de tags

    // Filtros para Galería General (fechas mantienen selección simple)
    private DateTime? _selectedFilterDateFrom;
    private DateTime? _selectedFilterDateTo;

    // Edición en lote
    private bool _showBatchEditPopup;
    private ObservableCollection<Athlete> _batchEditAthletes = new();
    private ObservableCollection<Tag> _batchEditTags = new();
    private Athlete? _selectedBatchAthlete;
    private int _batchEditSection;
    private bool _batchEditSectionEnabled;
    private bool _batchEditAthleteEnabled;
    private string? _newBatchAthleteSurname;
    private string? _newBatchAthleteName;
    private string? _newBatchTagText;
    private readonly HashSet<int> _batchEditSelectedTagIds = new();

    // Edición de sesión individual
    private bool _showSessionEditPopup;
    private SessionRow? _editingSessionRow;
    private string? _sessionEditName;
    private string? _sessionEditLugar;
    private string? _sessionEditTipoSesion;

    private CancellationTokenSource? _selectedSessionVideosCts;

    private sealed record GalleryStatsSnapshot(
        List<SectionStats> SectionStats,
        List<double> SectionDurationMinutes,
        List<string> SectionLabels,
        List<SessionAthleteTimeRow> AthleteTimes);

    private static async Task ReplaceCollectionInBatchesAsync<T>(
        ObservableCollection<T> target,
        IEnumerable<T> items,
        CancellationToken ct,
        int batchSize = 20)
    {
        target.Clear();

        var i = 0;
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested)
                return;

            target.Add(item);
            i++;

            if (i % batchSize == 0)
                await Task.Yield();
        }
    }

    private static GalleryStatsSnapshot BuildGalleryStatsSnapshot(List<VideoClip> clips)
    {
        var sectionStats = clips
            .GroupBy(v => v.Section)
            .OrderBy(g => g.Key)
            .Select(g => new SectionStats
            {
                Section = g.Key,
                VideoCount = g.Count(),
                TotalDuration = g.Sum(v => v.ClipDuration)
            })
            .ToList();

        var sectionMinutes = sectionStats.Select(s => Math.Round(s.TotalDuration / 60.0, 1)).ToList();
        var sectionLabels = sectionStats.Select(s => s.Section.ToString()).ToList();

        var byAthlete = clips
            .Where(v => v.AtletaId != 0)
            .GroupBy(v => v.AtletaId)
            .Select(g => new SessionAthleteTimeRow(
                g.FirstOrDefault()?.Atleta?.NombreCompleto ?? $"Atleta {g.Key}",
                g.Count(),
                g.Sum(v => v.ClipDuration),
                g.Any() ? g.Average(v => v.ClipDuration) : 0))
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        return new GalleryStatsSnapshot(sectionStats, sectionMinutes, sectionLabels, byAthlete);
    }

    private async Task ApplyGalleryStatsSnapshotAsync(GalleryStatsSnapshot snapshot, CancellationToken ct)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (ct.IsCancellationRequested)
                return;

            SelectedSessionSectionStats.Clear();
            SelectedSessionAthleteTimes.Clear();
            SelectedSessionSectionDurationMinutes.Clear();
            SelectedSessionSectionLabels.Clear();
            SelectedSessionTagVideoCounts.Clear();
            SelectedSessionTagLabels.Clear();

            var i = 0;
            foreach (var s in snapshot.SectionStats)
            {
                if (ct.IsCancellationRequested)
                    return;

                SelectedSessionSectionStats.Add(s);
                i++;
                if (i % 20 == 0)
                    await Task.Yield();
            }

            foreach (var m in snapshot.SectionDurationMinutes)
                SelectedSessionSectionDurationMinutes.Add(m);
            foreach (var l in snapshot.SectionLabels)
                SelectedSessionSectionLabels.Add(l);

            i = 0;
            foreach (var row in snapshot.AthleteTimes)
            {
                if (ct.IsCancellationRequested)
                    return;

                SelectedSessionAthleteTimes.Add(row);
                i++;
                if (i % 20 == 0)
                    await Task.Yield();
            }
        });
    }

    public DashboardStats? Stats
    {
        get => _stats;
        set
        {
            if (SetProperty(ref _stats, value))
            {
                OnPropertyChanged(nameof(AllGalleryItemCount));
            }
        }
    }

    // Contador para el item fijo "Galería General" en el sidebar.
    // Usa cache cuando está disponible; si no, cae a Stats para evitar mostrar 0.
    public int AllGalleryItemCount => _allVideosCache?.Count ?? Stats?.TotalVideos ?? 0;

    public Session? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                if (value != null)
                {
                    IsAllGallerySelected = false;
                    IsVideoLessonsSelected = false;
                    IsDiaryViewSelected = false;
                }
                else
                {
                    // Deseleccionar el SessionRow actual cuando SelectedSession = null
                    if (_selectedSessionListItem is SessionRow oldRow)
                    {
                        oldRow.IsSelected = false;
                    }
                    _selectedSessionListItem = null;
                    OnPropertyChanged(nameof(SelectedSessionListItem));
                }
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(HasSpecificSessionSelected));
                OnPropertyChanged(nameof(CanShowRecordButton));
                _ = LoadSelectedSessionVideosAsync(value);
                
                // Recargar datos de la pestaña activa
                if (IsDiaryTabSelected)
                {
                    _ = LoadSessionDiaryAsync();
                }
            }
        }
    }

    public bool IsAllGallerySelected
    {
        get => _isAllGallerySelected;
        set
        {
            if (SetProperty(ref _isAllGallerySelected, value))
            {
                if (value)
                {
                    IsVideoLessonsSelected = false;
                    IsDiaryViewSelected = false;
                    ClearSectionTimes();
                }
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(VideoCountDisplayText));
                OnPropertyChanged(nameof(ShowSectionTimesTable));
                OnPropertyChanged(nameof(HasSpecificSessionSelected));
            }
        }
    }

    public bool IsVideoLessonsSelected
    {
        get => _isVideoLessonsSelected;
        set
        {
            if (SetProperty(ref _isVideoLessonsSelected, value))
            {
                if (value)
                {
                    IsAllGallerySelected = false;
                    IsDiaryViewSelected = false;
                    ClearSectionTimes();
                }
                UpdateRightPanelLayout();
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(VideoCountDisplayText));
                OnPropertyChanged(nameof(ShowSectionTimesTable));
                OnPropertyChanged(nameof(HasSpecificSessionSelected));
                OnPropertyChanged(nameof(ShowVideoGallery));
            }
        }
    }

    public bool IsDiaryViewSelected
    {
        get => _isDiaryViewSelected;
        set
        {
            if (SetProperty(ref _isDiaryViewSelected, value))
            {
                if (value)
                {
                    IsAllGallerySelected = false;
                    IsVideoLessonsSelected = false;
                    SelectedSession = null;
                    ClearSectionTimes();
                    _ = LoadDiaryViewDataAsync();
                }
                UpdateRightPanelLayout();
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(VideoCountDisplayText));
                OnPropertyChanged(nameof(ShowSectionTimesTable));
                OnPropertyChanged(nameof(HasSpecificSessionSelected));
                OnPropertyChanged(nameof(ShowVideoGallery));
            }
        }
    }

    public DateTime SelectedDiaryDate
    {
        get => _selectedDiaryDate;
        set
        {
            if (SetProperty(ref _selectedDiaryDate, value))
            {
                _ = LoadDiaryForDateAsync(value);
                _ = LoadWellnessDataForDateAsync(value);
            }
        }
    }

    public List<SessionDiary> DiaryEntriesForMonth
    {
        get => _diaryEntriesForMonth;
        set => SetProperty(ref _diaryEntriesForMonth, value);
    }

    public List<Session> SessionsForMonth
    {
        get => _sessionsForMonth;
        set => SetProperty(ref _sessionsForMonth, value);
    }

    public List<DailyWellness> WellnessDataForMonth
    {
        get => _wellnessDataForMonth;
        set
        {
            if (SetProperty(ref _wellnessDataForMonth, value))
            {
                OnPropertyChanged(nameof(AverageWellnessScore));
            }
        }
    }

    /// <summary>
    /// Media del WellnessScore de todos los datos del mes
    /// </summary>
    public int? AverageWellnessScore
    {
        get
        {
            if (_wellnessDataForMonth == null || _wellnessDataForMonth.Count == 0)
                return null;
            
            var scoresWithData = _wellnessDataForMonth
                .Where(w => w.HasData && w.WellnessScore > 0)
                .Select(w => w.WellnessScore)
                .ToList();
            
            if (scoresWithData.Count == 0)
                return null;
            
            return (int)Math.Round(scoresWithData.Average());
        }
    }

    public SessionDiary? SelectedDateDiary
    {
        get => _selectedDateDiary;
        set
        {
            if (SetProperty(ref _selectedDateDiary, value))
            {
                OnPropertyChanged(nameof(HasSelectedDateDiary));
            }
        }
    }

    public bool HasSelectedDateDiary => SelectedDateDiary != null;

    public ObservableCollection<SessionWithDiary> SelectedDateSessionsWithDiary
    {
        get => _selectedDateSessionsWithDiary;
        set => SetProperty(ref _selectedDateSessionsWithDiary, value);
    }

    public bool HasSelectedDateSessions => SelectedDateSessionsWithDiary.Count > 0;

    // Propiedades para nueva sesión manual
    public bool IsAddingNewSession
    {
        get => _isAddingNewSession;
        set => SetProperty(ref _isAddingNewSession, value);
    }

    public string NewSessionName
    {
        get => _newSessionName;
        set => SetProperty(ref _newSessionName, value);
    }

    public string NewSessionType
    {
        get => _newSessionType;
        set => SetProperty(ref _newSessionType, value);
    }

    public string NewSessionLugar
    {
        get => _newSessionLugar;
        set => SetProperty(ref _newSessionLugar, value);
    }

    public bool ShowNewSessionSidebarPopup
    {
        get => _showNewSessionSidebarPopup;
        set => SetProperty(ref _showNewSessionSidebarPopup, value);
    }

    public bool ShowSmartFolderSidebarPopup
    {
        get => _showSmartFolderSidebarPopup;
        set => SetProperty(ref _showSmartFolderSidebarPopup, value);
    }

    public bool ShowIconColorPickerPopup
    {
        get => _showIconColorPickerPopup;
        set => SetProperty(ref _showIconColorPickerPopup, value);
    }

    public SmartFolderDefinition? IconColorPickerTargetSmartFolder
    {
        get => _iconColorPickerTargetSmartFolder;
        set => SetProperty(ref _iconColorPickerTargetSmartFolder, value);
    }

    public SessionRow? IconColorPickerTargetSession
    {
        get => _iconColorPickerTargetSession;
        set => SetProperty(ref _iconColorPickerTargetSession, value);
    }

    public bool IsPickerForSmartFolder => IconColorPickerTargetSmartFolder != null;
    public bool IsPickerForSession => IconColorPickerTargetSession != null;

    public string IconColorPickerTitle => IsPickerForSmartFolder 
        ? IconColorPickerTargetSmartFolder?.Name ?? "Carpeta" 
        : IconColorPickerTargetSession?.Session?.DisplayName ?? "Sesión";

    // Icono y color actualmente seleccionado del elemento target
    public string SelectedPickerIcon => IsPickerForSmartFolder 
        ? IconColorPickerTargetSmartFolder?.Icon ?? "folder"
        : IconColorPickerTargetSession?.Session?.Icon ?? "oar.2.crossed";

    public string SelectedPickerColor => IsPickerForSmartFolder 
        ? IconColorPickerTargetSmartFolder?.IconColor ?? "#FF888888"
        : IconColorPickerTargetSession?.Session?.IconColor ?? "#FF6DDDFF";

    // Iconos disponibles para el picker
    public ObservableCollection<IconPickerItem> AvailableIcons { get; } = new()
    {
        new() { Name = "folder" }, new() { Name = "folder.fill" }, new() { Name = "star" }, new() { Name = "star.fill" },
        new() { Name = "heart" }, new() { Name = "heart.fill" }, new() { Name = "flag" }, new() { Name = "flag.fill" },
        new() { Name = "bookmark" }, new() { Name = "bookmark.fill" }, new() { Name = "tag" }, new() { Name = "tag.fill" },
        new() { Name = "bolt" }, new() { Name = "bolt.fill" }, new() { Name = "flame" }, new() { Name = "flame.fill" },
        new() { Name = "trophy" }, new() { Name = "trophy.fill" }, new() { Name = "figure.run" }, new() { Name = "oar.2.crossed" },
        new() { Name = "sportscourt" }, new() { Name = "bicycle" }, new() { Name = "figure.walk" }, new() { Name = "camera" },
        new() { Name = "video" }, new() { Name = "photo" }, new() { Name = "doc" }, new() { Name = "calendar" },
        new() { Name = "clock" }, new() { Name = "bell" }, new() { Name = "mappin" }, new() { Name = "location" },
        new() { Name = "house" }, new() { Name = "building.2" }, new() { Name = "car" }, new() { Name = "airplane" }
    };

    // Colores disponibles para el picker
    public List<string> AvailableColors { get; } = new()
    {
        "#FFFFFFFF", // Blanco
        "#FF888888", // Gris
        "#FFFF453A", // Rojo
        "#FFFF9F0A", // Naranja
        "#FFFFD60A", // Amarillo
        "#FF30D158", // Verde
        "#FF64D2FF", // Cyan
        "#FF0A84FF", // Azul
        "#FFBF5AF2", // Morado
        "#FFFF375F", // Rosa
        "#FF6DDDFF"  // Cyan claro (default sesión)
    };

    private bool _isSmartFoldersExpanded = true;
    public bool IsSmartFoldersExpanded
    {
        get => _isSmartFoldersExpanded;
        set => SetProperty(ref _isSmartFoldersExpanded, value);
    }

    private bool _isSessionsExpanded = true;
    public bool IsSessionsExpanded
    {
        get => _isSessionsExpanded;
        set => SetProperty(ref _isSessionsExpanded, value);
    }

    public ObservableCollection<SmartFolderDefinition> SmartFolders { get; } = new();
    public ObservableCollection<SmartFolderCriterion> NewSmartFolderCriteria { get; } = new();

    public ObservableCollection<string> SmartFolderFieldOptions { get; } = new()
    {
        "Lugar",
        "Deportista",
        "Sección",
        "Tag",
        "Fecha",
    };

    public string NewSmartFolderName
    {
        get => _newSmartFolderName;
        set => SetProperty(ref _newSmartFolderName, value);
    }

    public string NewSmartFolderMatchMode
    {
        get => _newSmartFolderMatchMode;
        set
        {
            if (SetProperty(ref _newSmartFolderMatchMode, value))
            {
                OnPropertyChanged(nameof(IsSmartFolderMatchAll));
                OnPropertyChanged(nameof(IsSmartFolderMatchAny));
                RecomputeNewSmartFolderLiveMatchCount();
            }
        }
    }

    public bool IsSmartFolderMatchAll
    {
        get => NewSmartFolderMatchMode == "All";
        set
        {
            if (value)
                NewSmartFolderMatchMode = "All";
        }
    }

    public bool IsSmartFolderMatchAny
    {
        get => NewSmartFolderMatchMode == "Any";
        set
        {
            if (value)
                NewSmartFolderMatchMode = "Any";
        }
    }

    public int NewSmartFolderLiveMatchCount
    {
        get => _newSmartFolderLiveMatchCount;
        private set => SetProperty(ref _newSmartFolderLiveMatchCount, value);
    }

    /// <summary>
    /// Indica si el botón Grabar debe mostrarse (solo iOS y con sesión seleccionada)
    /// </summary>
    public bool CanShowRecordButton => SelectedSession != null && DeviceInfo.Platform == DevicePlatform.iOS;

    public ObservableCollection<SessionTypeOption> SessionTypeOptions { get; } = new()
    {
        new SessionTypeOption { Name = "Gimnasio", IsSelected = true },
        new SessionTypeOption { Name = "Aguas tranquilas" },
        new SessionTypeOption { Name = "Carrera" },
        new SessionTypeOption { Name = "Ciclismo" },
        new SessionTypeOption { Name = "Esquí de fondo" },
        new SessionTypeOption { Name = "Natación" },
        new SessionTypeOption { Name = "Recuperación" },
        new SessionTypeOption { Name = "Otro" }
    };

    // Propiedades HealthKit
    public DailyHealthData? SelectedDateHealthData
    {
        get => _selectedDateHealthData;
        set
        {
            if (SetProperty(ref _selectedDateHealthData, value))
            {
                OnPropertyChanged(nameof(HasHealthData));
            }
        }
    }

    public bool HasHealthData => SelectedDateHealthData?.HasData == true;
    public bool IsHealthKitAvailable => _healthKitService.IsAvailable;

    public bool IsHealthKitAuthorized
    {
        get => _isHealthKitAuthorized;
        set => SetProperty(ref _isHealthKitAuthorized, value);
    }

    public bool IsLoadingHealthData
    {
        get => _isLoadingHealthData;
        set => SetProperty(ref _isLoadingHealthData, value);
    }

    // Propiedades Bienestar Diario (entrada manual)
    public DailyWellness? SelectedDateWellness
    {
        get => _selectedDateWellness;
        set
        {
            if (SetProperty(ref _selectedDateWellness, value))
            {
                OnPropertyChanged(nameof(HasWellnessData));
                // Sincronizar campos de edición
                if (value != null)
                {
                    WellnessSleepHours = value.SleepHours;
                    WellnessSleepQuality = value.SleepQuality;
                    WellnessRecoveryFeeling = value.RecoveryFeeling;
                    WellnessMuscleFatigue = value.MuscleFatigue;
                    WellnessMoodRating = value.MoodRating;
                    WellnessRestingHeartRate = value.RestingHeartRate;
                    WellnessHRV = value.HeartRateVariability;
                    WellnessNotes = value.Notes;
                }
                else
                {
                    ClearWellnessFields();
                }
            }
        }
    }

    public bool HasWellnessData => SelectedDateWellness?.HasData == true;

    public bool IsEditingWellness
    {
        get => _isEditingWellness;
        set => SetProperty(ref _isEditingWellness, value);
    }

    public double? WellnessSleepHours
    {
        get => _wellnessSleepHours;
        set => SetProperty(ref _wellnessSleepHours, value);
    }

    public int? WellnessSleepQuality
    {
        get => _wellnessSleepQuality;
        set => SetProperty(ref _wellnessSleepQuality, value);
    }

    public int? WellnessRecoveryFeeling
    {
        get => _wellnessRecoveryFeeling;
        set => SetProperty(ref _wellnessRecoveryFeeling, value);
    }

    public int? WellnessMuscleFatigue
    {
        get => _wellnessMuscleFatigue;
        set => SetProperty(ref _wellnessMuscleFatigue, value);
    }

    public int? WellnessMoodRating
    {
        get => _wellnessMoodRating;
        set => SetProperty(ref _wellnessMoodRating, value);
    }

    public int? WellnessRestingHeartRate
    {
        get => _wellnessRestingHeartRate;
        set => SetProperty(ref _wellnessRestingHeartRate, value);
    }

    public int? WellnessHRV
    {
        get => _wellnessHRV;
        set => SetProperty(ref _wellnessHRV, value);
    }

    public string? WellnessNotes
    {
        get => _wellnessNotes;
        set => SetProperty(ref _wellnessNotes, value);
    }

    // Opciones para selectores de bienestar
    public List<int> SleepQualityOptions { get; } = new() { 1, 2, 3, 4, 5 };
    public List<int> RecoveryFeelingOptions { get; } = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public List<int> MuscleFatigueOptions { get; } = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    public List<int> MoodRatingOptions { get; } = new() { 1, 2, 3, 4, 5 };

    public GridLength RightPanelWidth
    {
        get => _rightPanelWidth;
        set => SetProperty(ref _rightPanelWidth, value);
    }

    public GridLength MainPanelWidth
    {
        get => _mainPanelWidth;
        set => SetProperty(ref _mainPanelWidth, value);
    }

    public GridLength RightSplitterWidth
    {
        get => _rightSplitterWidth;
        set => SetProperty(ref _rightSplitterWidth, value);
    }

    private void UpdateRightPanelLayout()
    {
        if (IsVideoLessonsSelected)
        {
            MainPanelWidth = new GridLength(2.5, GridUnitType.Star);
            RightSplitterWidth = new GridLength(0);
            RightPanelWidth = new GridLength(0);
        }
        else if (IsDiaryViewSelected)
        {
            // En Diario, el modo aislado no aplica.
            MainPanelWidth = new GridLength(2.5, GridUnitType.Star);
            RightSplitterWidth = new GridLength(8);
            RightPanelWidth = new GridLength(1.2, GridUnitType.Star);
        }
        else if (_isQuickAnalysisIsolatedMode)
        {
            // Modo aislado:
            // - El sidebar izquierdo se mantiene fijo.
            // - Del espacio restante, la columna derecha (reproductores) ocupa 1/2 (1*)
            //   y el contenido principal el otro 1/2 (1*).
            // Nota: los anchos pueden ser ajustados por el usuario mediante el splitter;
            // aquí solo nos aseguramos de que el splitter esté visible.
            RightSplitterWidth = new GridLength(8);
        }
        else
        {
            MainPanelWidth = new GridLength(2.5, GridUnitType.Star);
            RightSplitterWidth = new GridLength(8);
            RightPanelWidth = new GridLength(1.2, GridUnitType.Star);
        }
    }

    public bool IsSessionsListExpanded
    {
        get => _isSessionsListExpanded;
        set
        {
            if (SetProperty(ref _isSessionsListExpanded, value))
            {
                // La visibilidad se controla llenando/vaciando la colección
                SyncVisibleSessionRows();
            }
        }
    }

    public bool IsUserLibraryExpanded
    {
        get => _isUserLibraryExpanded;
        set
        {
            if (SetProperty(ref _isUserLibraryExpanded, value))
            {
                // La visibilidad se controla llenando/vaciando la colección
                SyncVisibleSessionRows();
                SyncVisibleRecentSessions();
            }
        }
    }

    public string SelectedSessionTitle => IsDiaryViewSelected
        ? "Diario Personal"
        : (IsVideoLessonsSelected
            ? "Videolecciones"
            : (IsAllGallerySelected
                ? "Galería General"
                : (SelectedSession?.DisplayName ?? "Selecciona una sesión")));

    public bool IsLoadingSelectedSessionVideos
    {
        get => _isLoadingSelectedSessionVideos;
        private set => SetProperty(ref _isLoadingSelectedSessionVideos, value);
    }

    public string ImportProgressText
    {
        get => _importProgressText;
        set => SetProperty(ref _importProgressText, value);
    }

    public int ImportProgressValue
    {
        get => _importProgressValue;
        set => SetProperty(ref _importProgressValue, value);
    }

    public bool IsImporting
    {
        get => _isImporting;
        set => SetProperty(ref _isImporting, value);
    }

    /// <summary>
    /// Indica si hay una importación ejecutándose en segundo plano
    /// </summary>
    public bool IsBackgroundImporting
    {
        get => _isBackgroundImporting;
        set => SetProperty(ref _isBackgroundImporting, value);
    }

    /// <summary>
    /// Porcentaje de progreso de la importación en segundo plano (0-100)
    /// </summary>
    public int BackgroundImportProgress
    {
        get => _backgroundImportProgress;
        set => SetProperty(ref _backgroundImportProgress, value);
    }

    /// <summary>
    /// Texto descriptivo de la importación en segundo plano
    /// </summary>
    public string BackgroundImportText
    {
        get => _backgroundImportText;
        set => SetProperty(ref _backgroundImportText, value);
    }

    /// <summary>
    /// Nombre de la sesión que se está importando
    /// </summary>
    public string ImportingSessionName
    {
        get => _importingSessionName;
        set => SetProperty(ref _importingSessionName, value);
    }

    /// <summary>
    /// Archivo actual que se está procesando
    /// </summary>
    public string ImportingCurrentFile
    {
        get => _importingCurrentFile;
        set => SetProperty(ref _importingCurrentFile, value);
    }

    /// <summary>
    /// Progreso de importación como valor entre 0 y 1 para ProgressBar
    /// </summary>
    public double BackgroundImportProgressNormalized => BackgroundImportProgress / 100.0;

    public bool IsStatsTabSelected
    {
        get => _isStatsTabSelected;
        set
        {
            if (SetProperty(ref _isStatsTabSelected, value))
            {
                if (value)
                {
                    IsCrudTechTabSelected = false;
                    IsDiaryTabSelected = false;
                }
            }
        }
    }

    public bool IsCrudTechTabSelected
    {
        get => _isCrudTechTabSelected;
        set
        {
            if (SetProperty(ref _isCrudTechTabSelected, value))
            {
                if (value)
                {
                    IsStatsTabSelected = false;
                    IsDiaryTabSelected = false;
                }
            }
        }
    }

    public bool IsDiaryTabSelected
    {
        get => _isDiaryTabSelected;
        set
        {
            if (SetProperty(ref _isDiaryTabSelected, value))
            {
                if (value)
                {
                    IsStatsTabSelected = false;
                    IsCrudTechTabSelected = false;
                    _ = LoadSessionDiaryAsync();
                }
            }
        }
    }

    // Propiedades del Diario de Sesión
    public int DiaryValoracionFisica
    {
        get => _diaryValoracionFisica;
        set => SetProperty(ref _diaryValoracionFisica, Math.Clamp(value, 1, 5));
    }

    public int DiaryValoracionMental
    {
        get => _diaryValoracionMental;
        set => SetProperty(ref _diaryValoracionMental, Math.Clamp(value, 1, 5));
    }

    public int DiaryValoracionTecnica
    {
        get => _diaryValoracionTecnica;
        set => SetProperty(ref _diaryValoracionTecnica, Math.Clamp(value, 1, 5));
    }

    public string DiaryNotas
    {
        get => _diaryNotas;
        set => SetProperty(ref _diaryNotas, value ?? "");
    }

    public double AvgValoracionFisica
    {
        get => _avgValoracionFisica;
        set => SetProperty(ref _avgValoracionFisica, value);
    }

    public double AvgValoracionMental
    {
        get => _avgValoracionMental;
        set => SetProperty(ref _avgValoracionMental, value);
    }

    public double AvgValoracionTecnica
    {
        get => _avgValoracionTecnica;
        set => SetProperty(ref _avgValoracionTecnica, value);
    }

    public int AvgValoracionCount
    {
        get => _avgValoracionCount;
        set => SetProperty(ref _avgValoracionCount, value);
    }

    public int SelectedEvolutionPeriod
    {
        get => _selectedEvolutionPeriod;
        set
        {
            if (SetProperty(ref _selectedEvolutionPeriod, value))
            {
                _ = LoadValoracionEvolutionAsync();
            }
        }
    }

    public ObservableCollection<SessionDiary> ValoracionEvolution
    {
        get => _valoracionEvolution;
        set => SetProperty(ref _valoracionEvolution, value);
    }

    public ObservableCollection<int> EvolutionFisicaValues
    {
        get => _evolutionFisicaValues;
        set => SetProperty(ref _evolutionFisicaValues, value);
    }

    public ObservableCollection<int> EvolutionMentalValues
    {
        get => _evolutionMentalValues;
        set => SetProperty(ref _evolutionMentalValues, value);
    }

    public ObservableCollection<int> EvolutionTecnicaValues
    {
        get => _evolutionTecnicaValues;
        set => SetProperty(ref _evolutionTecnicaValues, value);
    }

    public ObservableCollection<string> EvolutionLabels
    {
        get => _evolutionLabels;
        set => SetProperty(ref _evolutionLabels, value);
    }

    public bool HasDiaryData => _currentSessionDiary != null;

    /// <summary>Indica si el usuario está editando el diario (muestra formulario vs vista de resultados)</summary>
    public bool IsEditingDiary
    {
        get => _isEditingDiary;
        set => SetProperty(ref _isEditingDiary, value);
    }

    /// <summary>Indica si debe mostrarse la vista de resultados del diario (datos guardados y no editando)</summary>
    public bool ShowDiaryResults => HasDiaryData && !IsEditingDiary;

    /// <summary>Indica si debe mostrarse el formulario del diario (sin datos o editando)</summary>
    public bool ShowDiaryForm => !HasDiaryData || IsEditingDiary;

    /// <summary>Indica si debe mostrarse la galería de vídeos (no en Videolecciones ni Diario)</summary>
    public bool ShowVideoGallery => !IsVideoLessonsSelected && !IsDiaryViewSelected;

    public bool IsMultiSelectMode
    {
        get => _isMultiSelectMode;
        set
        {
            if (SetProperty(ref _isMultiSelectMode, value))
            {
                if (!value)
                {
                    // Al desactivar, limpiar selección
                    ClearVideoSelection();
                }
                OnPropertyChanged(nameof(SelectedVideoCount));
            }
        }
    }

    public bool IsSelectAllActive
    {
        get => _isSelectAllActive;
        set
        {
            if (SetProperty(ref _isSelectAllActive, value))
            {
                if (value)
                {
                    SelectAllFilteredVideos();
                }
                else
                {
                    ClearVideoSelection();
                }
            }
        }
    }

    public int SelectedVideoCount => _selectedVideoIds.Count;

    public bool IsHorizontalOrientation
    {
        get => _isHorizontalOrientation;
        set
        {
            if (SetProperty(ref _isHorizontalOrientation, value))
            {
                OnPropertyChanged(nameof(IsVerticalOrientation));
                if (_isQuickAnalysisIsolatedMode)
                {
                    UpdateRightPanelLayout();
                }
            }
        }
    }

    public bool IsVerticalOrientation
    {
        get => !_isHorizontalOrientation;
        set
        {
            if (value != !_isHorizontalOrientation)
            {
                IsHorizontalOrientation = !value;
            }
        }
    }

    // Modo único (un solo video) vs paralelo (dos videos) vs cuádruple (cuatro videos)
    public bool IsSingleVideoMode
    {
        get => _isSingleVideoMode;
        set
        {
            if (SetProperty(ref _isSingleVideoMode, value))
            {
                if (value)
                {
                    _isQuadVideoMode = false;
                    OnPropertyChanged(nameof(IsQuadVideoMode));
                }
                OnPropertyChanged(nameof(IsParallelVideoMode));
                // Limpiar slots extra si cambiamos a modo único
                if (value)
                {
                    ParallelVideo2 = null;
                    ParallelVideo3 = null;
                    ParallelVideo4 = null;
                }

                if (_isQuickAnalysisIsolatedMode)
                {
                    UpdateRightPanelLayout();
                }
            }
        }
    }

    public bool IsParallelVideoMode
    {
        get => !_isSingleVideoMode && !_isQuadVideoMode;
        set
        {
            if (value && (!IsParallelVideoMode))
            {
                _isSingleVideoMode = false;
                _isQuadVideoMode = false;
                OnPropertyChanged(nameof(IsSingleVideoMode));
                OnPropertyChanged(nameof(IsQuadVideoMode));
                OnPropertyChanged(nameof(IsParallelVideoMode));
                // Limpiar slots 3 y 4 si pasamos a paralelo
                ParallelVideo3 = null;
                ParallelVideo4 = null;

                if (_isQuickAnalysisIsolatedMode)
                {
                    UpdateRightPanelLayout();
                }
            }
        }
    }

    public bool IsQuadVideoMode
    {
        get => _isQuadVideoMode;
        set
        {
            if (SetProperty(ref _isQuadVideoMode, value))
            {
                if (value)
                {
                    _isSingleVideoMode = false;
                    OnPropertyChanged(nameof(IsSingleVideoMode));
                }
                OnPropertyChanged(nameof(IsParallelVideoMode));

                if (_isQuickAnalysisIsolatedMode)
                {
                    UpdateRightPanelLayout();
                }
            }
        }
    }

    public bool IsQuickAnalysisIsolatedMode
    {
        get => _isQuickAnalysisIsolatedMode;
        set
        {
            if (SetProperty(ref _isQuickAnalysisIsolatedMode, value))
            {
                if (value)
                {
                    // Valor por defecto al activar: 50/50 del espacio restante.
                    // A partir de aquí, el usuario puede ajustarlo con el splitter.
                    MainPanelWidth = new GridLength(1, GridUnitType.Star);
                    RightPanelWidth = new GridLength(1, GridUnitType.Star);
                }
                UpdateRightPanelLayout();
            }
        }
    }

    public int VideoGalleryColumnSpan
    {
        get => _videoGalleryColumnSpan;
        set
        {
            var clamped = Math.Clamp(value, 1, 4);
            SetProperty(ref _videoGalleryColumnSpan, clamped);
        }
    }

    public bool IsVideoSelected(int videoId) => _selectedVideoIds.Contains(videoId);

    public VideoClip? ParallelVideo1
    {
        get => _parallelVideo1;
        set
        {
            if (SetProperty(ref _parallelVideo1, value))
            {
                IsPreviewPlayer1Ready = false;
                OnPropertyChanged(nameof(HasParallelVideo1));
                OnPropertyChanged(nameof(ParallelVideo1ClipPath));
                OnPropertyChanged(nameof(ParallelVideo1ThumbnailPath));
                OnPropertyChanged(nameof(ShowParallelVideo1Thumbnail));
                // Forzar refresh del modo preview con delay para que el UI se actualice
                if (value != null)
                {
                    _ = RefreshPreviewModeAsync();
                }
            }
        }
    }

    public VideoClip? ParallelVideo2
    {
        get => _parallelVideo2;
        set
        {
            if (SetProperty(ref _parallelVideo2, value))
            {
                IsPreviewPlayer2Ready = false;
                OnPropertyChanged(nameof(HasParallelVideo2));
                OnPropertyChanged(nameof(ParallelVideo2ClipPath));
                OnPropertyChanged(nameof(ParallelVideo2ThumbnailPath));
                OnPropertyChanged(nameof(ShowParallelVideo2Thumbnail));
                // Forzar refresh del modo preview con delay para que el UI se actualice
                if (value != null)
                {
                    _ = RefreshPreviewModeAsync();
                }
            }
        }
    }

    public bool HasParallelVideo1 => _parallelVideo1 != null;
    public bool HasParallelVideo2 => _parallelVideo2 != null;
    public bool HasParallelVideo3 => _parallelVideo3 != null;
    public bool HasParallelVideo4 => _parallelVideo4 != null;

    public string? ParallelVideo1ClipPath => _parallelVideo1?.LocalClipPath ?? _parallelVideo1?.ClipPath;
    public string? ParallelVideo1ThumbnailPath => _parallelVideo1?.LocalThumbnailPath ?? _parallelVideo1?.ThumbnailPath;
    public string? ParallelVideo2ClipPath => _parallelVideo2?.LocalClipPath ?? _parallelVideo2?.ClipPath;
    public string? ParallelVideo2ThumbnailPath => _parallelVideo2?.LocalThumbnailPath ?? _parallelVideo2?.ThumbnailPath;
    public string? ParallelVideo3ClipPath => _parallelVideo3?.LocalClipPath ?? _parallelVideo3?.ClipPath;
    public string? ParallelVideo3ThumbnailPath => _parallelVideo3?.LocalThumbnailPath ?? _parallelVideo3?.ThumbnailPath;
    public string? ParallelVideo4ClipPath => _parallelVideo4?.LocalClipPath ?? _parallelVideo4?.ClipPath;
    public string? ParallelVideo4ThumbnailPath => _parallelVideo4?.LocalThumbnailPath ?? _parallelVideo4?.ThumbnailPath;

    public VideoClip? ParallelVideo3
    {
        get => _parallelVideo3;
        set
        {
            if (SetProperty(ref _parallelVideo3, value))
            {
                IsPreviewPlayer3Ready = false;
                OnPropertyChanged(nameof(HasParallelVideo3));
                OnPropertyChanged(nameof(ParallelVideo3ClipPath));
                OnPropertyChanged(nameof(ParallelVideo3ThumbnailPath));
                OnPropertyChanged(nameof(ShowParallelVideo3Thumbnail));
                // Forzar refresh del modo preview con delay para que el UI se actualice
                if (value != null)
                {
                    _ = RefreshPreviewModeAsync();
                }
            }
        }
    }

    public VideoClip? ParallelVideo4
    {
        get => _parallelVideo4;
        set
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] ParallelVideo4 SETTER: value={(value != null ? $"Id={value.Id}, ClipPath={value.ClipPath}, LocalClipPath={value.LocalClipPath}" : "null")}");
            if (SetProperty(ref _parallelVideo4, value))
            {
                IsPreviewPlayer4Ready = false;
                OnPropertyChanged(nameof(HasParallelVideo4));
                OnPropertyChanged(nameof(ParallelVideo4ClipPath));
                OnPropertyChanged(nameof(ParallelVideo4ThumbnailPath));
                OnPropertyChanged(nameof(ShowParallelVideo4Thumbnail));
                System.Diagnostics.Debug.WriteLine($"[Dashboard] ParallelVideo4 AFTER SET: HasParallelVideo4={HasParallelVideo4}, ClipPath={ParallelVideo4ClipPath}, ThumbnailPath={ParallelVideo4ThumbnailPath}");
                // Forzar refresh del modo preview con delay para que el UI se actualice
                if (value != null)
                {
                    _ = RefreshPreviewModeAsync();
                }
            }
        }
    }

    public async Task SetParallelVideoSlotAsync(int slot, VideoClip video)
    {
        if (video == null) return;

        System.Diagnostics.Debug.WriteLine($"[Dashboard] SetParallelVideoSlotAsync: slot={slot}, video.Id={video.Id}, ClipPath={video.ClipPath}, LocalClipPath={video.LocalClipPath}");

        // Resolver path local si es posible
        var resolvedPath = await ResolveVideoPathAsync(video);
        System.Diagnostics.Debug.WriteLine($"[Dashboard] ResolvedPath for slot {slot}: {resolvedPath}");
        
        if (!string.IsNullOrWhiteSpace(resolvedPath))
            video.LocalClipPath = resolvedPath;

        // Asignar al slot correspondiente en hilo UI
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] Assigning video to slot {slot} on UI thread");
            switch (slot)
            {
                case 1: ParallelVideo1 = video; break;
                case 2: ParallelVideo2 = video; break;
                case 3: ParallelVideo3 = video; break;
                case 4: ParallelVideo4 = video; break;
            }
            System.Diagnostics.Debug.WriteLine($"[Dashboard] After assignment - HasParallelVideo4={HasParallelVideo4}, ParallelVideo4ClipPath={ParallelVideo4ClipPath}");
        });
    }

    private async Task<string?> ResolveVideoPathAsync(VideoClip video)
    {
        var videoPath = video.LocalClipPath;

        // Fallback: construir ruta local desde la carpeta de la sesión
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            try
            {
                var session = SelectedSession ?? await _databaseService.GetSessionByIdAsync(video.SessionId);
                if (!string.IsNullOrWhiteSpace(session?.PathSesion))
                {
                    var normalized = (video.ClipPath ?? "").Replace('\\', '/');
                    var fileName = Path.GetFileName(normalized);
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = $"CROWN{video.Id}.mp4";

                    var candidate = Path.Combine(session.PathSesion, "videos", fileName);
                    if (File.Exists(candidate))
                        videoPath = candidate;
                }
            }
            catch
            {
                // Ignorar: si falla, se usará el ClipPath si existe
            }
        }

        // Último recurso: usar ClipPath si fuera una ruta real
        if (string.IsNullOrWhiteSpace(videoPath))
            videoPath = video.ClipPath;

        return videoPath;
    }

    private async Task RefreshPreviewModeAsync()
    {
        IsPreviewMode = false;
        await Task.Delay(50); // Pequeño delay para que el binding se procese
        IsPreviewMode = true;
    }

    /// <summary>
    /// Limpia los videos de preview (recuadros de arrastrar)
    /// </summary>
    public void ClearPreviewVideos()
    {
        ParallelVideo1 = null;
        ParallelVideo2 = null;
        ParallelVideo3 = null;
        ParallelVideo4 = null;
        IsPreviewMode = false;

        IsPreviewPlayer1Ready = false;
        IsPreviewPlayer2Ready = false;
        IsPreviewPlayer3Ready = false;
        IsPreviewPlayer4Ready = false;
    }

    public bool IsPreviewMode
    {
        get => _isPreviewMode;
        set
        {
            if (SetProperty(ref _isPreviewMode, value))
            {
                OnPropertyChanged(nameof(ShowParallelVideo1Thumbnail));
                OnPropertyChanged(nameof(ShowParallelVideo2Thumbnail));
                OnPropertyChanged(nameof(ShowParallelVideo3Thumbnail));
                OnPropertyChanged(nameof(ShowParallelVideo4Thumbnail));
            }
        }
    }

    public bool IsPreviewPlayer1Ready
    {
        get => _isPreviewPlayer1Ready;
        set
        {
            if (SetProperty(ref _isPreviewPlayer1Ready, value))
            {
                OnPropertyChanged(nameof(ShowParallelVideo1Thumbnail));
            }
        }
    }

    public bool IsPreviewPlayer2Ready
    {
        get => _isPreviewPlayer2Ready;
        set
        {
            if (SetProperty(ref _isPreviewPlayer2Ready, value))
            {
                OnPropertyChanged(nameof(ShowParallelVideo2Thumbnail));
            }
        }
    }

    public bool IsPreviewPlayer3Ready
    {
        get => _isPreviewPlayer3Ready;
        set
        {
            if (SetProperty(ref _isPreviewPlayer3Ready, value))
            {
                OnPropertyChanged(nameof(ShowParallelVideo3Thumbnail));
            }
        }
    }

    public bool IsPreviewPlayer4Ready
    {
        get => _isPreviewPlayer4Ready;
        set
        {
            if (SetProperty(ref _isPreviewPlayer4Ready, value))
            {
                OnPropertyChanged(nameof(ShowParallelVideo4Thumbnail));
            }
        }
    }

    public bool ShowParallelVideo1Thumbnail => HasParallelVideo1 && (!IsPreviewMode || !IsPreviewPlayer1Ready);
    public bool ShowParallelVideo2Thumbnail => HasParallelVideo2 && (!IsPreviewMode || !IsPreviewPlayer2Ready);
    public bool ShowParallelVideo3Thumbnail => HasParallelVideo3 && (!IsPreviewMode || !IsPreviewPlayer3Ready);
    public bool ShowParallelVideo4Thumbnail => HasParallelVideo4 && (!IsPreviewMode || !IsPreviewPlayer4Ready);

    // Video en hover para preview
    private VideoClip? _hoverVideo;
    public VideoClip? HoverVideo
    {
        get => _hoverVideo;
        set
        {
            if (SetProperty(ref _hoverVideo, value))
            {
                OnPropertyChanged(nameof(HasHoverVideo));
            }
        }
    }
    public bool HasHoverVideo => _hoverVideo != null;

    private void ToggleVideoSelection(VideoClip? video)
    {
        if (video == null || !IsMultiSelectMode) return;

        video.IsSelected = !video.IsSelected;

        if (video.IsSelected)
            _selectedVideoIds.Add(video.Id);
        else
            _selectedVideoIds.Remove(video.Id);

        OnPropertyChanged(nameof(SelectedVideoCount));
    }
    private void SelectAllFilteredVideos()
    {
        var source = _filteredVideosCache ?? _allVideosCache;
        if (source == null) return;

        foreach (var v in source)
        {
            v.IsSelected = true;
            _selectedVideoIds.Add(v.Id);
        }

        OnPropertyChanged(nameof(SelectedVideoCount));
    }

    private void ClearVideoSelection()
    {
        // Limpiar IsSelected en todos los videos visibles y cacheados
        foreach (var v in SelectedSessionVideos)
            v.IsSelected = false;

        var source = _filteredVideosCache ?? _allVideosCache;
        if (source != null)
        {
            foreach (var v in source)
                v.IsSelected = false;
        }

        _selectedVideoIds.Clear();
        _isSelectAllActive = false;
        OnPropertyChanged(nameof(IsSelectAllActive));
        OnPropertyChanged(nameof(SelectedVideoCount));
    }

    public bool HasMoreVideos
    {
        get => _hasMoreVideos;
        private set => SetProperty(ref _hasMoreVideos, value);
    }

    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        private set => SetProperty(ref _isLoadingMore, value);
    }

    // Listas de opciones para filtros (selección múltiple)
    public ObservableCollection<PlaceFilterItem> FilterPlaces { get; } = new();
    public ObservableCollection<AthleteFilterItem> FilterAthletes { get; } = new();
    public ObservableCollection<SectionFilterItem> FilterSections { get; } = new();
    public ObservableCollection<TagFilterItem> FilterTagItems { get; } = new();

    // Control de expansión de filtros (acordeón)
    private string? _expandedFilter;
    public bool IsPlacesExpanded
    {
        get => _expandedFilter == "Places";
        set { if (value) ExpandFilter("Places"); else if (_expandedFilter == "Places") _expandedFilter = null; OnPropertyChanged(); }
    }
    public bool IsAthletesExpanded
    {
        get => _expandedFilter == "Athletes";
        set { if (value) ExpandFilter("Athletes"); else if (_expandedFilter == "Athletes") _expandedFilter = null; OnPropertyChanged(); }
    }
    public bool IsSectionsExpanded
    {
        get => _expandedFilter == "Sections";
        set { if (value) ExpandFilter("Sections"); else if (_expandedFilter == "Sections") _expandedFilter = null; OnPropertyChanged(); }
    }
    public bool IsTagsExpanded
    {
        get => _expandedFilter == "Tags";
        set { if (value) ExpandFilter("Tags"); else if (_expandedFilter == "Tags") _expandedFilter = null; OnPropertyChanged(); }
    }

    private void ExpandFilter(string filterName)
    {
        _expandedFilter = filterName;
        OnPropertyChanged(nameof(IsPlacesExpanded));
        OnPropertyChanged(nameof(IsAthletesExpanded));
        OnPropertyChanged(nameof(IsSectionsExpanded));
        OnPropertyChanged(nameof(IsTagsExpanded));
    }

    // Resumen de selección múltiple
    public string SelectedPlacesSummary => GetSelectedPlacesSummary();
    public string SelectedAthletesSummary => GetSelectedAthletesSummary();
    public string SelectedSectionsSummary => GetSelectedSectionsSummary();
    public string SelectedTagsSummary => GetSelectedTagsSummary();

    private string GetSelectedPlacesSummary()
    {
        var selected = FilterPlaces.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return "Todos los lugares";
        if (selected.Count == 1) return selected[0].DisplayName;
        return $"{selected.Count} lugares";
    }

    private string GetSelectedAthletesSummary()
    {
        var selected = FilterAthletes.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return "Todos los deportistas";
        if (selected.Count == 1) return selected[0].DisplayName;
        return $"{selected.Count} deportistas";
    }

    private string GetSelectedSectionsSummary()
    {
        var selected = FilterSections.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return "Todas las secciones";
        if (selected.Count == 1) return selected[0].DisplayName;
        return $"{selected.Count} secciones";
    }

    private string GetSelectedTagsSummary()
    {
        var selected = FilterTagItems.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0) return "Todos los tags";
        if (selected.Count == 1) return selected[0].DisplayName;
        return $"{selected.Count} tags";
    }

    public DateTime? SelectedFilterDateFrom
    {
        get => _selectedFilterDateFrom;
        set
        {
            if (SetProperty(ref _selectedFilterDateFrom, value))
                _ = ApplyFiltersAsync();
        }
    }

    public DateTime? SelectedFilterDateTo
    {
        get => _selectedFilterDateTo;
        set
        {
            if (SetProperty(ref _selectedFilterDateTo, value))
                _ = ApplyFiltersAsync();
        }
    }

    public ObservableCollection<Session> RecentSessions { get; } = new();
    public ObservableCollection<Session> VisibleRecentSessions { get; } = new();
    public ObservableCollection<SessionsListRow> SessionRows { get; } = new();
    public ObservableCollection<VideoClip> SelectedSessionVideos { get; } = new();
    public ObservableCollection<VideoLesson> VideoLessons { get; } = new();

    private SessionsListRow? _selectedSessionListItem;
    public SessionsListRow? SelectedSessionListItem
    {
        get => _selectedSessionListItem;
        set
        {
            // Si seleccionan una cabecera, anulamos la selección para evitar estados raros.
            if (value is SessionGroupHeaderRow)
            {
                if (_selectedSessionListItem != null)
                {
                    _selectedSessionListItem = null;
                    OnPropertyChanged();
                }
                return;
            }

            var oldRow = _selectedSessionListItem as SessionRow;
            if (SetProperty(ref _selectedSessionListItem, value))
            {
                // Actualizar IsSelected en los SessionRow
                if (oldRow != null)
                    oldRow.IsSelected = false;

                if (value is SessionRow newRow)
                {
                    newRow.IsSelected = true;
                    SelectedSession = newRow.Session;
                    
                    // Limpiar carpeta inteligente activa al seleccionar una sesión
                    _activeSmartFolder = null;
                    _smartFolderFilteredVideosCache = null;
                }
            }
        }
    }

    private void SyncVisibleRecentSessions()
    {
        VisibleRecentSessions.Clear();

        if (!IsUserLibraryExpanded || !IsSessionsListExpanded)
            return;

        foreach (var session in RecentSessions)
            VisibleRecentSessions.Add(session);
    }

    private void SyncVisibleSessionGroups()
    {
        // Obsoleto: se usaba para CollectionView agrupado.
    }

    private void SyncVisibleSessionRows()
    {
        SessionRows.Clear();

        // La visibilidad se controla llenando/vaciando la colección
        if (!IsUserLibraryExpanded || !IsSessionsListExpanded)
            return;

        var sessionsSnapshot = RecentSessions.ToList();
        var groups = BuildSessionGroups(sessionsSnapshot);

        foreach (var g in groups)
        {
            SessionRows.Add(new SessionGroupHeaderRow(g.Key, g.Title, g.IsExpanded, g.TotalCount));

            if (!g.IsExpanded)
                continue;

            foreach (var s in g)
            {
                // Aplicar personalizaciones de icono y color
                ApplySessionCustomization(s);
                
                var row = new SessionRow(s);
                // Marcar como seleccionado si corresponde a la sesión actual (comparar por Id)
                if (SelectedSession != null && s.Id == SelectedSession.Id)
                {
                    row.IsSelected = true;
                    _selectedSessionListItem = row; // Sincronizar referencia
                    SelectedSession = s; // Actualizar referencia a la nueva instancia
                }
                SessionRows.Add(row);
            }
        }
    }

    private void ToggleSessionGroupExpanded(string? groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
            return;

        var current = _sessionGroupExpandedState.TryGetValue(groupKey, out var v) && v;
        _sessionGroupExpandedState[groupKey] = !current;
        SyncVisibleSessionRows();
    }

    private List<SessionGroup> BuildSessionGroups(List<Session> sessions)
    {
        var culture = new CultureInfo("es-ES");
        var today = DateTime.Today;

        DateTime StartOfWeek(DateTime d)
        {
            // Semana empieza en lunes
            var diff = ((int)d.DayOfWeek + 6) % 7;
            return d.Date.AddDays(-diff);
        }

        var startThisWeek = StartOfWeek(today);
        var startLastWeek = startThisWeek.AddDays(-7);
        var startTwoWeeksAgo = startThisWeek.AddDays(-14);

        string BucketKey(DateTime d)
        {
            if (d.Date == today) return "today";
            if (d.Date == today.AddDays(-1)) return "yesterday";
            if (d.Date >= startThisWeek) return "this_week";
            if (d.Date >= startLastWeek) return "last_week";
            if (d.Date >= startTwoWeeksAgo) return "two_weeks";
            return $"month_{d:yyyy_MM}";
        }

        string BucketTitle(string key)
        {
            return key switch
            {
                "today" => "Hoy",
                "yesterday" => "Ayer",
                "this_week" => "Esta semana",
                "last_week" => "Semana pasada",
                "two_weeks" => "Hace dos semanas",
                _ when key.StartsWith("month_") =>
                    DateTime.ParseExact(key[6..], "yyyy_MM", CultureInfo.InvariantCulture)
                        .ToString("MMMM yyyy", culture),
                _ => key
            };
        }

        int BucketSortOrder(string key)
        {
            // Menor = más arriba
            return key switch
            {
                "today" => 0,
                "yesterday" => 1,
                "this_week" => 2,
                "last_week" => 3,
                "two_weeks" => 4,
                _ => 100
            };
        }

        var grouped = sessions
            .Select(s => new { Session = s, Date = s.FechaDateTime })
            .GroupBy(x => BucketKey(x.Date))
            .ToList();

        var result = new List<SessionGroup>();

        var relativeGroups = grouped
            .Where(g => !g.Key.StartsWith("month_"))
            .OrderBy(g => BucketSortOrder(g.Key));

        foreach (var g in relativeGroups)
        {
            var sessionsInGroup = g
                .Select(x => x.Session)
                .OrderByDescending(s => s.FechaDateTime)
                .ThenByDescending(s => s.Id)
                .ToList();

            var expanded = _sessionGroupExpandedState.TryGetValue(g.Key, out var v) ? v : true;
            result.Add(new SessionGroup(g.Key, BucketTitle(g.Key), sessionsInGroup, expanded));
        }

        var monthGroups = grouped
            .Where(g => g.Key.StartsWith("month_"))
            .OrderByDescending(g => g.Key); // month_YYYY_MM ordenable como string

        foreach (var g in monthGroups)
        {
            var sessionsInGroup = g
                .Select(x => x.Session)
                .OrderByDescending(s => s.FechaDateTime)
                .ThenByDescending(s => s.Id)
                .ToList();

            // Por defecto, meses colapsados
            var expanded = _sessionGroupExpandedState.TryGetValue(g.Key, out var v) ? v : false;
            result.Add(new SessionGroup(g.Key, BucketTitle(g.Key), sessionsInGroup, expanded));
        }

        return result;
    }

    public ObservableCollection<SectionStats> SelectedSessionSectionStats { get; } = new();
    public ObservableCollection<SessionAthleteTimeRow> SelectedSessionAthleteTimes { get; } = new();

    // Datos (DB) de la sesión seleccionada
    public ObservableCollection<Input> SelectedSessionInputs { get; } = new();
    public ObservableCollection<Valoracion> SelectedSessionValoraciones { get; } = new();

    // Series para gráfico (duración total por sección, en minutos)
    public ObservableCollection<double> SelectedSessionSectionDurationMinutes { get; } = new();
    public ObservableCollection<string> SelectedSessionSectionLabels { get; } = new();

    // Etiquetas (tags): vídeos etiquetados por TagId
    public ObservableCollection<double> SelectedSessionTagVideoCounts { get; } = new();
    public ObservableCollection<string> SelectedSessionTagLabels { get; } = new();

    // Penalizaciones: conteo de tags 2 y 50
    public ObservableCollection<double> SelectedSessionPenaltyCounts { get; } = new();
    public ObservableCollection<string> SelectedSessionPenaltyLabels { get; } = new();

    // ==================== ESTADÍSTICAS ABSOLUTAS (CONTADORES) ====================
    private int _totalEventTagsCount;
    private int _totalUniqueEventTagsCount;
    private int _totalLabelTagsCount;
    private int _totalUniqueLabelTagsCount;
    private int _labeledVideosCount;
    private int _totalVideosForLabeling;
    private double _avgTagsPerSession;

    /// <summary>Total de eventos de tag establecidos (Input.IsEvent = 1)</summary>
    public int TotalEventTagsCount
    {
        get => _totalEventTagsCount;
        set => SetProperty(ref _totalEventTagsCount, value);
    }

    /// <summary>Número de tipos de eventos únicos usados</summary>
    public int TotalUniqueEventTagsCount
    {
        get => _totalUniqueEventTagsCount;
        set => SetProperty(ref _totalUniqueEventTagsCount, value);
    }

    /// <summary>Total de etiquetas asignadas (Input.IsEvent = 0)</summary>
    public int TotalLabelTagsCount
    {
        get => _totalLabelTagsCount;
        set => SetProperty(ref _totalLabelTagsCount, value);
    }

    /// <summary>Número de etiquetas únicas usadas</summary>
    public int TotalUniqueLabelTagsCount
    {
        get => _totalUniqueLabelTagsCount;
        set => SetProperty(ref _totalUniqueLabelTagsCount, value);
    }

    /// <summary>Vídeos con nombre/etiqueta asignada</summary>
    public int LabeledVideosCount
    {
        get => _labeledVideosCount;
        set => SetProperty(ref _labeledVideosCount, value);
    }

    /// <summary>Total de vídeos en contexto para calcular porcentaje</summary>
    public int TotalVideosForLabeling
    {
        get => _totalVideosForLabeling;
        set => SetProperty(ref _totalVideosForLabeling, value);
    }

    /// <summary>Media de etiquetas por sesión</summary>
    public double AvgTagsPerSession
    {
        get => _avgTagsPerSession;
        set => SetProperty(ref _avgTagsPerSession, value);
    }

    /// <summary>Texto formateado de vídeos etiquetados</summary>
    public string LabeledVideosText => TotalVideosForLabeling > 0 
        ? $"{LabeledVideosCount} de {TotalVideosForLabeling}"
        : "0";

    /// <summary>Porcentaje de vídeos etiquetados</summary>
    public double LabeledVideosPercentage => TotalVideosForLabeling > 0
        ? (double)LabeledVideosCount / TotalVideosForLabeling * 100
        : 0;

    /// <summary>Top tags de eventos más usados</summary>
    public ObservableCollection<TagUsageRow> TopEventTags { get; } = new();

    /// <summary>Valores para gráfico circular de eventos</summary>
    public ObservableCollection<double> TopEventTagValues { get; } = new();

    /// <summary>Nombres para gráfico circular de eventos</summary>
    public ObservableCollection<string> TopEventTagNames { get; } = new();

    /// <summary>Top etiquetas más usadas</summary>
    public ObservableCollection<TagUsageRow> TopLabelTags { get; } = new();

    /// <summary>Valores para gráfico circular de etiquetas</summary>
    public ObservableCollection<double> TopLabelTagValues { get; } = new();

    /// <summary>Nombres para gráfico circular de etiquetas</summary>
    public ObservableCollection<string> TopLabelTagNames { get; } = new();

    // ==================== TABLA DE TIEMPOS POR SECCIÓN ====================
    
    /// <summary>Secciones con tiempos de atletas</summary>
    public ObservableCollection<SectionWithAthleteRows> SectionTimes { get; } = new();
    
    private bool _hasSectionTimes;
    /// <summary>Indica si hay tiempos por sección para mostrar</summary>
    public bool HasSectionTimes
    {
        get => _hasSectionTimes;
        set => SetProperty(ref _hasSectionTimes, value);
    }

    /// <summary>Indica si se debe mostrar la tabla de tiempos (solo para sesiones específicas, no Galería General ni Videolecciones)</summary>
    public bool ShowSectionTimesTable => !IsAllGallerySelected && !IsVideoLessonsSelected && SelectedSession != null && HasSectionTimes;

    /// <summary>Indica si hay una sesión específica seleccionada (no Galería General ni Videolecciones)</summary>
    public bool HasSpecificSessionSelected => !IsAllGallerySelected && !IsVideoLessonsSelected && SelectedSession != null;

    private bool _showSectionTimesDifferences;
    /// <summary>Indica si se muestran las diferencias respecto al usuario de referencia</summary>
    public bool ShowSectionTimesDifferences
    {
        get => _showSectionTimesDifferences;
        set
        {
            if (SetProperty(ref _showSectionTimesDifferences, value))
            {
                OnPropertyChanged(nameof(ShowSectionTimesAbsolute));
                // Recalcular diferencias cuando se activa el modo
                if (value && ReferenceAthleteId.HasValue)
                {
                    UpdateSectionTimeDifferences();
                }
            }
        }
    }

    /// <summary>Indica si se muestran los tiempos absolutos (inverso de diferencias)</summary>
    public bool ShowSectionTimesAbsolute => !ShowSectionTimesDifferences;

    /// <summary>Comando para alternar entre vista de tiempos y diferencias</summary>
    public ICommand ToggleSectionTimesViewCommand { get; }

    // ==================== POPUP TABLA DETALLADA DE TIEMPOS ====================
    
    private bool _showDetailedTimesPopup;
    /// <summary>Indica si se muestra el popup de tiempos detallados</summary>
    public bool ShowDetailedTimesPopup
    {
        get => _showDetailedTimesPopup;
        set => SetProperty(ref _showDetailedTimesPopup, value);
    }

    private string _detailedTimesHtml = string.Empty;
    public string DetailedTimesHtml
    {
        get => _detailedTimesHtml;
        set => SetProperty(ref _detailedTimesHtml, value);
    }
    
    /// <summary>Secciones con tiempos detallados (incluye Laps)</summary>
    public ObservableCollection<SectionWithDetailedAthleteRows> DetailedSectionTimes { get; } = new();
    
    // ---- Opciones de visibilidad del informe ----
    private ReportOptions _reportOptions = ReportOptions.FullAnalysis();
    /// <summary>Opciones de visibilidad para el informe de sesión</summary>
    public ReportOptions ReportOptions
    {
        get => _reportOptions;
        set
        {
            if (SetProperty(ref _reportOptions, value))
            {
                RegenerateDetailedTimesHtml();
            }
        }
    }
    
    private bool _showReportOptionsPanel;
    /// <summary>Indica si se muestra el panel de opciones del informe</summary>
    public bool ShowReportOptionsPanel
    {
        get => _showReportOptionsPanel;
        set => SetProperty(ref _showReportOptionsPanel, value);
    }
    
    /// <summary>Comando para alternar el panel de opciones</summary>
    public ICommand ToggleReportOptionsPanelCommand => new RelayCommand(() => ShowReportOptionsPanel = !ShowReportOptionsPanel);
    
    /// <summary>Comando para aplicar preset de análisis completo</summary>
    public ICommand ApplyFullAnalysisPresetCommand => new RelayCommand(() =>
    {
        ReportOptions = ReportOptions.FullAnalysis();
        OnPropertyChanged(nameof(ReportOptions));
        RegenerateDetailedTimesHtml();
    });
    
    /// <summary>Comando para aplicar preset de resumen rápido</summary>
    public ICommand ApplyQuickSummaryPresetCommand => new RelayCommand(() =>
    {
        ReportOptions = ReportOptions.QuickSummary();
        OnPropertyChanged(nameof(ReportOptions));
        RegenerateDetailedTimesHtml();
    });
    
    /// <summary>Comando para aplicar preset de informe de atleta</summary>
    public ICommand ApplyAthleteReportPresetCommand => new RelayCommand(() =>
    {
        ReportOptions = ReportOptions.AthleteReport();
        OnPropertyChanged(nameof(ReportOptions));
        RegenerateDetailedTimesHtml();
    });
    
    /// <summary>Actualiza el informe cuando cambia una opción</summary>
    public ICommand UpdateReportOptionCommand => new RelayCommand<string>(optionName =>
    {
        // Las propiedades ya se actualizan por binding, solo regeneramos
        RegenerateDetailedTimesHtml();
    });

    // ---- Selector de atleta de referencia para gráficos ----
    /// <summary>Lista de atletas disponibles para selección en el popup</summary>
    public ObservableCollection<AthletePickerItem> DetailedTimesAthletes { get; } = new();

    private AthletePickerItem? _selectedDetailedTimesAthlete;
    /// <summary>Atleta seleccionado para marcar en los gráficos del popup</summary>
    public AthletePickerItem? SelectedDetailedTimesAthlete
    {
        get => _selectedDetailedTimesAthlete;
        set
        {
            if (SetProperty(ref _selectedDetailedTimesAthlete, value))
            {
                OnPropertyChanged(nameof(HasSelectedDetailedTimesAthlete));
                OnPropertyChanged(nameof(SelectedDetailedTimesAthleteName));
                RegenerateDetailedTimesHtml();
            }
        }
    }

    /// <summary>Indica si hay un atleta seleccionado para el gráfico</summary>
    public bool HasSelectedDetailedTimesAthlete => SelectedDetailedTimesAthlete != null;

    /// <summary>Nombre del atleta seleccionado (para mostrar en el botón)</summary>
    public string SelectedDetailedTimesAthleteName => SelectedDetailedTimesAthlete?.DisplayName ?? "Selecciona atleta";

    private bool _isAthleteDropdownExpanded;
    /// <summary>Indica si el dropdown de atletas está expandido</summary>
    public bool IsAthleteDropdownExpanded
    {
        get => _isAthleteDropdownExpanded;
        set => SetProperty(ref _isAthleteDropdownExpanded, value);
    }

    /// <summary>Comando para alternar el dropdown de atletas</summary>
    public ICommand ToggleAthleteDropdownCommand { get; }

    /// <summary>Comando para seleccionar un atleta del dropdown</summary>
    public ICommand SelectDetailedTimesAthleteCommand { get; }

    /// <summary>Comando para limpiar la selección de atleta en el popup</summary>
    public ICommand ClearDetailedTimesAthleteCommand { get; }

    /// <summary>Regenera el HTML con el atleta seleccionado actual</summary>
    private void RegenerateDetailedTimesHtml()
    {
        if (SelectedSession == null || DetailedSectionTimes.Count == 0)
            return;

        var refId = SelectedDetailedTimesAthlete?.Id;
        var refVideoId = SelectedDetailedTimesAthlete?.VideoId;
        var refName = SelectedDetailedTimesAthlete?.DisplayName;
        
        // Generar datos del informe completo
        var reportData = SessionReportService.GenerateReportData(
            SelectedSession, 
            DetailedSectionTimes.ToList(), 
            refId, 
            refVideoId);
        
        // Generar HTML con las opciones seleccionadas
        DetailedTimesHtml = _tableExportService.BuildSessionReportHtml(
            reportData, 
            ReportOptions, 
            refId, 
            refVideoId,
            refName);
    }
    
    private bool _isLoadingDetailedTimes;
    /// <summary>Indica si se están cargando los tiempos detallados</summary>
    public bool IsLoadingDetailedTimes
    {
        get => _isLoadingDetailedTimes;
        set => SetProperty(ref _isLoadingDetailedTimes, value);
    }

    private bool _isExportingDetailedTimes;
    public bool IsExportingDetailedTimes
    {
        get => _isExportingDetailedTimes;
        set => SetProperty(ref _isExportingDetailedTimes, value);
    }
    
    /// <summary>Indica si hay tiempos detallados con parciales</summary>
    public bool HasDetailedTimesWithLaps => DetailedSectionTimes.Any(s => s.HasAnyLaps);
    
    /// <summary>Comando para abrir el popup de tiempos detallados</summary>
    public ICommand OpenDetailedTimesPopupCommand { get; }
    
    /// <summary>Comando para cerrar el popup de tiempos detallados</summary>
    public ICommand CloseDetailedTimesPopupCommand { get; }
    
    /// <summary>Indica si se muestran los tiempos acumulados (Splits) en lugar de parciales (Laps)</summary>
    private bool _showCumulativeTimes;
    public bool ShowCumulativeTimes
    {
        get => _showCumulativeTimes;
        set
        {
            if (SetProperty(ref _showCumulativeTimes, value))
            {
                OnPropertyChanged(nameof(ShowLapTimes));
                OnPropertyChanged(nameof(LapTimeModeText));
            }
        }
    }
    
    /// <summary>Indica si se muestran los tiempos parciales (Laps)</summary>
    public bool ShowLapTimes => !ShowCumulativeTimes;
    
    /// <summary>Texto del modo actual de visualización de parciales</summary>
    public string LapTimeModeText => ShowCumulativeTimes ? "Acum." : "Parcial";
    
    /// <summary>Comando para alternar entre Laps y Splits</summary>
    public ICommand ToggleLapTimesModeCommand { get; }

    /// <summary>Comando para seleccionar explícitamente Lap/Acum.</summary>
    public ICommand SetLapTimesModeCommand { get; }

    public ICommand ExportDetailedTimesHtmlCommand { get; }
    public ICommand ExportDetailedTimesPdfCommand { get; }

    // ==================== ESTADÍSTICAS USUARIO ====================
    private UserAthleteStats? _userAthleteStats;
    private int? _referenceAthleteId;
    private string? _referenceAthleteName;

    public UserAthleteStats? UserAthleteStats
    {
        get => _userAthleteStats;
        set => SetProperty(ref _userAthleteStats, value);
    }

    public int? ReferenceAthleteId
    {
        get => _referenceAthleteId;
        set => SetProperty(ref _referenceAthleteId, value);
    }

    public string? ReferenceAthleteName
    {
        get => _referenceAthleteName;
        set => SetProperty(ref _referenceAthleteName, value);
    }

    public bool HasReferenceAthlete => ReferenceAthleteId.HasValue;
    public bool HasUserStats => UserAthleteStats?.HasData == true;

    public ObservableCollection<AthleteComparisonRow> AthleteComparison { get; } = new();

    // Estadísticas personales extendidas
    private UserPersonalStats? _userPersonalStats;
    public UserPersonalStats? UserPersonalStats
    {
        get => _userPersonalStats;
        set => SetProperty(ref _userPersonalStats, value);
    }
    
    // Propiedades para binding de gráficos de valoraciones
    public ObservableCollection<double> ValoracionFisicoValues { get; } = new();
    public ObservableCollection<string> ValoracionFisicoLabels { get; } = new();
    public ObservableCollection<double> ValoracionMentalValues { get; } = new();
    public ObservableCollection<string> ValoracionMentalLabels { get; } = new();
    public ObservableCollection<double> ValoracionTecnicoValues { get; } = new();
    public ObservableCollection<string> ValoracionTecnicoLabels { get; } = new();
    
    // Propiedades para binding de gráfico de medias por sesión
    public ObservableCollection<double> SessionAverageValues { get; } = new();
    public ObservableCollection<string> SessionAverageLabels { get; } = new();
    
    // Propiedades para binding de evolución de penalizaciones
    public ObservableCollection<double> PenaltyEvolutionValues { get; } = new();
    public ObservableCollection<string> PenaltyEvolutionLabels { get; } = new();

    private async Task LoadUserAthleteStatsAsync()
    {
        if (!ReferenceAthleteId.HasValue)
        {
            UserAthleteStats = null;
            UserPersonalStats = null;
            AthleteComparison.Clear();
            ClearPersonalStatsCharts();
            return;
        }

        try
        {
            int? sessionId = IsAllGallerySelected ? null : SelectedSession?.Id;

            // Cargar estadísticas del atleta de referencia
            UserAthleteStats = await _statisticsService.GetUserAthleteStatsAsync(ReferenceAthleteId.Value, sessionId);

            // Cargar comparativa con otros atletas
            var comparison = await _statisticsService.GetAthleteComparisonAsync(ReferenceAthleteId.Value, sessionId, 5);
            AthleteComparison.Clear();
            foreach (var row in comparison)
            {
                AthleteComparison.Add(row);
            }

            // Cargar estadísticas personales extendidas
            UserPersonalStats = await _statisticsService.GetUserPersonalStatsAsync(ReferenceAthleteId.Value, sessionId);
            UpdatePersonalStatsCharts();

            OnPropertyChanged(nameof(HasUserStats));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando estadísticas de usuario: {ex.Message}");
        }
    }

    private void ClearPersonalStatsCharts()
    {
        ValoracionFisicoValues.Clear();
        ValoracionFisicoLabels.Clear();
        ValoracionMentalValues.Clear();
        ValoracionMentalLabels.Clear();
        ValoracionTecnicoValues.Clear();
        ValoracionTecnicoLabels.Clear();
        SessionAverageValues.Clear();
        SessionAverageLabels.Clear();
        PenaltyEvolutionValues.Clear();
        PenaltyEvolutionLabels.Clear();
    }

    private void UpdatePersonalStatsCharts()
    {
        ClearPersonalStatsCharts();
        
        if (UserPersonalStats == null) return;

        // Medias por sesión
        foreach (var avg in UserPersonalStats.SessionAverages)
        {
            SessionAverageValues.Add(avg.AverageTimeMs / 1000.0); // Convertir a segundos
            SessionAverageLabels.Add(avg.SessionDateShort);
        }

        // Evolución de valoraciones - Físico
        foreach (var point in UserPersonalStats.ValoracionEvolution.Fisico)
        {
            ValoracionFisicoValues.Add(point.Value);
            ValoracionFisicoLabels.Add(point.DateShort);
        }

        // Evolución de valoraciones - Mental
        foreach (var point in UserPersonalStats.ValoracionEvolution.Mental)
        {
            ValoracionMentalValues.Add(point.Value);
            ValoracionMentalLabels.Add(point.DateShort);
        }

        // Evolución de valoraciones - Técnico
        foreach (var point in UserPersonalStats.ValoracionEvolution.Tecnico)
        {
            ValoracionTecnicoValues.Add(point.Value);
            ValoracionTecnicoLabels.Add(point.DateShort);
        }

        // Evolución de penalizaciones
        foreach (var point in UserPersonalStats.PenaltyEvolution)
        {
            PenaltyEvolutionValues.Add(point.PenaltyCount);
            PenaltyEvolutionLabels.Add(point.DateShort);
        }
    }

    private async Task LoadReferenceAthleteAsync()
    {
        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId.HasValue == true)
            {
                ReferenceAthleteId = profile.ReferenceAthleteId;

                var athletes = await _databaseService.GetAllAthletesAsync();
                var refAthlete = athletes.FirstOrDefault(a => a.Id == ReferenceAthleteId.Value);
                ReferenceAthleteName = refAthlete != null
                    ? $"{refAthlete.Nombre} {refAthlete.Apellido}".Trim()
                    : null;
            }
            else
            {
                ReferenceAthleteId = null;
                ReferenceAthleteName = null;
            }
            OnPropertyChanged(nameof(HasReferenceAthlete));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando atleta de referencia: {ex.Message}");
        }
    }

    private async Task LoadAbsoluteTagStatsAsync(int? sessionId)
    {
        try
        {
            var stats = await _statisticsService.GetAbsoluteTagStatsAsync(sessionId);

            TotalEventTagsCount = stats.TotalEventTags;
            TotalUniqueEventTagsCount = stats.UniqueEventTags;
            TotalLabelTagsCount = stats.TotalLabelTags;
            TotalUniqueLabelTagsCount = stats.UniqueLabelTags;
            LabeledVideosCount = stats.LabeledVideos;
            TotalVideosForLabeling = stats.TotalVideos;
            AvgTagsPerSession = stats.AvgTagsPerSession;

            TopEventTags.Clear();
            TopEventTagValues.Clear();
            TopEventTagNames.Clear();
            foreach (var tag in stats.TopEventTags)
            {
                TopEventTags.Add(tag);
                TopEventTagValues.Add(tag.UsageCount);
                TopEventTagNames.Add(tag.TagName);
            }

            TopLabelTags.Clear();
            TopLabelTagValues.Clear();
            TopLabelTagNames.Clear();
            foreach (var tag in stats.TopLabelTags)
            {
                TopLabelTags.Add(tag);
                TopLabelTagValues.Add(tag.UsageCount);
                TopLabelTagNames.Add(tag.TagName);
            }

            OnPropertyChanged(nameof(LabeledVideosText));
            OnPropertyChanged(nameof(LabeledVideosPercentage));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando estadísticas absolutas: {ex.Message}");
        }
    }

    private async Task LoadAthleteSectionTimesAsync(int sessionId)
    {
        try
        {
            var sections = await _statisticsService.GetAthleteSectionTimesAsync(sessionId);

            SectionTimes.Clear();
            foreach (var section in sections)
            {
                SectionTimes.Add(section);
            }

            HasSectionTimes = SectionTimes.Count > 0;
            OnPropertyChanged(nameof(ShowSectionTimesTable));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando tiempos por sección: {ex.Message}");
            HasSectionTimes = false;
            OnPropertyChanged(nameof(ShowSectionTimesTable));
        }
    }

    private void ClearSectionTimes()
    {
        SectionTimes.Clear();
        HasSectionTimes = false;
        OnPropertyChanged(nameof(ShowSectionTimesTable));
    }

    /// <summary>
    /// Abre el popup con la tabla de tiempos detallados (incluye parciales)
    /// </summary>
    private async Task OpenDetailedTimesPopupAsync()
    {
        if (SelectedSession == null) return;
        
        IsLoadingDetailedTimes = true;
        ShowDetailedTimesPopup = true;
        IsAthleteDropdownExpanded = false;
        
        try
        {
            var detailedTimes = await _statisticsService.GetDetailedAthleteSectionTimesAsync(SelectedSession.Id);
            
            DetailedSectionTimes.Clear();
            foreach (var section in detailedTimes)
            {
                DetailedSectionTimes.Add(section);
            }
            
            OnPropertyChanged(nameof(HasDetailedTimesWithLaps));

            // Cargar lista de atletas para el selector (atletas únicos de la sesión)
            DetailedTimesAthletes.Clear();
            
            // Obtener atletas únicos de todas las secciones
            var uniqueAthletes = detailedTimes
                .SelectMany(s => s.Athletes)
                .GroupBy(a => a.AthleteId)
                .Select(g => new {
                    AthleteId = g.Key,
                    AthleteName = g.First().AthleteName,
                    TotalRuns = g.Count()
                })
                .OrderBy(a => a.AthleteName)
                .ToList();

            // Añadir cada atleta único al selector
            foreach (var athlete in uniqueAthletes)
            {
                var suffix = athlete.TotalRuns > 1 ? $" ({athlete.TotalRuns} mangas)" : "";
                DetailedTimesAthletes.Add(new AthletePickerItem(
                    athlete.AthleteId, 
                    athlete.AthleteName + suffix, 
                    videoId: null, // Sin video específico, usamos el atleta
                    attemptNumber: 0, 
                    hasMultipleAttempts: athlete.TotalRuns > 1));
            }

            // Preseleccionar el atleta de referencia global si existe en la lista
            if (ReferenceAthleteId.HasValue)
            {
                // Buscar la opción "Mejor" o la única entrada del atleta
                SelectedDetailedTimesAthlete = DetailedTimesAthletes
                    .FirstOrDefault(a => a.Id == ReferenceAthleteId.Value && a.AttemptNumber == 0)
                    ?? DetailedTimesAthletes.FirstOrDefault(a => a.Id == ReferenceAthleteId.Value);
            }
            else
            {
                SelectedDetailedTimesAthlete = null;
            }

            // Generar HTML inicial
            RegenerateDetailedTimesHtml();
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardViewModel", $"Error cargando tiempos detallados: {ex.Message}", ex);
        }
        finally
        {
            IsLoadingDetailedTimes = false;
        }
    }

    /// <summary>
    /// Actualiza las diferencias de tiempos respecto al atleta de referencia
    /// </summary>
    private void UpdateSectionTimeDifferences()
    {
        if (!ReferenceAthleteId.HasValue) return;
        
        var refAthleteId = ReferenceAthleteId.Value;
        
        foreach (var section in SectionTimes)
        {
            // Buscar el tiempo del atleta de referencia en esta sección
            var refAthleteRow = section.Athletes.FirstOrDefault(a => a.AthleteId == refAthleteId);
            long refTotalMs = refAthleteRow?.TotalMs ?? 0;
            
            foreach (var athlete in section.Athletes)
            {
                athlete.SetReferenceDifference(refTotalMs, athlete.AthleteId == refAthleteId);
            }
        }
        
        // Forzar actualización de la UI
        OnPropertyChanged(nameof(SectionTimes));
    }

    /// <summary>
    /// Refresca las estadísticas si hay actualizaciones pendientes (llamar desde OnAppearing)
    /// </summary>
    public async Task RefreshPendingStatsAsync()
    {
        if (!_hasStatsUpdatePending)
            return;

        _hasStatsUpdatePending = false;
        _modifiedVideoIds.Clear();

        try
        {
            // Actualizar contadores de SmartFolders ya que los videos pueden haber cambiado
            UpdateSmartFolderVideoCounts();
            
            // Refrescar estadísticas según el contexto actual
            if (SelectedSession != null)
            {
                await LoadAbsoluteTagStatsAsync(SelectedSession.Id);
                await LoadAthleteSectionTimesAsync(SelectedSession.Id);
            }
            else if (IsAllGallerySelected)
            {
                await LoadAbsoluteTagStatsAsync(null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refrescando estadísticas pendientes: {ex.Message}");
        }
    }

    // ==================== FIN ESTADÍSTICAS RELATIVAS ====================

    // Estadísticas: usar el cache filtrado para mostrar totales reales (no solo paginados)
    public int TotalFilteredVideoCount => IsVideoLessonsSelected
        ? VideoLessons.Count
        : ((_filteredVideosCache ?? _allVideosCache)?.Count ?? SelectedSessionVideos.Count);

    public int TotalAvailableVideoCount => IsVideoLessonsSelected
        ? VideoLessons.Count
        : (_allVideosCache?.Count ?? SelectedSessionVideos.Count);

    public double TotalFilteredDurationSeconds => IsVideoLessonsSelected
        ? 0
        : ((_filteredVideosCache ?? _allVideosCache)?.Sum(v => v.ClipDuration) ?? SelectedSessionVideos.Sum(v => v.ClipDuration));
    
    public string VideoCountDisplayText
    {
        get
        {
            if (IsVideoLessonsSelected)
            {
                var shownLessons = VideoLessons.Count;
                return $"{shownLessons} videolecciones";
            }

            var shown = SelectedSessionVideos.Count;
            var total = TotalFilteredVideoCount;
            return shown == total
                ? $"{shown} vídeos"
                : $"{shown} de {total} vídeos";
        }
    }
    
    public double SelectedSessionTotalDurationSeconds => TotalFilteredDurationSeconds;
    public string SelectedSessionTotalDurationFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(TotalFilteredDurationSeconds);
            return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";
        }
    }

    public ICommand ImportCommand { get; }
    public ICommand ImportCrownFileCommand { get; }
    public ICommand CreateSessionFromVideosCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ViewSessionCommand { get; }
    public ICommand ViewAllSessionsCommand { get; }
    public ICommand ViewAthletesCommand { get; }
    public ICommand PlaySelectedVideoCommand { get; }
    public ICommand DeleteSelectedSessionCommand { get; }
    public ICommand SelectAllGalleryCommand { get; }
    public ICommand ViewVideoLessonsCommand { get; }
    public ICommand ViewTrashCommand { get; }
    public ICommand ViewDiaryCommand { get; }
    public ICommand SelectDiaryDateCommand { get; }
    public ICommand LoadMoreVideosCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ToggleFilterItemCommand { get; }
    public ICommand ToggleUserLibraryExpandedCommand { get; }
    public ICommand ToggleSessionsListExpandedCommand { get; }
    public ICommand ToggleSessionGroupExpandedCommand { get; }
    public ICommand SelectSessionRowCommand { get; }
    public ICommand TogglePlacesExpandedCommand { get; }
    public ICommand ToggleAthletesExpandedCommand { get; }
    public ICommand ToggleSectionsExpandedCommand { get; }
    public ICommand ToggleTagsExpandedCommand { get; }
    public ICommand SelectStatsTabCommand { get; }
    public ICommand SelectCrudTechTabCommand { get; }
    public ICommand SelectDiaryTabCommand { get; }
    public ICommand SaveDiaryCommand { get; }
    public ICommand EditDiaryCommand { get; }
    public ICommand SaveCalendarDiaryCommand { get; }
    public ICommand ViewSessionAsPlaylistCommand { get; }
    public ICommand ConnectHealthKitCommand { get; }
    public ICommand ImportHealthDataToWellnessCommand { get; }
    public ICommand SaveWellnessCommand { get; }
    public ICommand StartEditWellnessCommand { get; }
    public ICommand CancelEditWellnessCommand { get; }
    public ICommand SetMoodCommand { get; }
    public ICommand ShowPPMInfoCommand { get; }
    public ICommand ShowHRVInfoCommand { get; }
    public ICommand ToggleAddNewSessionCommand { get; }
    public ICommand CreateNewSessionCommand { get; }
    public ICommand CancelNewSessionCommand { get; }
    public ICommand SelectSessionTypeCommand { get; }
    public ICommand OpenCameraRecordingCommand { get; }
    public ICommand OpenNewSessionSidebarPopupCommand { get; }
    public ICommand CancelNewSessionSidebarPopupCommand { get; }
    public ICommand CreateSessionAndRecordCommand { get; }
    public ICommand OpenSmartFolderSidebarPopupCommand { get; }
    public ICommand CancelSmartFolderSidebarPopupCommand { get; }
    public ICommand AddSmartFolderCriterionCommand { get; }
    public ICommand RemoveSmartFolderCriterionCommand { get; }
    public ICommand CreateSmartFolderCommand { get; }
    public ICommand SetSmartFolderMatchModeCommand { get; }
    public ICommand SelectSmartFolderCommand { get; }
    public ICommand SelectCriterionFieldCommand { get; }
    public ICommand SelectCriterionOperatorCommand { get; }
    public ICommand ShowSmartFolderContextMenuCommand { get; }
    public ICommand RenameSmartFolderCommand { get; }
    public ICommand DeleteSmartFolderCommand { get; }
    public ICommand ChangeSmartFolderIconCommand { get; }
    public ICommand ChangeSmartFolderColorCommand { get; }
    public ICommand SetSmartFolderIconCommand { get; }
    public ICommand SetSmartFolderColorCommand { get; }
    public ICommand OpenIconColorPickerForSmartFolderCommand { get; }
    public ICommand OpenIconColorPickerForSessionCommand { get; }
    public ICommand CloseIconColorPickerCommand { get; }
    public ICommand SelectPickerIconCommand { get; }
    public ICommand SelectPickerColorCommand { get; }
    public ICommand ToggleSmartFoldersExpansionCommand { get; }
    public ICommand ToggleSessionsExpansionCommand { get; }
    public ICommand RenameSessionCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand SetSessionIconCommand { get; }
    public ICommand SetSessionColorCommand { get; }
    public ICommand RecordForSelectedSessionCommand { get; }
    public ICommand ExportSelectedSessionCommand { get; }
    public ICommand SetEvolutionPeriodCommand { get; }
    public ICommand ToggleMultiSelectModeCommand { get; }
    public ICommand ToggleSelectAllCommand { get; }
    public ICommand ToggleVideoSelectionCommand { get; }
    public ICommand ClearVideoSelectionCommand { get; }
    public ICommand VideoTapCommand { get; }
    public ICommand VideoLessonTapCommand { get; }
    public ICommand ShareVideoLessonCommand { get; }
    public ICommand DeleteVideoLessonCommand { get; }
    public ICommand PlayAsPlaylistCommand { get; }
    public ICommand EditVideoDetailsCommand { get; }
    public ICommand ShareSelectedVideosCommand { get; }
    public ICommand DeleteSelectedVideosCommand { get; }
    public ICommand PlayParallelAnalysisCommand { get; }
    public ICommand PreviewParallelAnalysisCommand { get; }
    public ICommand ClearParallelAnalysisCommand { get; }
    public ICommand DropOnScreen1Command { get; }
    public ICommand DropOnScreen2Command { get; }
    public ICommand ToggleQuickAnalysisIsolatedModeCommand { get; }
    
    // Comandos de edición en lote
    public ICommand CloseBatchEditPopupCommand { get; }
    public ICommand ApplyBatchEditCommand { get; }
    public ICommand ToggleBatchTagCommand { get; }
    public ICommand SelectBatchAthleteCommand { get; }
    public ICommand AddNewBatchAthleteCommand { get; }
    public ICommand AddNewBatchTagCommand { get; }

    // Propiedades de edición en lote
    public bool ShowBatchEditPopup
    {
        get => _showBatchEditPopup;
        set => SetProperty(ref _showBatchEditPopup, value);
    }

    public ObservableCollection<Athlete> BatchEditAthletes
    {
        get => _batchEditAthletes;
        set => SetProperty(ref _batchEditAthletes, value);
    }

    public ObservableCollection<Tag> BatchEditTags
    {
        get => _batchEditTags;
        set => SetProperty(ref _batchEditTags, value);
    }

    public Athlete? SelectedBatchAthlete
    {
        get => _selectedBatchAthlete;
        set => SetProperty(ref _selectedBatchAthlete, value);
    }

    public int BatchEditSection
    {
        get => _batchEditSection;
        set => SetProperty(ref _batchEditSection, value);
    }

    public bool BatchEditSectionEnabled
    {
        get => _batchEditSectionEnabled;
        set => SetProperty(ref _batchEditSectionEnabled, value);
    }

    public bool BatchEditAthleteEnabled
    {
        get => _batchEditAthleteEnabled;
        set => SetProperty(ref _batchEditAthleteEnabled, value);
    }

    public string? NewBatchAthleteSurname
    {
        get => _newBatchAthleteSurname;
        set => SetProperty(ref _newBatchAthleteSurname, value);
    }

    public string? NewBatchAthleteName
    {
        get => _newBatchAthleteName;
        set => SetProperty(ref _newBatchAthleteName, value);
    }

    public string? NewBatchTagText
    {
        get => _newBatchTagText;
        set => SetProperty(ref _newBatchTagText, value);
    }

    // Propiedades de edición de sesión individual
    public bool ShowSessionEditPopup
    {
        get => _showSessionEditPopup;
        set => SetProperty(ref _showSessionEditPopup, value);
    }

    public string? SessionEditName
    {
        get => _sessionEditName;
        set => SetProperty(ref _sessionEditName, value);
    }

    public string? SessionEditLugar
    {
        get => _sessionEditLugar;
        set => SetProperty(ref _sessionEditLugar, value);
    }

    public string? SessionEditTipoSesion
    {
        get => _sessionEditTipoSesion;
        set => SetProperty(ref _sessionEditTipoSesion, value);
    }

    // Comandos de edición de sesión
    public ICommand OpenSessionEditPopupCommand { get; }
    public ICommand CloseSessionEditPopupCommand { get; }
    public ICommand ApplySessionEditCommand { get; }

    public DashboardViewModel(
        DatabaseService databaseService,
        ITrashService trashService,
        CrownFileService crownFileService,
        StatisticsService statisticsService,
        ThumbnailService thumbnailService,
        ITableExportService tableExportService,
        IHealthKitService healthKitService,
        VideoExportNotifier? videoExportNotifier = null,
        ImportProgressService? importProgressService = null)
    {
        _databaseService = databaseService;
        _trashService = trashService;
        _crownFileService = crownFileService;
        _statisticsService = statisticsService;
        _thumbnailService = thumbnailService;
        _tableExportService = tableExportService;
        _healthKitService = healthKitService;
        _importProgressService = importProgressService;

        // Suscribirse a eventos de exportación de video
        if (videoExportNotifier != null)
        {
            videoExportNotifier.VideoExported += OnVideoExported;
        }

        // Suscribirse a eventos de importación en segundo plano
        if (_importProgressService != null)
        {
            _importProgressService.ProgressChanged += OnImportProgressChanged;
            _importProgressService.ImportCompleted += OnImportCompleted;
            
            // Verificar si hay una importación en curso al iniciar
            if (_importProgressService.IsImporting && _importProgressService.CurrentTask != null)
            {
                IsBackgroundImporting = true;
                BackgroundImportProgress = _importProgressService.CurrentTask.Percentage;
                BackgroundImportText = _importProgressService.CurrentTask.Name;
            }
        }

        Title = "Dashboard";

        ImportCommand = new AsyncRelayCommand(ShowImportOptionsAsync);
        ImportCrownFileCommand = new AsyncRelayCommand(ImportCrownFileAsync);
        CreateSessionFromVideosCommand = new AsyncRelayCommand(OpenImportPageForVideosAsync);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ViewSessionCommand = new AsyncRelayCommand<Session>(ViewSessionAsync);
        ViewAllSessionsCommand = new AsyncRelayCommand(ViewAllSessionsAsync);
        ViewAthletesCommand = new AsyncRelayCommand(ViewAthletesAsync);
        PlaySelectedVideoCommand = new AsyncRelayCommand<VideoClip>(PlaySelectedVideoAsync);
        DeleteSelectedSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync);
        SelectAllGalleryCommand = new AsyncRelayCommand(SelectAllGalleryAsync);
        ViewVideoLessonsCommand = new AsyncRelayCommand(ViewVideoLessonsAsync);
        ViewTrashCommand = new AsyncRelayCommand(ViewTrashAsync);
        ViewDiaryCommand = new RelayCommand(() => IsDiaryViewSelected = true);
        SelectDiaryDateCommand = new RelayCommand<DateTime>(date => SelectedDiaryDate = date);
        LoadMoreVideosCommand = new AsyncRelayCommand(LoadMoreVideosAsync);
        ClearFiltersCommand = new RelayCommand(() => ClearFilters());
        ToggleFilterItemCommand = new RelayCommand<object?>(ToggleFilterItem);
        ToggleUserLibraryExpandedCommand = new RelayCommand(() => IsUserLibraryExpanded = !IsUserLibraryExpanded);
        ToggleSessionsListExpandedCommand = new RelayCommand(() => IsSessionsListExpanded = !IsSessionsListExpanded);
        ToggleSessionGroupExpandedCommand = new RelayCommand<string>(ToggleSessionGroupExpanded);
        SelectSessionRowCommand = new RelayCommand<SessionRow>(row => { if (row != null) SelectedSessionListItem = row; });
        TogglePlacesExpandedCommand = new RelayCommand(() => IsPlacesExpanded = !IsPlacesExpanded);
        ToggleAthletesExpandedCommand = new RelayCommand(() => IsAthletesExpanded = !IsAthletesExpanded);
        ToggleSectionsExpandedCommand = new RelayCommand(() => IsSectionsExpanded = !IsSectionsExpanded);
        ToggleTagsExpandedCommand = new RelayCommand(() => IsTagsExpanded = !IsTagsExpanded);

        OpenSmartFolderSidebarPopupCommand = new RelayCommand(OpenSmartFolderSidebarPopup);
        CancelSmartFolderSidebarPopupCommand = new RelayCommand(CloseSmartFolderSidebarPopup);
        AddSmartFolderCriterionCommand = new RelayCommand(AddSmartFolderCriterion);
        RemoveSmartFolderCriterionCommand = new RelayCommand<SmartFolderCriterion>(RemoveSmartFolderCriterion);
        CreateSmartFolderCommand = new RelayCommand(CreateSmartFolder);
        SelectCriterionFieldCommand = new AsyncRelayCommand<SmartFolderCriterion>(SelectCriterionFieldAsync);
        SelectCriterionOperatorCommand = new AsyncRelayCommand<SmartFolderCriterion>(SelectCriterionOperatorAsync);
        SetSmartFolderMatchModeCommand = new RelayCommand<string>(mode =>
        {
            if (string.Equals(mode, "Any", StringComparison.OrdinalIgnoreCase))
                NewSmartFolderMatchMode = "Any";
            else
                NewSmartFolderMatchMode = "All";
        });
        SelectSmartFolderCommand = new AsyncRelayCommand<SmartFolderDefinition>(SelectSmartFolderAsync);
        ShowSmartFolderContextMenuCommand = new AsyncRelayCommand<SmartFolderDefinition>(ShowSmartFolderContextMenuAsync);
        RenameSmartFolderCommand = new AsyncRelayCommand<SmartFolderDefinition>(RenameSmartFolderAsync);
        DeleteSmartFolderCommand = new AsyncRelayCommand<SmartFolderDefinition>(DeleteSmartFolderAsync);
        ChangeSmartFolderIconCommand = new AsyncRelayCommand<SmartFolderDefinition>(ChangeSmartFolderIconAsync);
        ChangeSmartFolderColorCommand = new AsyncRelayCommand<SmartFolderDefinition>(ChangeSmartFolderColorAsync);
        SetSmartFolderIconCommand = new RelayCommand<object?>(SetSmartFolderIcon);
        SetSmartFolderColorCommand = new RelayCommand<object?>(SetSmartFolderColor);
        RenameSessionCommand = new AsyncRelayCommand<SessionRow>(RenameSessionAsync);
        DeleteSessionCommand = new AsyncRelayCommand<SessionRow>(DeleteSessionAsync);
        SetSessionIconCommand = new RelayCommand<object?>(SetSessionIcon);
        SetSessionColorCommand = new RelayCommand<object?>(SetSessionColor);

        // Icon/Color Picker Popup commands
        OpenIconColorPickerForSmartFolderCommand = new RelayCommand<SmartFolderDefinition>(OpenIconColorPickerForSmartFolder);
        OpenIconColorPickerForSessionCommand = new RelayCommand<SessionRow>(OpenIconColorPickerForSession);
        CloseIconColorPickerCommand = new RelayCommand(CloseIconColorPicker);
        SelectPickerIconCommand = new RelayCommand<string>(SelectPickerIcon);
        SelectPickerColorCommand = new RelayCommand<string>(SelectPickerColor);
        ToggleSmartFoldersExpansionCommand = new RelayCommand(ToggleSmartFoldersExpansion);
        ToggleSessionsExpansionCommand = new RelayCommand(ToggleSessionsExpansion);

        SelectStatsTabCommand = new RelayCommand(() => IsStatsTabSelected = true);
        SelectCrudTechTabCommand = new RelayCommand(() => IsCrudTechTabSelected = true);
        SelectDiaryTabCommand = new RelayCommand(() => IsDiaryTabSelected = true);
        SaveDiaryCommand = new AsyncRelayCommand(SaveDiaryAsync);
        EditDiaryCommand = new RelayCommand(() => IsEditingDiary = true);
        SaveCalendarDiaryCommand = new AsyncRelayCommand<SessionWithDiary>(SaveCalendarDiaryAsync);
        ViewSessionAsPlaylistCommand = new AsyncRelayCommand<SessionWithDiary>(ViewSessionAsPlaylistAsync);
        ConnectHealthKitCommand = new AsyncRelayCommand(ConnectHealthKitAsync);
        ImportHealthDataToWellnessCommand = new AsyncRelayCommand(ImportHealthDataToWellnessAsync);
        SaveWellnessCommand = new AsyncRelayCommand(SaveWellnessAsync);
        StartEditWellnessCommand = new RelayCommand(() => IsEditingWellness = true);
        CancelEditWellnessCommand = new RelayCommand(CancelEditWellness);
        SetMoodCommand = new RelayCommand<string>(mood => 
        {
            if (int.TryParse(mood, out int moodValue))
                WellnessMoodRating = moodValue;
        });
        ShowPPMInfoCommand = new AsyncRelayCommand(ShowPPMInfoAsync);
        ShowHRVInfoCommand = new AsyncRelayCommand(ShowHRVInfoAsync);
        ToggleAddNewSessionCommand = new RelayCommand(() => 
        {
            IsAddingNewSession = !IsAddingNewSession;
            if (IsAddingNewSession)
            {
                // Valores por defecto
                NewSessionName = "";
                NewSessionType = "Gimnasio";
                NewSessionLugar = "";
                // Reset selection
                foreach (var opt in SessionTypeOptions)
                    opt.IsSelected = opt.Name == "Gimnasio";
            }
        });
        CreateNewSessionCommand = new AsyncRelayCommand(CreateNewSessionAsync);
        CancelNewSessionCommand = new RelayCommand(() => IsAddingNewSession = false);
        OpenCameraRecordingCommand = new AsyncRelayCommand(OpenCameraRecordingAsync);
        OpenNewSessionSidebarPopupCommand = new RelayCommand(() =>
        {
            ShowNewSessionSidebarPopup = true;
            // Valores por defecto
            NewSessionName = "";
            NewSessionType = "Entrenamiento";
            NewSessionLugar = "";
            foreach (var opt in SessionTypeOptions)
                opt.IsSelected = opt.Name == "Entrenamiento" || (opt.Name == "Gimnasio" && !SessionTypeOptions.Any(o => o.Name == "Entrenamiento"));
        });
        CancelNewSessionSidebarPopupCommand = new RelayCommand(() => ShowNewSessionSidebarPopup = false);
        CreateSessionAndRecordCommand = new AsyncRelayCommand(CreateSessionAndRecordAsync);

        LoadSmartFoldersFromPreferences();
        LoadSessionCustomizationsFromPreferences();

        RecordForSelectedSessionCommand = new AsyncRelayCommand(RecordForSelectedSessionAsync);
        ExportSelectedSessionCommand = new AsyncRelayCommand(ExportSelectedSessionAsync);
        SelectSessionTypeCommand = new RelayCommand<SessionTypeOption>(option => 
        {
            if (option != null)
            {
                foreach (var opt in SessionTypeOptions)
                    opt.IsSelected = opt == option;
                NewSessionType = option.Name;
            }
        });
        SetEvolutionPeriodCommand = new RelayCommand<string>(period => 
        {
            if (int.TryParse(period, out var p))
                SelectedEvolutionPeriod = p;
        });

        ToggleMultiSelectModeCommand = new RelayCommand(() =>
        {
            IsMultiSelectMode = !IsMultiSelectMode;
            if (IsMultiSelectMode) IsSelectAllActive = false;
        });
        ToggleSelectAllCommand = new RelayCommand(() =>
        {
            IsSelectAllActive = !IsSelectAllActive;
            if (IsSelectAllActive) IsMultiSelectMode = false;
        });
        ToggleVideoSelectionCommand = new RelayCommand<VideoClip>(ToggleVideoSelection);
        ClearVideoSelectionCommand = new RelayCommand(ClearVideoSelection);
        VideoTapCommand = new AsyncRelayCommand<VideoClip>(OnVideoTappedAsync);
        VideoLessonTapCommand = new AsyncRelayCommand<VideoLesson>(OnVideoLessonTappedAsync);
        ShareVideoLessonCommand = new AsyncRelayCommand<VideoLesson>(ShareVideoLessonAsync);
        DeleteVideoLessonCommand = new AsyncRelayCommand<VideoLesson>(DeleteVideoLessonAsync);
        PlayAsPlaylistCommand = new AsyncRelayCommand(PlayAsPlaylistAsync);
        EditVideoDetailsCommand = new AsyncRelayCommand(EditVideoDetailsAsync);
        ShareSelectedVideosCommand = new AsyncRelayCommand(ShareSelectedVideosAsync);
        DeleteSelectedVideosCommand = new AsyncRelayCommand(DeleteSelectedVideosAsync);
        PlayParallelAnalysisCommand = new AsyncRelayCommand(PlayParallelAnalysisAsync);
        PreviewParallelAnalysisCommand = new AsyncRelayCommand(PreviewParallelAnalysisAsync);
        ClearParallelAnalysisCommand = new RelayCommand(ClearParallelAnalysis);
        DropOnScreen1Command = new RelayCommand<VideoClip>(video => ParallelVideo1 = video);
        DropOnScreen2Command = new RelayCommand<VideoClip>(video => ParallelVideo2 = video);
        ToggleQuickAnalysisIsolatedModeCommand = new RelayCommand(() => IsQuickAnalysisIsolatedMode = !IsQuickAnalysisIsolatedMode);
        
        // Comandos de edición en lote
        CloseBatchEditPopupCommand = new RelayCommand(() => ShowBatchEditPopup = false);
        ApplyBatchEditCommand = new AsyncRelayCommand(ApplyBatchEditAsync);
        ToggleBatchTagCommand = new RelayCommand<Tag>(ToggleBatchTag);
        SelectBatchAthleteCommand = new RelayCommand<Athlete>(SelectBatchAthlete);
        AddNewBatchAthleteCommand = new AsyncRelayCommand(AddNewBatchAthleteAsync);
        AddNewBatchTagCommand = new AsyncRelayCommand(AddNewBatchTagAsync);

        // Comandos de edición de sesión individual
        OpenSessionEditPopupCommand = new RelayCommand<SessionRow>(OpenSessionEditPopup);
        CloseSessionEditPopupCommand = new RelayCommand(() => ShowSessionEditPopup = false);
        ApplySessionEditCommand = new AsyncRelayCommand(ApplySessionEditAsync);
        
        // Comando para alternar vista de tiempos
        ToggleSectionTimesViewCommand = new RelayCommand(() => ShowSectionTimesDifferences = !ShowSectionTimesDifferences);
        
        // Comandos para popup de tiempos detallados
        OpenDetailedTimesPopupCommand = new AsyncRelayCommand(OpenDetailedTimesPopupAsync);
        CloseDetailedTimesPopupCommand = new RelayCommand(() => ShowDetailedTimesPopup = false);
        ClearDetailedTimesAthleteCommand = new RelayCommand(() =>
        {
            SelectedDetailedTimesAthlete = null;
            IsAthleteDropdownExpanded = false;
        });
        ToggleAthleteDropdownCommand = new RelayCommand(() => IsAthleteDropdownExpanded = !IsAthleteDropdownExpanded);
        SelectDetailedTimesAthleteCommand = new RelayCommand<AthletePickerItem>(athlete =>
        {
            SelectedDetailedTimesAthlete = athlete;
            IsAthleteDropdownExpanded = false;
        });
        ToggleLapTimesModeCommand = new RelayCommand(() => ShowCumulativeTimes = !ShowCumulativeTimes);
        SetLapTimesModeCommand = new RelayCommand<string>(mode =>
        {
            // mode: "lap" | "acum"
            ShowCumulativeTimes = string.Equals(mode, "acum", StringComparison.OrdinalIgnoreCase);
        });

        ExportDetailedTimesHtmlCommand = new AsyncRelayCommand(ExportDetailedTimesHtmlAsync);
        ExportDetailedTimesPdfCommand = new AsyncRelayCommand(ExportDetailedTimesPdfAsync);
        
        // Notificar cambios en VideoCountDisplayText cuando cambie la colección
        SelectedSessionVideos.CollectionChanged += (s, e) => OnPropertyChanged(nameof(VideoCountDisplayText));
        VideoLessons.CollectionChanged += (s, e) => OnPropertyChanged(nameof(VideoCountDisplayText));
        
        // Suscribirse a mensajes de actualización de video individual
        MessagingCenter.Subscribe<SinglePlayerViewModel, int>(this, "VideoClipUpdated", async (sender, videoId) =>
        {
            await RefreshVideoClipInGalleryAsync(videoId);
            // Marcar que hay estadísticas pendientes de actualizar
            _modifiedVideoIds.Add(videoId);
            _hasStatsUpdatePending = true;
        });
    }

    public int TrashItemCount
    {
        get => _trashItemCount;
        private set => SetProperty(ref _trashItemCount, value);
    }

    private async Task RefreshTrashItemCountAsync()
    {
        try
        {
            var sessions = await _trashService.GetTrashedSessionsAsync();
            var videos = await _trashService.GetTrashedVideosAsync();
            TrashItemCount = (sessions?.Count ?? 0) + (videos?.Count ?? 0);
        }
        catch
        {
            // Best-effort: no bloquear la UI por fallos de conteo.
        }
    }

    private async Task ExportDetailedTimesHtmlAsync()
    {
        await ExportDetailedTimesAsync("html");
    }

    private async Task ExportDetailedTimesPdfAsync()
    {
        await ExportDetailedTimesAsync("pdf");
    }

    private async Task ExportDetailedTimesAsync(string format)
    {
        System.Diagnostics.Debug.WriteLine($"[ExportDetailedTimes] Iniciando exportación: format={format}");

        if (SelectedSession == null)
        {
            System.Diagnostics.Debug.WriteLine("[ExportDetailedTimes] Error: No hay sesión seleccionada");
            await Shell.Current.DisplayAlert("Error", "No hay ninguna sesión seleccionada", "OK");
            return;
        }

        if (IsLoadingDetailedTimes)
        {
            System.Diagnostics.Debug.WriteLine("[ExportDetailedTimes] Abortado: IsLoadingDetailedTimes=true");
            return;
        }

        if (IsExportingDetailedTimes)
        {
            System.Diagnostics.Debug.WriteLine("[ExportDetailedTimes] Abortado: ya hay una exportación en curso");
            return;
        }

        try
        {
            IsExportingDetailedTimes = true;
            System.Diagnostics.Debug.WriteLine("[ExportDetailedTimes] IsExportingDetailedTimes = true");

            var sections = DetailedSectionTimes.ToList();
            if (sections.Count == 0)
            {
                // Si el popup está abierto pero aún no se ha cargado, cargamos datos.
                System.Diagnostics.Debug.WriteLine("[ExportDetailedTimes] No hay secciones cargadas, cargando...");
                var detailedTimes = await _statisticsService.GetDetailedAthleteSectionTimesAsync(SelectedSession.Id);
                sections = detailedTimes;
            }

            System.Diagnostics.Debug.WriteLine($"[ExportDetailedTimes] Secciones: {sections.Count}");

            if (sections.Count == 0)
            {
                await Shell.Current.DisplayAlert("Exportación", "No hay datos de tiempos para exportar", "OK");
                return;
            }

            string filePath;
            // Usar el atleta seleccionado en el popup (si hay)
            var refId = SelectedDetailedTimesAthlete?.Id;
            var refVideoId = SelectedDetailedTimesAthlete?.VideoId;
            var refName = SelectedDetailedTimesAthlete?.DisplayName;

            System.Diagnostics.Debug.WriteLine($"[ExportDetailedTimes] Exportando {format}...");

            if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
                filePath = await _tableExportService.ExportDetailedSectionTimesToPdfAsync(SelectedSession, sections, refId, refVideoId, refName);
            else
                filePath = await _tableExportService.ExportDetailedSectionTimesToHtmlAsync(SelectedSession, sections, refId, refVideoId, refName);

            System.Diagnostics.Debug.WriteLine($"[ExportDetailedTimes] Archivo generado: {filePath}");

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Exportar {SelectedSession.DisplayName}",
                File = new ShareFile(filePath)
            });

            System.Diagnostics.Debug.WriteLine("[ExportDetailedTimes] Share completado");
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardViewModel", $"Error exportando tabla ({format}): {ex.Message}", ex);
            System.Diagnostics.Debug.WriteLine($"[ExportDetailedTimes] Error: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo exportar la tabla a {format.ToUpperInvariant()}: {ex.Message}", "OK");
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine("[ExportDetailedTimes] Finalizando (IsExportingDetailedTimes = false)");
            IsExportingDetailedTimes = false;
        }
    }

    /// <summary>
    /// Manejador para cuando se exporta un video comparativo
    /// </summary>
    private async void OnVideoExported(object? sender, VideoExportedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Video exportado: SessionId={e.SessionId}, ClipId={e.VideoClipId}");
            
            // Si estamos viendo la galería general, recargar
            if (IsAllGallerySelected)
            {
                await LoadAllVideosAsync();
            }
            // Si estamos viendo la sesión donde se exportó el video, recargar esa sesión
            else if (SelectedSession?.Id == e.SessionId)
            {
                await LoadSelectedSessionVideosAsync(SelectedSession);
            }
            // Si no estamos viendo esa sesión, agregar el video al cache para que aparezca cuando se seleccione
            else if (_allVideosCache != null)
            {
                // Cargar el nuevo clip de la base de datos
                var newClip = await _databaseService.GetVideoClipByIdAsync(e.VideoClipId);
                if (newClip != null)
                {
                    _allVideosCache.Insert(0, newClip);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Error al procesar video exportado: {ex.Message}");
        }
    }

    /// <summary>
    /// Manejador para cambios de progreso en la importación en segundo plano
    /// </summary>
    private void OnImportProgressChanged(object? sender, ImportTask task)
    {
        IsBackgroundImporting = !task.IsCompleted;
        BackgroundImportProgress = task.Percentage;
        BackgroundImportText = $"{task.Name} ({task.ProcessedFiles}/{task.TotalFiles})";
        ImportingSessionName = task.Name.Replace("Sesión: ", "");
        ImportingCurrentFile = task.CurrentFile;
        OnPropertyChanged(nameof(BackgroundImportProgressNormalized));
    }

    /// <summary>
    /// Manejador para cuando se completa una importación en segundo plano
    /// </summary>
    private async void OnImportCompleted(object? sender, ImportTask task)
    {
        System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] OnImportCompleted llamado. HasError={task.HasError}, SessionId={task.CreatedSessionId}");
        
        if (task.HasError)
        {
            IsBackgroundImporting = false;
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Importación fallida: {task.ErrorMessage}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Importación completada: {task.Name}, SessionId={task.CreatedSessionId}");
        
        try
        {
            // Forzar recarga de sesiones directamente para evitar conflictos con IsBusy
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Recargando sesiones... IsBusy={IsBusy}");
            
            var stats = await _statisticsService.GetDashboardStatsAsync();
            
            // Actualizar en el hilo principal
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Stats = stats;
                RecentSessions.Clear();
                foreach (var session in stats.RecentSessions)
                {
                    RecentSessions.Add(session);
                }
                
                // Asegurar que la lista esté expandida para mostrar la nueva sesión
                if (!IsSessionsListExpanded)
                {
                    IsSessionsListExpanded = true;
                }

                SyncVisibleSessionRows();

                System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Sesiones recargadas: {RecentSessions.Count} total, filas={SessionRows.Count}");
            });
            
            // Ahora ocultamos el indicador de importación
            IsBackgroundImporting = false;
            
            // Si se creó una sesión, seleccionarla automáticamente
            if (task.CreatedSessionId.HasValue)
            {
                var newSession = await _databaseService.GetSessionByIdAsync(task.CreatedSessionId.Value);
                if (newSession != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Seleccionando nueva sesión: {newSession.DisplayName}");
                    SelectedSession = newSession;
                }
            }
            
            // Limpiar la tarea completada después de un breve momento
            await Task.Delay(3000);
            _importProgressService?.ClearCompletedTask();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Error al procesar importación completada: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Stack trace: {ex.StackTrace}");
            IsBackgroundImporting = false;
        }
    }

    private async Task OnVideoLessonTappedAsync(VideoLesson? lesson)
    {
        if (lesson == null)
            return;

        if (string.IsNullOrWhiteSpace(lesson.FilePath) || !File.Exists(lesson.FilePath))
        {
            await Shell.Current.DisplayAlert("Archivo no encontrado", "No se encontró el vídeo de esta videolección en el dispositivo.", "OK");
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(SinglePlayerPage)}?videoPath={Uri.EscapeDataString(lesson.FilePath)}");
    }

    private async Task ShareVideoLessonAsync(VideoLesson? lesson)
    {
        if (lesson == null)
            return;

        if (string.IsNullOrWhiteSpace(lesson.FilePath) || !File.Exists(lesson.FilePath))
        {
            await Shell.Current.DisplayAlert("Archivo no encontrado", "No se encontró el vídeo de esta videolección.", "OK");
            return;
        }

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = lesson.DisplayTitle ?? "Videolección",
            File = new ShareFile(lesson.FilePath)
        });
    }

    private async Task DeleteVideoLessonAsync(VideoLesson? lesson)
    {
        if (lesson == null)
            return;

        var confirm = await Shell.Current.DisplayAlert(
            "Eliminar videolección",
            $"¿Estás seguro de que quieres eliminar \"{lesson.DisplayTitle}\"?",
            "Eliminar",
            "Cancelar");

        if (!confirm)
            return;

        try
        {
            // Eliminar archivo de vídeo
            if (!string.IsNullOrWhiteSpace(lesson.FilePath) && File.Exists(lesson.FilePath))
                File.Delete(lesson.FilePath);

            // Eliminar thumbnail
            var thumbPath = Path.Combine(FileSystem.AppDataDirectory, "videoLessonThumbs", $"lesson_{lesson.Id}.jpg");
            if (File.Exists(thumbPath))
                File.Delete(thumbPath);

            // Eliminar de la base de datos
            await _databaseService.DeleteVideoLessonAsync(lesson);

            // Actualizar la colección en UI
            await MainThread.InvokeOnMainThreadAsync(() => VideoLessons.Remove(lesson));
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo eliminar la videolección: {ex.Message}", "OK");
        }
    }

    private async Task LoadVideoLessonsAsync(CancellationToken ct)
    {
        await MainThread.InvokeOnMainThreadAsync(() => VideoLessons.Clear());

        var lessons = await _databaseService.GetAllVideoLessonsAsync();
        if (ct.IsCancellationRequested)
            return;

        // Resolver nombres de sesión (mejor esfuerzo)
        var sessionNameCache = new Dictionary<int, string?>();

        var thumbnailsDir = Path.Combine(FileSystem.AppDataDirectory, "videoLessonThumbs");
        Directory.CreateDirectory(thumbnailsDir);

        var thumbTasks = new List<Task>();
        using var thumbSemaphore = new SemaphoreSlim(2);

        for (var idx = 0; idx < lessons.Count; idx++)
        {
            var lesson = lessons[idx];
            if (ct.IsCancellationRequested)
                return;

            if (!sessionNameCache.TryGetValue(lesson.SessionId, out var sessionName))
            {
                var session = lesson.SessionId > 0 ? await _databaseService.GetSessionByIdAsync(lesson.SessionId) : null;
                sessionName = session?.DisplayName;
                sessionNameCache[lesson.SessionId] = sessionName;
            }
            lesson.SessionDisplayName = sessionName;

            var thumbPath = Path.Combine(thumbnailsDir, $"lesson_{lesson.Id}.jpg");
            lesson.LocalThumbnailPath = File.Exists(thumbPath) ? thumbPath : null;

            await MainThread.InvokeOnMainThreadAsync(() => VideoLessons.Add(lesson));

            if (!File.Exists(thumbPath))
            {
                thumbTasks.Add(Task.Run(async () =>
                {
                    await thumbSemaphore.WaitAsync(ct);
                    try
                    {
                        if (!File.Exists(thumbPath))
                            await _thumbnailService.GenerateThumbnailAsync(lesson.FilePath, thumbPath);
                    }
                    catch
                    {
                        // Ignorar: placeholder
                    }
                    finally
                    {
                        thumbSemaphore.Release();
                    }

                    if (ct.IsCancellationRequested)
                        return;

                    if (File.Exists(thumbPath))
                    {
                        lesson.LocalThumbnailPath = thumbPath;
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            var currentIndex = VideoLessons.IndexOf(lesson);
                            if (currentIndex >= 0)
                                VideoLessons[currentIndex] = lesson;
                        });
                    }
                }, ct));
            }

            if (idx % 10 == 0)
                await Task.Yield();
        }

        try
        {
            await Task.WhenAll(thumbTasks);
        }
        catch
        {
            // cancelación
        }
    }

    /// <summary>
    /// Actualiza un video específico en la galería sin recargar toda la colección
    /// </summary>
    private async Task RefreshVideoClipInGalleryAsync(int videoId)
    {
        try
        {
            // Buscar el video en la colección actual
            var existingVideo = SelectedSessionVideos.FirstOrDefault(v => v.Id == videoId);
            if (existingVideo == null) return;

            var index = SelectedSessionVideos.IndexOf(existingVideo);
            if (index < 0) return;

            // Obtener el video actualizado de la base de datos
            var updatedVideo = await _databaseService.GetVideoClipByIdAsync(videoId);
            if (updatedVideo == null) return;

            // Cargar datos relacionados
            if (updatedVideo.AtletaId > 0)
            {
                updatedVideo.Atleta = await _databaseService.GetAthleteByIdAsync(updatedVideo.AtletaId);
            }
            if (updatedVideo.SessionId > 0)
            {
                updatedVideo.Session = await _databaseService.GetSessionByIdAsync(updatedVideo.SessionId);
            }

            // Hidratar tags y eventos
            await _databaseService.HydrateTagsForClips(new List<VideoClip> { updatedVideo });

            // Mantener el estado de selección si aplica
            updatedVideo.IsSelected = existingVideo.IsSelected;

            // Actualizar también en los caches si existe
            if (_allVideosCache != null)
            {
                var cacheIndex = _allVideosCache.FindIndex(v => v.Id == videoId);
                if (cacheIndex >= 0)
                    _allVideosCache[cacheIndex] = updatedVideo;
            }
            if (_filteredVideosCache != null)
            {
                var filteredIndex = _filteredVideosCache.FindIndex(v => v.Id == videoId);
                if (filteredIndex >= 0)
                    _filteredVideosCache[filteredIndex] = updatedVideo;
            }

            // Reemplazar en la colección (esto dispara la actualización del UI)
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SelectedSessionVideos[index] = updatedVideo;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al actualizar video en galería: {ex.Message}");
        }
    }

    private async Task PlayAsPlaylistAsync()
    {
        // Obtener videos seleccionados
        var source = _filteredVideosCache ?? _allVideosCache ?? SelectedSessionVideos.ToList();
        var selectedVideos = source.Where(v => _selectedVideoIds.Contains(v.Id)).OrderBy(v => v.CreationDate).ToList();
        
        if (selectedVideos.Count == 0)
        {
            await Shell.Current.DisplayAlert("Playlist", "No hay vídeos seleccionados.", "OK");
            return;
        }
        
        // Navegar a SinglePlayerPage con la playlist
        var singlePage = App.Current?.Handler?.MauiContext?.Services.GetService<Views.SinglePlayerPage>();
        if (singlePage?.BindingContext is SinglePlayerViewModel singleVm)
        {
            await singleVm.InitializeWithPlaylistAsync(selectedVideos, 0);
            await Shell.Current.Navigation.PushAsync(singlePage);
        }
        else
        {
            // Fallback: reproducir el primer video seleccionado
            var firstVideo = selectedVideos.First();
            await Shell.Current.GoToAsync($"{nameof(SinglePlayerPage)}?videoPath={Uri.EscapeDataString(firstVideo.ClipPath ?? "")}");
        }
    }

    private async Task EditVideoDetailsAsync()
    {
        // Verificar que hay videos seleccionados
        if (_selectedVideoIds.Count == 0)
        {
            await Shell.Current.DisplayAlert("Editar", "No hay vídeos seleccionados.", "OK");
            return;
        }
        
        // Cargar atletas disponibles y resetear selección
        var athletes = await _databaseService.GetAllAthletesAsync();
        BatchEditAthletes.Clear();
        foreach (var a in athletes.OrderBy(a => a.NombreCompleto))
        {
            a.IsSelected = false;
            BatchEditAthletes.Add(a);
        }
        
        // Cargar tags disponibles (no eventos) y resetear selección
        var tags = await _databaseService.GetAllTagsAsync();
        BatchEditTags.Clear();
        foreach (var t in tags.OrderBy(t => t.NombreTag))
        {
            t.IsSelectedBool = false;
            BatchEditTags.Add(t);
        }
        
        // Resetear selecciones
        SelectedBatchAthlete = null;
        BatchEditSection = 1;
        BatchEditSectionEnabled = false;
        BatchEditAthleteEnabled = false;
        _batchEditSelectedTagIds.Clear();

        // Resetear entradas de creación
        NewBatchAthleteSurname = string.Empty;
        NewBatchAthleteName = string.Empty;
        NewBatchTagText = string.Empty;
        
        // Mostrar popup
        ShowBatchEditPopup = true;
    }

    private static string NormalizeSpaces(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var parts = value
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', parts);
    }

    private async Task AddNewBatchAthleteAsync()
    {
        try
        {
            var apellido = NormalizeSpaces(NewBatchAthleteSurname);
            var nombre = NormalizeSpaces(NewBatchAthleteName);

            if (string.IsNullOrWhiteSpace(apellido) && string.IsNullOrWhiteSpace(nombre))
            {
                await Shell.Current.DisplayAlert(
                    "Atleta",
                    "Introduce el apellido y/o el nombre del atleta.",
                    "OK");
                return;
            }

            // Si existe, reutilizarlo
            var existing = await _databaseService.FindAthleteByNameAsync(nombre, apellido);
            Athlete athlete;
            if (existing != null)
            {
                athlete = existing;
            }
            else
            {
                athlete = new Athlete
                {
                    Nombre = nombre,
                    Apellido = apellido,
                    CategoriaId = 0,
                    Category = 0
                };

                await _databaseService.InsertAthleteAsync(athlete);
                _databaseService.InvalidateCache();
            }

            // Asegurar que está en la lista (y seleccionar la instancia que usa la UI)
            var inList = BatchEditAthletes.FirstOrDefault(a => a.Id == athlete.Id);
            if (inList == null)
            {
                athlete.IsSelected = false;
                BatchEditAthletes.Add(athlete);
                inList = athlete;
            }

            // Seleccionarlo
            SelectBatchAthlete(inList);
            BatchEditAthleteEnabled = true;

            NewBatchAthleteSurname = string.Empty;
            NewBatchAthleteName = string.Empty;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo añadir el atleta: {ex.Message}", "OK");
        }
    }

    private async Task AddNewBatchTagAsync()
    {
        try
        {
            var name = NormalizeSpaces(NewBatchTagText);
            if (string.IsNullOrWhiteSpace(name))
            {
                await Shell.Current.DisplayAlert("Etiqueta", "Introduce un nombre de etiqueta.", "OK");
                return;
            }

            // Si existe, reutilizarla
            var existing = await _databaseService.FindTagByNameAsync(name);
            Tag tag;
            if (existing != null)
            {
                tag = existing;
            }
            else
            {
                tag = new Tag { NombreTag = name, IsSelected = 0 };
                await _databaseService.InsertTagAsync(tag);
                _databaseService.InvalidateCache();
            }

            // Asegurar que está en la lista
            if (!BatchEditTags.Any(t => t.Id == tag.Id))
            {
                tag.IsSelectedBool = false;
                BatchEditTags.Add(tag);
            }

            // Marcarla como seleccionada para el lote
            _batchEditSelectedTagIds.Add(tag.Id);
            tag.IsSelectedBool = true;

            NewBatchTagText = string.Empty;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo añadir la etiqueta: {ex.Message}", "OK");
        }
    }
    private void ToggleBatchTag(Tag? tag)
    {
        if (tag == null) return;
        
        if (_batchEditSelectedTagIds.Contains(tag.Id))
        {
            _batchEditSelectedTagIds.Remove(tag.Id);
            tag.IsSelectedBool = false;
        }
        else
        {
            _batchEditSelectedTagIds.Add(tag.Id);
            tag.IsSelectedBool = true;
        }
    }

    private void SelectBatchAthlete(Athlete? athlete)
    {
        if (athlete == null) return;
        
        // Deseleccionar el atleta anterior
        if (SelectedBatchAthlete != null)
            SelectedBatchAthlete.IsSelected = false;
        
        // Seleccionar el nuevo atleta
        athlete.IsSelected = true;
        SelectedBatchAthlete = athlete;
        
        // Habilitar automáticamente la opción de atleta
        BatchEditAthleteEnabled = true;
    }

    public bool IsTagSelectedForBatchEdit(int tagId) => _batchEditSelectedTagIds.Contains(tagId);
    private async Task ApplyBatchEditAsync()
    {
        var source = _filteredVideosCache ?? _allVideosCache ?? SelectedSessionVideos.ToList();
        var selectedVideos = source.Where(v => _selectedVideoIds.Contains(v.Id)).ToList();
        
        if (selectedVideos.Count == 0)
        {
            ShowBatchEditPopup = false;
            return;
        }
        
        try
        {
            foreach (var video in selectedVideos)
            {
                bool updated = false;
                
                // Actualizar atleta si está habilitado
                if (BatchEditAthleteEnabled && SelectedBatchAthlete != null)
                {
                    video.AtletaId = SelectedBatchAthlete.Id;
                    video.Atleta = SelectedBatchAthlete;
                    updated = true;
                }
                
                // Actualizar sección si está habilitado
                if (BatchEditSectionEnabled && BatchEditSection > 0)
                {
                    video.Section = BatchEditSection;
                    updated = true;
                }
                
                // Guardar cambios en el video
                if (updated)
                {
                    await _databaseService.SaveVideoClipAsync(video);
                }
                
                // Agregar tags seleccionados
                foreach (var tagId in _batchEditSelectedTagIds)
                {
                    await _databaseService.AddTagToVideoAsync(video.Id, tagId, video.SessionId, video.AtletaId);
                }
            }
            
            // Cerrar popup
            ShowBatchEditPopup = false;
            
            // Mostrar confirmación
            var msg = $"Se actualizaron {selectedVideos.Count} vídeos.";
            if (_batchEditSelectedTagIds.Count > 0)
                msg += $"\nSe añadieron {_batchEditSelectedTagIds.Count} etiquetas.";
            
            await Shell.Current.DisplayAlert("Edición completada", msg, "OK");
            
            // Refrescar galería para mostrar cambios
            if (SelectedSession != null)
                await LoadSelectedSessionVideosAsync(SelectedSession);
            else if (IsAllGallerySelected)
                await LoadAllVideosAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo aplicar los cambios: {ex.Message}", "OK");
        }
    }

    private async Task DeleteSelectedVideosAsync()
    {
        // Obtener videos seleccionados
        var source = _filteredVideosCache ?? _allVideosCache ?? SelectedSessionVideos.ToList();
        var selectedVideos = source.Where(v => _selectedVideoIds.Contains(v.Id)).ToList();
        
        if (selectedVideos.Count == 0)
        {
            await Shell.Current.DisplayAlert("Eliminar", "No hay vídeos seleccionados.", "OK");
            return;
        }
        
        // Confirmar eliminación
        var confirm = await Shell.Current.DisplayAlert(
            "Eliminar vídeos",
            $"¿Mover {selectedVideos.Count} vídeo(s) a la papelera?\n\nPodrás restaurarlos desde la Papelera durante 30 días.",
            "Mover a papelera",
            "Cancelar");
        
        if (!confirm)
            return;
        
        try
        {
            foreach (var video in selectedVideos)
            {
                await _trashService.MoveVideoToTrashAsync(video.Id);
            }
            
            // Limpiar selección
            _selectedVideoIds.Clear();
            OnPropertyChanged(nameof(SelectedVideoCount));

            await RefreshTrashItemCountAsync();
            
            // Refrescar galería
            if (SelectedSession != null)
                await LoadSelectedSessionVideosAsync(SelectedSession);
            else if (IsAllGallerySelected)
                await LoadAllVideosAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudieron eliminar los vídeos: {ex.Message}", "OK");
        }
    }

    private async Task ShareSelectedVideosAsync()
    {
        // Obtener videos seleccionados
        var source = _filteredVideosCache ?? _allVideosCache ?? SelectedSessionVideos.ToList();
        var selectedVideos = source.Where(v => _selectedVideoIds.Contains(v.Id)).ToList();
        
        if (selectedVideos.Count == 0)
        {
            await Shell.Current.DisplayAlert("Compartir", "No hay vídeos seleccionados.", "OK");
            return;
        }
        
        // Verificar que los archivos existen (usar LocalClipPath si existe, sino ClipPath)
        var existingFiles = selectedVideos
            .Select(v => !string.IsNullOrWhiteSpace(v.LocalClipPath) && File.Exists(v.LocalClipPath) 
                ? v.LocalClipPath 
                : v.ClipPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => new ShareFile(path!))
            .ToList();
        
        if (existingFiles.Count == 0)
        {
            await Shell.Current.DisplayAlert("Compartir", "No se encontraron los archivos de vídeo.", "OK");
            return;
        }
        
        // Compartir archivos
        await Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = $"Compartir {existingFiles.Count} vídeo(s)",
            Files = existingFiles
        });
    }
    private async Task PlayParallelAnalysisAsync()
    {
        // Verificar que hay al menos un vídeo
        if (!HasParallelVideo1 && !HasParallelVideo2 && !HasParallelVideo3 && !HasParallelVideo4)
        {
            await Shell.Current.DisplayAlert("Análisis", 
                "Arrastra al menos un vídeo a las áreas de análisis.", "OK");
            return;
        }

        // Desactivar el modo preview antes de abrir el reproductor
        IsPreviewMode = false;

        // Usar SinglePlayerPage con modo comparación para todos los modos
        var singlePage = App.Current?.Handler?.MauiContext?.Services.GetService<Views.SinglePlayerPage>();
        if (singlePage?.BindingContext is SinglePlayerViewModel singleVm)
        {
            // Recopilar videos por slot (1-4)
            var slotVideos = new List<(int slot, VideoClip clip)>();
            if (ParallelVideo1 != null) slotVideos.Add((1, ParallelVideo1));
            if (ParallelVideo2 != null) slotVideos.Add((2, ParallelVideo2));
            if (ParallelVideo3 != null) slotVideos.Add((3, ParallelVideo3));
            if (ParallelVideo4 != null) slotVideos.Add((4, ParallelVideo4));

            if (slotVideos.Count == 0) return;

            // El video principal es el del primer slot ocupado
            var mainSlot = slotVideos[0];
            VideoClip mainVideo = mainSlot.clip;

            var occupiedSlots = slotVideos.Select(v => v.slot).ToHashSet();
            var layout = DetermineLayoutFromOccupiedSlots(occupiedSlots);

            // Recopilar videos de comparación excluyendo por SLOT (no por referencia de objeto)
            // Esto permite que el mismo video esté en múltiples slots
            var comparisonVideos = slotVideos
                .Where(v => v.slot != mainSlot.slot)
                .OrderBy(v => v.slot)
                .Select(v => (VideoClip?)v.clip)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[Dashboard] PlayParallelAnalysisAsync: mainVideo.Id={mainVideo.Id}, mainSlot={mainSlot.slot}, layout={layout}");
            System.Diagnostics.Debug.WriteLine($"[Dashboard] slotVideos count={slotVideos.Count}, comparisonVideos count={comparisonVideos.Count}");
            for (int i = 0; i < comparisonVideos.Count; i++)
            {
                var cv = comparisonVideos[i];
                System.Diagnostics.Debug.WriteLine($"[Dashboard] comparisonVideos[{i}]: Id={cv?.Id}, ClipPath={cv?.ClipPath}");
            }

            await singleVm.InitializeWithComparisonAsync(
                mainVideo,
                comparisonVideos.ElementAtOrDefault(0),
                comparisonVideos.ElementAtOrDefault(1),
                comparisonVideos.ElementAtOrDefault(2),
                layout);

            await Shell.Current.Navigation.PushAsync(singlePage);
        }
    }

    private static ComparisonLayout DetermineLayoutFromOccupiedSlots(IReadOnlyCollection<int> occupiedSlots)
    {
        if (occupiedSlots == null || occupiedSlots.Count <= 1)
            return ComparisonLayout.Single;

        if (occupiedSlots.Count >= 3)
            return ComparisonLayout.Quad2x2;

        // Exactamente 2 slots: decidir si están en la misma fila o columna
        var has1 = occupiedSlots.Contains(1);
        var has2 = occupiedSlots.Contains(2);
        var has3 = occupiedSlots.Contains(3);
        var has4 = occupiedSlots.Contains(4);

        // Misma fila: (1,2) o (3,4)
        if ((has1 && has2) || (has3 && has4))
            return ComparisonLayout.Horizontal2x1;

        // Misma columna: (1,3) o (2,4)
        if ((has1 && has3) || (has2 && has4))
            return ComparisonLayout.Vertical1x2;

        // Diagonal: usar 2x2
        return ComparisonLayout.Quad2x2;
    }

    private async Task PreviewParallelAnalysisAsync()
    {
        // Toggle del modo preview
        IsPreviewMode = !IsPreviewMode;
        await Task.CompletedTask;
    }

    private void ClearParallelAnalysis()
    {
        IsPreviewMode = false;
        ParallelVideo1 = null;
        ParallelVideo2 = null;
        ParallelVideo3 = null;
        ParallelVideo4 = null;
    }

    private async Task OnVideoTappedAsync(VideoClip? video)
    {
        if (video == null) return;

        if (IsMultiSelectMode)
        {
            ToggleVideoSelection(video);
        }
        else
        {
            await PlaySelectedVideoAsync(video);
        }
    }

    private void ClearFilters(bool skipApplyFilters = false)
    {
        _suppressFilterSelectionChanged = true;
        try
        {
            // Limpiar selección múltiple
            foreach (var place in FilterPlaces) place.IsSelected = false;
            foreach (var athlete in FilterAthletes) athlete.IsSelected = false;
            foreach (var section in FilterSections) section.IsSelected = false;
            foreach (var tag in FilterTagItems) tag.IsSelected = false;
        }
        finally
        {
            _suppressFilterSelectionChanged = false;
        }
        
        // Limpiar filtros simples
        _selectedFilterDateFrom = null;
        _selectedFilterDateTo = null;
        
        OnPropertyChanged(nameof(SelectedFilterDateFrom));
        OnPropertyChanged(nameof(SelectedFilterDateTo));
        OnPropertyChanged(nameof(SelectedPlacesSummary));
        OnPropertyChanged(nameof(SelectedAthletesSummary));
        OnPropertyChanged(nameof(SelectedSectionsSummary));
        OnPropertyChanged(nameof(SelectedTagsSummary));
        
        if (!skipApplyFilters)
        {
            _ = ApplyFiltersAsync();
        }
    }

    private void ToggleFilterItem(object? item)
    {
        try
        {
            if (item == null)
                return;

            // FilterItem<T> es genérico; usamos reflexión para alternar IsSelected.
            var prop = item.GetType().GetProperty("IsSelected");
            if (prop == null || prop.PropertyType != typeof(bool) || !prop.CanWrite)
                return;

            var current = (bool)(prop.GetValue(item) ?? false);
            prop.SetValue(item, !current);
        }
        catch
        {
            // Ignorar: un item no compatible no debería romper la UI.
        }
    }

    private async Task ApplyFiltersAsync()
    {
        if (!IsAllGallerySelected || _allVideosCache == null)
            return;

        var version = Interlocked.Increment(ref _filtersVersion);

        // Si hay una carpeta inteligente activa, usar sus videos filtrados como base
        var allVideos = _activeSmartFolder != null && _smartFolderFilteredVideosCache != null
            ? _smartFolderFilteredVideosCache
            : _allVideosCache;
        var sessionsSnapshot = RecentSessions.ToList();
        var inputsSnapshot = _allInputsCache;

        // Capturar selección en UI thread
        var selectedPlaces = FilterPlaces.Where(p => p.IsSelected).Select(p => p.Value).ToList();
        var selectedAthletes = FilterAthletes.Where(a => a.IsSelected).Select(a => a.Value.Id).ToList();
        var selectedSections = FilterSections.Where(s => s.IsSelected).Select(s => s.Value).ToList();
        var selectedTags = FilterTagItems.Where(t => t.IsSelected).Select(t => t.Value.Id).ToList();

        await Task.Yield();

        var (filteredList, statsSnapshot) = await Task.Run(() =>
        {
            IEnumerable<VideoClip> query = allVideos;

            if (selectedPlaces.Any())
            {
                var sessionsInPlaces = sessionsSnapshot
                    .Where(s =>
                    {
                        var lugar = s.Lugar;
                        return lugar != null && selectedPlaces.Contains(lugar);
                    })
                    .Select(s => s.Id)
                    .ToHashSet();
                query = query.Where(v => sessionsInPlaces.Contains(v.SessionId));
            }

            if (SelectedFilterDateFrom.HasValue)
                query = query.Where(v => v.CreationDateTime >= SelectedFilterDateFrom.Value);

            if (SelectedFilterDateTo.HasValue)
                query = query.Where(v => v.CreationDateTime <= SelectedFilterDateTo.Value.AddDays(1));

            if (selectedAthletes.Any())
                query = query.Where(v => selectedAthletes.Contains(v.AtletaId));

            if (selectedSections.Any())
                query = query.Where(v => selectedSections.Contains(v.Section));

            if (selectedTags.Any() && inputsSnapshot != null)
            {
                var videoIdsWithTags = inputsSnapshot
                    .Where(i => selectedTags.Contains(i.InputTypeId))
                    .Select(i => i.VideoId)
                    .ToHashSet();
                query = query.Where(v => videoIdsWithTags.Contains(v.Id));
            }

            var list = query.ToList();
            var snapshot = BuildGalleryStatsSnapshot(list);
            return (list, snapshot);
        });

        // Si durante el cálculo se disparó otra ejecución más reciente, descartamos esta.
        if (version != Volatile.Read(ref _filtersVersion))
            return;

        _filteredVideosCache = filteredList;
        
        // Recargar videos paginados con el filtro aplicado
        _currentPage = 0;
        
        var filteredCache = _filteredVideosCache ?? new List<VideoClip>();
        var firstBatch = filteredCache.Take(PageSize).ToList();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, CancellationToken.None);
        });
        _currentPage = 1;
        HasMoreVideos = _filteredVideosCache.Count > PageSize;

        // Actualizar estadísticas sin bloquear el UI
        await ApplyGalleryStatsSnapshotAsync(statsSnapshot, CancellationToken.None);

        OnPropertyChanged(nameof(TotalFilteredVideoCount));
        OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));

        await Task.CompletedTask;
    }

    private void UpdateStatisticsFromFilteredVideos()
    {
        var clips = _filteredVideosCache ?? _allVideosCache ?? new List<VideoClip>();

        // Limpiar estadísticas anteriores
        SelectedSessionSectionStats.Clear();
        SelectedSessionAthleteTimes.Clear();
        SelectedSessionSectionDurationMinutes.Clear();
        SelectedSessionSectionLabels.Clear();
        SelectedSessionTagVideoCounts.Clear();
        SelectedSessionTagLabels.Clear();

        // Estadísticas por sección
        var sectionGroups = clips
            .GroupBy(v => v.Section)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in sectionGroups)
        {
            var stats = new SectionStats
            {
                Section = group.Key,
                VideoCount = group.Count(),
                TotalDuration = group.Sum(v => v.ClipDuration)
            };
            SelectedSessionSectionStats.Add(stats);
            SelectedSessionSectionDurationMinutes.Add(Math.Round(stats.TotalDuration / 60.0, 1));
            SelectedSessionSectionLabels.Add(stats.Section.ToString());
        }

        // Tabla de tiempos por atleta
        var byAthlete = clips
            .Where(v => v.AtletaId != 0)
            .GroupBy(v => v.AtletaId)
            .Select(g => new
            {
                AthleteName = g.FirstOrDefault()?.Atleta?.NombreCompleto ?? $"Atleta {g.Key}",
                VideoCount = g.Count(),
                TotalSeconds = g.Sum(v => v.ClipDuration),
                AvgSeconds = g.Any() ? g.Average(v => v.ClipDuration) : 0
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        foreach (var row in byAthlete)
        {
            SelectedSessionAthleteTimes.Add(new SessionAthleteTimeRow(
                row.AthleteName,
                row.VideoCount,
                row.TotalSeconds,
                row.AvgSeconds));
        }
    }

    private async Task SelectAllGalleryAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[SelectAllGalleryAsync] Called");
        // Deseleccionar sesión actual usando el setter para que se deseleccione el row
        SelectedSession = null;
        
        // Limpiar carpeta inteligente activa
        _activeSmartFolder = null;
        _smartFolderFilteredVideosCache = null;
        
        IsAllGallerySelected = true;
        System.Diagnostics.Debug.WriteLine($"[SelectAllGalleryAsync] IsAllGallerySelected = true, calling LoadAllVideosAsync...");
        await LoadAllVideosAsync();
        System.Diagnostics.Debug.WriteLine($"[SelectAllGalleryAsync] Done");
    }

    private async Task LoadAllVideosAsync()
    {
        _selectedSessionVideosCts?.Cancel();
        _selectedSessionVideosCts?.Dispose();
        _selectedSessionVideosCts = new CancellationTokenSource();
        var ct = _selectedSessionVideosCts.Token;

        SelectedSessionVideos.Clear();
        SelectedSessionSectionStats.Clear();
        SelectedSessionAthleteTimes.Clear();
        SelectedSessionSectionDurationMinutes.Clear();
        SelectedSessionSectionLabels.Clear();
        SelectedSessionTagVideoCounts.Clear();
        SelectedSessionTagLabels.Clear();
        SelectedSessionPenaltyCounts.Clear();
        SelectedSessionPenaltyLabels.Clear();
        SelectedSessionInputs.Clear();
        SelectedSessionValoraciones.Clear();

        _currentPage = 0;
        _allVideosCache = null;
        _filteredVideosCache = null;
        HasMoreVideos = false;

        OnPropertyChanged(nameof(AllGalleryItemCount));
        
        // Limpiar filtros al cargar
        ClearFilters();

        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));

        try
        {
            IsLoadingSelectedSessionVideos = true;
            await Task.Yield();
            
            // Cargar todos los videos en caché
            System.Diagnostics.Debug.WriteLine($"[LoadAllVideosAsync] Loading all videos from DB...");
            _allVideosCache = await _databaseService.GetAllVideoClipsAsync();
            System.Diagnostics.Debug.WriteLine($"[LoadAllVideosAsync] Videos loaded: {_allVideosCache?.Count ?? 0}");
            if (ct.IsCancellationRequested) return;

            OnPropertyChanged(nameof(AllGalleryItemCount));

            // Crear diccionario de sesiones para asignar a cada clip
            var sessionIds = _allVideosCache?.Select(c => c.SessionId).Distinct().ToList() ?? new List<int>();
            var sessionsDict = new Dictionary<int, Session>();
            foreach (var sessionId in sessionIds)
            {
                var session = await _databaseService.GetSessionByIdAsync(sessionId);
                if (session != null)
                    sessionsDict[sessionId] = session;
            }
            
            // Asignar la sesión a cada clip para que DisplayLine1/2 funcionen
            if (_allVideosCache != null)
            {
                foreach (var clip in _allVideosCache)
                {
                    if (sessionsDict.TryGetValue(clip.SessionId, out var session))
                        clip.Session = session;
                }
            }

            // Cargar primer lote
            _filteredVideosCache = _allVideosCache;
            var filteredCache = _filteredVideosCache ?? new List<VideoClip>();
            var firstBatch = filteredCache.Take(PageSize).ToList();
            System.Diagnostics.Debug.WriteLine($"[LoadAllVideosAsync] First batch: {firstBatch.Count}, adding to SelectedSessionVideos...");
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, ct);
            });
            System.Diagnostics.Debug.WriteLine($"[LoadAllVideosAsync] SelectedSessionVideos.Count: {SelectedSessionVideos.Count}");
            _currentPage = 1;
            HasMoreVideos = _filteredVideosCache.Count > PageSize;

            // Cargar opciones de filtro en paralelo (no bloquea la primera carga de galería)
            var filterTask = LoadFilterOptionsAsync();

            // Stats: cálculo fuera del hilo UI
            var snapshot = await Task.Run(() => BuildGalleryStatsSnapshot(filteredCache), ct);
            if (!ct.IsCancellationRequested)
                await ApplyGalleryStatsSnapshotAsync(snapshot, ct);

            // Cargar estadísticas absolutas de tags (galería general = null)
            await LoadAbsoluteTagStatsAsync(null);

            await filterTask;

            OnPropertyChanged(nameof(TotalFilteredVideoCount));
            OnPropertyChanged(nameof(TotalAvailableVideoCount));
            OnPropertyChanged(nameof(VideoCountDisplayText));
            OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
            OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
            OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));

            // Actualizar contadores de SmartFolders
            UpdateSmartFolderVideoCounts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading all videos: {ex.Message}");
        }
        finally
        {
            IsLoadingSelectedSessionVideos = false;
        }
    }

    private async Task LoadFilterOptionsAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] Start - RecentSessions: {RecentSessions.Count}, _allVideosCache: {_allVideosCache?.Count ?? 0}");

        await Task.Yield();

        var sessionsSnapshot = RecentSessions.ToList();
        var allVideosSnapshot = _allVideosCache?.ToList();
        
        // Cargar lugares únicos de las sesiones
        FilterPlaces.Clear();
        var places = await Task.Run(() => sessionsSnapshot
            .Where(s => !string.IsNullOrEmpty(s.Lugar))
            .Select(s => s.Lugar!)
            .Distinct()
            .OrderBy(p => p)
            .ToList());
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] Places found: {places.Count}");
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var i = 0;
            foreach (var place in places)
            {
                var item = new PlaceFilterItem(place);
                item.SelectionChanged += OnFilterSelectionChanged;
                FilterPlaces.Add(item);

                i++;
                if (i % 30 == 0)
                    await Task.Yield();
            }
        });
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] FilterPlaces.Count: {FilterPlaces.Count}");

        // Cargar atletas
        FilterAthletes.Clear();
        var athletes = await _databaseService.GetAllAthletesAsync();
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] Athletes from DB: {athletes.Count}");
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var i = 0;
            foreach (var athlete in athletes.OrderBy(a => a.NombreCompleto))
            {
                var item = new AthleteFilterItem(athlete);
                item.SelectionChanged += OnFilterSelectionChanged;
                FilterAthletes.Add(item);

                i++;
                if (i % 30 == 0)
                    await Task.Yield();
            }
        });
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] FilterAthletes.Count: {FilterAthletes.Count}");

        // Cargar secciones únicas de los videos
        FilterSections.Clear();
        if (allVideosSnapshot != null)
        {
            var sections = await Task.Run(() => allVideosSnapshot
                .Select(v => v.Section)
                .Distinct()
                .OrderBy(s => s)
                .ToList());
            System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] Sections found: {sections.Count}");
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var section in sections)
                {
                    var item = new SectionFilterItem(section);
                    item.SelectionChanged += OnFilterSelectionChanged;
                    FilterSections.Add(item);

                    i++;
                    if (i % 30 == 0)
                        await Task.Yield();
                }
            });
        }
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] FilterSections.Count: {FilterSections.Count}");

        OnPropertyChanged(nameof(SelectedPlacesSummary));
        OnPropertyChanged(nameof(SelectedAthletesSummary));
        OnPropertyChanged(nameof(SelectedSectionsSummary));

        // Cargar inputs y tags para filtrado
        _allInputsCache = await _databaseService.GetAllInputsAsync();
        _allTagsCache = await _databaseService.GetAllTagsAsync();

        // Cargar tags únicos (solo los que tienen inputs asociados a videos)
        FilterTagItems.Clear();
        if (_allInputsCache != null && _allTagsCache != null)
        {
            var usedTagIds = _allInputsCache
                .Select(i => i.InputTypeId)
                .Distinct()
                .ToHashSet();

            var tags = _allTagsCache
                .Where(t => usedTagIds.Contains(t.Id) && !string.IsNullOrEmpty(t.NombreTag))
                .OrderBy(t => t.NombreTag);
            
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var tag in tags)
                {
                    var item = new TagFilterItem(tag);
                    item.SelectionChanged += OnFilterSelectionChanged;
                    FilterTagItems.Add(item);

                    i++;
                    if (i % 30 == 0)
                        await Task.Yield();
                }
            });
        }
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] FilterTagItems.Count: {FilterTagItems.Count}");
        OnPropertyChanged(nameof(SelectedTagsSummary));
    }

    private void OnFilterSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressFilterSelectionChanged)
            return;

        OnPropertyChanged(nameof(SelectedPlacesSummary));
        OnPropertyChanged(nameof(SelectedAthletesSummary));
        OnPropertyChanged(nameof(SelectedSectionsSummary));
        OnPropertyChanged(nameof(SelectedTagsSummary));
        _ = ApplyFiltersAsync();
    }

    private async Task LoadMoreVideosAsync()
    {
        var videosSource = _filteredVideosCache ?? _allVideosCache;
        if (IsLoadingMore || !HasMoreVideos || videosSource == null)
            return;

        try
        {
            IsLoadingMore = true;
            
            // Simular un pequeño delay para UI
            await Task.Delay(100);

            var skip = _currentPage * PageSize;
            var nextBatch = videosSource.Skip(skip).Take(PageSize).ToList();

            var ct = _selectedSessionVideosCts?.Token ?? CancellationToken.None;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var clip in nextBatch)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    SelectedSessionVideos.Add(clip);
                    i++;
                    if (i % 20 == 0)
                        await Task.Yield();
                }
            });

            _currentPage++;
            HasMoreVideos = videosSource.Count > (_currentPage * PageSize);
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private async Task DeleteSelectedSessionAsync()
    {
        var session = SelectedSession;
        if (session == null)
        {
            await Shell.Current.DisplayAlert("Eliminar", "Selecciona una sesión primero.", "OK");
            return;
        }

        var confirm = await Shell.Current.DisplayAlert(
            "Eliminar sesión",
            $"¿Seguro que quieres mover a la papelera la sesión '{session.DisplayName}'?\n\nPodrás restaurarla durante 30 días.",
            "Eliminar",
            "Cancelar");

        if (!confirm)
            return;

        var deleted = false;
        try
        {
            IsBusy = true;
            await _trashService.MoveSessionToTrashAsync(session.Id);
            SelectedSession = null;
            deleted = true;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo eliminar la sesión: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }

        // Refrescar la lista de sesiones (LoadDataAsync se auto-gestiona con IsBusy,
        // así que debe llamarse cuando IsBusy ya esté a false).
        if (deleted)
            await LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            Stats = await _statisticsService.GetDashboardStatsAsync();

            RecentSessions.Clear();
            foreach (var session in Stats.RecentSessions)
            {
                RecentSessions.Add(session);
            }

            SyncVisibleSessionRows();

            // Cargar el atleta de referencia del perfil del usuario
            await LoadReferenceAthleteAsync();

            // Si la sesión seleccionada ya no existe (p.ej. tras borrar), limpiar panel derecho.
            if (SelectedSession != null && !RecentSessions.Any(s => s.Id == SelectedSession.Id))
            {
                SelectedSession = null;
            }

            // Recargar videolecciones si están seleccionadas (al volver de SinglePlayerPage)
            if (IsVideoLessonsSelected)
            {
                _selectedSessionVideosCts?.Cancel();
                _selectedSessionVideosCts?.Dispose();
                _selectedSessionVideosCts = new CancellationTokenSource();
                await LoadVideoLessonsAsync(_selectedSessionVideosCts.Token);
            }
            // Por defecto, mostrar Galería General al iniciar (solo si no hay ninguna vista activa)
            else if (SelectedSession == null && !IsAllGallerySelected && !IsDiaryViewSelected)
            {
                await SelectAllGalleryAsync();
            }

            await RefreshTrashItemCountAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar los datos: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSelectedSessionVideosAsync(Session? session)
    {
        _selectedSessionVideosCts?.Cancel();
        _selectedSessionVideosCts?.Dispose();
        _selectedSessionVideosCts = new CancellationTokenSource();
        var ct = _selectedSessionVideosCts.Token;

        SelectedSessionVideos.Clear();
        SelectedSessionSectionStats.Clear();
        SelectedSessionAthleteTimes.Clear();
        SelectedSessionSectionDurationMinutes.Clear();
        SelectedSessionSectionLabels.Clear();
        SelectedSessionTagVideoCounts.Clear();
        SelectedSessionTagLabels.Clear();
        SelectedSessionPenaltyCounts.Clear();
        SelectedSessionPenaltyLabels.Clear();
        SelectedSessionInputs.Clear();
        SelectedSessionValoraciones.Clear();

        _currentPage = 0;
        _allVideosCache = null;
        _filteredVideosCache = null;
        HasMoreVideos = false;

        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
        if (session == null) return;

        try
        {
            IsLoadingSelectedSessionVideos = true;
            var clips = await _databaseService.GetVideoClipsBySessionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            // Asignar la sesión actual a cada clip para que DisplayLine1/2 funcionen
            foreach (var clip in clips)
            {
                clip.Session = session;
            }

            // Guardar en caché para paginación
            _allVideosCache = clips;
            
            // Cargar primer lote
            var firstBatch = clips.Take(PageSize).ToList();
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, ct);
            });
            _currentPage = 1;
            HasMoreVideos = clips.Count > PageSize;

            // Estadísticas por sección (tabla + gráfico)
            var sectionStats = await _statisticsService.GetVideosBySectionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var s in sectionStats)
                {
                    SelectedSessionSectionStats.Add(s);

                    // Para gráfico: minutos, y etiqueta corta
                    SelectedSessionSectionDurationMinutes.Add(Math.Round(s.TotalDuration / 60.0, 1));
                    SelectedSessionSectionLabels.Add(s.Section.ToString());

                    i++;
                    if (i % 20 == 0)
                        await Task.Yield();
                }
            });

            // Tabla de tiempos por atleta
            var byAthlete = clips
                .Where(v => v.AtletaId != 0)
                .GroupBy(v => v.AtletaId)
                .Select(g => new
                {
                    AthleteName = g.FirstOrDefault()?.Atleta?.NombreCompleto ?? $"Atleta {g.Key}",
                    VideoCount = g.Count(),
                    TotalSeconds = g.Sum(v => v.ClipDuration),
                    AvgSeconds = g.Any() ? g.Average(v => v.ClipDuration) : 0
                })
                .OrderByDescending(x => x.TotalSeconds)
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var row in byAthlete)
                {
                    SelectedSessionAthleteTimes.Add(new SessionAthleteTimeRow(
                        row.AthleteName,
                        row.VideoCount,
                        row.TotalSeconds,
                        row.AvgSeconds));

                    i++;
                    if (i % 30 == 0)
                        await Task.Yield();
                }
            });

            // Etiquetas (tags) y penalizaciones: usando Inputs de la sesión.
            // Asunción: InputTypeId representa el TagId.
            var inputs = await _databaseService.GetInputsBySessionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var input in inputs.OrderBy(i => i.InputDateTime))
                {
                    SelectedSessionInputs.Add(input);
                    i++;
                    if (i % 50 == 0)
                        await Task.Yield();
                }
            });

            var tagStats = inputs
                .GroupBy(i => i.InputTypeId)
                .Select(g => new
                {
                    TagId = g.Key,
                    Assignments = g.Count(),
                    VideoCount = g.Select(x => x.VideoId).Distinct().Count()
                })
                .OrderByDescending(x => x.VideoCount)
                .ThenBy(x => x.TagId)
                .Take(12)
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var t in tagStats)
                {
                    SelectedSessionTagLabels.Add(t.TagId.ToString());
                    SelectedSessionTagVideoCounts.Add(t.VideoCount);
                }
            });

            // Penalizaciones (tags 2 y 50): por número de asignaciones
            var penalty2 = inputs.Count(i => i.InputTypeId == 2);
            var penalty50 = inputs.Count(i => i.InputTypeId == 50);
            SelectedSessionPenaltyLabels.Add("2");
            SelectedSessionPenaltyLabels.Add("50");
            SelectedSessionPenaltyCounts.Add(penalty2);
            SelectedSessionPenaltyCounts.Add(penalty50);

            // Cargar estadísticas absolutas de tags
            await LoadAbsoluteTagStatsAsync(session.Id);
            
            // Cargar tiempos por sección (Split Times)
            await LoadAthleteSectionTimesAsync(session.Id);

            // Valoraciones (tabla valoracion)
            var valoraciones = await _databaseService.GetValoracionesBySessionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var v in valoraciones.OrderBy(v => v.InputDateTime))
                {
                    SelectedSessionValoraciones.Add(v);
                    i++;
                    if (i % 50 == 0)
                        await Task.Yield();
                }
            });

            OnPropertyChanged(nameof(TotalFilteredVideoCount));
            OnPropertyChanged(nameof(TotalAvailableVideoCount));
            OnPropertyChanged(nameof(VideoCountDisplayText));
            OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
            OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar los vídeos: {ex.Message}", "OK");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoadingSelectedSessionVideos = false;
        }
    }

    private async Task PlaySelectedVideoAsync(VideoClip? video)
    {
        if (video == null) return;

        var videoPath = video.LocalClipPath;

        // Fallback: construir ruta local desde la carpeta de la sesión
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            try
            {
                var session = SelectedSession ?? await _databaseService.GetSessionByIdAsync(video.SessionId);
                if (!string.IsNullOrWhiteSpace(session?.PathSesion))
                {
                    var normalized = (video.ClipPath ?? "").Replace('\\', '/');
                    var fileName = Path.GetFileName(normalized);
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = $"CROWN{video.Id}.mp4";

                    var candidate = Path.Combine(session.PathSesion, "videos", fileName);
                    if (File.Exists(candidate))
                        videoPath = candidate;
                }
            }
            catch
            {
                // Ignorar: si falla, se mostrará el error estándar
            }
        }

        // Último recurso: usar ClipPath si fuera una ruta real
        if (string.IsNullOrWhiteSpace(videoPath))
            videoPath = video.ClipPath;

        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            await Shell.Current.DisplayAlert("Error", "El archivo de video no existe", "OK");
            return;
        }

        // Obtener la página del contenedor DI para poder pasar el VideoClip completo
        var playerPage = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<SinglePlayerPage>();
        if (playerPage?.BindingContext is SinglePlayerViewModel vm)
        {
            // Asegurar que el video tiene la información de Session y Atleta cargada
            if (video.Session == null && video.SessionId > 0)
            {
                video.Session = SelectedSession ?? await _databaseService.GetSessionByIdAsync(video.SessionId);
            }
            if (video.Atleta == null && video.AtletaId > 0)
            {
                video.Atleta = await _databaseService.GetAthleteByIdAsync(video.AtletaId);
            }
            // Cargar los tags del video
            if (video.Tags == null && video.Id > 0)
            {
                video.Tags = await _databaseService.GetTagsForVideoAsync(video.Id);
            }
            
            // Actualizar la ruta local si fue resuelta
            video.LocalClipPath = videoPath;
            
            // Inicializar el ViewModel con el video (carga datos de sesión y playlist)
            await vm.InitializeWithVideoAsync(video);
            
            await Shell.Current.Navigation.PushAsync(playerPage);
        }
        else
        {
            // Fallback: navegación por URL
            await Shell.Current.GoToAsync($"{nameof(SinglePlayerPage)}?videoPath={Uri.EscapeDataString(videoPath)}");
        }
    }

    private async Task ShowImportOptionsAsync()
    {
        var action = await Shell.Current.DisplayActionSheet(
            "¿Cómo deseas importar?",
            "Cancelar",
            null,
            "Desde archivo .crown",
            "Crear sesión desde vídeos");

        if (action == "Desde archivo .crown")
        {
            await ImportCrownFileAsync();
        }
        else if (action == "Crear sesión desde vídeos")
        {
            await OpenImportPageForVideosAsync();
        }
    }

    private static async Task OpenImportPageForVideosAsync()
    {
        await Shell.Current.GoToAsync(nameof(ImportPage));
    }

    private async Task ImportCrownFileAsync()
    {
        if (IsImporting) return; // Evitar múltiples importaciones simultáneas
        
        try
        {
            IsImporting = true;
            ImportProgressValue = 0;
            ImportProgressText = "Abriendo selector de archivos...";

            // Permite que la UI pinte el estado antes de abrir el picker.
            await Task.Yield();

            System.Diagnostics.Debug.WriteLine("Iniciando selección de archivo...");

            // El picker nativo MacCrownFilePicker usa runModal que es síncrono
            // y responde inmediatamente cuando el usuario selecciona o cancela.
            // No es necesario timeout ya que el picker siempre responde.
            var filePath = await _crownFileService.PickCrownFilePathAsync();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                System.Diagnostics.Debug.WriteLine("No se seleccionó ningún archivo (o no se pudo acceder a él)");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Archivo seleccionado: {filePath}");

            ImportProgressText = "Iniciando importación...";

            var progress = new Progress<ImportProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ImportProgressText = p.Message;
                    ImportProgressValue = p.Percentage;
                });
            });

            var result = await _crownFileService.ImportCrownFileAsync(filePath, progress);

            if (result.Success)
            {
                await Shell.Current.DisplayAlert(
                    "Importación Exitosa",
                    $"Sesión '{result.SessionName}' importada correctamente.\n" +
                    $"Videos: {result.VideosImported}\n" +
                    $"Atletas: {result.AthletesImported}",
                    "OK");

                await LoadDataAsync();
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", result.ErrorMessage ?? "Error desconocido", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error en importación: {ex}");
            await Shell.Current.DisplayAlert("Error", $"Error al importar: {ex.Message}", "OK");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsImporting = false;
                ImportProgressText = "";
                ImportProgressValue = 0;
            });
        }
    }

    private async Task ViewSessionAsync(Session? session)
    {
        if (session == null) return;
        await Shell.Current.GoToAsync($"{nameof(SessionDetailPage)}?sessionId={session.Id}");
    }

    private async Task ViewAllSessionsAsync()
    {
        await Shell.Current.GoToAsync(nameof(SessionsPage));
    }

    private async Task ViewVideoLessonsAsync()
    {
        try
        {
            IsLoadingSelectedSessionVideos = true;

            // Cambiar modo en el Dashboard (sin navegar)
            // NOTA: Esto cancela _selectedSessionVideosCts, así que creamos el token DESPUÉS
            SelectedSession = null;
            IsAllGallerySelected = false;
            IsVideoLessonsSelected = true;

            // Crear nuevo CancellationTokenSource DESPUÉS de SelectedSession = null
            // porque el setter de SelectedSession cancela el token anterior
            _selectedSessionVideosCts?.Cancel();
            _selectedSessionVideosCts?.Dispose();
            _selectedSessionVideosCts = new CancellationTokenSource();
            var ct = _selectedSessionVideosCts.Token;

            // Desactivar selección múltiple (no aplica a videolecciones)
            IsMultiSelectMode = false;
            IsSelectAllActive = false;

            // Limpiar caches de vídeos para que contadores y paginación no interfieran
            _currentPage = 0;
            _allVideosCache = null;
            _filteredVideosCache = null;
            HasMoreVideos = false;

            await LoadVideoLessonsAsync(ct);
        }
        catch (Exception ex)
        {
            if (_selectedSessionVideosCts?.Token.IsCancellationRequested != true)
                await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar las videolecciones: {ex.Message}", "OK");
        }
        finally
        {
            if (_selectedSessionVideosCts?.Token.IsCancellationRequested != true)
                IsLoadingSelectedSessionVideos = false;
        }
    }

    private static async Task ViewTrashAsync()
    {
        await Shell.Current.GoToAsync(nameof(TrashPage));
    }

    private async Task ViewAthletesAsync()
    {
        await Shell.Current.GoToAsync(nameof(AthletesPage));
    }

    #region Session Diary Methods

    private async Task LoadSessionDiaryAsync()
    {
        if (SelectedSession == null) return;

        try
        {
            // Obtener el atleta de referencia del perfil de usuario
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null) return;

            var athleteId = profile.ReferenceAthleteId.Value;

            // Cargar diario existente o crear uno nuevo
            _currentSessionDiary = await _databaseService.GetSessionDiaryAsync(SelectedSession.Id, athleteId);
            
            if (_currentSessionDiary != null)
            {
                DiaryValoracionFisica = _currentSessionDiary.ValoracionFisica;
                DiaryValoracionMental = _currentSessionDiary.ValoracionMental;
                DiaryValoracionTecnica = _currentSessionDiary.ValoracionTecnica;
                DiaryNotas = _currentSessionDiary.Notas ?? "";
                IsEditingDiary = false; // Hay datos, mostrar vista de resultados
            }
            else
            {
                // Valores por defecto
                DiaryValoracionFisica = 3;
                DiaryValoracionMental = 3;
                DiaryValoracionTecnica = 3;
                DiaryNotas = "";
                IsEditingDiary = true; // No hay datos, mostrar formulario
            }

            OnPropertyChanged(nameof(HasDiaryData));
            OnPropertyChanged(nameof(ShowDiaryResults));
            OnPropertyChanged(nameof(ShowDiaryForm));

            // Cargar promedios
            await LoadValoracionAveragesAsync(athleteId);

            // Cargar evolución
            await LoadValoracionEvolutionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando diario: {ex}");
        }
    }

    private async Task LoadValoracionAveragesAsync(int athleteId)
    {
        try
        {
            var (fisica, mental, tecnica, count) = await _databaseService.GetValoracionAveragesAsync(athleteId, 30);
            AvgValoracionFisica = fisica;
            AvgValoracionMental = mental;
            AvgValoracionTecnica = tecnica;
            AvgValoracionCount = count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando promedios: {ex}");
        }
    }

    private async Task LoadValoracionEvolutionAsync()
    {
        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null) return;

            var athleteId = profile.ReferenceAthleteId.Value;
            var now = DateTimeOffset.UtcNow;
            long startDate;

            switch (SelectedEvolutionPeriod)
            {
                case 0: // Semana
                    startDate = now.AddDays(-7).ToUnixTimeMilliseconds();
                    break;
                case 1: // Mes
                    startDate = now.AddMonths(-1).ToUnixTimeMilliseconds();
                    break;
                case 2: // Año
                    startDate = now.AddYears(-1).ToUnixTimeMilliseconds();
                    break;
                default: // Todo
                    startDate = 0;
                    break;
            }

            var endDate = now.ToUnixTimeMilliseconds();
            var evolution = await _databaseService.GetValoracionEvolutionAsync(athleteId, startDate, endDate);
            
            ValoracionEvolution = new ObservableCollection<SessionDiary>(evolution);
            
            // Poblar colecciones para el gráfico de evolución
            var fisicaValues = new ObservableCollection<int>();
            var mentalValues = new ObservableCollection<int>();
            var tecnicaValues = new ObservableCollection<int>();
            var labels = new ObservableCollection<string>();
            
            foreach (var diary in evolution.OrderBy(d => d.CreatedAt))
            {
                fisicaValues.Add(diary.ValoracionFisica);
                mentalValues.Add(diary.ValoracionMental);
                tecnicaValues.Add(diary.ValoracionTecnica);
                
                // Formatear fecha como etiqueta
                var date = DateTimeOffset.FromUnixTimeMilliseconds(diary.CreatedAt).LocalDateTime;
                labels.Add(date.ToString("dd/MM"));
            }
            
            EvolutionFisicaValues = fisicaValues;
            EvolutionMentalValues = mentalValues;
            EvolutionTecnicaValues = tecnicaValues;
            EvolutionLabels = labels;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando evolución: {ex}");
        }
    }

    private async Task SaveDiaryAsync()
    {
        if (SelectedSession == null) return;

        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null)
            {
                await Shell.Current.DisplayAlert("Error", "No hay un atleta de referencia configurado en tu perfil.", "OK");
                return;
            }

            var athleteId = profile.ReferenceAthleteId.Value;

            var diary = new SessionDiary
            {
                SessionId = SelectedSession.Id,
                AthleteId = athleteId,
                ValoracionFisica = DiaryValoracionFisica,
                ValoracionMental = DiaryValoracionMental,
                ValoracionTecnica = DiaryValoracionTecnica,
                Notas = DiaryNotas
            };

            await _databaseService.SaveSessionDiaryAsync(diary);
            _currentSessionDiary = diary;
            IsEditingDiary = false;
            OnPropertyChanged(nameof(HasDiaryData));
            OnPropertyChanged(nameof(ShowDiaryResults));
            OnPropertyChanged(nameof(ShowDiaryForm));

            // Recargar promedios y evolución
            await LoadValoracionAveragesAsync(athleteId);
            await LoadValoracionEvolutionAsync();

            await Shell.Current.DisplayAlert("Guardado", "Tu diario de sesión se ha guardado correctamente.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error guardando diario: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo guardar el diario: {ex.Message}", "OK");
        }
    }

    private async Task SaveCalendarDiaryAsync(SessionWithDiary? sessionWithDiary)
    {
        if (sessionWithDiary == null) return;

        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null)
            {
                await Shell.Current.DisplayAlert("Error", "No hay un atleta de referencia configurado en tu perfil.", "OK");
                return;
            }

            var athleteId = profile.ReferenceAthleteId.Value;

            var diary = new SessionDiary
            {
                SessionId = sessionWithDiary.Session.Id,
                AthleteId = athleteId,
                ValoracionFisica = sessionWithDiary.EditValoracionFisica,
                ValoracionMental = sessionWithDiary.EditValoracionMental,
                ValoracionTecnica = sessionWithDiary.EditValoracionTecnica,
                Notas = sessionWithDiary.EditNotas
            };

            await _databaseService.SaveSessionDiaryAsync(diary);
            
            // Actualizar el diario en el objeto
            sessionWithDiary.Diary = diary;

            // Recargar los datos del mes para actualizar indicadores del calendario
            await LoadDiaryEntriesForMonthAsync(SelectedDiaryDate, athleteId);
            
            // Recargar promedios
            await LoadValoracionAveragesAsync(athleteId);
            await LoadValoracionEvolutionAsync();

            await Shell.Current.DisplayAlert("Guardado", "Tu diario de sesión se ha guardado correctamente.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error guardando diario desde calendario: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo guardar el diario: {ex.Message}", "OK");
        }
    }

    private async Task ViewSessionAsPlaylistAsync(SessionWithDiary? sessionWithDiary)
    {
        if (sessionWithDiary == null || !sessionWithDiary.HasVideos) return;

        try
        {
            // Cargar todos los vídeos de la sesión (no solo los 6 de preview)
            var allVideos = await _databaseService.GetVideoClipsBySessionAsync(sessionWithDiary.Session.Id);
            
            if (allVideos.Count == 0)
            {
                await Shell.Current.DisplayAlert("Sesión", "Esta sesión no tiene vídeos.", "OK");
                return;
            }

            // Ordenar por fecha de creación
            var orderedVideos = allVideos.OrderBy(v => v.CreationDate).ToList();

            // Navegar a SinglePlayerPage con la playlist
            var singlePage = App.Current?.Handler?.MauiContext?.Services.GetService<Views.SinglePlayerPage>();
            if (singlePage?.BindingContext is SinglePlayerViewModel singleVm)
            {
                await singleVm.InitializeWithPlaylistAsync(orderedVideos, 0);
                await Shell.Current.Navigation.PushAsync(singlePage);
            }
            else
            {
                // Fallback: reproducir el primer video
                var firstVideo = orderedVideos.First();
                if (!string.IsNullOrEmpty(firstVideo.LocalClipPath))
                {
                    await Shell.Current.GoToAsync($"{nameof(Views.SinglePlayerPage)}?videoPath={Uri.EscapeDataString(firstVideo.LocalClipPath)}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error abriendo playlist de sesión: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo abrir la sesión: {ex.Message}", "OK");
        }
    }

    private async Task ConnectHealthKitAsync()
    {
        if (!_healthKitService.IsAvailable)
        {
            await Shell.Current.DisplayAlert("No disponible", "La app Salud no está disponible en este dispositivo.", "OK");
            return;
        }

        try
        {
            var authorized = await _healthKitService.RequestAuthorizationAsync();
            IsHealthKitAuthorized = authorized;

            if (authorized)
            {
                await Shell.Current.DisplayAlert("Conectado", "Se han conectado correctamente los datos de la app Salud.", "OK");
                // Recargar datos del día seleccionado
                await LoadHealthDataForDateAsync(SelectedDiaryDate);
            }
            else
            {
                await Shell.Current.DisplayAlert("Permiso denegado", 
                    "Para ver tus datos de salud, ve a Ajustes > Privacidad > Salud y permite el acceso a CrownRFEP Reader.", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error conectando HealthKit: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo conectar con la app Salud: {ex.Message}", "OK");
        }
    }

    private async Task LoadHealthDataForDateAsync(DateTime date)
    {
        if (!_healthKitService.IsAvailable || !IsHealthKitAuthorized)
        {
            SelectedDateHealthData = null;
            return;
        }

        try
        {
            IsLoadingHealthData = true;
            SelectedDateHealthData = await _healthKitService.GetHealthDataForDateAsync(date);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando datos de salud: {ex}");
            SelectedDateHealthData = null;
        }
        finally
        {
            IsLoadingHealthData = false;
        }
    }

    #region Bienestar Diario (Entrada Manual)

    private async Task LoadWellnessDataForDateAsync(DateTime date)
    {
        try
        {
            SelectedDateWellness = await _databaseService.GetDailyWellnessAsync(date);
            if (SelectedDateWellness == null)
            {
                // Crear uno vacío para la fecha seleccionada
                SelectedDateWellness = new DailyWellness { Date = date };
            }
            OnPropertyChanged(nameof(HasWellnessData));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando datos de bienestar: {ex}");
            SelectedDateWellness = new DailyWellness { Date = date };
        }
    }

    private async Task ImportHealthDataToWellnessAsync()
    {
        if (!_healthKitService.IsAvailable)
        {
            await Shell.Current.DisplayAlert("No disponible", 
                "La app Salud no está disponible en este dispositivo.", "OK");
            return;
        }

        try
        {
            // Si no está autorizado, solicitar autorización
            if (!IsHealthKitAuthorized)
            {
                var authorized = await _healthKitService.RequestAuthorizationAsync();
                IsHealthKitAuthorized = authorized;
                
                if (!authorized)
                {
                    await Shell.Current.DisplayAlert("Permiso denegado", 
                        "Para importar datos de salud, ve a Ajustes > Privacidad > Salud y permite el acceso a CrownRFEP Reader.", "OK");
                    return;
                }
            }

            // Obtener datos de salud para la fecha seleccionada
            var healthData = await _healthKitService.GetHealthDataForDateAsync(SelectedDiaryDate);
            
            if (healthData == null || !healthData.HasData)
            {
                await Shell.Current.DisplayAlert("Sin datos", 
                    $"No hay datos de salud disponibles para el {SelectedDiaryDate:d MMMM yyyy}.", "OK");
                return;
            }

            // Rellenar los campos del formulario con los datos de HealthKit
            var fieldsUpdated = new List<string>();
            
            if (healthData.SleepHours.HasValue && healthData.SleepHours > 0)
            {
                WellnessSleepHours = Math.Round(healthData.SleepHours.Value, 1);
                fieldsUpdated.Add($"Sueño: {WellnessSleepHours:F1} h");
            }
            
            if (healthData.RestingHeartRate.HasValue)
            {
                WellnessRestingHeartRate = healthData.RestingHeartRate.Value;
                fieldsUpdated.Add($"FC reposo: {WellnessRestingHeartRate} bpm");
            }
            
            if (healthData.HeartRateVariability.HasValue)
            {
                WellnessHRV = (int)Math.Round(healthData.HeartRateVariability.Value);
                fieldsUpdated.Add($"HRV: {WellnessHRV} ms");
            }

            if (fieldsUpdated.Any())
            {
                await Shell.Current.DisplayAlert("Datos importados", 
                    $"Se han importado los siguientes datos de la app Salud:\n\n• {string.Join("\n• ", fieldsUpdated)}", "OK");
            }
            else
            {
                await Shell.Current.DisplayAlert("Sin datos útiles", 
                    "No se encontraron datos relevantes (sueño, FC, HRV) para importar.", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error importando datos de salud: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudieron importar los datos: {ex.Message}", "OK");
        }
    }

    private async Task SaveWellnessAsync()
    {
        try
        {
            var wellness = new DailyWellness
            {
                Date = SelectedDiaryDate,
                SleepHours = WellnessSleepHours,
                SleepQuality = WellnessSleepQuality,
                RecoveryFeeling = WellnessRecoveryFeeling,
                MuscleFatigue = WellnessMuscleFatigue,
                MoodRating = WellnessMoodRating,
                RestingHeartRate = WellnessRestingHeartRate,
                HeartRateVariability = WellnessHRV,
                Notes = WellnessNotes
            };

            SelectedDateWellness = await _databaseService.SaveDailyWellnessAsync(wellness);
            IsEditingWellness = false;
            OnPropertyChanged(nameof(HasWellnessData));

            // Actualizar el calendario para reflejar los nuevos datos de bienestar
            var startOfMonth = new DateTime(SelectedDiaryDate.Year, SelectedDiaryDate.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
            WellnessDataForMonth = await _databaseService.GetDailyWellnessRangeAsync(startOfMonth, endOfMonth);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error guardando bienestar: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo guardar: {ex.Message}", "OK");
        }
    }

    private void CancelEditWellness()
    {
        IsEditingWellness = false;
        // Restaurar valores originales
        if (SelectedDateWellness != null)
        {
            WellnessSleepHours = SelectedDateWellness.SleepHours;
            WellnessSleepQuality = SelectedDateWellness.SleepQuality;
            WellnessRecoveryFeeling = SelectedDateWellness.RecoveryFeeling;
            WellnessMuscleFatigue = SelectedDateWellness.MuscleFatigue;
            WellnessMoodRating = SelectedDateWellness.MoodRating;
            WellnessRestingHeartRate = SelectedDateWellness.RestingHeartRate;
            WellnessHRV = SelectedDateWellness.HeartRateVariability;
            WellnessNotes = SelectedDateWellness.Notes;
        }
        else
        {
            ClearWellnessFields();
        }
    }

    private void ClearWellnessFields()
    {
        WellnessSleepHours = null;
        WellnessSleepQuality = null;
        WellnessRecoveryFeeling = null;
        WellnessMuscleFatigue = null;
        WellnessMoodRating = null;
        WellnessRestingHeartRate = null;
        WellnessHRV = null;
        WellnessNotes = null;
    }

    private async Task ShowPPMInfoAsync()
    {
        await Shell.Current.DisplayAlert(
            "PPM Basal (Frecuencia Cardíaca en Reposo)",
            "La frecuencia cardíaca en reposo (FCR) o PPM basal es el número de latidos por minuto cuando estás completamente relajado.\n\n" +
            "📊 VALORES DE REFERENCIA:\n" +
            "• Atletas de élite: 40-50 bpm\n" +
            "• Muy en forma: 50-60 bpm\n" +
            "• En forma: 60-70 bpm\n" +
            "• Promedio: 70-80 bpm\n" +
            "• Por encima de 80 bpm: mejorable\n\n" +
            "📈 INTERPRETACIÓN:\n" +
            "• Una FCR más baja indica mejor condición cardiovascular\n" +
            "• Si sube 5-10 bpm sobre tu media, puede indicar fatiga, estrés o enfermedad incipiente\n" +
            "• Mídela siempre en las mismas condiciones (al despertar, antes de levantarte)\n\n" +
            "💡 CONSEJO:\n" +
            "Lleva un registro diario para detectar tendencias y ajustar tu entrenamiento.",
            "Entendido");
    }

    private async Task ShowHRVInfoAsync()
    {
        await Shell.Current.DisplayAlert(
            "HRV (Variabilidad de Frecuencia Cardíaca)",
            "La HRV mide la variación en el tiempo entre latidos consecutivos. Se expresa en milisegundos (ms).\n\n" +
            "📊 VALORES DE REFERENCIA:\n" +
            "• Excelente: > 70 ms\n" +
            "• Bueno: 50-70 ms\n" +
            "• Normal: 30-50 ms\n" +
            "• Bajo: < 30 ms\n\n" +
            "📈 INTERPRETACIÓN:\n" +
            "• HRV ALTA = Sistema nervioso equilibrado, buena recuperación, listo para entrenar fuerte\n" +
            "• HRV BAJA = Estrés, fatiga acumulada, necesitas descanso o entrenamiento suave\n\n" +
            "⚠️ IMPORTANTE:\n" +
            "• La HRV es muy individual - compara con TU propia media\n" +
            "• Varía con edad, sexo, genética y estilo de vida\n" +
            "• Una caída del 10-15% respecto a tu media sugiere reducir intensidad\n\n" +
            "💡 CONSEJO:\n" +
            "Mídela cada mañana al despertar con apps como Elite HRV, HRV4Training u Oura Ring.",
            "Entendido");
    }

    #endregion

    private async Task CreateNewSessionAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewSessionName))
            {
                await Shell.Current.DisplayAlert("Error", "Por favor, introduce un nombre para la sesión.", "OK");
                return;
            }

            // Crear la nueva sesión con la fecha seleccionada en el calendario
            var newSession = new Session
            {
                NombreSesion = NewSessionName,
                TipoSesion = NewSessionType,
                Lugar = NewSessionLugar,
                Fecha = new DateTimeOffset(SelectedDiaryDate.Date.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute)).ToUnixTimeSeconds(),
                IsMerged = 0
            };

            await _databaseService.SaveSessionAsync(newSession);

            // Cerrar el formulario
            IsAddingNewSession = false;

            // Recargar los datos del mes
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId != null)
            {
                await LoadDiaryEntriesForMonthAsync(SelectedDiaryDate, profile.ReferenceAthleteId.Value);
                await LoadDiaryForDateAsync(SelectedDiaryDate);
            }

            await Shell.Current.DisplayAlert("Sesión creada", $"La sesión '{NewSessionName}' se ha creado correctamente. Ahora puedes añadir tus valoraciones y notas.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creando sesión: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo crear la sesión: {ex.Message}", "OK");
        }
    }

    private async Task OpenCameraRecordingAsync()
    {
        try
        {
            // Cerrar el formulario de nueva sesión si está abierto
            IsAddingNewSession = false;

            // Navegar a la página de cámara con los parámetros de la sesión
            var parameters = new Dictionary<string, object>
            {
                { "SessionName", NewSessionName ?? $"Sesión {SelectedDiaryDate:dd/MM/yyyy}" },
                { "SessionType", NewSessionType ?? "Entrenamiento" },
                { "Place", NewSessionLugar ?? "" },
                { "Date", SelectedDiaryDate }
            };

            await Shell.Current.GoToAsync(nameof(Views.CameraPage), parameters);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error abriendo cámara: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo abrir la cámara: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Crea una nueva sesión desde el popup de la barra lateral y abre la cámara para grabar
    /// </summary>
    private async Task CreateSessionAndRecordAsync()
    {
        try
        {
            // Crear la sesión sin requerir deportista
            var sessionName = string.IsNullOrWhiteSpace(NewSessionName) 
                ? $"Sesión {DateTime.Now:dd/MM/yyyy HH:mm}" 
                : NewSessionName;

            // Intentar obtener deportista de referencia o el primero disponible (opcional)
            string participantes = "";
            try
            {
                var profile = await _databaseService.GetUserProfileAsync();
                int? athleteId = profile?.ReferenceAthleteId;
                
                if (athleteId == null)
                {
                    var athletes = await _databaseService.GetAllAthletesAsync();
                    if (athletes != null && athletes.Count > 0)
                    {
                        athleteId = athletes[0].Id;
                    }
                }
                
                if (athleteId.HasValue)
                {
                    participantes = athleteId.Value.ToString();
                }
            }
            catch
            {
                // Ignorar errores al obtener deportista - la sesión se creará sin él
            }

            var session = new Session
            {
                NombreSesion = sessionName,
                TipoSesion = NewSessionType ?? "Entrenamiento",
                Lugar = string.IsNullOrWhiteSpace(NewSessionLugar) ? null : NewSessionLugar,
                Fecha = DateTimeOffset.Now.ToUnixTimeSeconds(),
                Participantes = participantes
            };

            await _databaseService.SaveSessionAsync(session);
            
            // Cerrar el popup
            ShowNewSessionSidebarPopup = false;

            // Recargar las sesiones para mostrar la nueva (en background)
            _ = LoadDataAsync();

            // Navegar a la cámara con el SessionId
            var parameters = new Dictionary<string, object>
            {
                { "SessionId", session.Id },
                { "SessionName", session.NombreSesion ?? session.DisplayName },
                { "SessionType", session.TipoSesion ?? "Entrenamiento" },
                { "Place", session.Lugar ?? "" },
                { "Date", session.FechaDateTime }
            };

            await Shell.Current.GoToAsync(nameof(Views.CameraPage), parameters);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creando sesión y grabando: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo crear la sesión: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Abre la cámara para grabar videos en la sesión actualmente seleccionada
    /// </summary>
    private async Task RecordForSelectedSessionAsync()
    {
        try
        {
            if (SelectedSession == null)
            {
                await Shell.Current.DisplayAlert("Error", "No hay ninguna sesión seleccionada", "OK");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "SessionId", SelectedSession.Id },
                { "SessionName", SelectedSession.NombreSesion ?? SelectedSession.DisplayName },
                { "SessionType", SelectedSession.TipoSesion ?? "Entrenamiento" },
                { "Place", SelectedSession.Lugar ?? "" },
                { "Date", SelectedSession.FechaDateTime }
            };

            await Shell.Current.GoToAsync(nameof(Views.CameraPage), parameters);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error abriendo cámara para sesión: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo abrir la cámara: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Exporta la sesión seleccionada a un archivo .crown
    /// </summary>
    private async Task ExportSelectedSessionAsync()
    {
        try
        {
            if (SelectedSession == null)
            {
                await Shell.Current.DisplayAlert("Error", "No hay ninguna sesión seleccionada", "OK");
                return;
            }

            // Mostrar indicador de progreso
            var progress = new Progress<ImportProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ImportProgressText = p.Message;
                    ImportProgressValue = p.Percentage;
                });
            });

            IsImporting = true;
            ImportProgressText = "Preparando exportación...";
            ImportProgressValue = 0;

            var crownFileService = new CrownFileService(_databaseService);
            var result = await crownFileService.ExportSessionAsync(SelectedSession.Id, progress);

            IsImporting = false;

            if (result.Success && !string.IsNullOrEmpty(result.FilePath))
            {
#if IOS
                // Compartir el archivo usando Share API
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = $"Exportar {result.SessionName}",
                    File = new ShareFile(result.FilePath)
                });
#else
                await Shell.Current.DisplayAlert("Exportación completada", 
                    $"Sesión '{result.SessionName}' exportada correctamente.\n{result.VideosExported} videos incluidos.\n\nArchivo: {result.FilePath}", "OK");
#endif
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", result.ErrorMessage ?? "Error desconocido al exportar", "OK");
            }
        }
        catch (Exception ex)
        {
            IsImporting = false;
            System.Diagnostics.Debug.WriteLine($"Error exportando sesión: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo exportar la sesión: {ex.Message}", "OK");
        }
    }

    #endregion

    #region Diary View Methods (Calendar)

    private async Task LoadDiaryViewDataAsync()
    {
        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null) return;

            // Cargar entradas del mes actual
            await LoadDiaryEntriesForMonthAsync(SelectedDiaryDate, profile.ReferenceAthleteId.Value);
            
            // Cargar diario del día seleccionado
            await LoadDiaryForDateAsync(SelectedDiaryDate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando vista diario: {ex}");
        }
    }

    private async Task LoadDiaryEntriesForMonthAsync(DateTime month, int athleteId)
    {
        try
        {
            var startOfMonth = new DateTime(month.Year, month.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            // Cargar sesiones del mes para mostrar iconos de video en el calendario
            var startUnix = new DateTimeOffset(startOfMonth).ToUnixTimeSeconds();
            var endUnix = new DateTimeOffset(endOfMonth.AddDays(1)).ToUnixTimeSeconds();
            var sessions = await _databaseService.GetAllSessionsAsync();
            SessionsForMonth = sessions.Where(s => s.Fecha >= startUnix && s.Fecha < endUnix).ToList();

            // Cargar diarios de las sesiones del mes (filtrados por SessionId)
            var sessionIds = SessionsForMonth.Select(s => s.Id).ToHashSet();
            var allDiaries = await _databaseService.GetAllSessionDiariesForAthleteAsync(athleteId);
            DiaryEntriesForMonth = allDiaries.Where(d => sessionIds.Contains(d.SessionId)).ToList();

            // Cargar datos de bienestar del mes
            WellnessDataForMonth = await _databaseService.GetDailyWellnessRangeAsync(startOfMonth, endOfMonth);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando entradas del mes: {ex}");
            DiaryEntriesForMonth = new List<SessionDiary>();
            SessionsForMonth = new List<Session>();
            WellnessDataForMonth = new List<DailyWellness>();
        }
    }

    private async Task LoadDiaryForDateAsync(DateTime date)
    {
        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId == null)
            {
                SelectedDateDiary = null;
                SelectedDateSessionsWithDiary.Clear();
                OnPropertyChanged(nameof(HasSelectedDateSessions));
                return;
            }

            var athleteId = profile.ReferenceAthleteId.Value;

            // Buscar un diario para esa fecha específica (mantener compatibilidad)
            var entries = await _databaseService.GetSessionDiariesForPeriodAsync(
                athleteId, date.Date, date.Date.AddDays(1).AddSeconds(-1));
            
            SelectedDateDiary = entries.FirstOrDefault();

            // Cargar todas las sesiones del día con sus diarios
            var sessionsForDate = SessionsForMonth
                .Where(s => s.FechaDateTime.Date == date.Date)
                .ToList();

            SelectedDateSessionsWithDiary.Clear();

            foreach (var session in sessionsForDate)
            {
                // Buscar el diario correspondiente a esta sesión
                var diary = DiaryEntriesForMonth.FirstOrDefault(d => d.SessionId == session.Id);
                
                // Cargar los vídeos de la sesión (máximo 6 para la mini galería)
                var allVideos = await _databaseService.GetVideoClipsBySessionAsync(session.Id);
                var previewVideos = allVideos.Take(6).ToList();
                
                var sessionWithDiary = new SessionWithDiary
                {
                    Session = session,
                    Diary = diary,
                    VideoCount = allVideos.Count
                };
                
                foreach (var video in previewVideos)
                    sessionWithDiary.Videos.Add(video);

                SelectedDateSessionsWithDiary.Add(sessionWithDiary);
            }

            OnPropertyChanged(nameof(HasSelectedDateSessions));
            
            // Cargar datos de salud si HealthKit está autorizado
            await LoadHealthDataForDateAsync(date);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando diario del día: {ex}");
            SelectedDateDiary = null;
            SelectedDateSessionsWithDiary.Clear();
            OnPropertyChanged(nameof(HasSelectedDateSessions));
        }
    }

    private void OpenSmartFolderSidebarPopup()
    {
        NewSmartFolderName = "";
        NewSmartFolderMatchMode = "All";

        foreach (var existing in NewSmartFolderCriteria)
            existing.PropertyChanged -= OnNewSmartFolderCriterionChanged;

        NewSmartFolderCriteria.Clear();
        AddSmartFolderCriterion();
        RecomputeNewSmartFolderLiveMatchCount();

        ShowSmartFolderSidebarPopup = true;
    }

    private void CloseSmartFolderSidebarPopup()
    {
        ShowSmartFolderSidebarPopup = false;
    }

    private void AddSmartFolderCriterion()
    {
        var criterion = new SmartFolderCriterion();
        criterion.PropertyChanged += OnNewSmartFolderCriterionChanged;
        NewSmartFolderCriteria.Add(criterion);
        RecomputeNewSmartFolderLiveMatchCount();
    }

    private void RemoveSmartFolderCriterion(SmartFolderCriterion? criterion)
    {
        if (criterion == null)
            return;

        criterion.PropertyChanged -= OnNewSmartFolderCriterionChanged;
        NewSmartFolderCriteria.Remove(criterion);
        RecomputeNewSmartFolderLiveMatchCount();
    }

    private void OnNewSmartFolderCriterionChanged(object? sender, PropertyChangedEventArgs e)
    {
        RecomputeNewSmartFolderLiveMatchCount();
    }

    private async Task SelectCriterionFieldAsync(SmartFolderCriterion? criterion)
    {
        if (criterion == null)
            return;

        try
        {
            var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (page == null)
                return;

            var options = SmartFolderFieldOptions.ToArray();
            var result = await page.DisplayActionSheet("Seleccionar campo", "Cancelar", null, options);
            if (!string.IsNullOrEmpty(result) && result != "Cancelar")
            {
                criterion.Field = result;
            }
        }
        catch { }
    }

    private async Task SelectCriterionOperatorAsync(SmartFolderCriterion? criterion)
    {
        if (criterion == null)
            return;

        try
        {
            var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (page == null)
                return;

            var options = criterion.AvailableOperators.ToArray();
            var result = await page.DisplayActionSheet("Seleccionar operador", "Cancelar", null, options);
            if (!string.IsNullOrEmpty(result) && result != "Cancelar")
            {
                criterion.Operator = result;
            }
        }
        catch { }
    }

    private void CreateSmartFolder()
    {
        var name = (NewSmartFolderName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var definition = new SmartFolderDefinition
        {
            Name = name,
            MatchMode = NewSmartFolderMatchMode == "Any" ? "Any" : "All",
            MatchingVideoCount = NewSmartFolderLiveMatchCount,
            Criteria = NewSmartFolderCriteria
                .Select(c => new SmartFolderCriterion
                {
                    Field = c.Field,
                    Operator = c.Operator,
                    Value = c.Value,
                    Value2 = c.Value2,
                })
                .ToList(),
        };

        SmartFolders.Add(definition);

        // Asegurar que los contadores se ajustan al estado actual de la caché.
        UpdateSmartFolderVideoCounts();
        SaveSmartFoldersToPreferences();
        CloseSmartFolderSidebarPopup();
    }

    private void ToggleSmartFoldersExpansion()
    {
        IsSmartFoldersExpanded = !IsSmartFoldersExpanded;
    }

    private void ToggleSessionsExpansion()
    {
        IsSessionsExpanded = !IsSessionsExpanded;
    }

    private async Task SelectSmartFolderAsync(SmartFolderDefinition? definition)
    {
        if (definition == null)
            return;

        SelectedSession = null;
        IsAllGallerySelected = true;

        if (_allVideosCache == null)
            await LoadAllVideosAsync();

        await ApplySmartFolderFilterAsync(definition);
    }

    private async Task ApplySmartFolderFilterAsync(SmartFolderDefinition definition)
    {
        var version = Interlocked.Increment(ref _filtersVersion);

        var source = _allVideosCache;
        if (source == null)
            return;

        // Guardar la carpeta inteligente activa para que ApplyFiltersAsync la use
        _activeSmartFolder = definition;

        // Evitar que los filtros manuales se mezclen con el filtro de carpeta.
        // Usamos skipApplyFilters=true para evitar que se sobrescriba el filtro de la carpeta inteligente.
        ClearFilters(skipApplyFilters: true);

        var criteria = definition.Criteria ?? new List<SmartFolderCriterion>();
        bool matchAll = !string.Equals(definition.MatchMode, "Any", StringComparison.OrdinalIgnoreCase);

        List<VideoClip> filtered;
        if (criteria.Count == 0)
        {
            filtered = source.ToList();
        }
        else
        {
            filtered = source
                .Where(v =>
                {
                    var matches = criteria.Select(c => MatchesCriterion(v, c)).ToList();
                    return matchAll ? matches.All(m => m) : matches.Any(m => m);
                })
                .ToList();
        }

        _filteredVideosCache = filtered;
        _smartFolderFilteredVideosCache = filtered; // Guardar cache de videos filtrados por carpeta inteligente

        if (version != Volatile.Read(ref _filtersVersion))
            return;

        _currentPage = 0;
        var firstBatch = filtered.Take(PageSize).ToList();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, CancellationToken.None);
        });
        _currentPage = 1;
        HasMoreVideos = filtered.Count > PageSize;

        var snapshot = await Task.Run(() => BuildGalleryStatsSnapshot(filtered));

        if (version != Volatile.Read(ref _filtersVersion))
            return;
        await ApplyGalleryStatsSnapshotAsync(snapshot, CancellationToken.None);

        // Actualizar Stats con los datos filtrados de la carpeta inteligente
        var uniqueSessionIds = filtered.Select(v => v.SessionId).Distinct().Count();
        var uniqueAthleteIds = filtered.Where(v => v.AtletaId != 0).Select(v => v.AtletaId).Distinct().Count();
        var totalDuration = filtered.Sum(v => v.ClipDuration);
        
        Stats = new DashboardStats
        {
            TotalSessions = uniqueSessionIds,
            TotalVideos = filtered.Count,
            TotalAthletes = uniqueAthleteIds,
            TotalDurationSeconds = totalDuration
        };

        OnPropertyChanged(nameof(TotalFilteredVideoCount));
        OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
        OnPropertyChanged(nameof(VideoCountDisplayText));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
    }

    private void LoadSmartFoldersFromPreferences()
    {
        try
        {
            var json = Preferences.Get(SmartFoldersPreferencesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var parsed = JsonSerializer.Deserialize<List<SmartFolderDefinition>>(json);
            if (parsed == null)
                return;

            SmartFolders.Clear();
            foreach (var def in parsed)
                SmartFolders.Add(def);
        }
        catch
        {
            // Si falla la deserialización, ignorar y seguir sin carpetas.
        }
    }

    /// <summary>
    /// Updates the MatchingVideoCount for each SmartFolder based on the current _allVideosCache.
    /// </summary>
    private void UpdateSmartFolderVideoCounts()
    {
        var source = _allVideosCache;
        if (source == null)
            return;

        foreach (var folder in SmartFolders)
        {
            var criteria = folder.Criteria ?? new List<SmartFolderCriterion>();
            bool matchAll = !string.Equals(folder.MatchMode, "Any", StringComparison.OrdinalIgnoreCase);

            int count;
            if (criteria.Count == 0)
            {
                count = source.Count;
            }
            else
            {
                count = source.Count(v =>
                {
                    var matches = criteria.Select(c => MatchesCriterion(v, c)).ToList();
                    return matchAll ? matches.All(m => m) : matches.Any(m => m);
                });
            }

            folder.MatchingVideoCount = count;
        }
    }

    private void SaveSmartFoldersToPreferences()
    {
        try
        {
            var json = JsonSerializer.Serialize(SmartFolders.ToList());
            Preferences.Set(SmartFoldersPreferencesKey, json);
        }
        catch
        {
            // Ignorar errores de persistencia.
        }
    }

    #region Smart Folder Context Menu

    private static readonly string[] SmartFolderIconOptions = new[]
    {
        "folder", "folder.fill", "star", "star.fill", "heart", "heart.fill",
        "flag", "flag.fill", "bookmark", "bookmark.fill", "tag", "tag.fill",
        "bolt", "bolt.fill", "flame", "flame.fill", "trophy", "trophy.fill",
        "sportscourt", "figure.run"
    };

    private static readonly (string Name, string HexColor)[] SmartFolderColorOptions = new[]
    {
        ("Gris", "#FF888888"),
        ("Rojo", "#FFFF453A"),
        ("Naranja", "#FFFF9F0A"),
        ("Amarillo", "#FFFFD60A"),
        ("Verde", "#FF30D158"),
        ("Menta", "#FF63E6E2"),
        ("Cyan", "#FF64D2FF"),
        ("Azul", "#FF0A84FF"),
        ("Morado", "#FFBF5AF2"),
        ("Rosa", "#FFFF375F")
    };

    private async Task ShowSmartFolderContextMenuAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.MainPage;
        if (page == null) return;

        var result = await page.DisplayActionSheet(
            folder.Name,
            "Cancelar",
            null,
            "Renombrar",
            "Cambiar icono",
            "Cambiar color",
            "Eliminar");

        switch (result)
        {
            case "Renombrar":
                await RenameSmartFolderAsync(folder);
                break;
            case "Cambiar icono":
                await ChangeSmartFolderIconAsync(folder);
                break;
            case "Cambiar color":
                await ChangeSmartFolderColorAsync(folder);
                break;
            case "Eliminar":
                await DeleteSmartFolderAsync(folder);
                break;
        }
    }

    private async Task RenameSmartFolderAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.MainPage;
        if (page == null) return;

        var newName = await page.DisplayPromptAsync(
            "Renombrar carpeta",
            "Introduce el nuevo nombre:",
            "Aceptar",
            "Cancelar",
            folder.Name,
            50,
            Keyboard.Text,
            folder.Name);

        if (!string.IsNullOrWhiteSpace(newName) && newName != folder.Name)
        {
            folder.Name = newName;
            SaveSmartFoldersToPreferences();
        }
    }

    private async Task DeleteSmartFolderAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.MainPage;
        if (page == null) return;

        var confirm = await page.DisplayAlert(
            "Eliminar carpeta",
            $"¿Seguro que quieres eliminar la carpeta \"{folder.Name}\"?",
            "Eliminar",
            "Cancelar");

        if (confirm)
        {
            SmartFolders.Remove(folder);
            SaveSmartFoldersToPreferences();
        }
    }

    private async Task ChangeSmartFolderIconAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.MainPage;
        if (page == null) return;

        var result = await page.DisplayActionSheet(
            "Seleccionar icono",
            "Cancelar",
            null,
            SmartFolderIconOptions);

        if (!string.IsNullOrEmpty(result) && result != "Cancelar")
        {
            folder.Icon = result;
            SaveSmartFoldersToPreferences();
        }
    }

    private async Task ChangeSmartFolderColorAsync(SmartFolderDefinition? folder)
    {
        if (folder == null) return;

        var page = Application.Current?.MainPage;
        if (page == null) return;

        var colorNames = SmartFolderColorOptions.Select(c => c.Name).ToArray();
        var result = await page.DisplayActionSheet(
            "Seleccionar color",
            "Cancelar",
            null,
            colorNames);

        if (!string.IsNullOrEmpty(result) && result != "Cancelar")
        {
            var selected = SmartFolderColorOptions.FirstOrDefault(c => c.Name == result);
            if (!string.IsNullOrEmpty(selected.HexColor))
            {
                folder.IconColor = selected.HexColor;
                SaveSmartFoldersToPreferences();
            }
        }
    }

    private void SetSmartFolderIcon(object? parameter)
    {
        if (parameter is ValueTuple<SmartFolderDefinition, string> tuple)
        {
            var (folder, icon) = tuple;
            folder.Icon = icon;
            SaveSmartFoldersToPreferences();
        }
    }

    private void SetSmartFolderColor(object? parameter)
    {
        if (parameter is ValueTuple<SmartFolderDefinition, string> tuple)
        {
            var (folder, color) = tuple;
            folder.IconColor = color;
            SaveSmartFoldersToPreferences();
        }
    }

    #endregion

    #region Session Context Menu

    private const string SessionCustomizationsPreferencesKey = "SessionCustomizations";

    private Dictionary<int, (string Icon, string Color)> _sessionCustomizations = new();

    private void LoadSessionCustomizationsFromPreferences()
    {
        try
        {
            var json = Preferences.Get(SessionCustomizationsPreferencesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var parsed = JsonSerializer.Deserialize<Dictionary<int, SessionCustomization>>(json);
            if (parsed == null)
                return;

            _sessionCustomizations.Clear();
            foreach (var kvp in parsed)
                _sessionCustomizations[kvp.Key] = (kvp.Value.Icon, kvp.Value.Color);
        }
        catch
        {
            // Si falla la deserialización, ignorar.
        }
    }

    private void SaveSessionCustomizationsToPreferences()
    {
        try
        {
            var toSerialize = _sessionCustomizations.ToDictionary(
                kvp => kvp.Key,
                kvp => new SessionCustomization { Icon = kvp.Value.Icon, Color = kvp.Value.Color });
            var json = JsonSerializer.Serialize(toSerialize);
            Preferences.Set(SessionCustomizationsPreferencesKey, json);
        }
        catch
        {
            // Ignorar errores de persistencia.
        }
    }

    private void ApplySessionCustomization(Session session)
    {
        if (_sessionCustomizations.TryGetValue(session.Id, out var customization))
        {
            session.Icon = customization.Icon;
            session.IconColor = customization.Color;
        }
    }

    private void OpenSessionEditPopup(SessionRow? row)
    {
        if (row?.Session == null) return;

        _editingSessionRow = row;
        var session = row.Session;

        // Cargar valores actuales
        SessionEditName = session.NombreSesion ?? session.DisplayName;
        SessionEditLugar = session.Lugar ?? string.Empty;
        SessionEditTipoSesion = session.TipoSesion ?? string.Empty;

        ShowSessionEditPopup = true;
    }

    private async Task ApplySessionEditAsync()
    {
        if (_editingSessionRow?.Session == null)
        {
            ShowSessionEditPopup = false;
            return;
        }

        var session = _editingSessionRow.Session;
        var hasChanges = false;

        // Aplicar cambios
        var newName = NormalizeSpaces(SessionEditName);
        if (!string.IsNullOrEmpty(newName) && newName != session.NombreSesion)
        {
            session.NombreSesion = newName;
            hasChanges = true;
        }

        var newLugar = NormalizeSpaces(SessionEditLugar);
        if (newLugar != session.Lugar)
        {
            session.Lugar = newLugar;
            hasChanges = true;
        }

        var newTipo = NormalizeSpaces(SessionEditTipoSesion);
        if (newTipo != session.TipoSesion)
        {
            session.TipoSesion = newTipo;
            hasChanges = true;
        }

        if (hasChanges)
        {
            await _databaseService.SaveSessionAsync(session);
            SyncVisibleSessionRows();
        }

        _editingSessionRow = null;
        ShowSessionEditPopup = false;
    }

    private async Task RenameSessionAsync(SessionRow? row)
    {
        if (row?.Session == null) return;

        var page = Application.Current?.MainPage;
        if (page == null) return;

        var session = row.Session;
        var currentName = session.NombreSesion ?? session.DisplayName;

        var newName = await page.DisplayPromptAsync(
            "Renombrar sesión",
            "Introduce el nuevo nombre:",
            "Aceptar",
            "Cancelar",
            currentName,
            100,
            Keyboard.Text,
            currentName);

        if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
        {
            session.NombreSesion = newName;
            await _databaseService.SaveSessionAsync(session);
            SyncVisibleSessionRows();
        }
    }

    private async Task DeleteSessionAsync(SessionRow? row)
    {
        if (row?.Session == null) return;

        var page = Application.Current?.MainPage;
        if (page == null) return;

        var session = row.Session;
        var confirm = await page.DisplayAlert(
            "Eliminar sesión",
            $"¿Seguro que quieres mover a la papelera la sesión \"{session.DisplayName}\"?\n\nPodrás restaurarla durante 30 días.",
            "Eliminar",
            "Cancelar");

        if (confirm)
        {
            await _trashService.MoveSessionToTrashAsync(session.Id);
            RecentSessions.Remove(session);
            _sessionCustomizations.Remove(session.Id);
            SaveSessionCustomizationsToPreferences();
            SyncVisibleSessionRows();

            if (SelectedSession?.Id == session.Id)
            {
                SelectedSession = null;
            }
        }
    }

    private void SetSessionIcon(object? parameter)
    {
        if (parameter is ValueTuple<SessionRow, string> tuple)
        {
            var (row, icon) = tuple;
            if (row.Session != null)
            {
                row.Session.Icon = icon;
                _sessionCustomizations[row.Session.Id] = (icon, row.Session.IconColor);
                SaveSessionCustomizationsToPreferences();
            }
        }
    }

    private void SetSessionColor(object? parameter)
    {
        if (parameter is ValueTuple<SessionRow, string> tuple)
        {
            var (row, color) = tuple;
            if (row.Session != null)
            {
                row.Session.IconColor = color;
                _sessionCustomizations[row.Session.Id] = (row.Session.Icon, color);
                SaveSessionCustomizationsToPreferences();
            }
        }
    }

    private record SessionCustomization
    {
        public string Icon { get; init; } = "oar.2.crossed";
        public string Color { get; init; } = "#FF6DDDFF";
    }

    #endregion

    #region Icon/Color Picker Popup

    private void OpenIconColorPickerForSmartFolder(SmartFolderDefinition? folder)
    {
        if (folder == null) return;
        IconColorPickerTargetSession = null;
        IconColorPickerTargetSmartFolder = folder;
        OnPropertyChanged(nameof(IsPickerForSmartFolder));
        OnPropertyChanged(nameof(IsPickerForSession));
        OnPropertyChanged(nameof(IconColorPickerTitle));
        UpdateIconPickerSelection();
        ShowIconColorPickerPopup = true;
    }

    private void OpenIconColorPickerForSession(SessionRow? row)
    {
        if (row?.Session == null) return;
        IconColorPickerTargetSmartFolder = null;
        IconColorPickerTargetSession = row;
        OnPropertyChanged(nameof(IsPickerForSmartFolder));
        OnPropertyChanged(nameof(IsPickerForSession));
        OnPropertyChanged(nameof(IconColorPickerTitle));
        UpdateIconPickerSelection();
        ShowIconColorPickerPopup = true;
    }

    private void CloseIconColorPicker()
    {
        ShowIconColorPickerPopup = false;
        IconColorPickerTargetSmartFolder = null;
        IconColorPickerTargetSession = null;
    }

    private void UpdateIconPickerSelection()
    {
        var selectedIcon = SelectedPickerIcon;
        foreach (var item in AvailableIcons)
        {
            item.IsSelected = string.Equals(item.Name, selectedIcon, StringComparison.OrdinalIgnoreCase);
        }
        OnPropertyChanged(nameof(SelectedPickerIcon));
        OnPropertyChanged(nameof(SelectedPickerColor));
    }

    private void SelectPickerIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return;

        if (IconColorPickerTargetSmartFolder is { } folder)
        {
            folder.Icon = icon;
            SaveSmartFoldersToPreferences();
        }
        else if (IconColorPickerTargetSession?.Session is { } session)
        {
            session.Icon = icon;
            _sessionCustomizations[session.Id] = (icon, session.IconColor);
            SaveSessionCustomizationsToPreferences();
        }
        UpdateIconPickerSelection();
    }

    private void SelectPickerColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;

        if (IconColorPickerTargetSmartFolder is { } folder)
        {
            folder.IconColor = color;
            SaveSmartFoldersToPreferences();
        }
        else if (IconColorPickerTargetSession?.Session is { } session)
        {
            session.IconColor = color;
            _sessionCustomizations[session.Id] = (session.Icon, color);
            SaveSessionCustomizationsToPreferences();
        }
        OnPropertyChanged(nameof(SelectedPickerColor));
    }

    #endregion

    private void RecomputeNewSmartFolderLiveMatchCount()
    {
        try
        {
            var source = _allVideosCache;
            if (source == null || source.Count == 0)
            {
                NewSmartFolderLiveMatchCount = 0;
                return;
            }

            var criteriaSnapshot = NewSmartFolderCriteria.ToList();
            if (criteriaSnapshot.Count == 0)
            {
                NewSmartFolderLiveMatchCount = source.Count;
                return;
            }

            bool matchAll = NewSmartFolderMatchMode != "Any";

            int count = 0;
            foreach (var video in source)
            {
                var matches = criteriaSnapshot.Select(c => MatchesCriterion(video, c)).ToList();
                var ok = matchAll ? matches.All(m => m) : matches.Any(m => m);
                if (ok)
                    count++;
            }

            NewSmartFolderLiveMatchCount = count;
        }
        catch
        {
            NewSmartFolderLiveMatchCount = 0;
        }
    }

    private static bool MatchesCriterion(VideoClip video, SmartFolderCriterion c)
    {
        var field = (c.Field ?? string.Empty).Trim();
        var op = (c.Operator ?? string.Empty).Trim();
        var value = (c.Value ?? string.Empty).Trim();
        var value2 = (c.Value2 ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(field))
            return true;

        if (field == "Fecha")
        {
            var dt = video.CreationDateTime.Date;

            static bool TryParseDate(string s, out DateTime d)
            {
                if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("es-ES"), DateTimeStyles.AssumeLocal, out d))
                {
                    d = d.Date;
                    return true;
                }
                return false;
            }

            if (op == "Entre")
            {
                if (!TryParseDate(value, out var from) || !TryParseDate(value2, out var to))
                    return false;
                if (to < from)
                    (from, to) = (to, from);
                return dt >= from && dt <= to;
            }

            if (op == "Hasta")
            {
                if (!TryParseDate(value, out var to))
                    return false;
                return dt <= to;
            }

            // Desde (default)
            if (!TryParseDate(value, out var fromDate))
                return false;
            return dt >= fromDate;
        }

        if (field == "Sección")
        {
            if (!int.TryParse(value, out var section))
                return false;
            return video.Section == section;
        }

        string haystack = field switch
        {
            "Lugar" => video.Session?.Lugar ?? string.Empty,
            "Deportista" => video.Atleta?.NombreCompleto ?? string.Empty,
            "Tag" => string.Join(" ", (video.Tags ?? new List<Tag>()).Select(t => t.NombreTag ?? string.Empty))
                + " " + string.Join(" ", (video.EventTags ?? new List<Tag>()).Select(t => t.NombreTag ?? string.Empty)),
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (op == "Es")
            return string.Equals(haystack.Trim(), value, StringComparison.OrdinalIgnoreCase);

        // Contiene (default)
        return haystack.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
