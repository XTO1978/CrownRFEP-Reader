using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.ViewModels;

public sealed class TrashViewModel : BaseViewModel
{
    private readonly ITrashService _trashService;

    private int _selectedTabIndex;
    private int _selectedSessionsCount;
    private int _selectedVideosCount;

    public ObservableCollection<Session> TrashedSessions { get; } = new();
    public ObservableCollection<VideoClip> TrashedVideos { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SelectTabCommand { get; }

    public ICommand ToggleSessionSelectionCommand { get; }
    public ICommand ToggleVideoSelectionCommand { get; }

    public ICommand RestoreSelectedCommand { get; }
    public ICommand DeleteSelectedPermanentlyCommand { get; }

    public TrashViewModel(ITrashService trashService)
    {
        _trashService = trashService;
        Title = "Papelera";

        RefreshCommand = new AsyncRelayCommand(LoadAsync);

        SelectTabCommand = new Command<string>(SelectTab);

        ToggleSessionSelectionCommand = new Command<Session>(ToggleSessionSelection);
        ToggleVideoSelectionCommand = new Command<VideoClip>(ToggleVideoSelection);

        RestoreSelectedCommand = new AsyncRelayCommand(RestoreSelectedAsync);
        DeleteSelectedPermanentlyCommand = new AsyncRelayCommand(DeleteSelectedPermanentlyAsync);

        TrashedSessions.CollectionChanged += OnCollectionChanged;
        TrashedVideos.CollectionChanged += OnCollectionChanged;

        SelectedTabIndex = 0;
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(IsSessionsTab));
                OnPropertyChanged(nameof(IsVideosTab));
                OnPropertyChanged(nameof(CurrentSelectedCount));
                OnPropertyChanged(nameof(CanOperateCurrentTab));
            }
        }
    }

    public bool IsSessionsTab => SelectedTabIndex == 0;
    public bool IsVideosTab => SelectedTabIndex == 1;

    public int TrashedSessionsCount => TrashedSessions.Count;
    public int TrashedVideosCount => TrashedVideos.Count;

    public int SelectedSessionsCount
    {
        get => _selectedSessionsCount;
        private set
        {
            if (SetProperty(ref _selectedSessionsCount, value))
            {
                OnPropertyChanged(nameof(CurrentSelectedCount));
                OnPropertyChanged(nameof(CanOperateCurrentTab));
            }
        }
    }

    public int SelectedVideosCount
    {
        get => _selectedVideosCount;
        private set
        {
            if (SetProperty(ref _selectedVideosCount, value))
            {
                OnPropertyChanged(nameof(CurrentSelectedCount));
                OnPropertyChanged(nameof(CanOperateCurrentTab));
            }
        }
    }

    public int CurrentSelectedCount => IsSessionsTab ? SelectedSessionsCount : SelectedVideosCount;

    public bool CanOperateCurrentTab => CurrentSelectedCount >= 1;

    public async Task LoadAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            var sessions = await _trashService.GetTrashedSessionsAsync();
            var videos = await _trashService.GetTrashedVideosAsync();

            TrashedSessions.Clear();
            foreach (var s in sessions)
                TrashedSessions.Add(s);

            TrashedVideos.Clear();
            foreach (var v in videos)
                TrashedVideos.Add(v);

            ClearSelection();

            OnPropertyChanged(nameof(TrashedSessionsCount));
            OnPropertyChanged(nameof(TrashedVideosCount));
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo cargar la papelera: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TrashedSessionsCount));
        OnPropertyChanged(nameof(TrashedVideosCount));
    }

    private void SelectTab(string? tab)
    {
        if (int.TryParse(tab, out var index))
            SelectedTabIndex = index;
    }

    private void ToggleSessionSelection(Session? session)
    {
        if (session == null) return;
        session.IsSelected = !session.IsSelected;
        UpdateSelectedCounts();
    }

    private void ToggleVideoSelection(VideoClip? video)
    {
        if (video == null) return;
        video.IsSelected = !video.IsSelected;
        UpdateSelectedCounts();
    }

    private void ClearSelection()
    {
        foreach (var s in TrashedSessions)
            s.IsSelected = false;
        foreach (var v in TrashedVideos)
            v.IsSelected = false;
        UpdateSelectedCounts();
    }

    private void UpdateSelectedCounts()
    {
        SelectedSessionsCount = TrashedSessions.Count(s => s.IsSelected);
        SelectedVideosCount = TrashedVideos.Count(v => v.IsSelected);
    }

    private async Task RestoreSelectedAsync()
    {
        if (!CanOperateCurrentTab)
            return;

        try
        {
            if (IsSessionsTab)
            {
                var selected = TrashedSessions.Where(s => s.IsSelected).ToList();
                foreach (var s in selected)
                    await _trashService.RestoreSessionAsync(s.Id);
            }
            else
            {
                var selected = TrashedVideos.Where(v => v.IsSelected).ToList();
                foreach (var v in selected)
                    await _trashService.RestoreVideoAsync(v.Id);
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo restaurar: {ex.Message}", "OK");
        }
    }

    private async Task DeleteSelectedPermanentlyAsync()
    {
        if (!CanOperateCurrentTab)
            return;

        var confirm = await Shell.Current.DisplayAlert(
            "Eliminar definitivamente",
            "Se eliminarán definitivamente los elementos seleccionados y sus archivos.\n\nEsta acción no se puede deshacer.",
            "Eliminar",
            "Cancelar");

        if (!confirm)
            return;

        try
        {
            if (IsSessionsTab)
            {
                var selected = TrashedSessions.Where(s => s.IsSelected).ToList();
                foreach (var s in selected)
                    await _trashService.DeleteSessionPermanentlyAsync(s.Id);
            }
            else
            {
                var selected = TrashedVideos.Where(v => v.IsSelected).ToList();
                foreach (var v in selected)
                    await _trashService.DeleteVideoPermanentlyAsync(v.Id);
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo eliminar: {ex.Message}", "OK");
        }
    }
}
