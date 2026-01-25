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
    private StatusBarService? _statusBarService;
    
    // ID único de instancia para diagnóstico de singleton
    private static int _instanceCounter;
    private readonly int _instanceId;
    
    // Caché de datos relacionados para evitar consultas repetidas
    private Dictionary<int, Athlete>? _athleteCache;
    private Dictionary<int, Category>? _categoryCache;
    private Dictionary<int, Session>? _sessionCache;
    private Dictionary<int, Tag>? _tagCache;
    private Dictionary<int, EventTagDefinition>? _eventTagCache;
    private DateTime _cacheLastRefresh = DateTime.MinValue;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    public DatabaseService()
    {
        _instanceId = System.Threading.Interlocked.Increment(ref _instanceCounter);
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "CrownApp.db");
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] CREATED instance #{_instanceId} | dbPath={_dbPath}");
    }

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Inyecta el StatusBarService para logging (se llama desde MauiProgram después de construir)
    /// </summary>
    public void SetStatusBarService(StatusBarService statusBarService)
    {
        _statusBarService = statusBarService;
    }

    /// <summary>
    /// Invalida la caché de datos relacionados (llamar después de imports o cambios significativos)
    /// </summary>
    public void InvalidateCache()
    {
        _athleteCache = null;
        _categoryCache = null;
        _sessionCache = null;
        _tagCache = null;
        _eventTagCache = null;
        _cacheLastRefresh = DateTime.MinValue;
    }

    private bool IsCacheValid => _athleteCache != null && 
                                  _categoryCache != null && 
                                  DateTime.Now - _cacheLastRefresh < CacheExpiration;

    private async Task EnsureCacheLoadedAsync(SQLiteAsyncConnection db)
    {
        if (IsCacheValid) return;

        var athletes = await db.Table<Athlete>().ToListAsync();
        var categories = await db.Table<Category>().ToListAsync();
        var tags = await db.Table<Tag>().ToListAsync();
        var sessions = await db.Table<Session>().Where(s => s.IsDeleted == 0).ToListAsync();
        var eventTags = await db.Table<EventTagDefinition>().ToListAsync();

        _athleteCache = athletes.ToDictionary(a => a.Id, a => a);
        _categoryCache = categories.ToDictionary(c => c.Id, c => c);
        _tagCache = tags.ToDictionary(t => t.Id, t => t);
        _sessionCache = sessions.ToDictionary(s => s.Id, s => s);
        _eventTagCache = eventTags.ToDictionary(e => e.Id, e => e);
        _cacheLastRefresh = DateTime.Now;
    }

    private void LogInfo(string message) => _statusBarService?.LogDatabaseInfo(message);
    private void LogSuccess(string message) => _statusBarService?.LogDatabaseSuccess(message);
    private void LogWarning(string message) => _statusBarService?.LogDatabaseWarning(message);
    private void LogError(string message) => _statusBarService?.LogDatabaseError(message);

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_database != null)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Using existing connection: {_dbPath}");
#endif
            return _database;
        }

        LogInfo("Conectando a base de datos...");
    #if DEBUG
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Creating NEW connection: {_dbPath}");
    #endif
        // Conexión estándar (históricamente estable en el proyecto)
        _database = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        
        await InitializeDatabaseAsync();
        LogSuccess("Base de datos conectada");
        
        return _database;
    }

    /// <summary>
    /// Intenta resolver un VideoClip a partir de una ruta absoluta (LocalClipPath) o, como fallback,
    /// por nombre de archivo (ClipPath) o coincidencia parcial en LocalClipPath.
    /// Útil cuando se navega a SinglePlayerPage solo con ?videoPath=...
    /// </summary>
    public async Task<VideoClip?> FindVideoClipByAnyPathAsync(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return null;

        var original = videoPath;

        // Normalizar: aceptar rutas tipo file:///...
        var normalized = videoPath.Trim();
        if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.IsFile)
                    normalized = uri.LocalPath;
            }
            catch
            {
                // Ignorar
            }
        }

        try { normalized = Uri.UnescapeDataString(normalized); } catch { }
        normalized = normalized.Replace('\\', '/');

        var db = await GetConnectionAsync();

        // 1) Match exacto por LocalClipPath
        var exact = await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.LocalClipPath == normalized);
        if (exact != null)
            return exact;

        // 1b) Intento extra con el valor original (por si ya venía correcto)
        if (!string.Equals(original, normalized, StringComparison.Ordinal))
        {
            var exactOriginal = await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.LocalClipPath == original);
            if (exactOriginal != null)
                return exactOriginal;
        }

        // 2) Fallback por nombre de archivo
        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        // 2a) ClipPath suele ser filename, pero en algunos flujos puede venir como ruta.
        var byClipPath = await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.ClipPath == fileName);
        if (byClipPath != null)
            return byClipPath;

        var byClipPathAsPath = await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.ClipPath == normalized);
        if (byClipPathAsPath != null)
            return byClipPathAsPath;

        // 3) Fallback: LocalClipPath contiene el nombre
        var like = await db.QueryAsync<VideoClip>(
            "SELECT * FROM videoClip WHERE localClipPath LIKE ? ORDER BY CreationDate DESC LIMIT 1",
            $"%{fileName}%");

        return like.FirstOrDefault();
    }

    private async Task InitializeDatabaseAsync()
    {
        if (_database == null) return;

        LogInfo("Inicializando tablas...");
        
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
        await _database.CreateTableAsync<EventTagDefinition>();
        await _database.CreateTableAsync<ExecutionTimingEvent>();
        await _database.CreateTableAsync<WorkGroup>();
        await _database.CreateTableAsync<AthleteWorkGroup>();
        await _database.CreateTableAsync<UserProfile>();
        await _database.CreateTableAsync<VideoLesson>();
        await _database.CreateTableAsync<SessionDiary>();
        await _database.CreateTableAsync<DailyWellness>();
        await _database.CreateTableAsync<SessionLapConfig>();
        await _database.CreateTableAsync<LapConfigHistory>();

        // Migraciones ligeras: columnas nuevas en videoClip
        await EnsureColumnExistsAsync(_database, "videoClip", "localClipPath", "TEXT");
        await EnsureColumnExistsAsync(_database, "videoClip", "localThumbnailPath", "TEXT");

        // Papelera (soft-delete)
        await EnsureColumnExistsAsync(_database, "sesion", "is_deleted", "INTEGER DEFAULT 0");
        await EnsureColumnExistsAsync(_database, "sesion", "deleted_at_utc", "INTEGER DEFAULT 0");
        await EnsureColumnExistsAsync(_database, "videoClip", "is_deleted", "INTEGER DEFAULT 0");
        await EnsureColumnExistsAsync(_database, "videoClip", "deleted_at_utc", "INTEGER DEFAULT 0");

        // Favoritos
        await EnsureColumnExistsAsync(_database, "sesion", "is_favorite", "INTEGER DEFAULT 0");
        await EnsureColumnExistsAsync(_database, "videoClip", "is_favorite", "INTEGER DEFAULT 0");

        // Migraciones ligeras: columnas nuevas en userProfile
        await EnsureColumnExistsAsync(_database, "userProfile", "referenceAthleteId", "INTEGER");

        // Migraciones ligeras: columna IsEvent en input para distinguir eventos de asignaciones
        await EnsureColumnExistsAsync(_database, "input", "IsEvent", "INTEGER DEFAULT 0");

        // Migraciones ligeras: columnas nuevas en event_tags para tags de sistema
        await EnsureColumnExistsAsync(_database, "event_tags", "is_system", "INTEGER DEFAULT 0");
        await EnsureColumnExistsAsync(_database, "event_tags", "penalty_seconds", "INTEGER DEFAULT 0");

        // Sincronización cloud: columnas nuevas en videoClip
        await EnsureColumnExistsAsync(_database, "videoClip", "is_synced", "INTEGER DEFAULT 0");
        await EnsureColumnExistsAsync(_database, "videoClip", "last_sync_utc", "INTEGER DEFAULT 0");
        await EnsureColumnExistsAsync(_database, "videoClip", "file_hash", "TEXT");
        await EnsureColumnExistsAsync(_database, "videoClip", "source", "TEXT DEFAULT 'local'");

        // Migración: separar tipos de evento en event_tags (si venían guardados como Tag)
        await MigrateEventTagsAsync(_database);

        // Asegurar que existen los tags de sistema (penalizaciones)
        await EnsureSystemEventTagsAsync(_database);
        
        LogSuccess("Tablas inicializadas correctamente");
    }

    /// <summary>
    /// Crea los tags de evento de sistema para penalizaciones si no existen
    /// </summary>
    private static async Task EnsureSystemEventTagsAsync(SQLiteAsyncConnection db)
    {
        var systemTags = new[]
        {
            new { Name = "+2", PenaltySeconds = 2 },
            new { Name = "+50", PenaltySeconds = 50 }
        };

        foreach (var tagDef in systemTags)
        {
            // Buscar por nombre o por penalty_seconds
            var existing = await db.Table<EventTagDefinition>()
                .Where(t => t.PenaltySeconds == tagDef.PenaltySeconds && t.IsSystem)
                .FirstOrDefaultAsync();

            if (existing == null)
            {
                // No existe, crearlo
                var newTag = new EventTagDefinition
                {
                    Nombre = tagDef.Name,
                    IsSystem = true,
                    PenaltySeconds = tagDef.PenaltySeconds
                };
                await db.InsertAsync(newTag);
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] Creado tag de sistema: {tagDef.Name} (Id={newTag.Id})");
            }
        }
    }

    private static async Task MigrateEventTagsAsync(SQLiteAsyncConnection db)
    {
        // Si no hay eventos, no hacemos nada.
        var eventInputs = await db.Table<Input>().Where(i => i.IsEvent == 1).ToListAsync();
        if (eventInputs.Count == 0)
            return;

        // Catálogo actual de event_tags
        var eventDefs = await db.Table<EventTagDefinition>().ToListAsync();
        var eventDefIds = new HashSet<int>(eventDefs.Select(e => e.Id));

        // Comprobar si TODOS los InputTypeId de eventos ya existen en event_tags.
        // Si es así, la migración ya se ejecutó -> salir.
        var distinctEventTypeIds = eventInputs.Select(i => i.InputTypeId).Distinct().ToList();
        if (distinctEventTypeIds.All(id => eventDefIds.Contains(id)))
            return;

        // Catálogo previo de tags (antiguamente se reutilizaba Tag para eventos)
        var tags = await db.Table<Tag>().ToListAsync();
        var tagNameById = tags
            .Where(t => !string.IsNullOrWhiteSpace(t.NombreTag))
            .ToDictionary(t => t.Id, t => t.NombreTag!.Trim());

        var eventIdByName = eventDefs
            .Where(e => !string.IsNullOrWhiteSpace(e.Nombre))
            .GroupBy(e => e.Nombre!.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Mapear antiguos InputTypeId (referenciando Tag) -> nuevo Id en event_tags (por nombre)
        var remap = new Dictionary<int, int>();

        foreach (var oldId in distinctEventTypeIds)
        {
            // Saltar si ya existe en event_tags (no necesita remapeo)
            if (eventDefIds.Contains(oldId))
                continue;

            if (!tagNameById.TryGetValue(oldId, out var oldName))
                continue;

            var key = oldName.Trim().ToLowerInvariant();
            if (!eventIdByName.TryGetValue(key, out var newEventId))
            {
                var def = new EventTagDefinition { Nombre = oldName.Trim() };
                await db.InsertAsync(def);
                newEventId = def.Id;
                eventIdByName[key] = newEventId;
            }

            remap[oldId] = newEventId;
        }

        // Ejecutar remapeo en inputs (solo eventos que necesiten migración)
        foreach (var (oldId, newId) in remap)
        {
            await db.ExecuteAsync(
                "UPDATE \"input\" SET InputTypeID = ? WHERE IsEvent = 1 AND InputTypeID = ?",
                newId,
                oldId);
        }
    }

    private sealed class SqliteTableInfoRow
    {
        // PRAGMA table_info devuelve: cid, name, type, notnull, dflt_value, pk
        // Solo nos interesa 'name'.
        public string name { get; set; } = "";
    }

    private sealed class VideoIdOnlyRow
    {
        public int VideoID { get; set; }
    }

    private static async Task EnsureColumnExistsAsync(SQLiteAsyncConnection db, string tableName, string columnName, string sqliteType)
    {
        var rows = await db.QueryAsync<SqliteTableInfoRow>($"PRAGMA table_info({tableName});");
        var exists = rows.Any(r => string.Equals(r.name, columnName, StringComparison.OrdinalIgnoreCase));
        if (exists) return;

        await db.ExecuteAsync($"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqliteType};");
    }

    private static string GetNormalizedFileName(string? pathOrName, string fallbackFileName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
            return fallbackFileName;

        // Los .crown pueden venir de Windows con backslashes.
        var normalized = pathOrName.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(fileName) ? fallbackFileName : fileName;
    }

    private static void HydrateLocalMediaPaths(VideoClip clip, string? sessionPath)
    {
        if (string.IsNullOrWhiteSpace(sessionPath)) return;

        var clipFileName = GetNormalizedFileName(clip.ClipPath, $"CROWN{clip.Id}.mp4");
        var thumbFileName = GetNormalizedFileName(clip.ThumbnailPath, $"CROWN{clip.Id}_thumb.jpg");

        var candidateClipPath = Path.Combine(sessionPath, "videos", clipFileName);
        var candidateThumbPath = Path.Combine(sessionPath, "thumbnails", thumbFileName);

        if (string.IsNullOrWhiteSpace(clip.LocalClipPath) || !File.Exists(clip.LocalClipPath))
            clip.LocalClipPath = candidateClipPath;

        if (string.IsNullOrWhiteSpace(clip.LocalThumbnailPath) || !File.Exists(clip.LocalThumbnailPath))
            clip.LocalThumbnailPath = candidateThumbPath;
    }

    private static async Task<List<Input>> GetInputsForVideosAsync(SQLiteAsyncConnection db, IReadOnlyList<int> videoIds)
    {
        if (videoIds.Count == 0)
            return new List<Input>();

        // Query por IN para evitar depender de SessionID en inputs (hay flujos que pueden no rellenarlo).
        // videoIds viene de BD, así que el SQL es seguro al usar parámetros.
        var placeholders = string.Join(",", Enumerable.Repeat("?", videoIds.Count));
        var sql = $"SELECT * FROM \"input\" WHERE VideoID IN ({placeholders});";
        var args = videoIds.Cast<object>().ToArray();
        return await db.QueryAsync<Input>(sql, args);
    }

    private static async Task<HashSet<int>> GetVideoIdsWithExecutionTimingEventsAsync(SQLiteAsyncConnection db, IReadOnlyList<int> videoIds)
    {
        if (videoIds.Count == 0)
            return new HashSet<int>();

        var placeholders = string.Join(",", Enumerable.Repeat("?", videoIds.Count));
        var sql = $"SELECT DISTINCT VideoID FROM execution_timing_events WHERE VideoID IN ({placeholders});";
        var args = videoIds.Cast<object>().ToArray();
        var rows = await db.QueryAsync<VideoIdOnlyRow>(sql, args);
        return rows.Select(r => r.VideoID).ToHashSet();
    }

    // Tags internos que no se muestran en la galería
    private static readonly HashSet<string> InternalTagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Grabación completada"
    };

    /// <summary>
    /// Hidrata los tags y eventos para una lista de VideoClips
    /// </summary>
    public async Task HydrateTagsForClips(IEnumerable<VideoClip> clips)
    {
        var clipList = clips.ToList();
        if (clipList.Count == 0) return;

        var db = await GetConnectionAsync();
        var allTags = await db.Table<Tag>().ToListAsync();
        var allEventDefs = await db.Table<EventTagDefinition>().ToListAsync();
        var clipIds = clipList.Select(c => c.Id).ToList();
        var allInputsForClips = await GetInputsForVideosAsync(db, clipIds);

        HydrateTagsForClipsInternal(clipList, allTags, allEventDefs, allInputsForClips);
    }

    private static void HydrateTagsForClipsInternal(
        IReadOnlyList<VideoClip> clips,
        IReadOnlyList<Tag> allTags,
        IReadOnlyList<EventTagDefinition> allEventDefs,
        IReadOnlyList<Input> allInputsForClips)
    {
        var tagDict = allTags.ToDictionary(t => t.Id, t => t);
        var eventDict = allEventDefs.ToDictionary(e => e.Id, e => e);
        
        // Agrupar inputs por VideoId para O(1) lookup en lugar de O(n) con Where
        var inputsByVideoId = allInputsForClips
            .GroupBy(i => i.VideoId)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        foreach (var clip in clips)
        {
            if (!inputsByVideoId.TryGetValue(clip.Id, out var clipInputs))
            {
                clip.Tags = new List<Tag>();
                clip.EventTags = new List<Tag>();
                continue;
            }

            // Tags asignados (IsEvent == 0): asignados desde la app mediante el panel de etiquetas
            clip.Tags = clipInputs
                .Where(i => i.IsEvent == 0 && tagDict.ContainsKey(i.InputTypeId))
                .GroupBy(i => i.InputTypeId) // Evitar duplicados del mismo tipo
                .Select(g =>
                {
                    var input = g.First();
                    var baseTag = tagDict[input.InputTypeId];
                    
                    // Usar InputValue si tiene contenido, sino el nombre del tipo de tag
                    var displayName = !string.IsNullOrWhiteSpace(input.InputValue)
                        ? input.InputValue
                        : baseTag.NombreTag;
                    
                    return new Tag
                    {
                        Id = baseTag.Id,
                        NombreTag = displayName,
                        IsSelected = baseTag.IsSelected,
                        IsEventTag = false
                    };
                })
                .Where(t => !string.IsNullOrEmpty(t.NombreTag) && !InternalTagNames.Contains(t.NombreTag))
                .ToList();

            // Tags de eventos (IsEvent == 1): importados de .crown o creados como eventos
            // Los eventos usan EventTagDefinition (tabla event_tags), NO la tabla tags
            clip.EventTags = clipInputs
                .Where(i => i.IsEvent == 1 && eventDict.ContainsKey(i.InputTypeId))
                .GroupBy(i => i.InputTypeId) // Agrupar por tipo para contar ocurrencias
                .Select(g =>
                {
                    var input = g.First();
                    var eventDef = eventDict[input.InputTypeId];
                    var count = g.Count(); // Número de ocurrencias de este evento
                    
                    // Usar InputValue si tiene contenido, sino el nombre del tipo de evento
                    var displayName = !string.IsNullOrWhiteSpace(input.InputValue)
                        ? input.InputValue
                        : eventDef.Nombre;
                    
                    return new Tag
                    {
                        Id = eventDef.Id,
                        NombreTag = displayName,
                        IsSelected = 0,
                        IsEventTag = true,
                        EventCount = count // Incluir el conteo de ocurrencias
                    };
                })
                .Where(t => !string.IsNullOrEmpty(t.NombreTag) && !InternalTagNames.Contains(t.NombreTag))
                .ToList();
        }
    }

    // ==================== SESSIONS ====================
    public async Task<List<Session>> GetAllSessionsAsync()
    {
        var db = await GetConnectionAsync();
        var sessions = await db.Table<Session>()
            .Where(s => s.IsDeleted == 0)
            .OrderByDescending(s => s.Fecha)
            .ToListAsync();
        
        // Cargar el conteo de videos para cada sesión
        foreach (var session in sessions)
        {
            session.VideoCount = await db.Table<VideoClip>()
                .Where(v => v.SessionId == session.Id && v.IsDeleted == 0)
                .CountAsync();
        }
        
        return sessions;
    }

    public async Task<Session?> GetSessionByIdAsync(int id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Session>().FirstOrDefaultAsync(s => s.Id == id && s.IsDeleted == 0);
    }

    public async Task<int> SaveSessionAsync(Session session)
    {
        try
        {
            var db = await GetConnectionAsync();
            if (session.Id != 0)
            {
                await db.UpdateAsync(session);
                LogInfo($"Sesión actualizada: {session.DisplayName}");
                return session.Id;
            }
            else
            {
                await db.InsertAsync(session);
                LogSuccess($"Sesión creada: {session.DisplayName}");
                return session.Id;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error guardando sesión: {ex.Message}");
            throw;
        }
    }

    public async Task<int> InsertSessionWithIdAsync(Session session)
    {
        try
        {
            var db = await GetConnectionAsync();
            await db.InsertAsync(session);
            LogSuccess($"Sesión creada con Id explícito: {session.DisplayName}");
            return session.Id;
        }
        catch (Exception ex)
        {
            LogError($"Error insertando sesión con Id explícito: {ex.Message}");
            throw;
        }
    }

    public async Task<int> DeleteSessionAsync(Session session)
    {
        try
        {
            // Usar el método de eliminación en cascada que también borra archivos
            await DeleteSessionCascadeAsync(session.Id, deleteSessionFiles: true);
            return 1;
        }
        catch (Exception ex)
        {
            LogError($"Error eliminando sesión: {ex.Message}");
            throw;
        }
    }

    // ==================== VIDEO LESSONS ====================
    public async Task<List<VideoLesson>> GetAllVideoLessonsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoLesson>()
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync();
    }
    
    public async Task<int> GetVideoLessonsCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoLesson>().CountAsync();
    }

    public async Task<List<VideoLesson>> GetVideoLessonsBySessionAsync(int sessionId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoLesson>()
            .Where(v => v.SessionId == sessionId)
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task<int> SaveVideoLessonAsync(VideoLesson lesson)
    {
        try
        {
            var db = await GetConnectionAsync();
            if (lesson.Id != 0)
            {
                await db.UpdateAsync(lesson);
                LogInfo($"Videolección actualizada: {lesson.DisplayTitle}");
                return lesson.Id;
            }

            await db.InsertAsync(lesson);
            LogSuccess($"Videolección guardada: {lesson.DisplayTitle}");
            return lesson.Id;
        }
        catch (Exception ex)
        {
            LogError($"Error guardando videolección: {ex.Message}");
            throw;
        }
    }

    public async Task<int> DeleteVideoLessonAsync(VideoLesson lesson)
    {
        try
        {
            var db = await GetConnectionAsync();
            var result = await db.DeleteAsync(lesson);
            LogInfo($"Videolección eliminada: {lesson.DisplayTitle}");
            return result;
        }
        catch (Exception ex)
        {
            LogError($"Error eliminando videolección: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Elimina un VideoClip de la base de datos y sus dependencias (inputs, timing events)
    /// No elimina archivos físicos, solo la referencia en BD.
    /// </summary>
    public async Task DeleteVideoClipAsync(int videoId)
    {
        try
        {
            var db = await GetConnectionAsync();
            
            // Eliminar dependencias primero
            await db.ExecuteAsync("DELETE FROM \"execution_timing_events\" WHERE VideoID = ?;", videoId);
            await db.ExecuteAsync("DELETE FROM \"input\" WHERE VideoID = ?;", videoId);
            await db.ExecuteAsync("DELETE FROM \"split_time_data\" WHERE VideoID = ?;", videoId);
            
            // Eliminar el clip
            await db.ExecuteAsync("DELETE FROM videoClip WHERE ID = ?;", videoId);
            
            LogInfo($"VideoClip eliminado: ID={videoId}");
        }
        catch (Exception ex)
        {
            LogError($"Error eliminando VideoClip {videoId}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Elimina todos los inputs asociados a un VideoClip (tags y eventos).
    /// </summary>
    public async Task DeleteInputsByVideoAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE VideoID = ?;", videoId);
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

    /// <summary>
    /// Devuelve una lista de atletas únicos (deduplicados por nombre+apellido normalizados),
    /// manteniendo el atleta con menor ID en cada grupo.
    /// </summary>
    public async Task<List<Athlete>> GetUniqueAthletesAsync()
    {
        var db = await GetConnectionAsync();
        var allAthletes = await db.Table<Athlete>().ToListAsync();

        var unique = allAthletes
            .GroupBy(a => (NormalizeString(a.Nombre).ToLowerInvariant(), NormalizeString(a.Apellido).ToLowerInvariant()))
            .Select(g => g.OrderBy(a => a.Id).First())
            .OrderBy(a => a.Apellido)
            .ThenBy(a => a.Nombre)
            .ToList();

        // Cargar las categorías igual que en GetAllAthletesAsync
        var categories = await db.Table<Category>().ToListAsync();
        foreach (var athlete in unique)
        {
            var category = categories.FirstOrDefault(c => c.Id == athlete.CategoriaId);
            athlete.CategoriaNombre = category?.NombreCategoria;
        }

        return unique;
    }

    /// <summary>
    /// Busca un atleta existente por nombre y apellido (case-insensitive, normalizado).
    /// Devuelve null si no lo encuentra; devuelve el primer match si hay varios.
    /// </summary>
    public async Task<Athlete?> FindAthleteByNameAsync(string? nombre, string? apellido)
    {
        var normalizedNombre = NormalizeString(nombre);
        var normalizedApellido = NormalizeString(apellido);
        
        if (string.IsNullOrEmpty(normalizedNombre) && string.IsNullOrEmpty(normalizedApellido))
            return null;

        var db = await GetConnectionAsync();
        var athletes = await db.Table<Athlete>().ToListAsync();

        // Buscar match exacto (normalizado, case-insensitive)
        var match = athletes.FirstOrDefault(a =>
            string.Equals(NormalizeString(a.Nombre), normalizedNombre, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeString(a.Apellido), normalizedApellido, StringComparison.OrdinalIgnoreCase));

        return match;
    }

    /// <summary>
    /// Normaliza un string: trim, colapsar espacios múltiples en uno.
    /// </summary>
    private static string NormalizeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        
        // Trim y colapsar espacios múltiples
        return System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static void NormalizeAthleteForStorage(Athlete athlete)
    {
        // Nombre: trim + colapsar espacios.
        athlete.Nombre = NormalizeString(athlete.Nombre);

        // Apellido: trim + colapsar espacios + MAYÚSCULAS.
        var normalizedSurname = NormalizeString(athlete.Apellido);
        athlete.Apellido = string.IsNullOrEmpty(normalizedSurname)
            ? string.Empty
            : normalizedSurname.ToUpperInvariant();
    }

    /// <summary>
    /// Consolida atletas duplicados: mantiene el de menor ID y actualiza referencias.
    /// Devuelve el número de duplicados eliminados.
    /// </summary>
    public async Task<int> ConsolidateDuplicateAthletesAsync()
    {
        var db = await GetConnectionAsync();
        var allAthletes = await db.Table<Athlete>().ToListAsync();
        
        // Agrupar por nombre+apellido normalizados
        var groups = allAthletes
            .GroupBy(a => (NormalizeString(a.Nombre).ToLowerInvariant(), NormalizeString(a.Apellido).ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .ToList();

        int duplicatesRemoved = 0;

        foreach (var group in groups)
        {
            // Mantener el atleta con menor ID (el primero que se importó)
            var orderedAthletes = group.OrderBy(a => a.Id).ToList();
            var keepAthlete = orderedAthletes.First();
            var duplicatesToRemove = orderedAthletes.Skip(1).ToList();

            foreach (var duplicate in duplicatesToRemove)
            {
                // Actualizar referencias en videoClip (columna: AtletaID)
                await db.ExecuteAsync(
                    "UPDATE videoClip SET AtletaID = ? WHERE AtletaID = ?",
                    keepAthlete.Id, duplicate.Id);

                // Actualizar referencias en input (columna: AthleteID)
                await db.ExecuteAsync(
                    "UPDATE \"input\" SET AthleteID = ? WHERE AthleteID = ?",
                    keepAthlete.Id, duplicate.Id);

                // Actualizar referencias en valoracion (columna: AthleteID)
                await db.ExecuteAsync(
                    "UPDATE valoracion SET AthleteID = ? WHERE AthleteID = ?",
                    keepAthlete.Id, duplicate.Id);

                // Eliminar el duplicado
                await db.DeleteAsync(duplicate);
                duplicatesRemoved++;
            }
        }

        return duplicatesRemoved;
    }

    /// <summary>
    /// Fusiona manualmente una lista de atletas en uno solo.
    /// Actualiza todas las referencias y elimina los duplicados.
    /// </summary>
    public async Task<int> MergeAthletesAsync(int keepAthleteId, List<int> mergeAthleteIds)
    {
        var db = await GetConnectionAsync();
        int merged = 0;

        foreach (var duplicateId in mergeAthleteIds)
        {
            if (duplicateId == keepAthleteId) continue;

            // Actualizar referencias en videoClip (columna: AtletaID)
            await db.ExecuteAsync(
                "UPDATE videoClip SET AtletaID = ? WHERE AtletaID = ?",
                keepAthleteId, duplicateId);

            // Actualizar referencias en input (columna: AthleteID)
            await db.ExecuteAsync(
                "UPDATE \"input\" SET AthleteID = ? WHERE AthleteID = ?",
                keepAthleteId, duplicateId);

            // Actualizar referencias en valoracion (columna: AthleteID)
            await db.ExecuteAsync(
                "UPDATE valoracion SET AthleteID = ? WHERE AthleteID = ?",
                keepAthleteId, duplicateId);

            // Eliminar el atleta duplicado
            await db.ExecuteAsync("DELETE FROM Atleta WHERE id = ?", duplicateId);
            merged++;
        }

        return merged;
    }

    /// <summary>
    /// Busca atletas cuyo nombre o apellido contengan el texto dado (para sugerencias).
    /// </summary>
    public async Task<List<Athlete>> SearchAthletesAsync(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return new List<Athlete>();

        var db = await GetConnectionAsync();
        var athletes = await db.Table<Athlete>().ToListAsync();

        var lowerSearch = searchText.ToLowerInvariant();
        return athletes
            .Where(a =>
                (a.Nombre?.ToLowerInvariant().Contains(lowerSearch) ?? false) ||
                (a.Apellido?.ToLowerInvariant().Contains(lowerSearch) ?? false) ||
                (a.NombreCompleto?.ToLowerInvariant().Contains(lowerSearch) ?? false))
            .OrderBy(a => a.Apellido)
            .ThenBy(a => a.Nombre)
            .ToList();
    }

    /// <summary>
    /// Inserta un atleta nuevo (sin PK prefijada) y devuelve el ID autogenerado.
    /// </summary>
    public async Task<int> InsertAthleteAsync(Athlete athlete)
    {
        var db = await GetConnectionAsync();
        // Forzar Id=0 para que SQLite asigne uno nuevo con AUTOINCREMENT
        athlete.Id = 0;

        NormalizeAthleteForStorage(athlete);
        await db.InsertAsync(athlete);
        return athlete.Id;
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

        NormalizeAthleteForStorage(athlete);
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
            .Where(v => v.SessionId == sessionId && v.IsDeleted == 0)
            .OrderByDescending(v => v.CreationDate)
            .ToListAsync();

        // Cargar caché de datos relacionados
        await EnsureCacheLoadedAsync(db);

        // Asegurar rutas locales para reproducción/miniaturas
        _sessionCache!.TryGetValue(sessionId, out var session);
        foreach (var clip in clips)
        {
            HydrateLocalMediaPaths(clip, session?.PathSesion);
        }

        // Hidratar atletas desde caché
        foreach (var clip in clips)
        {
            if (_athleteCache!.TryGetValue(clip.AtletaId, out var athlete))
            {
                if (_categoryCache!.TryGetValue(athlete.CategoriaId, out var category))
                    athlete.CategoriaNombre = category.NombreCategoria;
                clip.Atleta = athlete;
            }
        }

        // Cargar tags para cada video
        var clipIds = clips.Select(c => c.Id).ToList();
        var allInputsForClips = await GetInputsForVideosAsync(db, clipIds);
        HydrateTagsForClipsInternal(clips, _tagCache!.Values.ToList(), _eventTagCache!.Values.ToList(), allInputsForClips);

        // Indicar si hay cronometraje/split guardado para pintar un overlay en miniaturas
        var splitVideoIds = allInputsForClips
            .Where(i => i.InputTypeId == SplitTimeInputTypeId)
            .Select(i => i.VideoId)
            .ToHashSet();
        var timingEventVideoIds = await GetVideoIdsWithExecutionTimingEventsAsync(db, clipIds);
        splitVideoIds.UnionWith(timingEventVideoIds);
        foreach (var clip in clips)
            clip.HasTiming = splitVideoIds.Contains(clip.Id);

        return clips;
    }

    public async Task<VideoClip?> GetVideoClipByIdAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.Id == videoId && v.IsDeleted == 0);
    }

    public async Task<List<VideoClip>> GetVideoClipsByAthleteAsync(int athleteId)
    {
        var db = await GetConnectionAsync();
        var clips = await db.Table<VideoClip>()
            .Where(v => v.AtletaId == athleteId && v.IsDeleted == 0)
            .OrderByDescending(v => v.CreationDate)
            .ToListAsync();

        // Hidratar rutas locales por sesión (para que el reproductor encuentre los archivos)
        var sessions = await db.Table<Session>().Where(s => s.IsDeleted == 0).ToListAsync();
        var sessionById = sessions.ToDictionary(s => s.Id, s => s);
        foreach (var clip in clips)
        {
            sessionById.TryGetValue(clip.SessionId, out var session);
            HydrateLocalMediaPaths(clip, session?.PathSesion);
        }

        // Indicar si hay cronometraje/split guardado para pintar un overlay en miniaturas
        var clipIds = clips.Select(c => c.Id).ToList();
        var allInputsForClips = await GetInputsForVideosAsync(db, clipIds);
        var splitVideoIds = allInputsForClips
            .Where(i => i.InputTypeId == SplitTimeInputTypeId)
            .Select(i => i.VideoId)
            .ToHashSet();
        var timingEventVideoIds = await GetVideoIdsWithExecutionTimingEventsAsync(db, clipIds);
        splitVideoIds.UnionWith(timingEventVideoIds);
        foreach (var clip in clips)
            clip.HasTiming = splitVideoIds.Contains(clip.Id);

        return clips;
    }

    /// <summary>
    /// Obtiene todos los video clips de todas las sesiones
    /// </summary>
    public async Task<List<VideoClip>> GetAllVideoClipsAsync()
    {
        var db = await GetConnectionAsync();
        var clips = await db.Table<VideoClip>()
            .Where(v => v.IsDeleted == 0)
            .OrderByDescending(v => v.CreationDate)
            .ToListAsync();

        // Cargar caché de datos relacionados
        await EnsureCacheLoadedAsync(db);

        // Hidratar rutas locales por sesión
        foreach (var clip in clips)
        {
            _sessionCache!.TryGetValue(clip.SessionId, out var session);
            HydrateLocalMediaPaths(clip, session?.PathSesion);
        }

        // Hidratar atletas desde caché
        foreach (var clip in clips)
        {
            if (_athleteCache!.TryGetValue(clip.AtletaId, out var athlete))
            {
                if (_categoryCache!.TryGetValue(athlete.CategoriaId, out var category))
                    athlete.CategoriaNombre = category.NombreCategoria;
                clip.Atleta = athlete;
            }
        }

        // Cargar tags
        var clipIds = clips.Select(c => c.Id).ToList();
        var allInputsForClips = await GetInputsForVideosAsync(db, clipIds);
        HydrateTagsForClipsInternal(clips, _tagCache!.Values.ToList(), _eventTagCache!.Values.ToList(), allInputsForClips);

        // Indicar si hay cronometraje/split guardado para pintar un overlay en miniaturas
        var splitVideoIds = allInputsForClips
            .Where(i => i.InputTypeId == SplitTimeInputTypeId)
            .Select(i => i.VideoId)
            .ToHashSet();
        var timingEventVideoIds = await GetVideoIdsWithExecutionTimingEventsAsync(db, clipIds);
        splitVideoIds.UnionWith(timingEventVideoIds);
        foreach (var clip in clips)
            clip.HasTiming = splitVideoIds.Contains(clip.Id);

        return clips;
    }

    public async Task<int> SaveVideoClipAsync(VideoClip clip)
    {
        try
        {
            var db = await GetConnectionAsync();
            var existing = await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.Id == clip.Id && v.SessionId == clip.SessionId);
            if (existing != null)
            {
                // Actualizar todos los campos editables
                existing.LocalClipPath = clip.LocalClipPath;
                existing.LocalThumbnailPath = clip.LocalThumbnailPath;
                existing.AtletaId = clip.AtletaId;
                existing.Section = clip.Section;
                existing.IsFavorite = clip.IsFavorite;
                await db.UpdateAsync(existing);
                LogInfo($"VideoClip actualizado: ID {existing.Id}");
                return existing.Id;
            }
            else
            {
                await db.InsertAsync(clip);
                LogInfo($"VideoClip insertado: ID {clip.Id}");
                return clip.Id;
            }
        }
        catch (Exception ex)
        {
            LogError($"Error guardando VideoClip: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Inserta un nuevo VideoClip y devuelve su ID autogenerado.
    /// A diferencia de SaveVideoClipAsync, siempre fuerza inserción (no busca existente).
    /// </summary>
    public async Task<int> InsertVideoClipAsync(VideoClip clip)
    {
        var db = await GetConnectionAsync();
        // Forzar Id=0 para que SQLite asigne uno nuevo con AUTOINCREMENT
        clip.Id = 0;
        await db.InsertAsync(clip);
        return clip.Id;
    }

    /// <summary>
    /// Actualiza todos los campos de un VideoClip existente.
    /// </summary>
    public async Task<int> UpdateVideoClipAsync(VideoClip clip)
    {
        try
        {
            var db = await GetConnectionAsync();
            await db.UpdateAsync(clip);
            LogInfo($"VideoClip actualizado: ID {clip.Id}");
            return clip.Id;
        }
        catch (Exception ex)
        {
            LogError($"Error actualizando VideoClip: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Obtiene todos los VideoClips pendientes de sincronizar (local pero no subidos a cloud).
    /// </summary>
    public async Task<List<VideoClip>> GetUnsyncedVideoClipsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoClip>()
            .Where(v => v.IsDeleted == 0 && v.IsSynced == 0 && !string.IsNullOrEmpty(v.LocalClipPath))
            .ToListAsync();
    }

    /// <summary>
    /// Obtiene todos los VideoClips que solo existen en remoto (necesitan descarga).
    /// </summary>
    public async Task<List<VideoClip>> GetRemoteOnlyVideoClipsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoClip>()
            .Where(v => v.IsDeleted == 0 && v.Source == "remote" && string.IsNullOrEmpty(v.LocalClipPath))
            .ToListAsync();
    }

    // ==================== CATEGORIES ====================

    /// <summary>
    /// Sincroniza la tabla categoria con los CategoriaId únicos usados en atletas e inputs
    /// </summary>
    private async Task SyncCategoriesFromReferencesAsync()
    {
        var db = await GetConnectionAsync();
        
        // Obtener todos los CategoriaId únicos de atletas
        var athletes = await db.Table<Athlete>().ToListAsync();
        var athleteCategoryIds = athletes
            .Where(a => a.CategoriaId > 0)
            .Select(a => a.CategoriaId)
            .Distinct();

        // Obtener todos los CategoriaId únicos de inputs
        var inputs = await db.Table<Input>().ToListAsync();
        var inputCategoryIds = inputs
            .Where(i => i.CategoriaId > 0)
            .Select(i => i.CategoriaId)
            .Distinct();

        // Unir todos los IDs únicos
        var allCategoryIds = athleteCategoryIds.Union(inputCategoryIds).Distinct().ToList();

        // Obtener categorías existentes
        var existingCategories = await db.Table<Category>().ToListAsync();
        var existingIds = existingCategories.Select(c => c.Id).ToHashSet();
        
        // También crear un set de nombres existentes para evitar duplicados
        var existingNames = existingCategories
            .Where(c => !string.IsNullOrWhiteSpace(c.NombreCategoria))
            .Select(c => c.NombreCategoria!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Insertar categorías que no existen (ni por ID ni por nombre)
        foreach (var categoryId in allCategoryIds)
        {
            var genericName = $"Categoría {categoryId}";
            // Solo insertar si no existe el ID Y no existe ya una categoría con ese nombre genérico
            if (!existingIds.Contains(categoryId) && !existingNames.Contains(genericName))
            {
                // Usar ExecuteAsync para insertar con ID específico
                await db.ExecuteAsync(
                    "INSERT OR IGNORE INTO categoria (id, nombreCategoria, isSystemDefault) VALUES (?, ?, 0)",
                    categoryId, genericName);
            }
        }
    }

    public async Task<List<Category>> GetAllCategoriesAsync()
    {
        await SyncCategoriesFromReferencesAsync();
        var db = await GetConnectionAsync();
        return await db.Table<Category>().OrderBy(c => c.NombreCategoria).ToListAsync();
    }

    public async Task<Category?> GetCategoryByIdAsync(int id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Category>().FirstOrDefaultAsync(c => c.Id == id);
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

    public async Task<List<Valoracion>> GetValoracionesByAthleteAsync(int athleteId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Valoracion>().Where(v => v.AthleteId == athleteId).ToListAsync();
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

    // ==================== TAGS ====================

    /// <summary>
    /// Sincroniza la tabla tags limpiando datos huérfanos.
    /// Ya NO crea tags genéricos automáticamente - los tags deben crearse explícitamente.
    /// </summary>
    private async Task SyncTagsFromInputsAsync()
    {
        var db = await GetConnectionAsync();
        
        // Obtener todos los tags existentes
        var existingTags = await db.Table<Tag>().ToListAsync();
        var existingIds = existingTags.Select(t => t.Id).ToHashSet();
        
        // Obtener InputTypeIds que están siendo usados en inputs de TAGS (IsEvent = 0)
        // Importante: no tocar inputs de eventos (IsEvent = 1), ya que su InputTypeId apunta a EventTagDefinition.
        var inputs = await db.Table<Input>().Where(i => i.IsEvent == 0).ToListAsync();
        var usedTagIds = inputs
            .Where(i => i.InputTypeId > 0)
            .Select(i => i.InputTypeId)
            .Distinct()
            .ToHashSet();
        
        // Limpiar inputs de TAGS que referencian tags que no existen (datos huérfanos)
        foreach (var input in inputs.Where(i => i.InputTypeId > 0 && !existingIds.Contains(i.InputTypeId)))
        {
            await db.DeleteAsync(input);
            System.Diagnostics.Debug.WriteLine($"[SyncTagsFromInputs] Eliminado input huérfano con InputTypeId={input.InputTypeId}");
        }
    }

    /// <summary>
    /// Elimina tags duplicados por nombre y tags genéricos huérfanos (Tag 1, Tag 2, etc.)
    /// </summary>
    private async Task CleanDuplicateTagsAsync()
    {
        var db = await GetConnectionAsync();
        var allTags = await db.Table<Tag>().ToListAsync();
        
        // 1. Eliminar tags genéricos huérfanos (Tag 1, Tag 2, etc.) que no tienen ninguna referencia real
        var genericTagPattern = new System.Text.RegularExpressions.Regex(@"^Tag \d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var genericTags = allTags.Where(t => !string.IsNullOrEmpty(t.NombreTag) && genericTagPattern.IsMatch(t.NombreTag)).ToList();
        
        foreach (var genericTag in genericTags)
        {
            // Verificar si este tag tiene referencias en input
            var hasReferences = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM input WHERE InputTypeID = ? AND IsEvent = 0", genericTag.Id);
            
            if (hasReferences == 0)
            {
                // No tiene referencias, eliminar
                await db.DeleteAsync(genericTag);
                System.Diagnostics.Debug.WriteLine($"[CleanDuplicateTags] Eliminado tag genérico sin referencias: ID={genericTag.Id}, Nombre='{genericTag.NombreTag}'");
            }
            else
            {
                // Tiene referencias pero es un tag genérico, eliminar las referencias y el tag
                await db.ExecuteAsync("DELETE FROM input WHERE InputTypeID = ? AND IsEvent = 0", genericTag.Id);
                await db.DeleteAsync(genericTag);
                System.Diagnostics.Debug.WriteLine($"[CleanDuplicateTags] Eliminado tag genérico con referencias huérfanas: ID={genericTag.Id}, Nombre='{genericTag.NombreTag}'");
            }
        }
        
        // Recargar tags después de limpiar genéricos
        allTags = await db.Table<Tag>().ToListAsync();
        
        // 2. Agrupar por nombre (case insensitive) y eliminar duplicados
        var duplicateGroups = allTags
            .Where(t => !string.IsNullOrWhiteSpace(t.NombreTag))
            .GroupBy(t => t.NombreTag!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            // Mantener el de menor ID, eliminar el resto
            var toKeep = group.OrderBy(t => t.Id).First();
            var toDelete = group.Where(t => t.Id != toKeep.Id).ToList();
            
            foreach (var tag in toDelete)
            {
                // Actualizar referencias en inputs
                await db.ExecuteAsync(
                    "UPDATE input SET InputTypeID = ? WHERE InputTypeID = ? AND IsEvent = 0",
                    toKeep.Id, tag.Id);
                
                // Eliminar el duplicado
                await db.DeleteAsync(tag);
                System.Diagnostics.Debug.WriteLine($"[CleanDuplicateTags] Eliminado tag duplicado: ID={tag.Id}, Nombre='{tag.NombreTag}'");
            }
        }
    }

    public async Task<List<Tag>> GetAllTagsAsync()
    {
        await CleanDuplicateTagsAsync();
        await SyncTagsFromInputsAsync();
        var db = await GetConnectionAsync();
        return await db.Table<Tag>().OrderBy(t => t.NombreTag).ToListAsync();
    }

    /// <summary>
    /// Guarda o actualiza un tag en la base de datos
    /// </summary>
    public async Task<int> SaveTagAsync(Tag tag)
    {
        var db = await GetConnectionAsync();
        var existing = await db.Table<Tag>().FirstOrDefaultAsync(t => t.Id == tag.Id);
        if (existing != null)
        {
            await db.UpdateAsync(tag);
            return tag.Id;
        }
        else
        {
            await db.InsertAsync(tag);
            return tag.Id;
        }
    }

    // ==================== EVENT TAG DEFINITIONS ====================
    public async Task<List<EventTagDefinition>> GetAllEventTagsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<EventTagDefinition>().ToListAsync();
    }

    public async Task<EventTagDefinition?> GetEventTagByIdAsync(int id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<EventTagDefinition>().FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<EventTagDefinition?> FindEventTagByNameAsync(string name)
    {
        var db = await GetConnectionAsync();
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        return await db.Table<EventTagDefinition>()
            .Where(t => t.Nombre != null && t.Nombre.ToLower() == normalized)
            .FirstOrDefaultAsync();
    }

    public async Task<int> InsertEventTagAsync(EventTagDefinition eventTag)
    {
        var db = await GetConnectionAsync();
        await db.InsertAsync(eventTag);
        return eventTag.Id;
    }

    public async Task<bool> DeleteEventTagAsync(int eventTagId)
    {
        var db = await GetConnectionAsync();

        // Verificar si es un tag de sistema - no se puede borrar
        var eventTag = await db.Table<EventTagDefinition>()
            .Where(t => t.Id == eventTagId)
            .FirstOrDefaultAsync();
        
        if (eventTag?.IsSystem == true)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] No se puede borrar tag de sistema: {eventTag.Nombre}");
            return false;
        }

        // Borrar ocurrencias SOLO de eventos
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE IsEvent = 1 AND InputTypeID = ?", eventTagId);

        // Borrar del catálogo de eventos
        await db.ExecuteAsync("DELETE FROM \"event_tags\" WHERE id = ?", eventTagId);
        return true;
    }

    /// <summary>
    /// Obtiene los tags de evento de sistema (penalizaciones)
    /// </summary>
    public async Task<List<EventTagDefinition>> GetSystemEventTagsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<EventTagDefinition>()
            .Where(t => t.IsSystem)
            .ToListAsync();
    }

    /// <summary>
    /// Obtiene los tags asignados a un video específico (no eventos)
    /// </summary>
    public async Task<List<Tag>> GetTagsForVideoAsync(int videoId)
    {
        var db = await GetConnectionAsync();

        // Tags asignados: IsEvent == 0
        var assignedInputs = await db.Table<Input>()
            .Where(i => i.VideoId == videoId && i.IsEvent == 0)
            .ToListAsync();

        var inputTypeIds = assignedInputs.Select(i => i.InputTypeId).Distinct().ToHashSet();
        if (inputTypeIds.Count == 0)
            return new List<Tag>();

        var allTags = await db.Table<Tag>().ToListAsync();
        return allTags
            .Where(t => inputTypeIds.Contains(t.Id) && !string.IsNullOrEmpty(t.NombreTag))
            .ToList();
    }

    /// <summary>
    /// Obtiene todos los inputs para un video específico
    /// </summary>
    public async Task<List<Input>> GetInputsForVideoAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Input>()
            .Where(i => i.VideoId == videoId)
            .ToListAsync();
    }

    /// <summary>
    /// Obtiene los eventos de etiquetas para un video específico
    /// </summary>
    public async Task<List<TagEvent>> GetTagEventsForVideoAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        
        // Diagnóstico: tamaño del archivo
        long fileSize = 0;
        try { fileSize = new FileInfo(_dbPath).Length; } catch { }
        
        // Diagnóstico global
        var globalTotal = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM input");
        var maxId = await db.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(id),0) FROM input");
        var globalEvents = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM input WHERE IsEvent=1");
        
        // Eventos: inputs con IsEvent == 1
        var inputs = await db.Table<Input>()
            .Where(i => i.VideoId == videoId && i.IsEvent == 1)
            .ToListAsync();

        AppLog.Info("DatabaseService", $"GetTagEventsForVideoAsync: dbPath={_dbPath}, fileSize={fileSize}, videoId={videoId}, events={inputs.Count}, globalTotal={globalTotal}, maxId={maxId}, globalEvents={globalEvents}");
        
        if (inputs.Count == 0)
            return new List<TagEvent>();

        // Obtener catálogo de tipos de evento (separado de tags)
        var allEventDefs = await db.Table<EventTagDefinition>().ToListAsync();
        var eventDict = allEventDefs.ToDictionary(t => t.Id, t => t.Nombre ?? "");

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[GetTagEventsForVideoAsync] EventTagDefinitions count: {allEventDefs.Count}");
        foreach (var def in allEventDefs)
        {
            System.Diagnostics.Debug.WriteLine($"  EventTagDef Id={def.Id}, Nombre={def.Nombre}");
        }
#endif
        
        // Crear lista de eventos ordenados por timestamp.
        // IMPORTANTE: no filtrar por catálogo; si el tipo no existe (o fue borrado) lo mostramos igual
        // para que el evento "persista" visualmente como ocurre con los tags asignados.
        var events = inputs
            .Select(i =>
            {
                var name = eventDict.TryGetValue(i.InputTypeId, out var n) ? n : null;
                if (string.IsNullOrWhiteSpace(name))
                    name = $"Evento {i.InputTypeId}";

                return new TagEvent
                {
                    InputId = i.Id,
                    TagId = i.InputTypeId,
                    TagName = name,
                    TimestampMs = i.TimeStamp
                };
            })
            .OrderBy(e => e.TimestampMs)
            .ToList();

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[GetTagEventsForVideoAsync] Final events count after filtering: {events.Count}");
#endif
        
        return events;
    }

    /// <summary>
    /// Añade un evento de etiqueta en un timestamp específico del video
    /// </summary>
    public async Task<int> AddTagEventAsync(int videoId, int tagId, long timestampMs, int sessionId, int athleteId)
    {
        var db = await GetConnectionAsync();
        
        // Diagnóstico: tamaño del archivo ANTES
        long fileSizeBefore = 0;
        try { fileSizeBefore = new FileInfo(_dbPath).Length; } catch { }
        
        // Diagnóstico: estado previo
        var beforeTotal = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM input");
        var maxIdBefore = await db.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(id),0) FROM input");
        AppLog.Info("DatabaseService", $"AddTagEventAsync BEFORE: dbPath={_dbPath}, fileSize={fileSizeBefore}, videoId={videoId}, totalInputs={beforeTotal}, maxId={maxIdBefore}");
        
        var input = new Input
        {
            VideoId = videoId,
            InputTypeId = tagId,
            TimeStamp = timestampMs,
            SessionId = sessionId,
            AthleteId = athleteId,
            InputDateTime = DateTimeOffset.Now.ToUnixTimeSeconds(),
            IsEvent = 1
        };
        
        await db.InsertAsync(input);
        
        // Forzar checkpoint WAL si existe
        try { await db.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE)"); } catch { }
        
        // Verificar el registro recién insertado
        var insertedRow = await db.FindAsync<Input>(input.Id);
        var insertedExists = insertedRow != null;
        var insertedIsEvent = insertedRow?.IsEvent ?? -1;
        
        // Forzar escritura a disco con SELECT inmediato
        var afterTotal = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM input");
        var maxIdAfter = await db.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(id),0) FROM input");
        var eventCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM input WHERE IsEvent=1");
        
        // Diagnóstico: tamaño del archivo DESPUÉS
        long fileSizeAfter = 0;
        try { fileSizeAfter = new FileInfo(_dbPath).Length; } catch { }
        
        AppLog.Info("DatabaseService", $"AddTagEventAsync AFTER: dbPath={_dbPath}, fileSize={fileSizeAfter} (delta={fileSizeAfter-fileSizeBefore}), videoId={videoId}, inputId={input.Id}, totalInputs={afterTotal}, maxId={maxIdAfter}, eventCount={eventCount}, insertedExists={insertedExists}, insertedIsEvent={insertedIsEvent}");
        
        return input.Id;
    }

    /// <summary>
    /// Elimina un evento de etiqueta específico
    /// </summary>
    public async Task DeleteTagEventAsync(int inputId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE id = ?", inputId);
    }

    // ==================== CRONOMETRAJE EJECUCIÓN ====================

    /// <summary>
    /// Guarda una lista de eventos de cronometraje de ejecución asociados a un vídeo.
    /// </summary>
    public async Task InsertExecutionTimingEventsAsync(IEnumerable<ExecutionTimingEvent> events)
    {
        var db = await GetConnectionAsync();
        var list = events as IList<ExecutionTimingEvent> ?? events.ToList();
        if (list.Count == 0) return;
        await db.InsertAllAsync(list);
    }

    /// <summary>
    /// Obtiene los eventos de cronometraje de ejecución de un vídeo.
    /// </summary>
    public async Task<List<ExecutionTimingEvent>> GetExecutionTimingEventsByVideoAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<ExecutionTimingEvent>()
            .Where(e => e.VideoId == videoId)
            .OrderBy(e => e.ElapsedMilliseconds)
            .ToListAsync();
    }

    /// <summary>
    /// Obtiene todos los eventos de cronometraje de ejecución de una sesión.
    /// </summary>
    public async Task<List<ExecutionTimingEvent>> GetExecutionTimingEventsBySessionAsync(int sessionId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<ExecutionTimingEvent>()
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.VideoId)
            .ThenBy(e => e.ElapsedMilliseconds)
            .ToListAsync();
    }

    /// <summary>
    /// Elimina todos los eventos de cronometraje de ejecución asociados a un vídeo.
    /// </summary>
    public async Task DeleteExecutionTimingEventsByVideoAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM \"execution_timing_events\" WHERE VideoID = ?", videoId);
    }

    /// <summary>
    /// Busca un tag por su nombre
    /// </summary>
    public async Task<Tag?> FindTagByNameAsync(string tagName)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Tag>()
            .Where(t => t.NombreTag != null && t.NombreTag.ToLower() == tagName.ToLower())
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Inserta un nuevo tag y devuelve su ID autogenerado
    /// </summary>
    public async Task<int> InsertTagAsync(Tag tag)
    {
        var db = await GetConnectionAsync();
        await db.InsertAsync(tag);
        return tag.Id;
    }

    // ==================== ALL INPUTS ====================
    public async Task<List<Input>> GetAllInputsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Input>().ToListAsync();
    }

    // ==================== ESTADÍSTICAS ====================
    public async Task<int> GetTotalSessionsCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Session>().Where(s => s.IsDeleted == 0).CountAsync();
    }

    public async Task<int> GetTotalVideosCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoClip>().Where(v => v.IsDeleted == 0).CountAsync();
    }

    // ==================== BORRADO ====================
    public async Task DeleteSessionCascadeAsync(int sessionId, bool deleteSessionFiles)
    {
        var db = await GetConnectionAsync();

        var session = await db.Table<Session>().FirstOrDefaultAsync(s => s.Id == sessionId);

        // Obtener los videos antes de borrarlos para poder eliminar sus archivos
        var videos = await db.Table<VideoClip>().Where(v => v.SessionId == sessionId).ToListAsync();

        // Borrar archivos de video y miniaturas individuales
        if (deleteSessionFiles)
        {
            foreach (var video in videos)
            {
                try
                {
                    // Eliminar archivo de video
                    var videoPath = video.LocalClipPath;
                    if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                    {
                        File.Delete(videoPath);
                        LogInfo($"Archivo de video eliminado: {videoPath}");
                    }

                    // Eliminar miniatura
                    var thumbPath = video.LocalThumbnailPath;
                    if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
                    {
                        File.Delete(thumbPath);
                        LogInfo($"Miniatura eliminada: {thumbPath}");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error eliminando archivos del video {video.Id}: {ex.Message}");
                    // Continuar con los demás videos
                }
            }
        }

        // Borrar filas dependientes.
        // Nota: usamos SQL explícito para asegurar que se usa el nombre real de tabla/columna,
        // especialmente en tablas con nombres conflictivos como "input".
        await db.ExecuteAsync("DELETE FROM videoClip WHERE SessionID = ?;", sessionId);
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE SessionID = ?;", sessionId);
        await db.ExecuteAsync("DELETE FROM valoracion WHERE SessionID = ?;", sessionId);

        // Borrar sesión (la tabla real es "sesion" y la PK es "id")
        await db.ExecuteAsync("DELETE FROM sesion WHERE id = ?;", sessionId);

        // Borrar carpeta de la sesión si existe (carpeta Media/<sesión>)
        if (deleteSessionFiles && !string.IsNullOrWhiteSpace(session?.PathSesion))
        {
            try
            {
                var path = session.PathSesion;
                var mediaRoot = Path.Combine(FileSystem.AppDataDirectory, "Media");

                // Seguridad: solo borrar dentro de AppDataDirectory/Media
                if (Directory.Exists(path))
                {
                    var fullPath = Path.GetFullPath(path);
                    var fullRoot = Path.GetFullPath(mediaRoot);
                    if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.Delete(fullPath, recursive: true);
                        LogInfo($"Carpeta de sesión eliminada: {path}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error eliminando carpeta de sesión: {ex.Message}");
                // Si no se puede borrar, no bloqueamos el borrado en DB.
            }
        }
        
        LogSuccess($"Sesión {sessionId} eliminada completamente");
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

    // ======================= USER PROFILE =======================

    /// <summary>
    /// Obtiene el perfil del usuario. Solo existe un perfil (el del usuario de la app).
    /// </summary>
    public async Task<UserProfile?> GetUserProfileAsync()
    {
        var db = await GetConnectionAsync();
        var profiles = await db.Table<UserProfile>().ToListAsync();
        return profiles.FirstOrDefault();
    }

    /// <summary>
    /// Guarda o actualiza el perfil del usuario.
    /// </summary>
    public async Task SaveUserProfileAsync(UserProfile profile)
    {
        var db = await GetConnectionAsync();
        profile.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        if (profile.Id == 0)
        {
            profile.CreatedAt = profile.UpdatedAt;
            await db.InsertAsync(profile);
        }
        else
        {
            await db.UpdateAsync(profile);
        }
    }

    // ======================= TAGS MANAGEMENT =======================

    /// <summary>
    /// Añade un tag asignado a un video (no es evento)
    /// </summary>
    public async Task AddTagToVideoAsync(int videoId, int tagId, int sessionId, int athleteId)
    {
        var db = await GetConnectionAsync();
        
        // Verificar si ya existe como tag asignado
        var existing = await db.Table<Input>()
            .Where(i => i.VideoId == videoId && i.InputTypeId == tagId && i.IsEvent == 0)
            .FirstOrDefaultAsync();
        
        if (existing == null)
        {
            var input = new Input
            {
                VideoId = videoId,
                InputTypeId = tagId,
                SessionId = sessionId,
                AthleteId = athleteId,
                TimeStamp = 0,
                IsEvent = 0,  // Marcar como asignación, no evento
                InputDateTime = DateTimeOffset.Now.ToUnixTimeSeconds()
            };
            await db.InsertAsync(input);
        }
    }

    /// <summary>
    /// Elimina un tag asignado de un video (no afecta eventos)
    /// </summary>
    public async Task RemoveTagFromVideoAsync(int videoId, int tagId)
    {
        var db = await GetConnectionAsync();
        // Solo elimina el tag asignado (IsEvent == 0), no los eventos
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE VideoID = ? AND InputTypeID = ? AND IsEvent = 0", videoId, tagId);
    }

    /// <summary>
    /// Reemplaza todos los tags asignados de un video con los nuevos (no afecta eventos)
    /// </summary>
    public async Task SetVideoTagsAsync(int videoId, int sessionId, int athleteId, IEnumerable<int> tagIds)
    {
        var db = await GetConnectionAsync();
        
        // Eliminar solo los tags asignados actuales del video (IsEvent == 0). No tocar eventos.
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE VideoID = ? AND IsEvent = 0", videoId);
        
        // Añadir los nuevos como asignaciones
        foreach (var tagId in tagIds)
        {
            var input = new Input
            {
                VideoId = videoId,
                InputTypeId = tagId,
                SessionId = sessionId,
                AthleteId = athleteId,
                TimeStamp = 0,
                IsEvent = 0,  // Marcar como asignación, no evento
                InputDateTime = DateTimeOffset.Now.ToUnixTimeSeconds()
            };
            await db.InsertAsync(input);
        }
    }

    /// <summary>
    /// Elimina todos los eventos de un tag específico de un video (elimina las ocurrencias con IsEvent == 1)
    /// </summary>
    public async Task RemoveEventTagFromVideoAsync(int videoId, int tagId)
    {
        var db = await GetConnectionAsync();
        // Solo elimina eventos (IsEvent == 1), no los tags asignados
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE VideoID = ? AND InputTypeID = ? AND IsEvent = 1", videoId, tagId);
    }

    /// <summary>
    /// Elimina un tag de la base de datos y todas sus referencias en input
    /// </summary>
    public async Task DeleteTagAsync(int tagId)
    {
        var db = await GetConnectionAsync();
        // Primero eliminar todas las referencias de ASIGNACIÓN (no eventos)
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE IsEvent = 0 AND InputTypeID = ?", tagId);

        // Luego eliminar el tag del catálogo general
        await db.ExecuteAsync("DELETE FROM \"tags\" WHERE id = ?", tagId);
    }

    // ==================== SPLIT TIME ====================
    
    /// <summary>
    /// ID especial para los inputs de tipo "split time"
    /// Usamos un valor negativo para evitar colisiones con tags normales
    /// </summary>
    private const int SplitTimeInputTypeId = -1;

    /// <summary>
    /// Obtiene el split time guardado para un video específico
    /// </summary>
    public async Task<Input?> GetSplitTimeForVideoAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Input>()
            .Where(i => i.VideoId == videoId && i.InputTypeId == SplitTimeInputTypeId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Guarda o actualiza el split time para un video.
    /// Si ya existe, lo sobreescribe.
    /// </summary>
    public async Task SaveSplitTimeAsync(int videoId, int sessionId, string splitDataJson)
    {
        var db = await GetConnectionAsync();
        
        // Buscar si ya existe un split para este video
        var existing = await db.Table<Input>()
            .Where(i => i.VideoId == videoId && i.InputTypeId == SplitTimeInputTypeId)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            // Actualizar el existente
            existing.InputValue = splitDataJson;
            existing.InputDateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await db.UpdateAsync(existing);
        }
        else
        {
            // Crear nuevo
            var input = new Input
            {
                VideoId = videoId,
                SessionId = sessionId,
                InputTypeId = SplitTimeInputTypeId,
                InputValue = splitDataJson,
                InputDateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                TimeStamp = 0,
                IsEvent = 0 // No es un evento, es un dato calculado
            };
            await db.InsertAsync(input);
        }
    }

    /// <summary>
    /// Elimina el split time de un video
    /// </summary>
    public async Task DeleteSplitTimeAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE VideoID = ? AND InputTypeID = ?", videoId, SplitTimeInputTypeId);
    }

    /// <summary>
    /// Obtiene todos los split times para una sesión
    /// </summary>
    public async Task<List<Input>> GetSplitTimesForSessionAsync(int sessionId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<Input>()
            .Where(i => i.SessionId == sessionId && i.InputTypeId == SplitTimeInputTypeId)
            .ToListAsync();
    }

    #region Session Diary

    /// <summary>
    /// Obtiene el diario de sesión para una sesión y atleta específicos
    /// </summary>
    public async Task<SessionDiary?> GetSessionDiaryAsync(int sessionId, int athleteId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<SessionDiary>()
            .Where(d => d.SessionId == sessionId && d.AthleteId == athleteId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Guarda o actualiza el diario de sesión
    /// </summary>
    public async Task<int> SaveSessionDiaryAsync(SessionDiary diary)
    {
        var db = await GetConnectionAsync();
        var existing = await db.Table<SessionDiary>()
            .Where(d => d.SessionId == diary.SessionId && d.AthleteId == diary.AthleteId)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            diary.Id = existing.Id;
            diary.CreatedAt = existing.CreatedAt;
            diary.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return await db.UpdateAsync(diary);
        }
        else
        {
            diary.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            diary.UpdatedAt = diary.CreatedAt;
            return await db.InsertAsync(diary);
        }
    }

    /// <summary>
    /// Obtiene las medias de valoraciones para un atleta en los últimos N días
    /// </summary>
    public async Task<(double Fisica, double Mental, double Tecnica, int Count)> GetValoracionAveragesAsync(int athleteId, int days = 30)
    {
        var db = await GetConnectionAsync();
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
        
        var diaries = await db.Table<SessionDiary>()
            .Where(d => d.AthleteId == athleteId && d.CreatedAt >= cutoffDate && 
                   (d.ValoracionFisica > 0 || d.ValoracionMental > 0 || d.ValoracionTecnica > 0))
            .ToListAsync();

        if (diaries.Count == 0)
            return (0, 0, 0, 0);

        var avgFisica = diaries.Average(d => d.ValoracionFisica);
        var avgMental = diaries.Average(d => d.ValoracionMental);
        var avgTecnica = diaries.Average(d => d.ValoracionTecnica);

        return (avgFisica, avgMental, avgTecnica, diaries.Count);
    }

    /// <summary>
    /// Obtiene la evolución de valoraciones para un atleta en un rango de fechas
    /// </summary>
    public async Task<List<SessionDiary>> GetValoracionEvolutionAsync(int athleteId, long startDate, long endDate)
    {
        var db = await GetConnectionAsync();
        return await db.Table<SessionDiary>()
            .Where(d => d.AthleteId == athleteId && d.CreatedAt >= startDate && d.CreatedAt <= endDate && 
                   (d.ValoracionFisica > 0 || d.ValoracionMental > 0 || d.ValoracionTecnica > 0))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Obtiene todos los diarios de sesión para un atleta
    /// </summary>
    public async Task<List<SessionDiary>> GetAllSessionDiariesForAthleteAsync(int athleteId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<SessionDiary>()
            .Where(d => d.AthleteId == athleteId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Obtiene los diarios de sesión para un atleta en un período específico
    /// </summary>
    public async Task<List<SessionDiary>> GetSessionDiariesForPeriodAsync(int athleteId, DateTime startDate, DateTime endDate)
    {
        var db = await GetConnectionAsync();
        var startUnix = new DateTimeOffset(startDate).ToUnixTimeSeconds();
        var endUnix = new DateTimeOffset(endDate).ToUnixTimeSeconds();
        
        return await db.Table<SessionDiary>()
            .Where(d => d.AthleteId == athleteId && d.CreatedAt >= startUnix && d.CreatedAt <= endUnix)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Elimina el diario de una sesión
    /// </summary>
    public async Task DeleteSessionDiaryAsync(int sessionId, int athleteId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM \"SessionDiary\" WHERE SessionId = ? AND AthleteId = ?", sessionId, athleteId);
    }

    #endregion

    #region DailyWellness (Bienestar diario)

    /// <summary>
    /// Obtiene los datos de bienestar para una fecha específica
    /// </summary>
    public async Task<DailyWellness?> GetDailyWellnessAsync(DateTime date)
    {
        var db = await GetConnectionAsync();
        var dateOnly = date.Date;
        return await db.Table<DailyWellness>()
            .Where(w => w.Date == dateOnly)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Guarda o actualiza los datos de bienestar para una fecha
    /// </summary>
    public async Task<DailyWellness> SaveDailyWellnessAsync(DailyWellness wellness)
    {
        var db = await GetConnectionAsync();
        wellness.Date = wellness.Date.Date; // Asegurar solo fecha
        wellness.UpdatedAt = DateTime.Now;
        
        // Buscar si ya existe un registro para esta fecha
        var existing = await db.Table<DailyWellness>()
            .Where(w => w.Date == wellness.Date)
            .FirstOrDefaultAsync();
        
        if (existing != null)
        {
            wellness.Id = existing.Id;
            wellness.CreatedAt = existing.CreatedAt;
            await db.UpdateAsync(wellness);
        }
        else
        {
            wellness.CreatedAt = DateTime.Now;
            await db.InsertAsync(wellness);
        }
        
        return wellness;
    }

    /// <summary>
    /// Obtiene los datos de bienestar para un rango de fechas
    /// </summary>
    public async Task<List<DailyWellness>> GetDailyWellnessRangeAsync(DateTime startDate, DateTime endDate)
    {
        var db = await GetConnectionAsync();
        var start = startDate.Date;
        var end = endDate.Date;
        
        return await db.Table<DailyWellness>()
            .Where(w => w.Date >= start && w.Date <= end)
            .OrderBy(w => w.Date)
            .ToListAsync();
    }

    /// <summary>
    /// Elimina los datos de bienestar de una fecha
    /// </summary>
    public async Task DeleteDailyWellnessAsync(DateTime date)
    {
        var db = await GetConnectionAsync();
        var dateOnly = date.Date;
        await db.ExecuteAsync("DELETE FROM \"DailyWellness\" WHERE Date = ?", dateOnly);
    }

    /// <summary>
    /// Obtiene las fechas que tienen datos de bienestar en un mes
    /// </summary>
    public async Task<List<DateTime>> GetWellnessDatesInMonthAsync(int year, int month)
    {
        var db = await GetConnectionAsync();
        var startOfMonth = new DateTime(year, month, 1);
        var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
        
        var records = await db.Table<DailyWellness>()
            .Where(w => w.Date >= startOfMonth && w.Date <= endOfMonth && 
                       (w.SleepHours != null || w.RecoveryFeeling != null || w.Notes != null))
            .ToListAsync();
        
        return records.Select(w => w.Date).ToList();
    }

    #endregion

    #region Places CRUD & Merge

    /// <summary>
    /// Sincroniza la tabla lugar con los valores únicos de sesion.lugar
    /// </summary>
    private async Task SyncPlacesFromSessionsAsync()
    {
        try
        {
            var db = await GetConnectionAsync();
            
            // Obtener todos los lugares únicos de la tabla sesion
            var sessions = await db.Table<Session>().ToListAsync();
            System.Diagnostics.Debug.WriteLine($"[SyncPlaces] Total sesiones: {sessions.Count}");
            
            // Log de todos los lugares en sesiones (incluyendo vacíos)
            foreach (var s in sessions)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncPlaces] Sesion ID={s.Id}, Lugar='{s.Lugar ?? "(null)"}'");
            }
            
            var uniquePlaces = sessions
                .Where(s => !string.IsNullOrWhiteSpace(s.Lugar))
                .Select(s => s.Lugar!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"[SyncPlaces] Lugares únicos en sesiones: {uniquePlaces.Count}");
            foreach (var p in uniquePlaces)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncPlaces] - '{p}'");
            }

            // Obtener lugares existentes en la tabla lugar
            var existingPlaces = await db.Table<Place>().ToListAsync();
            System.Diagnostics.Debug.WriteLine($"[SyncPlaces] Lugares existentes en tabla: {existingPlaces.Count}");
            
            var existingNames = existingPlaces
                .Where(p => !string.IsNullOrWhiteSpace(p.NombreLugar))
                .Select(p => p.NombreLugar!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Insertar lugares que no existen
            int inserted = 0;
            foreach (var placeName in uniquePlaces)
            {
                if (!existingNames.Contains(placeName))
                {
                    await db.InsertAsync(new Place { NombreLugar = placeName });
                    inserted++;
                    System.Diagnostics.Debug.WriteLine($"[SyncPlaces] Insertado: '{placeName}'");
                }
            }
            System.Diagnostics.Debug.WriteLine($"[SyncPlaces] Total insertados: {inserted}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncPlaces] ERROR: {ex.Message}");
        }
    }

    /// <summary>
    /// Obtiene todos los lugares (sincroniza primero desde sesiones)
    /// </summary>
    public async Task<List<Place>> GetAllPlacesAsync()
    {
        await SyncPlacesFromSessionsAsync();
        var db = await GetConnectionAsync();
        var result = await db.Table<Place>().OrderBy(p => p.NombreLugar).ToListAsync();
        System.Diagnostics.Debug.WriteLine($"[GetAllPlacesAsync] Retornando {result.Count} lugares");
        return result;
    }

    /// <summary>
    /// Guarda un lugar (insert o update)
    /// </summary>
    public async Task<int> SavePlaceAsync(Place place)
    {
        var db = await GetConnectionAsync();
        if (place.Id != 0)
        {
            await db.UpdateAsync(place);
            return place.Id;
        }
        else
        {
            await db.InsertAsync(place);
            return place.Id;
        }
    }

    /// <summary>
    /// Fusiona lugares: mantiene el de keepPlaceId y actualiza referencias.
    /// Las sesiones que referencian los lugares duplicados (por nombre) 
    /// se actualizan al nombre del lugar que se mantiene.
    /// </summary>
    public async Task<int> MergePlacesAsync(int keepPlaceId, List<int> mergePlaceIds)
    {
        var db = await GetConnectionAsync();
        int merged = 0;

        var keepPlace = await db.Table<Place>().FirstOrDefaultAsync(p => p.Id == keepPlaceId);
        if (keepPlace == null) return 0;

        foreach (var duplicateId in mergePlaceIds)
        {
            if (duplicateId == keepPlaceId) continue;

            var duplicatePlace = await db.Table<Place>().FirstOrDefaultAsync(p => p.Id == duplicateId);
            if (duplicatePlace == null) continue;

            // Actualizar sesiones que referencian el lugar duplicado (por nombre)
            await db.ExecuteAsync(
                "UPDATE sesion SET lugar = ? WHERE lugar = ?",
                keepPlace.NombreLugar, duplicatePlace.NombreLugar);

            // Eliminar el lugar duplicado
            await db.ExecuteAsync("DELETE FROM lugar WHERE id = ?", duplicateId);
            merged++;
        }

        return merged;
    }

    #endregion

    #region SessionTypes CRUD & Merge

    /// <summary>
    /// Sincroniza la tabla tipoSesion con los valores únicos de sesion.tipoSesion
    /// </summary>
    private async Task SyncSessionTypesFromSessionsAsync()
    {
        var db = await GetConnectionAsync();
        
        // Obtener todos los tipos únicos de la tabla sesion
        var sessions = await db.Table<Session>().ToListAsync();
        var uniqueTypes = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.TipoSesion))
            .Select(s => s.TipoSesion!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Obtener tipos existentes en la tabla tipoSesion
        var existingTypes = await db.Table<SessionType>().ToListAsync();
        var existingNames = existingTypes
            .Where(t => !string.IsNullOrWhiteSpace(t.TipoSesion))
            .Select(t => t.TipoSesion!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Insertar tipos que no existen
        foreach (var typeName in uniqueTypes)
        {
            if (!existingNames.Contains(typeName))
            {
                await db.InsertAsync(new SessionType { TipoSesion = typeName });
            }
        }
    }

    /// <summary>
    /// Obtiene todos los tipos de sesión (sincroniza primero desde sesiones)
    /// </summary>
    public async Task<List<SessionType>> GetAllSessionTypesAsync()
    {
        await SyncSessionTypesFromSessionsAsync();
        var db = await GetConnectionAsync();
        return await db.Table<SessionType>().OrderBy(s => s.TipoSesion).ToListAsync();
    }

    /// <summary>
    /// Guarda un tipo de sesión (insert o update)
    /// </summary>
    public async Task<int> SaveSessionTypeAsync(SessionType sessionType)
    {
        var db = await GetConnectionAsync();
        if (sessionType.Id != 0)
        {
            await db.UpdateAsync(sessionType);
            return sessionType.Id;
        }
        else
        {
            await db.InsertAsync(sessionType);
            return sessionType.Id;
        }
    }

    /// <summary>
    /// Fusiona tipos de sesión: mantiene el de keepId y actualiza referencias.
    /// </summary>
    public async Task<int> MergeSessionTypesAsync(int keepSessionTypeId, List<int> mergeSessionTypeIds)
    {
        var db = await GetConnectionAsync();
        int merged = 0;

        var keepType = await db.Table<SessionType>().FirstOrDefaultAsync(s => s.Id == keepSessionTypeId);
        if (keepType == null) return 0;

        foreach (var duplicateId in mergeSessionTypeIds)
        {
            if (duplicateId == keepSessionTypeId) continue;

            var duplicateType = await db.Table<SessionType>().FirstOrDefaultAsync(s => s.Id == duplicateId);
            if (duplicateType == null) continue;

            // Actualizar sesiones que referencian el tipo duplicado (por nombre)
            await db.ExecuteAsync(
                "UPDATE sesion SET tipoSesion = ? WHERE tipoSesion = ?",
                keepType.TipoSesion, duplicateType.TipoSesion);

            // Eliminar el tipo duplicado
            await db.ExecuteAsync("DELETE FROM tipoSesion WHERE id = ?", duplicateId);
            merged++;
        }

        return merged;
    }

    #endregion

    #region Categories Merge

    /// <summary>
    /// Fusiona categorías: mantiene la de keepId y actualiza referencias en atletas.
    /// </summary>
    public async Task<int> MergeCategoriesAsync(int keepCategoryId, List<int> mergeCategoryIds)
    {
        var db = await GetConnectionAsync();
        int merged = 0;

        foreach (var duplicateId in mergeCategoryIds)
        {
            if (duplicateId == keepCategoryId) continue;

            // Actualizar atletas que referencian la categoría duplicada
            await db.ExecuteAsync(
                "UPDATE Atleta SET categoriaId = ? WHERE categoriaId = ?",
                keepCategoryId, duplicateId);

            // Actualizar inputs que referencian la categoría duplicada
            await db.ExecuteAsync(
                "UPDATE \"input\" SET CategoriaID = ? WHERE CategoriaID = ?",
                keepCategoryId, duplicateId);

            // Eliminar la categoría duplicada
            await db.ExecuteAsync("DELETE FROM categoria WHERE id = ?", duplicateId);
            merged++;
        }

        // Invalidar caché de categorías
        _categoryCache = null;

        return merged;
    }

    #endregion

    #region Tags Merge

    /// <summary>
    /// Fusiona tags: mantiene el de keepId y actualiza referencias en inputs.
    /// </summary>
    public async Task<int> MergeTagsAsync(int keepTagId, List<int> mergeTagIds)
    {
        var db = await GetConnectionAsync();
        int merged = 0;

        foreach (var duplicateId in mergeTagIds)
        {
            if (duplicateId == keepTagId) continue;

            // Actualizar inputs que referencian el tag duplicado
            await db.ExecuteAsync(
                "UPDATE \"input\" SET InputTypeID = ? WHERE InputTypeID = ?",
                keepTagId, duplicateId);

            // Eliminar el tag duplicado
            await db.ExecuteAsync("DELETE FROM tags WHERE id = ?", duplicateId);
            merged++;
        }

        return merged;
    }

    #endregion

    #region Delete Operations for Database Management

    /// <summary>
    /// Elimina un atleta de la base de datos
    /// </summary>
    public async Task DeleteAthleteAsync(Athlete athlete)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync(athlete);
        _athleteCache = null;
    }

    /// <summary>
    /// Elimina un lugar de la base de datos
    /// </summary>
    public async Task DeletePlaceAsync(Place place)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync(place);
    }

    /// <summary>
    /// Elimina una categoría de la base de datos
    /// </summary>
    public async Task DeleteCategoryAsync(Category category)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync(category);
        _categoryCache = null;
    }

    /// <summary>
    /// Elimina un tag de la base de datos (modelo Tag)
    /// </summary>
    public async Task DeleteTagAsync(Tag tag)
    {
        var db = await GetConnectionAsync();
        // Primero eliminar todas las referencias de ASIGNACIÓN (no eventos)
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE IsEvent = 0 AND InputTypeID = ?", tag.Id);
        // Luego eliminar el tag del catálogo general
        await db.ExecuteAsync("DELETE FROM \"tags\" WHERE id = ?", tag.Id);
        _tagCache = null;
    }

    /// <summary>
    /// Elimina un tipo de sesión de la base de datos
    /// </summary>
    public async Task DeleteSessionTypeAsync(SessionType sessionType)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync(sessionType);
    }

    /// <summary>
    /// Guarda o actualiza un EventTag en la base de datos
    /// </summary>
    public async Task<int> SaveEventTagAsync(EventTagDefinition eventTag)
    {
        var db = await GetConnectionAsync();
        if (eventTag.Id != 0)
        {
            await db.UpdateAsync(eventTag);
            return eventTag.Id;
        }
        else
        {
            await db.InsertAsync(eventTag);
            return eventTag.Id;
        }
    }

    /// <summary>
    /// Fusiona event tags: mantiene el de keepId y actualiza referencias en inputs.
    /// </summary>
    public async Task<int> MergeEventTagsAsync(int keepEventTagId, List<int> mergeEventTagIds)
    {
        var db = await GetConnectionAsync();
        int merged = 0;

        foreach (var duplicateId in mergeEventTagIds)
        {
            if (duplicateId == keepEventTagId) continue;

            // Verificar si es un tag de sistema - no se puede fusionar
            var eventTag = await db.Table<EventTagDefinition>()
                .Where(t => t.Id == duplicateId)
                .FirstOrDefaultAsync();
            
            if (eventTag?.IsSystem == true) continue;

            // Actualizar inputs que referencian el event tag duplicado
            await db.ExecuteAsync(
                "UPDATE \"input\" SET InputTypeID = ? WHERE IsEvent = 1 AND InputTypeID = ?",
                keepEventTagId, duplicateId);

            // Eliminar el event tag duplicado
            await db.ExecuteAsync("DELETE FROM \"event_tags\" WHERE id = ?", duplicateId);
            merged++;
        }

        _eventTagCache = null;
        return merged;
    }

    #endregion

    #region Session Lap Config

    /// <summary>
    /// Obtiene la configuración de parciales para una sesión.
    /// Si no existe, devuelve null.
    /// </summary>
    public async Task<SessionLapConfig?> GetSessionLapConfigAsync(int sessionId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<SessionLapConfig>()
            .Where(c => c.SessionId == sessionId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Guarda o actualiza la configuración de parciales para una sesión.
    /// </summary>
    public async Task SaveSessionLapConfigAsync(SessionLapConfig config)
    {
        var db = await GetConnectionAsync();
        config.LastModified = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        var existing = await db.Table<SessionLapConfig>()
            .Where(c => c.SessionId == config.SessionId)
            .FirstOrDefaultAsync();
        
        if (existing != null)
        {
            await db.UpdateAsync(config);
        }
        else
        {
            await db.InsertAsync(config);
        }
    }

    /// <summary>
    /// Guarda la configuración de parciales con los nombres proporcionados.
    /// </summary>
    public async Task SaveSessionLapConfigAsync(int sessionId, int lapCount, List<string> lapNames)
    {
        var config = new SessionLapConfig
        {
            SessionId = sessionId,
            LapCount = lapCount,
            LapNames = string.Join("|", lapNames)
        };
        await SaveSessionLapConfigAsync(config);
    }

    #endregion

    #region Lap Config History

    /// <summary>
    /// Obtiene las últimas N configuraciones de parciales utilizadas.
    /// </summary>
    public async Task<List<LapConfigHistory>> GetRecentLapConfigsAsync(int count = 3)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LapConfigHistory>()
            .OrderByDescending(c => c.LastUsed)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// Guarda una configuración en el historial.
    /// Si ya existe una configuración idéntica, actualiza su fecha de último uso.
    /// </summary>
    public async Task AddToLapConfigHistoryAsync(int lapCount, List<string> lapNames)
    {
        var db = await GetConnectionAsync();
        var lapNamesStr = string.Join("|", lapNames);
        var uniqueKey = $"{lapCount}|{lapNamesStr}";
        
        // Buscar si ya existe una configuración idéntica
        var all = await db.Table<LapConfigHistory>().ToListAsync();
        var existing = all.FirstOrDefault(c => c.UniqueKey == uniqueKey);
        
        if (existing != null)
        {
            // Actualizar fecha y contador de uso
            existing.LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            existing.UseCount++;
            await db.UpdateAsync(existing);
        }
        else
        {
            // Insertar nueva configuración
            var newConfig = new LapConfigHistory
            {
                LapCount = lapCount,
                LapNames = lapNamesStr,
                LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UseCount = 1
            };
            await db.InsertAsync(newConfig);
            
            // Mantener solo las últimas 10 configuraciones
            var allConfigs = await db.Table<LapConfigHistory>()
                .OrderByDescending(c => c.LastUsed)
                .ToListAsync();
            
            if (allConfigs.Count > 10)
            {
                var toDelete = allConfigs.Skip(10).ToList();
                foreach (var config in toDelete)
                {
                    await db.DeleteAsync(config);
                }
            }
        }
    }

    #endregion
}
