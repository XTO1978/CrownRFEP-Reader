using CrownRFEP_Reader.Models;
using System.Collections.ObjectModel;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class AssistedLapPanel
{
    public bool IsAssistedModeEnabled { get; set; }
    public AssistedLapState AssistedLapState { get; set; } = AssistedLapState.Configuring;

    public ObservableCollection<AssistedLapDefinition> AssistedLaps { get; } = new();

    public int AssistedLapCount { get; set; } = 3;
    public int CurrentAssistedLapIndex { get; set; }

    public ObservableCollection<LapConfigHistory> RecentLapConfigs { get; } = new();
}
