using System.IO.Compression;
using System.Text.Json;
using CrownRFEP_Reader.Models;

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#endif

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio para importar archivos .crown
/// </summary>
public class CrownFileService
{
    private readonly DatabaseService _databaseService;
    private readonly string _mediaStoragePath;

    public CrownFileService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        _mediaStoragePath = Path.Combine(FileSystem.AppDataDirectory, "Media");
        
        // Asegurar que el directorio de medios existe
        if (!Directory.Exists(_mediaStoragePath))
        {
            Directory.CreateDirectory(_mediaStoragePath);
        }
    }

    /// <summary>
    /// Exporta una sesión a un archivo .crown
    /// </summary>
    public async Task<ExportResult> ExportSessionAsync(int sessionId, IProgress<ImportProgress>? progress = null)
    {
        var result = new ExportResult();

        return await Task.Run(async () =>
        {
            try
            {
                AppLog.Info("CrownFileService", $"[Export] Iniciando exportación de sesión ID={sessionId}");
                ReportProgress(progress, "Cargando datos de sesión...", 5);

                // Obtener la sesión
                var session = await _databaseService.GetSessionByIdAsync(sessionId);
                if (session == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Sesión no encontrada";
                    return result;
                }

                AppLog.Info("CrownFileService", $"[Export] Sesión encontrada: '{session.NombreSesion}', Fecha={session.Fecha}");
                result.SessionName = session.NombreSesion;

                ReportProgress(progress, "Obteniendo videos...", 10);

                // Obtener videos de la sesión
                var videos = await _databaseService.GetVideoClipsBySessionAsync(sessionId);
                AppLog.Info("CrownFileService", $"[Export] Videos encontrados: {videos.Count}");
                foreach (var v in videos)
                {
                    AppLog.Info("CrownFileService", $"[Export] Video ID={v.Id}, SessionId={v.SessionId}, CreationDate={v.CreationDate}, LocalClipPath={v.LocalClipPath}");
                }

                ReportProgress(progress, "Obteniendo atletas...", 15);

                // Obtener atletas únicos de los videos
                var athleteIds = videos.Select(v => v.AtletaId).Distinct().ToList();
                var athletes = new List<Athlete>();
                foreach (var athId in athleteIds)
                {
                    var athlete = await _databaseService.GetAthleteByIdAsync(athId);
                    if (athlete != null)
                        athletes.Add(athlete);
                }

                ReportProgress(progress, "Obteniendo categorías...", 20);

                // Obtener categorías
                var categoryIds = athletes.Select(a => a.CategoriaId).Distinct().ToList();
                var categories = new List<Category>();
                foreach (var catId in categoryIds)
                {
                    var cat = await _databaseService.GetCategoryByIdAsync(catId);
                    if (cat != null)
                        categories.Add(cat);
                }

                ReportProgress(progress, "Obteniendo eventos...", 25);

                // Obtener Inputs de los videos para exportar como eventos
                var allInputs = new List<(Input input, int videoId)>();
                foreach (var video in videos)
                {
                    var videoInputs = await _databaseService.GetInputsForVideoAsync(video.Id);
                    foreach (var inp in videoInputs)
                    {
                        allInputs.Add((inp, video.Id));
                    }
                }

                ReportProgress(progress, "Preparando datos para exportación...", 30);

                // Crear el objeto CrownFileData
                var crownData = new CrownFileData
                {
                    ExportMetadata = new ExportMetadata
                    {
                        ExportDate = DateTime.Now,
                        AppVersion = "1.0.0",
                        ExportVersion = "1.0",
                        SessionId = sessionId,
                        SessionName = session.NombreSesion,
                        ExportedBy = "CrownRFEP Reader",
                        DeviceInfo = DeviceInfo.Current.Model
                    },
                    Session = new SessionJson
                    {
                        Id = session.Id,
                        Fecha = DateTimeOffset.FromUnixTimeSeconds(session.Fecha).DateTime,
                        Lugar = session.Lugar,
                        TipoSesion = session.TipoSesion,
                        NombreSesion = session.NombreSesion,
                        PathSesion = session.PathSesion,
                        Participantes = session.Participantes,
                        Coach = session.Coach,
                        IsMerged = session.IsMerged == 1,
                        VideoCount = videos.Count
                    },
                    VideoClips = videos.Select(v => new VideoClipJson
                    {
                        Id = v.Id,
                        SessionId = v.SessionId,
                        AtletaId = v.AtletaId,
                        Section = v.Section,
                        CreationDate = v.CreationDate,
                        // Usar LocalClipPath si ClipPath está vacío
                        ClipPath = Path.GetFileName(!string.IsNullOrEmpty(v.ClipPath) ? v.ClipPath : v.LocalClipPath ?? ""),
                        ThumbnailPath = Path.GetFileName(!string.IsNullOrEmpty(v.ThumbnailPath) ? v.ThumbnailPath : v.LocalThumbnailPath ?? ""),
                        ComparisonName = v.ComparisonName,
                        ClipDuration = v.ClipDuration,
                        ClipSize = v.ClipSize,
                        IsComparisonVideo = v.IsComparisonVideo,
                        BadgeText = v.BadgeText,
                        BadgeBackgroundColor = v.BadgeBackgroundColor
                    }).ToList(),
                    Athletes = athletes.Select(a => new AthleteJson
                    {
                        Id = a.Id,
                        Nombre = a.Nombre,
                        Apellido = a.Apellido,
                        Categoria = a.Category,
                        CategoriaId = a.CategoriaId,
                        Favorite = a.Favorite,
                        IsFavorite = a.Favorite == 1,
                        CategoriaNombre = a.CategoriaNombre,
                        NombreCompleto = a.NombreCompleto,
                        IsSystemDefault = a.IsSystemDefault == 1
                    }).ToList(),
                    Categories = categories.Select(c => new CategoryJson
                    {
                        Id = c.Id,
                        NombreCategoria = c.NombreCategoria,
                        IsSystemDefault = c.IsSystemDefault == 1
                    }).ToList(),
                    Inputs = await ConvertInputsToInputJsonAsync(allInputs, videos)
                };

                ReportProgress(progress, "Creando archivo .crown...", 40);

                // Crear nombre de archivo
                var sanitizedName = SanitizeFolderName(session.NombreSesion ?? "Session");
                var fileName = $"CrownSession_{sanitizedName}_{DateTime.Now:yyyyMMdd_HHmmss}.crown";
                var exportDir = Path.Combine(FileSystem.CacheDirectory, "Exports");
                
                if (!Directory.Exists(exportDir))
                    Directory.CreateDirectory(exportDir);
                
                var exportPath = Path.Combine(exportDir, fileName);

                // Eliminar archivo existente si existe
                if (File.Exists(exportPath))
                    File.Delete(exportPath);

                // Crear el archivo ZIP
                using (var archive = ZipFile.Open(exportPath, ZipArchiveMode.Create))
                {
                    // Añadir session_data.json
                    var jsonEntry = archive.CreateEntry("session_data.json");
                    using (var entryStream = jsonEntry.Open())
                    using (var writer = new StreamWriter(entryStream))
                    {
                        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                        var json = JsonSerializer.Serialize(crownData, jsonOptions);
                        await writer.WriteAsync(json);
                    }

                    ReportProgress(progress, "Añadiendo videos...", 50);

                    // Añadir archivos de video
                    var totalVideos = videos.Count;
                    var processedVideos = 0;
                    var videosActuallyExported = 0;

                    foreach (var video in videos)
                    {
                        // Video - usar LocalClipPath (ruta absoluta) primero, luego ClipPath
                        var videoPath = video.LocalClipPath;
                        
                        // Si LocalClipPath está vacío, intentar con ClipPath
                        if (string.IsNullOrEmpty(videoPath))
                        {
                            videoPath = video.ClipPath;
                            // Si es una ruta relativa, construir la ruta absoluta basada en PathSesion
                            if (!string.IsNullOrEmpty(videoPath) && !Path.IsPathRooted(videoPath) && !string.IsNullOrEmpty(session.PathSesion))
                            {
                                videoPath = Path.Combine(session.PathSesion, "videos", videoPath);
                            }
                        }
                        
                        AppLog.Info("CrownFileService", $"[Export] Video: LocalClipPath={video.LocalClipPath}, ClipPath={video.ClipPath}, Resolved={videoPath}");
                        
                        if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                        {
                            var videoFileName = Path.GetFileName(videoPath);
                            archive.CreateEntryFromFile(videoPath, $"videos/{videoFileName}");
                            videosActuallyExported++;
                            AppLog.Info("CrownFileService", $"[Export] Video añadido: {videoPath}");
                        }
                        else if (!string.IsNullOrEmpty(videoPath))
                        {
                            AppLog.Warn("CrownFileService", $"[Export] Video no encontrado: {videoPath}");
                        }

                        // Thumbnail - usar LocalThumbnailPath primero, luego ThumbnailPath
                        var thumbPath = video.LocalThumbnailPath;
                        if (string.IsNullOrEmpty(thumbPath))
                        {
                            thumbPath = video.ThumbnailPath;
                            if (!string.IsNullOrEmpty(thumbPath) && !Path.IsPathRooted(thumbPath) && !string.IsNullOrEmpty(session.PathSesion))
                            {
                                thumbPath = Path.Combine(session.PathSesion, "thumbnails", thumbPath);
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
                        {
                            var thumbFileName = Path.GetFileName(thumbPath);
                            archive.CreateEntryFromFile(thumbPath, $"thumbnails/{thumbFileName}");
                        }

                        processedVideos++;
                        var videoProgress = 50 + (int)(40.0 * processedVideos / totalVideos);
                        ReportProgress(progress, $"Añadiendo video {processedVideos}/{totalVideos}...", videoProgress);
                    }

                    result.VideosExported = videosActuallyExported;
                    AppLog.Info("CrownFileService", $"[Export] Videos exportados: {videosActuallyExported}/{totalVideos}");
                }

                ReportProgress(progress, "Exportación completada", 100);

                result.Success = true;
                result.FilePath = exportPath;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Error al exportar: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error exportando sesión: {ex}");
                return result;
            }
        });
    }

    private async Task<List<InputJson>> ConvertInputsToInputJsonAsync(List<(Input input, int videoId)> allInputs, List<VideoClip> videos)
    {
        var inputs = new List<InputJson>();
        
        foreach (var (input, videoId) in allInputs)
        {
            var eventTag = await _databaseService.GetEventTagByIdAsync(input.InputTypeId);
            var video = videos.FirstOrDefault(v => v.Id == videoId);
            
            if (video != null)
            {
                inputs.Add(new InputJson
                {
                    Id = input.Id,
                    SessionId = video.SessionId,
                    VideoId = videoId,
                    AthleteId = video.AtletaId,
                    InputTypeId = input.InputTypeId,
                    InputDateTime = input.InputDateTime,
                    InputValue = eventTag?.PenaltySeconds > 0 ? $"+{eventTag.PenaltySeconds}" : (eventTag?.Nombre ?? input.InputValue),
                    TimeStamp = input.TimeStamp,
                    InputTypeObj = eventTag != null ? new InputTypeJson
                    {
                        Id = eventTag.Id,
                        TipoInput = eventTag.Nombre
                    } : null
                });
            }
        }
        
        return inputs;
    }

    /// <summary>
    /// Importa un archivo .crown y almacena sus datos en la base de datos local
    /// </summary>
    public async Task<ImportResult> ImportCrownFileAsync(string filePath, IProgress<ImportProgress>? progress = null)
    {
        var result = new ImportResult();
        
        // Ejecutar la importación en un hilo de fondo para no bloquear la UI
        return await Task.Run(async () =>
        {
            try
            {
                ReportProgress(progress, "Abriendo archivo...", 0);

                if (!File.Exists(filePath))
                {
                    result.Success = false;
                    result.ErrorMessage = "El archivo no existe";
                    return result;
                }

                // El archivo .crown es un ZIP
                using var archive = ZipFile.OpenRead(filePath);
                
                // Buscar el session_data.json
                var sessionDataEntry = archive.GetEntry("session_data.json");
                if (sessionDataEntry == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "El archivo .crown no contiene datos de sesión válidos";
                    return result;
                }

                ReportProgress(progress, "Leyendo datos de sesión...", 10);

                // Leer y deserializar el JSON
                CrownFileData? crownData;
                using (var stream = sessionDataEntry.Open())
                using (var reader = new StreamReader(stream))
                {
                    var json = await reader.ReadToEndAsync();
                    crownData = JsonSerializer.Deserialize<CrownFileData>(json);
                }

                if (crownData?.Session == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "No se pudieron leer los datos de la sesión";
                    return result;
                }

                ReportProgress(progress, "Guardando categorías...", 20);

                // Guardar categorías
                if (crownData.Categories != null)
                {
                    foreach (var cat in crownData.Categories)
                    {
                        var category = new Category
                        {
                            Id = cat.Id,
                            NombreCategoria = cat.NombreCategoria,
                            IsSystemDefault = cat.IsSystemDefault ? 1 : 0
                        };
                        await _databaseService.SaveCategoryAsync(category);
                    }
                }

                ReportProgress(progress, "Guardando atletas...", 30);

            // Mapeo: JSON AthleteId → Local AthleteId
            // Cada archivo .crown puede venir de un usuario distinto con IDs propios,
            // así que resolvemos por nombre/apellido y asignamos un ID local.
            var athleteIdMap = new Dictionary<int, int>(); // jsonId → localId

            if (crownData.Athletes != null)
            {
                foreach (var ath in crownData.Athletes)
                {
                    // Buscar si ya existe un atleta con el mismo nombre y apellido
                    var existing = await _databaseService.FindAthleteByNameAsync(ath.Nombre, ath.Apellido);

                    int localId;
                    if (existing != null)
                    {
                        // Atleta ya existe: usar su ID local
                        localId = existing.Id;
                    }
                    else
                    {
                        // Atleta nuevo: insertar y obtener ID autogenerado
                        var newAthlete = new Athlete
                        {
                            Nombre = ath.Nombre,
                            Apellido = ath.Apellido,
                            Category = ath.Categoria,
                            CategoriaId = ath.CategoriaId,
                            Favorite = ath.Favorite,
                            IsSystemDefault = ath.IsSystemDefault ? 1 : 0,
                            CategoriaNombre = ath.CategoriaNombre
                        };
                        localId = await _databaseService.InsertAthleteAsync(newAthlete);
                        result.AthletesImported++;
                    }

                    athleteIdMap[ath.Id] = localId;
                }
            }

            ReportProgress(progress, "Guardando etiquetas...", 35);

            // Mapeo: JSON InputTypeId → Local EventTagId (para eventos)
            var inputTypeIdToEventTagIdMap = new Dictionary<int, int>(); // jsonInputTypeId → localEventTagId
            
            // Obtener los tags de sistema de penalización
            var systemPenaltyTags = await _databaseService.GetSystemEventTagsAsync();
            var penalty2Tag = systemPenaltyTags.FirstOrDefault(t => t.PenaltySeconds == 2);
            var penalty50Tag = systemPenaltyTags.FirstOrDefault(t => t.PenaltySeconds == 50);
            
            // Extraer tipos de input únicos de los Inputs y guardarlos como EventTags
            if (crownData.Inputs != null)
            {
                var uniqueInputTypes = crownData.Inputs
                    .Where(i => i.InputTypeObj != null && !string.IsNullOrWhiteSpace(i.InputTypeObj.TipoInput))
                    .Select(i => i.InputTypeObj!)
                    .DistinctBy(it => it.Id)
                    .ToList();
                
                foreach (var inputType in uniqueInputTypes)
                {
                    var tipoInput = inputType.TipoInput?.Trim() ?? "";
                    
                    // Detectar si es una penalización por nombre o ID
                    bool isPenalty2 = tipoInput == "2" || tipoInput == "+2" || inputType.Id == 2;
                    bool isPenalty50 = tipoInput == "50" || tipoInput == "+50" || inputType.Id == 50;
                    
                    int localEventTagId;
                    
                    if (isPenalty2 && penalty2Tag != null)
                    {
                        // Es penalización de 2s: usar el tag de sistema
                        localEventTagId = penalty2Tag.Id;
                    }
                    else if (isPenalty50 && penalty50Tag != null)
                    {
                        // Es penalización de 50s: usar el tag de sistema
                        localEventTagId = penalty50Tag.Id;
                    }
                    else
                    {
                        // No es penalización: buscar o crear EventTag
                        var existingEventTag = await _databaseService.FindEventTagByNameAsync(tipoInput);
                        
                        if (existingEventTag != null)
                        {
                            // EventTag ya existe: usar su ID local
                            localEventTagId = existingEventTag.Id;
                        }
                        else
                        {
                            // EventTag nuevo: insertar y obtener ID autogenerado
                            var newEventTag = new EventTagDefinition
                            {
                                Nombre = tipoInput,
                                IsSystem = false,
                                PenaltySeconds = 0
                            };
                            localEventTagId = await _databaseService.InsertEventTagAsync(newEventTag);
                            result.TagsImported++;
                        }
                    }
                    
                    inputTypeIdToEventTagIdMap[inputType.Id] = localEventTagId;
                }
            }

            ReportProgress(progress, "Guardando sesión...", 40);

            // Crear carpeta para esta sesión
            var sessionFolderName = SanitizeFolderName($"{crownData.Session.NombreSesion}_{crownData.Session.Lugar}_{crownData.Session.Fecha:yyyyMMdd}");
            var sessionMediaPath = Path.Combine(_mediaStoragePath, sessionFolderName);
            
            if (!Directory.Exists(sessionMediaPath))
            {
                Directory.CreateDirectory(sessionMediaPath);
            }

            var videosPath = Path.Combine(sessionMediaPath, "videos");
            var thumbnailsPath = Path.Combine(sessionMediaPath, "thumbnails");
            
            Directory.CreateDirectory(videosPath);
            Directory.CreateDirectory(thumbnailsPath);

            // Guardar la sesión
            var session = new Session
            {
                Fecha = new DateTimeOffset(crownData.Session.Fecha).ToUnixTimeSeconds(),
                Lugar = crownData.Session.Lugar,
                TipoSesion = crownData.Session.TipoSesion,
                NombreSesion = crownData.Session.NombreSesion,
                PathSesion = sessionMediaPath,
                Participantes = crownData.Session.Participantes,
                Coach = crownData.Session.Coach,
                IsMerged = crownData.Session.IsMerged ? 1 : 0
            };

            var sessionId = await _databaseService.SaveSessionAsync(session);
            result.SessionId = sessionId;

            ReportProgress(progress, "Extrayendo videos...", 50);

            // Mapeo: JSON VideoId → Local VideoId (similar al mapeo de atletas)
            var videoIdMap = new Dictionary<int, int>(); // jsonVideoId → localVideoId

            // Extraer y guardar videos
            if (crownData.VideoClips != null)
            {
                var totalClips = crownData.VideoClips.Count;
                var processedClips = 0;

                foreach (var clipJson in crownData.VideoClips)
                {
                    var clipFileName = GetNormalizedFileName(clipJson.ClipPath, $"CROWN{clipJson.Id}.mp4");
                    var thumbFileName = GetNormalizedFileName(clipJson.ThumbnailPath, $"CROWN{clipJson.Id}_thumb.jpg");

                    var localClipPath = Path.Combine(videosPath, clipFileName);
                    var localThumbPath = Path.Combine(thumbnailsPath, thumbFileName);
                    
                    AppLog.Info("CrownFileService", $"[Import] Buscando video: clipFileName={clipFileName}, ClipPath JSON={clipJson.ClipPath}");

                    // Extraer video del ZIP
                    var videoEntry = FindEntry(archive, "videos", clipFileName);
                    var extractedClipPath = (string?)null;
                    if (videoEntry != null)
                    {
                        await ExtractEntryToFileAsync(videoEntry, localClipPath);
                        extractedClipPath = localClipPath;
                        AppLog.Info("CrownFileService", $"[Import] Video extraído a: {localClipPath}");
                    }
                    else
                    {
                        AppLog.Warn("CrownFileService", $"[Import] Video NO encontrado en ZIP: videos/{clipFileName}");
                        // Listar las entradas disponibles en el ZIP
                        var availableVideos = archive.Entries.Where(e => e.FullName.StartsWith("videos/")).Select(e => e.FullName).ToList();
                        AppLog.Info("CrownFileService", $"[Import] Entradas disponibles en ZIP/videos: {string.Join(", ", availableVideos)}");
                    }

                    // Extraer thumbnail del ZIP
                    var thumbEntry = FindEntry(archive, "thumbnails", thumbFileName);
                    var extractedThumbPath = (string?)null;
                    if (thumbEntry != null)
                    {
                        await ExtractEntryToFileAsync(thumbEntry, localThumbPath);
                        extractedThumbPath = localThumbPath;
                    }

                    // Resolver AtletaId usando el mapeo local
                    var localAtletaId = athleteIdMap.TryGetValue(clipJson.AtletaId, out var mappedId) ? mappedId : 0;

                    // Guardar en base de datos (no forzar Id; dejar que SQLite asigne)
                    var clip = new VideoClip
                    {
                        // No asignamos Id: SQLite lo autogenera
                        SessionId = sessionId,
                        AtletaId = localAtletaId,
                        Section = clipJson.Section,
                        CreationDate = clipJson.CreationDate,
                        ClipPath = clipJson.ClipPath,
                        ThumbnailPath = clipJson.ThumbnailPath,
                        ComparisonName = clipJson.ComparisonName,
                        ClipDuration = clipJson.ClipDuration,
                        ClipSize = clipJson.ClipSize,
                        LocalClipPath = extractedClipPath,
                        LocalThumbnailPath = extractedThumbPath,
                        // IsComparisonVideo se calcula automáticamente a partir de ComparisonName
                        BadgeText = clipJson.BadgeText,
                        BadgeBackgroundColor = clipJson.BadgeBackgroundColor
                    };

                    var localClipId = await _databaseService.InsertVideoClipAsync(clip);
                    videoIdMap[clipJson.Id] = localClipId;
                    result.VideosImported++;

                    processedClips++;
                    var percentage = 50 + (int)((processedClips / (double)totalClips) * 45);
                    ReportProgress(progress, $"Extrayendo video {processedClips}/{totalClips}...", percentage);
                }
            }

            ReportProgress(progress, "Guardando eventos de etiquetas...", 96);

            // Guardar inputs como eventos (todos los inputs importados de .crown son eventos)
            if (crownData.Inputs != null)
            {
                foreach (var inputJson in crownData.Inputs)
                {
                    var localInputAthleteId = athleteIdMap.TryGetValue(inputJson.AthleteId, out var mappedInputId) ? mappedInputId : 0;
                    
                    // Mapear el InputTypeId del JSON al EventTagId local
                    var localEventTagId = inputTypeIdToEventTagIdMap.TryGetValue(inputJson.InputTypeId, out var mappedEventTagId) ? mappedEventTagId : inputJson.InputTypeId;
                    
                    // Mapear el VideoId del JSON al VideoId local
                    var localVideoId = videoIdMap.TryGetValue(inputJson.VideoId, out var mappedVideoId) ? mappedVideoId : 0;
                    
                    // Saltar si no encontramos el video correspondiente
                    if (localVideoId == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Import] Skipping input: no local video found for JSON VideoId={inputJson.VideoId}");
                        continue;
                    }

                    var input = new Input
                    {
                        SessionId = sessionId,
                        VideoId = localVideoId,  // Usar el ID de video local mapeado
                        AthleteId = localInputAthleteId,
                        CategoriaId = inputJson.CategoriaId,
                        InputTypeId = localEventTagId,  // Usar el ID de EventTag local mapeado
                        InputDateTime = inputJson.InputDateTime,
                        InputValue = inputJson.InputValue,
                        TimeStamp = inputJson.TimeStamp,  // Mantener el timestamp original
                        IsEvent = 1  // IMPORTANTE: Marcar como evento (importado de .crown)
                    };
                    await _databaseService.SaveInputAsync(input);
                    result.TagEventsImported++;
                }
            }

            // Guardar valoraciones si existen
            if (crownData.Valoraciones != null)
            {
                foreach (var valJson in crownData.Valoraciones)
                {
                    var localValAthleteId = athleteIdMap.TryGetValue(valJson.AthleteId, out var mappedValId) ? mappedValId : 0;

                    var valoracion = new Valoracion
                    {
                        SessionId = sessionId,
                        AthleteId = localValAthleteId,
                        InputTypeId = valJson.InputTypeId,
                        InputDateTime = valJson.InputDateTime,
                        InputValue = valJson.InputValue,
                        TimeStamp = valJson.TimeStamp
                    };
                    await _databaseService.SaveValoracionAsync(valoracion);
                }
            }

            ReportProgress(progress, "Importación completada", 100);

            // Invalidar caché para que las nuevas consultas reflejen los datos importados
            _databaseService.InvalidateCache();

            result.Success = true;
            result.SessionName = crownData.Session.NombreSesion;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Error al importar: {ex.Message}";
        }

        return result;
        });
    }

    private void ReportProgress(IProgress<ImportProgress>? progress, string message, int percentage)
    {
        if (progress == null) return;
        
        // Usar MainThread para reportar progreso de forma segura a la UI
        MainThread.BeginInvokeOnMainThread(() =>
        {
            progress.Report(new ImportProgress { Message = message, Percentage = percentage });
        });
    }

    private async Task ExtractEntryToFileAsync(ZipArchiveEntry entry, string destinationPath)
    {
        // Usar buffer más grande para mejor rendimiento
        const int bufferSize = 81920; // 80KB buffer
        using var entryStream = entry.Open();
        using var fileStream = new FileStream(
            destinationPath, 
            FileMode.Create, 
            FileAccess.Write, 
            FileShare.None, 
            bufferSize, 
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await entryStream.CopyToAsync(fileStream, bufferSize);
    }

    private static string GetNormalizedFileName(string? pathOrName, string fallbackFileName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
            return fallbackFileName;

        // En exports de Windows es común que vengan rutas con backslashes.
        var normalized = pathOrName.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(fileName) ? fallbackFileName : fileName;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string folder, string fileName)
    {
        // Intento rápido: ruta exacta
        var direct = archive.GetEntry($"{folder}/{fileName}");
        if (direct != null) return direct;

        // Búsqueda robusta: ignorar mayúsculas/minúsculas y posibles subcarpetas
        return archive.Entries.FirstOrDefault(e =>
        {
            if (string.IsNullOrWhiteSpace(e.FullName)) return false;
            var full = e.FullName.Replace('\\', '/');
            if (!full.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) return false;
            var entryFileName = Path.GetFileName(full);
            return string.Equals(entryFileName, fileName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private string SanitizeFolderName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        return sanitized.Replace(" ", "_");
    }

    /// <summary>
    /// Permite al usuario seleccionar un archivo .crown para importar
    /// </summary>
    public async Task<FileResult?> PickCrownFileAsync()
    {
        try
        {
            // En MacCatalyst, establecer FileTypes puede provocar cuelgues del picker.
            // Preferimos abrir el selector sin filtros y validar después.
            if (DeviceInfo.Platform == DevicePlatform.MacCatalyst)
            {
                // En algunas versiones de MAUI, PickAsync(PickOptions) puede colgarse en MacCatalyst.
                // Usar PickAsync() sin opciones es lo más robusto.
                return await FilePicker.Default.PickAsync();
            }

            // En el resto de plataformas, mantenemos filtros razonables.
            var customFileType = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.data", "public.item", "public.content", "public.archive", "public.movie", "public.video" } },
                    { DevicePlatform.Android, new[] { "application/octet-stream", "application/zip", "video/*", "*/*" } },
                    { DevicePlatform.WinUI, new[] { ".crown", ".zip", ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".mpg", ".mpeg", ".m4v" } },
                });

            var filteredOptions = new PickOptions
            {
                PickerTitle = "Seleccionar archivo .crown",
                FileTypes = customFileType
            };

            return await FilePicker.Default.PickAsync(filteredOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error en FilePicker: {ex}");
            return null;
        }
    }

    public async Task<string?> PickCrownFilePathAsync()
    {
#if MACCATALYST
        // FilePicker en MacCatalyst puede colgarse; usamos un picker nativo.
        // Si el usuario cancela, retornamos null directamente sin mostrar otro picker.
        var macPath = await MacCrownFilePicker.PickToCacheAsync();
        // Retornar siempre el resultado del picker nativo (null si canceló, path si seleccionó)
        return string.IsNullOrWhiteSpace(macPath) ? null : macPath;
#endif

        var file = await PickCrownFileAsync();
        if (file == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(file.FullPath) && File.Exists(file.FullPath))
        {
            return file.FullPath;
        }

        // Fallback: en iOS/MacCatalyst a veces FullPath no es accesible; copiamos a Cache.
        try
        {
            await using var input = await file.OpenReadAsync();
            var fileName = string.IsNullOrWhiteSpace(file.FileName) ? "import.crown" : file.FileName;
            var safeFileName = fileName.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
            var destPath = Path.Combine(FileSystem.CacheDirectory, $"import_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{safeFileName}");

            await using var output = File.Create(destPath);
            await input.CopyToAsync(output);

            return destPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error copiando archivo seleccionado a cache: {ex}");
            return null;
        }
    }
}

/// <summary>
/// Resultado de una importación de archivo .crown
/// </summary>
public class ImportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int SessionId { get; set; }
    public string? SessionName { get; set; }
    public int VideosImported { get; set; }
    public int AthletesImported { get; set; }
    public int TagsImported { get; set; }
    public int TagEventsImported { get; set; }
}

/// <summary>
/// Resultado de una exportación de archivo .crown
/// </summary>
public class ExportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FilePath { get; set; }
    public string? SessionName { get; set; }
    public int VideosExported { get; set; }
}

/// <summary>
/// Progreso de importación
/// </summary>
public class ImportProgress
{
    public string Message { get; set; } = "";
    public int Percentage { get; set; }
}
