using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Services;

public interface ITableExportService
{
    string BuildDetailedSectionTimesHtml(
        Session session,
        IReadOnlyList<SectionWithDetailedAthleteRows> sections,
        int? referenceAthleteId = null,
        int? referenceVideoId = null,
        string? referenceAthleteName = null);

    Task<string> ExportDetailedSectionTimesToHtmlAsync(
        Session session,
        IReadOnlyList<SectionWithDetailedAthleteRows> sections,
        int? referenceAthleteId = null,
        int? referenceVideoId = null,
        string? referenceAthleteName = null);

    Task<string> ExportDetailedSectionTimesToPdfAsync(
        Session session,
        IReadOnlyList<SectionWithDetailedAthleteRows> sections,
        int? referenceAthleteId = null,
        int? referenceVideoId = null,
        string? referenceAthleteName = null);
    
    // ==================== NUEVAS SOBRECARGAS CON ReportOptions ====================
    
    /// <summary>
    /// Genera HTML para el informe de sesión con opciones de visibilidad configurables
    /// </summary>
    string BuildSessionReportHtml(
        SessionReportData reportData,
        ReportOptions options,
        int? referenceAthleteId = null,
        int? referenceVideoId = null,
        string? referenceAthleteName = null);
    
    /// <summary>
    /// Exporta informe de sesión a HTML con opciones configurables
    /// </summary>
    Task<string> ExportSessionReportToHtmlAsync(
        SessionReportData reportData,
        ReportOptions options,
        int? referenceAthleteId = null,
        int? referenceVideoId = null,
        string? referenceAthleteName = null);
    
    /// <summary>
    /// Exporta informe de sesión a PDF con opciones configurables
    /// </summary>
    Task<string> ExportSessionReportToPdfAsync(
        SessionReportData reportData,
        ReportOptions options,
        int? referenceAthleteId = null,
        int? referenceVideoId = null,
        string? referenceAthleteName = null);
}
