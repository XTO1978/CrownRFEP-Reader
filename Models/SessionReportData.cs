using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Datos agregados del resumen de un entrenamiento/sesión
/// </summary>
public class TrainingSummary
{
    /// <summary>Nombre de la sesión</summary>
    public string SessionName { get; set; } = "";
    
    /// <summary>Fecha de la sesión</summary>
    public DateTime SessionDate { get; set; }
    
    /// <summary>Lugar del entrenamiento</summary>
    public string Location { get; set; } = "";
    
    /// <summary>Entrenador responsable</summary>
    public string Coach { get; set; } = "";
    
    /// <summary>Tipo de sesión</summary>
    public string SessionType { get; set; } = "";
    
    /// <summary>Total de mangas/videos grabados</summary>
    public int TotalRuns { get; set; }
    
    /// <summary>Total de atletas participantes</summary>
    public int TotalAthletes { get; set; }
    
    /// <summary>Total de tramos</summary>
    public int TotalSections { get; set; }
    
    /// <summary>Duración total de grabaciones (segundos)</summary>
    public double TotalRecordingDuration { get; set; }
    
    /// <summary>Tamaño total de archivos (bytes)</summary>
    public long TotalFileSize { get; set; }
    
    /// <summary>Tiempo medio de todas las mangas (ms)</summary>
    public long AverageTimeMs { get; set; }
    
    /// <summary>Mejor tiempo de la sesión (ms)</summary>
    public long BestTimeMs { get; set; }
    
    /// <summary>Atleta con el mejor tiempo</summary>
    public string BestTimeAthlete { get; set; } = "";
    
    /// <summary>Número de mangas con penalización</summary>
    public int RunsWithPenalty { get; set; }
    
    // Propiedades formateadas
    public string SessionDateFormatted => SessionDate.ToString("dd/MM/yyyy HH:mm");
    public string TotalDurationFormatted => TimeSpan.FromSeconds(TotalRecordingDuration).ToString(@"h\:mm\:ss");
    public string TotalSizeFormatted => FormatFileSize(TotalFileSize);
    public string AverageTimeFormatted => FormatTime(AverageTimeMs);
    public string BestTimeFormatted => FormatTime(BestTimeMs);
    public string PenaltyRatio => TotalRuns > 0 ? $"{RunsWithPenalty}/{TotalRuns} ({(RunsWithPenalty * 100.0 / TotalRuns):0.0}%)" : "0/0";
    
