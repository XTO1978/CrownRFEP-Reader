#if MACCATALYST || IOS
using AVFoundation;
using CoreMedia;
using Foundation;
using CrownRFEP_Reader.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using UIKit;

namespace CrownRFEP_Reader.Handlers;

/// <summary>
/// Handler nativo para PrecisionVideoPlayer usando AVPlayer.
/// Proporciona control preciso frame-by-frame.
/// </summary>
public class PrecisionVideoPlayerHandler : ViewHandler<Controls.PrecisionVideoPlayer, UIView>
{
    private PlayerContainerView? _containerView;
    private AVPlayerLayer? _playerLayer;
    private AVPlayer? _player;
    private AVPlayerItem? _playerItem;
    
    /// <summary>
    /// Expone el AVPlayer nativo para sincronización precisa entre múltiples players
    /// </summary>
    public AVPlayer? NativePlayer => _player;
    private NSObject? _timeObserver;
    private NSObject? _endObserver;
    private NSObject? _statusObserver;
    private CancellationTokenSource? _statusCheckCts;
    private bool _isUpdatingPosition;
    private bool _isSeekingFromBinding;
    private bool _isSeeking; // Flag para evitar seeks simultáneos
    private TimeSpan _pendingSeekPosition; // Posición pendiente si hay seek en progreso
    private bool _isDisconnected; // Flag para evitar acceso a VirtualView después de desconexión
    private bool _autoPlayWhenReady;

    private sealed class PlayerContainerView : UIView
    {
        public AVPlayerLayer PlayerLayer { get; }

        public PlayerContainerView()
        {
            // Fondo transparente para no tapar otros elementos cuando el player no tiene video
            BackgroundColor = UIColor.Clear;
            Opaque = false;
            
            PlayerLayer = new AVPlayerLayer
            {
                VideoGravity = AVLayerVideoGravity.ResizeAspect
            };

            Layer.AddSublayer(PlayerLayer);
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            PlayerLayer.Frame = Bounds;
        }
    }

    public static IPropertyMapper<Controls.PrecisionVideoPlayer, PrecisionVideoPlayerHandler> Mapper =
        new PropertyMapper<Controls.PrecisionVideoPlayer, PrecisionVideoPlayerHandler>(ViewHandler.ViewMapper)
        {
            [nameof(Controls.PrecisionVideoPlayer.Source)] = MapSource,
            [nameof(Controls.PrecisionVideoPlayer.Speed)] = MapSpeed,
            [nameof(Controls.PrecisionVideoPlayer.IsMuted)] = MapIsMuted,
            [nameof(Controls.PrecisionVideoPlayer.Aspect)] = MapAspect,
        };

    public PrecisionVideoPlayerHandler() : base(Mapper)
    {
    }

