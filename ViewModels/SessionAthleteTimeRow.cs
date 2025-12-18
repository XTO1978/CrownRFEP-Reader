namespace CrownRFEP_Reader.ViewModels;

public sealed class SessionAthleteTimeRow
{
    public string AthleteName { get; }
    public int VideoCount { get; }
    public double TotalSeconds { get; }
    public double AverageSeconds { get; }

    public string TotalFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(TotalSeconds);
            return ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}" : $"0:{ts.Seconds:D2}";
        }
    }

    public string AverageFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(AverageSeconds);
            return ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}" : $"0:{ts.Seconds:D2}";
        }
    }

    public SessionAthleteTimeRow(string athleteName, int videoCount, double totalSeconds, double averageSeconds)
    {
        AthleteName = athleteName;
        VideoCount = videoCount;
        TotalSeconds = totalSeconds;
        AverageSeconds = averageSeconds;
    }
}