    private static string FormatTime(long ms)
    {
        if (ms <= 0) return "-";
        var totalSeconds = ms / 1000;
        var centiseconds = (ms % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
    
    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Análisis de un parcial específico para un atleta
/// </summary>
public class LapAnalysisData
{
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    public int LapIndex { get; set; }
    public string LapLabel => $"P{LapIndex}";
    
    /// <summary>Tiempo del atleta en este parcial (ms)</summary>
    public long AthleteTimeMs { get; set; }
    
    /// <summary>Mejor tiempo del grupo en este parcial (ms)</summary>
    public long BestTimeMs { get; set; }
    
    /// <summary>Media del grupo en este parcial (ms)</summary>
    public long AverageTimeMs { get; set; }
    
    /// <summary>Diferencia respecto al mejor (ms, positivo = más lento)</summary>
    public long DiffToBestMs => AthleteTimeMs - BestTimeMs;
    
    /// <summary>Diferencia respecto a la media (ms, positivo = más lento)</summary>
    public long DiffToAverageMs => AthleteTimeMs - AverageTimeMs;
    
    /// <summary>Porcentaje de diferencia respecto al mejor</summary>
    public double DiffToBestPercent => BestTimeMs > 0 ? (DiffToBestMs * 100.0 / BestTimeMs) : 0;
    
    /// <summary>Porcentaje de diferencia respecto a la media</summary>
    public double DiffToAveragePercent => AverageTimeMs > 0 ? (DiffToAverageMs * 100.0 / AverageTimeMs) : 0;
    
    /// <summary>Indica si el atleta es el mejor en este parcial</summary>
    public bool IsBestInLap => AthleteTimeMs == BestTimeMs;
    
    /// <summary>Indica si está por encima de la media (más lento)</summary>
    public bool IsAboveAverage => AthleteTimeMs > AverageTimeMs;
    
    // Formateados
    public string AthleteTimeFormatted => FormatTime(AthleteTimeMs);
    public string DiffToBestFormatted => DiffToBestMs == 0 ? "—" : $"+{FormatTime(DiffToBestMs)}";
    public string DiffToBestPercentFormatted => DiffToBestMs == 0 ? "—" : $"+{DiffToBestPercent:0.1}%";
    public string DiffToAverageFormatted => DiffToAverageMs == 0 ? "—" : (DiffToAverageMs > 0 ? $"+{FormatTime(DiffToAverageMs)}" : $"-{FormatTime(Math.Abs(DiffToAverageMs))}");
    
    /// <summary>Color según si está por encima o debajo de la media</summary>
    public string DiffColor => DiffToAverageMs < 0 ? "#4CAF50" : (DiffToAverageMs > 0 ? "#F44336" : "#9E9E9E");
    
    private static string FormatTime(long ms)
    {
        var totalSeconds = Math.Abs(ms) / 1000;
        var centiseconds = (Math.Abs(ms) % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
}

/// <summary>
/// Perfil de análisis de parciales para un atleta
/// </summary>
public class AthleteLapProfile
{
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    
    /// <summary>Análisis de cada parcial</summary>
    public List<LapAnalysisData> LapAnalysis { get; set; } = new();
    
    /// <summary>Parcial donde más pierde respecto al mejor</summary>
    public int WorstLapIndex => LapAnalysis.Count > 0 
        ? LapAnalysis.OrderByDescending(l => l.DiffToBestPercent).First().LapIndex 
        : 0;
    
    /// <summary>Parcial donde mejor rinde (menos diferencia o mejor que media)</summary>
    public int BestLapIndex => LapAnalysis.Count > 0 
        ? LapAnalysis.OrderBy(l => l.DiffToBestPercent).First().LapIndex 
        : 0;
    
    /// <summary>Tiempo total perdido respecto al mejor en todos los parciales</summary>
    public long TotalTimeLostMs => LapAnalysis.Sum(l => l.DiffToBestMs);
    
    public string TotalTimeLostFormatted => TotalTimeLostMs > 0 ? $"+{FormatTime(TotalTimeLostMs)}" : "—";
    
    private static string FormatTime(long ms)
    {
        var totalSeconds = ms / 1000;
        var centiseconds = (ms % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
}

/// <summary>
/// Posición de un atleta en el ranking
/// </summary>
public class RankingEntry
{
    public int Position { get; set; }
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int VideoId { get; set; }
    public int AttemptNumber { get; set; }
    public bool IsBestAttempt { get; set; }
    
    /// <summary>Tiempo total (ms)</summary>
    public long TotalTimeMs { get; set; }
    
    /// <summary>Diferencia respecto al primero (ms)</summary>
    public long DiffToFirstMs { get; set; }
    
    /// <summary>Diferencia respecto a la mediana (ms)</summary>
    public long DiffToMedianMs { get; set; }
    
    /// <summary>Porcentaje respecto al primero</summary>
    public double DiffToFirstPercent { get; set; }
    
    /// <summary>Indica si es el líder</summary>
    public bool IsLeader => Position == 1;
    
    /// <summary>Indica si está por encima de la mediana (peor)</summary>
    public bool IsBelowMedian => DiffToMedianMs > 0;
    
    // Formateados
    public string PositionFormatted => $"#{Position}";
    public string TotalTimeFormatted => FormatTime(TotalTimeMs);
    public string DiffToFirstFormatted => DiffToFirstMs == 0 ? "—" : $"+{FormatTime(DiffToFirstMs)}";
    public string DiffToFirstPercentFormatted => DiffToFirstMs == 0 ? "—" : $"+{DiffToFirstPercent:0.1}%";
    public string DiffToMedianFormatted => DiffToMedianMs == 0 ? "—" : (DiffToMedianMs > 0 ? $"+{FormatTime(DiffToMedianMs)}" : $"-{FormatTime(Math.Abs(DiffToMedianMs))}");
    
    public string DisplayName => AttemptNumber > 1 ? $"{AthleteName} ({AttemptNumber})" : AthleteName;
    
    private static string FormatTime(long ms)
    {
        var totalSeconds = Math.Abs(ms) / 1000;
        var centiseconds = (Math.Abs(ms) % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
}

/// <summary>
/// Análisis de penalizaciones para un atleta
/// </summary>
public class PenaltyAnalysis
{
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    
    /// <summary>Total de mangas del atleta</summary>
    public int TotalRuns { get; set; }
    
    /// <summary>Mangas con penalización</summary>
    public int RunsWithPenalty { get; set; }
    
    /// <summary>Tiempo total penalizado (ms)</summary>
    public long TotalPenaltyMs { get; set; }
    
    /// <summary>Ratio de mangas con penalización</summary>
    public double PenaltyRatio => TotalRuns > 0 ? (RunsWithPenalty * 100.0 / TotalRuns) : 0;
    
    /// <summary>Posiciones perdidas por penalizaciones (estimado)</summary>
    public int EstimatedPositionsLost { get; set; }
    
    // Formateados
    public string PenaltyRatioFormatted => $"{RunsWithPenalty}/{TotalRuns} ({PenaltyRatio:0.0}%)";
    public string TotalPenaltyFormatted => TotalPenaltyMs > 0 ? $"+{TotalPenaltyMs / 1000}s" : "—";
}

/// <summary>
/// Perfil completo de un atleta para el informe
/// </summary>
public class AthleteReportProfile
{
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    
    /// <summary>Total de mangas grabadas</summary>
    public int TotalRuns { get; set; }
    
    /// <summary>Mejor tiempo personal en esta sesión (ms)</summary>
    public long PersonalBestMs { get; set; }
    
    /// <summary>Tiempo medio del atleta (ms)</summary>
    public long AverageTimeMs { get; set; }
    
    /// <summary>Posición en el ranking general</summary>
    public int BestPosition { get; set; }
    
    /// <summary>Análisis de parciales</summary>
    public AthleteLapProfile? LapProfile { get; set; }
    
    /// <summary>Análisis de penalizaciones</summary>
    public PenaltyAnalysis? PenaltyAnalysis { get; set; }
    
    /// <summary>Métricas de consistencia individual</summary>
    public AthleteConsistencyMetrics? ConsistencyMetrics { get; set; }
    
    /// <summary>Historial de tiempos en esta sesión</summary>
    public List<RankingEntry> RunHistory { get; set; } = new();
    
    // Formateados
    public string PersonalBestFormatted => FormatTime(PersonalBestMs);
    public string AverageTimeFormatted => FormatTime(AverageTimeMs);
    public string BestPositionFormatted => BestPosition > 0 ? $"#{BestPosition}" : "—";
    
    private static string FormatTime(long ms)
    {
        if (ms <= 0) return "—";
        var totalSeconds = ms / 1000;
        var centiseconds = (ms % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
}

/// <summary>
/// Contenedor completo de datos para el informe de sesión
/// </summary>
public class SessionReportData
{
    /// <summary>ID de la sesión</summary>
    public int SessionId { get; set; }
    
    /// <summary>Resumen del entrenamiento</summary>
    public TrainingSummary Summary { get; set; } = new();
    
    /// <summary>Datos por sección/tramo</summary>
    public List<SectionReportData> Sections { get; set; } = new();
    
    /// <summary>Ranking general (todos los tramos combinados o primer tramo)</summary>
    public List<RankingEntry> GeneralRanking { get; set; } = new();
    
    /// <summary>Perfiles de atletas</summary>
    public List<AthleteReportProfile> AthleteProfiles { get; set; } = new();
    
    /// <summary>Análisis de penalizaciones por atleta</summary>
    public List<PenaltyAnalysis> PenaltyAnalysis { get; set; } = new();
    
    /// <summary>Atleta de referencia seleccionado (si hay)</summary>
    public int? ReferenceAthleteId { get; set; }
    
    /// <summary>VideoId de referencia (intento específico)</summary>
    public int? ReferenceVideoId { get; set; }
}

/// <summary>
/// Datos de informe para una sección/tramo específico
/// </summary>
public class SectionReportData
{
    public int SectionId { get; set; }
    public string SectionName { get; set; } = "";
    
    /// <summary>Datos detallados de atletas (para tablas)</summary>
    public List<AthleteDetailedTimeRow> Athletes { get; set; } = new();
    
    /// <summary>Métricas de consistencia del grupo</summary>
    public ConsistencyMetrics? GroupConsistency { get; set; }
    
    /// <summary>Métricas de consistencia individual</summary>
    public List<AthleteConsistencyMetrics> IndividualConsistency { get; set; } = new();
    
    /// <summary>Ranking de esta sección</summary>
    public List<RankingEntry> Ranking { get; set; } = new();
    
    /// <summary>Análisis de parciales por atleta</summary>
    public List<AthleteLapProfile> LapProfiles { get; set; } = new();
    
    /// <summary>Número máximo de parciales en esta sección</summary>
    public int MaxLaps => Athletes.Count > 0 ? Athletes.Max(a => a.LapCount) : 0;
}