    protected override UIView CreatePlatformView()
    {
        _containerView = new PlayerContainerView();
        _playerLayer = _containerView.PlayerLayer;
        return _containerView;
    }

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);

        AppLog.Info("PrecisionVideoPlayerHandler", $"ConnectHandler | previous _isDisconnected={_isDisconnected}");

        // Importante: en algunos ciclos de vida (Shell navegación / Hot Reload) el handler
        // puede desconectarse y volverse a conectar. No podemos dejarlo permanentemente
        // en estado "disconnected".
        _isDisconnected = false;

        // Si por algún motivo perdimos referencias (p.ej. DisconnectHandler previo),
        // recupéralas desde el platformView actual.
        if (_containerView == null && platformView is PlayerContainerView playerContainer)
        {
            _containerView = playerContainer;
        }
        if (_playerLayer == null && _containerView != null)
        {
            _playerLayer = _containerView.PlayerLayer;
        }

        // Suscribirse a eventos del control
        VirtualView.PlayRequested += OnPlayRequested;
        VirtualView.PauseRequested += OnPauseRequested;
        VirtualView.StopRequested += OnStopRequested;
        VirtualView.StepForwardRequested += OnStepForwardRequested;
        VirtualView.StepBackwardRequested += OnStepBackwardRequested;
        VirtualView.SeekRequested += OnSeekRequested;
        VirtualView.PositionChangedFromBinding += OnPositionChangedFromBinding;
        VirtualView.SpeedChangedInternal += OnSpeedChanged;
        VirtualView.MutedChangedInternal += OnMutedChanged;
        VirtualView.AspectChangedInternal += OnAspectChanged;
        VirtualView.PrepareForCleanupRequested += OnPrepareForCleanupRequested;

        // Si el Source ya está asignado (por binding) y el player fue limpiado al desconectar,
        // recargarlo al conectar para que Play/Seek funcionen.
        if (!string.IsNullOrEmpty(VirtualView.Source) && (_player == null || _playerItem == null))
        {
            try { LoadSource(VirtualView.Source); } catch { }
        }
    }

    protected override void DisconnectHandler(UIView platformView)
    {
#if DEBUG
        AppLog.Info("PrecisionVideoPlayerHandler", $"DisconnectHandler ENTER | MainThread={MainThread.IsMainThread}");
#endif

        // Importante: UIKit/AVFoundation deben tocarse en el main thread.
        // En escenarios con debugger/hotreload, a veces DisconnectHandler llega desde otro hilo.
        if (!MainThread.IsMainThread)
        {
#if DEBUG
            AppLog.Warn("PrecisionVideoPlayerHandler", "DisconnectHandler called off main thread; rescheduling to main thread");
#endif
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    DisconnectHandler(platformView);
                }
                catch (Exception ex)
                {
                    AppLog.Error("PrecisionVideoPlayerHandler", "DisconnectHandler rescheduled threw", ex);
                }
            });
            return;
        }

        if (_isDisconnected)
            return;

        _isDisconnected = true;
        
        // Desuscribirse de eventos
        VirtualView.PlayRequested -= OnPlayRequested;
        VirtualView.PauseRequested -= OnPauseRequested;
        VirtualView.StopRequested -= OnStopRequested;
        VirtualView.StepForwardRequested -= OnStepForwardRequested;
        VirtualView.StepBackwardRequested -= OnStepBackwardRequested;
        VirtualView.SeekRequested -= OnSeekRequested;
        VirtualView.PositionChangedFromBinding -= OnPositionChangedFromBinding;
        VirtualView.SpeedChangedInternal -= OnSpeedChanged;
        VirtualView.MutedChangedInternal -= OnMutedChanged;
        VirtualView.AspectChangedInternal -= OnAspectChanged;
        VirtualView.PrepareForCleanupRequested -= OnPrepareForCleanupRequested;

        CleanupPlayer();

        // Nota: no desmontar ni disponer manualmente el platformView / layer aquí.
        // MAUI controla el ciclo de vida del UIView. Si removemos el layer o hacemos Dispose,
        // el handler puede no ser capaz de reconectar correctamente al volver a la página.
        try
        {
            if (_playerLayer != null)
                _playerLayer.Player = null;
        }
        catch (Exception ex)
        {
            AppLog.Error("PrecisionVideoPlayerHandler", "DisconnectHandler setting playerLayer.Player=null threw", ex);
        }

        base.DisconnectHandler(platformView);

    #if DEBUG
        AppLog.Info("PrecisionVideoPlayerHandler", "DisconnectHandler EXIT");
    #endif
    }

    private void CleanupPlayer()
    {
#if DEBUG
        AppLog.Info("PrecisionVideoPlayerHandler", "CleanupPlayer BEGIN");
#endif

        _autoPlayWhenReady = false;

        try
        {
            _statusCheckCts?.Cancel();
            _statusCheckCts?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("PrecisionVideoPlayerHandler", "Cancelling statusCheckCts threw", ex);
        }
        finally
        {
            _statusCheckCts = null;
        }

        if (_timeObserver != null && _player != null)
        {
            try
            {
                _player.RemoveTimeObserver(_timeObserver);
            }
            catch (Exception ex)
            {
                AppLog.Error("PrecisionVideoPlayerHandler", "RemoveTimeObserver threw", ex);
            }

            try
            {
                _timeObserver.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("PrecisionVideoPlayerHandler", "Dispose timeObserver threw", ex);
            }
            _timeObserver = null;
        }

        if (_endObserver != null)
        {
            try
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_endObserver);
            }
            catch (Exception ex)
            {
                AppLog.Error("PrecisionVideoPlayerHandler", "RemoveObserver endObserver threw", ex);
            }

            try
            {
                _endObserver.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("PrecisionVideoPlayerHandler", "Dispose endObserver threw", ex);
            }
            _endObserver = null;
        }

        if (_statusObserver != null)
        {
            // Importante: quitar el observer de NSNotificationCenter antes de Dispose.
            try
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_statusObserver);
            }
            catch (Exception ex)
            {
                AppLog.Error("PrecisionVideoPlayerHandler", "RemoveObserver statusObserver threw", ex);
            }

            try
            {
                _statusObserver.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("PrecisionVideoPlayerHandler", "Dispose statusObserver threw", ex);
            }
            _statusObserver = null;
        }

        try
        {
            // Desasociar del view controller para evitar callbacks tardíos.
            if (_playerLayer != null)
                _playerLayer.Player = null;
        }
        catch (Exception ex)
        {
            AppLog.Error("PrecisionVideoPlayerHandler", "Setting Player=null threw", ex);
        }

        try { _player?.Pause(); }
        catch (Exception ex) { AppLog.Error("PrecisionVideoPlayerHandler", "Pause threw", ex); }

        // Cortar la relación Player -> Item antes de disponer, para evitar callbacks tardíos.
        try
        {
            _player?.ReplaceCurrentItemWithPlayerItem(null);
        }
        catch (Exception ex)
        {
            AppLog.Error("PrecisionVideoPlayerHandler", "ReplaceCurrentItemWithPlayerItem(null) threw", ex);
        }

        var playerToDispose = _player;
        _player = null;
        try { playerToDispose?.Dispose(); }
        catch (Exception ex) { AppLog.Error("PrecisionVideoPlayerHandler", "Dispose player threw", ex); }

        var itemToDispose = _playerItem;
        _playerItem = null;
        try { itemToDispose?.Dispose(); }
        catch (Exception ex) { AppLog.Error("PrecisionVideoPlayerHandler", "Dispose playerItem threw", ex); }

