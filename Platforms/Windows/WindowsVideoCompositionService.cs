using CrownRFEP_Reader.Services;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using WinFoundation = Windows.Foundation;

namespace CrownRFEP_Reader.Platforms.Windows;

/// <summary>
/// Servicio de composición de video para Windows usando Windows.Media.Editing
/// </summary>
public class WindowsVideoCompositionService : IVideoCompositionService
{
    public bool IsAvailable => true;

    public async Task<VideoExportResult> ExportParallelVideosAsync(
        ParallelVideoExportParams parameters,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Obtener archivos de video
            var video1File = await StorageFile.GetFileFromPathAsync(parameters.Video1Path);
            var video2File = await StorageFile.GetFileFromPathAsync(parameters.Video2Path);

            // Crear clips de media
            var clip1 = await MediaClip.CreateFromFileAsync(video1File);
            var clip2 = await MediaClip.CreateFromFileAsync(video2File);

            // Aplicar offset de inicio si es necesario
            if (parameters.Video1StartPosition > TimeSpan.Zero)
            {
                clip1.TrimTimeFromStart = parameters.Video1StartPosition;
            }
            if (parameters.Video2StartPosition > TimeSpan.Zero)
            {
                clip2.TrimTimeFromStart = parameters.Video2StartPosition;
            }

            // Calcular duración
            var duration1 = clip1.OriginalDuration - parameters.Video1StartPosition;
            var duration2 = clip2.OriginalDuration - parameters.Video2StartPosition;
            var minDuration = duration1 < duration2 ? duration1 : duration2;
            
            if (parameters.MaxDuration.HasValue && parameters.MaxDuration.Value < minDuration)
            {
                minDuration = parameters.MaxDuration.Value;
            }

            // Recortar clips a la duración deseada
            clip1.TrimTimeFromEnd = clip1.OriginalDuration - parameters.Video1StartPosition - minDuration;
            clip2.TrimTimeFromEnd = clip2.OriginalDuration - parameters.Video2StartPosition - minDuration;

            // Crear composición
            var composition = new MediaComposition();
            composition.Clips.Add(clip1);

            // Crear overlay track para el segundo video
            var overlayTrack = new MediaOverlayLayer();
            
            // Configurar posición del overlay según el layout
            var overlayClip = new MediaOverlay(clip2);
            overlayClip.Position = new WinFoundation.Rect(
                parameters.IsHorizontalLayout ? 0.5 : 0,     // X: mitad derecha si horizontal
                parameters.IsHorizontalLayout ? 0 : 0.5,     // Y: mitad inferior si vertical
                parameters.IsHorizontalLayout ? 0.5 : 1.0,   // Width
                parameters.IsHorizontalLayout ? 1.0 : 0.5    // Height
            );
            overlayClip.Delay = TimeSpan.Zero;

            overlayTrack.Overlays.Add(overlayClip);
            composition.OverlayLayers.Add(overlayTrack);

            // Configurar encoding
            var encodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            
            // Configurar resolución según layout
            if (parameters.IsHorizontalLayout)
            {
                // Lado a lado: 3840x1080 (dos 1920x1080)
                encodingProfile.Video!.Width = 3840;
                encodingProfile.Video.Height = 1080;
            }
            else
            {
                // Arriba/abajo: 1920x2160 (dos 1920x1080 apilados)
                encodingProfile.Video!.Width = 1920;
                encodingProfile.Video.Height = 2160;
            }

            // Crear archivo de salida
            var outputFolder = await StorageFolder.GetFolderFromPathAsync(
                System.IO.Path.GetDirectoryName(parameters.OutputPath)!);
            var outputFile = await outputFolder.CreateFileAsync(
                System.IO.Path.GetFileName(parameters.OutputPath),
                CreationCollisionOption.ReplaceExisting);

            // Renderizar
            var renderOperation = composition.RenderToFileAsync(outputFile, MediaTrimmingPreference.Precise, encodingProfile);
            
            renderOperation.Progress = (info, progressValue) =>
            {
                progress?.Report(progressValue / 100.0);
            };

            cancellationToken.Register(() => renderOperation.Cancel());
            
            var result = await renderOperation;

            if (result != TranscodeFailureReason.None)
            {
                return new VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"Transcoding failed: {result}"
                };
            }

            var props = await outputFile.GetBasicPropertiesAsync();
            
