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
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "CrownApp.db");
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
        var sessions = await db.Table<Session>().ToListAsync();
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
            return _database;

        LogInfo("Conectando a base de datos...");
        _database = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        
        await InitializeDatabaseAsync();
        LogSuccess("Base de datos conectada");
        
        return _database;
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
        await _database.CreateTableAsync<WorkGroup>();
        await _database.CreateTableAsync<AthleteWorkGroup>();
        await _database.CreateTableAsync<UserProfile>();
        await _database.CreateTableAsync<VideoLesson>();

        // Migraciones ligeras: columnas nuevas en videoClip
        await EnsureColumnExistsAsync(_database, "videoClip", "localClipPath", "TEXT");
        await EnsureColumnExistsAsync(_database, "videoClip", "localThumbnailPath", "TEXT");

        // Migraciones ligeras: columnas nuevas en userProfile
        await EnsureColumnExistsAsync(_database, "userProfile", "referenceAthleteId", "INTEGER");

        // Migraciones ligeras: columna IsEvent en input para distinguir eventos de asignaciones
        await EnsureColumnExistsAsync(_database, "input", "IsEvent", "INTEGER DEFAULT 0");

        // Migración: separar tipos de evento en event_tags (si venían guardados como Tag)
        await MigrateEventTagsAsync(_database);
        
        LogSuccess("Tablas inicializadas correctamente");
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

    public async Task<int> DeleteSessionAsync(Session session)
    {
        try
        {
            var db = await GetConnectionAsync();
            // Eliminar también los videos asociados
            var deletedVideos = await db.Table<VideoClip>().DeleteAsync(v => v.SessionId == session.Id);
            LogInfo($"Eliminados {deletedVideos} vídeos de la sesión");
            var result = await db.DeleteAsync(session);
            LogSuccess($"Sesión eliminada: {session.DisplayName}");
            return result;
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

        return clips;
    }

    public async Task<VideoClip?> GetVideoClipByIdAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoClip>().FirstOrDefaultAsync(v => v.Id == videoId);
    }

    public async Task<List<VideoClip>> GetVideoClipsByAthleteAsync(int athleteId)
    {
        var db = await GetConnectionAsync();
        var clips = await db.Table<VideoClip>()
            .Where(v => v.AtletaId == athleteId)
            .OrderByDescending(v => v.CreationDate)
            .ToListAsync();

        // Hidratar rutas locales por sesión (para que el reproductor encuentre los archivos)
        var sessions = await db.Table<Session>().ToListAsync();
        var sessionById = sessions.ToDictionary(s => s.Id, s => s);
        foreach (var clip in clips)
        {
            sessionById.TryGetValue(clip.SessionId, out var session);
            HydrateLocalMediaPaths(clip, session?.PathSesion);
        }

        return clips;
    }

    /// <summary>
    /// Obtiene todos los video clips de todas las sesiones
    /// </summary>
    public async Task<List<VideoClip>> GetAllVideoClipsAsync()
    {
        var db = await GetConnectionAsync();
        var clips = await db.Table<VideoClip>()
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

    // ==================== TAGS ====================
    public async Task<List<Tag>> GetAllTagsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Tag>().ToListAsync();
    }

    // ==================== EVENT TAG DEFINITIONS ====================
    public async Task<List<EventTagDefinition>> GetAllEventTagsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<EventTagDefinition>().ToListAsync();
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

    public async Task DeleteEventTagAsync(int eventTagId)
    {
        var db = await GetConnectionAsync();

        // Borrar ocurrencias SOLO de eventos
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE IsEvent = 1 AND InputTypeID = ?", eventTagId);

        // Borrar del catálogo de eventos
        await db.ExecuteAsync("DELETE FROM \"event_tags\" WHERE id = ?", eventTagId);
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
    /// Obtiene los eventos de etiquetas para un video específico
    /// </summary>
    public async Task<List<TagEvent>> GetTagEventsForVideoAsync(int videoId)
    {
        var db = await GetConnectionAsync();
        
        // Eventos: inputs con IsEvent == 1
        var inputs = await db.Table<Input>()
            .Where(i => i.VideoId == videoId && i.IsEvent == 1)
            .ToListAsync();
        
        if (inputs.Count == 0)
            return new List<TagEvent>();

        // Obtener catálogo de tipos de evento (separado de tags)
        var allEventDefs = await db.Table<EventTagDefinition>().ToListAsync();
        var eventDict = allEventDefs.ToDictionary(t => t.Id, t => t.Nombre ?? "");
        
        // Crear lista de eventos ordenados por timestamp
        var events = inputs
            .Where(i => eventDict.ContainsKey(i.InputTypeId) && !string.IsNullOrEmpty(eventDict[i.InputTypeId]))
            .Select(i => new TagEvent
            {
                InputId = i.Id,
                TagId = i.InputTypeId,
                TagName = eventDict[i.InputTypeId],
                TimestampMs = i.TimeStamp
            })
            .OrderBy(e => e.TimestampMs)
            .ToList();
        
        return events;
    }

    /// <summary>
    /// Añade un evento de etiqueta en un timestamp específico del video
    /// </summary>
    public async Task<int> AddTagEventAsync(int videoId, int tagId, long timestampMs, int sessionId, int athleteId)
    {
        var db = await GetConnectionAsync();
        
        var input = new Input
        {
            VideoId = videoId,
            // Para eventos (IsEvent=1), InputTypeId referencia event_tags
            InputTypeId = tagId,
            TimeStamp = timestampMs,
            SessionId = sessionId,
            AthleteId = athleteId,
            InputDateTime = DateTimeOffset.Now.ToUnixTimeSeconds(),
            IsEvent = 1  // Marcar como evento
        };
        
        await db.InsertAsync(input);
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
        return await db.Table<Session>().CountAsync();
    }

    public async Task<int> GetTotalVideosCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<VideoClip>().CountAsync();
    }

    // ==================== BORRADO ====================
    public async Task DeleteSessionCascadeAsync(int sessionId, bool deleteSessionFiles)
    {
        var db = await GetConnectionAsync();

        var session = await db.Table<Session>().FirstOrDefaultAsync(s => s.Id == sessionId);

        // Borrar filas dependientes.
        // Nota: usamos SQL explícito para asegurar que se usa el nombre real de tabla/columna,
        // especialmente en tablas con nombres conflictivos como "input".
        await db.ExecuteAsync("DELETE FROM videoClip WHERE SessionID = ?;", sessionId);
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE SessionID = ?;", sessionId);
        await db.ExecuteAsync("DELETE FROM valoracion WHERE SessionID = ?;", sessionId);

        // Borrar sesión (la tabla real es "sesion" y la PK es "id")
        await db.ExecuteAsync("DELETE FROM sesion WHERE id = ?;", sessionId);

        // Borrar archivos de la sesión (carpeta Media/<sesión>)
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
                        Directory.Delete(fullPath, recursive: true);
                }
            }
            catch
            {
                // Si no se puede borrar, no bloqueamos el borrado en DB.
            }
        }
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
}