#if DEBUG
        AppLog.Info("PrecisionVideoPlayerHandler", "CleanupPlayer END");
#endif
    }

    /// <summary>
    /// En iOS/MacCatalyst usamos AVPlayerLayer (sin UIViewController). Este hook
    /// fuerza una limpieza anticipada (p.ej. OnDisappearing) para cortar callbacks tardíos.
    /// </summary>
    private void OnPrepareForCleanupRequested(object? sender, EventArgs e)
    {
        if (_isDisconnected)
            return;

        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { OnPrepareForCleanupRequested(sender, e); }
                catch (Exception ex) { AppLog.Error("PrecisionVideoPlayerHandler", "PrepareForCleanup rescheduled threw", ex); }
            });
            return;
        }

        CleanupPlayer();
    }

    #region Property Mappers

    private static void MapSource(PrecisionVideoPlayerHandler handler, Controls.PrecisionVideoPlayer view)
    {
        handler.LoadSource(view.Source);
    }

    private static void MapSpeed(PrecisionVideoPlayerHandler handler, Controls.PrecisionVideoPlayer view)
    {
        if (handler._player != null && view.IsPlaying)
        {
            handler._player.Rate = (float)view.Speed;
        }
    }

    private static void MapIsMuted(PrecisionVideoPlayerHandler handler, Controls.PrecisionVideoPlayer view)
    {
        if (handler._player != null)
        {
            handler._player.Muted = view.IsMuted;
        }
    }

    private static void MapAspect(PrecisionVideoPlayerHandler handler, Controls.PrecisionVideoPlayer view)
    {
        if (handler._playerLayer != null)
        {
            handler._playerLayer.VideoGravity = view.Aspect switch
            {
                Aspect.AspectFill => AVLayerVideoGravity.ResizeAspectFill,
                Aspect.Fill => AVLayerVideoGravity.Resize,
                _ => AVLayerVideoGravity.ResizeAspect
            };
        }
    }

    #endregion

    #region Source Loading

    private void LoadSource(string? source)
    {
        AppLog.Info("PrecisionVideoPlayerHandler", $"LoadSource ENTER | source={(source != null ? "[set]" : "[null]")} | _isDisconnected={_isDisconnected} | _player={((_player != null) ? "[alive]" : "[null]")}");

        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    LoadSource(source);
                }
                catch (Exception ex)
                {
                    AppLog.Error("PrecisionVideoPlayerHandler", "LoadSource (rescheduled) threw", ex);
                }
            });
            return;
        }

        CleanupPlayer();

        if (string.IsNullOrEmpty(source))
        {
            // Sin source: aseguramos que el layer no retenga player
            if (_playerLayer != null)
                _playerLayer.Player = null;
            return;
        }

        NSUrl? url = null;

        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = NSUrl.FromString(source);
        }
        else
        {
            // Archivo local
            url = NSUrl.FromFilename(source);
        }

        if (url == null)
            return;

