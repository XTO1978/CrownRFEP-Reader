using System.Text.Json.Serialization;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Datos completos de un video para sincronización remota.
/// Incluye todos los metadatos asociados: inputs, timing events, tags, atleta, etc.
/// </summary>
public class VideoSyncData
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("videoId")]
    public int VideoId { get; set; }

    [JsonPropertyName("sessionId")]
    public int SessionId { get; set; }

    [JsonPropertyName("video")]
    public VideoClipSyncData? Video { get; set; }

    [JsonPropertyName("athlete")]
    public AthleteSyncData? Athlete { get; set; }

    [JsonPropertyName("inputs")]
    public List<InputSyncData> Inputs { get; set; } = new();

    [JsonPropertyName("timingEvents")]
    public List<TimingEventSyncData> TimingEvents { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<TagSyncData> Tags { get; set; } = new();

    [JsonPropertyName("syncedAtUtc")]
    public long SyncedAtUtc { get; set; }
}

/// <summary>
/// Datos del VideoClip para sincronización
/// </summary>
public class VideoClipSyncData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("sessionId")]
    public int SessionId { get; set; }

    [JsonPropertyName("atletaId")]
    public int AtletaId { get; set; }

    [JsonPropertyName("section")]
    public int Section { get; set; }

    [JsonPropertyName("creationDate")]
    public long CreationDate { get; set; }

    [JsonPropertyName("clipPath")]
    public string? ClipPath { get; set; }

    [JsonPropertyName("thumbnailPath")]
    public string? ThumbnailPath { get; set; }

    [JsonPropertyName("comparisonName")]
    public string? ComparisonName { get; set; }

    [JsonPropertyName("clipDuration")]
    public double ClipDuration { get; set; }

    [JsonPropertyName("clipSize")]
    public long ClipSize { get; set; }

    public static VideoClipSyncData FromVideoClip(VideoClip clip)
    {
        return new VideoClipSyncData
        {
            Id = clip.Id,
            SessionId = clip.SessionId,
            AtletaId = clip.AtletaId,
            Section = clip.Section,
            CreationDate = clip.CreationDate,
            ClipPath = clip.ClipPath,
            ThumbnailPath = clip.ThumbnailPath,
            ComparisonName = clip.ComparisonName,
            ClipDuration = clip.ClipDuration,
            ClipSize = clip.ClipSize
        };
    }
}

/// <summary>
/// Datos del atleta para sincronización
/// </summary>
public class AthleteSyncData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("nombre")]
    public string? Nombre { get; set; }

    [JsonPropertyName("apellido")]
    public string? Apellido { get; set; }

    [JsonPropertyName("category")]
    public int Category { get; set; }

    [JsonPropertyName("categoriaId")]
    public int CategoriaId { get; set; }

    [JsonPropertyName("favorite")]
    public int Favorite { get; set; }

    public static AthleteSyncData? FromAthlete(Athlete? athlete)
    {
        if (athlete == null) return null;
        return new AthleteSyncData
        {
            Id = athlete.Id,
            Nombre = athlete.Nombre,
            Apellido = athlete.Apellido,
            Category = athlete.Category,
            CategoriaId = athlete.CategoriaId,
            Favorite = athlete.Favorite
        };
    }
}

/// <summary>
/// Datos de Input (eventos y asignaciones de tags) para sincronización
/// </summary>
public class InputSyncData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("sessionId")]
    public int SessionId { get; set; }

    [JsonPropertyName("videoId")]
    public int VideoId { get; set; }

    [JsonPropertyName("athleteId")]
    public int AthleteId { get; set; }

    [JsonPropertyName("categoriaId")]
    public int CategoriaId { get; set; }

    [JsonPropertyName("inputTypeId")]
    public int InputTypeId { get; set; }

    [JsonPropertyName("inputDateTime")]
    public long InputDateTime { get; set; }

    [JsonPropertyName("inputValue")]
    public string? InputValue { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("isEvent")]
    public int IsEvent { get; set; }

    public static InputSyncData FromInput(Input input)
    {
        return new InputSyncData
        {
            Id = input.Id,
            SessionId = input.SessionId,
            VideoId = input.VideoId,
            AthleteId = input.AthleteId,
            CategoriaId = input.CategoriaId,
            InputTypeId = input.InputTypeId,
            InputDateTime = input.InputDateTime,
            InputValue = input.InputValue,
            Timestamp = input.TimeStamp,
            IsEvent = input.IsEvent
        };
    }

    public Input ToInput()
    {
        return new Input
        {
            Id = Id,
            SessionId = SessionId,
            VideoId = VideoId,
            AthleteId = AthleteId,
            CategoriaId = CategoriaId,
            InputTypeId = InputTypeId,
            InputDateTime = InputDateTime,
            InputValue = InputValue,
            TimeStamp = Timestamp,
            IsEvent = IsEvent
        };
    }
}

/// <summary>
/// Datos de eventos de timing (cronometraje) para sincronización
/// </summary>
public class TimingEventSyncData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("videoId")]
    public int VideoId { get; set; }

    [JsonPropertyName("sessionId")]
    public int SessionId { get; set; }

    [JsonPropertyName("athleteId")]
    public int AthleteId { get; set; }

    [JsonPropertyName("sectionId")]
    public int SectionId { get; set; }

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMilliseconds { get; set; }

    [JsonPropertyName("splitMs")]
    public long SplitMilliseconds { get; set; }

    [JsonPropertyName("lapIndex")]
    public int LapIndex { get; set; }

    [JsonPropertyName("runIndex")]
    public int RunIndex { get; set; }

    [JsonPropertyName("createdAt")]
    public long CreatedAtUnixSeconds { get; set; }

    public static TimingEventSyncData FromEvent(ExecutionTimingEvent evt)
    {
        return new TimingEventSyncData
        {
            Id = evt.Id,
            VideoId = evt.VideoId,
            SessionId = evt.SessionId,
            AthleteId = evt.AthleteId,
            SectionId = evt.SectionId,
            Kind = evt.Kind,
            ElapsedMilliseconds = evt.ElapsedMilliseconds,
            SplitMilliseconds = evt.SplitMilliseconds,
            LapIndex = evt.LapIndex,
            RunIndex = evt.RunIndex,
            CreatedAtUnixSeconds = evt.CreatedAtUnixSeconds
        };
    }

    public ExecutionTimingEvent ToEvent()
    {
        return new ExecutionTimingEvent
        {
            Id = Id,
            VideoId = VideoId,
            SessionId = SessionId,
            AthleteId = AthleteId,
            SectionId = SectionId,
            Kind = Kind,
            ElapsedMilliseconds = ElapsedMilliseconds,
            SplitMilliseconds = SplitMilliseconds,
            LapIndex = LapIndex,
            RunIndex = RunIndex,
            CreatedAtUnixSeconds = CreatedAtUnixSeconds
        };
    }
}

/// <summary>
/// Datos de tags asignadas al video para sincronización
/// </summary>
public class TagSyncData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    public static TagSyncData FromTag(Tag tag)
    {
        return new TagSyncData
        {
            Id = tag.Id,
            Name = tag.NombreTag
        };
    }
}
