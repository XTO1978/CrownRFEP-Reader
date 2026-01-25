using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para los detalles de una sesión
/// </summary>
[QueryProperty(nameof(SessionId), "sessionId")]
public class SessionDetailViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private readonly StatisticsService _statisticsService;
    private readonly IVideoClipUpdateNotifier _videoClipUpdateNotifier;

    private int _sessionId;
    private Session? _session;
    private VideoClip? _selectedVideo;
    private Athlete? _selectedAthleteFilter;
    private int? _selectedSectionFilter;

    public int SessionId
    {
        get => _sessionId;
        set
        {
            _sessionId = value;
            _ = LoadSessionAsync();
        }
    }

    public Session? Session
    {
        get => _session;
        set => SetProperty(ref _session, value);
    }

    public VideoClip? SelectedVideo
    {
        get => _selectedVideo;
        set => SetProperty(ref _selectedVideo, value);
    }

    public Athlete? SelectedAthleteFilter
    {
        get => _selectedAthleteFilter;
        set
        {
            if (SetProperty(ref _selectedAthleteFilter, value))
            {
                FilterVideos();
            }
        }
    }

    public int? SelectedSectionFilter
    {
        get => _selectedSectionFilter;
        set
        {
            if (SetProperty(ref _selectedSectionFilter, value))
            {
                FilterVideos();
            }
        }
    }

    public ObservableCollection<VideoClip> AllVideos { get; } = new();
    public ObservableCollection<VideoClip> FilteredVideos { get; } = new();
    public ObservableCollection<Athlete> SessionAthletes { get; } = new();
    public ObservableCollection<SectionStats> SectionStats { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand PlayVideoCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand FilterByAthleteCommand { get; }
    public ICommand FilterBySectionCommand { get; }
    public ICommand RecordVideoCommand { get; }

    /// <summary>
    /// Indica si la grabación de video está disponible (solo iOS)
    /// </summary>
    public bool CanRecordVideo => DeviceInfo.Platform == DevicePlatform.iOS;

    public SessionDetailViewModel(DatabaseService databaseService, StatisticsService statisticsService, IVideoClipUpdateNotifier videoClipUpdateNotifier)
    {
        _databaseService = databaseService;
        _statisticsService = statisticsService;
        _videoClipUpdateNotifier = videoClipUpdateNotifier;

        RefreshCommand = new AsyncRelayCommand(LoadSessionAsync);
        PlayVideoCommand = new AsyncRelayCommand<VideoClip>(PlayVideoAsync);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        FilterByAthleteCommand = new RelayCommand<Athlete>(athlete => SelectedAthleteFilter = athlete);
        FilterBySectionCommand = new RelayCommand<int?>(section => SelectedSectionFilter = section);
        RecordVideoCommand = new AsyncRelayCommand(RecordVideoAsync);

        // Refrescar galería/filtros si un VideoClip cambia (p.ej. asignación de atleta desde SinglePlayer)
        _videoClipUpdateNotifier.VideoClipUpdated += HandleVideoClipUpdated;
    }

    private async void HandleVideoClipUpdated(object? sender, int videoClipId)
    {
        if (SessionId == 0) return;

        var wasInList = AllVideos.Any(v => v.Id == videoClipId);
        var updated = await _databaseService.GetVideoClipByIdAsync(videoClipId);
        var shouldReload = wasInList || (updated != null && updated.SessionId == SessionId);
        if (!shouldReload) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await LoadSessionAsync();
        });
    }

    /// <summary>
    /// Navega a la página de cámara para grabar videos de esta sesión
    /// </summary>
    private async Task RecordVideoAsync()
    {
        if (Session == null) return;

        try
        {
            var navigationParams = new Dictionary<string, object>
            {
                { "SessionId", Session.Id },
                { "SessionName", Session.DisplayName },
                { "SessionType", Session.TipoSesion ?? "Entrenamiento" },
                { "Place", Session.Lugar ?? "" },
                { "Date", Session.FechaDateTime }
            };

            await Shell.Current.GoToAsync("CameraPage", navigationParams);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(SessionDetailViewModel), $"Error navigating to camera: {ex.Message}", ex);
        }
    }

    public async Task LoadSessionAsync()
    {
        if (IsBusy || SessionId == 0) return;

        try
        {
            IsBusy = true;

            // Cargar sesión
            Session = await _databaseService.GetSessionByIdAsync(SessionId);
            if (Session == null) return;

            Title = Session.DisplayName;

            // Cargar videos
            var videos = await _databaseService.GetVideoClipsBySessionAsync(SessionId);
            AllVideos.Clear();
            FilteredVideos.Clear();

            foreach (var video in videos)
            {
                AllVideos.Add(video);
                FilteredVideos.Add(video);
            }

            // Obtener atletas únicos de la sesión
            var athleteIds = videos.Select(v => v.AtletaId).Distinct();
            SessionAthletes.Clear();
            foreach (var athleteId in athleteIds)
            {
                var athlete = await _databaseService.GetAthleteByIdAsync(athleteId);
                if (athlete != null)
                {
                    SessionAthletes.Add(athlete);
                }
            }

            // Cargar estadísticas por sección
            var sectionStats = await _statisticsService.GetVideosBySectionAsync(SessionId);
            SectionStats.Clear();
            foreach (var stat in sectionStats)
            {
                SectionStats.Add(stat);
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo cargar la sesión: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void FilterVideos()
    {
        FilteredVideos.Clear();

        var filtered = AllVideos.AsEnumerable();

        if (SelectedAthleteFilter != null)
        {
            filtered = filtered.Where(v => v.AtletaId == SelectedAthleteFilter.Id);
        }

        if (SelectedSectionFilter.HasValue)
        {
            filtered = filtered.Where(v => v.Section == SelectedSectionFilter.Value);
        }

        foreach (var video in filtered)
        {
            FilteredVideos.Add(video);
        }
    }

    private void ClearFilters()
    {
        SelectedAthleteFilter = null;
        SelectedSectionFilter = null;
        FilterVideos();
    }

    private async Task PlayVideoAsync(VideoClip? video)
    {
        if (video == null) return;

        var videoPath = video.LocalClipPath;

        // Fallback: construir ruta local desde la carpeta de la sesión
        if ((string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) && !string.IsNullOrWhiteSpace(Session?.PathSesion))
        {
            var normalized = (video.ClipPath ?? "").Replace('\\', '/');
            var fileName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"CROWN{video.Id}.mp4";

            var candidate = Path.Combine(Session.PathSesion, "videos", fileName);
            if (File.Exists(candidate))
                videoPath = candidate;
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
}
