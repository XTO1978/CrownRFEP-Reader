using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Services;

public interface ITrashService
{
    Task MoveSessionToTrashAsync(int sessionId);
    Task MoveVideoToTrashAsync(int videoId);

    Task RestoreSessionAsync(int sessionId);
    Task RestoreVideoAsync(int videoId);

    Task DeleteSessionPermanentlyAsync(int sessionId);
    Task DeleteVideoPermanentlyAsync(int videoId);

    Task<List<Session>> GetTrashedSessionsAsync();
    Task<List<VideoClip>> GetTrashedVideosAsync();

    /// <summary>
    /// Elimina físicamente los elementos en papelera cuyo DeletedAtUtc excede la retención.
    /// Por defecto 30 días.
    /// </summary>
    Task PurgeExpiredAsync(TimeSpan? retention = null);
}
