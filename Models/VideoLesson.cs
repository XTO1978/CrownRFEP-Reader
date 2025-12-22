using SQLite;

namespace CrownRFEP_Reader.Models;

[Table("videoLesson")]
public class VideoLesson
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int SessionId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string FilePath { get; set; } = string.Empty;

    public string? Title { get; set; }

    [Ignore]
    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title! : $"Videolección {Id}";

    [Ignore]
    public string? SessionDisplayName { get; set; }

    [Ignore]
    public string? LocalThumbnailPath { get; set; }
}
