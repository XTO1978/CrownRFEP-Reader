using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.ViewModels;

public class BatchEditViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService;
    private Func<IReadOnlyList<VideoClip>> _getSelectedSessionVideos = () => Array.Empty<VideoClip>();
    private Func<IReadOnlyList<VideoClip>?> _getFilteredVideos = () => null;
    private Func<IReadOnlyList<VideoClip>?> _getAllVideosCache = () => null;
    private Func<Session?> _getSelectedSession = () => null;
    private Func<bool> _isAllGallerySelected = () => false;
    private Func<Task> _loadSelectedSessionVideosAsync = () => Task.CompletedTask;
    private Func<Task> _loadAllVideosAsync = () => Task.CompletedTask;

    private bool _isMultiSelectMode;
    private bool _isSelectAllActive;
    private readonly HashSet<int> _selectedVideoIds = new();

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

    public BatchEditViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;

        ToggleMultiSelectModeCommand = new RelayCommand(() => IsMultiSelectMode = !IsMultiSelectMode);
        ToggleSelectAllCommand = new RelayCommand(() => IsSelectAllActive = !IsSelectAllActive);
        ToggleVideoSelectionCommand = new RelayCommand<VideoClip>(ToggleVideoSelection);
        ClearVideoSelectionCommand = new RelayCommand(ClearVideoSelection);

        CloseBatchEditPopupCommand = new RelayCommand(() => ShowBatchEditPopup = false);
        ApplyBatchEditCommand = new AsyncRelayCommand(ApplyBatchEditAsync);
        EditVideoDetailsCommand = new AsyncRelayCommand(EditVideoDetailsAsync);

        ToggleBatchTagCommand = new RelayCommand<Tag>(ToggleBatchTag);
        SelectBatchAthleteCommand = new RelayCommand<Athlete>(SelectBatchAthlete);
        AddNewBatchAthleteCommand = new AsyncRelayCommand(AddNewBatchAthleteAsync);
        AddNewBatchTagCommand = new AsyncRelayCommand(AddNewBatchTagAsync);
    }

    public void Configure(
        Func<IReadOnlyList<VideoClip>> getSelectedSessionVideos,
        Func<IReadOnlyList<VideoClip>?> getFilteredVideos,
        Func<IReadOnlyList<VideoClip>?> getAllVideosCache,
        Func<Session?> getSelectedSession,
        Func<bool> isAllGallerySelected,
        Func<Task> loadSelectedSessionVideosAsync,
        Func<Task> loadAllVideosAsync)
    {
        _getSelectedSessionVideos = getSelectedSessionVideos;
        _getFilteredVideos = getFilteredVideos;
        _getAllVideosCache = getAllVideosCache;
        _getSelectedSession = getSelectedSession;
        _isAllGallerySelected = isAllGallerySelected;
        _loadSelectedSessionVideosAsync = loadSelectedSessionVideosAsync;
        _loadAllVideosAsync = loadAllVideosAsync;
    }

    public bool IsMultiSelectMode
    {
        get => _isMultiSelectMode;
        set
        {
            if (SetProperty(ref _isMultiSelectMode, value))
            {
                if (!value)
                {
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

    public bool IsTagSelectedForBatchEdit(int tagId) => _batchEditSelectedTagIds.Contains(tagId);

    public bool IsVideoSelected(int videoId) => _selectedVideoIds.Contains(videoId);

    public IReadOnlyCollection<int> SelectedVideoIds => _selectedVideoIds;

    public List<VideoClip> GetSelectedVideos()
    {
        var source = GetSelectionSource();
        return source.Where(v => _selectedVideoIds.Contains(v.Id)).ToList();
    }

    public void ToggleVideoSelection(VideoClip? video)
    {
        if (video == null || !IsMultiSelectMode) return;

        video.IsSelected = !video.IsSelected;

        if (video.IsSelected)
            _selectedVideoIds.Add(video.Id);
        else
            _selectedVideoIds.Remove(video.Id);

        OnPropertyChanged(nameof(SelectedVideoCount));
    }

    public void SelectSingleVideoForEdit(VideoClip video)
    {
        if (video == null) return;

        ClearVideoSelection();

        video.IsSelected = true;
        _selectedVideoIds.Add(video.Id);
        OnPropertyChanged(nameof(SelectedVideoCount));
    }

    public void UpdateVideoSelectionState(VideoClip video)
    {
        if (video == null) return;

        if (video.IsSelected)
            _selectedVideoIds.Add(video.Id);
        else
            _selectedVideoIds.Remove(video.Id);

        OnPropertyChanged(nameof(SelectedVideoCount));
    }

    public void SelectSingleVideo(VideoClip video)
    {
        if (video == null) return;

        foreach (var v in _getSelectedSessionVideos())
            v.IsSelected = false;

        var source = _getFilteredVideos() ?? _getAllVideosCache();
        if (source != null)
        {
            foreach (var v in source)
                v.IsSelected = false;
        }

        _selectedVideoIds.Clear();

        video.IsSelected = true;
        _selectedVideoIds.Add(video.Id);

        OnPropertyChanged(nameof(SelectedVideoCount));
    }

    public void ClearVideoSelection()
    {
        foreach (var v in _getSelectedSessionVideos())
            v.IsSelected = false;

        var source = _getFilteredVideos() ?? _getAllVideosCache();
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

    private void SelectAllFilteredVideos()
    {
        var source = _getFilteredVideos() ?? _getAllVideosCache();
        if (source == null) return;

        foreach (var v in source)
        {
            v.IsSelected = true;
            _selectedVideoIds.Add(v.Id);
        }

        OnPropertyChanged(nameof(SelectedVideoCount));
    }

    private IEnumerable<VideoClip> GetSelectionSource()
    {
        var filtered = _getFilteredVideos();
        if (filtered != null)
            return filtered;

        var all = _getAllVideosCache();
        if (all != null)
            return all;

        return _getSelectedSessionVideos();
    }

    private async Task EditVideoDetailsAsync()
    {
        if (_selectedVideoIds.Count == 0)
        {
            await Shell.Current.DisplayAlert("Editar", "No hay vídeos seleccionados.", "OK");
            return;
        }

        var athletes = await _databaseService.GetAllAthletesAsync();
        BatchEditAthletes.Clear();
        foreach (var a in athletes.OrderBy(a => a.NombreCompleto))
        {
            a.IsSelected = false;
            BatchEditAthletes.Add(a);
        }

        var tags = await _databaseService.GetAllTagsAsync();
        BatchEditTags.Clear();
        foreach (var t in tags.OrderBy(t => t.NombreTag))
        {
            t.IsSelectedBool = false;
            BatchEditTags.Add(t);
        }

        SelectedBatchAthlete = null;
        BatchEditSection = 1;
        BatchEditSectionEnabled = false;
        BatchEditAthleteEnabled = false;
        _batchEditSelectedTagIds.Clear();

        NewBatchAthleteSurname = string.Empty;
        NewBatchAthleteName = string.Empty;
        NewBatchTagText = string.Empty;

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
                    Favorite = 0
                };

                await _databaseService.SaveAthleteAsync(athlete);
            }

            athlete.IsSelected = false;
            BatchEditAthletes.Add(athlete);

            NewBatchAthleteSurname = string.Empty;
            NewBatchAthleteName = string.Empty;

            SelectBatchAthlete(athlete);
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
                await Shell.Current.DisplayAlert("Etiqueta", "Introduce el nombre de la etiqueta.", "OK");
                return;
            }

            var existing = await _databaseService.FindTagByNameAsync(name);
            Tag tag;
            if (existing != null)
            {
                tag = existing;
            }
            else
            {
                tag = new Tag
                {
                    NombreTag = name,
                    IsEventTag = false
                };

                await _databaseService.SaveTagAsync(tag);
            }

            tag.IsSelectedBool = true;
            BatchEditTags.Add(tag);
            _batchEditSelectedTagIds.Add(tag.Id);

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

        tag.IsSelectedBool = !tag.IsSelectedBool;

        if (tag.IsSelectedBool)
            _batchEditSelectedTagIds.Add(tag.Id);
        else
            _batchEditSelectedTagIds.Remove(tag.Id);
    }

    private void SelectBatchAthlete(Athlete? athlete)
    {
        if (athlete == null) return;

        if (SelectedBatchAthlete != null)
            SelectedBatchAthlete.IsSelected = false;

        athlete.IsSelected = true;
        SelectedBatchAthlete = athlete;

        BatchEditAthleteEnabled = true;
    }

    private async Task ApplyBatchEditAsync()
    {
        var selectedVideos = GetSelectedVideos();

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

                if (BatchEditAthleteEnabled && SelectedBatchAthlete != null)
                {
                    video.AtletaId = SelectedBatchAthlete.Id;
                    video.Atleta = SelectedBatchAthlete;
                    updated = true;
                }

                if (BatchEditSectionEnabled && BatchEditSection > 0)
                {
                    video.Section = BatchEditSection;
                    updated = true;
                }

                if (updated)
                {
                    await _databaseService.SaveVideoClipAsync(video);
                }

                foreach (var tagId in _batchEditSelectedTagIds)
                {
                    await _databaseService.AddTagToVideoAsync(video.Id, tagId, video.SessionId, video.AtletaId);
                }
            }

            ShowBatchEditPopup = false;

            var msg = $"Se actualizaron {selectedVideos.Count} vídeos.";
            if (_batchEditSelectedTagIds.Count > 0)
                msg += $"\nSe añadieron {_batchEditSelectedTagIds.Count} etiquetas.";

            await Shell.Current.DisplayAlert("Edición completada", msg, "OK");

            var selectedSession = _getSelectedSession();
            if (selectedSession != null)
                await _loadSelectedSessionVideosAsync();
            else if (_isAllGallerySelected())
                await _loadAllVideosAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo aplicar los cambios: {ex.Message}", "OK");
        }
    }

    public ICommand ToggleMultiSelectModeCommand { get; }
    public ICommand ToggleSelectAllCommand { get; }
    public ICommand ToggleVideoSelectionCommand { get; }
    public ICommand ClearVideoSelectionCommand { get; }

    public ICommand EditVideoDetailsCommand { get; }
    public ICommand CloseBatchEditPopupCommand { get; }
    public ICommand ApplyBatchEditCommand { get; }

    public ICommand ToggleBatchTagCommand { get; }
    public ICommand SelectBatchAthleteCommand { get; }
    public ICommand AddNewBatchAthleteCommand { get; }
    public ICommand AddNewBatchTagCommand { get; }
}
