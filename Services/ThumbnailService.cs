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
#else
        return await Task.FromResult(0.0);
#endif
    }
}
