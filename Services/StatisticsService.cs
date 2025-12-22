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

    /// <summary>
    /// Obtiene estadísticas absolutas de tags y etiquetas
    /// </summary>
    public async Task<AbsoluteTagStats> GetAbsoluteTagStatsAsync(int? sessionId = null)
    {
        List<Input> inputs;
        List<VideoClip> videos;
        int sessionCount;

        if (sessionId.HasValue)
        {
            inputs = await _databaseService.GetInputsBySessionAsync(sessionId.Value);
            videos = await _databaseService.GetVideoClipsBySessionAsync(sessionId.Value);
            sessionCount = 1;
        }
        else
        {
            inputs = await _databaseService.GetAllInputsAsync();
            videos = await _databaseService.GetAllVideoClipsAsync();
            sessionCount = await _databaseService.GetTotalSessionsCountAsync();
        }

        var tags = await _databaseService.GetAllTagsAsync();
        var tagById = tags.ToDictionary(t => t.Id, t => t.NombreTag ?? $"Tag {t.Id}");

        // Separar eventos y etiquetas
        var eventInputs = inputs.Where(i => i.IsEvent == 1).ToList();
        var labelInputs = inputs.Where(i => i.IsEvent == 0).ToList();

        // Estadísticas de eventos
        var eventStats = eventInputs
            .GroupBy(i => i.InputTypeId)
            .Select(g => new TagUsageRow
            {
                TagId = g.Key,
                TagName = tagById.GetValueOrDefault(g.Key, $"Tag {g.Key}"),
                UsageCount = g.Count()
            })
            .OrderByDescending(t => t.UsageCount)
            .ToList();

        // Estadísticas de etiquetas
        var labelStats = labelInputs
            .GroupBy(i => i.InputTypeId)
            .Select(g => new TagUsageRow
            {
                TagId = g.Key,
                TagName = tagById.GetValueOrDefault(g.Key, $"Tag {g.Key}"),
                UsageCount = g.Count()
            })
            .OrderByDescending(t => t.UsageCount)
            .ToList();

        // Vídeos con nombre asignado (ComparisonName no vacío)
        var labeledVideos = videos.Count(v => !string.IsNullOrWhiteSpace(v.ComparisonName));

        // Media de tags por sesión
        var avgTagsPerSession = sessionCount > 0 
            ? (double)(eventInputs.Count + labelInputs.Count) / sessionCount 
            : 0;

        return new AbsoluteTagStats
        {
            TotalEventTags = eventInputs.Count,
            UniqueEventTags = eventStats.Count,
            TotalLabelTags = labelInputs.Count,
            UniqueLabelTags = labelStats.Count,
            LabeledVideos = labeledVideos,
            TotalVideos = videos.Count,
            AvgTagsPerSession = avgTagsPerSession,
            TopEventTags = eventStats.Take(5).ToList(),
            TopLabelTags = labelStats.Take(5).ToList()
        };
    }

    /// <summary>
    /// Obtiene estadísticas del atleta de referencia (usuario)
    /// </summary>
    public async Task<UserAthleteStats?> GetUserAthleteStatsAsync(int? referenceAthleteId, int? sessionId = null)
    {
        if (!referenceAthleteId.HasValue)
            return null;

        var athletes = await _databaseService.GetAllAthletesAsync();
        var athlete = athletes.FirstOrDefault(a => a.Id == referenceAthleteId.Value);
        if (athlete == null)
            return null;

        // Obtener todos los videos del atleta de referencia
        List<VideoClip> userVideos;
        List<VideoClip> allVideos;

        if (sessionId.HasValue)
        {
            allVideos = await _databaseService.GetVideoClipsBySessionAsync(sessionId.Value);
            userVideos = allVideos.Where(v => v.AtletaId == referenceAthleteId.Value).ToList();
        }
        else
        {
            allVideos = await _databaseService.GetAllVideoClipsAsync();
            userVideos = allVideos.Where(v => v.AtletaId == referenceAthleteId.Value).ToList();
        }

        if (!userVideos.Any())
            return new UserAthleteStats
            {
                Athlete = athlete,
                HasData = false
            };

        // Calcular estadísticas del usuario
        var userStats = new UserAthleteStats
        {
            Athlete = athlete,
            HasData = true,
            TotalVideos = userVideos.Count,
            TotalDurationSeconds = userVideos.Sum(v => v.ClipDuration),
            AverageDurationSeconds = userVideos.Average(v => v.ClipDuration),
            SectionBreakdown = userVideos
                .GroupBy(v => v.Section)
                .Select(g => new SectionStats
                {
                    Section = g.Key,
                    SectionName = GetSectionName(g.Key),
                    VideoCount = g.Count(),
                    TotalDuration = g.Sum(v => v.ClipDuration)
                })
                .OrderBy(s => s.Section)
                .ToList()
        };

        // Calcular comparativa con otros atletas (ranking)
        var allAthleteStats = allVideos
            .GroupBy(v => v.AtletaId)
            .Select(g => new
            {
                AthleteId = g.Key,
                VideoCount = g.Count(),
                TotalDuration = g.Sum(v => v.ClipDuration)
            })
            .OrderByDescending(a => a.TotalDuration)
            .ToList();

        var userRankByDuration = allAthleteStats.FindIndex(a => a.AthleteId == referenceAthleteId.Value) + 1;
        var userRankByVideos = allAthleteStats.OrderByDescending(a => a.VideoCount).ToList()
            .FindIndex(a => a.AthleteId == referenceAthleteId.Value) + 1;

        userStats.RankByDuration = userRankByDuration;
        userStats.RankByVideoCount = userRankByVideos;
        userStats.TotalAthletesInContext = allAthleteStats.Count;

        // Porcentaje respecto al atleta con más tiempo
        if (allAthleteStats.Any())
        {
            var maxDuration = allAthleteStats.Max(a => a.TotalDuration);
            userStats.DurationPercentage = maxDuration > 0 
                ? (userStats.TotalDurationSeconds / maxDuration) * 100 
                : 0;

            var maxVideos = allAthleteStats.Max(a => a.VideoCount);
            userStats.VideoCountPercentage = maxVideos > 0
                ? ((double)userStats.TotalVideos / maxVideos) * 100
                : 0;
        }

        return userStats;
    }

    /// <summary>
    /// Compara el atleta de referencia con el top de atletas
    /// </summary>
    public async Task<List<AthleteComparisonRow>> GetAthleteComparisonAsync(int? referenceAthleteId, int? sessionId = null, int topN = 5)
    {
        List<VideoClip> videos;
        
        if (sessionId.HasValue)
            videos = await _databaseService.GetVideoClipsBySessionAsync(sessionId.Value);
        else
            videos = await _databaseService.GetAllVideoClipsAsync();

        var athletes = await _databaseService.GetAllAthletesAsync();
        var athleteById = athletes.ToDictionary(a => a.Id, a => a);

        var stats = videos
            .GroupBy(v => v.AtletaId)
            .Select(g =>
            {
                athleteById.TryGetValue(g.Key, out var athlete);
                return new AthleteComparisonRow
                {
                    AthleteId = g.Key,
                    AthleteName = athlete != null 
                        ? $"{athlete.Apellido?.ToUpperInvariant()} {athlete.Nombre}".Trim()
                        : $"Atleta {g.Key}",
                    VideoCount = g.Count(),
                    TotalDurationSeconds = g.Sum(v => v.ClipDuration),
                    IsReferenceAthlete = g.Key == referenceAthleteId
                };
            })
            .OrderByDescending(s => s.TotalDurationSeconds)
            .ToList();

        // Asignar ranking
        for (int i = 0; i < stats.Count; i++)
        {
            stats[i].Rank = i + 1;
        }

        // Si el usuario está en el top N, devolver el top N
        // Si no, devolver top (N-1) + usuario
        var referenceIndex = stats.FindIndex(s => s.IsReferenceAthlete);
        
        if (referenceIndex < 0 || referenceIndex < topN)
        {
            return stats.Take(topN).ToList();
        }
        else
        {
            var result = stats.Take(topN - 1).ToList();
            result.Add(stats[referenceIndex]);
            return result;
        }
    }
}

