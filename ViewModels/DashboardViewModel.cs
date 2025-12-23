using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para la página principal / Dashboard
/// </summary>
public class DashboardViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private readonly CrownFileService _crownFileService;
    private readonly StatisticsService _statisticsService;
    private readonly ThumbnailService _thumbnailService;

    private DashboardStats? _stats;
    private Session? _selectedSession;
    private bool _isAllGallerySelected;
    private bool _isVideoLessonsSelected;
    private bool _isSessionsListExpanded = true;
    private bool _isLoadingSelectedSessionVideos;

    private GridLength _rightPanelWidth = new(1.2, GridUnitType.Star);
    private GridLength _rightSplitterWidth = new(8);
    private string _importProgressText = "";
    private int _importProgressValue;
    private bool _isImporting;

    // Pestañas columna derecha
    private bool _isStatsTabSelected = true;
    private bool _isCrudTechTabSelected;
    private bool _isDiaryTabSelected;

    // Diario de sesión
    private SessionDiary? _currentSessionDiary;
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

    // Modo de análisis: único o paralelo (paralelo por defecto)
    private bool _isSingleVideoMode = false;
    
    // Orientación análisis paralelo
    private bool _isHorizontalOrientation = false;

    // Vídeos para análisis paralelo
    private VideoClip? _parallelVideo1;
    private VideoClip? _parallelVideo2;
    private bool _isPreviewMode;

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
    private readonly HashSet<int> _batchEditSelectedTagIds = new();

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
        set => SetProperty(ref _stats, value);
    }

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
                }
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(HasSpecificSessionSelected));
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
                    ClearSectionTimes();
                }
                UpdateRightPanelLayout();
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(VideoCountDisplayText));
                OnPropertyChanged(nameof(ShowSectionTimesTable));
                OnPropertyChanged(nameof(HasSpecificSessionSelected));
            }
        }
    }

    public GridLength RightPanelWidth
    {
        get => _rightPanelWidth;
        set => SetProperty(ref _rightPanelWidth, value);
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
            RightSplitterWidth = new GridLength(0);
            RightPanelWidth = new GridLength(0);
        }
        else
        {
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
                SyncVisibleRecentSessions();
            }
        }
    }

    public string SelectedSessionTitle => IsVideoLessonsSelected
        ? "Videolecciones"
        : (IsAllGallerySelected
            ? "Galería General"
            : (SelectedSession?.DisplayName ?? "Selecciona una sesión"));

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

    // Modo único (un solo video) vs paralelo (dos videos)
    public bool IsSingleVideoMode
    {
        get => _isSingleVideoMode;
        set
        {
            if (SetProperty(ref _isSingleVideoMode, value))
            {
                OnPropertyChanged(nameof(IsParallelVideoMode));
                // Limpiar segundo slot si cambiamos a modo único
                if (value && _parallelVideo2 != null)
                {
                    ParallelVideo2 = null;
                }
            }
        }
    }

    public bool IsParallelVideoMode
    {
        get => !_isSingleVideoMode;
        set
        {
            if (value != !_isSingleVideoMode)
            {
                IsSingleVideoMode = !value;
            }
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
                OnPropertyChanged(nameof(HasParallelVideo1));
                // Activar preview automáticamente al soltar un video
                if (value != null)
                {
                    IsPreviewMode = true;
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
                OnPropertyChanged(nameof(HasParallelVideo2));
                // Activar preview automáticamente al soltar un video
                if (value != null)
                {
                    IsPreviewMode = true;
                }
            }
        }
    }

    public bool HasParallelVideo1 => _parallelVideo1 != null;
    public bool HasParallelVideo2 => _parallelVideo2 != null;

    /// <summary>
    /// Limpia los videos de preview (recuadros de arrastrar)
    /// </summary>
    public void ClearPreviewVideos()
    {
        ParallelVideo1 = null;
        ParallelVideo2 = null;
        IsPreviewMode = false;
    }

    public bool IsPreviewMode
    {
        get => _isPreviewMode;
        set => SetProperty(ref _isPreviewMode, value);
    }

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
    public ObservableCollection<VideoClip> SelectedSessionVideos { get; } = new();
    public ObservableCollection<VideoLesson> VideoLessons { get; } = new();

    private void SyncVisibleRecentSessions()
    {
        VisibleRecentSessions.Clear();

        if (!IsSessionsListExpanded)
            return;

        foreach (var session in RecentSessions)
            VisibleRecentSessions.Add(session);
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
            var total = TotalAvailableVideoCount;
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
    public ICommand RefreshCommand { get; }
    public ICommand ViewSessionCommand { get; }
    public ICommand ViewAllSessionsCommand { get; }
    public ICommand ViewAthletesCommand { get; }
    public ICommand PlaySelectedVideoCommand { get; }
    public ICommand DeleteSelectedSessionCommand { get; }
    public ICommand SelectAllGalleryCommand { get; }
    public ICommand ViewVideoLessonsCommand { get; }
    public ICommand LoadMoreVideosCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ToggleSessionsListExpandedCommand { get; }
    public ICommand TogglePlacesExpandedCommand { get; }
    public ICommand ToggleAthletesExpandedCommand { get; }
    public ICommand ToggleSectionsExpandedCommand { get; }
    public ICommand ToggleTagsExpandedCommand { get; }
    public ICommand SelectStatsTabCommand { get; }
    public ICommand SelectCrudTechTabCommand { get; }
    public ICommand SelectDiaryTabCommand { get; }
    public ICommand SaveDiaryCommand { get; }
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
    
    // Comandos de edición en lote
    public ICommand CloseBatchEditPopupCommand { get; }
    public ICommand ApplyBatchEditCommand { get; }
    public ICommand ToggleBatchTagCommand { get; }
    public ICommand SelectBatchAthleteCommand { get; }

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

    public DashboardViewModel(
        DatabaseService databaseService,
        CrownFileService crownFileService,
        StatisticsService statisticsService,
        ThumbnailService thumbnailService)
    {
        _databaseService = databaseService;
        _crownFileService = crownFileService;
        _statisticsService = statisticsService;
        _thumbnailService = thumbnailService;

        Title = "Dashboard";

        ImportCommand = new AsyncRelayCommand(ShowImportOptionsAsync);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ViewSessionCommand = new AsyncRelayCommand<Session>(ViewSessionAsync);
        ViewAllSessionsCommand = new AsyncRelayCommand(ViewAllSessionsAsync);
        ViewAthletesCommand = new AsyncRelayCommand(ViewAthletesAsync);
        PlaySelectedVideoCommand = new AsyncRelayCommand<VideoClip>(PlaySelectedVideoAsync);
        DeleteSelectedSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync);
        SelectAllGalleryCommand = new AsyncRelayCommand(SelectAllGalleryAsync);
        ViewVideoLessonsCommand = new AsyncRelayCommand(ViewVideoLessonsAsync);
        LoadMoreVideosCommand = new AsyncRelayCommand(LoadMoreVideosAsync);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        ToggleSessionsListExpandedCommand = new RelayCommand(() => IsSessionsListExpanded = !IsSessionsListExpanded);
        TogglePlacesExpandedCommand = new RelayCommand(() => IsPlacesExpanded = !IsPlacesExpanded);
        ToggleAthletesExpandedCommand = new RelayCommand(() => IsAthletesExpanded = !IsAthletesExpanded);
        ToggleSectionsExpandedCommand = new RelayCommand(() => IsSectionsExpanded = !IsSectionsExpanded);
        ToggleTagsExpandedCommand = new RelayCommand(() => IsTagsExpanded = !IsTagsExpanded);

        SelectStatsTabCommand = new RelayCommand(() => IsStatsTabSelected = true);
        SelectCrudTechTabCommand = new RelayCommand(() => IsCrudTechTabSelected = true);
        SelectDiaryTabCommand = new RelayCommand(() => IsDiaryTabSelected = true);
        SaveDiaryCommand = new AsyncRelayCommand(SaveDiaryAsync);
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
        
        // Comandos de edición en lote
        CloseBatchEditPopupCommand = new RelayCommand(() => ShowBatchEditPopup = false);
        ApplyBatchEditCommand = new AsyncRelayCommand(ApplyBatchEditAsync);
        ToggleBatchTagCommand = new RelayCommand<Tag>(ToggleBatchTag);
        SelectBatchAthleteCommand = new RelayCommand<Athlete>(SelectBatchAthlete);
        
        // Comando para alternar vista de tiempos
        ToggleSectionTimesViewCommand = new RelayCommand(() => ShowSectionTimesDifferences = !ShowSectionTimesDifferences);
        
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
        
        // Mostrar popup
        ShowBatchEditPopup = true;
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
            $"¿Estás seguro de que quieres eliminar {selectedVideos.Count} vídeo(s)?\n\nEsta acción no se puede deshacer.",
            "Eliminar",
            "Cancelar");
        
        if (!confirm)
            return;
        
        try
        {
            int deletedCount = 0;
            
            foreach (var video in selectedVideos)
            {
                // Eliminar archivo de vídeo local
                var videoPath = !string.IsNullOrWhiteSpace(video.LocalClipPath) && File.Exists(video.LocalClipPath)
                    ? video.LocalClipPath
                    : video.ClipPath;
                    
                if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
                {
                    try { File.Delete(videoPath); } catch { }
                }
                
                // Eliminar thumbnail local
                var thumbPath = !string.IsNullOrWhiteSpace(video.LocalThumbnailPath) && File.Exists(video.LocalThumbnailPath)
                    ? video.LocalThumbnailPath
                    : video.ThumbnailPath;
                    
                if (!string.IsNullOrWhiteSpace(thumbPath) && File.Exists(thumbPath))
                {
                    try { File.Delete(thumbPath); } catch { }
                }
                
                // Eliminar de la base de datos
                var db = await _databaseService.GetConnectionAsync();
                await db.DeleteAsync(video);
                
                deletedCount++;
            }
            
            // Limpiar selección
            _selectedVideoIds.Clear();
            OnPropertyChanged(nameof(SelectedVideoCount));
            
            // Mostrar confirmación
            await Shell.Current.DisplayAlert("Eliminación completada", $"Se eliminaron {deletedCount} vídeo(s).", "OK");
            
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
        if (!HasParallelVideo1 && !HasParallelVideo2)
        {
            await Shell.Current.DisplayAlert("Análisis", 
                "Arrastra al menos un vídeo a las áreas de análisis.", "OK");
            return;
        }

        // Desactivar el modo preview antes de abrir el reproductor
        IsPreviewMode = false;

        // Si está en modo único y hay un video en ParallelVideo1, usar SinglePlayerPage
        if (IsSingleVideoMode && HasParallelVideo1)
        {
            var singlePage = App.Current?.Handler?.MauiContext?.Services.GetService<Views.SinglePlayerPage>();
            if (singlePage?.BindingContext is SinglePlayerViewModel singleVm)
            {
                await singleVm.InitializeWithVideoAsync(ParallelVideo1!);
                await Shell.Current.Navigation.PushAsync(singlePage);
            }
            return;
        }

        // Modo paralelo: usar ParallelPlayerPage
        var page = App.Current?.Handler?.MauiContext?.Services.GetService<Views.ParallelPlayerPage>();
        if (page?.BindingContext is ParallelPlayerViewModel vm)
        {
            vm.Initialize(ParallelVideo1, ParallelVideo2, IsHorizontalOrientation);
            await Shell.Current.Navigation.PushAsync(page);
        }
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

    private void ClearFilters()
    {
        // Limpiar selección múltiple
        foreach (var place in FilterPlaces) place.IsSelected = false;
        foreach (var athlete in FilterAthletes) athlete.IsSelected = false;
        foreach (var section in FilterSections) section.IsSelected = false;
        foreach (var tag in FilterTagItems) tag.IsSelected = false;
        
        // Limpiar filtros simples
        _selectedFilterDateFrom = null;
        _selectedFilterDateTo = null;
        
        OnPropertyChanged(nameof(SelectedFilterDateFrom));
        OnPropertyChanged(nameof(SelectedFilterDateTo));
        OnPropertyChanged(nameof(SelectedPlacesSummary));
        OnPropertyChanged(nameof(SelectedAthletesSummary));
        OnPropertyChanged(nameof(SelectedSectionsSummary));
        OnPropertyChanged(nameof(SelectedTagsSummary));
        
        _ = ApplyFiltersAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        if (!IsAllGallerySelected || _allVideosCache == null)
            return;

        var allVideos = _allVideosCache;
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
        // Deseleccionar sesión actual
        _selectedSession = null;
        OnPropertyChanged(nameof(SelectedSession));
        
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
            $"¿Seguro que quieres eliminar la sesión '{session.DisplayName}'?\n\nSe borrarán sus vídeos, inputs y valoraciones.",
            "Eliminar",
            "Cancelar");

        if (!confirm)
            return;

        var deleted = false;
        try
        {
            IsBusy = true;
            await _databaseService.DeleteSessionCascadeAsync(session.Id, deleteSessionFiles: true);
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

            SyncVisibleRecentSessions();

            // Cargar el atleta de referencia del perfil del usuario
            await LoadReferenceAthleteAsync();

            // Si la sesión seleccionada ya no existe (p.ej. tras borrar), limpiar panel derecho.
            if (SelectedSession != null && !RecentSessions.Any(s => s.Id == SelectedSession.Id))
            {
                SelectedSession = null;
            }

            // Por defecto, mostrar Galería General al iniciar
            if (SelectedSession == null && !IsAllGallerySelected && !IsVideoLessonsSelected)
            {
                await SelectAllGalleryAsync();
            }
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
            await Shell.Current.GoToAsync(nameof(ImportPage));
        }
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
        _selectedSessionVideosCts?.Cancel();
        _selectedSessionVideosCts?.Dispose();
        _selectedSessionVideosCts = new CancellationTokenSource();
        var ct = _selectedSessionVideosCts.Token;

        try
        {
            IsLoadingSelectedSessionVideos = true;

            // Cambiar modo en el Dashboard (sin navegar)
            SelectedSession = null;
            IsAllGallerySelected = false;
            IsVideoLessonsSelected = true;

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
            if (!ct.IsCancellationRequested)
                await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar las videolecciones: {ex.Message}", "OK");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoadingSelectedSessionVideos = false;
        }
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
            }
            else
            {
                // Valores por defecto
                DiaryValoracionFisica = 3;
                DiaryValoracionMental = 3;
                DiaryValoracionTecnica = 3;
                DiaryNotas = "";
            }

            OnPropertyChanged(nameof(HasDiaryData));

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
            OnPropertyChanged(nameof(HasDiaryData));

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

    #endregion
}
