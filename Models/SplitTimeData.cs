namespace CrownRFEP_Reader.Models;

/// <summary>
/// Datos del split time serializados en JSON
/// </summary>
public class SplitTimeData
{
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public long DurationMs { get; set; }
    
    /// <summary>
    /// Formatea la duración en formato legible (mm:ss.fff)
    /// </summary>
    public string DurationFormatted => FormatTime(DurationMs);
    
    /// <summary>
    /// Formatea el tiempo de inicio en formato legible
    /// </summary>
    public string StartFormatted => FormatTime(StartMs);
    
    /// <summary>
    /// Formatea el tiempo de fin en formato legible
    /// </summary>
    public string EndFormatted => FormatTime(EndMs);
    
    private static string FormatTime(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
