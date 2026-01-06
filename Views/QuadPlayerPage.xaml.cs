using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.ViewModels;
using System.ComponentModel;

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#elif WINDOWS
using CrownRFEP_Reader.Platforms.Windows;
#endif

namespace CrownRFEP_Reader.Views;

public partial class QuadPlayerPage : ContentPage
{
    private readonly QuadPlayerViewModel _viewModel;

    // Lap-sync playback ("espera" en cada lap)
    private List<TimeSpan>? _lapSyncEnds1;
    private List<TimeSpan>? _lapSyncEnds2;
    private List<TimeSpan>? _lapSyncEnds3;
    private List<TimeSpan>? _lapSyncEnds4;
    private int _lapSyncSegmentIndex;
    private bool _lapSyncPaused1;
    private bool _lapSyncPaused2;
    private bool _lapSyncPaused3;
    private bool _lapSyncPaused4;
    private TimeSpan _lapSyncHoldPos1;
    private TimeSpan _lapSyncHoldPos2;
    private TimeSpan _lapSyncHoldPos3;
    private TimeSpan _lapSyncHoldPos4;
    private bool _lapSyncSeekInProgress;
    private bool _lapSyncLoading;
    private DateTime _lapSyncResumedAt;
    private TimeSpan _lapSyncResumePos1;
    private TimeSpan _lapSyncResumePos2;
    private TimeSpan _lapSyncResumePos3;
    private TimeSpan _lapSyncResumePos4;
    private static readonly TimeSpan LapSyncEpsilon = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan LapSyncResumeGrace = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan LapSyncMinAdvance = TimeSpan.FromMilliseconds(100);
    
    // Posiciones actuales para scrubbing
    private double _currentScrubPosition1;
    private double _currentScrubPosition2;
    private double _currentScrubPosition3;
    private double _currentScrubPosition4;
    
    // Flags para detectar arrastre de sliders
    private bool _isDragging1;
    private bool _isDragging2;
    private bool _isDragging3;
    private bool _isDragging4;
    private bool _isDraggingGlobal;
    private bool _wasPlayingBeforeDragGlobal;

#if WINDOWS
    // Throttle para seeks en Windows (evitar bloqueos)
    private DateTime _lastSeekTime1 = DateTime.MinValue;
    private DateTime _lastSeekTime2 = DateTime.MinValue;
    private DateTime _lastSeekTime3 = DateTime.MinValue;
    private DateTime _lastSeekTime4 = DateTime.MinValue;
    private const int SeekThrottleMs = 50; // milisegundos entre seeks
#endif

    public QuadPlayerPage(QuadPlayerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ModeChanged += OnModeChanged;

        // Suscribirse a eventos globales del ViewModel
        _viewModel.PlayRequested += OnPlayRequested;
        _viewModel.PauseRequested += OnPauseRequested;
        _viewModel.StopRequested += OnStopRequested;
        _viewModel.SeekRequested += OnSeekRequested;
        _viewModel.FrameForwardRequested += OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested += OnFrameBackwardRequested;
        _viewModel.SpeedChanged += OnSpeedChanged;
        _viewModel.SyncRequested += OnSyncRequested;

        // Suscribirse a eventos individuales
        _viewModel.PlayRequested1 += OnPlayRequested1;
        _viewModel.PauseRequested1 += OnPauseRequested1;
        _viewModel.SeekRequested1 += OnSeekRequested1;
        _viewModel.PlayRequested2 += OnPlayRequested2;
        _viewModel.PauseRequested2 += OnPauseRequested2;
        _viewModel.SeekRequested2 += OnSeekRequested2;
        _viewModel.PlayRequested3 += OnPlayRequested3;
        _viewModel.PauseRequested3 += OnPauseRequested3;
        _viewModel.SeekRequested3 += OnSeekRequested3;
        _viewModel.PlayRequested4 += OnPlayRequested4;
        _viewModel.PauseRequested4 += OnPauseRequested4;
        _viewModel.SeekRequested4 += OnSeekRequested4;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SetupMediaOpenedHandlers();
        
        // Suscribirse a eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated += OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded += OnScrubEnded;

#if MACCATALYST || WINDOWS
#if WINDOWS
        KeyPressHandler.EnsureAttached();
#endif
        KeyPressHandler.SpaceBarPressed += OnSpaceBarPressed;
        KeyPressHandler.ArrowLeftPressed += OnArrowLeftPressed;
        KeyPressHandler.ArrowRightPressed += OnArrowRightPressed;
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Desuscribirse de eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated -= OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded -= OnScrubEnded;

#if MACCATALYST || WINDOWS
        KeyPressHandler.SpaceBarPressed -= OnSpaceBarPressed;
        KeyPressHandler.ArrowLeftPressed -= OnArrowLeftPressed;
        KeyPressHandler.ArrowRightPressed -= OnArrowRightPressed;
#endif
        
        CleanupResources();
    }

#if MACCATALYST || WINDOWS
    private void OnSpaceBarPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => _viewModel.PlayPauseCommand.Execute(null));
    }

    private void OnArrowLeftPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => _viewModel.FrameBackwardCommand.Execute(null));
    }

    private void OnArrowRightPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => _viewModel.FrameForwardCommand.Execute(null));
    }
