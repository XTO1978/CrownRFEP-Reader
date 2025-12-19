using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
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
    /// Inicializa el ViewModel con un video y carga los datos de filtrado de la sesión
    /// </summary>
    public async Task InitializeWithVideoAsync(VideoClip video)
    {
        VideoClip = video;
        
        if (video.SessionId > 0)
        {
            await LoadSessionDataAsync(video.SessionId);
            
            // Encontrar el video actual en la playlist
            _currentPlaylistIndex = _filteredPlaylist.FindIndex(v => v.Id == video.Id);
            if (_currentPlaylistIndex < 0) _currentPlaylistIndex = 0;
            
            UpdatePlaylistProperties();
        }
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
                NavigateToCurrentPlaylistVideo();
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
        NavigateToCurrentPlaylistVideo();
    }

    private void GoToNextVideo()
    {
        if (!CanGoNext) return;
        
        _currentPlaylistIndex++;
        NavigateToCurrentPlaylistVideo();
    }

    private void NavigateToCurrentPlaylistVideo()
    {
        if (_currentPlaylistIndex < 0 || _currentPlaylistIndex >= _filteredPlaylist.Count)
            return;
        
        var newVideo = _filteredPlaylist[_currentPlaylistIndex];
        
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
        
        // Actualizar path
        if (newVideo.LocalClipPath != null)
        {
            _videoPath = newVideo.LocalClipPath;
            OnPropertyChanged(nameof(VideoPath));
        }
        
        // Actualizar título
        VideoTitle = newVideo.Atleta?.NombreCompleto ?? Path.GetFileNameWithoutExtension(_videoPath);
        
        UpdatePlaylistProperties();
        
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
