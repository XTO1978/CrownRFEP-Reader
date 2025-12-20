using System;
using System.IO;
using System.Threading.Tasks;

#if MACCATALYST
using AVFoundation;
using CoreMedia;
using CoreGraphics;
using UIKit;
#endif

namespace CrownRFEP_Reader.Services;

/// <summary>
/// Servicio para generar thumbnails de archivos de video
/// </summary>
public class ThumbnailService
{
    private const int ThumbnailWidth = 320;
    private const int ThumbnailHeight = 180;
    private const double ThumbnailTimeSeconds = 1.0; // Capturar frame al segundo 1

    /// <summary>
    /// Genera un thumbnail para un archivo de video
    /// </summary>
    /// <param name="videoPath">Ruta del archivo de video</param>
    /// <param name="outputPath">Ruta donde guardar el thumbnail</param>
    /// <returns>True si se generó correctamente, false en caso contrario</returns>
    public async Task<bool> GenerateThumbnailAsync(string videoPath, string outputPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            System.Diagnostics.Debug.WriteLine($"ThumbnailService: Video file not found: {videoPath}");
            return false;
        }

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
            // Para otras plataformas, retornar false por ahora
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
