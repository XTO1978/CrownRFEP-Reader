using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para los detalles de un atleta
/// </summary>
[QueryProperty(nameof(AthleteId), "athleteId")]
public class AthleteDetailViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;

    private int _athleteId;
    private Athlete? _athlete;
    private int _totalVideos;
    private double _totalDuration;

    public int AthleteId
    {
        get => _athleteId;
        set
        {
            _athleteId = value;
            _ = LoadAthleteAsync();
        }
    }

    public Athlete? Athlete
    {
        get => _athlete;
        set => SetProperty(ref _athlete, value);
    }

    public int TotalVideos
    {
        get => _totalVideos;
        set => SetProperty(ref _totalVideos, value);
    }

    public double TotalDuration
    {
        get => _totalDuration;
        set => SetProperty(ref _totalDuration, value);
    }

    public string TotalDurationFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(TotalDuration);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }

    public ObservableCollection<VideoClip> Videos { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand PlayVideoCommand { get; }

    public AthleteDetailViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;

        RefreshCommand = new AsyncRelayCommand(LoadAthleteAsync);
        PlayVideoCommand = new AsyncRelayCommand<VideoClip>(PlayVideoAsync);

        // Refrescar lista si un video cambia (p.ej. reasignación de atleta desde SinglePlayer)
        MessagingCenter.Subscribe<SinglePlayerViewModel, int>(this, "VideoClipUpdated", (sender, videoId) =>
        {
            if (AthleteId == 0) return;
            _ = Task.Run(async () =>
            {
                var wasInList = Videos.Any(v => v.Id == videoId);
                var updated = await _databaseService.GetVideoClipByIdAsync(videoId);
                var shouldReload = wasInList || (updated != null && updated.AtletaId == AthleteId);
                if (!shouldReload) return;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await LoadAthleteAsync();
                });
            });
        });
    }

    public async Task LoadAthleteAsync()
    {
        if (IsBusy || AthleteId == 0) return;

        try
        {
            IsBusy = true;

            // Cargar atleta
            Athlete = await _databaseService.GetAthleteByIdAsync(AthleteId);
            if (Athlete == null) return;

            Title = Athlete.NombreCompleto;

            // Cargar videos del atleta
            var videos = await _databaseService.GetVideoClipsByAthleteAsync(AthleteId);
            Videos.Clear();

            foreach (var video in videos)
            {
                Videos.Add(video);
            }

            TotalVideos = videos.Count;
            TotalDuration = videos.Sum(v => v.ClipDuration);
            OnPropertyChanged(nameof(TotalDurationFormatted));
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo cargar el atleta: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PlayVideoAsync(VideoClip? video)
    {
        if (video == null) return;

        var videoPath = video.LocalClipPath;

        // Fallback: construir ruta local desde la carpeta de la sesión
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            try
            {
                var session = await _databaseService.GetSessionByIdAsync(video.SessionId);
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
}