#if DEBUG
        try
        {
            using var asset = AVAsset.FromUrl(url);
            var audioTracks = asset.GetTracks(AVMediaTypes.Audio);
            AppLog.Info("PrecisionVideoPlayerHandler", $"LoadSource | muted={VirtualView.IsMuted} | audioTracks={audioTracks?.Length ?? 0} | url={url}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("PrecisionVideoPlayerHandler", $"LoadSource audioTracks probe failed: {ex.Message}");
        }
#endif

        _playerItem = new AVPlayerItem(url);
        _player = new AVPlayer(_playerItem);
        _player.Muted = VirtualView.IsMuted;

        if (_playerLayer != null)
            _playerLayer.Player = _player;

        // Observar cuando el item está listo usando notificaciones
        _statusObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            new NSString("AVPlayerItemDidBecomeReadyToPlayNotification"),
            OnPlayerItemReady,
            _playerItem);

        // También verificar el status directamente con un timer breve
        _statusCheckCts = new CancellationTokenSource();
        _ = CheckPlayerStatusAsync(_statusCheckCts.Token);

        // Observar el fin del vídeo
        _endObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            AVPlayerItem.DidPlayToEndTimeNotification,
            OnPlaybackEnded,
            _playerItem);

        // Observar la posición cada 1/60 de segundo para precisión
        var interval = CMTime.FromSeconds(1.0 / 60.0, 600);
        _timeObserver = _player.AddPeriodicTimeObserver(interval, null, (time) =>
        {
            if (_isUpdatingPosition || _isSeekingFromBinding || _isDisconnected)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isDisconnected) return;
                
                try
                {
                    VirtualView?.UpdatePosition(TimeSpan.FromSeconds(time.Seconds));
                }
                catch { /* Handler ya desconectado */ }
            });
        });

        AppLog.Info("PrecisionVideoPlayerHandler", $"LoadSource EXIT | player created OK | _player={((_player != null) ? "[alive]" : "[null]")}");
    }

    private async Task CheckPlayerStatusAsync(CancellationToken ct)
    {
        try
        {
            // Esperar un poco y verificar el status (máx 5s)
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(100, ct);

                if (ct.IsCancellationRequested || _isDisconnected)
                    return;

                // Acceder a AVPlayerItem en main thread para evitar crashes nativos intermitentes.
                var isReady = await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (ct.IsCancellationRequested || _isDisconnected || _playerItem == null)
                        return false;

                    return _playerItem.Status == AVPlayerItemStatus.ReadyToPlay;
                });

                if (isReady)
                {
                    OnPlayerReady();
                    return;
                }
            }
        }
        catch (TaskCanceledException)
        {
            // normal
        }
        catch (Exception ex)
        {
            AppLog.Error("PrecisionVideoPlayerHandler", "CheckPlayerStatusAsync threw", ex);
        }
    }

    private void OnPlayerItemReady(NSNotification notification)
    {
        if (_isDisconnected) return;

        try
        {
            // Ignorar notificaciones de items antiguos.
            if (notification.Object != null && _playerItem != null && notification.Object.Handle != _playerItem.Handle)
                return;
        }
        catch
        {
            return;
        }

        OnPlayerReady();
    }

    private void OnPlayerReady()
    {
        if (_isDisconnected) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isDisconnected) return;
            
            try
            {
                if (VirtualView != null && _playerItem != null)
                {
                    // Duration puede no estar disponible aún; no bloquear el "ready" por ello.
                    if (_playerItem.Duration.IsNumeric)
                    {
                        var duration = TimeSpan.FromSeconds(_playerItem.Duration.Seconds);
                        VirtualView.UpdateDuration(duration);
                    }

                    // Detectar frame rate del vídeo
                    DetectFrameRate();

                    VirtualView.RaiseMediaOpened();

                    // Si alguien llamó a Play() antes de que el item estuviera listo,
                    // arrancar aquí para evitar quedarse con pantalla "gris".
                    if ((_autoPlayWhenReady || VirtualView.IsPlaying) && _player != null)
                    {
                        try
                        {
                            _player.Play();
                            _player.Rate = (float)VirtualView.Speed;
                        }
                        catch { }

                        _autoPlayWhenReady = false;
                    }
                }
            }
            catch { /* Handler ya desconectado */ }
        });
    }

    private void DetectFrameRate()
    {
        if (_isDisconnected) return;
        
        if (_playerItem?.Asset is AVAsset asset)
        {
            var videoTracks = asset.GetTracks(AVMediaTypes.Video);
            if (videoTracks.Length > 0)
            {
                var track = videoTracks[0];
                var frameRate = track.NominalFrameRate;
                if (frameRate > 0)
                {
                    try { VirtualView?.UpdateFrameRate(frameRate); } catch { }
                }
            }
        }
    }

    private void OnPlaybackEnded(NSNotification notification)
    {
        if (_isDisconnected) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isDisconnected) return;
            try { VirtualView?.RaiseMediaEnded(); } catch { }
        });
    }

    #endregion

    #region Playback Control

    private void OnPlayRequested(object? sender, EventArgs e)
    {
        AppLog.Info("PrecisionVideoPlayerHandler", $"OnPlayRequested | _isDisconnected={_isDisconnected} | _player={((_player != null) ? "[alive]" : "[null]")}");

        if (_isDisconnected)
        {
            AppLog.Warn("PrecisionVideoPlayerHandler", "OnPlayRequested ignored: _isDisconnected=true");
            return;
        }

        // UIKit/AVFoundation: siempre en main thread.
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { OnPlayRequested(sender, e); } catch { }
            });
            return;
        }

        // Si el player fue limpiado pero el Source sigue, recargar y auto-reproducir.
        if (_player == null || _playerItem == null)
        {
            var source = VirtualView?.Source;
            AppLog.Info("PrecisionVideoPlayerHandler", $"OnPlayRequested | player null, reloading source={(source != null ? "[set]" : "[null]")}");
            if (!string.IsNullOrEmpty(source))
            {
                _autoPlayWhenReady = true;
                LoadSource(source);
            }
            return;
        }

        // En MacCatalyst, si la app se queda con una AVAudioSession en PlayAndRecord
        // (por ejemplo tras una grabación), al reproducir un vídeo macOS puede reactivar
        // el input y mostrar el icono naranja de micrófono. Forzamos Playback aquí.
        TryConfigureAppleAudioSessionForPlayback();

        // Si el video llegó al final, hacer seek al inicio antes de reproducir.
        // AVPlayer no reproduce si está en la posición final.
        var currentTime = _player.CurrentTime;
        var duration = _playerItem.Duration;
        
        // Verificar que duration y currentTime son válidos usando sus Seconds
        // Si el valor es NaN o infinito, no es válido
        var durationSeconds = duration.Seconds;
        var currentSeconds = currentTime.Seconds;
        
        bool durationIsValid = !double.IsNaN(durationSeconds) && !double.IsInfinity(durationSeconds) && durationSeconds > 0;
        bool currentTimeIsValid = !double.IsNaN(currentSeconds) && !double.IsInfinity(currentSeconds);
        
        if (durationIsValid && currentTimeIsValid)
        {
            // Considerar "al final" si está a menos de 0.5 segundos del final
            if (currentSeconds >= durationSeconds - 0.5)
            {
                _player.Seek(CMTime.Zero, CMTime.Zero, CMTime.Zero, (finished) =>
                {
                    if (finished)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _player?.Play();
                            if (_player != null)
                                _player.Rate = (float)VirtualView.Speed;
                        });
                    }
                });
                return;
            }
        }

        // Asegurar que realmente arranca la reproducción.
        try
        {
            _player.Play();
            _player.Rate = (float)VirtualView.Speed;
        }
        catch { }
    }

    private static void TryConfigureAppleAudioSessionForPlayback()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            NSError? error;

            // Si la sesión ya está en PlayAndRecord, probablemente estamos en modo videolección.
            // No forzar Playback aquí: cambiar de categoría mientras se graba puede dejar el mic
            // capturando silencio (metering en -120 dB) aunque el recorder siga "Recording=true".
            // La sesión se restaura a Playback cuando termina la grabación.
            var currentCategory = session.Category?.ToString();
            if (string.Equals(currentCategory, AVAudioSessionCategory.PlayAndRecord.ToString(), StringComparison.Ordinal))
                return;

            // Playback: no requiere micrófono.
            // MixWithOthers: menos intrusivo con audio del sistema.
            session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.MixWithOthers, out error);
            session.SetActive(true, out error);
        }
        catch
        {
            // Best-effort.
        }
    }

    private void OnPauseRequested(object? sender, EventArgs e)
    {
        _player?.Pause();
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        _player?.Pause();
        SeekToTime(TimeSpan.Zero);
    }

    /// <summary>
    /// Avanza exactamente un frame usando el método nativo Step
    /// </summary>
    private void OnStepForwardRequested(object? sender, EventArgs e)
    {
        if (_playerItem == null) return;

        // Pausar primero para ver el frame
        _player?.Pause();

        // Usar StepByCount nativo de AVPlayerItem para precisión exacta
        _playerItem.StepByCount(1);

        // Actualizar posición después del step
        UpdatePositionFromPlayer();
    }

    /// <summary>
    /// Retrocede exactamente un frame usando el método nativo Step
    /// </summary>
    private void OnStepBackwardRequested(object? sender, EventArgs e)
    {
        if (_playerItem == null) return;

        // Pausar primero para ver el frame
        _player?.Pause();

        // Usar StepByCount nativo de AVPlayerItem con valor negativo
        _playerItem.StepByCount(-1);

        // Actualizar posición después del step
        UpdatePositionFromPlayer();
    }

    private void OnSeekRequested(object? sender, TimeSpan position)
    {
        SeekToTime(position);
    }

    private void OnPositionChangedFromBinding(object? sender, TimeSpan position)
    {
        // Si ya hay un seek en progreso, guardar la posición pendiente
        // para hacer seek al finalizar el actual
        if (_isSeeking)
        {
            _pendingSeekPosition = position;
            return;
        }
        
        // Seek preciso cuando la posición cambia desde el binding (slider)
        _isSeekingFromBinding = true;
        SeekToTime(position, () =>
        {
            _isSeekingFromBinding = false;
        });
    }

    /// <summary>
    /// Realiza un seek preciso con tolerancia cero para mostrar el frame exacto
    /// </summary>
    private void SeekToTime(TimeSpan position, Action? completion = null)
    {
        if (_player == null || _isDisconnected) return;

        _isUpdatingPosition = true;
        _isSeeking = true;
        _pendingSeekPosition = TimeSpan.MinValue; // Resetear posición pendiente

        var cmTime = CMTime.FromSeconds(position.TotalSeconds, 600);
        
        // Seek con tolerancia cero para precisión de frame
        _player.Seek(cmTime, CMTime.Zero, CMTime.Zero, (finished) =>
        {
            _isUpdatingPosition = false;
            
            // Verificar si el handler fue desconectado durante el seek asíncrono
            if (_isDisconnected) 
            {
                _isSeeking = false;
                return;
            }
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Doble verificación en el hilo principal
                if (_isDisconnected) 
                {
                    _isSeeking = false;
                    return;
                }
                
                try
                {
                    VirtualView?.UpdatePosition(position);
                }
                catch { /* Handler ya desconectado */ }
                
                completion?.Invoke();
                
                // Procesar posición pendiente si hay alguna
                if (!_isDisconnected && _pendingSeekPosition != TimeSpan.MinValue)
                {
                    var pendingPos = _pendingSeekPosition;
                    _isSeeking = false;
                    SeekToTime(pendingPos, null);
                }
                else
                {
                    _isSeeking = false;
                }
            });
        });
    }

    private void UpdatePositionFromPlayer()
    {
        if (_player == null) return;

        var currentTime = _player.CurrentTime;
        if (currentTime.IsNumeric)
        {
            var position = TimeSpan.FromSeconds(currentTime.Seconds);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                VirtualView?.UpdatePosition(position);
            });
        }
    }

    #endregion

    #region Speed and Mute

    private void OnSpeedChanged(object? sender, double speed)
    {
        if (_player != null && VirtualView.IsPlaying)
        {
            _player.Rate = (float)speed;
        }
    }

    private void OnMutedChanged(object? sender, bool muted)
    {
        if (_player != null)
        {
            _player.Muted = muted;
        }
    }

    private void OnAspectChanged(object? sender, Aspect aspect)
    {
        if (_playerLayer != null)
        {
            _playerLayer.VideoGravity = aspect switch
            {
                Aspect.AspectFill => AVLayerVideoGravity.ResizeAspectFill,
                Aspect.Fill => AVLayerVideoGravity.Resize,
                _ => AVLayerVideoGravity.ResizeAspect
            };
        }
    }

    #endregion
}
#endif

