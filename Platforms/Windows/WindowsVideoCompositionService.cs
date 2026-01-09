using CrownRFEP_Reader.Services;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using SkiaSharp;
using WinFoundation = Windows.Foundation;

namespace CrownRFEP_Reader.Platforms.Windows;

/// <summary>
/// Servicio de composición de video para Windows usando Windows.Media.Editing
/// </summary>
public class WindowsVideoCompositionService : IVideoCompositionService
{
    public bool IsAvailable => true;

    private static string FormatLap(TimeSpan lap)
    {
        if (lap < TimeSpan.Zero) lap = TimeSpan.Zero;
        var totalMinutes = (int)Math.Floor(lap.TotalMinutes);
        return $"{totalMinutes:00}:{lap.Seconds:00}.{lap.Milliseconds:000}";
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static async Task<StorageFile> CreateStillImageAsync(
        MediaClip sourceClip,
        TimeSpan sourceTime,
        string prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Evitar pedir un thumbnail exactamente en el final
        var safeTime = sourceTime;
        if (safeTime < TimeSpan.Zero) safeTime = TimeSpan.Zero;
        if (safeTime > sourceClip.OriginalDuration)
            safeTime = sourceClip.OriginalDuration;
        if (safeTime == sourceClip.OriginalDuration && safeTime > TimeSpan.FromMilliseconds(5))
            safeTime -= TimeSpan.FromMilliseconds(5);

        // Crear una composición temporal para obtener el thumbnail
        // MediaClip no tiene GetThumbnailAsync, pero MediaComposition sí
        var tempComposition = new MediaComposition();
        var clonedClip = sourceClip.Clone();
        clonedClip.TrimTimeFromStart = safeTime;
        clonedClip.TrimTimeFromEnd = sourceClip.OriginalDuration - safeTime - TimeSpan.FromMilliseconds(100);
        tempComposition.Clips.Add(clonedClip);

        var tempFolder = ApplicationData.Current.TemporaryFolder;
        var file = await tempFolder.CreateFileAsync(
            $"{prefix}_{Guid.NewGuid():N}.png",
            CreationCollisionOption.ReplaceExisting);

        using var thumbStream = await tempComposition.GetThumbnailAsync(
            TimeSpan.Zero,
            640,
            360,
            VideoFramePrecision.NearestFrame);

        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        await RandomAccessStream.CopyAsync(thumbStream, output);
        await output.FlushAsync();

        return file;
    }

    private static async Task<StorageFile> CreateTextOverlayImageAsync(
        string text,
        SKColor textColor,
        int width,
        int height,
        string prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = new SKBitmap(width, height, true);
        using var canvas = new SKCanvas(bitmap);
        // Fondo mate (100% opaco)
        canvas.Clear(new SKColor(0, 0, 0, 255));

        using var paint = new SKPaint
        {
            Color = textColor,
            IsAntialias = true,
            TextSize = height * 0.55f,
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        var bounds = new SKRect();
        paint.MeasureText(text, ref bounds);
        var x = (width - bounds.Width) / 2f - bounds.Left;
        var y = (height - bounds.Height) / 2f - bounds.Top;
        canvas.DrawText(text, x, y, paint);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var tempFolder = ApplicationData.Current.TemporaryFolder;
        var file = await tempFolder.CreateFileAsync(
            $"{prefix}_{Guid.NewGuid():N}.png",
            CreationCollisionOption.ReplaceExisting);

        using var ras = await file.OpenAsync(FileAccessMode.ReadWrite);
        using var stream = ras.AsStreamForWrite();
        data.SaveTo(stream);
        await stream.FlushAsync(cancellationToken);

        return file;
    }

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

            if (parameters.SyncByLaps &&
                parameters.Video1LapBoundaries?.Count >= 2 &&
                parameters.Video2LapBoundaries?.Count >= 2)
            {
                var boundaries1 = parameters.Video1LapBoundaries;
                var boundaries2 = parameters.Video2LapBoundaries;
                var segmentCount = Math.Min(boundaries1.Count, boundaries2.Count) - 1;

                var syncComposition = new MediaComposition();
                var overlayLayer = new MediaOverlayLayer();

                var cursor = TimeSpan.Zero;
                var maxTotal = parameters.MaxDuration;

                for (int i = 0; i < segmentCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var start1 = Clamp(boundaries1[i], TimeSpan.Zero, clip1.OriginalDuration);
                    var end1 = Clamp(boundaries1[i + 1], TimeSpan.Zero, clip1.OriginalDuration);
                    var start2 = Clamp(boundaries2[i], TimeSpan.Zero, clip2.OriginalDuration);
                    var end2 = Clamp(boundaries2[i + 1], TimeSpan.Zero, clip2.OriginalDuration);

                    if (end1 <= start1 || end2 <= start2)
                        break;

                    var dur1 = end1 - start1;
                    var dur2 = end2 - start2;
                    var segDuration = dur1 > dur2 ? dur1 : dur2;

                    if (maxTotal.HasValue && cursor + segDuration > maxTotal.Value)
                        break;

                    // Base (video 1)
                    var segClip1 = await MediaClip.CreateFromFileAsync(video1File);
                    segClip1.TrimTimeFromStart = start1;
                    segClip1.TrimTimeFromEnd = segClip1.OriginalDuration - end1;
                    syncComposition.Clips.Add(segClip1);

                    // Overlay (video 2)
                    var segClip2 = await MediaClip.CreateFromFileAsync(video2File);
                    segClip2.TrimTimeFromStart = start2;
                    segClip2.TrimTimeFromEnd = segClip2.OriginalDuration - end2;

                    var overlayPos = new WinFoundation.Rect(
                        parameters.IsHorizontalLayout ? 0.5 : 0,
                        parameters.IsHorizontalLayout ? 0 : 0.5,
                        parameters.IsHorizontalLayout ? 0.5 : 1.0,
                        parameters.IsHorizontalLayout ? 1.0 : 0.5);

                    overlayLayer.Overlays.Add(new MediaOverlay(segClip2)
                    {
                        Position = overlayPos,
                        Delay = cursor
                    });

                    // ===== Overlays de parciales por lap + diferencia =====
                    var green = new SKColor(52, 199, 89);
                    var red = new SKColor(255, 59, 48);
                    var white = SKColors.White;

                    var is1Better = dur1 < dur2;
                    var is2Better = dur2 < dur1;
                    var lapColor1 = is1Better ? green : (is2Better ? red : white);
                    var lapColor2 = is2Better ? green : (is1Better ? red : white);

                    var diff = dur1 - dur2;
                    if (diff < TimeSpan.Zero) diff = diff.Negate();

                    var lapText1 = FormatLap(dur1);
                    var lapText2 = FormatLap(dur2);
                    var diffText = $"Δ {FormatLap(diff)}";

                    // Tamaños de overlay (en px) y posiciones (normalizadas)
                    var overlayImgW = 520;
                    var overlayImgH = 120;

                    WinFoundation.Rect lapRect1;
                    WinFoundation.Rect lapRect2;
                    WinFoundation.Rect diffRect;

                    if (parameters.IsHorizontalLayout)
                    {
                        lapRect1 = new WinFoundation.Rect(0.02, 0.02, 0.22, 0.08);
                        // Pegado a la derecha para no solapar con el delta centrado
                        lapRect2 = new WinFoundation.Rect(0.76, 0.02, 0.22, 0.08);
                        diffRect = new WinFoundation.Rect(0.39, 0.46, 0.22, 0.08);
                    }
                    else
                    {
                        lapRect1 = new WinFoundation.Rect(0.02, 0.02, 0.30, 0.05);
                        lapRect2 = new WinFoundation.Rect(0.02, 0.52, 0.30, 0.05);
                        diffRect = new WinFoundation.Rect(0.35, 0.46, 0.30, 0.05);
                    }

                    var lapImg1 = await CreateTextOverlayImageAsync(lapText1, lapColor1, overlayImgW, overlayImgH, "lap_t1", cancellationToken);
                    var lapClip1 = await MediaClip.CreateFromImageFileAsync(lapImg1, segDuration);
                    overlayLayer.Overlays.Add(new MediaOverlay(lapClip1)
                    {
                        Position = lapRect1,
                        Delay = cursor
                    });

                    var lapImg2 = await CreateTextOverlayImageAsync(lapText2, lapColor2, overlayImgW, overlayImgH, "lap_t2", cancellationToken);
                    var lapClip2 = await MediaClip.CreateFromImageFileAsync(lapImg2, segDuration);
                    overlayLayer.Overlays.Add(new MediaOverlay(lapClip2)
                    {
                        Position = lapRect2,
                        Delay = cursor
                    });

                    var diffImg = await CreateTextOverlayImageAsync(diffText, white, overlayImgW, overlayImgH, "lap_diff", cancellationToken);
                    var diffClip = await MediaClip.CreateFromImageFileAsync(diffImg, segDuration);
                    overlayLayer.Overlays.Add(new MediaOverlay(diffClip)
                    {
                        Position = diffRect,
                        Delay = cursor
                    });

                    // Padding freeze (video 1)
                    if (segDuration > dur1)
                    {
                        var pad = segDuration - dur1;
                        var stillFile = await CreateStillImageAsync(clip1, end1, "lap1", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncComposition.Clips.Add(stillClip);
                    }

                    // Padding freeze (video 2)
                    if (segDuration > dur2)
                    {
                        var pad = segDuration - dur2;
                        var stillFile = await CreateStillImageAsync(clip2, end2, "lap2", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        overlayLayer.Overlays.Add(new MediaOverlay(stillClip)
                        {
                            Position = overlayPos,
                            Delay = cursor + dur2
                        });
                    }

                    cursor += segDuration;
                }

                syncComposition.OverlayLayers.Add(overlayLayer);

                // Configurar encoding
                var syncEncodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
                if (parameters.IsHorizontalLayout)
                {
                    syncEncodingProfile.Video!.Width = 3840;
                    syncEncodingProfile.Video.Height = 1080;
                }
                else
                {
                    syncEncodingProfile.Video!.Width = 1920;
                    syncEncodingProfile.Video.Height = 2160;
                }

                // Crear archivo de salida
                var syncOutputFolder = await StorageFolder.GetFolderFromPathAsync(
                    System.IO.Path.GetDirectoryName(parameters.OutputPath)!);
                var syncOutputFile = await syncOutputFolder.CreateFileAsync(
                    System.IO.Path.GetFileName(parameters.OutputPath),
                    CreationCollisionOption.ReplaceExisting);

                // Renderizar
                var syncRenderOperation = syncComposition.RenderToFileAsync(syncOutputFile, MediaTrimmingPreference.Precise, syncEncodingProfile);
                syncRenderOperation.Progress = (info, progressValue) => progress?.Report(progressValue / 100.0);
                cancellationToken.Register(() => syncRenderOperation.Cancel());

                var syncResult = await syncRenderOperation;
                if (syncResult != TranscodeFailureReason.None)
                {
                    return new VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = $"Transcoding failed: {syncResult}"
                    };
                }

                var syncProps = await syncOutputFile.GetBasicPropertiesAsync();
                return new VideoExportResult
                {
                    Success = true,
                    OutputPath = parameters.OutputPath,
                    FileSizeBytes = (long)syncProps.Size,
                    Duration = cursor
                };
            }

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

            if (parameters.SyncByLaps &&
                parameters.Video1LapBoundaries?.Count >= 2 &&
                parameters.Video2LapBoundaries?.Count >= 2 &&
                parameters.Video3LapBoundaries?.Count >= 2 &&
                parameters.Video4LapBoundaries?.Count >= 2)
            {
                var b1 = parameters.Video1LapBoundaries;
                var b2 = parameters.Video2LapBoundaries;
                var b3 = parameters.Video3LapBoundaries;
                var b4 = parameters.Video4LapBoundaries;
                var segmentCount = new[] { b1.Count, b2.Count, b3.Count, b4.Count }.Min() - 1;

                var syncComposition = new MediaComposition();
                var syncOverlayLayer = new MediaOverlayLayer();

                var pos2 = new WinFoundation.Rect(0.5, 0, 0.5, 0.5);
                var pos3 = new WinFoundation.Rect(0, 0.5, 0.5, 0.5);
                var pos4 = new WinFoundation.Rect(0.5, 0.5, 0.5, 0.5);

                var cursor = TimeSpan.Zero;
                var maxTotal = parameters.MaxDuration;

                for (int i = 0; i < segmentCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var s1 = Clamp(b1[i], TimeSpan.Zero, clip1.OriginalDuration);
                    var e1 = Clamp(b1[i + 1], TimeSpan.Zero, clip1.OriginalDuration);
                    var s2 = Clamp(b2[i], TimeSpan.Zero, clip2.OriginalDuration);
                    var e2 = Clamp(b2[i + 1], TimeSpan.Zero, clip2.OriginalDuration);
                    var s3 = Clamp(b3[i], TimeSpan.Zero, clip3.OriginalDuration);
                    var e3 = Clamp(b3[i + 1], TimeSpan.Zero, clip3.OriginalDuration);
                    var s4 = Clamp(b4[i], TimeSpan.Zero, clip4.OriginalDuration);
                    var e4 = Clamp(b4[i + 1], TimeSpan.Zero, clip4.OriginalDuration);

                    if (e1 <= s1 || e2 <= s2 || e3 <= s3 || e4 <= s4)
                        break;

                    var d1 = e1 - s1;
                    var d2 = e2 - s2;
                    var d3 = e3 - s3;
                    var d4 = e4 - s4;
                    var segDuration = new[] { d1, d2, d3, d4 }.Max();

                    if (maxTotal.HasValue && cursor + segDuration > maxTotal.Value)
                        break;

                    // Base (video 1)
                    var segClip1 = await MediaClip.CreateFromFileAsync(video1File);
                    segClip1.TrimTimeFromStart = s1;
                    segClip1.TrimTimeFromEnd = segClip1.OriginalDuration - e1;
                    syncComposition.Clips.Add(segClip1);

                    // Overlays 2-4
                    var segClip2 = await MediaClip.CreateFromFileAsync(video2File);
                    segClip2.TrimTimeFromStart = s2;
                    segClip2.TrimTimeFromEnd = segClip2.OriginalDuration - e2;
                    syncOverlayLayer.Overlays.Add(new MediaOverlay(segClip2) { Position = pos2, Delay = cursor });

                    var segClip3 = await MediaClip.CreateFromFileAsync(video3File);
                    segClip3.TrimTimeFromStart = s3;
                    segClip3.TrimTimeFromEnd = segClip3.OriginalDuration - e3;
                    syncOverlayLayer.Overlays.Add(new MediaOverlay(segClip3) { Position = pos3, Delay = cursor });

                    var segClip4 = await MediaClip.CreateFromFileAsync(video4File);
                    segClip4.TrimTimeFromStart = s4;
                    segClip4.TrimTimeFromEnd = segClip4.OriginalDuration - e4;
                    syncOverlayLayer.Overlays.Add(new MediaOverlay(segClip4) { Position = pos4, Delay = cursor });

                    // Padding base (video 1)
                    if (segDuration > d1)
                    {
                        var pad = segDuration - d1;
                        var stillFile = await CreateStillImageAsync(clip1, e1, "lapq1", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncComposition.Clips.Add(stillClip);
                    }

                    // Padding overlays 2-4
                    if (segDuration > d2)
                    {
                        var pad = segDuration - d2;
                        var stillFile = await CreateStillImageAsync(clip2, e2, "lapq2", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(stillClip) { Position = pos2, Delay = cursor + d2 });
                    }
                    if (segDuration > d3)
                    {
                        var pad = segDuration - d3;
                        var stillFile = await CreateStillImageAsync(clip3, e3, "lapq3", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(stillClip) { Position = pos3, Delay = cursor + d3 });
                    }
                    if (segDuration > d4)
                    {
                        var pad = segDuration - d4;
                        var stillFile = await CreateStillImageAsync(clip4, e4, "lapq4", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(stillClip) { Position = pos4, Delay = cursor + d4 });
                    }

                    cursor += segDuration;
                }

                syncComposition.OverlayLayers.Add(syncOverlayLayer);

                // Configurar encoding - 2x2 grid a 1920x1080 total
                var syncEncodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
                syncEncodingProfile.Video!.Width = 1920;
                syncEncodingProfile.Video.Height = 1080;

                // Crear archivo de salida
                var syncOutputFolder = await StorageFolder.GetFolderFromPathAsync(
                    System.IO.Path.GetDirectoryName(parameters.OutputPath)!);
                var syncOutputFile = await syncOutputFolder.CreateFileAsync(
                    System.IO.Path.GetFileName(parameters.OutputPath),
                    CreationCollisionOption.ReplaceExisting);

                // Renderizar
                var syncRenderOperation = syncComposition.RenderToFileAsync(syncOutputFile, MediaTrimmingPreference.Precise, syncEncodingProfile);
                syncRenderOperation.Progress = (info, progressValue) => progress?.Report(progressValue / 100.0);
                cancellationToken.Register(() => syncRenderOperation.Cancel());

                var syncResult = await syncRenderOperation;
                if (syncResult != TranscodeFailureReason.None)
                {
                    return new VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = $"Transcoding failed: {syncResult}"
                    };
                }

                var syncProps = await syncOutputFile.GetBasicPropertiesAsync();
                return new VideoExportResult
                {
                    Success = true,
                    OutputPath = parameters.OutputPath,
                    FileSizeBytes = (long)syncProps.Size,
                    Duration = cursor
                };
            }

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
