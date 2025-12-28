#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CrownRFEP_Reader.Views.Controls;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Graphics.Imaging;
using Windows.Media.MediaProperties;
using System.Diagnostics;
using WinBorder = Microsoft.UI.Xaml.Controls.Border;

namespace CrownRFEP_Reader.Handlers;

/// <summary>
/// Handler para WebcamPreview en Windows usando MediaCapture con FrameReader.
/// WinUI 3 no tiene CaptureElement, así que usamos un Border con ImageBrush
/// que se actualiza con frames de la cámara.
/// </summary>
public partial class WebcamPreviewHandler : ViewHandler<WebcamPreview, WinBorder>
{
    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private SoftwareBitmapSource? _bitmapSource;
    private Microsoft.UI.Xaml.Controls.Image? _image;
    private bool _isInitialized;
    private bool _isPreviewing;
    private long _framesSeen;
    private int _loggedNoBitmap;
    private int _uiUpdateInProgress;

    public static IPropertyMapper<WebcamPreview, WebcamPreviewHandler> PropertyMapper =
        new PropertyMapper<WebcamPreview, WebcamPreviewHandler>(ViewMapper)
        {
            [nameof(WebcamPreview.IsActive)] = MapIsActive
        };

    public WebcamPreviewHandler() : base(PropertyMapper) { }

    protected override WinBorder CreatePlatformView()
    {
        var border = new WinBorder
        {
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
        };
        
        // Crear el Image que mostrará los frames (source persistente para evitar parpadeo)
        _bitmapSource ??= new SoftwareBitmapSource();
        var image = new Microsoft.UI.Xaml.Controls.Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            Source = _bitmapSource
        };
        _image = image;
        border.Child = _image;
        
