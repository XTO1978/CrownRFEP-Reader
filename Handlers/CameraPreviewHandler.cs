#if IOS
using AVFoundation;
using CoreAnimation;
using CoreGraphics;
using Microsoft.Maui.Handlers;
using UIKit;
using CrownRFEP_Reader.Controls;

namespace CrownRFEP_Reader.Handlers;

/// <summary>
/// Handler de iOS para el control CameraPreview.
/// Conecta el AVCaptureVideoPreviewLayer nativo con el control MAUI.
/// </summary>
public class CameraPreviewHandler : ViewHandler<CameraPreview, UIView>
{
    private UIView? _containerView;
    private AVCaptureVideoPreviewLayer? _previewLayer;

    public static IPropertyMapper<CameraPreview, CameraPreviewHandler> PropertyMapper = new PropertyMapper<CameraPreview, CameraPreviewHandler>(ViewMapper)
    {
        [nameof(CameraPreview.PreviewHandle)] = MapPreviewHandle
    };

    public CameraPreviewHandler() : base(PropertyMapper)
    {
    }

    protected override UIView CreatePlatformView()
    {
        _containerView = new UIView
        {
            BackgroundColor = UIColor.Black,
            ClipsToBounds = false // Permitir que el contenido se extienda fuera de los límites
        };
        return _containerView;
    }

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);
        UpdatePreviewLayer();
    }

    protected override void DisconnectHandler(UIView platformView)
    {
        base.DisconnectHandler(platformView);
        
        if (_previewLayer != null)
        {
            _previewLayer.RemoveFromSuperLayer();
            _previewLayer = null;
        }
    }

    private static void MapPreviewHandle(CameraPreviewHandler handler, CameraPreview view)
    {
        handler.UpdatePreviewLayer();
    }

    private void UpdatePreviewLayer()
    {
        if (_containerView == null || VirtualView == null)
            return;

        var handle = VirtualView.PreviewHandle;
        
        // Si el handle es null, limpiar el layer existente
        if (handle == null)
        {
            if (_previewLayer != null)
            {
                _previewLayer.RemoveFromSuperLayer();
                _previewLayer = null;
            }
            return;
        }
        
        if (handle is AVCaptureVideoPreviewLayer newLayer)
        {
            // Remover layer anterior si existe
            if (_previewLayer != null && _previewLayer != newLayer)
            {
                _previewLayer.RemoveFromSuperLayer();
            }

            _previewLayer = newLayer;
            
            // Calcular frame que cubra toda la pantalla incluyendo safe area
            var fullScreenFrame = CalculateFullScreenFrame();
            _previewLayer.Frame = fullScreenFrame;
            _previewLayer.VideoGravity = AVLayerVideoGravity.ResizeAspectFill;

            // Configurar la orientación del preview
            if (_previewLayer.Connection != null && _previewLayer.Connection.SupportsVideoOrientation)
            {
                _previewLayer.Connection.VideoOrientation = GetVideoOrientation();
            }

            // Agregar al contenedor si no está ya
            if (_previewLayer.SuperLayer != _containerView.Layer)
            {
                _containerView.Layer.InsertSublayer(_previewLayer, 0);
            }
        }
    }

    /// <summary>
    /// Calcula el frame necesario para cubrir toda la pantalla,
    /// compensando el offset del safe area con márgenes negativos
    /// </summary>
    private CGRect CalculateFullScreenFrame()
    {
        var screenBounds = UIScreen.MainScreen.Bounds;
        
        // Obtener el safe area insets de la ventana
        var windowScene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault();

        if (windowScene?.Windows.FirstOrDefault(w => w.IsKeyWindow) is UIWindow keyWindow)
        {
            var safeAreaInsets = keyWindow.SafeAreaInsets;
            
            // El contenedor está posicionado dentro del safe area,
            // así que necesitamos expandir el preview layer con offsets negativos
            return new CGRect(
                -safeAreaInsets.Left,
                -safeAreaInsets.Top,
                screenBounds.Width,
                screenBounds.Height
            );
        }

        return screenBounds;
    }

    public override void PlatformArrange(Rect frame)
    {
        base.PlatformArrange(frame);
        
        // Actualizar frame del preview layer cuando cambia el tamaño
        if (_previewLayer != null && _containerView != null)
        {
            // Recalcular para cubrir pantalla completa
            _previewLayer.Frame = CalculateFullScreenFrame();
        }
    }

    private static AVCaptureVideoOrientation GetVideoOrientation()
    {
        var windowScene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault();

        if (windowScene == null)
            return AVCaptureVideoOrientation.Portrait;

        return windowScene.InterfaceOrientation switch
        {
            UIInterfaceOrientation.Portrait => AVCaptureVideoOrientation.Portrait,
            UIInterfaceOrientation.PortraitUpsideDown => AVCaptureVideoOrientation.PortraitUpsideDown,
            UIInterfaceOrientation.LandscapeLeft => AVCaptureVideoOrientation.LandscapeLeft,
            UIInterfaceOrientation.LandscapeRight => AVCaptureVideoOrientation.LandscapeRight,
            _ => AVCaptureVideoOrientation.Portrait
        };
    }
}
#endif
