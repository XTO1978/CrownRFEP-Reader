using CrownRFEP_Reader.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class TagEventsTimeline
{
    public bool ShowTagEventsPanel { get; set; }

    public ObservableCollection<EventTagDefinition> AllEventTags { get; } = new();
    public ObservableCollection<TagEvent> TagEvents { get; } = new();
    public ObservableCollection<TimelineMarker> TimelineMarkers { get; } = new();

    public EventTagDefinition? SelectedEventTagToAdd { get; set; }
    public string NewEventName { get; set; } = string.Empty;

    public bool HasTagEvents => TagEvents.Count > 0;
    public string TagEventsCountText => TagEvents.Count == 1 ? "1 evento" : $"{TagEvents.Count} eventos";

    public void SetAllEventTags(IEnumerable<EventTagDefinition> tags)
    {
        AllEventTags.Clear();
        if (tags == null)
            return;

        foreach (var tag in tags)
            AllEventTags.Add(tag);
    }

    public void SetTagEvents(IEnumerable<TagEvent> events)
    {
        TagEvents.Clear();
        if (events == null)
            return;

        foreach (var evt in events)
            TagEvents.Add(evt);
    }

    public void SetTimelineMarkers(IEnumerable<TimelineMarker> markers)
    {
        TimelineMarkers.Clear();
        if (markers == null)
            return;

        foreach (var marker in markers)
            TimelineMarkers.Add(marker);
    }

    public void ClearSelection()
    {
        if (SelectedEventTagToAdd != null)
            SelectedEventTagToAdd.IsSelected = false;

        SelectedEventTagToAdd = null;

        foreach (var tag in AllEventTags)
            tag.IsSelected = false;
    }

    public void RefreshTimelineMarkers(TimeSpan duration)
    {
        var durationMs = duration.TotalMilliseconds;
        if (durationMs <= 0 || TagEvents.Count == 0)
        {
            if (TimelineMarkers.Count > 0)
                TimelineMarkers.Clear();
            return;
        }

        var markers = TagEvents
            .Where(e => e.TimestampMs >= 0)
            .Select(e => new TimelineMarker
            {
                Position = Math.Clamp(e.TimestampMs / durationMs, 0.0, 1.0),
                TimestampMs = e.TimestampMs,
                Label = e.TagName
            })
            .ToList();

        TimelineMarkers.Clear();
        foreach (var marker in markers)
            TimelineMarkers.Add(marker);
    }
}