/// <summary>
/// Estadísticas del atleta de referencia (usuario)
/// </summary>
public class UserAthleteStats
{
    public Athlete? Athlete { get; set; }
    public bool HasData { get; set; }
    public int TotalVideos { get; set; }
    public double TotalDurationSeconds { get; set; }
    public double AverageDurationSeconds { get; set; }
    public int RankByDuration { get; set; }
    public int RankByVideoCount { get; set; }
    public int TotalAthletesInContext { get; set; }
    public double DurationPercentage { get; set; }
    public double VideoCountPercentage { get; set; }
    public List<SectionStats> SectionBreakdown { get; set; } = new();

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

    public string AverageDurationFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(AverageDurationSeconds);
            return ts.TotalMinutes >= 1 
                ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}" 
                : $"0:{ts.Seconds:D2}";
        }
    }

    public string RankText => HasData && TotalAthletesInContext > 0 
        ? $"#{RankByDuration} de {TotalAthletesInContext}" 
        : "-";
}

/// <summary>
/// Fila para comparativa de atletas
/// </summary>
public class AthleteComparisonRow
{
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    public int Rank { get; set; }
    public int VideoCount { get; set; }
    public double TotalDurationSeconds { get; set; }
    public bool IsReferenceAthlete { get; set; }

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
/// Fila para uso de tags/etiquetas
/// </summary>
public class TagUsageRow
{
    public int TagId { get; set; }
    public string TagName { get; set; } = "";
    public int UsageCount { get; set; }
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

/// <summary>
/// Estadísticas absolutas de tags y etiquetas
/// </summary>
public class AbsoluteTagStats
{
    public int TotalEventTags { get; set; }
    public int UniqueEventTags { get; set; }
    public int TotalLabelTags { get; set; }
    public int UniqueLabelTags { get; set; }
    public int LabeledVideos { get; set; }
    public int TotalVideos { get; set; }
    public double AvgTagsPerSession { get; set; }
    public List<TagUsageRow> TopEventTags { get; set; } = new();
    public List<TagUsageRow> TopLabelTags { get; set; } = new();
}
