#if MACCATALYST || IOS
using AVFoundation;
using CoreMedia;
using CrownRFEP_Reader.Controls;

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Implementación nativa de sincronización de players usando AVPlayer.
/// Usa setRate:time:atHostTime: para sincronización precisa de frame.
/// </summary>
public static class NativeSyncPlaybackService
{
    // Delay en nanosegundos para dar tiempo a que todos los players estén listos
    // 50ms es suficiente para que AVPlayer prepare los buffers
    private const long SyncDelayNanoseconds = 50_000_000; // 50ms

    /// <summary>
    /// Obtiene el AVPlayer nativo de un PrecisionVideoPlayer
    /// </summary>
    private static AVPlayer? GetNativePlayer(PrecisionVideoPlayer player)
    {
        // Acceder al handler para obtener el AVPlayer nativo
        if (player.Handler is Handlers.PrecisionVideoPlayerHandler handler)
        {
            return handler.NativePlayer;
        }
        return null;
    }

    /// <summary>
    /// Inicia reproducción sincronizada de múltiples players usando el reloj del host.
    /// Todos los players comenzarán a reproducir en el mismo instante exacto.
    /// </summary>
    public static void PlaySynchronized(PrecisionVideoPlayer[] players, TimeSpan startPosition, double speed)
    {
        if (players.Length == 0) return;

        var nativePlayers = new List<AVPlayer>();
        
        foreach (var player in players)
        {
            var nativePlayer = GetNativePlayer(player);
            if (nativePlayer != null)
            {
                nativePlayers.Add(nativePlayer);
            }
        }

        if (nativePlayers.Count == 0) return;

        // Primero, hacer seek preciso en todos los players y esperar
        var seekTasks = new List<TaskCompletionSource<bool>>();
        var cmStartTime = CMTime.FromSeconds(startPosition.TotalSeconds, 600);

        foreach (var nativePlayer in nativePlayers)
        {
            var tcs = new TaskCompletionSource<bool>();
            seekTasks.Add(tcs);

            nativePlayer.Seek(cmStartTime, CMTime.Zero, CMTime.Zero, finished =>
            {
                tcs.TrySetResult(finished);
            });
        }

        // Esperar a que todos los seeks completen, luego iniciar sincronizado
        Task.Run(async () =>
        {
            try
            {
                // Esperar a que todos los seeks terminen
                await Task.WhenAll(seekTasks.Select(t => t.Task));

                // Calcular el tiempo de host futuro para sincronizar
                // Usamos el tiempo actual del host + un pequeño delay
                var hostTime = CMClock.HostTimeClock.CurrentTime;
                var syncTime = CMTime.Add(hostTime, CMTime.FromSeconds(0.05, 1_000_000_000)); // 50ms en el futuro

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var nativePlayer in nativePlayers)
                    {
                        try
                        {
                            // setRate:time:atHostTime: sincroniza todos los players
                            // para comenzar a reproducir exactamente en syncTime
                            nativePlayer.SetRate(
                                (float)speed,
                                cmStartTime,
                                syncTime
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
            catch (Exception ex)
            {
                AppLog.Error("NativeSyncPlaybackService", $"Error during synchronized play setup", ex);
                
                // Fallback: reproducción secuencial normal
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var nativePlayer in nativePlayers)
                    {
                        nativePlayer.Play();
                        nativePlayer.Rate = (float)speed;
                    }
                });
            }
        });
    }

    /// <summary>
    /// Pausa todos los players simultáneamente
    /// </summary>
    public static void PauseSynchronized(PrecisionVideoPlayer[] players)
    {
        // Pause es inmediato, no requiere sincronización especial
        // pero lo hacemos en el main thread para garantizar orden
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
    /// Hace seek sincronizado en todos los players
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

        // Actualizar posiciones en los controles una vez completado
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
/// </summary>
public static class NativeSyncPlaybackService
{
    // Controller compartido para sincronización
    private static Windows.Media.MediaTimelineController? _sharedTimelineController;
    private static readonly List<MediaPlayer> _synchronizedPlayers = new();
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
    /// Todos los players compartirán el mismo controlador de timeline para sincronización perfecta.
    /// </summary>
    public static void PlaySynchronized(PrecisionVideoPlayer[] players, TimeSpan startPosition, double speed)
    {
        if (players.Length == 0) return;

        var nativePlayers = new List<MediaPlayer>();
        
        foreach (var player in players)
        {
            var nativePlayer = GetNativePlayer(player);
            if (nativePlayer != null)
            {
                nativePlayers.Add(nativePlayer);
            }
        }

        if (nativePlayers.Count == 0) return;

        try
        {
            // Limpiar controller anterior si existe
            CleanupTimelineController();

            // Crear nuevo MediaTimelineController para sincronización
            _sharedTimelineController = new Windows.Media.MediaTimelineController();
            _sharedTimelineController.ClockRate = speed;
            _synchronizedPlayers.Clear();

            // Primero hacer seek en todos los players a la posición inicial
            foreach (var nativePlayer in nativePlayers)
            {
                // Pausar y posicionar
                nativePlayer.Pause();
                nativePlayer.PlaybackSession.Position = startPosition;
                
                // Asociar al timeline controller
                nativePlayer.TimelineController = _sharedTimelineController;
                nativePlayer.TimelineControllerPositionOffset = startPosition;
                
                _synchronizedPlayers.Add(nativePlayer);
            }

            // Iniciar el controller - todos los players comenzarán simultáneamente
            _sharedTimelineController.Position = TimeSpan.Zero;
            _sharedTimelineController.Start();
            _isControllerActive = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NativeSyncPlaybackService] Error in synchronized play: {ex.Message}");
            
            // Fallback: reproducción secuencial normal
            CleanupTimelineController();
            foreach (var nativePlayer in nativePlayers)
            {
                nativePlayer.PlaybackSession.Position = startPosition;
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
            // Fallback: pausar individualmente
            foreach (var player in players)
            {
                var nativePlayer = GetNativePlayer(player);
                nativePlayer?.Pause();
            }
        }
    }

    /// <summary>
    /// Hace seek sincronizado en todos los players
    /// </summary>
    public static void SeekSynchronized(PrecisionVideoPlayer[] players, TimeSpan position)
    {
        if (_isControllerActive && _sharedTimelineController != null)
        {
            try
            {
                // Pausar el controller
                var wasPlaying = _sharedTimelineController.State == Windows.Media.MediaTimelineControllerState.Running;
                _sharedTimelineController.Pause();

                // Actualizar offsets para cada player
                foreach (var nativePlayer in _synchronizedPlayers)
                {
                    nativePlayer.TimelineControllerPositionOffset = position;
                }

                // Reset del controller y reanudar si estaba reproduciendo
                _sharedTimelineController.Position = TimeSpan.Zero;
                
                if (wasPlaying)
                {
                    _sharedTimelineController.Start();
                }

                // Actualizar posición en los controles
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

            // Desasociar players del controller
            foreach (var player in _synchronizedPlayers)
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
