using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using System.Collections.ObjectModel;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class RightSidebarTabsAndDiary
{
    public bool IsToolsTabSelected { get; set; } = true;
    public bool IsStatsTabSelected { get; set; }
    public bool IsDiaryTabSelected { get; set; }

    public bool IsLeftPanelVisible { get; set; } = true;
    public bool IsRightPanelVisible { get; set; } = true;

    public bool ShowDetailedTimesPopup { get; set; }
    public string DetailedTimesHtml { get; set; } = string.Empty;
    public ReportOptions ReportOptions { get; set; } = ReportOptions.FullAnalysis();
    public bool ShowReportOptionsPanel { get; set; }

    public ObservableCollection<SectionWithDetailedAthleteRows> DetailedSectionTimes { get; } = new();
    public ObservableCollection<AthletePickerItem> DetailedTimesAthletes { get; } = new();

    public AthletePickerItem? SelectedDetailedTimesAthlete { get; set; }
    public bool IsAthleteDropdownExpanded { get; set; }
    public bool IsLoadingDetailedTimes { get; set; }
    public bool IsExportingDetailedTimes { get; set; }

    public SessionDiaryPanel Diary { get; } = new();
}
