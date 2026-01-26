namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed record LapSegment(int LapNumber, TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}
