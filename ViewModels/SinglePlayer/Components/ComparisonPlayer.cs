using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;
using Microsoft.Maui.Graphics;
using System.Collections.Generic;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class ComparisonPlayer
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

    public List<LapSegment>? LapSegments1 { get; set; }
    public List<LapSegment>? LapSegments2 { get; set; }
    public List<LapSegment>? LapSegments3 { get; set; }
    public List<LapSegment>? LapSegments4 { get; set; }

    public TimeSpan CurrentPosition2 { get; set; }
    public TimeSpan CurrentPosition3 { get; set; }
    public TimeSpan CurrentPosition4 { get; set; }

    public TimeSpan Duration2 { get; set; }
    public TimeSpan Duration3 { get; set; }
    public TimeSpan Duration4 { get; set; }

    public bool IsPlaying2 { get; set; }
    public bool IsPlaying3 { get; set; }
    public bool IsPlaying4 { get; set; }

    public bool HasLapTiming { get; set; }
    public string CurrentLapText1 { get; set; } = string.Empty;
    public string CurrentLapText2 { get; set; } = string.Empty;
    public string CurrentLapText3 { get; set; } = string.Empty;
    public string CurrentLapText4 { get; set; } = string.Empty;
    public string CurrentLapDiffText { get; set; } = string.Empty;

    public Color CurrentLapColor1 { get; set; } = Colors.White;
    public Color CurrentLapColor2 { get; set; } = Colors.White;
    public Color CurrentLapColor3 { get; set; } = Colors.White;
    public Color CurrentLapColor4 { get; set; } = Colors.White;
    public Color CurrentLapBgColor1 { get; set; } = Color.FromArgb("#E6000000");
    public Color CurrentLapBgColor2 { get; set; } = Color.FromArgb("#E6000000");
    public Color CurrentLapBgColor3 { get; set; } = Color.FromArgb("#E6000000");
    public Color CurrentLapBgColor4 { get; set; } = Color.FromArgb("#E6000000");

    public int CurrentLapIndex1 { get; set; }
    public int CurrentLapIndex2 { get; set; }
    public int CurrentLapIndex3 { get; set; }
    public int CurrentLapIndex4 { get; set; }
    public bool WaitingAtLapBoundary1 { get; set; }
    public bool WaitingAtLapBoundary2 { get; set; }
    public bool WaitingAtLapBoundary3 { get; set; }
    public bool WaitingAtLapBoundary4 { get; set; }
    public bool IsProcessingLapSync { get; set; }

    public TimeSpan ComparisonPosition2 { get; set; }
    public TimeSpan ComparisonPosition3 { get; set; }
    public TimeSpan ComparisonPosition4 { get; set; }

    public bool HasSyncBaseline { get; set; }
    public DateTime LastSyncPlayUtc { get; set; }
    public TimeSpan SyncBaselineMain { get; set; }
    public TimeSpan SyncBaseline2 { get; set; }
    public TimeSpan SyncBaseline3 { get; set; }
    public TimeSpan SyncBaseline4 { get; set; }
}
