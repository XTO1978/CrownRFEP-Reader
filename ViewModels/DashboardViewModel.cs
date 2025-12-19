using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para la página principal / Dashboard
/// </summary>
public class DashboardViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private readonly CrownFileService _crownFileService;
    private readonly StatisticsService _statisticsService;

    private DashboardStats? _stats;
    private Session? _selectedSession;
    private bool _isAllGallerySelected;
    private bool _isLoadingSelectedSessionVideos;
    private string _importProgressText = "";
    private int _importProgressValue;
    private bool _isImporting;

    // Pestañas columna derecha
    private bool _isStatsTabSelected = true;
    private bool _isCrudTechTabSelected;

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

    private CancellationTokenSource? _selectedSessionVideosCts;

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
                }
                OnPropertyChanged(nameof(SelectedSessionTitle));
                _ = LoadSelectedSessionVideosAsync(value);
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
                OnPropertyChanged(nameof(SelectedSessionTitle));
            }
        }
    }

    public string SelectedSessionTitle => IsAllGallerySelected 
        ? "Galería General" 
        : (SelectedSession?.DisplayName ?? "Selecciona una sesión");

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
                }
            }
        }
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
    public ObservableCollection<VideoClip> SelectedSessionVideos { get; } = new();

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

    // Estadísticas: usar el cache filtrado para mostrar totales reales (no solo paginados)
    public int TotalFilteredVideoCount => (_filteredVideosCache ?? _allVideosCache)?.Count ?? SelectedSessionVideos.Count;
    public double TotalFilteredDurationSeconds => (_filteredVideosCache ?? _allVideosCache)?.Sum(v => v.ClipDuration) ?? SelectedSessionVideos.Sum(v => v.ClipDuration);
    
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
    public ICommand LoadMoreVideosCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand TogglePlacesExpandedCommand { get; }
    public ICommand ToggleAthletesExpandedCommand { get; }
    public ICommand ToggleSectionsExpandedCommand { get; }
    public ICommand ToggleTagsExpandedCommand { get; }
    public ICommand SelectStatsTabCommand { get; }
    public ICommand SelectCrudTechTabCommand { get; }

    public DashboardViewModel(
        DatabaseService databaseService,
        CrownFileService crownFileService,
        StatisticsService statisticsService)
    {
        _databaseService = databaseService;
        _crownFileService = crownFileService;
        _statisticsService = statisticsService;

        Title = "Dashboard";

        ImportCommand = new AsyncRelayCommand(ImportCrownFileAsync);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ViewSessionCommand = new AsyncRelayCommand<Session>(ViewSessionAsync);
        ViewAllSessionsCommand = new AsyncRelayCommand(ViewAllSessionsAsync);
        ViewAthletesCommand = new AsyncRelayCommand(ViewAthletesAsync);
        PlaySelectedVideoCommand = new AsyncRelayCommand<VideoClip>(PlaySelectedVideoAsync);
        DeleteSelectedSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync);
        SelectAllGalleryCommand = new AsyncRelayCommand(SelectAllGalleryAsync);
        LoadMoreVideosCommand = new AsyncRelayCommand(LoadMoreVideosAsync);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        TogglePlacesExpandedCommand = new RelayCommand(() => IsPlacesExpanded = !IsPlacesExpanded);
        ToggleAthletesExpandedCommand = new RelayCommand(() => IsAthletesExpanded = !IsAthletesExpanded);
        ToggleSectionsExpandedCommand = new RelayCommand(() => IsSectionsExpanded = !IsSectionsExpanded);
        ToggleTagsExpandedCommand = new RelayCommand(() => IsTagsExpanded = !IsTagsExpanded);

        SelectStatsTabCommand = new RelayCommand(() => IsStatsTabSelected = true);
        SelectCrudTechTabCommand = new RelayCommand(() => IsCrudTechTabSelected = true);
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

        var filtered = _allVideosCache.AsEnumerable();

        // Filtrar por lugares seleccionados (selección múltiple)
        var selectedPlaces = FilterPlaces.Where(p => p.IsSelected).Select(p => p.Value).ToList();
        if (selectedPlaces.Any())
        {
            var sessionsInPlaces = RecentSessions
                .Where(s => selectedPlaces.Contains(s.Lugar))
                .Select(s => s.Id)
                .ToHashSet();
            filtered = filtered.Where(v => sessionsInPlaces.Contains(v.SessionId));
        }

        // Filtrar por fecha desde
        if (SelectedFilterDateFrom.HasValue)
        {
            filtered = filtered.Where(v => v.CreationDateTime >= SelectedFilterDateFrom.Value);
        }

        // Filtrar por fecha hasta
        if (SelectedFilterDateTo.HasValue)
        {
            filtered = filtered.Where(v => v.CreationDateTime <= SelectedFilterDateTo.Value.AddDays(1));
        }

        // Filtrar por deportistas seleccionados (selección múltiple)
        var selectedAthletes = FilterAthletes.Where(a => a.IsSelected).Select(a => a.Value.Id).ToList();
        if (selectedAthletes.Any())
        {
            filtered = filtered.Where(v => selectedAthletes.Contains(v.AtletaId));
        }

        // Filtrar por secciones seleccionadas (selección múltiple)
        var selectedSections = FilterSections.Where(s => s.IsSelected).Select(s => s.Value).ToList();
        if (selectedSections.Any())
        {
            filtered = filtered.Where(v => selectedSections.Contains(v.Section));
        }

        // Filtrar por tags seleccionados (selección múltiple)
        var selectedTags = FilterTagItems.Where(t => t.IsSelected).Select(t => t.Value.Id).ToList();
        if (selectedTags.Any() && _allInputsCache != null)
        {
            // Obtener los VideoIds que tienen alguno de los tags seleccionados
            var videoIdsWithTags = _allInputsCache
                .Where(i => selectedTags.Contains(i.InputTypeId))
                .Select(i => i.VideoId)
                .ToHashSet();
            
            filtered = filtered.Where(v => videoIdsWithTags.Contains(v.Id));
        }

        _filteredVideosCache = filtered.ToList();
        
        // Recargar videos paginados con el filtro aplicado
        SelectedSessionVideos.Clear();
        _currentPage = 0;
        
        var firstBatch = _filteredVideosCache.Take(PageSize).ToList();
        foreach (var clip in firstBatch)
        {
            SelectedSessionVideos.Add(clip);
        }
        _currentPage = 1;
        HasMoreVideos = _filteredVideosCache.Count > PageSize;

        // Actualizar estadísticas basadas en videos filtrados
        UpdateStatisticsFromFilteredVideos();

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
            
            // Cargar todos los videos en caché
            System.Diagnostics.Debug.WriteLine($"[LoadAllVideosAsync] Loading all videos from DB...");
            _allVideosCache = await _databaseService.GetAllVideoClipsAsync();
            System.Diagnostics.Debug.WriteLine($"[LoadAllVideosAsync] Videos loaded: {_allVideosCache?.Count ?? 0}");
            if (ct.IsCancellationRequested) return;

            // Cargar opciones de filtro
            await LoadFilterOptionsAsync();

            // Cargar primer lote
            _filteredVideosCache = _allVideosCache;
            var firstBatch = _filteredVideosCache.Take(PageSize).ToList();
            System.Diagnostics.Debug.WriteLine($"[LoadAllVideosAsync] First batch: {firstBatch.Count}, adding to SelectedSessionVideos...");
            foreach (var clip in firstBatch)
            {
                SelectedSessionVideos.Add(clip);
            }
            System.Diagnostics.Debug.WriteLine($"[LoadAllVideosAsync] SelectedSessionVideos.Count: {SelectedSessionVideos.Count}");
            _currentPage = 1;
            HasMoreVideos = _filteredVideosCache.Count > PageSize;

            // Actualizar estadísticas basadas en todos los videos
            UpdateStatisticsFromFilteredVideos();

            OnPropertyChanged(nameof(TotalFilteredVideoCount));
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
        
        // Cargar lugares únicos de las sesiones
        FilterPlaces.Clear();
        var places = RecentSessions
            .Where(s => !string.IsNullOrEmpty(s.Lugar))
            .Select(s => s.Lugar!)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] Places found: {places.Count}");
        foreach (var place in places)
        {
            var item = new PlaceFilterItem(place);
            item.SelectionChanged += OnFilterSelectionChanged;
            FilterPlaces.Add(item);
        }
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] FilterPlaces.Count: {FilterPlaces.Count}");

        // Cargar atletas
        FilterAthletes.Clear();
        var athletes = await _databaseService.GetAllAthletesAsync();
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] Athletes from DB: {athletes.Count}");
        foreach (var athlete in athletes.OrderBy(a => a.NombreCompleto))
        {
            var item = new AthleteFilterItem(athlete);
            item.SelectionChanged += OnFilterSelectionChanged;
            FilterAthletes.Add(item);
        }
        System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] FilterAthletes.Count: {FilterAthletes.Count}");

        // Cargar secciones únicas de los videos
        FilterSections.Clear();
        if (_allVideosCache != null)
        {
            var sections = _allVideosCache
                .Select(v => v.Section)
                .Distinct()
                .OrderBy(s => s)
                .ToList();
            System.Diagnostics.Debug.WriteLine($"[LoadFilterOptionsAsync] Sections found: {sections.Count}");
            foreach (var section in sections)
            {
                var item = new SectionFilterItem(section);
                item.SelectionChanged += OnFilterSelectionChanged;
                FilterSections.Add(item);
            }
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
            
            foreach (var tag in tags)
            {
                var item = new TagFilterItem(tag);
                item.SelectionChanged += OnFilterSelectionChanged;
                FilterTagItems.Add(item);
            }
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

            foreach (var clip in nextBatch)
            {
                SelectedSessionVideos.Add(clip);
            }

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

            // Si la sesión seleccionada ya no existe (p.ej. tras borrar), limpiar panel derecho.
            if (SelectedSession != null && !RecentSessions.Any(s => s.Id == SelectedSession.Id))
            {
                SelectedSession = null;
            }

            // Por defecto, mostrar Galería General al iniciar
            if (SelectedSession == null && !IsAllGallerySelected)
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

            // Guardar en caché para paginación
            _allVideosCache = clips;
            
            // Cargar primer lote
            var firstBatch = clips.Take(PageSize).ToList();
            foreach (var clip in firstBatch)
            {
                SelectedSessionVideos.Add(clip);
            }
            _currentPage = 1;
            HasMoreVideos = clips.Count > PageSize;

            // Estadísticas por sección (tabla + gráfico)
            var sectionStats = await _statisticsService.GetVideosBySectionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            foreach (var s in sectionStats)
            {
                SelectedSessionSectionStats.Add(s);

                // Para gráfico: minutos, y etiqueta corta
                SelectedSessionSectionDurationMinutes.Add(Math.Round(s.TotalDuration / 60.0, 1));
                SelectedSessionSectionLabels.Add(s.Section.ToString());
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

            // Etiquetas (tags) y penalizaciones: usando Inputs de la sesión.
            // Asunción: InputTypeId representa el TagId.
            var inputs = await _databaseService.GetInputsBySessionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            foreach (var input in inputs.OrderBy(i => i.InputDateTime))
            {
                SelectedSessionInputs.Add(input);
            }

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

            foreach (var t in tagStats)
            {
                SelectedSessionTagLabels.Add(t.TagId.ToString());
                SelectedSessionTagVideoCounts.Add(t.VideoCount);
            }

            // Penalizaciones (tags 2 y 50): por número de asignaciones
            var penalty2 = inputs.Count(i => i.InputTypeId == 2);
            var penalty50 = inputs.Count(i => i.InputTypeId == 50);
            SelectedSessionPenaltyLabels.Add("2");
            SelectedSessionPenaltyLabels.Add("50");
            SelectedSessionPenaltyCounts.Add(penalty2);
            SelectedSessionPenaltyCounts.Add(penalty50);

            // Valoraciones (tabla valoracion)
            var valoraciones = await _databaseService.GetValoracionesBySessionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            foreach (var v in valoraciones.OrderBy(v => v.InputDateTime))
            {
                SelectedSessionValoraciones.Add(v);
            }

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

        await Shell.Current.GoToAsync($"{nameof(VideoPlayerPage)}?videoPath={Uri.EscapeDataString(videoPath)}");
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

            Task<string?> pickTask;
            if (MainThread.IsMainThread)
            {
                pickTask = _crownFileService.PickCrownFilePathAsync();
            }
            else
            {
                pickTask = MainThread.InvokeOnMainThreadAsync(_crownFileService.PickCrownFilePathAsync);
            }

            var completed = await Task.WhenAny(pickTask, Task.Delay(TimeSpan.FromSeconds(15)));
            if (completed != pickTask)
            {
                await Shell.Current.DisplayAlert(
                    "Selector bloqueado",
                    "El selector de archivos no respondió (15s). Si ocurre siempre en macOS, suele ser un problema del FilePicker de MacCatalyst. Prueba a traer la ventana al frente y vuelve a intentar.",
                    "OK");
                return;
            }

            var filePath = await pickTask;

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

    private async Task ViewAthletesAsync()
    {
        await Shell.Current.GoToAsync(nameof(AthletesPage));
    }
}
