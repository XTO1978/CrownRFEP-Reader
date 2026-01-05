using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio para generar datos completos de informe de sesión
/// </summary>
public static class SessionReportService
{
    /// <summary>
    /// Genera todos los datos necesarios para el informe de sesión (sobrecarga sin videos)
    /// </summary>
    public static SessionReportData GenerateReportData(
        Session session,
        IReadOnlyList<SectionWithDetailedAthleteRows> sections,
        int? referenceAthleteId = null,
        int? referenceVideoId = null)
    {
        return GenerateReportData(session, Array.Empty<VideoClip>(), sections, referenceAthleteId, referenceVideoId);
    }
    
    /// <summary>
    /// Genera todos los datos necesarios para el informe de sesión
    /// </summary>
    public static SessionReportData GenerateReportData(
        Session session,
        IReadOnlyList<VideoClip> videos,
        IReadOnlyList<SectionWithDetailedAthleteRows> sections,
        int? referenceAthleteId = null,
        int? referenceVideoId = null)
    {
        var report = new SessionReportData
        {
            SessionId = session.Id,
            ReferenceAthleteId = referenceAthleteId,
            ReferenceVideoId = referenceVideoId
        };
        
        // 1. Resumen del entrenamiento
        report.Summary = BuildTrainingSummary(session, videos, sections);
        
        // 2. Datos por sección
        foreach (var section in sections.OrderBy(s => s.Section))
        {
            var sectionData = BuildSectionReportData(section);
            report.Sections.Add(sectionData);
        }
        
        // 3. Ranking general (basado en la primera sección o combinado)
        if (report.Sections.Count > 0)
        {
            report.GeneralRanking = report.Sections.First().Ranking;
        }
        
        // 4. Perfiles de atletas
        report.AthleteProfiles = BuildAthleteProfiles(sections);
        
        // 5. Análisis de penalizaciones
        report.PenaltyAnalysis = BuildPenaltyAnalysis(sections);
        
        return report;
    }
    
    /// <summary>
    /// Construye el resumen del entrenamiento
    /// </summary>
    private static TrainingSummary BuildTrainingSummary(
        Session session,
        IReadOnlyList<VideoClip> videos,
        IReadOnlyList<SectionWithDetailedAthleteRows> sections)
    {
        var allAthletes = sections
            .SelectMany(s => s.Athletes)
            .Select(a => a.AthleteId)
            .Distinct()
            .ToList();
        
        var allTimes = sections
            .SelectMany(s => s.Athletes)
            .Where(a => a.TotalMs > 0)
            .Select(a => a.TotalMs)
            .ToList();
        
        var runsWithPenalty = sections
            .SelectMany(s => s.Athletes)
            .Count(a => a.PenaltyMs > 0);
        
        var bestRun = sections
            .SelectMany(s => s.Athletes)
            .Where(a => a.TotalMs > 0)
            .OrderBy(a => a.TotalMs)
            .FirstOrDefault();
        
        return new TrainingSummary
        {
            SessionName = session.DisplayName,
            SessionDate = session.FechaDateTime,
            Location = session.Lugar ?? "",
            Coach = session.Coach ?? "",
            SessionType = session.TipoSesion ?? "",
            TotalRuns = sections.Sum(s => s.Athletes.Count),
            TotalAthletes = allAthletes.Count,
            TotalSections = sections.Count,
            TotalRecordingDuration = videos.Sum(v => v.ClipDuration),
            TotalFileSize = videos.Sum(v => v.ClipSize),
            AverageTimeMs = allTimes.Count > 0 ? (long)allTimes.Average() : 0,
            BestTimeMs = bestRun?.TotalMs ?? 0,
            BestTimeAthlete = bestRun?.AthleteName ?? "",
            RunsWithPenalty = runsWithPenalty
        };
    }
    
