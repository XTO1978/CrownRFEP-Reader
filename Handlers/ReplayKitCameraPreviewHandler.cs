#if MACCATALYST
using AVFoundation;
using CoreAnimation;
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
            handler.StopAll();
    }

    private void EnsurePreview(UIView platformView)
    {
        // 1) Preferimos el preview nativo de ReplayKit si está disponible.
        var attachedReplayKit = TryAttachReplayKitPreview(platformView);
        if (attachedReplayKit)
        {
            StopFallbackCamera();
            return;
        }

        // 2) Si ReplayKit no lo proporciona (común en MacCatalyst), usamos cámara real.
        EnsureFallbackCamera(platformView);

        // 3) Opcional: reintentar enganchar ReplayKit por si aparece al iniciar grabación.
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
        session.SessionPreset = AVCaptureSession.PresetMedium;

        if (session.CanAddInput(input))
            session.AddInput(input);

        var layer = new AVCaptureVideoPreviewLayer(session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = platformView.Bounds
        };

        // Limpieza de layers previas
        StopFallbackCamera();

        platformView.Layer.AddSublayer(layer);
        _captureSession = session;
        _captureLayer = layer;

        session.StartRunning();
    }

    private void StopFallbackCamera()
    {
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

    private void StopAll()
    {
        _attachTimer?.Invalidate();
        _attachTimer = null;

        _attachedPreview = null;
        StopFallbackCamera();

        // Quitamos subviews (por si el preview de ReplayKit estaba incrustado)
        if (PlatformView != null)
        {
            foreach (var sub in PlatformView.Subviews)
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
            StopAll();
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
