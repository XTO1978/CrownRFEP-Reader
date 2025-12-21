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
        await _database.CreateTableAsync<UserProfile>();

        // Migraciones ligeras: columnas nuevas en videoClip
        await EnsureColumnExistsAsync(_database, "videoClip", "localClipPath", "TEXT");
        await EnsureColumnExistsAsync(_database, "videoClip", "localThumbnailPath", "TEXT");

        // Migraciones ligeras: columnas nuevas en userProfile
        await EnsureColumnExistsAsync(_database, "userProfile", "referenceAthleteId", "INTEGER");

        // Migraciones ligeras: columna IsEvent en input para distinguir eventos de asignaciones
        await EnsureColumnExistsAsync(_database, "input", "IsEvent", "INTEGER DEFAULT 0");
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
        var clipIds = clipList.Select(c => c.Id).ToList();
        var allInputsForClips = await GetInputsForVideosAsync(db, clipIds);

        HydrateTagsForClipsInternal(clipList, allTags, allInputsForClips);
    }

    private static void HydrateTagsForClipsInternal(
        IReadOnlyList<VideoClip> clips,
        IReadOnlyList<Tag> allTags,
        IReadOnlyList<Input> allInputsForClips)
    {
        var tagDict = allTags.ToDictionary(t => t.Id, t => t);
        
        foreach (var clip in clips)
        {
            // Tags asignados (IsEvent == 0): asignados desde la app mediante el panel de etiquetas
            var assignedInputs = allInputsForClips
                .Where(i => i.VideoId == clip.Id && i.IsEvent == 0)
                .ToList();

            clip.Tags = assignedInputs
                .Where(i => tagDict.ContainsKey(i.InputTypeId))
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
            var eventInputs = allInputsForClips
                .Where(i => i.VideoId == clip.Id && i.IsEvent == 1)
                .ToList();

            clip.EventTags = eventInputs
                .Where(i => tagDict.ContainsKey(i.InputTypeId))
                .GroupBy(i => i.InputTypeId) // Agrupar por tipo para contar ocurrencias
                .Select(g =>
                {
                    var input = g.First();
                    var baseTag = tagDict[input.InputTypeId];
                    var count = g.Count(); // Número de ocurrencias de este evento
                    
                    // Usar InputValue si tiene contenido, sino el nombre del tipo de tag
                    var displayName = !string.IsNullOrWhiteSpace(input.InputValue)
                        ? input.InputValue
                        : baseTag.NombreTag;
                    
                    return new Tag
                    {
                        Id = baseTag.Id,
                        NombreTag = displayName,
                        IsSelected = baseTag.IsSelected,
                        IsEventTag = true,
                        EventCount = count // Incluir el conteo de ocurrencias
                    };
                })
                .Where(t => !string.IsNullOrEmpty(t.NombreTag) && !InternalTagNames.Contains(t.NombreTag))
                .ToList();

            // Nunca dejar null para evitar problemas de binding/medida
            clip.Tags ??= new List<Tag>();
            clip.EventTags ??= new List<Tag>();
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

        // Asegurar rutas locales para reproducción/miniaturas (compatibilidad con imports antiguos)
        var session = await db.Table<Session>().FirstOrDefaultAsync(s => s.Id == sessionId);
        foreach (var clip in clips)
        {
            HydrateLocalMediaPaths(clip, session?.PathSesion);
        }

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

        // Cargar tags para cada video.
        // Preferencia: tags asignados (TimeStamp == 0). Si no existen, usar eventos (TimeStamp > 0) como fallback.
        // Importante: NO filtrar por SessionID aquí; algunos flujos históricos no lo rellenaban en "input".
        var allTags = await db.Table<Tag>().ToListAsync();
        var clipIds = clips.Select(c => c.Id).ToList();
        var allInputsForClips = await GetInputsForVideosAsync(db, clipIds);
        HydrateTagsForClipsInternal(clips, allTags, allInputsForClips);

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

        // Hidratar rutas locales por sesión
        var sessions = await db.Table<Session>().ToListAsync();
        var sessionById = sessions.ToDictionary(s => s.Id, s => s);
        foreach (var clip in clips)
        {
            sessionById.TryGetValue(clip.SessionId, out var session);
            HydrateLocalMediaPaths(clip, session?.PathSesion);
        }

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

        // Cargar tags también en Galería General (Dashboard usa este método).
        var allTags = await db.Table<Tag>().ToListAsync();
        var clipIds = clips.Select(c => c.Id).ToList();
        var allInputsForClips = await GetInputsForVideosAsync(db, clipIds);
        HydrateTagsForClipsInternal(clips, allTags, allInputsForClips);

        return clips;
    }

    public async Task<int> SaveVideoClipAsync(VideoClip clip)
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
            return existing.Id;
        }
        else
        {
            await db.InsertAsync(clip);
            return clip.Id;
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
        
        // Obtener todos los tags
        var allTags = await db.Table<Tag>().ToListAsync();
        var tagDict = allTags.ToDictionary(t => t.Id, t => t.NombreTag ?? "");
        
        // Crear lista de eventos ordenados por timestamp
        var events = inputs
            .Where(i => tagDict.ContainsKey(i.InputTypeId) && !string.IsNullOrEmpty(tagDict[i.InputTypeId]))
            .Select(i => new TagEvent
            {
                InputId = i.Id,
                TagId = i.InputTypeId,
                TagName = tagDict[i.InputTypeId],
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
        // Primero eliminar todas las referencias en input
        await db.ExecuteAsync("DELETE FROM \"input\" WHERE InputTypeID = ?", tagId);
        // Luego eliminar el tag
        await db.ExecuteAsync("DELETE FROM \"inputtype\" WHERE id = ?", tagId);
    }
}