#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace CrownRFEP_Reader.Handlers;

/// <summary>
/// Handler para PrecisionVideoPlayer en Windows usando MediaPlayerElement.
/// Proporciona control preciso de reproducción de video.
/// </summary>
public class PrecisionVideoPlayerHandler : ViewHandler<Controls.PrecisionVideoPlayer, MediaPlayerElement>
{
    private MediaPlayer? _mediaPlayer;
    private DispatcherTimer? _positionTimer;
    private bool _isUpdatingPosition;
    private bool _isSeekingFromBinding;
    private bool _isSeeking;
    private TimeSpan _pendingSeekPosition;
    private bool _isDisconnecting; // Flag para evitar callbacks tardíos durante/después de desconexión

    /// <summary>
    /// Expone el MediaPlayer nativo para sincronización precisa entre múltiples players
    /// </summary>
    public MediaPlayer? NativePlayer => _mediaPlayer;

    public static IPropertyMapper<Controls.PrecisionVideoPlayer, PrecisionVideoPlayerHandler> Mapper =
        new PropertyMapper<Controls.PrecisionVideoPlayer, PrecisionVideoPlayerHandler>(ViewHandler.ViewMapper)
        {
            [nameof(Controls.PrecisionVideoPlayer.Source)] = MapSource,
            [nameof(Controls.PrecisionVideoPlayer.Speed)] = MapSpeed,
            [nameof(Controls.PrecisionVideoPlayer.IsMuted)] = MapIsMuted,
            [nameof(Controls.PrecisionVideoPlayer.Aspect)] = MapAspect,
        };