#endif

    private void CleanupResources()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ModeChanged -= OnModeChanged;
        // Desuscribirse de eventos globales del ViewModel
        _viewModel.PlayRequested -= OnPlayRequested;
        _viewModel.PauseRequested -= OnPauseRequested;
        _viewModel.StopRequested -= OnStopRequested;
        _viewModel.SeekRequested -= OnSeekRequested;
        _viewModel.FrameForwardRequested -= OnFrameForwardRequested;
        _viewModel.FrameBackwardRequested -= OnFrameBackwardRequested;
        _viewModel.SpeedChanged -= OnSpeedChanged;
        _viewModel.SyncRequested -= OnSyncRequested;

        // Desuscribirse de eventos individuales
        _viewModel.PlayRequested1 -= OnPlayRequested1;
        _viewModel.PauseRequested1 -= OnPauseRequested1;
        _viewModel.SeekRequested1 -= OnSeekRequested1;
        _viewModel.PlayRequested2 -= OnPlayRequested2;
        _viewModel.PauseRequested2 -= OnPauseRequested2;
        _viewModel.SeekRequested2 -= OnSeekRequested2;
        _viewModel.PlayRequested3 -= OnPlayRequested3;
        _viewModel.PauseRequested3 -= OnPauseRequested3;
        _viewModel.SeekRequested3 -= OnSeekRequested3;
        _viewModel.PlayRequested4 -= OnPlayRequested4;
        _viewModel.PauseRequested4 -= OnPauseRequested4;
        _viewModel.SeekRequested4 -= OnSeekRequested4;

        // Limpiar handlers de MediaOpened
        MediaPlayer1.MediaOpened -= OnMediaOpened;
        MediaPlayer2.MediaOpened -= OnMediaOpened;
        MediaPlayer3.MediaOpened -= OnMediaOpened;
        MediaPlayer4.MediaOpened -= OnMediaOpened;

        // Detener todos los reproductores
        StopAllPlayers();

        ResetLapSyncState();
    }

    private void ResetLapSyncState()
    {
        _lapSyncEnds1 = null;
        _lapSyncEnds2 = null;
        _lapSyncEnds3 = null;
        _lapSyncEnds4 = null;
        _lapSyncSegmentIndex = -1;
        _lapSyncPaused1 = false;
        _lapSyncPaused2 = false;
        _lapSyncPaused3 = false;
        _lapSyncPaused4 = false;
        _lapSyncHoldPos1 = TimeSpan.Zero;
        _lapSyncHoldPos2 = TimeSpan.Zero;
        _lapSyncHoldPos3 = TimeSpan.Zero;
        _lapSyncHoldPos4 = TimeSpan.Zero;
        _lapSyncSeekInProgress = false;
        _lapSyncLoading = false;
        _lapSyncResumedAt = DateTime.MinValue;
        _lapSyncResumePos1 = TimeSpan.Zero;
        _lapSyncResumePos2 = TimeSpan.Zero;
        _lapSyncResumePos3 = TimeSpan.Zero;
        _lapSyncResumePos4 = TimeSpan.Zero;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuadPlayerViewModel.IsLapSyncEnabled))
        {
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                ResetLapSyncState();
                if (_viewModel.IsLapSyncEnabled && _viewModel.IsSimultaneousMode)
                {
                    await EnsureLapSyncDataAsync();
                    SeekAllToPosition(0);
                }
            });
        }
    }

    private void OnModeChanged(object? sender, bool isSimultaneous)
    {
        if (!isSimultaneous)
        {
            ResetLapSyncState();
            return;
        }

        if (_viewModel.IsLapSyncEnabled)
        {
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                ResetLapSyncState();
                await EnsureLapSyncDataAsync();
                SeekAllToPosition(0);
            });
        }
    }

    private sealed record LapSegment(TimeSpan Start, TimeSpan End);

    private static List<LapSegment>? BuildLapSegments(List<ExecutionTimingEvent> events, TimeSpan fallbackEnd)
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
            segments.Add(new LapSegment(segStart, segEnd));
        }

        return segments.Count > 0 ? segments : null;
    }

    private static TimeSpan GetFallbackDuration(VideoClip? clip, TimeSpan durationProperty)
    {
        if (durationProperty > TimeSpan.Zero)
            return durationProperty;
        if (clip != null && clip.ClipDuration > 0)
            return TimeSpan.FromSeconds(clip.ClipDuration);
        return TimeSpan.Zero;
    }

    private async Task EnsureLapSyncDataAsync()
    {
        if (_lapSyncLoading)
            return;
        if (_lapSyncEnds1 != null || _lapSyncEnds2 != null || _lapSyncEnds3 != null || _lapSyncEnds4 != null)
            return;

        _lapSyncLoading = true;
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            var databaseService = services?.GetService<DatabaseService>();
            if (databaseService == null)
                return;

            async Task<(List<TimeSpan>? Ends, TimeSpan? Start, TimeSpan? End)> LoadEndsAsync(VideoClip? clip, TimeSpan durationProp)
            {
                if (clip == null)
                    return (null, null, null);

                var events = await databaseService.GetExecutionTimingEventsByVideoAsync(clip.Id);
                var fallbackEnd = GetFallbackDuration(clip, durationProp);
                var segs = BuildLapSegments(events, fallbackEnd);
                if (segs == null || segs.Count == 0)
                    return (null, null, null);

                var ends = segs.Select(s => s.End).ToList();
                if (ends.Count > 0)
                    ends.RemoveAt(ends.Count - 1);

                return (ends.Count > 0 ? ends : null, segs[0].Start, segs[^1].End);
            }

            var r1 = await LoadEndsAsync(_viewModel.Video1, _viewModel.Duration1);
            var r2 = await LoadEndsAsync(_viewModel.Video2, _viewModel.Duration2);
            var r3 = await LoadEndsAsync(_viewModel.Video3, _viewModel.Duration3);
            var r4 = await LoadEndsAsync(_viewModel.Video4, _viewModel.Duration4);

            _lapSyncEnds1 = r1.Ends;
            _lapSyncEnds2 = r2.Ends;
            _lapSyncEnds3 = r3.Ends;
            _lapSyncEnds4 = r4.Ends;

            // Anclar SyncPoints a Inicio si hay datos
            if (r1.Start.HasValue) _viewModel.SyncPoint1 = r1.Start.Value;
            if (r2.Start.HasValue) _viewModel.SyncPoint2 = r2.Start.Value;
            if (r3.Start.HasValue) _viewModel.SyncPoint3 = r3.Start.Value;
            if (r4.Start.HasValue) _viewModel.SyncPoint4 = r4.Start.Value;

            // Duración global: mínimo de lo reproducible desde cada inicio
            var remaining = new List<TimeSpan>();
            if (r1.End.HasValue && r1.Start.HasValue) remaining.Add(r1.End.Value - r1.Start.Value);
            if (r2.End.HasValue && r2.Start.HasValue) remaining.Add(r2.End.Value - r2.Start.Value);
            if (r3.End.HasValue && r3.Start.HasValue) remaining.Add(r3.End.Value - r3.Start.Value);
            if (r4.End.HasValue && r4.Start.HasValue) remaining.Add(r4.End.Value - r4.Start.Value);
            if (remaining.Count > 0)
            {
                _viewModel.SyncDuration = remaining.Min();
                _viewModel.Duration = _viewModel.SyncDuration;
            }
        }
        catch
        {
            // No bloquear el reproductor
        }
        finally
        {
            _lapSyncLoading = false;
        }
    }

    private static int FindSegmentIndexByEnds(List<TimeSpan> ends, TimeSpan position)
    {
        for (int i = 0; i < ends.Count; i++)
        {
            if (position < ends[i] - LapSyncEpsilon)
                return i;
        }
        return Math.Max(0, ends.Count - 1);
    }

    private void RecalculateLapSyncSegmentIndex()
    {
        var indices = new List<int>();

        if (_viewModel.HasVideo1 && _lapSyncEnds1 != null && _lapSyncEnds1.Count > 0)
            indices.Add(FindSegmentIndexByEnds(_lapSyncEnds1, MediaPlayer1.Position));
        if (_viewModel.HasVideo2 && _lapSyncEnds2 != null && _lapSyncEnds2.Count > 0)
            indices.Add(FindSegmentIndexByEnds(_lapSyncEnds2, MediaPlayer2.Position));
        if (_viewModel.HasVideo3 && _lapSyncEnds3 != null && _lapSyncEnds3.Count > 0)
            indices.Add(FindSegmentIndexByEnds(_lapSyncEnds3, MediaPlayer3.Position));
        if (_viewModel.HasVideo4 && _lapSyncEnds4 != null && _lapSyncEnds4.Count > 0)
            indices.Add(FindSegmentIndexByEnds(_lapSyncEnds4, MediaPlayer4.Position));

        _lapSyncSegmentIndex = indices.Count > 0 ? indices.Min() : 0;
    }

    private static TimeSpan GetTargetEnd(List<TimeSpan>? ends, int index)
    {
        if (ends == null || ends.Count == 0)
            return TimeSpan.Zero;
        return ends[Math.Min(index, ends.Count - 1)];
    }

    private void HandleLapSyncTick()
    {
        if (!_viewModel.IsSimultaneousMode || !_viewModel.IsLapSyncEnabled || !_viewModel.IsPlaying)
            return;
        if (_lapSyncSeekInProgress || _isDraggingGlobal || _isDragging1 || _isDragging2 || _isDragging3 || _isDragging4)
            return;

        if (_lapSyncEnds1 == null && _lapSyncEnds2 == null && _lapSyncEnds3 == null && _lapSyncEnds4 == null)
            return;

        // Tras reanudar, dar un pequeño margen de tiempo para que la posición avance.
        if (_lapSyncResumedAt != DateTime.MinValue && (DateTime.UtcNow - _lapSyncResumedAt) < LapSyncResumeGrace)
            return;

        // Además, exigir que todos hayan avanzado un mínimo desde la posición de resume.
        if (_lapSyncResumePos1 != TimeSpan.Zero || _lapSyncResumePos2 != TimeSpan.Zero ||
            _lapSyncResumePos3 != TimeSpan.Zero || _lapSyncResumePos4 != TimeSpan.Zero)
        {
            var pos1 = MediaPlayer1.Position;
            var pos2 = MediaPlayer2.Position;
            var pos3 = MediaPlayer3.Position;
            var pos4 = MediaPlayer4.Position;

            var adv1 = !_viewModel.HasVideo1 || (pos1 - _lapSyncResumePos1) >= LapSyncMinAdvance;
            var adv2 = !_viewModel.HasVideo2 || (pos2 - _lapSyncResumePos2) >= LapSyncMinAdvance;
            var adv3 = !_viewModel.HasVideo3 || (pos3 - _lapSyncResumePos3) >= LapSyncMinAdvance;
            var adv4 = !_viewModel.HasVideo4 || (pos4 - _lapSyncResumePos4) >= LapSyncMinAdvance;

            if (!(adv1 && adv2 && adv3 && adv4))
                return;

            // Una vez avanzados, limpiar.
            _lapSyncResumePos1 = TimeSpan.Zero;
            _lapSyncResumePos2 = TimeSpan.Zero;
            _lapSyncResumePos3 = TimeSpan.Zero;
            _lapSyncResumePos4 = TimeSpan.Zero;
        }

        // Ajustar el segmento actual si venimos de un seek/salto (índice no inicializado).
        if (_lapSyncSegmentIndex < 0)
            RecalculateLapSyncSegmentIndex();

        // Laps comunes (mínimo número de marcadores entre los vídeos activos)
        var counts = new List<int>();
        if (_viewModel.HasVideo1 && _lapSyncEnds1 != null) counts.Add(_lapSyncEnds1.Count);
        if (_viewModel.HasVideo2 && _lapSyncEnds2 != null) counts.Add(_lapSyncEnds2.Count);
        if (_viewModel.HasVideo3 && _lapSyncEnds3 != null) counts.Add(_lapSyncEnds3.Count);
        if (_viewModel.HasVideo4 && _lapSyncEnds4 != null) counts.Add(_lapSyncEnds4.Count);

        var maxIdx = (counts.Count > 0 ? counts.Min() : 0) - 1;
        if (maxIdx < 0)
            return;
        if (_lapSyncSegmentIndex > maxIdx)
            return;

        var idx = Math.Max(0, Math.Min(_lapSyncSegmentIndex, maxIdx));

        var target1 = GetTargetEnd(_lapSyncEnds1, idx);
        var target2 = GetTargetEnd(_lapSyncEnds2, idx);
        var target3 = GetTargetEnd(_lapSyncEnds3, idx);
        var target4 = GetTargetEnd(_lapSyncEnds4, idx);

        // Si ya hemos pausado un player por lap-sync, usamos la posición objetivo guardada,
        // para evitar deadlocks cuando Position tarda en reflejar el Seek.
        var effective1 = _lapSyncPaused1 ? _lapSyncHoldPos1 : MediaPlayer1.Position;
        var effective2 = _lapSyncPaused2 ? _lapSyncHoldPos2 : MediaPlayer2.Position;
        var effective3 = _lapSyncPaused3 ? _lapSyncHoldPos3 : MediaPlayer3.Position;
        var effective4 = _lapSyncPaused4 ? _lapSyncHoldPos4 : MediaPlayer4.Position;

        var reached1 = !_viewModel.HasVideo1 || _lapSyncPaused1 || effective1 >= target1 - LapSyncEpsilon;
        var reached2 = !_viewModel.HasVideo2 || _lapSyncPaused2 || effective2 >= target2 - LapSyncEpsilon;
        var reached3 = !_viewModel.HasVideo3 || _lapSyncPaused3 || effective3 >= target3 - LapSyncEpsilon;
        var reached4 = !_viewModel.HasVideo4 || _lapSyncPaused4 || effective4 >= target4 - LapSyncEpsilon;

        var allReached = reached1 && reached2 && reached3 && reached4;

        if (!allReached)
        {
            if (_viewModel.HasVideo1 && reached1 && !_lapSyncPaused1)
            {
                _lapSyncPaused1 = true;
                _lapSyncHoldPos1 = target1;
                MediaPlayer1.Pause();
                MediaPlayer1.SeekTo(target1);
            }
            if (_viewModel.HasVideo2 && reached2 && !_lapSyncPaused2)
            {
                _lapSyncPaused2 = true;
                _lapSyncHoldPos2 = target2;
                MediaPlayer2.Pause();
                MediaPlayer2.SeekTo(target2);
            }
            if (_viewModel.HasVideo3 && reached3 && !_lapSyncPaused3)
            {
                _lapSyncPaused3 = true;
                _lapSyncHoldPos3 = target3;
                MediaPlayer3.Pause();
                MediaPlayer3.SeekTo(target3);
            }
            if (_viewModel.HasVideo4 && reached4 && !_lapSyncPaused4)
            {
                _lapSyncPaused4 = true;
                _lapSyncHoldPos4 = target4;
                MediaPlayer4.Pause();
                MediaPlayer4.SeekTo(target4);
            }

            return;
        }

        _lapSyncSeekInProgress = true;
        try
        {
            if (_viewModel.HasVideo1) MediaPlayer1.SeekTo(target1);
            if (_viewModel.HasVideo2) MediaPlayer2.SeekTo(target2);
            if (_viewModel.HasVideo3) MediaPlayer3.SeekTo(target3);
            if (_viewModel.HasVideo4) MediaPlayer4.SeekTo(target4);

            _lapSyncPaused1 = false;
            _lapSyncPaused2 = false;
            _lapSyncPaused3 = false;
            _lapSyncPaused4 = false;

            _lapSyncHoldPos1 = TimeSpan.Zero;
            _lapSyncHoldPos2 = TimeSpan.Zero;
            _lapSyncHoldPos3 = TimeSpan.Zero;
            _lapSyncHoldPos4 = TimeSpan.Zero;

            _lapSyncSegmentIndex = idx + 1;

            // Registrar cuándo reanudamos y desde qué posición, para ignorar detecciones inmediatas.
            _lapSyncResumedAt = DateTime.UtcNow;
            _lapSyncResumePos1 = target1;
            _lapSyncResumePos2 = target2;
            _lapSyncResumePos3 = target3;
            _lapSyncResumePos4 = target4;

            PlayAllPlayers();
        }
        finally
        {
            _lapSyncSeekInProgress = false;
        }
    }

    private void SetupMediaOpenedHandlers()
    {
        MediaPlayer1.MediaOpened += OnMediaOpened;
        MediaPlayer2.MediaOpened += OnMediaOpened;
        MediaPlayer3.MediaOpened += OnMediaOpened;
        MediaPlayer4.MediaOpened += OnMediaOpened;
        
        // Configurar timer para actualizar posición
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += OnTimerTick;
        timer.Start();
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Actualizar duraciones individuales
            if (sender == MediaPlayer1 && MediaPlayer1.Duration > TimeSpan.Zero)
                _viewModel.Duration1 = MediaPlayer1.Duration;
            else if (sender == MediaPlayer2 && MediaPlayer2.Duration > TimeSpan.Zero)
                _viewModel.Duration2 = MediaPlayer2.Duration;
            else if (sender == MediaPlayer3 && MediaPlayer3.Duration > TimeSpan.Zero)
                _viewModel.Duration3 = MediaPlayer3.Duration;
            else if (sender == MediaPlayer4 && MediaPlayer4.Duration > TimeSpan.Zero)
                _viewModel.Duration4 = MediaPlayer4.Duration;

            // Usar la duración más larga entre todos los videos para el modo global
            var maxDuration = TimeSpan.Zero;
            if (MediaPlayer1.Duration > maxDuration) maxDuration = MediaPlayer1.Duration;
            if (MediaPlayer2.Duration > maxDuration) maxDuration = MediaPlayer2.Duration;
            if (MediaPlayer3.Duration > maxDuration) maxDuration = MediaPlayer3.Duration;
            if (MediaPlayer4.Duration > maxDuration) maxDuration = MediaPlayer4.Duration;
            
            _viewModel.UpdateDurationFromPage(maxDuration);
        });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Actualizar posiciones individuales siempre (excepto cuando se está arrastrando el slider)
            if (_viewModel.HasVideo1 && !_isDragging1)
            {
                _viewModel.CurrentPosition1 = MediaPlayer1.Position;
                // Actualizar slider directamente
                if (_viewModel.Duration1.TotalSeconds > 0)
                    ProgressSlider1.Value = MediaPlayer1.Position.TotalSeconds / _viewModel.Duration1.TotalSeconds;
            }
            if (_viewModel.HasVideo2 && !_isDragging2)
            {
                _viewModel.CurrentPosition2 = MediaPlayer2.Position;
                if (_viewModel.Duration2.TotalSeconds > 0)
                    ProgressSlider2.Value = MediaPlayer2.Position.TotalSeconds / _viewModel.Duration2.TotalSeconds;
            }
            if (_viewModel.HasVideo3 && !_isDragging3)
            {
                _viewModel.CurrentPosition3 = MediaPlayer3.Position;
                if (_viewModel.Duration3.TotalSeconds > 0)
                    ProgressSlider3.Value = MediaPlayer3.Position.TotalSeconds / _viewModel.Duration3.TotalSeconds;
            }
            if (_viewModel.HasVideo4 && !_isDragging4)
            {
                _viewModel.CurrentPosition4 = MediaPlayer4.Position;
                if (_viewModel.Duration4.TotalSeconds > 0)
                    ProgressSlider4.Value = MediaPlayer4.Position.TotalSeconds / _viewModel.Duration4.TotalSeconds;
            }

            // Actualizar posición global solo si está reproduciendo
            if (!_viewModel.IsPlaying || _isDraggingGlobal) return;

            // En modo simultáneo, usar posición relativa a SyncPoint (si aplica)
            var globalRelative = TimeSpan.Zero;
            if (_viewModel.IsSimultaneousMode)
            {
                if (_viewModel.HasVideo1)
                    globalRelative = TimeSpan.FromMilliseconds(Math.Max(0, (MediaPlayer1.Position - _viewModel.SyncPoint1).TotalMilliseconds));
                if (_viewModel.HasVideo2)
                    globalRelative = TimeSpan.FromMilliseconds(Math.Max(globalRelative.TotalMilliseconds, Math.Max(0, (MediaPlayer2.Position - _viewModel.SyncPoint2).TotalMilliseconds)));
                if (_viewModel.HasVideo3)
                    globalRelative = TimeSpan.FromMilliseconds(Math.Max(globalRelative.TotalMilliseconds, Math.Max(0, (MediaPlayer3.Position - _viewModel.SyncPoint3).TotalMilliseconds)));
                if (_viewModel.HasVideo4)
                    globalRelative = TimeSpan.FromMilliseconds(Math.Max(globalRelative.TotalMilliseconds, Math.Max(0, (MediaPlayer4.Position - _viewModel.SyncPoint4).TotalMilliseconds)));
            }
            else
            {
                // Modo individual: posición del primer video disponible
                if (_viewModel.HasVideo1 && MediaPlayer1.Position > TimeSpan.Zero)
                    globalRelative = MediaPlayer1.Position;
                else if (_viewModel.HasVideo2 && MediaPlayer2.Position > TimeSpan.Zero)
                    globalRelative = MediaPlayer2.Position;
                else if (_viewModel.HasVideo3 && MediaPlayer3.Position > TimeSpan.Zero)
                    globalRelative = MediaPlayer3.Position;
                else if (_viewModel.HasVideo4 && MediaPlayer4.Position > TimeSpan.Zero)
                    globalRelative = MediaPlayer4.Position;
            }

            _viewModel.UpdatePositionFromPage(globalRelative);

            if (_viewModel.Duration.TotalSeconds > 0)
                ProgressSlider.Value = globalRelative.TotalSeconds / _viewModel.Duration.TotalSeconds;

            // Aplicar "espera" por laps si procede
            HandleLapSyncTick();
        });
    }

    #region Global Slider Handling (Simultaneous Mode)

    private void OnProgressSliderDragStarted(object? sender, EventArgs e)
    {
        _isDraggingGlobal = true;
        _wasPlayingBeforeDragGlobal = _viewModel.IsPlaying;
        PauseAllPlayers();
    }

    private void OnProgressSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDraggingGlobal) return;
        
        // Actualizar texto de posición
        var totalSeconds = e.NewValue * _viewModel.Duration.TotalSeconds;
        var position = TimeSpan.FromSeconds(totalSeconds);
        _viewModel.UpdatePositionFromPage(position);
        
        // Hacer seek para mostrar preview durante el arrastre
        SeekAllToPosition(totalSeconds);
    }

    private void OnProgressSliderDragCompleted(object? sender, EventArgs e)
    {
        var position = ProgressSlider.Value * _viewModel.Duration.TotalSeconds;
        SeekAllToPosition(position);
        
        _isDraggingGlobal = false;
        
        if (_wasPlayingBeforeDragGlobal)
            PlayAllPlayers();
    }

    #endregion

    #region Scrub Handling

    private void OnScrubUpdated(object? sender, VideoScrubEventArgs e)
    {
        // En modo simultáneo, mover todos los reproductores
        if (_viewModel.IsSimultaneousMode)
        {
            OnScrubUpdatedSimultaneous(e);
            return;
        }
        
        // Modo individual: mover solo el reproductor correspondiente
        var player = GetPlayerForIndex(e.VideoIndex);
        if (player == null) return;

        if (e.IsStart)
        {
            // Guardar posición actual
            switch (e.VideoIndex)
            {
                case 1: _currentScrubPosition1 = player.Position.TotalMilliseconds; break;
                case 2: _currentScrubPosition2 = player.Position.TotalMilliseconds; break;
                case 3: _currentScrubPosition3 = player.Position.TotalMilliseconds; break;
                case 4: _currentScrubPosition4 = player.Position.TotalMilliseconds; break;
            }
            player.Pause();
        }
        else
        {
            // Aplicar delta
            ref double currentPos = ref _currentScrubPosition1;
            if (e.VideoIndex == 2) currentPos = ref _currentScrubPosition2;
            else if (e.VideoIndex == 3) currentPos = ref _currentScrubPosition3;
            else if (e.VideoIndex == 4) currentPos = ref _currentScrubPosition4;

            currentPos += e.DeltaMilliseconds;

            if (player.Duration != TimeSpan.Zero)
            {
                currentPos = Math.Max(0, Math.Min(currentPos, player.Duration.TotalMilliseconds));
                player.SeekTo(TimeSpan.FromMilliseconds(currentPos));
            }
        }
    }

    private void OnScrubUpdatedSimultaneous(VideoScrubEventArgs e)
    {
        if (e.IsStart)
        {
            // Guardar posiciones actuales de todos
            if (_viewModel.HasVideo1) _currentScrubPosition1 = MediaPlayer1.Position.TotalMilliseconds;
            if (_viewModel.HasVideo2) _currentScrubPosition2 = MediaPlayer2.Position.TotalMilliseconds;
            if (_viewModel.HasVideo3) _currentScrubPosition3 = MediaPlayer3.Position.TotalMilliseconds;
            if (_viewModel.HasVideo4) _currentScrubPosition4 = MediaPlayer4.Position.TotalMilliseconds;
            
            // Pausar todos
            PauseAllPlayers();
            _viewModel.SetPlayingState(false);
        }
        else
        {
            // Aplicar delta a todos los reproductores
            if (_viewModel.HasVideo1 && MediaPlayer1.Duration != TimeSpan.Zero)
            {
                _currentScrubPosition1 += e.DeltaMilliseconds;
                _currentScrubPosition1 = Math.Max(0, Math.Min(_currentScrubPosition1, MediaPlayer1.Duration.TotalMilliseconds));
                MediaPlayer1.SeekTo(TimeSpan.FromMilliseconds(_currentScrubPosition1));
            }
            if (_viewModel.HasVideo2 && MediaPlayer2.Duration != TimeSpan.Zero)
            {
                _currentScrubPosition2 += e.DeltaMilliseconds;
                _currentScrubPosition2 = Math.Max(0, Math.Min(_currentScrubPosition2, MediaPlayer2.Duration.TotalMilliseconds));
                MediaPlayer2.SeekTo(TimeSpan.FromMilliseconds(_currentScrubPosition2));
            }
            if (_viewModel.HasVideo3 && MediaPlayer3.Duration != TimeSpan.Zero)
            {
                _currentScrubPosition3 += e.DeltaMilliseconds;
                _currentScrubPosition3 = Math.Max(0, Math.Min(_currentScrubPosition3, MediaPlayer3.Duration.TotalMilliseconds));
                MediaPlayer3.SeekTo(TimeSpan.FromMilliseconds(_currentScrubPosition3));
            }
            if (_viewModel.HasVideo4 && MediaPlayer4.Duration != TimeSpan.Zero)
            {
                _currentScrubPosition4 += e.DeltaMilliseconds;
                _currentScrubPosition4 = Math.Max(0, Math.Min(_currentScrubPosition4, MediaPlayer4.Duration.TotalMilliseconds));
                MediaPlayer4.SeekTo(TimeSpan.FromMilliseconds(_currentScrubPosition4));
            }
            
            // Actualizar posición global en el ViewModel
            _viewModel.UpdatePositionFromPage(TimeSpan.FromMilliseconds(_currentScrubPosition1));
        }
    }

    private void OnScrubEnded(object? sender, VideoScrubEventArgs e)
    {
        // En modo simultáneo, pausar todos
        if (_viewModel.IsSimultaneousMode)
        {
            PauseAllPlayers();
            _viewModel.SetPlayingState(false);
            return;
        }
        
        // Modo individual: pausar solo el correspondiente
        var player = GetPlayerForIndex(e.VideoIndex);
        player?.Pause();
    }

    private PrecisionVideoPlayer? GetPlayerForIndex(int index)
    {
        return index switch
        {
            1 => MediaPlayer1,
            2 => MediaPlayer2,
            3 => MediaPlayer3,
            4 => MediaPlayer4,
            _ => null
        };
    }

    #endregion

    #region ViewModel Event Handlers

    private void OnPlayRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_viewModel.IsLapSyncEnabled && _viewModel.IsSimultaneousMode)
            {
                await EnsureLapSyncDataAsync();
                if (_lapSyncEnds1 != null || _lapSyncEnds2 != null || _lapSyncEnds3 != null || _lapSyncEnds4 != null)
                    RecalculateLapSyncSegmentIndex();
            }

            PlayAllPlayers();
        });
    }

    private void OnPauseRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PauseAllPlayers();
            _lapSyncPaused1 = false;
            _lapSyncPaused2 = false;
            _lapSyncPaused3 = false;
            _lapSyncPaused4 = false;
        });
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StopAllPlayers();
            SeekAllToPosition(0);
            ResetLapSyncState();
        });
    }

    private void OnSeekRequested(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => SeekAllToPosition(position));
    }

    private void OnFrameForwardRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PauseAllPlayers();
            StepAllForward();
        });
    }

    private void OnFrameBackwardRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PauseAllPlayers();
            StepAllBackward();
        });
    }

    private void OnSpeedChanged(object? sender, double speed)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MediaPlayer1.Speed = speed;
            MediaPlayer2.Speed = speed;
            MediaPlayer3.Speed = speed;
            MediaPlayer4.Speed = speed;
        });
    }

    private void OnSyncRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Sincronizar todos los videos a la posición del primero disponible
            var position = TimeSpan.Zero;
            if (_viewModel.HasVideo1)
                position = MediaPlayer1.Position;
            else if (_viewModel.HasVideo2)
                position = MediaPlayer2.Position;
            else if (_viewModel.HasVideo3)
                position = MediaPlayer3.Position;
            else if (_viewModel.HasVideo4)
                position = MediaPlayer4.Position;

            SeekAllToPosition(position.TotalSeconds);
        });
    }

    #endregion

    #region Player Control Methods

    private void PlayAllPlayers()
    {
        if (_viewModel.HasVideo1) MediaPlayer1.Play();
        if (_viewModel.HasVideo2) MediaPlayer2.Play();
        if (_viewModel.HasVideo3) MediaPlayer3.Play();
        if (_viewModel.HasVideo4) MediaPlayer4.Play();
    }

    private void PauseAllPlayers()
    {
        MediaPlayer1.Pause();
        MediaPlayer2.Pause();
        MediaPlayer3.Pause();
        MediaPlayer4.Pause();
    }

    private void StopAllPlayers()
    {
        MediaPlayer1.Stop();
        MediaPlayer2.Stop();
        MediaPlayer3.Stop();
        MediaPlayer4.Stop();
    }

    private void SeekAllToPosition(double seconds)
    {
        // Un seek rompe el estado de "espera"; se recalculará en el siguiente tick.
        _lapSyncSegmentIndex = 0;
        _lapSyncPaused1 = _lapSyncPaused2 = _lapSyncPaused3 = _lapSyncPaused4 = false;

        var relative = TimeSpan.FromSeconds(seconds);

        if (_viewModel.IsSimultaneousMode)
        {
            if (_viewModel.HasVideo1) MediaPlayer1.SeekTo(_viewModel.SyncPoint1 + relative);
            if (_viewModel.HasVideo2) MediaPlayer2.SeekTo(_viewModel.SyncPoint2 + relative);
            if (_viewModel.HasVideo3) MediaPlayer3.SeekTo(_viewModel.SyncPoint3 + relative);
            if (_viewModel.HasVideo4) MediaPlayer4.SeekTo(_viewModel.SyncPoint4 + relative);
        }
        else
        {
            if (_viewModel.HasVideo1) MediaPlayer1.SeekTo(relative);
            if (_viewModel.HasVideo2) MediaPlayer2.SeekTo(relative);
            if (_viewModel.HasVideo3) MediaPlayer3.SeekTo(relative);
            if (_viewModel.HasVideo4) MediaPlayer4.SeekTo(relative);
        }
    }

    private void StepAllForward()
    {
        if (_viewModel.HasVideo1) MediaPlayer1.StepForward();
        if (_viewModel.HasVideo2) MediaPlayer2.StepForward();
        if (_viewModel.HasVideo3) MediaPlayer3.StepForward();
        if (_viewModel.HasVideo4) MediaPlayer4.StepForward();
    }

    private void StepAllBackward()
    {
        if (_viewModel.HasVideo1) MediaPlayer1.StepBackward();
        if (_viewModel.HasVideo2) MediaPlayer2.StepBackward();
        if (_viewModel.HasVideo3) MediaPlayer3.StepBackward();
        if (_viewModel.HasVideo4) MediaPlayer4.StepBackward();
    }

    #endregion

    #region Individual Player Event Handlers

    // Video 1
    private void OnPlayRequested1(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer1.Play());
    }

    private void OnPauseRequested1(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer1.Pause());
    }

    private void OnSeekRequested1(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer1.SeekTo(TimeSpan.FromSeconds(position)));
    }

    // Video 2
    private void OnPlayRequested2(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer2.Play());
    }

    private void OnPauseRequested2(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer2.Pause());
    }

    private void OnSeekRequested2(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer2.SeekTo(TimeSpan.FromSeconds(position)));
    }

    // Video 3
    private void OnPlayRequested3(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer3.Play());
    }

    private void OnPauseRequested3(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer3.Pause());
    }

    private void OnSeekRequested3(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer3.SeekTo(TimeSpan.FromSeconds(position)));
    }

    // Video 4
    private void OnPlayRequested4(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer4.Play());
    }

    private void OnPauseRequested4(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer4.Pause());
    }

    private void OnSeekRequested4(object? sender, double position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaPlayer4.SeekTo(TimeSpan.FromSeconds(position)));
    }

    #endregion

    #region Individual Slider Handlers

    private bool _wasPlaying1BeforeDrag;
    private bool _wasPlaying2BeforeDrag;
    private bool _wasPlaying3BeforeDrag;
    private bool _wasPlaying4BeforeDrag;

    // Slider 1
    private void OnProgressSlider1DragStarted(object? sender, EventArgs e)
    {
        _isDragging1 = true;
        _viewModel.IsDragging1 = true;
        _wasPlaying1BeforeDrag = _viewModel.IsPlaying1;
        MediaPlayer1.Pause();
        _viewModel.IsPlaying1 = false;
    }

    private void OnProgressSlider1DragCompleted(object? sender, EventArgs e)
    {
        var position = ProgressSlider1.Value * _viewModel.Duration1.TotalSeconds;
        MediaPlayer1.SeekTo(TimeSpan.FromSeconds(position));
        
        _isDragging1 = false;
        _viewModel.IsDragging1 = false;
        
        if (_wasPlaying1BeforeDrag)
        {
            MediaPlayer1.Play();
            _viewModel.IsPlaying1 = true;
        }
    }

    // Slider 2
    private void OnProgressSlider2DragStarted(object? sender, EventArgs e)
    {
        _isDragging2 = true;
        _viewModel.IsDragging2 = true;
        _wasPlaying2BeforeDrag = _viewModel.IsPlaying2;
        MediaPlayer2.Pause();
        _viewModel.IsPlaying2 = false;
    }

    private void OnProgressSlider2DragCompleted(object? sender, EventArgs e)
    {
        var position = ProgressSlider2.Value * _viewModel.Duration2.TotalSeconds;
        MediaPlayer2.SeekTo(TimeSpan.FromSeconds(position));
        
        _isDragging2 = false;
        _viewModel.IsDragging2 = false;
        
        if (_wasPlaying2BeforeDrag)
        {
            MediaPlayer2.Play();
            _viewModel.IsPlaying2 = true;
        }
    }

    // Slider 3
    private void OnProgressSlider3DragStarted(object? sender, EventArgs e)
    {
        _isDragging3 = true;
        _viewModel.IsDragging3 = true;
        _wasPlaying3BeforeDrag = _viewModel.IsPlaying3;
        MediaPlayer3.Pause();
        _viewModel.IsPlaying3 = false;
    }

    private void OnProgressSlider3DragCompleted(object? sender, EventArgs e)
    {
        var position = ProgressSlider3.Value * _viewModel.Duration3.TotalSeconds;
        MediaPlayer3.SeekTo(TimeSpan.FromSeconds(position));
        
        _isDragging3 = false;
        _viewModel.IsDragging3 = false;
        
        if (_wasPlaying3BeforeDrag)
        {
            MediaPlayer3.Play();
            _viewModel.IsPlaying3 = true;
        }
    }

    // Slider 4
    private void OnProgressSlider4DragStarted(object? sender, EventArgs e)
    {
        _isDragging4 = true;
        _viewModel.IsDragging4 = true;
        _wasPlaying4BeforeDrag = _viewModel.IsPlaying4;
        MediaPlayer4.Pause();
        _viewModel.IsPlaying4 = false;
    }

    private void OnProgressSlider4DragCompleted(object? sender, EventArgs e)
    {
        var position = ProgressSlider4.Value * _viewModel.Duration4.TotalSeconds;
        MediaPlayer4.SeekTo(TimeSpan.FromSeconds(position));
        
        _isDragging4 = false;
        _viewModel.IsDragging4 = false;
        
        if (_wasPlaying4BeforeDrag)
        {
            MediaPlayer4.Play();
            _viewModel.IsPlaying4 = true;
        }
    }

    // ValueChanged handlers - seek en tiempo real mientras se arrastra
    private void OnProgressSlider1ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDragging1) return;
        var position = e.NewValue * _viewModel.Duration1.TotalSeconds;
        _viewModel.CurrentPosition1 = TimeSpan.FromSeconds(position);
