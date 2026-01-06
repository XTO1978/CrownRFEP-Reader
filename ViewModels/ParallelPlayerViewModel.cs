using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using Microsoft.Maui.Graphics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para el reproductor de vídeos paralelos.
/// Soporta dos modos:
/// - Individual: Cada vídeo tiene sus propios controles
/// - Simultáneo: Reproducción sincronizada con controles globales
/// </summary>
public class ParallelPlayerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private sealed record LapSegment(int LapNumber, TimeSpan Start, TimeSpan End)
    {
        public TimeSpan Duration => End - Start;
    }

    private VideoClip? _video1;
    private VideoClip? _video2;
    private bool _isHorizontalOrientation;
    private bool _isSimultaneousMode = false; // Por defecto modo individual
    private double _playbackSpeed = 1.0;

    private bool _isLapSyncEnabled;

    private List<LapSegment>? _lapSegments1;
    private List<LapSegment>? _lapSegments2;

    private bool _hasLapTiming;
    private string _currentLapText1 = string.Empty;
    private string _currentLapText2 = string.Empty;
    private string _currentLapDiffText = string.Empty;
    private Color _currentLapColor1 = Colors.White;
    private Color _currentLapColor2 = Colors.White;

    // Flags para evitar actualizar Progress mientras el usuario arrastra los sliders
    private bool _isDraggingSlider;
    private bool _isDraggingSlider1;
    private bool _isDraggingSlider2;

    // Estado global (modo simultáneo)
    private bool _isPlaying;
    private TimeSpan _currentPosition;
    private TimeSpan _duration;
    private double _progress;

    // Estado individual video 1
    private bool _isPlaying1;
    private TimeSpan _currentPosition1;
    private TimeSpan _duration1;
    private double _progress1;

    // Estado individual video 2
    private bool _isPlaying2;
    private TimeSpan _currentPosition2;
    private TimeSpan _duration2;
    private double _progress2;

    // Puntos de sincronización (posiciones de referencia al sincronizar)
    private TimeSpan _syncPoint1 = TimeSpan.Zero;
    private TimeSpan _syncPoint2 = TimeSpan.Zero;
    private TimeSpan _syncDuration = TimeSpan.Zero; // Duración efectiva en modo sincronizado

    // Reproductor seleccionado en modo individual (1 o 2)
    private int _selectedPlayer = 1;

    public ParallelPlayerViewModel()
    {
        // Comandos globales (modo simultáneo)
        PlayPauseCommand = new Command(TogglePlayPause);
        StopCommand = new Command(Stop);
        SeekBackwardCommand = new Command(() => Seek(-5));
        SeekForwardCommand = new Command(() => Seek(5));
        FrameBackwardCommand = new Command(StepBackward);
        FrameForwardCommand = new Command(StepForward);

        // Comandos individuales video 1
        PlayPauseCommand1 = new Command(TogglePlayPause1);
        StopCommand1 = new Command(Stop1);
        SeekBackwardCommand1 = new Command(() => Seek1(-5));
        SeekForwardCommand1 = new Command(() => Seek1(5));
        FrameBackwardCommand1 = new Command(StepBackward1);
        FrameForwardCommand1 = new Command(StepForward1);

        // Comandos individuales video 2
        PlayPauseCommand2 = new Command(TogglePlayPause2);
        StopCommand2 = new Command(Stop2);
        SeekBackwardCommand2 = new Command(() => Seek2(-5));
        SeekForwardCommand2 = new Command(() => Seek2(5));
        FrameBackwardCommand2 = new Command(StepBackward2);
        FrameForwardCommand2 = new Command(StepForward2);

        // Comandos comunes
        SetSpeedCommand = new Command<string>(SetSpeed);
        ToggleOrientationCommand = new Command(ToggleOrientation);
        ToggleModeCommand = new Command(ToggleMode);
        CloseCommand = new Command(async () => await CloseAsync());

        // Comandos de selección de reproductor
        SelectPlayer1Command = new Command(() => SelectedPlayer = 1);
        SelectPlayer2Command = new Command(() => SelectedPlayer = 2);
        
        // Comando de exportación
        ExportCommand = new Command(async () => await ExportParallelVideosAsync(), () => CanExport);
    }

    public bool HasLapTiming
    {
        get => _hasLapTiming;
        private set
        {
            if (_hasLapTiming == value) return;
            _hasLapTiming = value;
            OnPropertyChanged();
        }
    }

    public string CurrentLapText1
    {
        get => _currentLapText1;
        private set
        {
            if (_currentLapText1 == value) return;
            _currentLapText1 = value;
            OnPropertyChanged();
        }
    }

    public string CurrentLapText2
    {
        get => _currentLapText2;
        private set
        {
            if (_currentLapText2 == value) return;
            _currentLapText2 = value;
            OnPropertyChanged();
        }
    }

    public string CurrentLapDiffText
    {
        get => _currentLapDiffText;
        private set
        {
            if (_currentLapDiffText == value) return;
            _currentLapDiffText = value;
            OnPropertyChanged();
        }
    }

    public Color CurrentLapColor1
    {
        get => _currentLapColor1;
        private set
        {
            if (_currentLapColor1 == value) return;
            _currentLapColor1 = value;
            OnPropertyChanged();
        }
    }

    public Color CurrentLapColor2
    {
        get => _currentLapColor2;
        private set
        {
            if (_currentLapColor2 == value) return;
            _currentLapColor2 = value;
            OnPropertyChanged();
        }
    }

    private static string FormatLapTime(TimeSpan time)
    {
        // mm:ss.ff (centésimas) para encajar en overlay
        return $"{time:mm\\:ss\\.ff}";
    }

    private static List<LapSegment>? BuildLapSegments(
        List<ExecutionTimingEvent> events,
        TimeSpan fallbackEnd)
    {
        if (events.Count == 0)
            return null;

        // Elegir el run con más laps (y si empata, el de mayor tiempo)
        var bestRun = events
            .GroupBy(e => e.RunIndex)
            .Select(g => new
            {
                RunIndex = g.Key,
                LapCount = g.Count(e => e.Kind == 1),
                EndMs = g.Where(e => e.Kind == 2).Select(e => e.ElapsedMilliseconds).DefaultIfEmpty(0).Max()
            })
            .OrderByDescending(x => x.LapCount)
            .ThenByDescending(x => x.EndMs)
            .FirstOrDefault();

        var runIndex = bestRun?.RunIndex ?? 0;
        var runEvents = events
            .Where(e => e.RunIndex == runIndex)
            .OrderBy(e => e.ElapsedMilliseconds)
            .ToList();

        var startMs = runEvents.FirstOrDefault(e => e.Kind == 0)?.ElapsedMilliseconds ?? 0;
        var endMs = runEvents.LastOrDefault(e => e.Kind == 2)?.ElapsedMilliseconds;

        var start = TimeSpan.FromMilliseconds(startMs);
        var end = endMs.HasValue ? TimeSpan.FromMilliseconds(endMs.Value) : fallbackEnd;
        if (end <= start)
            return null;

        var lapMarkers = runEvents
            .Where(e => e.Kind == 1)
            .Select(e => TimeSpan.FromMilliseconds(e.ElapsedMilliseconds))
            .Where(t => t > start && t < end)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var boundaries = new List<TimeSpan>(capacity: 2 + lapMarkers.Count) { start };
        boundaries.AddRange(lapMarkers);
        boundaries.Add(end);

        if (boundaries.Count < 2)
            return null;

        var segments = new List<LapSegment>(capacity: boundaries.Count - 1);
        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            var segStart = boundaries[i];
            var segEnd = boundaries[i + 1];
            if (segEnd <= segStart) continue;
            segments.Add(new LapSegment(i + 1, segStart, segEnd));
        }

        return segments.Count > 0 ? segments : null;
    }

    private static LapSegment? FindLapAtPosition(List<LapSegment> segments, TimeSpan position)
    {
        // Último segmento si está fuera
        if (position <= segments[0].Start)
            return segments[0];
        if (position >= segments[^1].End)
            return segments[^1];

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (position >= seg.Start && position < seg.End)
                return seg;
        }

        return null;
    }

    private void UpdateLapOverlay()
    {
        if (_lapSegments1 == null || _lapSegments2 == null || _lapSegments1.Count == 0 || _lapSegments2.Count == 0)
        {
            HasLapTiming = false;
            CurrentLapText1 = string.Empty;
            CurrentLapText2 = string.Empty;
            CurrentLapDiffText = string.Empty;
            CurrentLapColor1 = Colors.White;
            CurrentLapColor2 = Colors.White;
            return;
        }

        var time1 = IsSimultaneousMode ? SyncPoint1 + CurrentPosition : CurrentPosition1;
        var time2 = IsSimultaneousMode ? SyncPoint2 + CurrentPosition : CurrentPosition2;

        var seg1 = FindLapAtPosition(_lapSegments1, time1);
        var seg2 = FindLapAtPosition(_lapSegments2, time2);
        if (seg1 == null || seg2 == null)
        {
            HasLapTiming = false;
            return;
        }

        HasLapTiming = true;

        var lapTime1 = seg1.Duration;
        var lapTime2 = seg2.Duration;

        CurrentLapText1 = $"Lap {seg1.LapNumber}: {FormatLapTime(lapTime1)}";
        CurrentLapText2 = $"Lap {seg2.LapNumber}: {FormatLapTime(lapTime2)}";

        var diff = lapTime1 - lapTime2;
        var sign = diff.TotalMilliseconds >= 0 ? "+" : "-";
        CurrentLapDiffText = $"Δ {sign}{FormatLapTime(TimeSpan.FromMilliseconds(Math.Abs(diff.TotalMilliseconds)))}";

        if (lapTime1 < lapTime2)
        {
            CurrentLapColor1 = Color.FromArgb("#FF34C759");
            CurrentLapColor2 = Color.FromArgb("#FFFF3B30");
        }
        else if (lapTime2 < lapTime1)
        {
            CurrentLapColor1 = Color.FromArgb("#FFFF3B30");
            CurrentLapColor2 = Color.FromArgb("#FF34C759");
        }
        else
        {
            CurrentLapColor1 = Colors.White;
            CurrentLapColor2 = Colors.White;
        }
    }

    private async Task LoadLapTimingAsync()
    {
        try
        {
            if (Video1 == null || Video2 == null)
                return;

            var services = Application.Current?.Handler?.MauiContext?.Services;
            var databaseService = services?.GetService<DatabaseService>();
            if (databaseService == null)
                return;

            var events1 = await databaseService.GetExecutionTimingEventsByVideoAsync(Video1.Id);
            var events2 = await databaseService.GetExecutionTimingEventsByVideoAsync(Video2.Id);

            var fallbackEnd1 = GetFallbackDuration(Video1, Duration1);
            var fallbackEnd2 = GetFallbackDuration(Video2, Duration2);

            _lapSegments1 = BuildLapSegments(events1, fallbackEnd1);
            _lapSegments2 = BuildLapSegments(events2, fallbackEnd2);

            UpdateLapOverlay();
        }
        catch
        {
            // No bloquear el reproductor si falla la lectura de parciales
        }
    }

    public bool IsLapSyncEnabled
    {
        get => _isLapSyncEnabled;
        set
        {
            if (_isLapSyncEnabled == value) return;
            _isLapSyncEnabled = value;
            OnPropertyChanged();
        }
    }

    #region Export Methods

    private static TimeSpan GetFallbackDuration(VideoClip clip, TimeSpan durationProperty)
    {
        if (durationProperty > TimeSpan.Zero)
            return durationProperty;

        if (clip.ClipDuration > 0)
            return TimeSpan.FromSeconds(clip.ClipDuration);

        return TimeSpan.Zero;
    }

    private static List<TimeSpan>? BuildLapBoundaries(
        List<ExecutionTimingEvent> timingEvents,
        TimeSpan exportStart,
        TimeSpan exportEnd)
    {
        if (exportEnd <= exportStart)
            return null;

        var lapMarkers = timingEvents
            .Where(e => e.Kind == 1)
            .Select(e => TimeSpan.FromMilliseconds(e.ElapsedMilliseconds))
            .Where(t => t > exportStart && t < exportEnd)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var boundaries = new List<TimeSpan>(capacity: 2 + lapMarkers.Count)
        {
            exportStart
        };
        boundaries.AddRange(lapMarkers);
        boundaries.Add(exportEnd);

        return boundaries.Count >= 2 ? boundaries : null;
    }

    private async Task ExportParallelVideosAsync()
    {
        if (Video1 == null || Video2 == null || IsExporting)
            return;

        try
        {
            IsExporting = true;
            ExportStatus = "Preparando exportación...";
            ExportProgress = 0;

            // Obtener servicios necesarios
            var services = Application.Current?.Handler?.MauiContext?.Services;
            var compositionService = services?.GetService<IVideoCompositionService>();
            var databaseService = services?.GetService<DatabaseService>();
            var exportNotifier = services?.GetService<VideoExportNotifier>();
            
            if (compositionService == null)
            {
                ExportStatus = "Error: Servicio de composición no disponible";
                return;
            }
            
            if (databaseService == null)
            {
                ExportStatus = "Error: Servicio de base de datos no disponible";
                return;
            }

            // Determinar la sesión y carpeta de destino
            var session = Video1.Session;
            string outputFolder;
            string sessionPath = "";
            
            if (session != null && !string.IsNullOrEmpty(session.PathSesion))
            {
                sessionPath = session.PathSesion;
                // Guardar en la subcarpeta "videos" de la sesión
                outputFolder = Path.Combine(sessionPath, "videos");
                
                // Crear la carpeta si no existe
                if (!Directory.Exists(outputFolder))
                {
                    Directory.CreateDirectory(outputFolder);
                }
            }
            else
            {
                outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            }

            // Crear nombre de archivo descriptivo: "Atleta1 vs Atleta2"
            var athlete1Name = Video1.Atleta?.NombreCompleto ?? "Atleta1";
            var athlete2Name = Video2.Atleta?.NombreCompleto ?? "Atleta2";
            var fileName = $"{athlete1Name} vs {athlete2Name}.mp4";
            // Limpiar caracteres inválidos del nombre
            fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            var outputPath = Path.Combine(outputFolder, fileName);
            
            // Si ya existe un archivo con ese nombre, añadir timestamp
            if (File.Exists(outputPath))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                fileName = $"{athlete1Name} vs {athlete2Name}_{timestamp}.mp4";
                fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
                outputPath = Path.Combine(outputFolder, fileName);
            }

            // Determinar las rutas correctas de los videos (preferir LocalClipPath)
            var video1Path = Video1.LocalClipPath ?? Video1.ClipPath ?? string.Empty;
            var video2Path = Video2.LocalClipPath ?? Video2.ClipPath ?? string.Empty;
            
            System.Diagnostics.Debug.WriteLine($"[Export] Video1Path: {video1Path}");
            System.Diagnostics.Debug.WriteLine($"[Export] Video2Path: {video2Path}");

            // Configurar parámetros de exportación
            var exportParams = new ParallelVideoExportParams
            {
                Video1Path = video1Path,
                Video2Path = video2Path,
                Video1StartPosition = CurrentPosition1,
                Video2StartPosition = CurrentPosition2,
                IsHorizontalLayout = IsHorizontalOrientation,
                Video1AthleteName = Video1.Atleta?.NombreCompleto,
                Video1Category = Video1.Atleta?.CategoriaNombre,
                Video1Section = Video1.Section,
                Video2AthleteName = Video2.Atleta?.NombreCompleto,
                Video2Category = Video2.Atleta?.CategoriaNombre,
                Video2Section = Video2.Section,
                OutputPath = outputPath
            };

            if (IsLapSyncEnabled)
            {
                ExportStatus = "Leyendo parciales...";

                var timing1 = await databaseService.GetExecutionTimingEventsByVideoAsync(Video1.Id);
                var timing2 = await databaseService.GetExecutionTimingEventsByVideoAsync(Video2.Id);

                var end1FromEventsMs = timing1.LastOrDefault(e => e.Kind == 2)?.ElapsedMilliseconds;
                var end2FromEventsMs = timing2.LastOrDefault(e => e.Kind == 2)?.ElapsedMilliseconds;

                var end1 = end1FromEventsMs.HasValue
                    ? TimeSpan.FromMilliseconds(end1FromEventsMs.Value)
                    : GetFallbackDuration(Video1, Duration1);
                var end2 = end2FromEventsMs.HasValue
                    ? TimeSpan.FromMilliseconds(end2FromEventsMs.Value)
                    : GetFallbackDuration(Video2, Duration2);

                var start1FromEventsMs = timing1.FirstOrDefault(e => e.Kind == 0)?.ElapsedMilliseconds;
                var start2FromEventsMs = timing2.FirstOrDefault(e => e.Kind == 0)?.ElapsedMilliseconds;

                var exportStart1 = start1FromEventsMs.HasValue
                    ? TimeSpan.FromMilliseconds(start1FromEventsMs.Value)
                    : CurrentPosition1;
                var exportStart2 = start2FromEventsMs.HasValue
                    ? TimeSpan.FromMilliseconds(start2FromEventsMs.Value)
                    : CurrentPosition2;

                var boundaries1 = BuildLapBoundaries(timing1, exportStart1, end1);
                var boundaries2 = BuildLapBoundaries(timing2, exportStart2, end2);

                if (boundaries1 == null || boundaries2 == null || boundaries1.Count < 2 || boundaries2.Count < 2)
                {
                    ExportStatus = "No hay parciales suficientes para sincronizar";
                    return;
                }

                exportParams.SyncByLaps = true;
                exportParams.Video1LapBoundaries = boundaries1;
                exportParams.Video2LapBoundaries = boundaries2;
                exportParams.Video1StartPosition = exportStart1;
                exportParams.Video2StartPosition = exportStart2;
            }

            ExportStatus = "Componiendo vídeos...";

            // Ejecutar la exportación
            var result = await compositionService.ExportParallelVideosAsync(
                exportParams,
                new Progress<double>(progress =>
                {
                    ExportProgress = progress;
                    ExportStatus = $"Exportando... {progress:P0}";
                }));

            if (result.Success)
            {
                ExportStatus = "Generando miniatura...";
                ExportProgress = 0.90;

                // Generar miniatura para el video exportado
                var thumbnailService = services?.GetService<ThumbnailService>();
                string? thumbnailPath = null;
                string? thumbnailFileName = null;
                
                if (thumbnailService != null && !string.IsNullOrEmpty(sessionPath))
                {
                    // Crear carpeta de thumbnails si no existe
                    var thumbnailFolder = Path.Combine(sessionPath, "thumbnails");
                    if (!Directory.Exists(thumbnailFolder))
                    {
                        Directory.CreateDirectory(thumbnailFolder);
                    }
                    
                    // Nombre del thumbnail basado en el nombre del video
                    thumbnailFileName = Path.GetFileNameWithoutExtension(fileName) + "_thumb.jpg";
                    thumbnailPath = Path.Combine(thumbnailFolder, thumbnailFileName);
                    
                    try
                    {
                        var thumbResult = await thumbnailService.GenerateComparisonThumbnailAsync(result.OutputPath!, thumbnailPath);
                        if (!thumbResult)
                        {
                            System.Diagnostics.Debug.WriteLine("[Export] No se pudo generar la miniatura");
                            thumbnailPath = null;
                            thumbnailFileName = null;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[Export] Miniatura generada: {thumbnailPath}");
                        }
                    }
                    catch (Exception thumbEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Export] Error generando miniatura: {thumbEx.Message}");
                        thumbnailPath = null;
                        thumbnailFileName = null;
                    }
                }

                ExportStatus = "Guardando en biblioteca...";
                ExportProgress = 0.95;

                // Crear VideoClip para guardar en la base de datos
                // IsComparisonVideo se calcula automáticamente a partir de ComparisonName
                var newClip = new VideoClip
                {
                    SessionId = session?.Id ?? Video1.SessionId,
                    AtletaId = Video1.AtletaId, // Asociar al atleta principal
                    Section = 0, // Las comparativas no tienen sección específica
                    CreationDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ClipPath = fileName, // Solo el nombre del archivo (relativo a la carpeta de videos)
                    LocalClipPath = result.OutputPath, // Ruta absoluta
                    ThumbnailPath = thumbnailFileName, // Nombre del archivo de miniatura
                    LocalThumbnailPath = thumbnailPath, // Ruta absoluta de la miniatura
                    ClipDuration = result.Duration.TotalSeconds,
                    ClipSize = result.FileSizeBytes,
                    ComparisonName = $"{Video1.Atleta?.NombreCompleto ?? "Atleta 1"} vs {Video2.Atleta?.NombreCompleto ?? "Atleta 2"}"
                };

                // Guardar en la base de datos
                try
                {
                    var clipId = await databaseService.InsertVideoClipAsync(newClip);
                    System.Diagnostics.Debug.WriteLine($"[Export] VideoClip guardado con ID: {clipId}");
                    
                    // Notificar al Dashboard para que refresque la galería
                    exportNotifier?.NotifyVideoExported(newClip.SessionId, clipId);
                }
                catch (Exception dbEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Export] Error guardando en BD: {dbEx.Message}");
                    // Continuar aunque falle la BD - el archivo ya está guardado
                }

                ExportStatus = "¡Exportación completada!";
                ExportProgress = 1.0;
            }
            else
            {
                ExportStatus = $"Error: {result.ErrorMessage}";
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Error de exportación",
                        result.ErrorMessage ?? "Error desconocido durante la exportación",
                        "OK");
                }
            }
        }
        catch (Exception ex)
        {
            ExportStatus = $"Error: {ex.Message}";
            if (Application.Current?.MainPage != null)
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Error",
                    $"Error durante la exportación: {ex.Message}",
                    "OK");
            }
        }
        finally
        {
            IsExporting = false;
            // Notificar cambio en CanExport
            OnPropertyChanged(nameof(CanExport));
        }
    }

    #endregion

    #region Propiedades de vídeos

    public VideoClip? Video1
    {
        get => _video1;
        set { _video1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasVideo1)); }
    }

    public VideoClip? Video2
    {
        get => _video2;
        set { _video2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasVideo2)); }
    }

    public bool HasVideo1 => _video1 != null;
    public bool HasVideo2 => _video2 != null;

    #endregion

    #region Propiedades de orientación y modo

    public bool IsHorizontalOrientation
    {
        get => _isHorizontalOrientation;
        set
        {
            _isHorizontalOrientation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVerticalOrientation));
        }
    }

    public bool IsVerticalOrientation => !_isHorizontalOrientation;

    public bool IsSimultaneousMode
    {
        get => _isSimultaneousMode;
        set
        {
            if (_isSimultaneousMode != value)
            {
                _isSimultaneousMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIndividualMode));
                OnPropertyChanged(nameof(ModeIcon));
                OnPropertyChanged(nameof(ModeText));
                // Notificar cambio en propiedades de selección visual
                OnPropertyChanged(nameof(ShowPlayer1Selected));
                OnPropertyChanged(nameof(ShowPlayer2Selected));
                // Notificar cambio en CanExport (solo se puede exportar en modo sincronizado)
                OnPropertyChanged(nameof(CanExport));
                UpdateLapOverlay();
                ModeChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsIndividualMode => !_isSimultaneousMode;
    public string ModeIcon => IsSimultaneousMode ? "link.badge.plus" : "link";
    public string ModeText => IsSimultaneousMode ? "Desincronizar" : "Sincronizar";

    /// <summary>
    /// Reproductor seleccionado en modo individual (1 o 2).
    /// Los controles de teclado actuarán sobre este reproductor.
    /// </summary>
    public int SelectedPlayer
    {
        get => _selectedPlayer;
        set
        {
            // Solo permitir cambio de selección en modo individual
            if (IsSimultaneousMode)
                return;
                
            if (_selectedPlayer != value)
            {
                _selectedPlayer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPlayer1Selected));
                OnPropertyChanged(nameof(IsPlayer2Selected));
                OnPropertyChanged(nameof(ShowPlayer1Selected));
                OnPropertyChanged(nameof(ShowPlayer2Selected));
            }
        }
    }

    public bool IsPlayer1Selected => SelectedPlayer == 1;
    public bool IsPlayer2Selected => SelectedPlayer == 2;
    
    /// <summary>
    /// Indica si mostrar la selección visual del Player 1 (solo en modo individual)
    /// </summary>
    public bool ShowPlayer1Selected => IsIndividualMode && IsPlayer1Selected;
    
    /// <summary>
    /// Indica si mostrar la selección visual del Player 2 (solo en modo individual)
    /// </summary>
    public bool ShowPlayer2Selected => IsIndividualMode && IsPlayer2Selected;

    #endregion

    #region Estado global (modo simultáneo)

    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon)); }
    }

    public string PlayPauseIcon => IsPlaying ? "pause.fill" : "play.fill";

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            _currentPosition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionText));
            UpdateProgress();
            UpdateLapOverlay();
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            _duration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText));
            UpdateProgress();
        }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }
    
    /// <summary>
    /// Flags para indicar si el usuario está arrastrando los sliders.
    /// Cuando están activos, UpdateProgress no actualiza Progress para evitar conflictos.
    /// </summary>
    public bool IsDraggingSlider
    {
        get => _isDraggingSlider;
        set => _isDraggingSlider = value;
    }
    
    public bool IsDraggingSlider1
    {
        get => _isDraggingSlider1;
        set => _isDraggingSlider1 = value;
    }
    
    public bool IsDraggingSlider2
    {
        get => _isDraggingSlider2;
        set => _isDraggingSlider2 = value;
    }

    public string CurrentPositionText => $"{CurrentPosition:mm\\:ss\\.ff}";
    public string DurationText => $"{Duration:mm\\:ss\\.ff}";

    #endregion

    #region Estado individual video 1

    public bool IsPlaying1
    {
        get => _isPlaying1;
        set { _isPlaying1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon1)); }
    }

    public string PlayPauseIcon1 => IsPlaying1 ? "pause.fill" : "play.fill";

    public TimeSpan CurrentPosition1
    {
        get => _currentPosition1;
        set
        {
            _currentPosition1 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionText1));
            UpdateProgress1();
            UpdateLapOverlay();
        }
    }

    public TimeSpan Duration1
    {
        get => _duration1;
        set
        {
            _duration1 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText1));
            UpdateProgress1();
        }
    }

    public double Progress1
    {
        get => _progress1;
        set { _progress1 = value; OnPropertyChanged(); }
    }

    public string CurrentPositionText1 => $"{CurrentPosition1:mm\\:ss\\.ff}";
    public string DurationText1 => $"{Duration1:mm\\:ss\\.ff}";

    #endregion

    #region Estado individual video 2

    public bool IsPlaying2
    {
        get => _isPlaying2;
        set { _isPlaying2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon2)); }
    }

    public string PlayPauseIcon2 => IsPlaying2 ? "pause.fill" : "play.fill";

    public TimeSpan CurrentPosition2
    {
        get => _currentPosition2;
        set
        {
            _currentPosition2 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionText2));
            UpdateProgress2();
            UpdateLapOverlay();
        }
    }

    public TimeSpan Duration2
    {
        get => _duration2;
        set
        {
            _duration2 = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText2));
            UpdateProgress2();
        }
    }

    public double Progress2
    {
        get => _progress2;
        set { _progress2 = value; OnPropertyChanged(); }
    }

    public string CurrentPositionText2 => $"{CurrentPosition2:mm\\:ss\\.ff}";
    public string DurationText2 => $"{Duration2:mm\\:ss\\.ff}";

    #endregion

    #region Velocidad

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set { _playbackSpeed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); }
    }

    public string SpeedText => $"{PlaybackSpeed:0.##}x";

    #endregion

    #region Comandos globales

    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand FrameBackwardCommand { get; }
    public ICommand FrameForwardCommand { get; }

    #endregion

    #region Comandos individuales video 1

    public ICommand PlayPauseCommand1 { get; }
    public ICommand StopCommand1 { get; }
    public ICommand SeekBackwardCommand1 { get; }
    public ICommand SeekForwardCommand1 { get; }
    public ICommand FrameBackwardCommand1 { get; }
    public ICommand FrameForwardCommand1 { get; }

    #endregion

    #region Comandos individuales video 2

    public ICommand PlayPauseCommand2 { get; }
    public ICommand StopCommand2 { get; }
    public ICommand SeekBackwardCommand2 { get; }
    public ICommand SeekForwardCommand2 { get; }
    public ICommand FrameBackwardCommand2 { get; }
    public ICommand FrameForwardCommand2 { get; }

    #endregion

    #region Comandos comunes

    public ICommand SetSpeedCommand { get; }
    public ICommand ToggleOrientationCommand { get; }
    public ICommand ToggleModeCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand SelectPlayer1Command { get; }
    public ICommand SelectPlayer2Command { get; }
    public ICommand ExportCommand { get; }

    #endregion

    #region Export Properties

    private bool _isExporting;
    public bool IsExporting
    {
        get => _isExporting;
        set { _isExporting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExport)); }
    }

    private double _exportProgress;
    public double ExportProgress
    {
        get => _exportProgress;
        set { _exportProgress = value; OnPropertyChanged(); }
    }

    private string _exportStatus = string.Empty;
    public string ExportStatus
    {
        get => _exportStatus;
        set { _exportStatus = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Solo se puede exportar cuando hay dos videos y están sincronizados
    /// </summary>
    public bool CanExport => !IsExporting && HasVideo1 && HasVideo2 && IsSimultaneousMode;

    #endregion

    #region Eventos

    // Eventos globales (modo simultáneo)
    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? FrameForwardRequested;
    public event EventHandler? FrameBackwardRequested;

    // Eventos individuales video 1
    public event EventHandler? PlayRequested1;
    public event EventHandler? PauseRequested1;
    public event EventHandler? StopRequested1;
    public event EventHandler<double>? SeekRequested1;
    public event EventHandler? FrameForwardRequested1;
    public event EventHandler? FrameBackwardRequested1;

    // Eventos individuales video 2
    public event EventHandler? PlayRequested2;
    public event EventHandler? PauseRequested2;
    public event EventHandler? StopRequested2;
    public event EventHandler<double>? SeekRequested2;
    public event EventHandler? FrameForwardRequested2;
    public event EventHandler? FrameBackwardRequested2;

    // Eventos comunes
    public event EventHandler<double>? SpeedChangeRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler<bool>? ModeChanged;
    
    /// <summary>
    /// Se dispara cuando se establece la sincronización con los puntos de referencia.
    /// Los argumentos son (SyncPoint1, SyncPoint2).
    /// </summary>
    public event EventHandler<(TimeSpan SyncPoint1, TimeSpan SyncPoint2)>? SyncPointsEstablished;

    #endregion

    #region Propiedades de sincronización

    /// <summary>
    /// Punto de referencia del vídeo 1 para sincronización
    /// </summary>
    public TimeSpan SyncPoint1
    {
        get => _syncPoint1;
        private set { _syncPoint1 = value; OnPropertyChanged(); UpdateLapOverlay(); }
    }

    /// <summary>
    /// Punto de referencia del vídeo 2 para sincronización
    /// </summary>
    public TimeSpan SyncPoint2
    {
        get => _syncPoint2;
        private set { _syncPoint2 = value; OnPropertyChanged(); UpdateLapOverlay(); }
    }

    /// <summary>
    /// Duración efectiva en modo sincronizado (la menor de las duraciones restantes)
    /// </summary>
    public TimeSpan SyncDuration
    {
        get => _syncDuration;
        private set { _syncDuration = value; OnPropertyChanged(); }
    }

    #endregion

    #region Métodos públicos

    public void Initialize(VideoClip? video1, VideoClip? video2, bool isHorizontal)
    {
        Video1 = video1;
        Video2 = video2;
        IsHorizontalOrientation = isHorizontal;
        IsSimultaneousMode = false; // Iniciar en modo individual
        ResetAllStates();

        _ = LoadLapTimingAsync();
    }

    public void SeekToPosition(double position)
    {
        var newPosition = position * Duration.TotalSeconds;
        SeekRequested?.Invoke(this, newPosition);
    }

    public void SeekToPosition1(double position)
    {
        var newPosition = position * Duration1.TotalSeconds;
        SeekRequested1?.Invoke(this, newPosition);
    }

    public void SeekToPosition2(double position)
    {
        var newPosition = position * Duration2.TotalSeconds;
        SeekRequested2?.Invoke(this, newPosition);
    }

    #endregion

    #region Métodos privados - Globales

    private void TogglePlayPause()
    {
        IsPlaying = !IsPlaying;
        if (IsPlaying)
            PlayRequested?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        IsPlaying = false;
        CurrentPosition = TimeSpan.Zero;
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Seek(double seconds)
    {
        var newPosition = CurrentPosition.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration.TotalSeconds));
        SeekRequested?.Invoke(this, newPosition);
    }

    private void StepForward()
    {
        IsPlaying = false;
        FrameForwardRequested?.Invoke(this, EventArgs.Empty);
    }

    private void StepBackward()
    {
        IsPlaying = false;
        FrameBackwardRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateProgress()
    {
        if (_isDraggingSlider) return;
        
        if (Duration.TotalSeconds > 0)
            Progress = CurrentPosition.TotalSeconds / Duration.TotalSeconds;
        else
            Progress = 0;
    }

    #endregion

    #region Métodos privados - Video 1

    private void TogglePlayPause1()
    {
        IsPlaying1 = !IsPlaying1;
        if (IsPlaying1)
            PlayRequested1?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested1?.Invoke(this, EventArgs.Empty);
    }

    private void Stop1()
    {
        IsPlaying1 = false;
        CurrentPosition1 = TimeSpan.Zero;
        StopRequested1?.Invoke(this, EventArgs.Empty);
    }

    private void Seek1(double seconds)
    {
        var newPosition = CurrentPosition1.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration1.TotalSeconds));
        SeekRequested1?.Invoke(this, newPosition);
    }

    private void StepForward1()
    {
        IsPlaying1 = false;
        FrameForwardRequested1?.Invoke(this, EventArgs.Empty);
    }

    private void StepBackward1()
    {
        IsPlaying1 = false;
        FrameBackwardRequested1?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateProgress1()
    {
        if (_isDraggingSlider1) return;
        
        if (Duration1.TotalSeconds > 0)
            Progress1 = CurrentPosition1.TotalSeconds / Duration1.TotalSeconds;
        else
            Progress1 = 0;
    }

    #endregion

    #region Métodos privados - Video 2

    private void TogglePlayPause2()
    {
        IsPlaying2 = !IsPlaying2;
        if (IsPlaying2)
            PlayRequested2?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested2?.Invoke(this, EventArgs.Empty);
    }

    private void Stop2()
    {
        IsPlaying2 = false;
        CurrentPosition2 = TimeSpan.Zero;
        StopRequested2?.Invoke(this, EventArgs.Empty);
    }

    private void Seek2(double seconds)
    {
        var newPosition = CurrentPosition2.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration2.TotalSeconds));
        SeekRequested2?.Invoke(this, newPosition);
    }

    private void StepForward2()
    {
        IsPlaying2 = false;
        FrameForwardRequested2?.Invoke(this, EventArgs.Empty);
    }

    private void StepBackward2()
    {
        IsPlaying2 = false;
        FrameBackwardRequested2?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateProgress2()
    {
        if (_isDraggingSlider2) return;
        
        if (Duration2.TotalSeconds > 0)
            Progress2 = CurrentPosition2.TotalSeconds / Duration2.TotalSeconds;
        else
            Progress2 = 0;
    }

    #endregion

    #region Métodos privados - Comunes

    private void SetSpeed(string? speedStr)
    {
        if (double.TryParse(speedStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var speed))
        {
            PlaybackSpeed = speed;
            SpeedChangeRequested?.Invoke(this, speed);
        }
    }

    private void ToggleOrientation()
    {
        IsHorizontalOrientation = !IsHorizontalOrientation;
    }

    private void ToggleMode()
    {
        // Pausar todo antes de cambiar de modo
        if (IsSimultaneousMode)
        {
            // Saliendo del modo sincronizado → volver a individual
            if (IsPlaying) Stop();
            
            // Resetear los sync points
            SyncPoint1 = TimeSpan.Zero;
            SyncPoint2 = TimeSpan.Zero;
            SyncDuration = TimeSpan.Zero;
        }
        else
        {
            // Entrando al modo sincronizado → capturar posiciones actuales como puntos de referencia
            if (IsPlaying1) 
            {
                PauseRequested1?.Invoke(this, EventArgs.Empty);
                IsPlaying1 = false;
            }
            if (IsPlaying2) 
            {
                PauseRequested2?.Invoke(this, EventArgs.Empty);
                IsPlaying2 = false;
            }
            
            // Guardar las posiciones actuales como puntos de sincronización
            SyncPoint1 = CurrentPosition1;
            SyncPoint2 = CurrentPosition2;
            
            // Calcular la duración efectiva sincronizada
            // Es el mínimo entre lo que queda de cada vídeo desde su punto de sync
            var remaining1 = Duration1 - SyncPoint1;
            var remaining2 = Duration2 - SyncPoint2;
            SyncDuration = remaining1 < remaining2 ? remaining1 : remaining2;
            
            // Inicializar la posición global a 0 (inicio de la sincronización)
            Duration = SyncDuration;
            CurrentPosition = TimeSpan.Zero;
            Progress = 0;
            
            // Notificar los puntos de sincronización establecidos
            SyncPointsEstablished?.Invoke(this, (SyncPoint1, SyncPoint2));
        }

        IsSimultaneousMode = !IsSimultaneousMode;
    }

    private async Task CloseAsync()
    {
        Stop();
        Stop1();
        Stop2();
        CloseRequested?.Invoke(this, EventArgs.Empty);
        await Shell.Current.Navigation.PopAsync();
    }

    private void ResetAllStates()
    {
        IsPlaying = false;
        CurrentPosition = TimeSpan.Zero;
        Progress = 0;
        PlaybackSpeed = 1.0;

        IsPlaying1 = false;
        CurrentPosition1 = TimeSpan.Zero;
        Progress1 = 0;

        IsPlaying2 = false;
        CurrentPosition2 = TimeSpan.Zero;
        Progress2 = 0;
    }

    #endregion

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
