using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#if MACCATALYST
using AVFoundation;
using CoreMedia;
using CoreGraphics;
using UIKit;
#endif

#if WINDOWS
using Windows.Media.Editing;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
#endif

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio para generar thumbnails de archivos de video con cola de procesamiento
/// </summary>
public class ThumbnailService
{
    private const int ThumbnailWidth = 320;
    private const int ThumbnailHeight = 180;
    private const double ThumbnailTimeSeconds = 1.0;
    private const int MaxConcurrentGenerations = 3;

    private readonly SemaphoreSlim _generationSemaphore = new(MaxConcurrentGenerations);
    private readonly ConcurrentDictionary<string, Task<bool>> _pendingGenerations = new();

    /// <summary>
    /// Genera un thumbnail para un archivo de video (con semáforo para limitar concurrencia)
    /// </summary>
    public async Task<bool> GenerateThumbnailAsync(string videoPath, string outputPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            System.Diagnostics.Debug.WriteLine($"ThumbnailService: Video file not found: {videoPath}");
            return false;
        }

        // Si ya existe el thumbnail, retornar true inmediatamente
        if (File.Exists(outputPath))
            return true;

        // Si ya hay una generación pendiente para este archivo, esperar a que termine
        if (_pendingGenerations.TryGetValue(outputPath, out var existingTask))
            return await existingTask;

        var tcs = new TaskCompletionSource<bool>();
        if (!_pendingGenerations.TryAdd(outputPath, tcs.Task))
        {
            // Otro hilo añadió primero, usar su tarea
            if (_pendingGenerations.TryGetValue(outputPath, out var otherTask))
                return await otherTask;
        }

        try
        {
            await _generationSemaphore.WaitAsync();
            try
            {
                // Double-check después de obtener el semáforo
                if (File.Exists(outputPath))
                {
                    tcs.SetResult(true);
                    return true;
                }

                var result = await GenerateThumbnailInternalAsync(videoPath, outputPath);
                tcs.SetResult(result);
                return result;
            }
            finally
            {
                _generationSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
        finally
        {
            _pendingGenerations.TryRemove(outputPath, out _);
        }
    }

    private async Task<bool> GenerateThumbnailInternalAsync(string videoPath, string outputPath)
    {
        try
        {
            // Asegurar que el directorio de destino existe
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

#if MACCATALYST
            return await GenerateThumbnailMacAsync(videoPath, outputPath);
#elif WINDOWS
            return await GenerateThumbnailWindowsAsync(videoPath, outputPath);
#else
            System.Diagnostics.Debug.WriteLine("ThumbnailService: Thumbnail generation not supported on this platform");
            return false;
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ThumbnailService: Error generating thumbnail: {ex.Message}");
            return false;
        }
    }

#if MACCATALYST
    private async Task<bool> GenerateThumbnailMacAsync(string videoPath, string outputPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var url = Foundation.NSUrl.FromFilename(videoPath);
                if (url == null)
                {
                    System.Diagnostics.Debug.WriteLine("ThumbnailService: Could not create URL from path");
                    return false;
                }

                using var asset = AVAsset.FromUrl(url);
                if (asset == null)
                {
                    System.Diagnostics.Debug.WriteLine("ThumbnailService: Could not load asset");
                    return false;
                }

                using var imageGenerator = new AVAssetImageGenerator(asset);
                imageGenerator.AppliesPreferredTrackTransform = true;
                imageGenerator.MaximumSize = new CGSize(ThumbnailWidth, ThumbnailHeight);

                // Tiempo para capturar el frame
                var time = CMTime.FromSeconds(ThumbnailTimeSeconds, 600);
                
                // Generar imagen
                var cgImage = imageGenerator.CopyCGImageAtTime(time, out _, out var error);
                
                if (cgImage == null)
                {
                    // Si falla al segundo 1, intentar en el segundo 0
                    time = CMTime.Zero;
                    cgImage = imageGenerator.CopyCGImageAtTime(time, out _, out error);
                }

                if (cgImage == null)
                {
                    System.Diagnostics.Debug.WriteLine($"ThumbnailService: Could not generate image: {error?.LocalizedDescription}");
                    return false;
                }

                // Convertir a UIImage y guardar como JPEG
                using var uiImage = new UIImage(cgImage);
                var jpegData = uiImage.AsJPEG(0.85f);
                
                if (jpegData == null)
                {
                    System.Diagnostics.Debug.WriteLine("ThumbnailService: Could not convert image to JPEG");
                    return false;
                }

                // Guardar archivo
                jpegData.Save(outputPath, atomically: true);
                
                System.Diagnostics.Debug.WriteLine($"ThumbnailService: Thumbnail generated successfully: {outputPath}");
                return File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ThumbnailService: Error in GenerateThumbnailMacAsync: {ex.Message}");
                return false;
            }
        });
    }
