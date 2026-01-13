#if MACCATALYST || IOS
using AVFoundation;
using CoreAnimation;
using CoreFoundation;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using ReplayKit;
using UIKit;
using CrownRFEP_Reader.Views.Controls;

namespace CrownRFEP_Reader.Handlers;

public sealed class ReplayKitCameraPreviewHandler : ViewHandler<ReplayKitCameraPreview, UIView>
{
    private UIView? _attachedPreview;
    private NSTimer? _attachTimer;

    private AVCaptureSession? _captureSession;
    private AVCaptureVideoPreviewLayer? _captureLayer;
    private NSObject? _orientationObserver;

    public static IPropertyMapper<ReplayKitCameraPreview, ReplayKitCameraPreviewHandler> Mapper
        = new PropertyMapper<ReplayKitCameraPreview, ReplayKitCameraPreviewHandler>(ViewMapper)
        {
            [nameof(ReplayKitCameraPreview.IsActive)] = MapIsActive
        };

    public ReplayKitCameraPreviewHandler() : base(Mapper)
    {
    }

    protected override UIView CreatePlatformView()
    {
        var container = new UIView
        {
            BackgroundColor = UIColor.Clear
        };

        return container;
    }

    private static void MapIsActive(ReplayKitCameraPreviewHandler handler, ReplayKitCameraPreview view)
    {
        if (handler.PlatformView == null)
            return;

        if (view.IsActive)
            handler.EnsurePreview(handler.PlatformView);
        else
            handler.StopAll(handler.PlatformView);
    }

    private void EnsurePreview(UIView platformView)
    {
        // En todas las plataformas, intentamos primero usar ReplayKit CameraPreviewView
        // ya que es el que funciona cuando la cámara está siendo usada por la grabación
        var attachedReplayKit = TryAttachReplayKitPreview(platformView);
        if (attachedReplayKit)
        {
            StopFallbackCamera();
            return;
        }

#if IOS
        // En iOS, si ReplayKit no proporciona preview, esperamos un poco y reintentamos
        // ya que el preview puede tardar en estar disponible después de iniciar la grabación
        _attachTimer?.Invalidate();
        _attachTimer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100), _ =>
        {
            try
            {
                var active = VirtualView?.IsActive == true;
                if (!active)
                {
                    _attachTimer?.Invalidate();
                    _attachTimer = null;
                    return;
                }

                if (TryAttachReplayKitPreview(platformView))
                {
                    _attachTimer?.Invalidate();
                    _attachTimer = null;
                }
            }
            catch
            {
                _attachTimer?.Invalidate();
                _attachTimer = null;
            }
        });
#else
        // En MacCatalyst, usamos fallback a AVCaptureSession si ReplayKit no da preview
        EnsureFallbackCamera(platformView);

        // Opcional: reintentar enganchar ReplayKit por si aparece al iniciar grabación.
        _attachTimer?.Invalidate();
        _attachTimer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(500), _ =>
        {
            try
            {
                var active = VirtualView?.IsActive == true;
                if (!active)
                {
                    _attachTimer?.Invalidate();
                    _attachTimer = null;
                    return;
                }

                if (TryAttachReplayKitPreview(platformView))
                {
                    StopFallbackCamera();
                    _attachTimer?.Invalidate();
                    _attachTimer = null;
                }
            }
            catch
            {
                // Si algo falla, paramos el timer para evitar loops de crash.
                _attachTimer?.Invalidate();
                _attachTimer = null;
            }
        });