    /// <summary>
    /// Construye los datos de informe para una sección
    /// </summary>
    private static SectionReportData BuildSectionReportData(SectionWithDetailedAthleteRows section)
    {
        var sectionData = new SectionReportData
        {
            SectionId = section.Section,
            SectionName = section.SectionName,
            Athletes = section.Athletes.ToList()
        };
        
        // Métricas de consistencia del grupo
        if (section.Athletes.Count >= 2)
        {
            var groupData = section.Athletes
                .Select(a => (a.VideoId, a.DisplayName, a.AttemptNumber, a.TotalMs))
                .ToList();
            sectionData.GroupConsistency = ConsistencyCalculator.Calculate(groupData);
            
            // Consistencia individual
            sectionData.IndividualConsistency = ConsistencyCalculator.CalculateIndividual(section.Athletes);
        }
        
        // Ranking de la sección
        sectionData.Ranking = BuildRanking(section.Athletes);
        
        // Análisis de parciales
        sectionData.LapProfiles = BuildLapProfiles(section);
        
        return sectionData;
    }
    
    /// <summary>
    /// Construye el ranking para una lista de atletas
    /// </summary>
    private static List<RankingEntry> BuildRanking(IReadOnlyList<AthleteDetailedTimeRow> athletes)
    {
        var sorted = athletes
            .Where(a => a.TotalMs > 0)
            .OrderBy(a => a.TotalMs)
            .ToList();
        
        if (sorted.Count == 0)
            return new List<RankingEntry>();
        
        var firstTime = sorted.First().TotalMs;
        var times = sorted.Select(a => a.TotalMs).ToList();
        var medianTime = times.Count > 0 ? Percentile(times, 50) : 0;
        
        var ranking = new List<RankingEntry>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var athlete = sorted[i];
            var diffToFirst = athlete.TotalMs - firstTime;
            var diffToFirstPercent = firstTime > 0 ? (diffToFirst * 100.0 / firstTime) : 0;
            
            ranking.Add(new RankingEntry
            {
                Position = i + 1,
                AthleteId = athlete.AthleteId,
                AthleteName = athlete.AthleteName,
                CategoryName = athlete.CategoryName,
                VideoId = athlete.VideoId,
                AttemptNumber = athlete.AttemptNumber,
                IsBestAttempt = athlete.IsBestAttempt,
                TotalTimeMs = athlete.TotalMs,
                DiffToFirstMs = diffToFirst,
                DiffToFirstPercent = diffToFirstPercent,
                DiffToMedianMs = athlete.TotalMs - (long)medianTime
            });
        }
        