#if WINDOWS
        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime1).TotalMilliseconds >= SeekThrottleMs)
        {
            _lastSeekTime1 = now;
            MediaPlayer1.SeekTo(TimeSpan.FromSeconds(position));
        }
#else
        // En MacCatalyst/iOS: hacer seek para mostrar el frame actual
        MediaPlayer1.SeekTo(TimeSpan.FromSeconds(position));
#endif
    }

    private void OnProgressSlider2ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDragging2) return;
        var position = e.NewValue * _viewModel.Duration2.TotalSeconds;
        _viewModel.CurrentPosition2 = TimeSpan.FromSeconds(position);
#if WINDOWS
        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime2).TotalMilliseconds >= SeekThrottleMs)
        {
            _lastSeekTime2 = now;
            MediaPlayer2.SeekTo(TimeSpan.FromSeconds(position));
        }
#else
        // En MacCatalyst/iOS: hacer seek para mostrar el frame actual
        MediaPlayer2.SeekTo(TimeSpan.FromSeconds(position));
#endif
    }

    private void OnProgressSlider3ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDragging3) return;
        var position = e.NewValue * _viewModel.Duration3.TotalSeconds;
        _viewModel.CurrentPosition3 = TimeSpan.FromSeconds(position);
#if WINDOWS
        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime3).TotalMilliseconds >= SeekThrottleMs)
        {
            _lastSeekTime3 = now;
            MediaPlayer3.SeekTo(TimeSpan.FromSeconds(position));
        }
#else
        // En MacCatalyst/iOS: hacer seek para mostrar el frame actual
        MediaPlayer3.SeekTo(TimeSpan.FromSeconds(position));
#endif
    }

    private void OnProgressSlider4ValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isDragging4) return;
        var position = e.NewValue * _viewModel.Duration4.TotalSeconds;
        _viewModel.CurrentPosition4 = TimeSpan.FromSeconds(position);
#if WINDOWS
        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime4).TotalMilliseconds >= SeekThrottleMs)
        {
            _lastSeekTime4 = now;
            MediaPlayer4.SeekTo(TimeSpan.FromSeconds(position));
        }
#else
        // En MacCatalyst/iOS: hacer seek para mostrar el frame actual
        MediaPlayer4.SeekTo(TimeSpan.FromSeconds(position));
#endif
    }

    #endregion
}