        return border;
    }

    protected override void DisconnectHandler(WinBorder platformView)
    {
        base.DisconnectHandler(platformView);
        _ = StopPreviewAsync();
        CleanupMediaCapture();
    }

    private static async void MapIsActive(WebcamPreviewHandler handler, WebcamPreview view)
    {
        try
        {
            if (view.IsActive)
            {
                await handler.StartPreviewAsync();
            }
            else
            {
                await handler.StopPreviewAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebcamPreviewHandler] Error en MapIsActive: {ex.Message}");
        }
    }

    private async Task StartPreviewAsync()
    {
        if (_isPreviewing)
            return;

        try
        {
            if (!_isInitialized)
            {
                await InitializeMediaCaptureAsync();
            }

            if (_frameReader != null)
            {
                _frameReader.FrameArrived += FrameReader_FrameArrived;
                var result = await _frameReader.StartAsync();
                if (result == MediaFrameReaderStartStatus.Success)
                {
                    _isPreviewing = true;
                    Debug.WriteLine("[WebcamPreviewHandler] Preview iniciado");
                }
                else
                {
                    Debug.WriteLine($"[WebcamPreviewHandler] Error al iniciar FrameReader: {result}");
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            Debug.WriteLine("[WebcamPreviewHandler] Acceso a cámara denegado. Verifica permisos.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebcamPreviewHandler] Error al iniciar preview: {ex.Message}");
        }
    }

    private async Task StopPreviewAsync()
    {
        if (!_isPreviewing)
            return;

        try
        {
            if (_frameReader != null)
            {
                _frameReader.FrameArrived -= FrameReader_FrameArrived;
                await _frameReader.StopAsync();
            }
            _isPreviewing = false;
            
            Debug.WriteLine("[WebcamPreviewHandler] Preview detenido");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebcamPreviewHandler] Error al detener preview: {ex.Message}");
        }
    }

    private void FrameReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        try
        {
            // Evitar solapado de SetBitmapAsync en el hilo UI (causa parpadeos intermitentes)
            if (Interlocked.CompareExchange(ref _uiUpdateInProgress, 1, 0) != 0)
                return;

            using var frameReference = sender.TryAcquireLatestFrame();
            var mediaFrame = frameReference?.VideoMediaFrame;
            if (mediaFrame == null)
            {
                Interlocked.Exchange(ref _uiUpdateInProgress, 0);
                return;
            }

            var softwareBitmap = mediaFrame.SoftwareBitmap;
            if (softwareBitmap == null)
            {
                if (Interlocked.Exchange(ref _loggedNoBitmap, 1) == 0)
                    Debug.WriteLine("[WebcamPreviewHandler] Frame recibido pero SoftwareBitmap es null (¿subtype incorrecto?)");
                Interlocked.Exchange(ref _uiUpdateInProgress, 0);
                return;
            }
            
            // Convertir a formato compatible con ImageSource si es necesario
            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                softwareBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                softwareBitmap = SoftwareBitmap.Convert(
                    softwareBitmap, 
                    BitmapPixelFormat.Bgra8, 
                    BitmapAlphaMode.Premultiplied);
            }

            // Hacer una copia para usar en el UI thread
            var bitmapCopy = SoftwareBitmap.Copy(softwareBitmap);
            Interlocked.Increment(ref _framesSeen);

            // Actualizar en el UI thread (no usar async lambda directa: puede solaparse)
            var enqueued = PlatformView?.DispatcherQueue.TryEnqueue(() =>
            {
                _ = UpdateImageAsync(bitmapCopy);
            });

            if (enqueued == false)
            {
                // Si no se pudo encolar, liberar copia para evitar fugas.
                try { bitmapCopy.Dispose(); } catch { }
                Interlocked.Exchange(ref _uiUpdateInProgress, 0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebcamPreviewHandler] Error procesando frame: {ex.Message}");
            Interlocked.Exchange(ref _uiUpdateInProgress, 0);
        }
    }

    private async Task UpdateImageAsync(SoftwareBitmap bitmapCopy)
    {
        try
        {
            _bitmapSource ??= new SoftwareBitmapSource();
            await _bitmapSource.SetBitmapAsync(bitmapCopy);

            // Mantener el Source estable (evita parpadeo)
            if (_image != null && _image.Source != _bitmapSource)
                _image.Source = _bitmapSource;

            var seen = Interlocked.Read(ref _framesSeen);
            if (seen == 1 || seen == 30)
                Debug.WriteLine($"[WebcamPreviewHandler] Frames mostrados: {seen}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebcamPreviewHandler] Error actualizando frame: {ex.Message}");
        }
        finally
        {
            try { bitmapCopy.Dispose(); } catch { }
            Interlocked.Exchange(ref _uiUpdateInProgress, 0);
        }
    }

    private async Task InitializeMediaCaptureAsync()
    {
        if (_isInitialized)
            return;

        try
        {
            _mediaCapture = new MediaCapture();

            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MediaCategory = MediaCategory.Communications,
                SourceGroup = await FindCameraSourceGroupAsync(),
                // IMPORTANTE: si no es CPU, WinUI suele entregar Direct3DSurface y SoftwareBitmap==null
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            };

            await _mediaCapture.InitializeAsync(settings);

            // Crear FrameReader para obtener frames de video
            var frameSource = _mediaCapture.FrameSources.Values.FirstOrDefault(
                                  source => source.Info.MediaStreamType == MediaStreamType.VideoPreview)
                              ?? _mediaCapture.FrameSources.Values.FirstOrDefault(
                                  source => source.Info.MediaStreamType == MediaStreamType.VideoRecord);

            if (frameSource == null)
            {
                Debug.WriteLine("[WebcamPreviewHandler] No se encontró fuente de frames");
                return;
            }

            // Preferir formato de baja resolución
            var preferredFormat = frameSource.SupportedFormats
                .Where(f => f.VideoFormat.Width <= 640)
                .OrderByDescending(f => f.VideoFormat.Width)
                .FirstOrDefault();

            if (preferredFormat != null)
            {
                await frameSource.SetFormatAsync(preferredFormat);
                Debug.WriteLine($"[WebcamPreviewHandler] Formato: {preferredFormat.VideoFormat.Width}x{preferredFormat.VideoFormat.Height}");
            }

            // IMPORTANTE: pedir BGRA8 para que VideoMediaFrame.SoftwareBitmap no sea null.
            _frameReader = await _mediaCapture.CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Bgra8);

            _isInitialized = true;
            Debug.WriteLine("[WebcamPreviewHandler] MediaCapture inicializado con FrameReader");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebcamPreviewHandler] Error al inicializar MediaCapture: {ex.Message}");
            CleanupMediaCapture();
        }
    }

    private async Task<MediaFrameSourceGroup?> FindCameraSourceGroupAsync()
    {
        var sourceGroups = await MediaFrameSourceGroup.FindAllAsync();
        
        // Buscar cámara frontal/webcam
        foreach (var group in sourceGroups)
        {
            var videoSource = group.SourceInfos.FirstOrDefault(
                info => info.MediaStreamType == MediaStreamType.VideoPreview ||
                        info.MediaStreamType == MediaStreamType.VideoRecord);

            if (videoSource != null)
            {
                // Preferir webcam frontal
                if (group.DisplayName.Contains("front", StringComparison.OrdinalIgnoreCase) ||
                    group.DisplayName.Contains("webcam", StringComparison.OrdinalIgnoreCase) ||
                    group.DisplayName.Contains("camera", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[WebcamPreviewHandler] Usando cámara: {group.DisplayName}");
                    return group;
                }
            }
        }

        // Usar la primera disponible
        var fallback = sourceGroups.FirstOrDefault(g => 
            g.SourceInfos.Any(info => 
                info.MediaStreamType == MediaStreamType.VideoPreview ||
                info.MediaStreamType == MediaStreamType.VideoRecord));

        if (fallback != null)
        {
            Debug.WriteLine($"[WebcamPreviewHandler] Usando cámara (fallback): {fallback.DisplayName}");
        }

        return fallback;
    }

    private void CleanupMediaCapture()
    {
        _isPreviewing = false;
        _isInitialized = false;
        Interlocked.Exchange(ref _uiUpdateInProgress, 0);
        
        if (_frameReader != null)
        {
            _frameReader.Dispose();
            _frameReader = null;
        }

        if (_mediaCapture != null)
        {
            _mediaCapture.Dispose();
            _mediaCapture = null;
        }

        _bitmapSource = null;
        _image = null;
    }
}
#endif
