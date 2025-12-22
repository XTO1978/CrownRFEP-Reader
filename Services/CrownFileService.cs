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

            // Mapeo: JSON InputTypeId → Local TagId
            var inputTypeIdToTagIdMap = new Dictionary<int, int>(); // jsonInputTypeId → localTagId
            
            // Extraer tipos de input únicos de los Inputs y guardarlos como Tags
            if (crownData.Inputs != null)
            {
                var uniqueInputTypes = crownData.Inputs
                    .Where(i => i.InputTypeObj != null && !string.IsNullOrWhiteSpace(i.InputTypeObj.TipoInput))
                    .Select(i => i.InputTypeObj!)
                    .DistinctBy(it => it.Id)
                    .ToList();
                
                foreach (var inputType in uniqueInputTypes)
                {
                    // Buscar si ya existe un tag con el mismo nombre
                    var existingTag = await _databaseService.FindTagByNameAsync(inputType.TipoInput!);
                    
                    int localTagId;
                    if (existingTag != null)
                    {
                        // Tag ya existe: usar su ID local
                        localTagId = existingTag.Id;
                    }
                    else
                    {
                        // Tag nuevo: insertar y obtener ID autogenerado
                        var newTag = new Tag
                        {
                            NombreTag = inputType.TipoInput
                        };
                        localTagId = await _databaseService.InsertTagAsync(newTag);
                        result.TagsImported++;
                    }
                    
                    inputTypeIdToTagIdMap[inputType.Id] = localTagId;
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

                    // Extraer video del ZIP
                    var videoEntry = FindEntry(archive, "videos", clipFileName);
                    var extractedClipPath = (string?)null;
                    if (videoEntry != null)
                    {
                        await ExtractEntryToFileAsync(videoEntry, localClipPath);
                        extractedClipPath = localClipPath;
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
                        IsComparisonVideo = clipJson.IsComparisonVideo,
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
                    
                    // Mapear el InputTypeId del JSON al TagId local
                    var localTagId = inputTypeIdToTagIdMap.TryGetValue(inputJson.InputTypeId, out var mappedTagId) ? mappedTagId : inputJson.InputTypeId;
                    
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
                        InputTypeId = localTagId,  // Usar el ID de tag local mapeado
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
                    { DevicePlatform.iOS, new[] { "public.data", "public.item", "public.content", "public.archive" } },
                    { DevicePlatform.Android, new[] { "application/octet-stream", "application/zip", "*/*" } },
                    { DevicePlatform.WinUI, new[] { ".crown", ".zip" } },
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
/// Progreso de importación
/// </summary>
public class ImportProgress
{
    public string Message { get; set; } = "";
    public int Percentage { get; set; }
}