#endif

#if WINDOWS
    private async Task<bool> GenerateThumbnailWindowsAsync(string videoPath, string outputPath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(videoPath);
            if (file == null)
            {
                System.Diagnostics.Debug.WriteLine("ThumbnailService: Could not open video file");
                return false;
            }

            // Usar MediaClip para obtener thumbnail
            var clip = await MediaClip.CreateFromFileAsync(file);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            // Obtener thumbnail en el segundo 1 o al inicio
            var thumbnailTime = clip.OriginalDuration.TotalSeconds > 1 
                ? TimeSpan.FromSeconds(ThumbnailTimeSeconds) 
                : TimeSpan.Zero;

            var thumbnail = await composition.GetThumbnailAsync(
                thumbnailTime,
                ThumbnailWidth,
                ThumbnailHeight,
                VideoFramePrecision.NearestFrame);

            if (thumbnail == null)
            {
                System.Diagnostics.Debug.WriteLine("ThumbnailService: Could not generate thumbnail");
                return false;
            }

            // Guardar como JPEG
            var outputFile = await StorageFile.GetFileFromPathAsync(outputPath)
                .AsTask()
                .ContinueWith(t => t.IsFaulted ? null : t.Result);

            // Si no existe, crear el archivo
            if (outputFile == null)
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outputPath)!);
                outputFile = await folder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting);
            }

            using (var stream = await outputFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
                encoder.SetSoftwareBitmap(await GetSoftwareBitmapFromImageStreamAsync(thumbnail));
                encoder.BitmapTransform.ScaledWidth = (uint)ThumbnailWidth;
                encoder.BitmapTransform.ScaledHeight = (uint)ThumbnailHeight;
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
                await encoder.FlushAsync();
            }

            System.Diagnostics.Debug.WriteLine($"ThumbnailService: Thumbnail generated successfully: {outputPath}");
            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ThumbnailService: Error in GenerateThumbnailWindowsAsync: {ex.Message}");
            return false;
        }
    }

    private async Task<SoftwareBitmap> GetSoftwareBitmapFromImageStreamAsync(ImageStream imageStream)
    {
        var decoder = await BitmapDecoder.CreateAsync(imageStream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
#endif

    /// <summary>
    /// Obtiene la duración de un video en segundos
    /// </summary>
    public async Task<double> GetVideoDurationAsync(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            return 0;

#if MACCATALYST
        return await Task.Run(() =>
        {
            try
            {
                var url = Foundation.NSUrl.FromFilename(videoPath);
                if (url == null) return 0;

                using var asset = AVAsset.FromUrl(url);
                if (asset == null) return 0;

                return asset.Duration.Seconds;
            }
            catch
            {
                return 0;
            }
        });
#elif WINDOWS
        return await Task.Run(async () =>
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(videoPath);
                if (file == null) return 0.0;

                var clip = await MediaClip.CreateFromFileAsync(file);
                return clip.OriginalDuration.TotalSeconds;
            }
            catch
            {
                return 0.0;
            }
        });
#else
        return await Task.FromResult(0.0);
#endif
    }

    /// <summary>
    /// Genera un thumbnail para un video comparativo/paralelo con fondo negro
    /// respetando las proporciones del video original
    /// </summary>
    public async Task<bool> GenerateComparisonThumbnailAsync(string videoPath, string outputPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            System.Diagnostics.Debug.WriteLine($"ThumbnailService: Video file not found: {videoPath}");
            return false;
        }

        // Si ya existe el thumbnail, retornar true
        if (File.Exists(outputPath))
            return true;

        try
        {
            await _generationSemaphore.WaitAsync();
            try
            {
                // Asegurar que el directorio existe
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

#if MACCATALYST
                return await GenerateComparisonThumbnailMacAsync(videoPath, outputPath);
#elif WINDOWS
                return await GenerateThumbnailWindowsAsync(videoPath, outputPath); // Usar mismo método para Windows
#else
                return false;
#endif
            }
            finally
            {
                _generationSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ThumbnailService: Error generating comparison thumbnail: {ex.Message}");
            return false;
        }
    }

