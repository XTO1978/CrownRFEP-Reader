using CrownRFEP_Reader.Models;
using SQLite;

namespace CrownRFEP_Reader.Services;

public sealed class TrashService : ITrashService
{
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);
    private readonly DatabaseService _databaseService;

    public TrashService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    private static long NowUtcUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public async Task MoveSessionToTrashAsync(int sessionId)
    {
        var db = await _databaseService.GetConnectionAsync();
        var now = NowUtcUnixSeconds();

        // Marcar sesión y todos sus vídeos como eliminados.
        await db.ExecuteAsync("UPDATE sesion SET is_deleted = 1, deleted_at_utc = ? WHERE id = ?;", now, sessionId);
        await db.ExecuteAsync("UPDATE videoClip SET is_deleted = 1, deleted_at_utc = ? WHERE SessionID = ?;", now, sessionId);

        _databaseService.InvalidateCache();
    }

    public async Task MoveVideoToTrashAsync(int videoId)
    {
        var db = await _databaseService.GetConnectionAsync();
        var now = NowUtcUnixSeconds();

        await db.ExecuteAsync("UPDATE videoClip SET is_deleted = 1, deleted_at_utc = ? WHERE ID = ?;", now, videoId);

        _databaseService.InvalidateCache();
    }

    public async Task RestoreSessionAsync(int sessionId)
    {
        var db = await _databaseService.GetConnectionAsync();

        await db.ExecuteAsync("UPDATE sesion SET is_deleted = 0, deleted_at_utc = 0 WHERE id = ?;", sessionId);
        await db.ExecuteAsync("UPDATE videoClip SET is_deleted = 0, deleted_at_utc = 0 WHERE SessionID = ?;", sessionId);

        _databaseService.InvalidateCache();
    }

    public async Task RestoreVideoAsync(int videoId)
    {
        var db = await _databaseService.GetConnectionAsync();

        // Obtener SessionID por si hay que restaurar también la sesión.
        var clip = await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.Id == videoId);
        if (clip == null)
            return;

        // Restaurar clip
        await db.ExecuteAsync("UPDATE videoClip SET is_deleted = 0, deleted_at_utc = 0 WHERE ID = ?;", videoId);

        // Si la sesión estaba en papelera, restaurarla para que el vídeo vuelva a ser accesible.
        var session = await db.Table<Session>().FirstOrDefaultAsync(s => s.Id == clip.SessionId);
        if (session != null && session.IsDeleted == 1)
        {
            await RestoreSessionAsync(session.Id);
            return;
        }

        _databaseService.InvalidateCache();
    }

    public async Task DeleteSessionPermanentlyAsync(int sessionId)
    {
        // Borrado físico en cascada + archivos
        await _databaseService.DeleteSessionCascadeAsync(sessionId, deleteSessionFiles: true);
        _databaseService.InvalidateCache();
    }

    public async Task DeleteVideoPermanentlyAsync(int videoId)
    {
        var db = await _databaseService.GetConnectionAsync();
        await DeleteVideoClipPhysicallyAsync(db, videoId);
        _databaseService.InvalidateCache();
    }

    public async Task<List<Session>> GetTrashedSessionsAsync()
    {
        var db = await _databaseService.GetConnectionAsync();
        var sessions = await db.Table<Session>()
            .Where(s => s.IsDeleted == 1)
            .OrderByDescending(s => s.DeletedAtUtc)
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.VideoCount = await db.Table<VideoClip>().Where(v => v.SessionId == session.Id).CountAsync();
        }

        return sessions;
    }

    public async Task<List<VideoClip>> GetTrashedVideosAsync()
    {
        var db = await _databaseService.GetConnectionAsync();
        var clips = await db.Table<VideoClip>()
            .Where(v => v.IsDeleted == 1)
            .OrderByDescending(v => v.DeletedAtUtc)
            .ToListAsync();

        // Hidratar Session/Atleta de forma ligera (sin usar caché privada de DatabaseService)
        if (clips.Count == 0)
            return clips;

        var sessionIds = clips.Select(c => c.SessionId).Distinct().ToList();
        var athleteIds = clips.Select(c => c.AtletaId).Where(id => id != 0).Distinct().ToList();

        var sessions = await db.Table<Session>().Where(s => sessionIds.Contains(s.Id)).ToListAsync();
        var athletes = athleteIds.Count > 0
            ? await db.Table<Athlete>().Where(a => athleteIds.Contains(a.Id)).ToListAsync()
            : new List<Athlete>();

        var sessionById = sessions.ToDictionary(s => s.Id, s => s);
        var athleteById = athletes.ToDictionary(a => a.Id, a => a);

        foreach (var clip in clips)
        {
            sessionById.TryGetValue(clip.SessionId, out var session);
            athleteById.TryGetValue(clip.AtletaId, out var athlete);
            clip.Session = session;
            clip.Atleta = athlete;
        }

        return clips;
    }

    public async Task PurgeExpiredAsync(TimeSpan? retention = null)
    {
        var keepFor = retention ?? DefaultRetention;
        var cutoffUtc = DateTimeOffset.UtcNow.Subtract(keepFor).ToUnixTimeSeconds();

        try
        {
            var db = await _databaseService.GetConnectionAsync();

            // 1) Sesiones en papelera expiradas -> borrado físico en cascada + archivos
            var expiredSessionIds = await db.QueryAsync<IdRow>(
                "SELECT id AS Id FROM sesion WHERE is_deleted = 1 AND deleted_at_utc > 0 AND deleted_at_utc < ?;",
                cutoffUtc);

            foreach (var row in expiredSessionIds)
            {
                await _databaseService.DeleteSessionCascadeAsync(row.Id, deleteSessionFiles: true);
            }

            // 2) Vídeos en papelera expirados cuyo Session NO está eliminado (si la sesión estaba eliminada, ya se borró arriba)
            var expiredVideoRows = await db.QueryAsync<VideoExpireRow>(
                "SELECT ID AS VideoId, SessionID AS SessionId FROM videoClip WHERE is_deleted = 1 AND deleted_at_utc > 0 AND deleted_at_utc < ?;",
                cutoffUtc);

            foreach (var row in expiredVideoRows)
            {
                var session = await db.Table<Session>().FirstOrDefaultAsync(s => s.Id == row.SessionId);
                if (session != null && session.IsDeleted == 1)
                    continue;

                await DeleteVideoClipPhysicallyAsync(db, row.VideoId);
            }

            _databaseService.InvalidateCache();
        }
        catch
        {
            // Best-effort: no bloquear el arranque si falla el purgado.
        }
    }

    private static async Task DeleteVideoClipPhysicallyAsync(SQLiteAsyncConnection db, int videoId)
    {
        // Cargar para obtener rutas
        var clip = await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.Id == videoId);
        if (clip != null)
        {
            var videoPath = !string.IsNullOrWhiteSpace(clip.LocalClipPath) && File.Exists(clip.LocalClipPath)
                ? clip.LocalClipPath
                : clip.ClipPath;

            if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
            {
                try { File.Delete(videoPath); } catch { }
            }

            var thumbPath = !string.IsNullOrWhiteSpace(clip.LocalThumbnailPath) && File.Exists(clip.LocalThumbnailPath)
                ? clip.LocalThumbnailPath
                : clip.ThumbnailPath;

            if (!string.IsNullOrWhiteSpace(thumbPath) && File.Exists(thumbPath))
            {
                try { File.Delete(thumbPath); } catch { }
            }
        }

        // Borrar dependencias
        await db.ExecuteAsync("DELETE FROM \"execution_timing_events\" WHERE VideoID = ?;", videoId);
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE VideoID = ?;", videoId);

        // Borrar el propio clip
        await db.ExecuteAsync("DELETE FROM videoClip WHERE ID = ?;", videoId);
    }

    private sealed class IdRow
    {
        public int Id { get; set; }
    }

    private sealed class VideoExpireRow
    {
        public int VideoId { get; set; }
        public int SessionId { get; set; }
    }
}