    public PrecisionVideoPlayerHandler() : base(Mapper)
    {
    }

    protected override MediaPlayerElement CreatePlatformView()
    {
        _mediaPlayer = new MediaPlayer
        {
            AutoPlay = false,
            AudioCategory = MediaPlayerAudioCategory.Media
        };

        var element = new MediaPlayerElement
        {
            AreTransportControlsEnabled = false,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
        };

        element.SetMediaPlayer(_mediaPlayer);

        return element;
    }

    protected override void ConnectHandler(MediaPlayerElement platformView)
    {
        base.ConnectHandler(platformView);

        // Este handler puede reutilizarse; resetear el estado de desconexión
        _isDisconnecting = false;

        if (_mediaPlayer != null)
        {
            _mediaPlayer.MediaOpened += OnMediaOpened;
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
        }

        // Timer para actualizar la posición
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _positionTimer.Tick += OnPositionTimerTick;

        // Suscribirse a eventos del control
        VirtualView.PlayRequested += OnPlayRequested;
        VirtualView.PauseRequested += OnPauseRequested;
        VirtualView.StopRequested += OnStopRequested;
        VirtualView.StepForwardRequested += OnStepForwardRequested;
        VirtualView.StepBackwardRequested += OnStepBackwardRequested;
        VirtualView.SeekRequested += OnSeekRequested;
        VirtualView.PositionChangedFromBinding += OnPositionChangedFromBinding;
        VirtualView.SpeedChangedInternal += OnSpeedChanged;
        VirtualView.MutedChangedInternal += OnMutedChanged;
        VirtualView.AspectChangedInternal += OnAspectChanged;
        VirtualView.PrepareForCleanupRequested += OnPrepareForCleanup;

        // Cargar source inicial si existe
        if (!string.IsNullOrEmpty(VirtualView.Source))
        {
            LoadVideoAsync(VirtualView.Source);
        }
    }

