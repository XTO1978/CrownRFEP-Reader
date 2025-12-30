using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// ViewModel para el reproductor de 4 vídeos (cuádruple).
/// Soporta modo simultáneo e individual.
/// </summary>
public class QuadPlayerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private VideoClip? _video1;
    private VideoClip? _video2;
    private VideoClip? _video3;
    private VideoClip? _video4;
    private double _playbackSpeed = 1.0;
    private bool _isSimultaneousMode = false;

    // Flags para evitar actualizar Progress mientras el usuario arrastra los sliders
    private bool _isDragging1;
    private bool _isDragging2;
    private bool _isDragging3;
    private bool _isDragging4;

    // Estado global (sincronizado)
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

    // Estado individual video 3
    private bool _isPlaying3;
    private TimeSpan _currentPosition3;
    private TimeSpan _duration3;
    private double _progress3;

    // Estado individual video 4
    private bool _isPlaying4;
    private TimeSpan _currentPosition4;
    private TimeSpan _duration4;
    private double _progress4;

    // Puntos de sincronización
    private TimeSpan _syncPoint1 = TimeSpan.Zero;
    private TimeSpan _syncPoint2 = TimeSpan.Zero;
    private TimeSpan _syncPoint3 = TimeSpan.Zero;
    private TimeSpan _syncPoint4 = TimeSpan.Zero;
    private TimeSpan _syncDuration = TimeSpan.Zero;

    // Estado de exportación
    private bool _isExporting;
    private double _exportProgress;
    private string _exportStatus = "";

    public QuadPlayerViewModel()
    {
        // Comandos globales (sincronizados)
        PlayPauseCommand = new Command(TogglePlayPause);
        StopCommand = new Command(Stop);
        SeekBackwardCommand = new Command(() => Seek(-5));
        SeekForwardCommand = new Command(() => Seek(5));
        FrameBackwardCommand = new Command(StepBackward);
        FrameForwardCommand = new Command(StepForward);

        // Comandos individuales video 1
        PlayPauseCommand1 = new Command(TogglePlayPause1);
        SeekBackwardCommand1 = new Command(() => Seek1(-5));
        SeekForwardCommand1 = new Command(() => Seek1(5));

        // Comandos individuales video 2
        PlayPauseCommand2 = new Command(TogglePlayPause2);
        SeekBackwardCommand2 = new Command(() => Seek2(-5));
        SeekForwardCommand2 = new Command(() => Seek2(5));

        // Comandos individuales video 3
        PlayPauseCommand3 = new Command(TogglePlayPause3);
        SeekBackwardCommand3 = new Command(() => Seek3(-5));
        SeekForwardCommand3 = new Command(() => Seek3(5));

        // Comandos individuales video 4
        PlayPauseCommand4 = new Command(TogglePlayPause4);
        SeekBackwardCommand4 = new Command(() => Seek4(-5));
        SeekForwardCommand4 = new Command(() => Seek4(5));

        // Comandos comunes
        SetSpeedCommand = new Command<string>(SetSpeed);
        CloseCommand = new Command(async () => await CloseAsync());
        ToggleModeCommand = new Command(ToggleMode);
        SyncVideosCommand = new Command(SyncVideos);
        
        // Comando de exportación
        ExportCommand = new Command(async () => await ExportQuadVideosAsync(), () => CanExport);
    }

    #region Propiedades de Videos

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

    public VideoClip? Video3
    {
        get => _video3;
        set { _video3 = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasVideo3)); }
    }

    public VideoClip? Video4
    {
        get => _video4;
        set { _video4 = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasVideo4)); }
    }

    public bool HasVideo1 => Video1 != null;
    public bool HasVideo2 => Video2 != null;
    public bool HasVideo3 => Video3 != null;
    public bool HasVideo4 => Video4 != null;

    public int VideoCount => (HasVideo1 ? 1 : 0) + (HasVideo2 ? 1 : 0) + (HasVideo3 ? 1 : 0) + (HasVideo4 ? 1 : 0);

    #endregion

    #region Propiedades de Modo

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
                OnPropertyChanged(nameof(ModeText));
                ModeChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsIndividualMode => !IsSimultaneousMode;
    public string ModeText => IsSimultaneousMode ? "Simultáneo" : "Individual";

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (_playbackSpeed != value)
            {
                _playbackSpeed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedText));
                SpeedChanged?.Invoke(this, value);
            }
        }
    }

    public string SpeedText => PlaybackSpeed == 1.0 ? "1x" : $"{PlaybackSpeed:0.##}x";

    #endregion

    #region Estado Global

    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon)); }
    }

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set { _currentPosition = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentPositionText)); UpdateProgress(); }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); UpdateProgress(); }
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
    public bool IsDragging1 { get => _isDragging1; set => _isDragging1 = value; }
    public bool IsDragging2 { get => _isDragging2; set => _isDragging2 = value; }
    public bool IsDragging3 { get => _isDragging3; set => _isDragging3 = value; }
    public bool IsDragging4 { get => _isDragging4; set => _isDragging4 = value; }

    public string PlayPauseIcon => IsPlaying ? "pause.fill" : "play.fill";
    public string CurrentPositionText => FormatTime(CurrentPosition);
    public string DurationText => FormatTime(Duration);

    #endregion

    #region Estado Individual Video 1

    public bool IsPlaying1
    {
        get => _isPlaying1;
        set { _isPlaying1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon1)); }
    }

    public TimeSpan CurrentPosition1
    {
        get => _currentPosition1;
        set { _currentPosition1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentPositionText1)); UpdateProgress1(); }
    }

    public TimeSpan Duration1
    {
        get => _duration1;
        set { _duration1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText1)); UpdateProgress1(); }
    }

    public double Progress1
    {
        get => _progress1;
        set { _progress1 = value; OnPropertyChanged(); }
    }

    public string PlayPauseIcon1 => IsPlaying1 ? "pause.fill" : "play.fill";
    public string CurrentPositionText1 => FormatTime(CurrentPosition1);
    public string DurationText1 => FormatTime(Duration1);

    #endregion

    #region Estado Individual Video 2

    public bool IsPlaying2
    {
        get => _isPlaying2;
        set { _isPlaying2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon2)); }
    }

    public TimeSpan CurrentPosition2
    {
        get => _currentPosition2;
        set { _currentPosition2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentPositionText2)); UpdateProgress2(); }
    }

    public TimeSpan Duration2
    {
        get => _duration2;
        set { _duration2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText2)); UpdateProgress2(); }
    }

    public double Progress2
    {
        get => _progress2;
        set { _progress2 = value; OnPropertyChanged(); }
    }

    public string PlayPauseIcon2 => IsPlaying2 ? "pause.fill" : "play.fill";
    public string CurrentPositionText2 => FormatTime(CurrentPosition2);
    public string DurationText2 => FormatTime(Duration2);

    #endregion

    #region Estado Individual Video 3

    public bool IsPlaying3
    {
        get => _isPlaying3;
        set { _isPlaying3 = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon3)); }
    }

    public TimeSpan CurrentPosition3
    {
        get => _currentPosition3;
        set { _currentPosition3 = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentPositionText3)); UpdateProgress3(); }
    }

    public TimeSpan Duration3
    {
        get => _duration3;
        set { _duration3 = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText3)); UpdateProgress3(); }
    }

    public double Progress3
    {
        get => _progress3;
        set { _progress3 = value; OnPropertyChanged(); }
    }

    public string PlayPauseIcon3 => IsPlaying3 ? "pause.fill" : "play.fill";
    public string CurrentPositionText3 => FormatTime(CurrentPosition3);
    public string DurationText3 => FormatTime(Duration3);

    #endregion

    #region Estado Individual Video 4

    public bool IsPlaying4
    {
        get => _isPlaying4;
        set { _isPlaying4 = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayPauseIcon4)); }
    }

    public TimeSpan CurrentPosition4
    {
        get => _currentPosition4;
        set { _currentPosition4 = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentPositionText4)); UpdateProgress4(); }
    }

    public TimeSpan Duration4
    {
        get => _duration4;
        set { _duration4 = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText4)); UpdateProgress4(); }
    }

    public double Progress4
    {
        get => _progress4;
        set { _progress4 = value; OnPropertyChanged(); }
    }

    public string PlayPauseIcon4 => IsPlaying4 ? "pause.fill" : "play.fill";
    public string CurrentPositionText4 => FormatTime(CurrentPosition4);
    public string DurationText4 => FormatTime(Duration4);

    #endregion

    #region Puntos de Sincronización

    public TimeSpan SyncPoint1 { get; set; } = TimeSpan.Zero;
    public TimeSpan SyncPoint2 { get; set; } = TimeSpan.Zero;
    public TimeSpan SyncPoint3 { get; set; } = TimeSpan.Zero;
    public TimeSpan SyncPoint4 { get; set; } = TimeSpan.Zero;
    public TimeSpan SyncDuration { get; set; } = TimeSpan.Zero;

    #endregion

    #region Propiedades de Exportación

    public bool IsExporting
    {
        get => _isExporting;
        set { _isExporting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExport)); }
    }

    public double ExportProgress
    {
        get => _exportProgress;
        set { _exportProgress = value; OnPropertyChanged(); }
    }

    public string ExportStatus
    {
        get => _exportStatus;
        set { _exportStatus = value; OnPropertyChanged(); }
    }

    public bool CanExport => !IsExporting && Video1 != null && Video2 != null && Video3 != null && Video4 != null;

    #endregion

    #region Comandos

    // Globales
    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand FrameBackwardCommand { get; }
    public ICommand FrameForwardCommand { get; }
    public ICommand SetSpeedCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ToggleModeCommand { get; }
    public ICommand SyncVideosCommand { get; }
    public ICommand ExportCommand { get; }

    // Video 1
    public ICommand PlayPauseCommand1 { get; }
    public ICommand SeekBackwardCommand1 { get; }
    public ICommand SeekForwardCommand1 { get; }

    // Video 2
    public ICommand PlayPauseCommand2 { get; }
    public ICommand SeekBackwardCommand2 { get; }
    public ICommand SeekForwardCommand2 { get; }

    // Video 3
    public ICommand PlayPauseCommand3 { get; }
    public ICommand SeekBackwardCommand3 { get; }
    public ICommand SeekForwardCommand3 { get; }

    // Video 4
    public ICommand PlayPauseCommand4 { get; }
    public ICommand SeekBackwardCommand4 { get; }
    public ICommand SeekForwardCommand4 { get; }

    #endregion

    #region Eventos

    // Globales
    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? StopRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? FrameForwardRequested;
    public event EventHandler? FrameBackwardRequested;
    public event EventHandler<double>? SpeedChanged;
    public event EventHandler? SyncRequested;
    public event EventHandler<bool>? ModeChanged;

    // Video 1
    public event EventHandler? PlayRequested1;
    public event EventHandler? PauseRequested1;
    public event EventHandler<double>? SeekRequested1;

    // Video 2
    public event EventHandler? PlayRequested2;
    public event EventHandler? PauseRequested2;
    public event EventHandler<double>? SeekRequested2;

    // Video 3
    public event EventHandler? PlayRequested3;
    public event EventHandler? PauseRequested3;
    public event EventHandler<double>? SeekRequested3;

    // Video 4
    public event EventHandler? PlayRequested4;
    public event EventHandler? PauseRequested4;
    public event EventHandler<double>? SeekRequested4;

    #endregion

    #region Métodos públicos

    public void Initialize(VideoClip? video1, VideoClip? video2, VideoClip? video3, VideoClip? video4)
    {
        Video1 = video1;
        Video2 = video2;
        Video3 = video3;
        Video4 = video4;
        IsSimultaneousMode = false;
        ResetAllStates();
    }

    #endregion

    #region Export Methods

    private async Task ExportQuadVideosAsync()
    {
        if (Video1 == null || Video2 == null || Video3 == null || Video4 == null || IsExporting)
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

            // Crear nombre de archivo descriptivo
            var athlete1Name = Video1.Atleta?.NombreCompleto ?? "Atleta1";
            var athlete2Name = Video2.Atleta?.NombreCompleto ?? "Atleta2";
            var athlete3Name = Video3.Atleta?.NombreCompleto ?? "Atleta3";
            var athlete4Name = Video4.Atleta?.NombreCompleto ?? "Atleta4";
            var fileName = $"Cuádruple_{athlete1Name}_{athlete2Name}_{athlete3Name}_{athlete4Name}.mp4";
            // Limpiar caracteres inválidos del nombre
            fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            var outputPath = Path.Combine(outputFolder, fileName);

            // Si ya existe un archivo con ese nombre, añadir timestamp
            if (File.Exists(outputPath))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                fileName = $"Cuádruple_{timestamp}.mp4";
                fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
                outputPath = Path.Combine(outputFolder, fileName);
            }

            // Determinar las rutas correctas de los videos (preferir LocalClipPath)
            var video1Path = Video1.LocalClipPath ?? Video1.ClipPath ?? string.Empty;
            var video2Path = Video2.LocalClipPath ?? Video2.ClipPath ?? string.Empty;
            var video3Path = Video3.LocalClipPath ?? Video3.ClipPath ?? string.Empty;
            var video4Path = Video4.LocalClipPath ?? Video4.ClipPath ?? string.Empty;

            System.Diagnostics.Debug.WriteLine($"[Export] Video1Path: {video1Path}");
            System.Diagnostics.Debug.WriteLine($"[Export] Video2Path: {video2Path}");
            System.Diagnostics.Debug.WriteLine($"[Export] Video3Path: {video3Path}");
            System.Diagnostics.Debug.WriteLine($"[Export] Video4Path: {video4Path}");

            // Configurar parámetros de exportación
            var exportParams = new QuadVideoExportParams
            {
                Video1Path = video1Path,
                Video2Path = video2Path,
                Video3Path = video3Path,
                Video4Path = video4Path,
                Video1StartPosition = CurrentPosition1,
                Video2StartPosition = CurrentPosition2,
                Video3StartPosition = CurrentPosition3,
                Video4StartPosition = CurrentPosition4,
                Video1AthleteName = Video1.Atleta?.NombreCompleto,
                Video1Category = Video1.Atleta?.CategoriaNombre,
                Video1Section = Video1.Section,
                Video2AthleteName = Video2.Atleta?.NombreCompleto,
                Video2Category = Video2.Atleta?.CategoriaNombre,
                Video2Section = Video2.Section,
                Video3AthleteName = Video3.Atleta?.NombreCompleto,
                Video3Category = Video3.Atleta?.CategoriaNombre,
                Video3Section = Video3.Section,
                Video4AthleteName = Video4.Atleta?.NombreCompleto,
                Video4Category = Video4.Atleta?.CategoriaNombre,
                Video4Section = Video4.Section,
                OutputPath = outputPath
            };

            ExportStatus = "Componiendo vídeos...";

            // Ejecutar la exportación
            var result = await compositionService.ExportQuadVideosAsync(
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
                    ComparisonName = $"Cuádruple: {Video1.Atleta?.NombreCompleto ?? "1"}, {Video2.Atleta?.NombreCompleto ?? "2"}, {Video3.Atleta?.NombreCompleto ?? "3"}, {Video4.Atleta?.NombreCompleto ?? "4"}"
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

    #region Métodos públicos

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

    public void SeekToPosition3(double position)
    {
        var newPosition = position * Duration3.TotalSeconds;
        SeekRequested3?.Invoke(this, newPosition);
    }

    public void SeekToPosition4(double position)
    {
        var newPosition = position * Duration4.TotalSeconds;
        SeekRequested4?.Invoke(this, newPosition);
    }

    public void UpdatePositionFromPage(TimeSpan position)
    {
        _currentPosition = position;
        OnPropertyChanged(nameof(CurrentPosition));
        OnPropertyChanged(nameof(CurrentPositionText));
        UpdateProgress();
    }

    public void UpdateDurationFromPage(TimeSpan duration)
    {
        _duration = duration;
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DurationText));
        UpdateProgress();
    }

    public void SetPlayingState(bool isPlaying)
    {
        _isPlaying = isPlaying;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseIcon));
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
        if (Duration.TotalSeconds > 0)
            Progress = CurrentPosition.TotalSeconds / Duration.TotalSeconds;
        else
            Progress = 0;
    }

    private void ToggleMode()
    {
        IsSimultaneousMode = !IsSimultaneousMode;
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

    private void Seek1(double seconds)
    {
        var newPosition = CurrentPosition1.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration1.TotalSeconds));
        SeekRequested1?.Invoke(this, newPosition);
    }

    private void UpdateProgress1()
    {
        if (_isDragging1) return;
        
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

    private void Seek2(double seconds)
    {
        var newPosition = CurrentPosition2.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration2.TotalSeconds));
        SeekRequested2?.Invoke(this, newPosition);
    }

    private void UpdateProgress2()
    {
        if (_isDragging2) return;
        
        if (Duration2.TotalSeconds > 0)
            Progress2 = CurrentPosition2.TotalSeconds / Duration2.TotalSeconds;
        else
            Progress2 = 0;
    }

    #endregion

    #region Métodos privados - Video 3

    private void TogglePlayPause3()
    {
        IsPlaying3 = !IsPlaying3;
        if (IsPlaying3)
            PlayRequested3?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested3?.Invoke(this, EventArgs.Empty);
    }

    private void Seek3(double seconds)
    {
        var newPosition = CurrentPosition3.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration3.TotalSeconds));
        SeekRequested3?.Invoke(this, newPosition);
    }

    private void UpdateProgress3()
    {
        if (_isDragging3) return;
        
        if (Duration3.TotalSeconds > 0)
            Progress3 = CurrentPosition3.TotalSeconds / Duration3.TotalSeconds;
        else
            Progress3 = 0;
    }

    #endregion

    #region Métodos privados - Video 4

    private void TogglePlayPause4()
    {
        IsPlaying4 = !IsPlaying4;
        if (IsPlaying4)
            PlayRequested4?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested4?.Invoke(this, EventArgs.Empty);
    }

    private void Seek4(double seconds)
    {
        var newPosition = CurrentPosition4.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, Duration4.TotalSeconds));
        SeekRequested4?.Invoke(this, newPosition);
    }

    private void UpdateProgress4()
    {
        if (_isDragging4) return;
        
        if (Duration4.TotalSeconds > 0)
            Progress4 = CurrentPosition4.TotalSeconds / Duration4.TotalSeconds;
        else
            Progress4 = 0;
    }

    #endregion

    #region Métodos privados - Comunes

    private void SetSpeed(string? speedStr)
    {
        if (double.TryParse(speedStr, System.Globalization.NumberStyles.Any, 
            System.Globalization.CultureInfo.InvariantCulture, out var speed))
        {
            PlaybackSpeed = speed;
        }
    }

    private void SyncVideos()
    {
        SyncRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task CloseAsync()
    {
        IsPlaying = false;
        StopRequested?.Invoke(this, EventArgs.Empty);
        await Shell.Current.Navigation.PopAsync();
    }

    private void ResetAllStates()
    {
        IsPlaying = false;
        CurrentPosition = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        Progress = 0;
        
        IsPlaying1 = false;
        CurrentPosition1 = TimeSpan.Zero;
        Duration1 = TimeSpan.Zero;
        Progress1 = 0;
        
        IsPlaying2 = false;
        CurrentPosition2 = TimeSpan.Zero;
        Duration2 = TimeSpan.Zero;
        Progress2 = 0;
        
        IsPlaying3 = false;
        CurrentPosition3 = TimeSpan.Zero;
        Duration3 = TimeSpan.Zero;
        Progress3 = 0;
        
        IsPlaying4 = false;
        CurrentPosition4 = TimeSpan.Zero;
        Duration4 = TimeSpan.Zero;
        Progress4 = 0;
        
        SyncPoint1 = TimeSpan.Zero;
        SyncPoint2 = TimeSpan.Zero;
        SyncPoint3 = TimeSpan.Zero;
        SyncPoint4 = TimeSpan.Zero;
        SyncDuration = TimeSpan.Zero;
        PlaybackSpeed = 1.0;
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? string.Format("{0}:{1:D2}:{2:D2}", (int)time.TotalHours, time.Minutes, time.Seconds)
            : string.Format("{0:D2}:{1:D2}", time.Minutes, time.Seconds);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
