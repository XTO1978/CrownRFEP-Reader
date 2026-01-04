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
    /// Obtiene los tiempos de atletas por sección con tiempos parciales (Laps) detallados
    /// </summary>
    public async Task<List<SectionWithDetailedAthleteRows>> GetDetailedAthleteSectionTimesAsync(int sessionId)
    {
        var videos = await _databaseService.GetVideoClipsBySessionAsync(sessionId);
        var allInputs = await _databaseService.GetInputsBySessionAsync(sessionId);
        var allTimingEvents = await _databaseService.GetExecutionTimingEventsBySessionAsync(sessionId);
        
        if (videos.Count == 0)
            return new List<SectionWithDetailedAthleteRows>();

        // Crear diccionario de videos por Id
        var videoDict = videos.ToDictionary(v => v.Id, v => v);
        
        // Agrupar timing events por video
        var timingEventsByVideo = allTimingEvents.GroupBy(e => e.VideoId).ToDictionary(g => g.Key, g => g.ToList());
        
        // Obtener split times (InputTypeId = -1)
        var splitInputs = allInputs.Where(i => i.InputTypeId == -1).ToList();
        
        // Obtener tags de sistema con penalizaciones
        var systemEventTags = await _databaseService.GetSystemEventTagsAsync();
        var penaltyTagIds = systemEventTags
            .Where(t => t.PenaltySeconds > 0)
            .ToDictionary(t => t.Id, t => t.PenaltySeconds * 1000L);
        
        // Obtener eventos que son penalizaciones
        var penaltyInputs = allInputs
            .Where(i => i.IsEvent == 1 && penaltyTagIds.ContainsKey(i.InputTypeId))
            .GroupBy(i => i.VideoId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<AthleteDetailedTimeRow>();
        
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

            // Calcular penalizaciones
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

            // Obtener tiempos parciales (Laps) del video
            var laps = new List<LapTimeData>();
            if (timingEventsByVideo.TryGetValue(video.Id, out var timingEvents))
            {
                // Filtrar solo los Laps (Kind = 1)
                var lapEvents = timingEvents.Where(e => e.Kind == 1).OrderBy(e => e.LapIndex).ToList();
                long cumulative = 0;
                foreach (var lapEvent in lapEvents)
                {
                    cumulative += lapEvent.SplitMilliseconds;
                    laps.Add(new LapTimeData
                    {
                        LapIndex = lapEvent.LapIndex,
                        SplitMs = lapEvent.SplitMilliseconds,
                        CumulativeMs = cumulative
                    });
                }
            }

            var athlete = video.Atleta;
            var athleteName = athlete != null 
                ? $"{athlete.Apellido?.ToUpperInvariant() ?? ""} {athlete.Nombre ?? ""}".Trim()
                : $"Atleta {video.AtletaId}";

            rows.Add(new AthleteDetailedTimeRow
            {
                AthleteId = video.AtletaId,
                AthleteName = athleteName,
                CategoryName = athlete?.CategoriaNombre ?? "",
                Section = video.Section,
                SectionName = GetSectionName(video.Section),
                VideoId = video.Id,
                VideoName = video.ComparisonName ?? video.ClipPath ?? "",
                DurationMs = durationMs,
                PenaltyMs = penaltyMs,
                Laps = laps
            });
        }

        // Agrupar por sección
        var result = rows
            .GroupBy(r => r.Section)
            .OrderBy(g => g.Key)
            .Select(sectionGroup =>
            {
                var athletes = sectionGroup.OrderBy(r => r.TotalMs).ToList();

                // Calcular número de intento y marcar mejor para atletas con múltiples ejecuciones
                AssignAttemptNumbers(athletes);

                // Alternar colores de fondo
                for (int i = 0; i < athletes.Count; i++)
                {
                    athletes[i].RowBackgroundColor = i % 2 == 0 ? "#FF1E1E1E" : "#FF252525";
                }

                ApplyConditionalFormattingForSection(athletes);

                return new SectionWithDetailedAthleteRows
                {
                    Section = sectionGroup.Key,
                    SectionName = GetSectionName(sectionGroup.Key),
                    Athletes = athletes
                };
            })
            .ToList();

        return result;
    }

    /// <summary>
    /// Asigna números de intento a atletas con múltiples ejecuciones en la misma sección
    /// </summary>
    private static void AssignAttemptNumbers(List<AthleteDetailedTimeRow> athletes)
    {
        // Agrupar por atleta
        var byAthlete = athletes.GroupBy(a => a.AthleteId).ToList();

        foreach (var athleteGroup in byAthlete)
        {
            var attempts = athleteGroup.OrderBy(a => a.TotalMs).ToList();
            var totalAttempts = attempts.Count;

            if (totalAttempts == 1)
            {
                attempts[0].AttemptNumber = 1;
                attempts[0].TotalAttempts = 1;
                attempts[0].IsBestAttempt = true;
                continue;
            }

            // Múltiples intentos: numerar por orden de tiempo (1 = mejor)
            for (int i = 0; i < attempts.Count; i++)
            {
                attempts[i].AttemptNumber = i + 1;
                attempts[i].TotalAttempts = totalAttempts;
                attempts[i].IsBestAttempt = i == 0; // El primero es el mejor (ya ordenados por TotalMs)
            }
        }
    }

    private static void ApplyConditionalFormattingForSection(List<AthleteDetailedTimeRow> athletes)
    {
        const string bestColor = "#FF4CAF50";   // verde (mejor)
        const string goodColor = "#FF6DDDFF";   // azul/cian (top)
        const string defaultColor = "White";

        if (athletes.Count == 0)
            return;

        // TOTAL: percentiles sobre TotalMs (incluye penalización)
        var totalValues = athletes
            .Select(a => a.TotalMs)
            .Where(v => v > 0)
            .OrderBy(v => v)
            .ToList();

        var (bestTotal, goodTotal) = GetThresholds(totalValues);
        foreach (var athlete in athletes)
        {
            athlete.TotalTextColor = GetTierColor(athlete.TotalMs, bestTotal, goodTotal, bestColor, goodColor, defaultColor);
        }

        // PARCIALES (Laps): percentiles por LapIndex
        var splitByLap = athletes
            .SelectMany(a => a.Laps.Select(l => (LapIndex: l.LapIndex, SplitMs: l.SplitMs)))
            .Where(x => x.SplitMs > 0)
            .GroupBy(x => x.LapIndex)
            .ToDictionary(g => g.Key, g => g.Select(x => x.SplitMs).OrderBy(v => v).ToList());

        var cumulativeByLap = athletes
            .SelectMany(a => a.Laps.Select(l => (LapIndex: l.LapIndex, CumulativeMs: l.CumulativeMs)))
            .Where(x => x.CumulativeMs > 0)
            .GroupBy(x => x.LapIndex)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CumulativeMs).OrderBy(v => v).ToList());

        foreach (var athlete in athletes)
        {
            foreach (var lap in athlete.Laps)
            {
                if (splitByLap.TryGetValue(lap.LapIndex, out var splitValues))
                {
                    var (bestSplit, goodSplit) = GetThresholds(splitValues);
                    lap.SplitTextColor = GetTierColor(lap.SplitMs, bestSplit, goodSplit, bestColor, goodColor, defaultColor);
                }
                else
                {
                    lap.SplitTextColor = defaultColor;
                }

                if (cumulativeByLap.TryGetValue(lap.LapIndex, out var cumulativeValues))
                {
                    var (bestCum, goodCum) = GetThresholds(cumulativeValues);
                    lap.CumulativeTextColor = GetTierColor(lap.CumulativeMs, bestCum, goodCum, bestColor, goodColor, defaultColor);
                }
                else
                {
                    lap.CumulativeTextColor = defaultColor;
                }
            }
        }
    }

    private static (long best, long good) GetThresholds(List<long> sortedAscending)
    {
        if (sortedAscending.Count <= 0)
            return (0, 0);

        // Pocos datos: resaltamos 1º (best) y 2º (good) si existe
        if (sortedAscending.Count == 1)
            return (sortedAscending[0], sortedAscending[0]);
        if (sortedAscending.Count <= 3)
            return (sortedAscending[0], sortedAscending[1]);

        var best = Percentile(sortedAscending, 0.10);
        var good = Percentile(sortedAscending, 0.25);
        if (good < best) good = best;
        return (best, good);
    }

    private static long Percentile(List<long> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 0)
            return 0;

        if (percentile <= 0) return sortedAscending[0];
        if (percentile >= 1) return sortedAscending[^1];

        var idx = (int)Math.Floor(percentile * (sortedAscending.Count - 1));
        idx = Math.Max(0, Math.Min(idx, sortedAscending.Count - 1));
        return sortedAscending[idx];
    }

    private static string GetTierColor(long value, long bestThreshold, long goodThreshold, string bestColor, string goodColor, string defaultColor)
    {
        if (value <= 0)
            return defaultColor;
        if (bestThreshold > 0 && value <= bestThreshold)
            return bestColor;
        if (goodThreshold > 0 && value <= goodThreshold)
            return goodColor;
        return defaultColor;
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
    
    /// <summary>Penalización formateada como segundos enteros</summary>
    public string PenaltyFormatted => PenaltyMs > 0 ? $"+{PenaltyMs / 1000}" : "-";
    
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

    /// <summary>
    /// Formatea tiempo en milisegundos a segundos,centésimas (ej: 92320ms → "92,32")
    /// </summary>
    private static string FormatTime(long ms)
    {
        var totalSeconds = ms / 1000.0;
        var centiseconds = (ms % 1000) / 10; // Convertir milésimas a centésimas
        return $"{(int)totalSeconds},{centiseconds:D2}";
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
    
    /// <summary>
    /// Formatea tiempo en milisegundos a segundos,centésimas (ej: 92320ms → "92,32")
    /// </summary>
    private static string FormatTime(long ms)
    {
        var absMs = Math.Abs(ms);
        var totalSeconds = absMs / 1000;
        var centiseconds = (absMs % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
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

// ==================== TABLA EXTENDIDA DE TIEMPOS CON PARCIALES ====================

/// <summary>
/// Tiempo parcial individual (Lap) dentro de una ejecución
/// </summary>
public class LapTimeData
{
    public int LapIndex { get; set; }
    public long SplitMs { get; set; }
    public long CumulativeMs { get; set; }

    // Formato condicional (colores como string para bind directo en XAML)
    public string SplitTextColor { get; set; } = "White";
    public string CumulativeTextColor { get; set; } = "White";
    
    public string SplitFormatted => FormatTime(SplitMs);
    public string CumulativeFormatted => FormatTime(CumulativeMs);
    
    private static string FormatTime(long ms)
    {
        var totalSeconds = ms / 1000;
        var centiseconds = (ms % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
}

/// <summary>
/// Fila extendida con tiempos parciales para la tabla modal
/// </summary>
public class AthleteDetailedTimeRow
{
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int Section { get; set; }
    public string SectionName { get; set; } = "";
    public int VideoId { get; set; }
    public string VideoName { get; set; } = "";
    
    /// <summary>Número de intento del atleta en esta sección (1, 2, 3...)</summary>
    public int AttemptNumber { get; set; } = 1;
    
    /// <summary>Total de intentos del atleta en esta sección</summary>
    public int TotalAttempts { get; set; } = 1;
    
    /// <summary>Indica si es el mejor intento del atleta en esta sección</summary>
    public bool IsBestAttempt { get; set; }
    
    /// <summary>Nombre para mostrar (incluye número de intento si hay varios)</summary>
    public string DisplayName => TotalAttempts > 1 
        ? $"{AthleteName} ({AttemptNumber})" 
        : AthleteName;
    
    /// <summary>Duración total del split en milisegundos</summary>
    public long DurationMs { get; set; }
    
    /// <summary>Penalización en milisegundos (almacenada pero no usada en cálculos)</summary>
    public long PenaltyMs { get; set; }
    
    /// <summary>Tiempo total (solo duración, sin penalizaciones)</summary>
    public long TotalMs => DurationMs;
    
    /// <summary>Lista de tiempos parciales (Laps)</summary>
    public List<LapTimeData> Laps { get; set; } = new();
    
    /// <summary>Indica si hay tiempos parciales</summary>
    public bool HasLaps => Laps.Count > 0;
    
    /// <summary>Número de laps</summary>
    public int LapCount => Laps.Count;
    
    public string DurationFormatted => FormatTime(DurationMs);
    public string PenaltyFormatted => PenaltyMs > 0 ? $"+{PenaltyMs / 1000}" : "-";
    public string TotalFormatted => FormatTime(TotalMs);

    // Formato condicional (TOTAL)
    public string TotalTextColor { get; set; } = "White";
    
    /// <summary>Color de fondo de la fila (alternado)</summary>
    public string RowBackgroundColor { get; set; } = "#FF1A1A1A";
    
    private static string FormatTime(long ms)
    {
        var totalSeconds = ms / 1000;
        var centiseconds = (ms % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
}

/// <summary>
/// Sección con filas detalladas de atletas (incluye parciales)
/// </summary>
public class SectionWithDetailedAthleteRows
{
    public int Section { get; set; }
    public string SectionName { get; set; } = "";
    public List<AthleteDetailedTimeRow> Athletes { get; set; } = new();
    
    /// <summary>Número máximo de laps en esta sección (para determinar columnas)</summary>
    public int MaxLaps => Athletes.Count > 0 ? Athletes.Max(a => a.LapCount) : 0;
    
    /// <summary>Indica si hay algún atleta con laps</summary>
    public bool HasAnyLaps => Athletes.Any(a => a.HasLaps);
}

/// <summary>
/// Métricas de consistencia para un grupo de tiempos
/// </summary>
public class ConsistencyMetrics
{
    /// <summary>Número total de ejecuciones</summary>
    public int TotalCount { get; set; }
    
    /// <summary>Número de ejecuciones válidas (sin outliers)</summary>
    public int ValidCount { get; set; }
    
    /// <summary>Número de outliers descartados</summary>
    public int OutliersCount => TotalCount - ValidCount;
    
    /// <summary>Media en milisegundos (sin outliers)</summary>
    public double MeanMs { get; set; }
    
    /// <summary>Mediana en milisegundos (sin outliers)</summary>
    public double MedianMs { get; set; }
    
    /// <summary>Desviación estándar en milisegundos (sin outliers)</summary>
    public double StdDevMs { get; set; }
    
    /// <summary>Coeficiente de variación (%) - menor = más consistente</summary>
    public double CoefficientOfVariation => MeanMs > 0 ? (StdDevMs / MeanMs) * 100 : 0;
    
    /// <summary>Rango (max - min) en milisegundos (sin outliers)</summary>
    public long RangeMs { get; set; }
    
    /// <summary>Tiempo mínimo en milisegundos (sin outliers)</summary>
    public long MinMs { get; set; }
    
    /// <summary>Tiempo máximo en milisegundos (sin outliers)</summary>
    public long MaxMs { get; set; }
    
    /// <summary>Límite inferior para outliers (Q1 - 1.5*IQR)</summary>
    public double LowerBoundMs { get; set; }
    
    /// <summary>Límite superior para outliers (Q3 + 1.5*IQR)</summary>
    public double UpperBoundMs { get; set; }
    
    /// <summary>Lista de outliers con información del atleta</summary>
    public List<OutlierInfo> Outliers { get; set; } = new();
    
    /// <summary>Indica si hay métricas válidas</summary>
    public bool IsValid => ValidCount >= 2;
    
    /// <summary>Descripción del criterio de exclusión de outliers</summary>
    public string OutlierCriteria => "IQR×1.5: valores fuera del rango [Q1 - 1.5×IQR, Q3 + 1.5×IQR]";
    
    /// <summary>Formato de CV%</summary>
    public string CvFormatted => $"{CoefficientOfVariation:0.1}%";
    
    /// <summary>Formato de rango</summary>
    public string RangeFormatted => FormatTime(RangeMs);
    
    /// <summary>Formato de media</summary>
    public string MeanFormatted => FormatTime((long)MeanMs);
    
    /// <summary>Formato de desviación estándar</summary>
    public string StdDevFormatted => FormatTime((long)StdDevMs);
    
    private static string FormatTime(long ms)
    {
        if (ms < 0) ms = 0;
        var totalSeconds = ms / 1000;
        var centiseconds = (ms % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
}

/// <summary>
/// Información de un outlier
/// </summary>
public class OutlierInfo
{
    public int VideoId { get; set; }
    public string AthleteName { get; set; } = "";
    public int AttemptNumber { get; set; }
    public long TimeMs { get; set; }
    public string Reason { get; set; } = "";
    
    public string TimeFormatted
    {
        get
        {
            var totalSeconds = TimeMs / 1000;
            var centiseconds = (TimeMs % 1000) / 10;
            return $"{totalSeconds},{centiseconds:D2}";
        }
    }
}

/// <summary>
/// Métricas de consistencia individual de un atleta
/// </summary>
public class AthleteConsistencyMetrics
{
    public int AthleteId { get; set; }
    public string AthleteName { get; set; } = "";
    public int AttemptCount { get; set; }
    
    /// <summary>CV% del atleta entre sus intentos</summary>
    public double CoefficientOfVariation { get; set; }
    
    /// <summary>Rango entre mejor y peor tiempo</summary>
    public long RangeMs { get; set; }
    
    public string CvFormatted => $"{CoefficientOfVariation:0.1}%";
    
    public string RangeFormatted
    {
        get
        {
            var totalSeconds = RangeMs / 1000;
            var centiseconds = (RangeMs % 1000) / 10;
            return $"{totalSeconds},{centiseconds:D2}";
        }
    }
}

/// <summary>
/// Calculadora de métricas de consistencia
/// </summary>
public static class ConsistencyCalculator
{
    /// <summary>
    /// Calcula métricas de consistencia para un grupo de tiempos
    /// </summary>
    public static ConsistencyMetrics Calculate(IEnumerable<(int videoId, string athleteName, int attempt, long timeMs)> data)
    {
        var list = data.Where(d => d.timeMs > 0).ToList();
        var metrics = new ConsistencyMetrics { TotalCount = list.Count };
        
        if (list.Count < 2)
        {
            metrics.ValidCount = list.Count;
            if (list.Count == 1)
            {
                metrics.MeanMs = list[0].timeMs;
                metrics.MedianMs = list[0].timeMs;
                metrics.MinMs = list[0].timeMs;
                metrics.MaxMs = list[0].timeMs;
            }
            return metrics;
        }
        
        var times = list.Select(d => d.timeMs).OrderBy(t => t).ToList();
        
        // Calcular cuartiles para IQR
        var q1 = Percentile(times, 25);
        var q3 = Percentile(times, 75);
        var iqr = q3 - q1;
        
        // Límites de outliers (método IQR × 1.5)
        metrics.LowerBoundMs = q1 - 1.5 * iqr;
        metrics.UpperBoundMs = q3 + 1.5 * iqr;
        
        // Identificar outliers
        var validData = new List<(int videoId, string athleteName, int attempt, long timeMs)>();
        foreach (var item in list)
        {
            if (item.timeMs < metrics.LowerBoundMs)
            {
                metrics.Outliers.Add(new OutlierInfo
                {
                    VideoId = item.videoId,
                    AthleteName = item.athleteName,
                    AttemptNumber = item.attempt,
                    TimeMs = item.timeMs,
                    Reason = $"Inferior al límite ({FormatTime((long)metrics.LowerBoundMs)})"
                });
            }
            else if (item.timeMs > metrics.UpperBoundMs)
            {
                metrics.Outliers.Add(new OutlierInfo
                {
                    VideoId = item.videoId,
                    AthleteName = item.athleteName,
                    AttemptNumber = item.attempt,
                    TimeMs = item.timeMs,
                    Reason = $"Superior al límite ({FormatTime((long)metrics.UpperBoundMs)})"
                });
            }
            else
            {
                validData.Add(item);
            }
        }
        
        var validTimes = validData.Select(d => d.timeMs).OrderBy(t => t).ToList();
        metrics.ValidCount = validTimes.Count;
        
        if (validTimes.Count == 0)
        {
            // Si todos son outliers, usar los datos originales
            validTimes = times;
            metrics.ValidCount = times.Count;
            metrics.Outliers.Clear();
        }
        
        // Calcular estadísticas sin outliers
        metrics.MeanMs = validTimes.Average();
        metrics.MedianMs = Percentile(validTimes, 50);
        metrics.MinMs = validTimes.Min();
        metrics.MaxMs = validTimes.Max();
        metrics.RangeMs = metrics.MaxMs - metrics.MinMs;
        
        // Desviación estándar
        if (validTimes.Count >= 2)
        {
            var sumSqDiff = validTimes.Sum(t => Math.Pow(t - metrics.MeanMs, 2));
            metrics.StdDevMs = Math.Sqrt(sumSqDiff / (validTimes.Count - 1));
        }
        
        return metrics;
    }
    
    /// <summary>
    /// Calcula métricas de consistencia individual para atletas con múltiples intentos
    /// </summary>
    public static List<AthleteConsistencyMetrics> CalculateIndividual(IEnumerable<AthleteDetailedTimeRow> athletes)
    {
        var grouped = athletes
            .Where(a => a.TotalMs > 0)
            .GroupBy(a => a.AthleteId)
            .Where(g => g.Count() >= 2)
            .ToList();
        
        var result = new List<AthleteConsistencyMetrics>();
        
        foreach (var group in grouped)
        {
            var times = group.Select(a => a.TotalMs).OrderBy(t => t).ToList();
            var mean = times.Average();
            var sumSqDiff = times.Sum(t => Math.Pow(t - mean, 2));
            var stdDev = Math.Sqrt(sumSqDiff / (times.Count - 1));
            
            result.Add(new AthleteConsistencyMetrics
            {
                AthleteId = group.Key,
                AthleteName = group.First().AthleteName,
                AttemptCount = times.Count,
                CoefficientOfVariation = mean > 0 ? (stdDev / mean) * 100 : 0,
                RangeMs = times.Max() - times.Min()
            });
        }
        
        return result.OrderBy(a => a.CoefficientOfVariation).ToList();
    }
    
    private static double Percentile(List<long> sortedData, double percentile)
    {
        if (sortedData.Count == 0) return 0;
        if (sortedData.Count == 1) return sortedData[0];
        
        var n = sortedData.Count;
        var rank = (percentile / 100.0) * (n - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        
        if (lowerIndex == upperIndex || upperIndex >= n)
            return sortedData[Math.Min(lowerIndex, n - 1)];
        
        var fraction = rank - lowerIndex;
        return sortedData[lowerIndex] + fraction * (sortedData[upperIndex] - sortedData[lowerIndex]);
    }
    
    private static string FormatTime(long ms)
    {
        if (ms < 0) ms = 0;
        var totalSeconds = ms / 1000;
        var centiseconds = (ms % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }
}