            return new VideoExportResult
            {
                Success = true,
                OutputPath = parameters.OutputPath,
                FileSizeBytes = (long)props.Size,
                Duration = minDuration
            };
        }
        catch (Exception ex)
        {
            return new VideoExportResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<VideoExportResult> ExportQuadVideosAsync(
        QuadVideoExportParams parameters,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Obtener archivos de video
            var video1File = await StorageFile.GetFileFromPathAsync(parameters.Video1Path);
            var video2File = await StorageFile.GetFileFromPathAsync(parameters.Video2Path);
            var video3File = await StorageFile.GetFileFromPathAsync(parameters.Video3Path);
            var video4File = await StorageFile.GetFileFromPathAsync(parameters.Video4Path);

            // Crear clips de media
            var clip1 = await MediaClip.CreateFromFileAsync(video1File);
            var clip2 = await MediaClip.CreateFromFileAsync(video2File);
            var clip3 = await MediaClip.CreateFromFileAsync(video3File);
            var clip4 = await MediaClip.CreateFromFileAsync(video4File);

            // Aplicar offset de inicio si es necesario
            if (parameters.Video1StartPosition > TimeSpan.Zero)
                clip1.TrimTimeFromStart = parameters.Video1StartPosition;
            if (parameters.Video2StartPosition > TimeSpan.Zero)
                clip2.TrimTimeFromStart = parameters.Video2StartPosition;
            if (parameters.Video3StartPosition > TimeSpan.Zero)
                clip3.TrimTimeFromStart = parameters.Video3StartPosition;
            if (parameters.Video4StartPosition > TimeSpan.Zero)
                clip4.TrimTimeFromStart = parameters.Video4StartPosition;

            // Calcular duración mínima
            var durations = new[]
            {
                clip1.OriginalDuration - parameters.Video1StartPosition,
                clip2.OriginalDuration - parameters.Video2StartPosition,
                clip3.OriginalDuration - parameters.Video3StartPosition,
                clip4.OriginalDuration - parameters.Video4StartPosition
            };
            var minDuration = durations.Min();
            
            if (parameters.MaxDuration.HasValue && parameters.MaxDuration.Value < minDuration)
            {
                minDuration = parameters.MaxDuration.Value;
            }

            // Recortar clips a la duración deseada
            clip1.TrimTimeFromEnd = clip1.OriginalDuration - parameters.Video1StartPosition - minDuration;
            clip2.TrimTimeFromEnd = clip2.OriginalDuration - parameters.Video2StartPosition - minDuration;
            clip3.TrimTimeFromEnd = clip3.OriginalDuration - parameters.Video3StartPosition - minDuration;
            clip4.TrimTimeFromEnd = clip4.OriginalDuration - parameters.Video4StartPosition - minDuration;

            // Crear composición con el primer clip como base
            var composition = new MediaComposition();
            composition.Clips.Add(clip1);

            // Crear overlay layer para los otros 3 videos
            var overlayTrack = new MediaOverlayLayer();

            // Clip 2 - Arriba derecha
            var overlay2 = new MediaOverlay(clip2)
            {
                Position = new WinFoundation.Rect(0.5, 0, 0.5, 0.5),
                Delay = TimeSpan.Zero
            };
            overlayTrack.Overlays.Add(overlay2);

            // Clip 3 - Abajo izquierda
            var overlay3 = new MediaOverlay(clip3)
            {
                Position = new WinFoundation.Rect(0, 0.5, 0.5, 0.5),
                Delay = TimeSpan.Zero
            };
            overlayTrack.Overlays.Add(overlay3);

            // Clip 4 - Abajo derecha
            var overlay4 = new MediaOverlay(clip4)
            {
                Position = new WinFoundation.Rect(0.5, 0.5, 0.5, 0.5),
                Delay = TimeSpan.Zero
            };
            overlayTrack.Overlays.Add(overlay4);

            composition.OverlayLayers.Add(overlayTrack);

            // Configurar encoding - 2x2 grid a 1920x1080 total
            var encodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            encodingProfile.Video!.Width = 1920;
            encodingProfile.Video.Height = 1080;

            // Crear archivo de salida
            var outputFolder = await StorageFolder.GetFolderFromPathAsync(
                System.IO.Path.GetDirectoryName(parameters.OutputPath)!);
            var outputFile = await outputFolder.CreateFileAsync(
                System.IO.Path.GetFileName(parameters.OutputPath),
                CreationCollisionOption.ReplaceExisting);

            // Renderizar
            var renderOperation = composition.RenderToFileAsync(outputFile, MediaTrimmingPreference.Precise, encodingProfile);
            
            renderOperation.Progress = (info, progressValue) =>
            {
                progress?.Report(progressValue / 100.0);
            };

            cancellationToken.Register(() => renderOperation.Cancel());
            
            var result = await renderOperation;

            if (result != TranscodeFailureReason.None)
            {
                return new VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"Transcoding failed: {result}"
                };
            }

            var props = await outputFile.GetBasicPropertiesAsync();
            
            return new VideoExportResult
            {
                Success = true,
                OutputPath = parameters.OutputPath,
                FileSizeBytes = (long)props.Size,
                Duration = minDuration
            };
        }
        catch (Exception ex)
        {
            return new VideoExportResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
