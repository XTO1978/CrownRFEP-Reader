using CrownRFEP_Reader.Models;
using SQLite;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio para gestionar la base de datos SQLite local
/// </summary>
public class DatabaseService
{
    private SQLiteAsyncConnection? _database;
    private readonly string _dbPath;

    public DatabaseService()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "CrownApp.db");
    }

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_database != null)
            return _database;

        _database = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        
        await InitializeDatabaseAsync();
        
        return _database;
    }

    private async Task InitializeDatabaseAsync()
    {
        if (_database == null) return;

        // Crear las tablas si no existen
        await _database.CreateTableAsync<Session>();
        await _database.CreateTableAsync<Athlete>();
        await _database.CreateTableAsync<VideoClip>();
        await _database.CreateTableAsync<Category>();
        await _database.CreateTableAsync<Place>();
        await _database.CreateTableAsync<SessionType>();
        await _database.CreateTableAsync<Input>();
        await _database.CreateTableAsync<InputType>();
        await _database.CreateTableAsync<Valoracion>();
        await _database.CreateTableAsync<Tag>();
        await _database.CreateTableAsync<WorkGroup>();
        await _database.CreateTableAsync<AthleteWorkGroup>();
    }

    // ==================== SESSIONS ====================
    public async Task<List<Session>> GetAllSessionsAsync()
    {
        var db = await GetConnectionAsync();
        var sessions = await db.Table<Session>().OrderByDescending(s => s.Fecha).ToListAsync();
        
        // Cargar el conteo de videos para cada sesión
        foreach (var session in sessions)
        {
            session.VideoCount = await db.Table<VideoClip>().Where(v => v.SessionId == session.Id).CountAsync();
        }
        
        return sessions;
    }

    public async Task<Session?> GetSessionByIdAsync(int id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Session>().FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<int> SaveSessionAsync(Session session)
    {
        var db = await GetConnectionAsync();
        if (session.Id != 0)
        {
            await db.UpdateAsync(session);
            return session.Id;
        }
        else
        {
            await db.InsertAsync(session);
            return session.Id;
        }
    }

    public async Task<int> DeleteSessionAsync(Session session)
    {
        var db = await GetConnectionAsync();
        // Eliminar también los videos asociados
        await db.Table<VideoClip>().DeleteAsync(v => v.SessionId == session.Id);
        return await db.DeleteAsync(session);
    }

    // ==================== ATHLETES ====================
    public async Task<List<Athlete>> GetAllAthletesAsync()
    {
        var db = await GetConnectionAsync();
        var athletes = await db.Table<Athlete>().OrderBy(a => a.Apellido).ThenBy(a => a.Nombre).ToListAsync();
        
        // Cargar las categorías
        var categories = await db.Table<Category>().ToListAsync();
        foreach (var athlete in athletes)
        {
            var category = categories.FirstOrDefault(c => c.Id == athlete.CategoriaId);
            athlete.CategoriaNombre = category?.NombreCategoria;
        }
        
        return athletes;
    }

    public async Task<Athlete?> GetAthleteByIdAsync(int id)
    {
        var db = await GetConnectionAsync();
        var athlete = await db.Table<Athlete>().FirstOrDefaultAsync(a => a.Id == id);
        if (athlete != null)
        {
            var category = await db.Table<Category>().FirstOrDefaultAsync(c => c.Id == athlete.CategoriaId);
            athlete.CategoriaNombre = category?.NombreCategoria;
        }
        return athlete;
    }

    public async Task<int> SaveAthleteAsync(Athlete athlete)
    {
        var db = await GetConnectionAsync();
        var existing = await db.Table<Athlete>().FirstOrDefaultAsync(a => a.Id == athlete.Id);
        if (existing != null)
        {
            await db.UpdateAsync(athlete);
            return athlete.Id;
        }
        else
        {
            await db.InsertAsync(athlete);
            return athlete.Id;
        }
    }

    // ==================== VIDEO CLIPS ====================
    public async Task<List<VideoClip>> GetVideoClipsBySessionAsync(int sessionId)
    {
        var db = await GetConnectionAsync();
        var clips = await db.Table<VideoClip>()
            .Where(v => v.SessionId == sessionId)
            .OrderByDescending(v => v.CreationDate)
            .ToListAsync();

        // Cargar atletas
        var athletes = await db.Table<Athlete>().ToListAsync();
        var categories = await db.Table<Category>().ToListAsync();
        
        foreach (var clip in clips)
        {
            var athlete = athletes.FirstOrDefault(a => a.Id == clip.AtletaId);
            if (athlete != null)
            {
                athlete.CategoriaNombre = categories.FirstOrDefault(c => c.Id == athlete.CategoriaId)?.NombreCategoria;
                clip.Atleta = athlete;
            }
        }

        return clips;
    }

    public async Task<List<VideoClip>> GetVideoClipsByAthleteAsync(int athleteId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoClip>()
            .Where(v => v.AtletaId == athleteId)
            .OrderByDescending(v => v.CreationDate)
            .ToListAsync();
    }

    public async Task<int> SaveVideoClipAsync(VideoClip clip)
    {
        var db = await GetConnectionAsync();
        var existing = await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.Id == clip.Id && v.SessionId == clip.SessionId);
        if (existing != null)
        {
            existing.LocalClipPath = clip.LocalClipPath;
            existing.LocalThumbnailPath = clip.LocalThumbnailPath;
            await db.UpdateAsync(existing);
            return existing.Id;
        }
        else
        {
            await db.InsertAsync(clip);
            return clip.Id;
        }
    }

    // ==================== CATEGORIES ====================
    public async Task<List<Category>> GetAllCategoriesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Category>().ToListAsync();
    }

    public async Task<int> SaveCategoryAsync(Category category)
    {
        var db = await GetConnectionAsync();
        var existing = await db.Table<Category>().FirstOrDefaultAsync(c => c.Id == category.Id);
        if (existing != null)
        {
            await db.UpdateAsync(category);
            return category.Id;
        }
        else
        {
            await db.InsertAsync(category);
            return category.Id;
        }
    }

    // ==================== INPUTS ====================
    public async Task<List<Input>> GetInputsBySessionAsync(int sessionId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Input>().Where(i => i.SessionId == sessionId).ToListAsync();
    }

    public async Task<int> SaveInputAsync(Input input)
    {
        var db = await GetConnectionAsync();
        if (input.Id != 0)
        {
            await db.UpdateAsync(input);
            return input.Id;
        }
        else
        {
            await db.InsertAsync(input);
            return input.Id;
        }
    }

    // ==================== VALORACIONES ====================
    public async Task<List<Valoracion>> GetValoracionesBySessionAsync(int sessionId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Valoracion>().Where(v => v.SessionId == sessionId).ToListAsync();
    }

    public async Task<int> SaveValoracionAsync(Valoracion valoracion)
    {
        var db = await GetConnectionAsync();
        if (valoracion.Id != 0)
        {
            await db.UpdateAsync(valoracion);
            return valoracion.Id;
        }
        else
        {
            await db.InsertAsync(valoracion);
            return valoracion.Id;
        }
    }

    // ==================== ESTADÍSTICAS ====================
    public async Task<int> GetTotalSessionsCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Session>().CountAsync();
    }

    public async Task<int> GetTotalVideosCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoClip>().CountAsync();
    }

    public async Task<int> GetTotalAthletesCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Athlete>().CountAsync();
    }

    public async Task<double> GetTotalVideosDurationAsync()
    {
        var db = await GetConnectionAsync();
        var clips = await db.Table<VideoClip>().ToListAsync();
        return clips.Sum(c => c.ClipDuration);
    }
}