#endif
    }

    private bool TryAttachReplayKitPreview(UIView platformView)
    {
        if (_attachedPreview != null && _attachedPreview.Superview == platformView)
            return true;

        var recorder = RPScreenRecorder.SharedRecorder;
        var preview = recorder.CameraPreviewView;
        if (preview == null)
            return false;

        preview.TranslatesAutoresizingMaskIntoConstraints = true;
        preview.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        preview.Frame = platformView.Bounds;

        foreach (var sub in platformView.Subviews)
            sub.RemoveFromSuperview();

        platformView.AddSubview(preview);
        _attachedPreview = preview;
        return true;
    }

    private static AVCaptureDevice? TryGetFrontCamera()
    {
        try
        {
            var discovery = AVCaptureDeviceDiscoverySession.Create(
                new[] { AVCaptureDeviceType.BuiltInWideAngleCamera },
                AVMediaTypes.Video,
                AVCaptureDevicePosition.Unspecified);

            return discovery.Devices.FirstOrDefault(d => d.Position == AVCaptureDevicePosition.Front)
                   ?? discovery.Devices.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private async void EnsureFallbackCamera(UIView platformView)
    {
        if (_captureSession != null && _captureLayer != null)
            return;

        // Permisos cámara
        try
        {
            var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
            if (status == AVAuthorizationStatus.NotDetermined)
            {
                var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
                if (!granted)
                    return;
            }
            else if (status != AVAuthorizationStatus.Authorized)
            {
                return;
            }
        }
        catch
        {
            // Si no podemos consultar permisos, evitamos crashear.
        }

        var device = TryGetFrontCamera();
        if (device == null)
            return;

        NSError? error;
        var input = AVCaptureDeviceInput.FromDevice(device, out error);
        if (error != null || input == null)
            return;

        var session = new AVCaptureSession();
        session.SessionPreset = AVCaptureSession.Preset640x480;

        if (session.CanAddInput(input))
            session.AddInput(input);

        var layer = new AVCaptureVideoPreviewLayer(session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = platformView.Bounds
        };

#if IOS
        // Configurar la orientación correcta para iOS
        UpdateLayerOrientation(layer);
        
        // Observar cambios de orientación
        _orientationObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            UIDevice.OrientationDidChangeNotification,
            _ => MainThread.BeginInvokeOnMainThread(() => UpdateLayerOrientation(layer)));
#endif

        // Limpieza de layers previas
        StopFallbackCamera();

        platformView.Layer.AddSublayer(layer);
        _captureSession = session;
        _captureLayer = layer;

        // Iniciar la sesión en la cola de DispatchQueue para iOS
        DispatchQueue.DefaultGlobalQueue.DispatchAsync(() =>
        {
            try
            {
                session.StartRunning();
            }
            catch
            {
                // Ignorar errores al iniciar
            }
        });
    }

#if IOS
    private void UpdateLayerOrientation(AVCaptureVideoPreviewLayer layer)
    {
        if (layer.Connection == null || !layer.Connection.SupportsVideoOrientation)
            return;

        // Obtener la orientación de la interfaz en lugar de la del dispositivo
        var windowScene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault();
        
        var interfaceOrientation = windowScene?.InterfaceOrientation ?? UIInterfaceOrientation.Portrait;
        AVCaptureVideoOrientation videoOrientation;

        switch (interfaceOrientation)
        {
            case UIInterfaceOrientation.LandscapeLeft:
                videoOrientation = AVCaptureVideoOrientation.LandscapeLeft;
                break;
            case UIInterfaceOrientation.LandscapeRight:
                videoOrientation = AVCaptureVideoOrientation.LandscapeRight;
                break;
            case UIInterfaceOrientation.PortraitUpsideDown:
                videoOrientation = AVCaptureVideoOrientation.PortraitUpsideDown;
                break;
            default:
                videoOrientation = AVCaptureVideoOrientation.Portrait;
                break;
        }

        layer.Connection.VideoOrientation = videoOrientation;
    }
#endif

    private void StopFallbackCamera()
    {
#if IOS
        // Remover observer de orientación
        if (_orientationObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_orientationObserver);
            _orientationObserver = null;
        }
#endif

        try
        {
            if (_captureSession != null && _captureSession.Running)
                _captureSession.StopRunning();
        }
        catch
        {
        }

        try
        {
            _captureLayer?.RemoveFromSuperLayer();
        }
        catch
        {
        }

        _captureLayer?.Dispose();
        _captureLayer = null;

        _captureSession?.Dispose();
        _captureSession = null;
    }

    private void StopAll(UIView? platformView)
    {
        _attachTimer?.Invalidate();
        _attachTimer = null;

        _attachedPreview = null;
        StopFallbackCamera();

        // Quitamos subviews (por si el preview de ReplayKit estaba incrustado)
        if (platformView != null)
        {
            foreach (var sub in platformView.Subviews)
                sub.RemoveFromSuperview();
        }
    }

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);

        if (VirtualView?.IsActive == true)
            EnsurePreview(platformView);
    }

    protected override void DisconnectHandler(UIView platformView)
    {
        try
        {
            _attachTimer?.Invalidate();
            _attachTimer = null;
        }
        catch { }

        try
        {
            StopAll(platformView);
        }
        catch (Exception ex)
        {
            CrownRFEP_Reader.Services.AppLog.Error("ReplayKitCameraPreviewHandler", "DisconnectHandler StopAll threw", ex);
        }

        base.DisconnectHandler(platformView);
    }

    public override void PlatformArrange(Rect frame)
    {
        base.PlatformArrange(frame);
        if (PlatformView == null)
            return;

        _attachedPreview?.SetNeedsLayout();
        if (_attachedPreview != null)
            _attachedPreview.Frame = PlatformView.Bounds;

        if (_captureLayer != null)
            _captureLayer.Frame = PlatformView.Bounds;
    }
}
#endif

#if WINDOWS
using Microsoft.Maui.Handlers;
using CrownRFEP_Reader.Views.Controls;
using WinBorder = Microsoft.UI.Xaml.Controls.Border;

namespace CrownRFEP_Reader.Handlers;

/// <summary>
/// Handler stub para ReplayKitCameraPreview en Windows.
/// ReplayKit no está disponible en Windows, este es solo un placeholder.
/// </summary>
public sealed class ReplayKitCameraPreviewHandler : ViewHandler<ReplayKitCameraPreview, WinBorder>
{
    public static IPropertyMapper<ReplayKitCameraPreview, ReplayKitCameraPreviewHandler> Mapper
        = new PropertyMapper<ReplayKitCameraPreview, ReplayKitCameraPreviewHandler>(ViewMapper);

    public ReplayKitCameraPreviewHandler() : base(Mapper)
    {
    }

    protected override WinBorder CreatePlatformView()
    {
        return new WinBorder
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
    }
}
#endif
