using System.Globalization;
using System.Text;
using CrownRFEP_Reader.Models;
using SkiaSharp;

namespace CrownRFEP_Reader.Services;

public class TableExportService : ITableExportService
{
    public string BuildDetailedSectionTimesHtml(Session session, IReadOnlyList<SectionWithDetailedAthleteRows> sections, int? referenceAthleteId = null, int? referenceVideoId = null, string? referenceAthleteName = null)
        => BuildHtmlStatic(session, sections, referenceAthleteId, referenceVideoId, referenceAthleteName);

    public Task<string> ExportDetailedSectionTimesToHtmlAsync(Session session, IReadOnlyList<SectionWithDetailedAthleteRows> sections, int? referenceAthleteId = null, int? referenceVideoId = null, string? referenceAthleteName = null)
    {
        var fileName = BuildFileName(session, "tiempos_por_seccion", "html");
        var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

        // Usar versión interactiva con selector para exportación
        var html = BuildHtmlInteractive(session, sections, referenceAthleteId, referenceVideoId, referenceAthleteName);
        File.WriteAllText(filePath, html, Encoding.UTF8);

        return Task.FromResult(filePath);
    }

    public Task<string> ExportDetailedSectionTimesToPdfAsync(Session session, IReadOnlyList<SectionWithDetailedAthleteRows> sections, int? referenceAthleteId = null, int? referenceVideoId = null, string? referenceAthleteName = null)
    {
        var fileName = BuildFileName(session, "tiempos_por_seccion", "pdf");
        var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

        ExportPdfWithSkia(filePath, session, sections, referenceAthleteId, referenceVideoId, referenceAthleteName);
        return Task.FromResult(filePath);
    }

