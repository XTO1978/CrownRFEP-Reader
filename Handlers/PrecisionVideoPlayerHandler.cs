#if MACCATALYST || IOS
using AVFoundation;
using AVKit;
using CoreMedia;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace CrownRFEP_Reader.Handlers;

/// <summary>
/// Handler nativo para PrecisionVideoPlayer usando AVPlayer.
/// Proporciona control preciso frame-by-frame.
/// </summary>
public class PrecisionVideoPlayerHandler : ViewHandler<Controls.PrecisionVideoPlayer, UIView>
{
    private AVPlayerViewController? _playerViewController;
    private AVPlayer? _player;
    private AVPlayerItem? _playerItem;
    private NSObject? _timeObserver;
    private NSObject? _endObserver;
    private NSObject? _statusObserver;
    private bool _isUpdatingPosition;
    private bool _isSeekingFromBinding;
    private bool _isSeeking; // Flag para evitar seeks simultáneos
    private TimeSpan _pendingSeekPosition; // Posición pendiente si hay seek en progreso

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
        _playerViewController = new AVPlayerViewController
        {
            ShowsPlaybackControls = false,
            VideoGravity = AVLayerVideoGravity.ResizeAspect
        };

        return _playerViewController.View!;
    }

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);

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
    }

    protected override void DisconnectHandler(UIView platformView)
    {
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

        CleanupPlayer();
        
        _playerViewController?.Dispose();
        _playerViewController = null;

        base.DisconnectHandler(platformView);
    }

    private void CleanupPlayer()
    {
        if (_timeObserver != null && _player != null)
        {
            _player.RemoveTimeObserver(_timeObserver);
            _timeObserver.Dispose();
            _timeObserver = null;
        }

        if (_endObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_endObserver);
            _endObserver.Dispose();
            _endObserver = null;
        }

        if (_statusObserver != null)
        {
            _statusObserver.Dispose();
            _statusObserver = null;
        }

        _player?.Pause();
        _playerItem?.Dispose();
        _player?.Dispose();
        _playerItem = null;
        _player = null;
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
        if (handler._playerViewController != null)
        {
            handler._playerViewController.VideoGravity = view.Aspect switch
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
        CleanupPlayer();

        if (string.IsNullOrEmpty(source))
            return;

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

        _playerItem = new AVPlayerItem(url);
        _player = new AVPlayer(_playerItem);
        _player.Muted = VirtualView.IsMuted;

        if (_playerViewController != null)
        {
            _playerViewController.Player = _player;
        }

        // Observar cuando el item está listo usando notificaciones
        _statusObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            new NSString("AVPlayerItemDidBecomeReadyToPlayNotification"),
            OnPlayerItemReady,
            _playerItem);

        // También verificar el status directamente con un timer breve
        CheckPlayerStatus();

        // Observar el fin del vídeo
        _endObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            AVPlayerItem.DidPlayToEndTimeNotification,
            OnPlaybackEnded,
            _playerItem);

        // Observar la posición cada 1/60 de segundo para precisión
        var interval = CMTime.FromSeconds(1.0 / 60.0, 600);
        _timeObserver = _player.AddPeriodicTimeObserver(interval, null, (time) =>
        {
            if (_isUpdatingPosition || _isSeekingFromBinding)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (VirtualView != null)
                {
                    var position = TimeSpan.FromSeconds(time.Seconds);
                    VirtualView.UpdatePosition(position);
                }
            });
        });
    }

    private async void CheckPlayerStatus()
    {
        // Esperar un poco y verificar el status
        for (int i = 0; i < 50; i++) // Máximo 5 segundos
        {
            await Task.Delay(100);
            
            if (_playerItem == null) return;
            
            if (_playerItem.Status == AVPlayerItemStatus.ReadyToPlay)
            {
                OnPlayerReady();
                return;
            }
        }
    }

    private void OnPlayerItemReady(NSNotification notification)
    {
        OnPlayerReady();
    }

    private void OnPlayerReady()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (VirtualView != null && _playerItem != null && _playerItem.Duration.IsNumeric)
            {
                var duration = TimeSpan.FromSeconds(_playerItem.Duration.Seconds);
                VirtualView.UpdateDuration(duration);

                // Detectar frame rate del vídeo
                DetectFrameRate();

                VirtualView.RaiseMediaOpened();
            }
        });
    }

    private void DetectFrameRate()
    {
        if (_playerItem?.Asset is AVAsset asset)
        {
            var videoTracks = asset.GetTracks(AVMediaTypes.Video);
            if (videoTracks.Length > 0)
            {
                var track = videoTracks[0];
                var frameRate = track.NominalFrameRate;
                if (frameRate > 0)
                {
                    VirtualView?.UpdateFrameRate(frameRate);
                }
            }
        }
    }

    private void OnPlaybackEnded(NSNotification notification)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            VirtualView?.RaiseMediaEnded();
        });
    }

    #endregion

    #region Playback Control

    private void OnPlayRequested(object? sender, EventArgs e)
    {
        if (_player == null) return;
        _player.Rate = (float)VirtualView.Speed;
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
        if (_player == null) return;

        _isUpdatingPosition = true;
        _isSeeking = true;
        _pendingSeekPosition = TimeSpan.MinValue; // Resetear posición pendiente

        var cmTime = CMTime.FromSeconds(position.TotalSeconds, 600);
        
        // Seek con tolerancia cero para precisión de frame
        _player.Seek(cmTime, CMTime.Zero, CMTime.Zero, (finished) =>
        {
            _isUpdatingPosition = false;
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (VirtualView != null)
                {
                    VirtualView.UpdatePosition(position);
                }
                completion?.Invoke();
                
                // Procesar posición pendiente si hay alguna
                if (_pendingSeekPosition != TimeSpan.MinValue)
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
        if (_playerViewController != null)
        {
            _playerViewController.VideoGravity = aspect switch
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
