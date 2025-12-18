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
    private int _selectedCount;

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

    public int SelectedCount
    {
        get => _selectedCount;
        set => SetProperty(ref _selectedCount, value);
    }

    public bool CanMerge => SelectedCount >= 2;

    public ICommand RefreshCommand { get; }
    public ICommand ViewAthleteCommand { get; }
    public ICommand ConsolidateDuplicatesCommand { get; }
    public ICommand MergeSelectedAthletesCommand { get; }
    public ICommand ToggleAthleteSelectionCommand { get; }

    public AthletesViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        Title = "Atletas";

        RefreshCommand = new AsyncRelayCommand(LoadAthletesAsync);
        ViewAthleteCommand = new AsyncRelayCommand<Athlete>(ViewAthleteAsync);
        ConsolidateDuplicatesCommand = new AsyncRelayCommand(ConsolidateDuplicatesAsync);
        MergeSelectedAthletesCommand = new AsyncRelayCommand(MergeSelectedAthletesAsync);
        ToggleAthleteSelectionCommand = new Command<Athlete>(ToggleAthleteSelection);
    }

    private void ToggleAthleteSelection(Athlete? athlete)
    {
        if (athlete == null) return;
        athlete.IsSelected = !athlete.IsSelected;
        UpdateSelectedCount();
    }

    public void UpdateSelectedCount()
    {
        SelectedCount = Athletes.Count(a => a.IsSelected);
        OnPropertyChanged(nameof(CanMerge));
    }

    private async Task MergeSelectedAthletesAsync()
    {
        var selectedAthletes = Athletes.Where(a => a.IsSelected).OrderBy(a => a.Id).ToList();
        
        if (selectedAthletes.Count < 2)
        {
            await Shell.Current.DisplayAlert("Aviso", "Selecciona al menos 2 atletas para fusionar.", "OK");
            return;
        }

        // Crear lista de opciones con los nombres de los atletas seleccionados
        var options = selectedAthletes
            .Select(a => $"{a.Nombre} {a.Apellido}".Trim())
            .Distinct()
            .ToArray();

        var chosenName = await Shell.Current.DisplayActionSheet(
            "¿Qué nombre quieres usar para este atleta?",
            "Cancelar",
            null,
            options);

        if (string.IsNullOrEmpty(chosenName) || chosenName == "Cancelar")
            return;

        // Separar nombre y apellido (el usuario eligió "Nombre Apellido")
        var parts = chosenName.Split(' ', 2);
        var nombre = parts.Length > 0 ? parts[0] : "";
        var apellido = parts.Length > 1 ? parts[1] : "";

        try
        {
            IsBusy = true;

            // El primer atleta seleccionado será el que mantengamos
            var keepAthlete = selectedAthletes.First();
            
            // Actualizar el nombre del atleta que mantenemos
            keepAthlete.Nombre = nombre;
            keepAthlete.Apellido = apellido;
            await _databaseService.SaveAthleteAsync(keepAthlete);

            // Fusionar los demás en este
            var duplicatesToMerge = selectedAthletes.Skip(1).ToList();
            var merged = await _databaseService.MergeAthletesAsync(keepAthlete.Id, duplicatesToMerge.Select(a => a.Id).ToList());

            await Shell.Current.DisplayAlert("Fusión completada", 
                $"Se han fusionado {merged + 1} atletas bajo el nombre '{chosenName}'.", "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al fusionar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }

        // Recargar la lista
        await LoadAthletesAsync();
    }

    public async Task LoadAthletesAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            // Consolidar duplicados automáticamente antes de cargar
            var duplicatesRemoved = await _databaseService.ConsolidateDuplicateAthletesAsync();
            if (duplicatesRemoved > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Atletas duplicados consolidados: {duplicatesRemoved}");
            }

            var athletes = await _databaseService.GetAllAthletesAsync();

            // Ordenar por ID local (el ID autogenerado por la aplicación)
            var orderedAthletes = athletes.OrderBy(a => a.Id).ToList();

            Athletes.Clear();
            FilteredAthletes.Clear();

            foreach (var athlete in orderedAthletes)
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

    private async Task ConsolidateDuplicatesAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var duplicatesRemoved = await _databaseService.ConsolidateDuplicateAthletesAsync();
            
            if (duplicatesRemoved > 0)
            {
                await Shell.Current.DisplayAlert("Consolidación", 
                    $"Se han consolidado {duplicatesRemoved} atletas duplicados.", "OK");
            }
            else
            {
                await Shell.Current.DisplayAlert("Consolidación", 
                    "No se encontraron atletas duplicados.", "OK");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Error al consolidar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }

        // Recargar la lista
        await LoadAthletesAsync();
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
