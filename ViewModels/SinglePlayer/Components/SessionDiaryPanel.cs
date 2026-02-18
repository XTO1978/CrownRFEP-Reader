using CrownRFEP_Reader.Models;
using System.Collections.ObjectModel;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class SessionDiaryPanel
{
    public SessionDiary? CurrentSessionDiary { get; set; }
    public bool IsEditingDiary { get; set; }

    public int DiaryValoracionFisica { get; set; } = 3;
    public int DiaryValoracionMental { get; set; } = 3;
    public int DiaryValoracionTecnica { get; set; } = 3;
    public string DiaryNotas { get; set; } = string.Empty;

    public double AvgValoracionFisica { get; set; }
    public double AvgValoracionMental { get; set; }
    public double AvgValoracionTecnica { get; set; }
    public int AvgValoracionCount { get; set; }

    public int SelectedEvolutionPeriod { get; set; } = 1; // 0=Semana, 1=Mes, 2=Año, 3=Todo

    public ObservableCollection<SessionDiary> ValoracionEvolution { get; } = new();
    public ObservableCollection<int> EvolutionFisicaValues { get; } = new();
    public ObservableCollection<int> EvolutionMentalValues { get; } = new();
    public ObservableCollection<int> EvolutionTecnicaValues { get; } = new();
    public ObservableCollection<string> EvolutionLabels { get; } = new();
}
