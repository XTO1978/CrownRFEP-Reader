using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para la página de estadísticas y gráficas
/// </summary>
public class StatisticsViewModel : BaseViewModel
{
    private readonly StatisticsService _statisticsService;
    private int _selectedYear;

    public ObservableCollection<AthleteVideoStats> AthleteStats { get; } = new();
    public ObservableCollection<MonthlyStats> MonthlyStats { get; } = new();
    public ObservableCollection<int> AvailableYears { get; } = new();

    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (SetProperty(ref _selectedYear, value))
            {
                _ = LoadMonthlyStatsAsync();
            }
        }
    }

    public ICommand RefreshCommand { get; }

    public StatisticsViewModel(StatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
        Title = "Estadísticas";

        // Inicializar años disponibles
        var currentYear = DateTime.Now.Year;
        for (int i = currentYear; i >= currentYear - 5; i--)
        {
            AvailableYears.Add(i);
        }
        _selectedYear = currentYear;

        RefreshCommand = new AsyncRelayCommand(LoadAllStatsAsync);
    }

    public async Task LoadAllStatsAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            await LoadAthleteStatsAsync();
            await LoadMonthlyStatsAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar las estadísticas: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAthleteStatsAsync()
    {
        var stats = await _statisticsService.GetVideoStatsByAthleteAsync();
        AthleteStats.Clear();
        foreach (var stat in stats)
        {
            AthleteStats.Add(stat);
        }
    }

    private async Task LoadMonthlyStatsAsync()
    {
        var stats = await _statisticsService.GetSessionsByMonthAsync(SelectedYear);
        MonthlyStats.Clear();
        foreach (var stat in stats)
        {
            MonthlyStats.Add(stat);
        }
    }
}
