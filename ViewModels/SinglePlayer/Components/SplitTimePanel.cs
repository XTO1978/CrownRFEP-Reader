using CrownRFEP_Reader.Models;
using System.Collections.ObjectModel;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class SplitTimePanel
{
    public bool ShowSplitTimePanel { get; set; }

    public TimeSpan? SplitStartTime { get; set; }
    public TimeSpan? SplitEndTime { get; set; }
    public TimeSpan? SplitDuration { get; private set; }

    public bool HasSavedSplit { get; set; }

    public ObservableCollection<ExecutionTimingRow> SplitLapRows { get; } = new();
    public List<long> SplitLapMarksMs { get; } = new();

    public bool HasSplitLaps { get; private set; }

    public bool HasSplitStart => SplitStartTime.HasValue;
    public bool HasSplitEnd => SplitEndTime.HasValue;
    public bool HasSplitDuration => SplitDuration.HasValue;

    public bool CanSaveSplit => SplitStartTime.HasValue && SplitEndTime.HasValue &&
                                SplitDuration.HasValue && SplitDuration.Value.TotalMilliseconds > 0;

    public string SplitStartTimeText => SplitStartTime.HasValue ? $"{SplitStartTime.Value:mm\\:ss\\.ff}" : "--:--:--";
    public string SplitEndTimeText => SplitEndTime.HasValue ? $"{SplitEndTime.Value:mm\\:ss\\.ff}" : "--:--:--";
    public string SplitDurationText => SplitDuration.HasValue ? $"{SplitDuration.Value:mm\\:ss\\.fff}" : "--:--:---";

    public void CalculateSplitDuration()
    {
        if (SplitStartTime.HasValue && SplitEndTime.HasValue)
        {
            var duration = SplitEndTime.Value - SplitStartTime.Value;
            SplitDuration = duration.TotalMilliseconds >= 0 ? duration : TimeSpan.Zero;
        }
        else
        {
            SplitDuration = null;
        }
    }

    public void SetSplitDuration(TimeSpan? duration)
    {
        SplitDuration = duration;
    }

    public void SetHasSplitLaps(bool hasSplitLaps)
    {
        HasSplitLaps = hasSplitLaps;
    }

    public void ClearSplit()
    {
        SplitStartTime = null;
        SplitEndTime = null;
        SplitDuration = null;
        SplitLapMarksMs.Clear();
        SplitLapRows.Clear();
        HasSplitLaps = false;
        HasSavedSplit = false;
    }

    public void FilterLapMarksToRange()
    {
        if (!SplitStartTime.HasValue)
        {
            SplitLapMarksMs.Clear();
            return;
        }

        var startMs = (long)SplitStartTime.Value.TotalMilliseconds;
        long? endMs = SplitEndTime.HasValue ? (long)SplitEndTime.Value.TotalMilliseconds : null;

        SplitLapMarksMs.RemoveAll(ms => ms < startMs || (endMs.HasValue && ms > endMs.Value));
    }

    public void RebuildSplitLapRows()
    {
        SplitLapMarksMs.Sort();
        SplitLapRows.Clear();

        if (!SplitStartTime.HasValue)
        {
            HasSplitLaps = false;
            return;
        }

        var prev = (long)SplitStartTime.Value.TotalMilliseconds;
        var lapIndex = 0;
        foreach (var mark in SplitLapMarksMs)
        {
            lapIndex++;
            var splitMs = mark - prev;
            if (splitMs < 0) splitMs = 0;
            prev = mark;

            SplitLapRows.Add(new ExecutionTimingRow
            {
                Title = $"Lap {lapIndex}",
                Value = FormatMs(splitMs),
                IsTotal = false
            });
        }

        HasSplitLaps = SplitLapRows.Count > 0;
    }

    public void RebuildSplitLapRowsWithNames(IReadOnlyList<AssistedLapDefinition> assistedLaps)
    {
        SplitLapMarksMs.Sort();
        SplitLapRows.Clear();

        if (!SplitStartTime.HasValue)
        {
            HasSplitLaps = false;
            return;
        }

        var prev = (long)SplitStartTime.Value.TotalMilliseconds;

        for (int i = 0; i < SplitLapMarksMs.Count; i++)
        {
            var mark = SplitLapMarksMs[i];
            var splitMs = mark - prev;
            if (splitMs < 0) splitMs = 0;
            prev = mark;

            var lapName = (i < assistedLaps.Count) ? assistedLaps[i].DisplayName : $"Lap {i + 1}";

            SplitLapRows.Add(new ExecutionTimingRow
            {
                Title = lapName,
                Value = FormatMs(splitMs),
                IsTotal = false
            });
        }

        if (SplitEndTime.HasValue)
        {
            var endMs = (long)SplitEndTime.Value.TotalMilliseconds;
            var lastSegmentMs = endMs - prev;
            if (lastSegmentMs < 0) lastSegmentMs = 0;

            var finLapIndex = assistedLaps.Count - 1;
            var finLapName = (finLapIndex >= 0 && finLapIndex < assistedLaps.Count)
                ? assistedLaps[finLapIndex].DisplayName
                : "Fin";

            SplitLapRows.Add(new ExecutionTimingRow
            {
                Title = finLapName,
                Value = FormatMs(lastSegmentMs),
                IsTotal = false
            });
        }

        HasSplitLaps = SplitLapRows.Count > 0;
    }

    private static string FormatMs(long ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }
}
