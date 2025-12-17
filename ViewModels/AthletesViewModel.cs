using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para la lista de atletas
/// </summary>
public class AthletesViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private string _searchText = "";
    private Athlete? _selectedAthlete;

    public ObservableCollection<Athlete> Athletes { get; } = new();
    public ObservableCollection<Athlete> FilteredAthletes { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                FilterAthletes();
            }
        }
    }

    public Athlete? SelectedAthlete
    {
        get => _selectedAthlete;
        set => SetProperty(ref _selectedAthlete, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ViewAthleteCommand { get; }

    public AthletesViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        Title = "Atletas";

        RefreshCommand = new AsyncRelayCommand(LoadAthletesAsync);
        ViewAthleteCommand = new AsyncRelayCommand<Athlete>(ViewAthleteAsync);
    }

    public async Task LoadAthletesAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var athletes = await _databaseService.GetAllAthletesAsync();

            Athletes.Clear();
            FilteredAthletes.Clear();

            foreach (var athlete in athletes)
            {
                Athletes.Add(athlete);
                FilteredAthletes.Add(athlete);
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar los atletas: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void FilterAthletes()
    {
        FilteredAthletes.Clear();

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? Athletes
            : Athletes.Where(a =>
                (a.NombreCompleto?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.CategoriaNombre?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var athlete in filtered)
        {
            FilteredAthletes.Add(athlete);
        }
    }

    private async Task ViewAthleteAsync(Athlete? athlete)
    {
        if (athlete == null) return;
        await Shell.Current.GoToAsync($"{nameof(AthleteDetailPage)}?athleteId={athlete.Id}");
    }
}
