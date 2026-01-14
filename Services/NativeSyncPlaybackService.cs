#if MACCATALYST || IOS
using AVFoundation;
using CoreMedia;
using CrownRFEP_Reader.Controls;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Implementación nativa de sincronización de players usando AVPlayer.
/// Usa setRate:time:atHostTime: para sincronización precisa de frame.
/// Cada player mantiene su posición actual pero todos arrancan simultáneamente.
/// </summary>
public static class NativeSyncPlaybackService
{
    /// <summary>
    /// Obtiene el AVPlayer nativo de un PrecisionVideoPlayer
    /// </summary>
    private static AVPlayer? GetNativePlayer(PrecisionVideoPlayer player)
    {
        if (player.Handler is Handlers.PrecisionVideoPlayerHandler handler)
        {
            return handler.NativePlayer;
        }
        return null;
    }

    /// <summary>
    /// Inicia reproducción sincronizada de múltiples players usando el reloj del host.
    /// Cada player comienza desde su posición actual, pero todos arrancan exactamente
    /// en el mismo instante del reloj del sistema.
    /// </summary>
    public static void PlaySynchronized(PrecisionVideoPlayer[] players, double speed)
    {
        if (players.Length == 0) return;

        var playerData = new List<(AVPlayer player, CMTime currentPosition)>();
        
        foreach (var player in players)
        {
            var nativePlayer = GetNativePlayer(player);
            if (nativePlayer != null)
            {
                // Obtener la posición actual de cada player
                var currentTime = nativePlayer.CurrentTime;
                playerData.Add((nativePlayer, currentTime));
            }
        }

        if (playerData.Count == 0) return;

        // Calcular el tiempo de host futuro para sincronizar
        // 50ms en el futuro da tiempo a que todos los players estén listos
        var hostTime = CMClock.HostTimeClock.CurrentTime;
        var syncTime = CMTime.Add(hostTime, CMTime.FromSeconds(0.05, 1_000_000_000));

        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var (nativePlayer, currentPosition) in playerData)
            {
                try
                {
                    // setRate:time:atHostTime: puede fallar con NSException si AutomaticallyWaitsToMinimizeStalling==true
                    // (error: "cannot service synchronized playback request...").
                    nativePlayer.AutomaticallyWaitsToMinimizeStalling = false;

                    // setRate:time:atHostTime: sincroniza todos los players
                    // Cada uno usa SU posición actual, pero todos arrancan en syncTime
                    nativePlayer.SetRate(
                        (float)speed,
                        currentPosition,  // Posición actual de ESTE player
                        syncTime          // Mismo instante de arranque para TODOS
                    );
                }
                catch (Exception ex)
                {
                    AppLog.Error("NativeSyncPlaybackService", $"Error setting synchronized playback", ex);
                    // Fallback: reproducción normal
                    nativePlayer.Play();
                    nativePlayer.Rate = (float)speed;
                }
            }
        });
    }

    /// <summary>
    /// Pausa todos los players simultáneamente
    /// </summary>
    public static void PauseSynchronized(PrecisionVideoPlayer[] players)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var player in players)
            {
                var nativePlayer = GetNativePlayer(player);
                nativePlayer?.Pause();
            }
        });
    }

    /// <summary>
    /// Hace seek sincronizado en todos los players a la misma posición
    /// </summary>
    public static void SeekSynchronized(PrecisionVideoPlayer[] players, TimeSpan position)
    {
        var cmTime = CMTime.FromSeconds(position.TotalSeconds, 600);
        var seekTasks = new List<TaskCompletionSource<bool>>();

        foreach (var player in players)
        {
            var nativePlayer = GetNativePlayer(player);
            if (nativePlayer != null)
            {
                var tcs = new TaskCompletionSource<bool>();
                seekTasks.Add(tcs);

                nativePlayer.Seek(cmTime, CMTime.Zero, CMTime.Zero, finished =>
                {
                    tcs.TrySetResult(finished);
                });
            }
        }

        Task.Run(async () =>
        {
            await Task.WhenAll(seekTasks.Select(t => t.Task));
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var player in players)
                {
                    player.Position = position;
                }
            });
        });
    }
}
#elif WINDOWS
using Windows.Media.Playback;
using CrownRFEP_Reader.Controls;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Implementación nativa de sincronización de players usando MediaPlayer de Windows.
/// Usa MediaTimelineController para sincronización precisa entre múltiples players.
/// Cada player mantiene su posición actual pero todos arrancan simultáneamente.
/// </summary>
public static class NativeSyncPlaybackService
{
    private static Windows.Media.MediaTimelineController? _sharedTimelineController;
    private static readonly List<(MediaPlayer player, TimeSpan offset)> _synchronizedPlayers = new();
    private static bool _isControllerActive;