    /// <summary>
    /// Genera HTML estático para el WebView de la aplicación (sin selector interactivo).
    /// El atleta de referencia se muestra de forma fija.
    /// </summary>
    private static string BuildHtmlStatic(Session session, IReadOnlyList<SectionWithDetailedAthleteRows> sections, int? referenceAthleteId, int? referenceVideoId, string? referenceAthleteName)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"es\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine($"  <title>{HtmlEscape($"Tiempos por Sección - {session.DisplayName}")}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { color-scheme: light; }");
        sb.AppendLine("    body { font-family: -apple-system, system-ui, Segoe UI, Roboto, Helvetica, Arial, sans-serif; background:#fff; color:#111; margin:24px; }");
        sb.AppendLine("    h1 { margin:0 0 6px 0; font-size:22px; }");
        sb.AppendLine("    .subtitle { color:#555; margin:0 0 4px 0; }");
        sb.AppendLine("    .reference { color:#1B5E20; margin:0 0 18px 0; font-weight:600; }");
        sb.AppendLine("    .section { margin:18px 0 26px 0; }");
        sb.AppendLine("    .section h2 { color:#0B5D74; margin:0 0 10px 0; font-size:16px; }");
        sb.AppendLine("    .table-title { margin:10px 0 8px 0; color:#333; font-weight:700; }");
        sb.AppendLine("    .chart-title { margin:14px 0 8px 0; color:#333; font-weight:700; }");
        sb.AppendLine("    .chart { width:100%; border:1px solid #D0D0D0; background:#fff; padding:10px; box-sizing:border-box; }");
        sb.AppendLine("    .chart svg { width:100%; height:220px; display:block; }");
        sb.AppendLine("    table { width:100%; border-collapse:collapse; background:#fff; border:1px solid #D0D0D0; }");
        sb.AppendLine("    thead th { background:#F2F2F2; color:#333; text-align:left; font-size:12px; padding:10px; border-bottom:1px solid #D0D0D0; }");
        sb.AppendLine("    tbody tr:nth-child(even) { background:#FAFAFA; }");
        sb.AppendLine("    tbody td { padding:10px; border-top:1px solid #E2E2E2; vertical-align:top; }");
        sb.AppendLine("    .mono { font-family: ui-monospace, Menlo, Consolas, monospace; }");
        sb.AppendLine("    .num { text-align:right; white-space:nowrap; }");
        sb.AppendLine("    .foot { margin-top:16px; color:#555; font-size:12px; }");
        sb.AppendLine("    @media print { body { margin: 0; } .section { break-inside: avoid; } table { break-inside: avoid; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine($"  <h1>Tiempos por Sección</h1>");
        sb.AppendLine($"  <div class=\"subtitle\">{HtmlEscape(session.DisplayName)} · Exportado {DateTime.Now:yyyy-MM-dd HH:mm}</div>");
        if (!string.IsNullOrWhiteSpace(referenceAthleteName))
            sb.AppendLine($"  <div class=\"reference\">Informe para: {HtmlEscape(referenceAthleteName)}</div>");

        foreach (var section in sections.OrderBy(s => s.Section))
        {
            var maxLapIndex = Math.Max(1, section.Athletes.SelectMany(a => a.Laps).Select(l => l.LapIndex).DefaultIfEmpty(0).Max());

            sb.AppendLine("  <div class=\"section\">");
            sb.AppendLine($"    <h2>{HtmlEscape(section.SectionName)}</h2>");

            AppendHtmlMinMaxChart(sb, section, maxLapIndex, isCumulative: false, referenceAthleteId, referenceVideoId);
            AppendHtmlLapTable(sb, section, maxLapIndex, isCumulative: false);

            AppendHtmlMinMaxChart(sb, section, maxLapIndex, isCumulative: true, referenceAthleteId, referenceVideoId);
            AppendHtmlLapTable(sb, section, maxLapIndex, isCumulative: true);

            sb.AppendLine("  </div>");
        }

        sb.AppendLine("  <div class=\"foot\">Lap = tiempo parcial · Acum. = tiempo acumulado</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>
    /// Genera HTML interactivo para exportación con selector de atleta integrado.
    /// </summary>
    private static string BuildHtmlInteractive(Session session, IReadOnlyList<SectionWithDetailedAthleteRows> sections, int? referenceAthleteId, int? referenceVideoId, string? referenceAthleteName)
    {
        var sb = new StringBuilder();

        // Construir lista de atletas únicos para el selector
        var athleteOptions = BuildAthleteOptionsJson(sections);

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"es\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine($"  <title>{HtmlEscape($"Tiempos por Sección - {session.DisplayName}")}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { color-scheme: light; }");
        sb.AppendLine("    body { font-family: -apple-system, system-ui, Segoe UI, Roboto, Helvetica, Arial, sans-serif; background:#fff; color:#111; margin:24px; }");
        sb.AppendLine("    h1 { margin:0 0 6px 0; font-size:22px; }");
        sb.AppendLine("    .header-row { display:flex; align-items:center; gap:16px; margin:0 0 4px 0; flex-wrap:wrap; }");
        sb.AppendLine("    .subtitle { color:#555; margin:0 0 4px 0; }");
        sb.AppendLine("    .reference { color:#1B5E20; margin:0 0 18px 0; font-weight:600; }");
        sb.AppendLine("    .athlete-selector { display:flex; align-items:center; gap:8px; margin:8px 0 18px 0; }");
        sb.AppendLine("    .athlete-selector label { font-weight:600; color:#333; }");
        sb.AppendLine("    .athlete-selector select { padding:8px 12px; font-size:14px; border:1px solid #D0D0D0; border-radius:4px; background:#fff; min-width:200px; }");
        sb.AppendLine("    .section { margin:18px 0 26px 0; }");
        sb.AppendLine("    .section h2 { color:#0B5D74; margin:0 0 10px 0; font-size:16px; }");
        sb.AppendLine("    .table-title { margin:10px 0 8px 0; color:#333; font-weight:700; }");
        sb.AppendLine("    .chart-title { margin:14px 0 8px 0; color:#333; font-weight:700; }");
        sb.AppendLine("    .chart { width:100%; border:1px solid #D0D0D0; background:#fff; padding:10px; box-sizing:border-box; }");
        sb.AppendLine("    .chart svg { width:100%; height:220px; display:block; }");
        sb.AppendLine("    table { width:100%; border-collapse:collapse; background:#fff; border:1px solid #D0D0D0; }");
        sb.AppendLine("    thead th { background:#F2F2F2; color:#333; text-align:left; font-size:12px; padding:10px; border-bottom:1px solid #D0D0D0; }");
        sb.AppendLine("    tbody tr:nth-child(even) { background:#FAFAFA; }");
        sb.AppendLine("    tbody tr.selected { background:#E8F5E9 !important; }");
        sb.AppendLine("    tbody td { padding:10px; border-top:1px solid #E2E2E2; vertical-align:top; }");
        sb.AppendLine("    .mono { font-family: ui-monospace, Menlo, Consolas, monospace; }");
        sb.AppendLine("    .num { text-align:right; white-space:nowrap; }");
        sb.AppendLine("    .foot { margin-top:16px; color:#555; font-size:12px; }");
        sb.AppendLine("    .selected-point { display:none; }");
        sb.AppendLine("    .selected-point.active { display:block; }");
        sb.AppendLine("    @media print { body { margin: 0; } .section { break-inside: avoid; } table { break-inside: avoid; } .athlete-selector { display:none; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine($"  <h1>Tiempos por Sección</h1>");
        sb.AppendLine($"  <div class=\"subtitle\">{HtmlEscape(session.DisplayName)} · Exportado {DateTime.Now:yyyy-MM-dd HH:mm}</div>");
        
        // Selector de atleta interactivo
        sb.AppendLine("  <div class=\"athlete-selector\">");
        sb.AppendLine("    <label for=\"athleteSelect\">Comparar con:</label>");
        sb.AppendLine("    <select id=\"athleteSelect\" onchange=\"updateSelectedAthlete()\">");
        sb.AppendLine("      <option value=\"\">— Ninguno —</option>");
        // Las opciones se añadirán por JavaScript
        sb.AppendLine("    </select>");
        sb.AppendLine("    <span id=\"referenceLabel\" class=\"reference\" style=\"margin:0;\"></span>");
        sb.AppendLine("  </div>");

        foreach (var section in sections.OrderBy(s => s.Section))
        {
            var maxLapIndex = Math.Max(1, section.Athletes.SelectMany(a => a.Laps).Select(l => l.LapIndex).DefaultIfEmpty(0).Max());

            sb.AppendLine($"  <div class=\"section\" data-section=\"{section.Section}\">");
            sb.AppendLine($"    <h2>{HtmlEscape(section.SectionName)}</h2>");

            AppendHtmlMinMaxChartInteractive(sb, section, maxLapIndex, isCumulative: false);
            AppendHtmlLapTableInteractive(sb, section, maxLapIndex, isCumulative: false);

            AppendHtmlMinMaxChartInteractive(sb, section, maxLapIndex, isCumulative: true);
            AppendHtmlLapTableInteractive(sb, section, maxLapIndex, isCumulative: true);

            sb.AppendLine("  </div>");
        }

        sb.AppendLine("  <div class=\"foot\">Lap = tiempo parcial · Acum. = tiempo acumulado</div>");

        // Datos embebidos para interactividad
        sb.AppendLine("  <script>");
        sb.AppendLine($"    const athleteData = {athleteOptions};");
        sb.AppendLine($"    const initialAthleteId = {(referenceAthleteId.HasValue ? referenceAthleteId.Value.ToString() : "null")};");
        sb.AppendLine($"    const initialVideoId = {(referenceVideoId.HasValue ? referenceVideoId.Value.ToString() : "null")};");
        AppendJavaScript(sb);
        sb.AppendLine("  </script>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string BuildAthleteOptionsJson(IReadOnlyList<SectionWithDetailedAthleteRows> sections)
    {
        // Construir estructura de datos con todos los atletas y sus tiempos por sección
        var athletes = new List<object>();
        var seen = new HashSet<(int athleteId, int videoId)>();

        foreach (var section in sections)
        {
            foreach (var athlete in section.Athletes)
            {
                var key = (athlete.AthleteId, athlete.VideoId);
                if (seen.Contains(key))
                    continue;
                seen.Add(key);

                athletes.Add(new
                {
                    id = athlete.AthleteId,
                    videoId = athlete.VideoId,
                    name = athlete.AthleteName,
                    displayName = athlete.DisplayName,
                    attemptNumber = athlete.AttemptNumber,
                    totalAttempts = athlete.TotalAttempts,
                    isBest = athlete.IsBestAttempt
                });
            }
        }

        // Construir también los tiempos por sección para cada atleta
        var sectionData = new List<object>();
        foreach (var section in sections.OrderBy(s => s.Section))
        {
            var maxLapIndex = Math.Max(1, section.Athletes.SelectMany(a => a.Laps).Select(l => l.LapIndex).DefaultIfEmpty(0).Max());
            
            var athleteTimes = new List<object>();
            foreach (var athlete in section.Athletes)
            {
                var lapTimes = new List<object>();
                for (var i = 1; i <= maxLapIndex; i++)
                {
                    var lap = athlete.Laps.FirstOrDefault(l => l.LapIndex == i);
                    lapTimes.Add(new
                    {
                        lapIndex = i,
                        splitMs = lap?.SplitMs ?? 0,
                        cumulativeMs = lap?.CumulativeMs ?? 0
                    });
                }

                athleteTimes.Add(new
                {
                    athleteId = athlete.AthleteId,
                    videoId = athlete.VideoId,
                    displayName = athlete.DisplayName,
                    totalMs = athlete.TotalMs,
                    laps = lapTimes
                });
            }

            // Calcular min/max para cada columna
            var minMaxPartial = new List<object>();
            var minMaxCumulative = new List<object>();
            
            for (var i = 1; i <= maxLapIndex; i++)
            {
                var partialValues = section.Athletes
                    .Select(a => a.Laps.FirstOrDefault(l => l.LapIndex == i)?.SplitMs ?? 0)
                    .Where(v => v > 0)
                    .ToList();
                var cumulValues = section.Athletes
                    .Select(a => a.Laps.FirstOrDefault(l => l.LapIndex == i)?.CumulativeMs ?? 0)
                    .Where(v => v > 0)
                    .ToList();

                minMaxPartial.Add(new
                {
                    label = $"P{i}",
                    min = partialValues.Count > 0 ? partialValues.Min() : 0,
                    max = partialValues.Count > 0 ? partialValues.Max() : 0,
                    mean = partialValues.Count > 0 ? (long)partialValues.Average() : 0
                });
                minMaxCumulative.Add(new
                {
                    label = $"P{i}",
                    min = cumulValues.Count > 0 ? cumulValues.Min() : 0,
                    max = cumulValues.Count > 0 ? cumulValues.Max() : 0,
                    mean = cumulValues.Count > 0 ? (long)cumulValues.Average() : 0
                });
            }

            // TOTAL
            var totalValues = section.Athletes.Select(a => a.TotalMs).Where(v => v > 0).ToList();
            minMaxPartial.Add(new { label = "TOTAL", min = totalValues.Count > 0 ? totalValues.Min() : 0, max = totalValues.Count > 0 ? totalValues.Max() : 0, mean = totalValues.Count > 0 ? (long)totalValues.Average() : 0 });
            minMaxCumulative.Add(new { label = "TOTAL", min = totalValues.Count > 0 ? totalValues.Min() : 0, max = totalValues.Count > 0 ? totalValues.Max() : 0, mean = totalValues.Count > 0 ? (long)totalValues.Average() : 0 });

            sectionData.Add(new
            {
                section = section.Section,
                sectionName = section.SectionName,
                maxLapIndex = maxLapIndex,
                athletes = athleteTimes,
                minMaxPartial = minMaxPartial,
                minMaxCumulative = minMaxCumulative
            });
        }

        var data = new { athletes = athletes, sections = sectionData };
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

    private static void AppendJavaScript(StringBuilder sb)
    {
        sb.AppendLine(@"
    function formatTime(ms) {
      if (!ms || ms <= 0) return '-';
      const totalSeconds = Math.floor(ms / 1000);
      const centiseconds = Math.floor((ms % 1000) / 10);
      return totalSeconds + ',' + String(centiseconds).padStart(2, '0');
    }

    function populateSelector() {
      const select = document.getElementById('athleteSelect');
      athleteData.athletes.forEach(a => {
        const opt = document.createElement('option');
        opt.value = a.id + '_' + a.videoId;
        opt.textContent = a.displayName;
        if (a.totalAttempts > 1 && a.isBest) {
          opt.textContent += ' (Mejor)';
        }
        select.appendChild(opt);
      });

      // Seleccionar el atleta inicial si existe
      if (initialAthleteId && initialVideoId) {
        select.value = initialAthleteId + '_' + initialVideoId;
        updateSelectedAthlete();
      } else if (initialAthleteId) {
        // Buscar el mejor intento
        const best = athleteData.athletes.find(a => a.id === initialAthleteId && a.isBest);
        if (best) {
          select.value = best.id + '_' + best.videoId;
          updateSelectedAthlete();
        }
      }
    }

    function updateSelectedAthlete() {
      const select = document.getElementById('athleteSelect');
      const val = select.value;
      const label = document.getElementById('referenceLabel');
      
      // Ocultar todos los puntos seleccionados
      document.querySelectorAll('.selected-point').forEach(el => el.classList.remove('active'));
      document.querySelectorAll('tbody tr.selected').forEach(el => el.classList.remove('selected'));

      if (!val) {
        label.textContent = '';
        return;
      }

      const [athleteId, videoId] = val.split('_').map(Number);
      const athlete = athleteData.athletes.find(a => a.id === athleteId && a.videoId === videoId);
      
      if (athlete) {
        label.textContent = 'Informe para: ' + athlete.displayName;
      }

      // Activar los puntos correspondientes en los gráficos
      document.querySelectorAll(`[data-athlete-id='${athleteId}'][data-video-id='${videoId}']`).forEach(el => {
        el.classList.add('active');
      });

      // Marcar fila en tablas
      document.querySelectorAll(`tr[data-athlete-id='${athleteId}'][data-video-id='${videoId}']`).forEach(el => {
        el.classList.add('selected');
      });

      // Actualizar etiquetas de diferencia en los gráficos
      athleteData.sections.forEach(section => {
        const athleteRow = section.athletes.find(a => a.athleteId === athleteId && a.videoId === videoId);
        if (!athleteRow) return;

        // Actualizar gráficos parciales
        updateChartLabels(section.section, 'partial', athleteRow, section.minMaxPartial);
        // Actualizar gráficos acumulados
        updateChartLabels(section.section, 'cumulative', athleteRow, section.minMaxCumulative);
      });
    }

    function updateChartLabels(sectionId, chartType, athleteRow, minMaxData) {
      const maxLap = athleteRow.laps.length;
      
      for (let i = 0; i < minMaxData.length; i++) {
        const mm = minMaxData[i];
        const labelEl = document.getElementById(`label-${sectionId}-${chartType}-${i}`);
        if (!labelEl) continue;

        let athleteMs;
        if (i < maxLap) {
          const lap = athleteRow.laps[i];
          athleteMs = chartType === 'partial' ? lap.splitMs : lap.cumulativeMs;
        } else {
          athleteMs = athleteRow.totalMs;
        }

        if (!athleteMs || athleteMs <= 0) {
          labelEl.textContent = '';
          continue;
        }

        let text = formatTime(athleteMs);
        if (mm.min > 0 && athleteMs > mm.min) {
          const diffMs = athleteMs - mm.min;
          const diffPct = (diffMs * 100) / mm.min;
          text += ` (+${formatTime(diffMs)} / +${diffPct.toFixed(1)}%)`;
        }
        labelEl.textContent = text;
      }
    }

    // Inicializar al cargar
    document.addEventListener('DOMContentLoaded', populateSelector);
");
    }

    private static void AppendHtmlMinMaxChartInteractive(
        StringBuilder sb,
        SectionWithDetailedAthleteRows section,
        int maxLapIndex,
        bool isCumulative)
    {
        var points = BuildMinMaxPoints(section, maxLapIndex, isCumulative, null, null);
        // Verificar que hay datos válidos
        if (!points.Any(p => p.MinMs.HasValue && p.MaxMs.HasValue))
            return;

        var chartType = isCumulative ? "cumulative" : "partial";

        const float viewW = 1000f;
        const float viewH = 260f;
        const float padT = 32f;
        const float padR = 10f;
        const float padB = 28f;
        const float padL = 20f;
        var plotW = viewW - padL - padR;
        var plotH = viewH - padT - padB;

        var colCount = points.Count;
        var colW = plotW / Math.Max(1, colCount);

        string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        float X(int i) => padL + (colW * i) + (colW / 2f);

        float YNorm(MinMaxPoint p, long ms)
        {
            if (p.MinMs is null || p.MaxMs is null || p.MaxMs == p.MinMs)
                return padT + plotH / 2f;
            var range = p.MaxMs.Value - p.MinMs.Value;
            var t = Math.Clamp((ms - p.MinMs.Value) / (double)range, 0, 1);
            var usableH = plotH * 0.7f;
            var topMargin = plotH * 0.15f;
            return padT + topMargin + usableH * (float)(1 - t);
        }

        sb.AppendLine($"    <div class=\"chart-title\">Min–Max {(isCumulative ? "Acumulados" : "Parciales")} (normalizado)</div>");
        sb.AppendLine("    <div class=\"chart\">");
        sb.AppendLine($"      <svg viewBox=\"0 0 {F(viewW)} {F(viewH)}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"Min–Max {(isCumulative ? "acumulados" : "parciales")}\">");

        sb.AppendLine($"        <rect x=\"0\" y=\"0\" width=\"{F(viewW)}\" height=\"{F(viewH)}\" fill=\"#fff\" />");

        // Líneas verticales
        for (var i = 0; i <= colCount; i++)
        {
            var xLine = padL + (colW * i);
            sb.AppendLine($"        <line x1=\"{F(xLine)}\" y1=\"{F(padT)}\" x2=\"{F(xLine)}\" y2=\"{F(padT + plotH)}\" stroke=\"#E8E8E8\" stroke-width=\"1\" />");
        }
        sb.AppendLine($"        <line x1=\"{F(padL)}\" y1=\"{F(padT + plotH)}\" x2=\"{F(padL + plotW)}\" y2=\"{F(padT + plotH)}\" stroke=\"#D0D0D0\" stroke-width=\"1\" />");

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.MinMs is null || p.MaxMs is null)
                continue;

            var x = X(i);
            var yMinPx = YNorm(p, p.MinMs.Value);
            var yMaxPx = YNorm(p, p.MaxMs.Value);

            // Rango min-max
            sb.AppendLine($"        <line x1=\"{F(x)}\" y1=\"{F(yMaxPx)}\" x2=\"{F(x)}\" y2=\"{F(yMinPx)}\" stroke=\"#0B5D74\" stroke-width=\"3\" />");
            sb.AppendLine($"        <circle cx=\"{F(x)}\" cy=\"{F(yMaxPx)}\" r=\"5\" fill=\"#fff\" stroke=\"#0B5D74\" stroke-width=\"2\" />");
            sb.AppendLine($"        <circle cx=\"{F(x)}\" cy=\"{F(yMinPx)}\" r=\"5\" fill=\"#fff\" stroke=\"#0B5D74\" stroke-width=\"2\" />");

            // Etiquetas de tiempo
            sb.AppendLine($"        <text x=\"{F(x)}\" y=\"{F(yMaxPx - 10)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#0B5D74\">{HtmlEscape(FormatTimeMs(p.MaxMs.Value))}</text>");
            sb.AppendLine($"        <text x=\"{F(x)}\" y=\"{F(yMinPx + 14)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#0B5D74\">{HtmlEscape(FormatTimeMs(p.MinMs.Value))}</text>");

            // Media
            if (p.MeanMs is not null)
            {
                var ym = YNorm(p, p.MeanMs.Value);
                sb.AppendLine($"        <rect x=\"{F(x - 4)}\" y=\"{F(ym - 4)}\" width=\"8\" height=\"8\" fill=\"#616161\" transform=\"rotate(45 {F(x)} {F(ym)})\" />");
            }

            // Puntos de atletas seleccionados (uno por cada atleta, ocultos inicialmente)
            foreach (var athlete in section.Athletes)
            {
                long? athleteMs = null;
                if (i < maxLapIndex)
                {
                    var lap = athlete.Laps.FirstOrDefault(l => l.LapIndex == i + 1);
                    athleteMs = isCumulative ? lap?.CumulativeMs : lap?.SplitMs;
                }
                else
                {
                    athleteMs = athlete.TotalMs;
                }

                if (athleteMs.HasValue && athleteMs.Value > 0)
                {
                    var ys = YNorm(p, athleteMs.Value);
                    sb.AppendLine($"        <g class=\"selected-point\" data-athlete-id=\"{athlete.AthleteId}\" data-video-id=\"{athlete.VideoId}\">");
                    sb.AppendLine($"          <circle cx=\"{F(x)}\" cy=\"{F(ys)}\" r=\"7\" fill=\"#1B5E20\" stroke=\"#111\" stroke-width=\"1\" />");
                    sb.AppendLine($"          <text id=\"label-{section.Section}-{chartType}-{i}\" x=\"{F(x + 12)}\" y=\"{F(ys + 4)}\" font-size=\"9\" text-anchor=\"start\" fill=\"#1B5E20\" font-weight=\"bold\"></text>");
                    sb.AppendLine($"        </g>");
                }
            }

            // Etiqueta de columna
            sb.AppendLine($"        <text x=\"{F(x)}\" y=\"{F(padT + plotH + 18)}\" font-size=\"11\" text-anchor=\"middle\" fill=\"#333\" font-weight=\"bold\">{HtmlEscape(p.Label)}</text>");
        }

        sb.AppendLine("      </svg>");
        sb.AppendLine("    </div>");
    }

    private static void AppendHtmlLapTableInteractive(StringBuilder sb, SectionWithDetailedAthleteRows section, int maxLapIndex, bool isCumulative)
    {
        sb.AppendLine($"    <div class=\"table-title\">{(isCumulative ? "Acumulados" : "Parciales")}</div>");
        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead>");
        sb.AppendLine("        <tr>");
        sb.AppendLine("          <th style=\"width:22%\">ATLETA</th>");
        sb.AppendLine("          <th style=\"width:10%\">CAT.</th>");
        for (var i = 1; i <= maxLapIndex; i++)
            sb.AppendLine($"          <th class=\"num mono\">P{i}</th>");
        sb.AppendLine("          <th class=\"num\" style=\"width:10%\">TIEMPO</th>");
        sb.AppendLine("          <th class=\"num\" style=\"width:8%\">PENAL.</th>");
        sb.AppendLine("          <th class=\"num\" style=\"width:10%\">TOTAL</th>");
        sb.AppendLine("        </tr>");
        sb.AppendLine("      </thead>");
        sb.AppendLine("      <tbody>");

        foreach (var athlete in section.Athletes)
        {
            sb.AppendLine($"        <tr data-athlete-id=\"{athlete.AthleteId}\" data-video-id=\"{athlete.VideoId}\">");
            sb.AppendLine($"          <td>{HtmlEscape(athlete.DisplayName)}</td>");
            sb.AppendLine($"          <td style=\"color:#B1B1B1\">{HtmlEscape(athlete.CategoryName)}</td>");

            for (var i = 1; i <= maxLapIndex; i++)
            {
                var lap = athlete.Laps.FirstOrDefault(l => l.LapIndex == i);
                if (lap == null)
                {
                    sb.AppendLine("          <td class=\"num mono\" style=\"color:#808080\">—</td>");
                }
                else
                {
                    var text = isCumulative ? lap.CumulativeFormatted : lap.SplitFormatted;
                    var color = isCumulative ? lap.CumulativeTextColor : lap.SplitTextColor;
                    sb.AppendLine($"          <td class=\"num mono\" style=\"color:{CssColor(color)}\">{HtmlEscape(text)}</td>");
                }
            }

            sb.AppendLine($"          <td class=\"num mono\">{HtmlEscape(athlete.DurationFormatted)}</td>");
            sb.AppendLine($"          <td class=\"num mono\">{HtmlEscape(athlete.PenaltyFormatted)}</td>");
            sb.AppendLine($"          <td class=\"num mono\" style=\"font-weight:700; color:{CssColor(athlete.TotalTextColor)}\">{HtmlEscape(athlete.TotalFormatted)}</td>");
            sb.AppendLine("        </tr>");
        }

        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
    }

    private static void AppendHtmlMinMaxChart(
        StringBuilder sb,
        SectionWithDetailedAthleteRows section,
        int maxLapIndex,
        bool isCumulative,
        int? referenceAthleteId,
        int? referenceVideoId)
    {
        var points = BuildMinMaxPoints(section, maxLapIndex, isCumulative, referenceAthleteId, referenceVideoId);
        // Verificar que hay datos válidos
        if (!points.Any(p => p.MinMs.HasValue && p.MaxMs.HasValue))
            return;

        const float viewW = 1000f;
        const float viewH = 260f;  // Más alto para etiquetas
        const float padT = 32f;    // Espacio para etiquetas superiores
        const float padR = 10f;
        const float padB = 28f;
        const float padL = 20f;    // Reducido (ya no hay eje Y global)
        var plotW = viewW - padL - padR;
        var plotH = viewH - padT - padB;

        // Ancho de cada columna
        var colCount = points.Count;
        var colW = plotW / Math.Max(1, colCount);

        // Formatea float con punto decimal (requerido para SVG)
        string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // Posición X centrada para cada columna
        float X(int i) => padL + (colW * i) + (colW / 2f);

        // Normaliza un valor dentro del rango de un punto (0=min, 1=max) con padding
        float YNorm(MinMaxPoint p, long ms)
        {
            if (p.MinMs is null || p.MaxMs is null || p.MaxMs == p.MinMs)
                return padT + plotH / 2f;
            var range = p.MaxMs.Value - p.MinMs.Value;
            var t = Math.Clamp((ms - p.MinMs.Value) / (double)range, 0, 1);
            // Dejamos 15% de margen arriba y abajo para etiquetas
            var usableH = plotH * 0.7f;
            var topMargin = plotH * 0.15f;
            return padT + topMargin + usableH * (float)(1 - t);
        }

        sb.AppendLine($"    <div class=\"chart-title\">Min–Max {(isCumulative ? "Acumulados" : "Parciales")} (normalizado)</div>");
        sb.AppendLine("    <div class=\"chart\">");
        sb.AppendLine($"      <svg viewBox=\"0 0 {F(viewW)} {F(viewH)}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"Min–Max {(isCumulative ? "acumulados" : "parciales")}\">");

        sb.AppendLine($"        <rect x=\"0\" y=\"0\" width=\"{F(viewW)}\" height=\"{F(viewH)}\" fill=\"#fff\" />");

        // Líneas verticales de separación entre columnas
        for (var i = 0; i <= colCount; i++)
        {
            var xLine = padL + (colW * i);
            sb.AppendLine($"        <line x1=\"{F(xLine)}\" y1=\"{F(padT)}\" x2=\"{F(xLine)}\" y2=\"{F(padT + plotH)}\" stroke=\"#E8E8E8\" stroke-width=\"1\" />");
        }
        // Línea base
        sb.AppendLine($"        <line x1=\"{F(padL)}\" y1=\"{F(padT + plotH)}\" x2=\"{F(padL + plotW)}\" y2=\"{F(padT + plotH)}\" stroke=\"#D0D0D0\" stroke-width=\"1\" />");

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p.MinMs is null || p.MaxMs is null)
                continue;

            var x = X(i);
            var yMinPx = YNorm(p, p.MinMs.Value);
            var yMaxPx = YNorm(p, p.MaxMs.Value);

            // Rango min-max normalizado (línea vertical + círculos)
            sb.AppendLine($"        <line x1=\"{F(x)}\" y1=\"{F(yMaxPx)}\" x2=\"{F(x)}\" y2=\"{F(yMinPx)}\" stroke=\"#0B5D74\" stroke-width=\"3\" />");
            sb.AppendLine($"        <circle cx=\"{F(x)}\" cy=\"{F(yMaxPx)}\" r=\"5\" fill=\"#fff\" stroke=\"#0B5D74\" stroke-width=\"2\" />");
            sb.AppendLine($"        <circle cx=\"{F(x)}\" cy=\"{F(yMinPx)}\" r=\"5\" fill=\"#fff\" stroke=\"#0B5D74\" stroke-width=\"2\" />");

            // Etiquetas de tiempo real (max arriba, min abajo)
            sb.AppendLine($"        <text x=\"{F(x)}\" y=\"{F(yMaxPx - 10)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#0B5D74\">{HtmlEscape(FormatTimeMs(p.MaxMs.Value))}</text>");
            sb.AppendLine($"        <text x=\"{F(x)}\" y=\"{F(yMinPx + 14)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#0B5D74\">{HtmlEscape(FormatTimeMs(p.MinMs.Value))}</text>");

            // Media (rombo gris)
            if (p.MeanMs is not null)
            {
                var ym = YNorm(p, p.MeanMs.Value);
                sb.AppendLine($"        <rect x=\"{F(x - 4)}\" y=\"{F(ym - 4)}\" width=\"8\" height=\"8\" fill=\"#616161\" transform=\"rotate(45 {F(x)} {F(ym)})\" />");
            }

            // Atleta seleccionado (círculo verde)
            if (p.SelectedMs is not null)
            {
                var ys = YNorm(p, p.SelectedMs.Value);
                sb.AppendLine($"        <circle cx=\"{F(x)}\" cy=\"{F(ys)}\" r=\"7\" fill=\"#1B5E20\" stroke=\"#111\" stroke-width=\"1\" />");
                // Etiqueta del tiempo del atleta seleccionado con diferencia respecto al mejor
                var diffText = "";
                if (p.MinMs.HasValue && p.MinMs.Value > 0 && p.SelectedMs.Value > p.MinMs.Value)
                {
                    var diffMs = p.SelectedMs.Value - p.MinMs.Value;
                    var diffPct = (diffMs * 100.0) / p.MinMs.Value;
                    diffText = $" (+{FormatTimeMs(diffMs)} / +{diffPct:0.0}%)";
                }
                sb.AppendLine($"        <text x=\"{F(x + 12)}\" y=\"{F(ys + 4)}\" font-size=\"9\" text-anchor=\"start\" fill=\"#1B5E20\" font-weight=\"bold\">{HtmlEscape(FormatTimeMs(p.SelectedMs.Value) + diffText)}</text>");
            }

            // Etiqueta de la columna (P1, P2, ..., TOTAL)
            sb.AppendLine($"        <text x=\"{F(x)}\" y=\"{F(padT + plotH + 18)}\" font-size=\"11\" text-anchor=\"middle\" fill=\"#333\" font-weight=\"bold\">{HtmlEscape(p.Label)}</text>");
        }

        sb.AppendLine("      </svg>");
        sb.AppendLine("    </div>");
    }

    private static void AppendHtmlLapTable(StringBuilder sb, SectionWithDetailedAthleteRows section, int maxLapIndex, bool isCumulative)
    {
        sb.AppendLine($"    <div class=\"table-title\">{(isCumulative ? "Acumulados" : "Parciales")}</div>");
        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead>");
        sb.AppendLine("        <tr>");
        sb.AppendLine("          <th style=\"width:22%\">ATLETA</th>");
        sb.AppendLine("          <th style=\"width:10%\">CAT.</th>");
        for (var i = 1; i <= maxLapIndex; i++)
            sb.AppendLine($"          <th class=\"num mono\">P{i}</th>");
        sb.AppendLine("          <th class=\"num\" style=\"width:10%\">TIEMPO</th>");
        sb.AppendLine("          <th class=\"num\" style=\"width:8%\">PENAL.</th>");
        sb.AppendLine("          <th class=\"num\" style=\"width:10%\">TOTAL</th>");
        sb.AppendLine("        </tr>");
        sb.AppendLine("      </thead>");
        sb.AppendLine("      <tbody>");

        foreach (var athlete in section.Athletes)
        {
            sb.AppendLine("        <tr>");
            sb.AppendLine($"          <td>{HtmlEscape(athlete.DisplayName)}</td>");
            sb.AppendLine($"          <td style=\"color:#B1B1B1\">{HtmlEscape(athlete.CategoryName)}</td>");

            for (var i = 1; i <= maxLapIndex; i++)
            {
                var lap = athlete.Laps.FirstOrDefault(l => l.LapIndex == i);
                if (lap == null)
                {
                    sb.AppendLine("          <td class=\"num mono\" style=\"color:#808080\">—</td>");
                }
                else
                {
                    var text = isCumulative ? lap.CumulativeFormatted : lap.SplitFormatted;
                    var color = isCumulative ? lap.CumulativeTextColor : lap.SplitTextColor;
                    sb.AppendLine($"          <td class=\"num mono\" style=\"color:{CssColor(color)}\">{HtmlEscape(text)}</td>");
                }
            }

            sb.AppendLine($"          <td class=\"num mono\">{HtmlEscape(athlete.DurationFormatted)}</td>");
            sb.AppendLine($"          <td class=\"num mono\">{HtmlEscape(athlete.PenaltyFormatted)}</td>");
            sb.AppendLine($"          <td class=\"num mono\" style=\"font-weight:700; color:{CssColor(athlete.TotalTextColor)}\">{HtmlEscape(athlete.TotalFormatted)}</td>");
            sb.AppendLine("        </tr>");
        }

        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
    }

    private static void ExportPdfWithSkia(string filePath, Session session, IReadOnlyList<SectionWithDetailedAthleteRows> sections, int? referenceAthleteId, int? referenceVideoId, string? referenceAthleteName)
    {
        // A4 apaisado en puntos (72 dpi): 842 x 595
        const float pageWidth = 842;
        const float pageHeight = 595;
        const float margin = 24;

        using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var document = SKDocument.CreatePdf(stream);
        if (document == null)
            throw new InvalidOperationException("No se pudo inicializar el generador PDF.");

        // Estilo imprimible (fondo blanco y texto oscuro)
        var headerPaint = new SKPaint { Color = ParseColor("#FF111111"), IsAntialias = true, TextSize = 18, Typeface = SKTypeface.Default };
        var subtitlePaint = new SKPaint { Color = ParseColor("#FF555555"), IsAntialias = true, TextSize = 11, Typeface = SKTypeface.Default };
        var sectionPaint = new SKPaint { Color = ParseColor("#FF0B5D74"), IsAntialias = true, TextSize = 13, Typeface = SKTypeface.Default };
        var thPaint = new SKPaint { Color = ParseColor("#FF333333"), IsAntialias = true, TextSize = 9, Typeface = SKTypeface.Default, FakeBoldText = true };
        var tdPaint = new SKPaint { Color = ParseColor("#FF111111"), IsAntialias = true, TextSize = 10, Typeface = SKTypeface.Default };
        var monoPaint = new SKPaint { Color = ParseColor("#FF111111"), IsAntialias = true, TextSize = 10, Typeface = SKTypeface.FromFamilyName("Courier") ?? SKTypeface.Default };
        var smallGrey = new SKPaint { Color = ParseColor("#FF666666"), IsAntialias = true, TextSize = 9, Typeface = SKTypeface.Default };

        var bgHeader = ParseColor("#FFF2F2F2");
        var bgRow = ParseColor("#FFFFFFFF");
        var bgRowAlt = ParseColor("#FFFAFAFA");
        var grid = ParseColor("#FFD0D0D0");

        SKCanvas? canvas = null;
        float y = 0;

        void BeginPage()
        {
            canvas = document.BeginPage(pageWidth, pageHeight);
            if (canvas == null)
                throw new InvalidOperationException("No se pudo crear la página del PDF.");

            canvas.Clear(ParseColor("#FFFFFFFF"));
            y = margin;

            // Header
            canvas.DrawText("Tiempos por Sección", margin, y + headerPaint.TextSize, headerPaint);
            y += headerPaint.TextSize + 6;

            canvas.DrawText(session.DisplayName, margin, y + subtitlePaint.TextSize, subtitlePaint);
            y += subtitlePaint.TextSize + 2;

            canvas.DrawText($"Exportado {DateTime.Now:yyyy-MM-dd HH:mm}", margin, y + subtitlePaint.TextSize, subtitlePaint);
            y += subtitlePaint.TextSize + 2;

            if (!string.IsNullOrWhiteSpace(referenceAthleteName))
            {
                var refPaint = ClonePaint(subtitlePaint);
                refPaint.Color = ParseColor("#FF1B5E20");
                refPaint.FakeBoldText = true;
                canvas.DrawText($"Informe para: {referenceAthleteName}", margin, y + refPaint.TextSize, refPaint);
                y += refPaint.TextSize + 2;
            }

            y += 12;
        }

        void EndPage()
        {
            if (canvas == null) return;
            // Footer
            var footer = $"CrownRFEP Reader · {DateTime.Now:yyyy-MM-dd}";
            var footerWidth = MeasureText(smallGrey, footer);
            canvas.DrawText(footer, (pageWidth - footerWidth) / 2f, pageHeight - margin, smallGrey);
            document.EndPage();
            canvas = null;
        }

        void EnsureSpace(float needed)
        {
            if (canvas == null)
                BeginPage();
            if (y + needed > pageHeight - margin - 18)
            {
                EndPage();
                BeginPage();
            }
        }

        BeginPage();

        foreach (var section in sections.OrderBy(s => s.Section))
        {
            EnsureSpace(28);
            canvas!.DrawText(section.SectionName, margin, y + sectionPaint.TextSize, sectionPaint);
            y += sectionPaint.TextSize + 10;

            DrawPdfMinMaxPlot(section, isCumulative: false, referenceAthleteId, referenceVideoId);
            y += 10;
            DrawPdfLapTable(section, isCumulative: false);
            y += 12;

            DrawPdfMinMaxPlot(section, isCumulative: true, referenceAthleteId, referenceVideoId);
            y += 10;
            DrawPdfLapTable(section, isCumulative: true);
            y += 16;
        }

        EnsureSpace(18);
        canvas!.DrawText("Lap = tiempo parcial · Acum. = tiempo acumulado", margin, y + subtitlePaint.TextSize, subtitlePaint);
        y += subtitlePaint.TextSize + 8;

        EndPage();
        document.Close();

        void DrawPdfMinMaxPlot(SectionWithDetailedAthleteRows section, bool isCumulative, int? referenceAthleteId, int? referenceVideoId)
        {
            var titlePaint = ClonePaint(subtitlePaint);
            titlePaint.FakeBoldText = true;

            var maxLapIndex = Math.Max(1, section.Athletes.SelectMany(a => a.Laps).Select(l => l.LapIndex).DefaultIfEmpty(0).Max());
            var points = BuildMinMaxPoints(section, maxLapIndex, isCumulative, referenceAthleteId, referenceVideoId);
            // Verificar que hay datos válidos
            if (!points.Any(p => p.MinMs.HasValue && p.MaxMs.HasValue))
                return;

            const float chartH = 140f; // Más alto para etiquetas
            var chartW = pageWidth - (margin * 2);

            EnsureSpace(chartH + 28);
            canvas!.DrawText($"Min–Max {(isCumulative ? "Acumulados" : "Parciales")} (normalizado)", margin, y + titlePaint.TextSize, titlePaint);
            y += titlePaint.TextSize + 6;

            var x0 = margin;
            var y0 = y;
            var x1 = margin + chartW;
            var y1 = y + chartH;

            canvas.DrawRect(new SKRect(x0, y0, x1, y1), new SKPaint { Color = ParseColor("#FFFFFFFF") });
            canvas.DrawRect(new SKRect(x0, y0, x1, y1), new SKPaint { Color = ParseColor("#FFD0D0D0"), IsStroke = true, StrokeWidth = 1 });

            var padL = 10f;  // Reducido (ya no hay eje Y global)
            var padR = 10f;
            var padT = 24f;  // Espacio para etiquetas superiores
            var padB = 20f;
            var plotX0 = x0 + padL;
            var plotY0 = y0 + padT;
            var plotX1 = x1 - padR;
            var plotY1 = y1 - padB;
            var plotW = plotX1 - plotX0;
            var plotH = plotY1 - plotY0;

            // Ancho de cada columna
            var colCount = points.Count;
            var colW = plotW / Math.Max(1, colCount);

            // Posición X centrada para cada columna
            float X(int i) => plotX0 + (colW * i) + (colW / 2f);

            // Normaliza un valor dentro del rango de un punto (0=min, 1=max) con padding
            float YNorm(MinMaxPoint p, long ms)
            {
                if (p.MinMs is null || p.MaxMs is null || p.MaxMs == p.MinMs)
                    return plotY0 + plotH / 2f;
                var range = p.MaxMs.Value - p.MinMs.Value;
                var t = Math.Clamp((ms - p.MinMs.Value) / (double)range, 0, 1);
                // Dejamos 15% de margen arriba y abajo para etiquetas
                var usableH = plotH * 0.7f;
                var topMargin = plotH * 0.15f;
                return plotY0 + topMargin + usableH * (float)(1 - t);
            }

            // Líneas verticales de separación entre columnas
            var gridPaint = new SKPaint { Color = ParseColor("#FFE8E8E8"), StrokeWidth = 1, IsAntialias = true };
            for (var i = 0; i <= colCount; i++)
            {
                var xLine = plotX0 + (colW * i);
                canvas.DrawLine(xLine, plotY0, xLine, plotY1, gridPaint);
            }
            // Línea base
            canvas.DrawLine(plotX0, plotY1, plotX1, plotY1, new SKPaint { Color = ParseColor("#FFD0D0D0"), StrokeWidth = 1 });

            var rangePaint = new SKPaint { Color = ParseColor("#FF0B5D74"), StrokeWidth = 3, IsAntialias = true };
            var rangeDotPaint = new SKPaint { Color = ParseColor("#FF0B5D74"), IsAntialias = true };
            var rangeDotStroke = new SKPaint { Color = ParseColor("#FF111111"), IsAntialias = true, IsStroke = true, StrokeWidth = 1 };
            var rangeDotWhite = new SKPaint { Color = ParseColor("#FFFFFFFF"), IsAntialias = true };

            var meanPaint = new SKPaint { Color = ParseColor("#FF616161"), IsAntialias = true };
            var meanStroke = new SKPaint { Color = ParseColor("#FF111111"), IsAntialias = true, IsStroke = true, StrokeWidth = 1 };
            var selectedPaint = new SKPaint { Color = ParseColor("#FF1B5E20"), IsAntialias = true };
            var selectedStroke = new SKPaint { Color = ParseColor("#FF111111"), IsAntialias = true, IsStroke = true, StrokeWidth = 1 };

            var labelPaint = ClonePaint(thPaint);
            labelPaint.TextAlign = SKTextAlign.Center;
            labelPaint.FakeBoldText = true;

            var timeLabelPaint = ClonePaint(smallGrey);
            timeLabelPaint.TextAlign = SKTextAlign.Center;
            timeLabelPaint.Color = ParseColor("#FF0B5D74");

            var selLabelPaint = ClonePaint(smallGrey);
            selLabelPaint.TextAlign = SKTextAlign.Left;
            selLabelPaint.Color = ParseColor("#FF1B5E20");
            selLabelPaint.FakeBoldText = true;

            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (p.MinMs is null || p.MaxMs is null)
                    continue;

                var x = X(i);
                var yMinPx = YNorm(p, p.MinMs.Value);
                var yMaxPx = YNorm(p, p.MaxMs.Value);

                // Línea vertical min..max + marcadores
                canvas.DrawLine(x, yMaxPx, x, yMinPx, rangePaint);
                canvas.DrawCircle(x, yMaxPx, 4.5f, rangeDotWhite);
                canvas.DrawCircle(x, yMaxPx, 4.5f, rangeDotStroke);
                canvas.DrawCircle(x, yMinPx, 4.5f, rangeDotWhite);
                canvas.DrawCircle(x, yMinPx, 4.5f, rangeDotStroke);

                // Etiquetas de tiempo real (max arriba, min abajo)
                canvas.DrawText(FormatTimeMs(p.MaxMs.Value), x, yMaxPx - 8, timeLabelPaint);
                canvas.DrawText(FormatTimeMs(p.MinMs.Value), x, yMinPx + 12, timeLabelPaint);

                // Media (rombo gris)
                if (p.MeanMs is not null)
                {
                    var yMean = YNorm(p, p.MeanMs.Value);
                    canvas.Save();
                    canvas.RotateDegrees(45, x, yMean);
                    canvas.DrawRect(new SKRect(x - 3, yMean - 3, x + 3, yMean + 3), meanPaint);
                    canvas.Restore();
                }

                // Atleta seleccionado (círculo verde)
                if (p.SelectedMs is not null)
                {
                    var ys = YNorm(p, p.SelectedMs.Value);
                    canvas.DrawCircle(x, ys, 5.5f, selectedPaint);
                    canvas.DrawCircle(x, ys, 5.5f, selectedStroke);
                    // Etiqueta del tiempo del atleta seleccionado con diferencia respecto al mejor
                    var diffText = "";
                    if (p.MinMs.HasValue && p.MinMs.Value > 0 && p.SelectedMs.Value > p.MinMs.Value)
                    {
                        var diffMs = p.SelectedMs.Value - p.MinMs.Value;
                        var diffPct = (diffMs * 100.0) / p.MinMs.Value;
                        diffText = $" (+{FormatTimeMs(diffMs)} / +{diffPct:0.0}%)";
                    }
                    canvas.DrawText(FormatTimeMs(p.SelectedMs.Value) + diffText, x + 10, ys + 4, selLabelPaint);
                }

                // Etiqueta de la columna (P1, P2, ..., TOTAL)
                canvas.DrawText(p.Label, x, plotY1 + 14, labelPaint);
            }

            y += chartH + 6;
        }

        void DrawPdfLapTable(SectionWithDetailedAthleteRows section, bool isCumulative)
        {
            var titlePaint = ClonePaint(subtitlePaint);
            titlePaint.FakeBoldText = true;

            var maxLapIndex = Math.Max(1, section.Athletes.SelectMany(a => a.Laps).Select(l => l.LapIndex).DefaultIfEmpty(0).Max());

            EnsureSpace(18);
            canvas!.DrawText(isCumulative ? "Acumulados" : "Parciales", margin, y + titlePaint.TextSize, titlePaint);
            y += titlePaint.TextSize + 8;

            // Column layout (dinámico por nº de parciales)
            var tableWidth = pageWidth - (margin * 2);
            var colAthlete = tableWidth * 0.22f;
            var colCat = tableWidth * 0.10f;
            var colTime = tableWidth * 0.10f;
            var colPenal = tableWidth * 0.08f;
            var colTotal = tableWidth * 0.10f;
            var fixedCols = colAthlete + colCat + colTime + colPenal + colTotal;
            var colLap = Math.Max(28, (tableWidth - fixedCols) / maxLapIndex);

            float x0 = margin;
            float x = x0;

            // bounds
            var xAthleteEnd = x + colAthlete;
            var xCatEnd = xAthleteEnd + colCat;
            var xLapStarts = xCatEnd;
            var xLapEnd = xLapStarts + (colLap * maxLapIndex);
            var xTimeEnd = xLapEnd + colTime;
            var xPenalEnd = xTimeEnd + colPenal;
            var xTotalEnd = Math.Min(margin + tableWidth, xPenalEnd + colTotal);

            // header row
            const float headerH = 20;
            EnsureSpace(headerH + 8);
            canvas!.DrawRect(new SKRect(x0, y, xTotalEnd, y + headerH), new SKPaint { Color = bgHeader });
            DrawCellText(canvas!, "ATLETA", x0 + 6, y + 14, thPaint);
            DrawCellText(canvas!, "CAT.", xAthleteEnd + 6, y + 14, thPaint);

            // laps header
            var lapHeaderPaint = ClonePaint(thPaint);
            lapHeaderPaint.TextAlign = SKTextAlign.Right;
            for (var i = 1; i <= maxLapIndex; i++)
            {
                var right = xLapStarts + (colLap * i) - 6;
                DrawCellTextRight(canvas!, $"P{i}", right, y + 14, lapHeaderPaint);
            }

            DrawCellTextRight(canvas!, "TIEMPO", xTimeEnd - 6, y + 14, thPaint);
            DrawCellTextRight(canvas!, "PENAL.", xPenalEnd - 6, y + 14, thPaint);
            DrawCellTextRight(canvas!, "TOTAL", xTotalEnd - 6, y + 14, thPaint);
            canvas!.DrawLine(x0, y + headerH, xTotalEnd, y + headerH, new SKPaint { Color = grid, StrokeWidth = 1 });
            y += headerH;

            const float rowH = 22;
            var rowIndex = 0;
            foreach (var athlete in section.Athletes)
            {
                EnsureSpace(rowH + 4);
                var rowBg = (rowIndex % 2 == 0) ? bgRow : bgRowAlt;
                canvas!.DrawRect(new SKRect(x0, y, xTotalEnd, y + rowH), new SKPaint { Color = rowBg });
                canvas!.DrawLine(x0, y + rowH, xTotalEnd, y + rowH, new SKPaint { Color = grid, StrokeWidth = 1 });

                DrawCellText(canvas!, athlete.DisplayName, x0 + 6, y + 14, tdPaint);
                var categoryPaint = ClonePaint(tdPaint);
                categoryPaint.Color = ParseColor("#FFB1B1B1");
                DrawCellText(canvas!, athlete.CategoryName, xAthleteEnd + 6, y + 14, categoryPaint);

                // lap cells
                for (var i = 1; i <= maxLapIndex; i++)
                {
                    var lap = athlete.Laps.FirstOrDefault(l => l.LapIndex == i);
                    var value = lap == null ? "—" : (isCumulative ? lap.CumulativeFormatted : lap.SplitFormatted);
                    var color = lap == null ? "#FF999999" : (isCumulative ? lap.CumulativeTextColor : lap.SplitTextColor);
                    var lapPaint = ClonePaint(monoPaint);
                    lapPaint.Color = ParseColor(color);
                    var right = xLapStarts + (colLap * i) - 6;
                    DrawCellTextRight(canvas!, value, right, y + 14, lapPaint);
                }

                DrawCellTextRight(canvas!, athlete.DurationFormatted, xTimeEnd - 6, y + 14, monoPaint);
                DrawCellTextRight(canvas!, athlete.PenaltyFormatted, xPenalEnd - 6, y + 14, monoPaint);

                var totalPaint = ClonePaint(monoPaint);
                totalPaint.FakeBoldText = true;
                totalPaint.Color = ParseColor(athlete.TotalTextColor);
                DrawCellTextRight(canvas!, athlete.TotalFormatted, xTotalEnd - 6, y + 14, totalPaint);

                y += rowH;
                rowIndex++;
            }
        }
    }

    private sealed record MinMaxPoint(string Label, long? MinMs, long? MaxMs, long? MeanMs, long? SelectedMs);

    private static List<MinMaxPoint> BuildMinMaxPoints(
        SectionWithDetailedAthleteRows section,
        int maxLapIndex,
        bool isCumulative,
        int? referenceAthleteId,
        int? referenceVideoId)
    {
        var points = new List<MinMaxPoint>();
        
        // Determinar el atleta/intento seleccionado
        AthleteDetailedTimeRow? selected = null;
        if (referenceVideoId.HasValue)
        {
            // Buscar por VideoId específico (intento concreto)
            selected = section.Athletes.FirstOrDefault(a => a.VideoId == referenceVideoId.Value);
        }
        else if (referenceAthleteId.HasValue)
        {
            // Buscar el mejor intento del atleta (el primero porque ya están ordenados por TotalMs)
            selected = section.Athletes.FirstOrDefault(a => a.AthleteId == referenceAthleteId.Value && a.IsBestAttempt);
            // Fallback: cualquier intento del atleta
            selected ??= section.Athletes.FirstOrDefault(a => a.AthleteId == referenceAthleteId.Value);
        }

        for (var i = 1; i <= maxLapIndex; i++)
        {
            var values = section.Athletes
                .Select(a => a.Laps.FirstOrDefault(l => l.LapIndex == i))
                .Where(l => l != null)
                .Select(l => isCumulative ? l!.CumulativeMs : l!.SplitMs)
                .Where(ms => ms > 0)
                .ToList();

            if (values.Count == 0)
            {
                points.Add(new MinMaxPoint($"P{i}", null, null, null, null));
                continue;
            }

            var selLap = selected?.Laps.FirstOrDefault(l => l.LapIndex == i);
            var selMs = selLap == null ? (long?)null : (isCumulative ? selLap.CumulativeMs : selLap.SplitMs);

            var mean = (long)Math.Round(values.Average());
            points.Add(new MinMaxPoint($"P{i}", values.Min(), values.Max(), mean, selMs));
        }

        if (section.Athletes.Count > 0)
        {
            var totals = section.Athletes.Select(a => a.TotalMs).Where(ms => ms > 0).ToList();
            var selTotal = selected?.TotalMs;

            if (totals.Count > 0)
            {
                var mean = (long)Math.Round(totals.Average());
                points.Add(new MinMaxPoint("TOTAL", totals.Min(), totals.Max(), mean, selTotal));
            }
            else
            {
                points.Add(new MinMaxPoint("TOTAL", null, null, null, selTotal));
            }
        }

        return points;
    }

    private static string FormatTimeMs(long ms)
    {
        var totalSeconds = ms / 1000;
        var centiseconds = (ms % 1000) / 10;
        return $"{totalSeconds},{centiseconds:D2}";
    }

    private static void DrawCellText(SKCanvas canvas, string text, float x, float baselineY, SKPaint paint)
        => canvas.DrawText(text ?? string.Empty, x, baselineY, paint);

    private static void DrawCellTextRight(SKCanvas canvas, string text, float rightX, float baselineY, SKPaint paint)
    {
        var w = MeasureText(paint, text ?? string.Empty);
        canvas.DrawText(text ?? string.Empty, rightX - w, baselineY, paint);
    }

    private static SKPaint ClonePaint(SKPaint source)
    {
        // Clonado mínimo para texto (evita depender de constructores no disponibles)
        return new SKPaint
        {
            Color = source.Color,
            IsAntialias = source.IsAntialias,
            TextSize = source.TextSize,
            Typeface = source.Typeface,
            FakeBoldText = source.FakeBoldText,
            TextAlign = source.TextAlign,
        };
    }

    private static float MeasureText(SKPaint paint, string text)
    {
        text ??= string.Empty;

        // SkiaSharp ha cambiado APIs entre versiones; usamos SKFont + reflexión
        using var font = new SKFont(paint.Typeface, paint.TextSize);

        var methodWithPaint = typeof(SKFont).GetMethod("MeasureText", new[] { typeof(string), typeof(SKPaint) });
        if (methodWithPaint != null)
            return Convert.ToSingle(methodWithPaint.Invoke(font, new object?[] { text, paint }));

        var methodNoPaint = typeof(SKFont).GetMethod("MeasureText", new[] { typeof(string) });
        if (methodNoPaint != null)
            return Convert.ToSingle(methodNoPaint.Invoke(font, new object?[] { text }));

        // Fallback muy conservador
        return text.Length * paint.TextSize * 0.6f;
    }

    private static string BuildFileName(Session session, string kind, string extension)
    {
        var safeName = MakeSafeFileName(session.DisplayName);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{safeName}_{kind}_{stamp}.{extension}";
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "export" : name.Trim();
    }

    private static string HtmlEscape(string value)
        => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string CssColor(string? value)
    {
        // Documento imprimible: por defecto texto oscuro
        if (string.IsNullOrWhiteSpace(value))
            return "#111111";

        if (string.Equals(value, "White", StringComparison.OrdinalIgnoreCase))
            return "#111111";

        // Mapeos a colores con buen contraste sobre blanco
        if (string.Equals(value, "#FF4CAF50", StringComparison.OrdinalIgnoreCase))
            return "#1B5E20"; // verde oscuro
        if (string.Equals(value, "#FF6DDDFF", StringComparison.OrdinalIgnoreCase))
            return "#0B5D74"; // azul/teal oscuro

        if (!value.StartsWith("#", StringComparison.Ordinal))
            return "#111111";

        // Si viene en formato #AARRGGBB (MAUI), convertir a #RRGGBB para CSS
        var hex = value.TrimStart('#');
        if (hex.Length == 8)
        {
            var rrggbb = hex.Substring(2, 6);
            // Si era blanco, lo bajamos a texto oscuro
            if (string.Equals(rrggbb, "FFFFFF", StringComparison.OrdinalIgnoreCase))
                return "#111111";
            return "#" + rrggbb;
        }

        if (hex.Length == 6)
        {
            if (string.Equals(hex, "FFFFFF", StringComparison.OrdinalIgnoreCase))
                return "#111111";
            return "#" + hex;
        }

        return "#111111";
    }

    private static SKColor ParseColor(string? value)
    {
        // Documento imprimible: por defecto texto oscuro
        if (string.IsNullOrWhiteSpace(value))
            return new SKColor(0x11, 0x11, 0x11, 0xFF);

        if (string.Equals(value, "White", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x11, 0x11, 0x11, 0xFF);

        // Mapeos a colores con buen contraste sobre blanco
        if (string.Equals(value, "#FF4CAF50", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x1B, 0x5E, 0x20, 0xFF);
        if (string.Equals(value, "#FF6DDDFF", StringComparison.OrdinalIgnoreCase))
            return new SKColor(0x0B, 0x5D, 0x74, 0xFF);

        if (!value.StartsWith("#", StringComparison.Ordinal))
            return new SKColor(0x11, 0x11, 0x11, 0xFF);

        // #AARRGGBB o #RRGGBB
        var hex = value.TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;

        if (hex.Length != 8)
            return new SKColor(0x11, 0x11, 0x11, 0xFF);

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            return new SKColor(0x11, 0x11, 0x11, 0xFF);

        var a = (byte)((argb >> 24) & 0xFF);
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);
        return new SKColor(r, g, b, a);
    }
}
