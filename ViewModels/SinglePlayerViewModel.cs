using CrownRFEP_Reader.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para el reproductor de vídeo individual con control preciso frame-by-frame.
/// </summary>
[QueryProperty(nameof(VideoPath), "videoPath")]
public class SinglePlayerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

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

    public SinglePlayerViewModel()
    {
        // Comandos de reproducción
        PlayPauseCommand = new Command(TogglePlayPause);
        StopCommand = new Command(Stop);
        SeekBackwardCommand = new Command(() => Seek(-5));
        SeekForwardCommand = new Command(() => Seek(5));
        FrameBackwardCommand = new Command(StepBackward);
        FrameForwardCommand = new Command(StepForward);
        SetSpeedCommand = new Command<string>(SetSpeed);
        ToggleOverlayCommand = new Command(() => ShowOverlay = !ShowOverlay);
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

    #region Comandos

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand FrameBackwardCommand { get; }
    public ICommand FrameForwardCommand { get; }
    public ICommand SetSpeedCommand { get; }
    public ICommand ToggleOverlayCommand { get; }

    #endregion

    #region Eventos

    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? FrameForwardRequested;
    public event EventHandler? FrameBackwardRequested;
    public event EventHandler<double>? SpeedChangeRequested;

    #endregion

    #region Métodos públicos

    public void SeekToPosition(double normalizedPosition)
    {
        var newPosition = normalizedPosition * Duration.TotalSeconds;
        SeekRequested?.Invoke(this, newPosition);
    }

    #endregion

    #region Métodos privados

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

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
