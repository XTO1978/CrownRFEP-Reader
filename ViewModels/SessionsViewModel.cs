using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para la lista de sesiones
/// </summary>
public class SessionsViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private string _searchText = "";
    private Session? _selectedSession;

    public ObservableCollection<Session> Sessions { get; } = new();
    public ObservableCollection<Session> FilteredSessions { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                FilterSessions();
            }
        }
    }

    public Session? SelectedSession
    {
        get => _selectedSession;
        set => SetProperty(ref _selectedSession, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ViewSessionCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand ViewVideoLessonsCommand { get; }

    public SessionsViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        Title = "Sesiones";

        RefreshCommand = new AsyncRelayCommand(LoadSessionsAsync);
        ViewSessionCommand = new AsyncRelayCommand<Session>(ViewSessionAsync);
        DeleteSessionCommand = new AsyncRelayCommand<Session>(DeleteSessionAsync);
        ViewVideoLessonsCommand = new AsyncRelayCommand(ViewVideoLessonsAsync);
    }

    private static async Task ViewVideoLessonsAsync()
    {
        await Shell.Current.GoToAsync(nameof(VideoLessonsPage));
    }

    public async Task LoadSessionsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var sessions = await _databaseService.GetAllSessionsAsync();

            Sessions.Clear();
            FilteredSessions.Clear();

            foreach (var session in sessions)
            {
                Sessions.Add(session);
                FilteredSessions.Add(session);
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar las sesiones: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void FilterSessions()
    {
        FilteredSessions.Clear();

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? Sessions
            : Sessions.Where(s =>
                (s.NombreSesion?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.Lugar?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.Coach?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (s.Participantes?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var session in filtered)
        {
            FilteredSessions.Add(session);
        }
    }

    private async Task ViewSessionAsync(Session? session)
    {
        if (session == null) return;
        await Shell.Current.GoToAsync($"{nameof(SessionDetailPage)}?sessionId={session.Id}");
    }

    private async Task DeleteSessionAsync(Session? session)
    {
        if (session == null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Confirmar Eliminación",
            $"¿Estás seguro de que deseas eliminar la sesión '{session.DisplayName}'?\nEsta acción no se puede deshacer.",
            "Eliminar",
            "Cancelar");

        if (!confirm) return;

        try
        {
            await _databaseService.DeleteSessionAsync(session);
            Sessions.Remove(session);
            FilteredSessions.Remove(session);
            await Shell.Current.DisplayAlert("Éxito", "Sesión eliminada correctamente", "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo eliminar la sesión: {ex.Message}", "OK");
        }
    }
}