    /// <summary>
    /// Llamado por el control cuando la página está a punto de desaparecer.
    /// Detiene inmediatamente todos los timers y callbacks sin esperar a DisconnectHandler.
    /// </summary>
    private void OnPrepareForCleanup(object? sender, EventArgs e)
    {
        _isDisconnecting = true;

        try { _positionTimer?.Stop(); } catch { }

        if (_mediaPlayer != null)
        {
            // IMPORTANTE: soltar el MediaPlayer del MediaPlayerElement antes de tocarlo/disponearlo
            try { PlatformView?.SetMediaPlayer(null); } catch { }

            try { _mediaPlayer.MediaOpened -= OnMediaOpened; } catch { }
            try { _mediaPlayer.MediaEnded -= OnMediaEnded; } catch { }
            try { _mediaPlayer.MediaFailed -= OnMediaFailed; } catch { }
            try { _mediaPlayer.Pause(); } catch { }
            try { _mediaPlayer.Source = null; } catch { }
        }
    }

    protected override void DisconnectHandler(MediaPlayerElement platformView)
    {
        // Marcar como desconectando inmediatamente para bloquear todos los callbacks
        _isDisconnecting = true;

        try
        {
            _positionTimer?.Stop();
        }
        catch { /* ignorar */ }
        _positionTimer = null;

        if (VirtualView != null)
        {
            try
            {
                VirtualView.PlayRequested -= OnPlayRequested;
                VirtualView.PauseRequested -= OnPauseRequested;
                VirtualView.StopRequested -= OnStopRequested;
                VirtualView.StepForwardRequested -= OnStepForwardRequested;
                VirtualView.StepBackwardRequested -= OnStepBackwardRequested;
                VirtualView.SeekRequested -= OnSeekRequested;
                VirtualView.PositionChangedFromBinding -= OnPositionChangedFromBinding;
                VirtualView.SpeedChangedInternal -= OnSpeedChanged;
                VirtualView.MutedChangedInternal -= OnMutedChanged;
                VirtualView.AspectChangedInternal -= OnAspectChanged;
                VirtualView.PrepareForCleanupRequested -= OnPrepareForCleanup;
            }
            catch { /* ignorar errores al desuscribir */ }
        }

        if (_mediaPlayer != null)
        {
            // IMPORTANTE: soltar el MediaPlayer del elemento primero
            try { platformView.SetMediaPlayer(null); } catch { }

            try { _mediaPlayer.MediaOpened -= OnMediaOpened; } catch { }
            try { _mediaPlayer.MediaEnded -= OnMediaEnded; } catch { }
            try { _mediaPlayer.MediaFailed -= OnMediaFailed; } catch { }
            try { _mediaPlayer.Pause(); } catch { }
            try { _mediaPlayer.Source = null; } catch { }
            try { _mediaPlayer.Dispose(); } catch { }
            _mediaPlayer = null;
        }

        base.DisconnectHandler(platformView);
    }

    #region Property Mappers

    private static void MapSource(PrecisionVideoPlayerHandler handler, Controls.PrecisionVideoPlayer player)
    {
        if (handler._isDisconnecting || string.IsNullOrEmpty(player.Source)) return;
        handler.LoadVideoAsync(player.Source);
    }

    private static void MapSpeed(PrecisionVideoPlayerHandler handler, Controls.PrecisionVideoPlayer player)
    {
        if (handler._isDisconnecting || handler._mediaPlayer == null) return;
        handler._mediaPlayer.PlaybackSession.PlaybackRate = player.Speed;
    }

    private static void MapIsMuted(PrecisionVideoPlayerHandler handler, Controls.PrecisionVideoPlayer player)
    {
        if (handler._isDisconnecting || handler._mediaPlayer == null) return;
        handler._mediaPlayer.IsMuted = player.IsMuted;
    }

    private static void MapAspect(PrecisionVideoPlayerHandler handler, Controls.PrecisionVideoPlayer player)
    {
        if (handler._isDisconnecting || handler.PlatformView == null) return;
        handler.PlatformView.Stretch = player.Aspect switch
        {
            Aspect.AspectFill => Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            Aspect.Fill => Microsoft.UI.Xaml.Media.Stretch.Fill,
            _ => Microsoft.UI.Xaml.Media.Stretch.Uniform
        };
    }

    #endregion

    #region Video Loading

    private async void LoadVideoAsync(string source)
    {
        if (_mediaPlayer == null || _isDisconnecting) return;

        try
        {
            _positionTimer?.Stop();
            _mediaPlayer.Pause();

            if (source.StartsWith("http://") || source.StartsWith("https://"))
            {
                _mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(source));
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(source);
                if (_isDisconnecting) return; // Check again after await
                _mediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading video: {ex.Message}");
        }
    }

    #endregion

    #region Media Player Events

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        if (_isDisconnecting || VirtualView == null) return;

