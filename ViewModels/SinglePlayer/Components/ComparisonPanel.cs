using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class ComparisonPanel
{
    public bool ShowComparisonPanel { get; set; }
    public ComparisonLayout ComparisonLayout { get; set; } = ComparisonLayout.Single;

    public VideoClip? ComparisonVideo2 { get; set; }
    public VideoClip? ComparisonVideo3 { get; set; }
    public VideoClip? ComparisonVideo4 { get; set; }

    public int SelectedComparisonSlot { get; set; }

    public bool IsComparisonExporting { get; set; }
    public double ComparisonExportProgress { get; set; }
    public string ComparisonExportStatus { get; set; } = string.Empty;

    public bool IsComparisonLapSyncEnabled { get; set; }
}
