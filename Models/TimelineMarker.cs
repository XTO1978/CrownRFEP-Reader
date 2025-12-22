namespace CrownRFEP_Reader.Models;

public class TimelineMarker
{
    public double Position { get; set; }
    public long TimestampMs { get; set; }
    public string? Label { get; set; }
}