        // Los eventos de MediaPlayer pueden llegar en otro hilo, invocar en UI thread
        PlatformView?.DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisconnecting || VirtualView == null) return;

            // Actualizar duración
            var duration = sender.PlaybackSession.NaturalDuration;
            System.Diagnostics.Debug.WriteLine($"[WinHandler] OnMediaOpened: duration={duration}");
            VirtualView.UpdateDuration(duration);

            // Intentar detectar frame rate (usar 30 fps por defecto)
            VirtualView.UpdateFrameRate(30.0);

            VirtualView.RaiseMediaOpened();
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        if (_isDisconnecting) return;

        // Los eventos de MediaPlayer pueden llegar en otro hilo, invocar en UI thread
        PlatformView?.DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisconnecting) return;

            _positionTimer?.Stop();
            VirtualView?.RaiseMediaEnded();
            
            if (VirtualView != null)
            {
                VirtualView.IsPlaying = false;
            }
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (_isDisconnecting) return;
        System.Diagnostics.Debug.WriteLine($"Media failed: {args.ErrorMessage}");
    }

    private void OnPositionTimerTick(object? sender, object e)
    {
        if (_isDisconnecting || _mediaPlayer == null || VirtualView == null || _isUpdatingPosition) return;

        _isUpdatingPosition = true;
        var pos = _mediaPlayer.PlaybackSession.Position;
        System.Diagnostics.Debug.WriteLine($"[WinHandler] OnPositionTimerTick: pos={pos}");
        VirtualView.UpdatePosition(pos);
        _isUpdatingPosition = false;
    }

    #endregion

    #region Playback Control Events

    private void OnPlayRequested(object? sender, EventArgs e)
    {
        if (_isDisconnecting || _mediaPlayer == null) return;

        System.Diagnostics.Debug.WriteLine($"[WinHandler] OnPlayRequested: starting timer");
        _mediaPlayer.Play();
        _positionTimer?.Start();
    }

    private void OnPauseRequested(object? sender, EventArgs e)
    {
        if (_isDisconnecting || _mediaPlayer == null) return;

        _mediaPlayer.Pause();
        _positionTimer?.Stop();
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        if (_isDisconnecting || _mediaPlayer == null) return;

        _mediaPlayer.Pause();
        _mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
        _positionTimer?.Stop();

        if (VirtualView != null)
        {
            VirtualView.UpdatePosition(TimeSpan.Zero);
        }
    }

    private void OnStepForwardRequested(object? sender, EventArgs e)
    {
        if (_isDisconnecting || _mediaPlayer == null || VirtualView == null) return;

        var frameTime = TimeSpan.FromSeconds(VirtualView.FrameDuration);
        var newPosition = _mediaPlayer.PlaybackSession.Position + frameTime;
        
        if (newPosition <= _mediaPlayer.PlaybackSession.NaturalDuration)
        {
            _mediaPlayer.PlaybackSession.Position = newPosition;
            VirtualView.UpdatePosition(newPosition);
        }
    }

    private void OnStepBackwardRequested(object? sender, EventArgs e)
    {
        if (_isDisconnecting || _mediaPlayer == null || VirtualView == null) return;

        var frameTime = TimeSpan.FromSeconds(VirtualView.FrameDuration);
        var newPosition = _mediaPlayer.PlaybackSession.Position - frameTime;
        
        if (newPosition < TimeSpan.Zero)
            newPosition = TimeSpan.Zero;

        _mediaPlayer.PlaybackSession.Position = newPosition;
        VirtualView.UpdatePosition(newPosition);
    }

    private void OnSeekRequested(object? sender, TimeSpan position)
    {
        if (_isDisconnecting || _mediaPlayer == null || VirtualView == null) return;

        var session = _mediaPlayer.PlaybackSession;
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;
        if (position > session.NaturalDuration)
            position = session.NaturalDuration;

        session.Position = position;
        VirtualView.UpdatePosition(position);
    }

    private void OnPositionChangedFromBinding(object? sender, TimeSpan position)
    {
        if (_isDisconnecting || _mediaPlayer == null || _isUpdatingPosition) return;

        _isSeekingFromBinding = true;
        _mediaPlayer.PlaybackSession.Position = position;
        _isSeekingFromBinding = false;
    }

    private void OnSpeedChanged(object? sender, double speed)
    {
        if (_isDisconnecting || _mediaPlayer == null) return;
        _mediaPlayer.PlaybackSession.PlaybackRate = speed;
    }

    private void OnMutedChanged(object? sender, bool muted)
    {
        if (_isDisconnecting || _mediaPlayer == null) return;
        _mediaPlayer.IsMuted = muted;
    }

    private void OnAspectChanged(object? sender, Aspect aspect)
    {
        if (_isDisconnecting || PlatformView == null) return;
        PlatformView.Stretch = aspect switch
        {
            Aspect.AspectFill => Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            Aspect.Fill => Microsoft.UI.Xaml.Media.Stretch.Fill,
            _ => Microsoft.UI.Xaml.Media.Stretch.Uniform
        };
    }

    #endregion
}
#endif
