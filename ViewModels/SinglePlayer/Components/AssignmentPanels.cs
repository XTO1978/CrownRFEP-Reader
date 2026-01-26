using CrownRFEP_Reader.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class AssignmentPanels
{
    public bool ShowAthleteAssignPanel { get; set; }
    public ObservableCollection<Athlete> AllAthletes { get; } = new();
    public Athlete? SelectedAthleteToAssign { get; set; }
    public string NewAthleteName { get; set; } = "";
    public string NewAthleteSurname { get; set; } = "";

    public bool ShowSectionAssignPanel { get; set; }
    public int SectionToAssign { get; set; } = 1;

    public bool ShowTagsAssignPanel { get; set; }
    public ObservableCollection<Tag> AllTags { get; } = new();
    public ObservableCollection<Tag> SelectedTags { get; } = new();
    public string NewTagName { get; set; } = "";

    public void SetAllTags(IEnumerable<Tag> tags)
    {
        AllTags.Clear();
        foreach (var tag in tags)
            AllTags.Add(tag);
    }

    public void SetAllAthletes(IEnumerable<Athlete> athletes)
    {
        AllAthletes.Clear();
        foreach (var athlete in athletes)
            AllAthletes.Add(athlete);
    }

    public void SetSelectedTags(IEnumerable<Tag> tags)
    {
        SelectedTags.Clear();
        foreach (var tag in tags)
            SelectedTags.Add(tag);
    }

    public void SyncTagSelectionFlags()
    {
        foreach (var tag in AllTags)
        {
            tag.IsSelected = SelectedTags.Any(t => t.Id == tag.Id) ? 1 : 0;
        }
    }
}
