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
}
