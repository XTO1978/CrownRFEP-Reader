using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio para generar datos estadísticos y de gráficas
/// </summary>
public class StatisticsService
{
    private readonly DatabaseService _databaseService;

    public StatisticsService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// Obtiene un resumen general de la aplicación
    /// </summary>
    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        var stats = new DashboardStats
        {
            TotalSessions = await _databaseService.GetTotalSessionsCountAsync(),
            TotalVideos = await _databaseService.GetTotalVideosCountAsync(),
            TotalAthletes = await _databaseService.GetTotalAthletesCountAsync(),
            TotalDurationSeconds = await _databaseService.GetTotalVideosDurationAsync()
        };

        // Obtener últimas sesiones
        var allSessions = await _databaseService.GetAllSessionsAsync();
        stats.RecentSessions = allSessions.Take(5).ToList();

        return stats;
    }

    /// <summary>
    /// Obtiene estadísticas de videos por atleta
    /// </summary>
    public async Task<List<AthleteVideoStats>> GetVideoStatsByAthleteAsync()
    {
        var athletes = await _databaseService.GetAllAthletesAsync();
        var stats = new List<AthleteVideoStats>();

        foreach (var athlete in athletes)
        {
            var videos = await _databaseService.GetVideoClipsByAthleteAsync(athlete.Id);
            if (videos.Any())
            {
                stats.Add(new AthleteVideoStats
                {
                    Athlete = athlete,
                    VideoCount = videos.Count,
                    TotalDuration = videos.Sum(v => v.ClipDuration),
                    AverageDuration = videos.Average(v => v.ClipDuration)
                });
            }
        }

        return stats.OrderByDescending(s => s.VideoCount).ToList();
    }

    /// <summary>
    /// Obtiene estadísticas de sesiones por mes
    /// </summary>
    public async Task<List<MonthlyStats>> GetSessionsByMonthAsync(int year)
    {
        var sessions = await _databaseService.GetAllSessionsAsync();
        
        return sessions
            .Where(s => s.FechaDateTime.Year == year)
            .GroupBy(s => s.FechaDateTime.Month)
            .Select(g => new MonthlyStats
            {
                Month = g.Key,
                MonthName = new DateTime(year, g.Key, 1).ToString("MMMM"),
                SessionCount = g.Count(),
                VideoCount = g.Sum(s => s.VideoCount)
            })
            .OrderBy(m => m.Month)
            .ToList();
    }

    /// <summary>
    /// Obtiene estadísticas de videos por sección
    /// </summary>
    public async Task<List<SectionStats>> GetVideosBySectionAsync(int sessionId)
    {
        var videos = await _databaseService.GetVideoClipsBySessionAsync(sessionId);
        
        return videos
            .GroupBy(v => v.Section)
            .Select(g => new SectionStats
            {
                Section = g.Key,
                SectionName = GetSectionName(g.Key),
                VideoCount = g.Count(),
                TotalDuration = g.Sum(v => v.ClipDuration)
            })
            .OrderBy(s => s.Section)
            .ToList();
    }

    private string GetSectionName(int section)
    {
        return section switch
        {
            1 => "Salida",
            2 => "Contrarreloj",
            3 => "Técnica",
            4 => "Remontada",
            5 => "Bajada",
            6 => "Llegada",
            7 => "Recuperación",
            8 => "Sprint",
            9 => "General",
            10 => "Calentamiento",
            11 => "Enfriamiento",
            _ => $"Sección {section}"
        };
    }
}

/// <summary>
/// Estadísticas del dashboard principal
/// </summary>
public class DashboardStats
{
    public int TotalSessions { get; set; }
    public int TotalVideos { get; set; }
    public int TotalAthletes { get; set; }
    public double TotalDurationSeconds { get; set; }
    
    public string TotalDurationFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(TotalDurationSeconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }

    public List<Session> RecentSessions { get; set; } = new();
}

/// <summary>
/// Estadísticas de videos por atleta
/// </summary>
public class AthleteVideoStats
{
    public Athlete Athlete { get; set; } = null!;
    public int VideoCount { get; set; }
    public double TotalDuration { get; set; }
    public double AverageDuration { get; set; }

    public string TotalDurationFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(TotalDuration);
            return ts.TotalMinutes >= 1 
                ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}" 
                : $"0:{ts.Seconds:D2}";
        }
    }
}

/// <summary>
/// Estadísticas mensuales
/// </summary>
public class MonthlyStats
{
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public int SessionCount { get; set; }
    public int VideoCount { get; set; }
}

/// <summary>
/// Estadísticas por sección
/// </summary>
public class SectionStats
{
    public int Section { get; set; }
    public string SectionName { get; set; } = "";
    public int VideoCount { get; set; }
    public double TotalDuration { get; set; }
}
