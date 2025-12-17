using System.Windows.Input;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para el reproductor de video
/// </summary>
[QueryProperty(nameof(VideoPath), "videoPath")]
public class VideoPlayerViewModel : BaseViewModel
{
    private string _videoPath = "";
    private string _videoTitle = "";
    private bool _isPlaying;
    private double _currentPosition;
    private double _duration;

    public string VideoPath
    {
        get => _videoPath;
        set
        {
            var decodedPath = Uri.UnescapeDataString(value);
            SetProperty(ref _videoPath, decodedPath);
            VideoTitle = Path.GetFileNameWithoutExtension(decodedPath);
        }
    }

    public string VideoTitle
    {
        get => _videoTitle;
        set => SetProperty(ref _videoTitle, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public double CurrentPosition
    {
        get => _currentPosition;
        set => SetProperty(ref _currentPosition, value);
    }

    public double Duration
    {
        get => _duration;
        set => SetProperty(ref _duration, value);
    }

    public string CurrentPositionFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(CurrentPosition);
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
    }

    public string DurationFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(Duration);
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand SeekCommand { get; }
    public ICommand GoBackCommand { get; }

    public VideoPlayerViewModel()
    {
        Title = "Reproductor";
        PlayPauseCommand = new RelayCommand(TogglePlayPause);
        SeekCommand = new RelayCommand<double>(Seek);
        GoBackCommand = new AsyncRelayCommand(GoBackAsync);
    }

    private void TogglePlayPause()
    {
        IsPlaying = !IsPlaying;
    }

    private void Seek(double position)
    {
        CurrentPosition = position;
        OnPropertyChanged(nameof(CurrentPositionFormatted));
    }

    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
