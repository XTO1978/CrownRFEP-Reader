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

        // Obtener sesiones para el Dashboard (la columna izquierda debe mostrar todas)
        var allSessions = await _databaseService.GetAllSessionsAsync();
        // Nota: 'Fecha' es la fecha de la sesión (puede ser antigua si el usuario la selecciona).
        // Para mostrar las últimas sesiones *agregadas* arriba, ordenamos por Id (autoincrement).
        stats.RecentSessions = allSessions
            .OrderByDescending(s => s.Id)
            .ToList();

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
        // Usar formato simple "Tramo N" - no hay tabla de nombres de secciones en la BD
        return $"Tramo {section}";
    }

    /// <summary>
    /// Obtiene la tabla de tiempos por atleta y sección para una sesión específica
    /// Incluye split times y penalizaciones (eventos 2 y 50)
    /// </summary>
    public async Task<List<SectionWithAthleteRows>> GetAthleteSectionTimesAsync(int sessionId)
    {
        var videos = await _databaseService.GetVideoClipsBySessionAsync(sessionId);
        var allInputs = await _databaseService.GetInputsBySessionAsync(sessionId);
        
        if (videos.Count == 0)
            return new List<SectionWithAthleteRows>();

        // Crear diccionario de videos por Id
        var videoDict = videos.ToDictionary(v => v.Id, v => v);
        
        // Obtener split times (InputTypeId = -1)
        var splitInputs = allInputs.Where(i => i.InputTypeId == -1).ToList();
        
        // Obtener tags de sistema con penalizaciones
        var systemEventTags = await _databaseService.GetSystemEventTagsAsync();
        var penaltyTagIds = systemEventTags
            .Where(t => t.PenaltySeconds > 0)
            .ToDictionary(t => t.Id, t => t.PenaltySeconds * 1000L); // Convertir a ms
        
        // Obtener eventos que son penalizaciones (IsEvent=1 y InputTypeId está en penaltyTagIds)
        var penaltyInputs = allInputs
            .Where(i => i.IsEvent == 1 && penaltyTagIds.ContainsKey(i.InputTypeId))
            .GroupBy(i => i.VideoId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Crear filas de tiempos por video
        var rows = new List<AthleteSectionTimeRow>();
        
        foreach (var splitInput in splitInputs)
        {
            if (!videoDict.TryGetValue(splitInput.VideoId, out var video))
                continue;

            long durationMs = 0;
            if (!string.IsNullOrEmpty(splitInput.InputValue))
            {
                try
                {
                    var splitData = System.Text.Json.JsonSerializer.Deserialize<SplitTimeData>(splitInput.InputValue);
                    if (splitData != null)
                        durationMs = splitData.DurationMs;
                }
                catch { /* JSON inválido */ }
            }

            if (durationMs <= 0) continue;

            // Calcular penalizaciones para este video usando los tags de sistema
            long penaltyMs = 0;
            if (penaltyInputs.TryGetValue(video.Id, out var penalties))
            {
                foreach (var penalty in penalties)
                {
                    if (penaltyTagIds.TryGetValue(penalty.InputTypeId, out var penaltyValue))
                    {
                        penaltyMs += penaltyValue;
                    }
                }
            }

            var athlete = video.Atleta;
            var athleteName = athlete != null 
                ? $"{athlete.Apellido?.ToUpperInvariant() ?? ""} {athlete.Nombre ?? ""}".Trim()
                : $"Atleta {video.AtletaId}";

            rows.Add(new AthleteSectionTimeRow
            {
                AthleteId = video.AtletaId,
                AthleteName = athleteName,
                CategoryName = athlete?.CategoriaNombre ?? "",
                Section = video.Section,
                SectionName = GetSectionName(video.Section),
                VideoId = video.Id,
                VideoName = video.ComparisonName ?? video.ClipPath ?? "",
                DurationMs = durationMs,
                PenaltyMs = penaltyMs
            });
        }

        // Agrupar por sección numérica
        var result = rows
            .GroupBy(r => r.Section)
            .OrderBy(g => g.Key)
            .Select(sectionGroup => new SectionWithAthleteRows
            {
                Section = sectionGroup.Key,
                SectionName = GetSectionName(sectionGroup.Key),
                Athletes = sectionGroup
                    .OrderBy(r => r.TotalMs)
                    .ToList()
            })
            .ToList();

        return result;
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

        // Cargar etiquetas (tags) para IsEvent=0
        var tags = await _databaseService.GetAllTagsAsync();
        var tagById = tags.ToDictionary(t => t.Id, t => t.NombreTag ?? $"Tag {t.Id}");

        // Cargar eventos (event_tags) para IsEvent=1
        var eventTags = await _databaseService.GetAllEventTagsAsync();
        var eventTagById = eventTags.ToDictionary(t => t.Id, t => t.Nombre ?? $"Evento {t.Id}");

        // Separar eventos y etiquetas
        var eventInputs = inputs.Where(i => i.IsEvent == 1).ToList();
        var labelInputs = inputs.Where(i => i.IsEvent == 0).ToList();

        // Estadísticas de eventos (usar eventTagById)
        var eventStats = eventInputs
            .GroupBy(i => i.InputTypeId)
            .Select(g => new TagUsageRow
            {
                TagId = g.Key,
                TagName = eventTagById.GetValueOrDefault(g.Key, $"Evento {g.Key}"),
                UsageCount = g.Count()
            })
            .OrderByDescending(t => t.UsageCount)
            .ToList();

        // Estadísticas de etiquetas (usar tagById)
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

    /// <summary>
    /// Obtiene estadísticas personales extendidas para el atleta de referencia
    /// </summary>
    public async Task<UserPersonalStats> GetUserPersonalStatsAsync(int referenceAthleteId, int? currentSessionId = null)
    {
        var result = new UserPersonalStats();

        try
        {
            // 1. Media de resultados de las últimas 3 sesiones
            result.SessionAverages = await GetSessionAveragesAsync(referenceAthleteId, 3);

            // 2. Evolución de valoraciones
            result.ValoracionEvolution = await GetValoracionEvolutionAsync(referenceAthleteId);

            // 3. Tiempos por sección con diferencias (solo si hay sesión seleccionada)
            if (currentSessionId.HasValue)
            {
                result.SectionTimesWithDiff = await GetSectionTimesWithDiffAsync(referenceAthleteId, currentSessionId.Value);
            }

            // 4. Evolución de penalizaciones +2 última semana
            result.PenaltyEvolution = await GetPenaltyEvolutionAsync(referenceAthleteId, 7);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error obteniendo estadísticas personales: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Obtiene la media de tiempos de las últimas N sesiones para un atleta
    /// </summary>
    private async Task<List<SessionAverageData>> GetSessionAveragesAsync(int athleteId, int lastNSessions)
    {
        var videos = await _databaseService.GetVideoClipsByAthleteAsync(athleteId);
        var allInputs = await _databaseService.GetAllInputsAsync();
        
        // Obtener sesiones únicas ordenadas por fecha (más recientes primero)
        var sessions = await _databaseService.GetAllSessionsAsync();
        var sessionDict = sessions.ToDictionary(s => s.Id, s => s);
        
        var athleteSessionIds = videos
            .Where(v => sessionDict.ContainsKey(v.SessionId))
            .Select(v => v.SessionId)
            .Distinct()
            .OrderByDescending(sid => sessionDict[sid].FechaDateTime)
            .Take(lastNSessions)
            .ToList();

        var result = new List<SessionAverageData>();
        
        foreach (var sessionId in athleteSessionIds)
        {
            if (!sessionDict.TryGetValue(sessionId, out var session)) continue;
            
            var sessionVideos = videos.Where(v => v.SessionId == sessionId).ToList();
            var videoIds = sessionVideos.Select(v => v.Id).ToHashSet();
            
            // Obtener split times para estos videos
            var splitInputs = allInputs
                .Where(i => i.InputTypeId == -1 && videoIds.Contains(i.VideoId))
                .ToList();

            var times = new List<long>();
            foreach (var split in splitInputs)
            {
                if (!string.IsNullOrEmpty(split.InputValue))
                {
                    try
                    {
                        var splitData = System.Text.Json.JsonSerializer.Deserialize<SplitTimeData>(split.InputValue);
                        if (splitData != null && splitData.DurationMs > 0)
                            times.Add(splitData.DurationMs);
                    }
                    catch { }
                }
            }

            // Contar penalizaciones
            var systemEventTags = await _databaseService.GetSystemEventTagsAsync();
            var penaltyTagIds = systemEventTags.Where(t => t.PenaltySeconds > 0).Select(t => t.Id).ToHashSet();
            var penaltyCount = allInputs
                .Count(i => i.IsEvent == 1 && penaltyTagIds.Contains(i.InputTypeId) && videoIds.Contains(i.VideoId));

            if (times.Count > 0)
            {
                result.Add(new SessionAverageData
                {
                    SessionName = session.NombreSesion ?? $"Sesión {session.Id}",
                    SessionDate = session.FechaDateTime,
                    AverageTimeMs = times.Average(),
                    SectionCount = times.Count,
                    PenaltyCount = penaltyCount
                });
            }
        }

        return result.OrderBy(s => s.SessionDate).ToList();
    }

    /// <summary>
    /// Obtiene la evolución de valoraciones (físico, mental, técnico) para un atleta
    /// </summary>
    private async Task<ValoracionEvolution> GetValoracionEvolutionAsync(int athleteId)
    {
        var valoraciones = await _databaseService.GetValoracionesByAthleteAsync(athleteId);
        var sessions = await _databaseService.GetAllSessionsAsync();
        var sessionDict = sessions.ToDictionary(s => s.Id, s => s);

        var result = new ValoracionEvolution();

        // InputTypeId: 1 = Físico, 2 = Mental, 3 = Técnico (ajustar según tu esquema)
        foreach (var val in valoraciones.OrderBy(v => v.InputDateTime))
        {
            if (!sessionDict.TryGetValue(val.SessionId, out var session)) continue;
            
            var point = new ValoracionPoint
            {
                Date = val.InputDateTimeLocal,
                Value = val.InputValue,
                SessionName = session.NombreSesion ?? ""
            };

            switch (val.InputTypeId)
            {
                case 1:
                    result.Fisico.Add(point);
                    break;
                case 2:
                    result.Mental.Add(point);
                    break;
                case 3:
                    result.Tecnico.Add(point);
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Obtiene los tiempos por sección del usuario con diferencias respecto al mejor de la sesión
    /// </summary>
    private async Task<List<UserSectionTimeWithDiff>> GetSectionTimesWithDiffAsync(int athleteId, int sessionId)
    {
        var allSectionTimes = await GetAthleteSectionTimesAsync(sessionId);
        var result = new List<UserSectionTimeWithDiff>();

        foreach (var section in allSectionTimes)
        {
            var userRow = section.Athletes.FirstOrDefault(a => a.AthleteId == athleteId);
            var bestRow = section.Athletes.OrderBy(a => a.TotalMs).FirstOrDefault();

            if (userRow != null)
            {
                result.Add(new UserSectionTimeWithDiff
                {
                    Section = section.Section,
                    SectionName = section.SectionName,
                    UserTimeMs = userRow.DurationMs,
                    UserPenaltyMs = userRow.PenaltyMs,
                    BestTimeMs = bestRow?.TotalMs,
                    DifferenceMs = bestRow != null ? userRow.TotalMs - bestRow.TotalMs : null,
                    IsBestTime = bestRow != null && userRow.AthleteId == bestRow.AthleteId
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Obtiene la evolución de penalizaciones +2 en los últimos N días
    /// </summary>
    private async Task<List<PenaltyEvolutionPoint>> GetPenaltyEvolutionAsync(int athleteId, int lastNDays)
    {
        var cutoffDate = DateTime.Now.AddDays(-lastNDays);
        var videos = await _databaseService.GetVideoClipsByAthleteAsync(athleteId);
        var allInputs = await _databaseService.GetAllInputsAsync();
        var sessions = await _databaseService.GetAllSessionsAsync();
        var sessionDict = sessions.ToDictionary(s => s.Id, s => s);

        // Filtrar sesiones recientes
        var recentSessionIds = sessions
            .Where(s => s.FechaDateTime >= cutoffDate)
            .Select(s => s.Id)
            .ToHashSet();

        var athleteVideoIds = videos
            .Where(v => recentSessionIds.Contains(v.SessionId))
            .Select(v => v.Id)
            .ToHashSet();

        // Obtener tag de penalización +2
        var systemEventTags = await _databaseService.GetSystemEventTagsAsync();
        var penalty2TagId = systemEventTags.FirstOrDefault(t => t.PenaltySeconds == 2)?.Id ?? 0;

        if (penalty2TagId == 0)
            return new List<PenaltyEvolutionPoint>();

        // Agrupar penalizaciones por sesión
        var penaltyInputs = allInputs
            .Where(i => i.IsEvent == 1 && i.InputTypeId == penalty2TagId && athleteVideoIds.Contains(i.VideoId))
            .ToList();

        var videoToSession = videos.ToDictionary(v => v.Id, v => v.SessionId);
        
        var result = penaltyInputs
            .GroupBy(i => videoToSession.TryGetValue(i.VideoId, out var sid) ? sid : 0)
            .Where(g => g.Key > 0 && sessionDict.ContainsKey(g.Key))
            .Select(g => new PenaltyEvolutionPoint
            {
                Date = sessionDict[g.Key].FechaDateTime,
                PenaltyCount = g.Count(),
                SessionName = sessionDict[g.Key].NombreSesion ?? ""
            })
            .OrderBy(p => p.Date)
            .ToList();

        return result;
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

/// <summary>
/// Sección con filas de tiempos de atletas
/// </summary>
public class SectionWithAthleteRows
{
    public int Section { get; set; }
    public string SectionName { get; set; } = "";
    public List<AthleteSectionTimeRow> Athletes { get; set; } = new();
}

/// <summary>
/// Fila para tabla de tiempos por atleta y sección
/// </summary>
public class AthleteSectionTimeRow : System.ComponentModel.INotifyPropertyChanged
{
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int Section { get; set; }
    public string SectionName { get; set; } = "";
    public int VideoId { get; set; }
    public string VideoName { get; set; } = "";
    
    /// <summary>Duración del split en milisegundos</summary>
    public long DurationMs { get; set; }
    
    /// <summary>Penalización en milisegundos</summary>
    public long PenaltyMs { get; set; }
    
    /// <summary>Tiempo total (duración + penalización) en milisegundos</summary>
    public long TotalMs => DurationMs + PenaltyMs;

    /// <summary>Duración formateada como mm:ss.fff</summary>
    public string DurationFormatted => FormatTime(DurationMs);
    
    /// <summary>Penalización formateada</summary>
    public string PenaltyFormatted => PenaltyMs > 0 ? $"+{FormatTime(PenaltyMs)}" : "-";
    
    /// <summary>Total formateado</summary>
    public string TotalFormatted => FormatTime(TotalMs);

    // ==================== PROPIEDADES DE DIFERENCIA ====================
    
    private long _differenceMs;
    private bool _isReferenceAthlete;
    private bool _hasDifference;
    
    /// <summary>Diferencia respecto al atleta de referencia en ms</summary>
    public long DifferenceMs
    {
        get => _differenceMs;
        private set
        {
            _differenceMs = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DifferenceMs)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DifferenceFormatted)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DifferenceColor)));
        }
    }
    
    /// <summary>Indica si este atleta es el de referencia</summary>
    public bool IsReferenceAthlete
    {
        get => _isReferenceAthlete;
        private set
        {
            _isReferenceAthlete = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsReferenceAthlete)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DifferenceFormatted)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DifferenceColor)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(RowBackgroundColor)));
        }
    }
    
    /// <summary>Indica si hay un atleta de referencia para calcular diferencias</summary>
    public bool HasDifference
    {
        get => _hasDifference;
        private set
        {
            _hasDifference = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasDifference)));
        }
    }
    
    /// <summary>Diferencia formateada con signo</summary>
    public string DifferenceFormatted
    {
        get
        {
            if (IsReferenceAthlete) return "REF";
            if (!HasDifference) return "-";
            if (DifferenceMs == 0) return "0.000";
            var sign = DifferenceMs > 0 ? "+" : "-";
            return $"{sign}{FormatTime(Math.Abs(DifferenceMs))}";
        }
    }
    
    /// <summary>Color según la diferencia (verde si es mejor, rojo si es peor)</summary>
    public string DifferenceColor
    {
        get
        {
            if (IsReferenceAthlete) return "#FF6DDDFF"; // Azul para referencia
            if (!HasDifference) return "#FF808080";
            if (DifferenceMs < 0) return "#FF4CAF50"; // Verde - mejor tiempo
            if (DifferenceMs > 0) return "#FFFF6B6B"; // Rojo - peor tiempo
            return "#FFFFD700"; // Dorado - igual
        }
    }
    
    /// <summary>Color de fondo de la fila (destacar atleta de referencia)</summary>
    public string RowBackgroundColor => IsReferenceAthlete ? "#FF2A3A4A" : "Transparent";
    
    /// <summary>Establece la diferencia respecto al tiempo de referencia</summary>
    public void SetReferenceDifference(long referenceTotalMs, bool isReference)
    {
        IsReferenceAthlete = isReference;
        HasDifference = referenceTotalMs > 0;
        DifferenceMs = HasDifference ? TotalMs - referenceTotalMs : 0;
    }
    
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private static string FormatTime(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}

/// <summary>
/// Datos de media de tiempos para las últimas N sesiones
/// </summary>
public class SessionAverageData
{
    public string SessionName { get; set; } = "";
    public DateTime SessionDate { get; set; }
    public double AverageTimeMs { get; set; }
    public int SectionCount { get; set; }
    public int PenaltyCount { get; set; }
    
    public string AverageTimeFormatted
    {
        get
        {
            var ts = TimeSpan.FromMilliseconds(AverageTimeMs);
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
        }
    }
    
    public string SessionDateShort => SessionDate.ToString("dd/MM");
}

/// <summary>
/// Punto de datos para evolución de valoraciones
/// </summary>
public class ValoracionPoint
{
    public DateTime Date { get; set; }
    public int Value { get; set; }
    public string SessionName { get; set; } = "";
    
    public string DateShort => Date.ToString("dd/MM");
}

/// <summary>
/// Evolución de valoraciones (físico, mental, técnico)
/// </summary>
public class ValoracionEvolution
{
    public List<ValoracionPoint> Fisico { get; set; } = new();
    public List<ValoracionPoint> Mental { get; set; } = new();
    public List<ValoracionPoint> Tecnico { get; set; } = new();
    
    public bool HasData => Fisico.Count > 0 || Mental.Count > 0 || Tecnico.Count > 0;
}

/// <summary>
/// Tabla de tiempos del usuario con diferencias respecto a otros atletas
/// </summary>
public class UserSectionTimeWithDiff
{
    public int Section { get; set; }
    public string SectionName { get; set; } = "";
    public long UserTimeMs { get; set; }
    public long UserPenaltyMs { get; set; }
    public long UserTotalMs => UserTimeMs + UserPenaltyMs;
    public long? BestTimeMs { get; set; }
    public long? DifferenceMs { get; set; }
    public bool IsBestTime { get; set; }
    
    public string UserTimeFormatted => FormatTime(UserTotalMs);
    public string DifferenceFormatted => DifferenceMs.HasValue 
        ? (DifferenceMs.Value > 0 ? $"+{FormatTime(DifferenceMs.Value)}" : FormatTime(DifferenceMs.Value))
        : "-";
    public string DifferenceColor => DifferenceMs switch
    {
        null => "#FF808080",
        0 => "#FF4CAF50",
        < 0 => "#FF4CAF50",
        _ => "#FFFF6B6B"
    };
    
    private static string FormatTime(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(Math.Abs(ms));
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
    }
}

/// <summary>
/// Punto de datos para evolución de penalizaciones
/// </summary>
public class PenaltyEvolutionPoint
{
    public DateTime Date { get; set; }
    public int PenaltyCount { get; set; }
    public string SessionName { get; set; } = "";
    
    public string DateShort => Date.ToString("dd/MM");
}

/// <summary>
/// Estadísticas personales extendidas
/// </summary>
public class UserPersonalStats
{
    /// <summary>Media de tiempos de las últimas N sesiones</summary>
    public List<SessionAverageData> SessionAverages { get; set; } = new();
    
    /// <summary>Evolución de valoraciones</summary>
    public ValoracionEvolution ValoracionEvolution { get; set; } = new();
    
    /// <summary>Tiempos por sección con diferencia respecto al mejor</summary>
    public List<UserSectionTimeWithDiff> SectionTimesWithDiff { get; set; } = new();
    
    /// <summary>Evolución de penalizaciones +2</summary>
    public List<PenaltyEvolutionPoint> PenaltyEvolution { get; set; } = new();
    
    public bool HasSessionAverages => SessionAverages.Count > 0;
    public bool HasValoraciones => ValoracionEvolution.HasData;
    public bool HasSectionTimes => SectionTimesWithDiff.Count > 0;
    public bool HasPenaltyEvolution => PenaltyEvolution.Count > 0;
}