#if MACCATALYST
    /// <summary>
    /// Genera miniatura con fondo negro para videos comparativos en MacCatalyst
    /// </summary>
    private async Task<bool> GenerateComparisonThumbnailMacAsync(string videoPath, string outputPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var url = Foundation.NSUrl.FromFilename(videoPath);
                if (url == null) return false;

                using var asset = AVAsset.FromUrl(url);
                if (asset == null) return false;

                // Obtener dimensiones reales del video
                var videoTrack = asset.TracksWithMediaType(AVMediaTypes.Video.GetConstant()!).FirstOrDefault();
                CGSize videoSize;
                if (videoTrack != null)
                {
                    videoSize = videoTrack.NaturalSize;
                    // Aplicar transformación si es necesario (videos rotados)
                    var transform = videoTrack.PreferredTransform;
                    if (Math.Abs(transform.A) < 0.1 && Math.Abs(transform.D) < 0.1)
                    {
                        videoSize = new CGSize(videoSize.Height, videoSize.Width);
                    }
                }
                else
                {
                    videoSize = new CGSize(ThumbnailWidth, ThumbnailHeight);
                }

                using var imageGenerator = new AVAssetImageGenerator(asset);
                imageGenerator.AppliesPreferredTrackTransform = true;
                // No limitar tamaño para obtener mejor calidad
                imageGenerator.MaximumSize = CGSize.Empty;

                var time = CMTime.FromSeconds(ThumbnailTimeSeconds, 600);
                var cgImage = imageGenerator.CopyCGImageAtTime(time, out _, out var error);
                
                if (cgImage == null)
                {
                    time = CMTime.Zero;
                    cgImage = imageGenerator.CopyCGImageAtTime(time, out _, out error);
                }

                if (cgImage == null)
                {
                    System.Diagnostics.Debug.WriteLine($"ThumbnailService: Could not generate image: {error?.LocalizedDescription}");
                    return false;
                }

                // Calcular proporciones para centrar en canvas negro
                var canvasWidth = ThumbnailWidth;
                var canvasHeight = ThumbnailHeight;
                
                var videoAspect = videoSize.Width / videoSize.Height;
                var canvasAspect = (double)canvasWidth / canvasHeight;
                
                nfloat drawWidth, drawHeight, drawX, drawY;
                
                if (videoAspect > canvasAspect)
                {
                    // Video más ancho que el canvas - ajustar por ancho
                    drawWidth = canvasWidth;
                    drawHeight = (nfloat)(canvasWidth / videoAspect);
                    drawX = 0;
                    drawY = (canvasHeight - drawHeight) / 2;
                }
                else
                {
                    // Video más alto que el canvas - ajustar por alto
                    drawHeight = canvasHeight;
                    drawWidth = (nfloat)(canvasHeight * videoAspect);
                    drawX = (canvasWidth - drawWidth) / 2;
                    drawY = 0;
                }

                // Crear imagen con fondo negro
                UIGraphics.BeginImageContextWithOptions(new CGSize(canvasWidth, canvasHeight), true, 1.0f);
                var context = UIGraphics.GetCurrentContext();
                
                if (context == null)
                {
                    UIGraphics.EndImageContext();
                    return false;
                }

                // Fondo negro
                context.SetFillColor(UIColor.Black.CGColor);
                context.FillRect(new CGRect(0, 0, canvasWidth, canvasHeight));

                // Dibujar el frame del video centrado
                using var frameImage = new UIImage(cgImage);
                frameImage.Draw(new CGRect(drawX, drawY, drawWidth, drawHeight));

                var finalImage = UIGraphics.GetImageFromCurrentImageContext();
                UIGraphics.EndImageContext();

                if (finalImage == null) return false;

                // Guardar como JPEG
                var jpegData = finalImage.AsJPEG(0.85f);
                if (jpegData == null) return false;

                jpegData.Save(outputPath, atomically: true);
                
                System.Diagnostics.Debug.WriteLine($"ThumbnailService: Comparison thumbnail generated: {outputPath}");
                return File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ThumbnailService: Error in GenerateComparisonThumbnailMacAsync: {ex.Message}");
                return false;
            }
        });
    }
#endif
}