    /// <summary>
    /// Obtiene el MediaPlayer nativo de un PrecisionVideoPlayer
    /// </summary>
    private static MediaPlayer? GetNativePlayer(PrecisionVideoPlayer player)
    {
        if (player.Handler is Handlers.PrecisionVideoPlayerHandler handler)
        {
            return handler.NativePlayer;
        }
        return null;
    }

    /// <summary>
    /// Inicia reproducción sincronizada de múltiples players usando MediaTimelineController.
    /// Cada player comienza desde su posición actual, pero todos arrancan exactamente
    /// al mismo tiempo gracias al controller compartido.
    /// </summary>
    public static void PlaySynchronized(PrecisionVideoPlayer[] players, double speed)
    {
        if (players.Length == 0) return;

        var playerData = new List<(MediaPlayer player, TimeSpan currentPosition)>();
        
        foreach (var player in players)
        {
            var nativePlayer = GetNativePlayer(player);
            if (nativePlayer != null)
            {
                // Obtener la posición actual de cada player
                var currentPosition = nativePlayer.PlaybackSession.Position;
                playerData.Add((nativePlayer, currentPosition));
            }
        }

        if (playerData.Count == 0) return;

        try
        {
            // Limpiar controller anterior si existe
            CleanupTimelineController();

            // Crear nuevo MediaTimelineController para sincronización
            _sharedTimelineController = new Windows.Media.MediaTimelineController();
            _sharedTimelineController.ClockRate = speed;
            _synchronizedPlayers.Clear();

            foreach (var (nativePlayer, currentPosition) in playerData)
            {
                // Pausar el player
                nativePlayer.Pause();
                
                // Asociar al timeline controller
                // El offset es la posición actual de ESTE player
                nativePlayer.TimelineController = _sharedTimelineController;
                nativePlayer.TimelineControllerPositionOffset = currentPosition;
                
                _synchronizedPlayers.Add((nativePlayer, currentPosition));
            }

            // Iniciar el controller desde cero - todos los players comenzarán
            // simultáneamente desde sus respectivas posiciones actuales
            _sharedTimelineController.Position = TimeSpan.Zero;
            _sharedTimelineController.Start();
            _isControllerActive = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NativeSyncPlaybackService] Error in synchronized play: {ex.Message}");
            
            // Fallback: reproducción secuencial normal
            CleanupTimelineController();
            foreach (var (nativePlayer, _) in playerData)
            {
                nativePlayer.Play();
                nativePlayer.PlaybackSession.PlaybackRate = speed;
            }
        }
    }

    /// <summary>
    /// Pausa todos los players simultáneamente
    /// </summary>
    public static void PauseSynchronized(PrecisionVideoPlayer[] players)
    {
        if (_isControllerActive && _sharedTimelineController != null)
        {
            try
            {
                _sharedTimelineController.Pause();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NativeSyncPlaybackService] Error pausing controller: {ex.Message}");
            }
        }
        else
        {
            foreach (var player in players)
            {
                var nativePlayer = GetNativePlayer(player);
                nativePlayer?.Pause();
            }
        }
    }

    /// <summary>
    /// Hace seek sincronizado en todos los players a la misma posición
    /// </summary>
    public static void SeekSynchronized(PrecisionVideoPlayer[] players, TimeSpan position)
    {
        if (_isControllerActive && _sharedTimelineController != null)
        {
            try
            {
                var wasPlaying = _sharedTimelineController.State == Windows.Media.MediaTimelineControllerState.Running;
                _sharedTimelineController.Pause();

                // Actualizar offsets para cada player a la nueva posición común
                foreach (var (nativePlayer, _) in _synchronizedPlayers)
                {
                    nativePlayer.TimelineControllerPositionOffset = position;
                }

                _sharedTimelineController.Position = TimeSpan.Zero;
                
                if (wasPlaying)
                {
                    _sharedTimelineController.Start();
                }

                foreach (var player in players)
                {
                    player.Position = position;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NativeSyncPlaybackService] Error seeking: {ex.Message}");
                FallbackSeek(players, position);
            }
        }
        else
        {
            FallbackSeek(players, position);
        }
    }

    private static void FallbackSeek(PrecisionVideoPlayer[] players, TimeSpan position)
    {
        foreach (var player in players)
        {
            var nativePlayer = GetNativePlayer(player);
            if (nativePlayer != null)
            {
                nativePlayer.PlaybackSession.Position = position;
            }
            player.Position = position;
        }
    }

    /// <summary>
    /// Limpia el controller de timeline y desasocia los players
    /// </summary>
    public static void CleanupTimelineController()
    {
        if (_sharedTimelineController != null)
        {
            try
            {
                _sharedTimelineController.Pause();
            }
            catch { }

            foreach (var (player, _) in _synchronizedPlayers)
            {
                try
                {
                    player.TimelineController = null;
                }
                catch { }
            }

            _synchronizedPlayers.Clear();
            _sharedTimelineController = null;
            _isControllerActive = false;
        }
    }
}
#endif