        return ranking;
    }
    
    /// <summary>
    /// Construye los perfiles de análisis de parciales para cada atleta
    /// </summary>
    private static List<AthleteLapProfile> BuildLapProfiles(SectionWithDetailedAthleteRows section)
    {
        var profiles = new List<AthleteLapProfile>();
        var maxLaps = section.MaxLaps;
        
        if (maxLaps == 0 || section.Athletes.Count < 2)
            return profiles;
        
        // Calcular estadísticas por parcial
        var lapStats = new Dictionary<int, (long best, long avg)>();
        for (int lapIdx = 1; lapIdx <= maxLaps; lapIdx++)
        {
            var lapTimes = section.Athletes
                .Select(a => a.Laps.FirstOrDefault(l => l.LapIndex == lapIdx))
                .Where(l => l != null && l.SplitMs > 0)
                .Select(l => l!.SplitMs)
                .ToList();
            
            if (lapTimes.Count > 0)
            {
                lapStats[lapIdx] = (lapTimes.Min(), (long)lapTimes.Average());
            }
        }
        
        // Construir perfil para cada atleta
        foreach (var athlete in section.Athletes)
        {
            var profile = new AthleteLapProfile
            {
                AthleteId = athlete.AthleteId,
                AthleteName = athlete.AthleteName
            };
            
            foreach (var lap in athlete.Laps)
            {
                if (lapStats.TryGetValue(lap.LapIndex, out var stats))
                {
                    profile.LapAnalysis.Add(new LapAnalysisData
                    {
                        AthleteId = athlete.AthleteId,
                        AthleteName = athlete.AthleteName,
                        LapIndex = lap.LapIndex,
                        AthleteTimeMs = lap.SplitMs,
                        BestTimeMs = stats.best,
                        AverageTimeMs = stats.avg
                    });
                }
            }
            
            if (profile.LapAnalysis.Count > 0)
            {
                profiles.Add(profile);
            }
        }
        
        return profiles;
    }
    
    /// <summary>
    /// Construye perfiles completos de atletas
    /// </summary>
    private static List<AthleteReportProfile> BuildAthleteProfiles(
        IReadOnlyList<SectionWithDetailedAthleteRows> sections)
    {
        var profiles = new Dictionary<int, AthleteReportProfile>();
        
        foreach (var section in sections)
        {
            var ranking = BuildRanking(section.Athletes);
            
            foreach (var athlete in section.Athletes)
            {
                if (!profiles.TryGetValue(athlete.AthleteId, out var profile))
                {
                    profile = new AthleteReportProfile
                    {
                        AthleteId = athlete.AthleteId,
                        AthleteName = athlete.AthleteName,
                        CategoryName = athlete.CategoryName
                    };
                    profiles[athlete.AthleteId] = profile;
                }
                
                profile.TotalRuns++;
                profile.RunHistory.Add(ranking.FirstOrDefault(r => r.VideoId == athlete.VideoId) 
                    ?? new RankingEntry { AthleteId = athlete.AthleteId, TotalTimeMs = athlete.TotalMs });
                
                // Actualizar mejor tiempo
                if (athlete.TotalMs > 0 && (profile.PersonalBestMs == 0 || athlete.TotalMs < profile.PersonalBestMs))
                {
                    profile.PersonalBestMs = athlete.TotalMs;
                }
                
                // Actualizar mejor posición
                var position = ranking.FirstOrDefault(r => r.VideoId == athlete.VideoId)?.Position ?? 0;
                if (position > 0 && (profile.BestPosition == 0 || position < profile.BestPosition))
                {
                    profile.BestPosition = position;
                }
            }
        }
        
        // Calcular tiempos medios
        foreach (var profile in profiles.Values)
        {
            var times = profile.RunHistory.Where(r => r.TotalTimeMs > 0).Select(r => r.TotalTimeMs).ToList();
            profile.AverageTimeMs = times.Count > 0 ? (long)times.Average() : 0;
        }
        
        return profiles.Values.OrderBy(p => p.BestPosition).ToList();
    }
    
    /// <summary>
    /// Construye análisis de penalizaciones
    /// </summary>
    private static List<PenaltyAnalysis> BuildPenaltyAnalysis(
        IReadOnlyList<SectionWithDetailedAthleteRows> sections)
    {
        var penaltyByAthlete = new Dictionary<int, PenaltyAnalysis>();
        
        foreach (var section in sections)
        {
            foreach (var athlete in section.Athletes)
            {
                if (!penaltyByAthlete.TryGetValue(athlete.AthleteId, out var analysis))
                {
                    analysis = new PenaltyAnalysis
                    {
                        AthleteId = athlete.AthleteId,
                        AthleteName = athlete.AthleteName
                    };
                    penaltyByAthlete[athlete.AthleteId] = analysis;
                }
                
                analysis.TotalRuns++;
                if (athlete.PenaltyMs > 0)
                {
                    analysis.RunsWithPenalty++;
                    analysis.TotalPenaltyMs += athlete.PenaltyMs;
                }
            }
        }
        
        return penaltyByAthlete.Values
            .OrderByDescending(a => a.TotalPenaltyMs)
            .ToList();
    }
    
    /// <summary>
    /// Calcula el percentil de una lista ordenada
    /// </summary>
    private static double Percentile(List<long> sortedData, double percentile)
    {
        if (sortedData.Count == 0) return 0;
        if (sortedData.Count == 1) return sortedData[0];
        
        var n = sortedData.Count;
        var k = (percentile / 100.0) * (n - 1);
        var f = Math.Floor(k);
        var c = Math.Ceiling(k);
        
        if (Math.Abs(f - c) < 0.001)
            return sortedData[(int)k];
        
        var d0 = sortedData[(int)f] * (c - k);
        var d1 = sortedData[(int)c] * (k - f);
        return d0 + d1;
    }
}
