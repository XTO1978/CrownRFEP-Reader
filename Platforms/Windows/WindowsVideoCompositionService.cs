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

    /// <summary>
    /// Crea un clip de fondo negro para usar como base en composiciones lado a lado.
    /// En Windows.Media.Editing, los overlays se superponen sobre el clip base,
    /// así que para hacer side-by-side, necesitamos un fondo vacío y ambos videos como overlays.
    /// </summary>
    private static async Task<StorageFile> CreateBlackBackgroundImageAsync(
        int width, 
        int height, 
        string prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = new SKBitmap(width, height, true);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        // Usar Path.GetTempPath() en lugar de ApplicationData.Current (no disponible en apps no empaquetadas)
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.png");
        using (var fileStream = System.IO.File.Create(tempPath))
        {
            data.SaveTo(fileStream);
            await fileStream.FlushAsync(cancellationToken);
        }
        
        var file = await StorageFile.GetFileFromPathAsync(tempPath);
        return file;
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

        // Usar Path.GetTempPath() en lugar de ApplicationData.Current (no disponible en apps no empaquetadas)
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.png");

        using var thumbStream = await tempComposition.GetThumbnailAsync(
            TimeSpan.Zero,
            640,
            360,
            VideoFramePrecision.NearestFrame);

        using (var fileStream = System.IO.File.Create(tempPath))
        {
            await thumbStream.AsStreamForRead().CopyToAsync(fileStream);
            await fileStream.FlushAsync();
        }

        var file = await StorageFile.GetFileFromPathAsync(tempPath);
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

        // Usar Path.GetTempPath() en lugar de ApplicationData.Current (no disponible en apps no empaquetadas)
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.png");
        using (var fileStream = System.IO.File.Create(tempPath))
        {
            data.SaveTo(fileStream);
            await fileStream.FlushAsync(cancellationToken);
        }

        var file = await StorageFile.GetFileFromPathAsync(tempPath);
        return file;
    }

    private static string EllipsizeText(SKPaint paint, string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (paint.MeasureText(text) <= maxWidth)
            return text;

        const string ellipsis = "…";
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            var candidate = text.Substring(0, mid) + ellipsis;
            if (paint.MeasureText(candidate) <= maxWidth)
                low = mid + 1;
            else
                high = mid;
        }

        var len = Math.Max(0, low - 1);
        return len <= 0 ? ellipsis : text.Substring(0, len) + ellipsis;
    }

    private static SKColor Darken(SKColor c, float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return new SKColor(
            (byte)(c.Red * factor),
            (byte)(c.Green * factor),
            (byte)(c.Blue * factor),
            255);
    }

    private static int ComputePillWidthTight(string text, int height, int maxWidth)
    {
        maxWidth = Math.Max(1, maxWidth);
        height = Math.Max(1, height);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = Math.Max(16f, height * 0.52f),
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        var padding = Math.Max(14f, height * 0.22f);
        var textWidth = paint.MeasureText(text ?? string.Empty);
        var desired = (int)Math.Ceiling(textWidth + (padding * 2f));

        // Asegurar que el texto pueda elipsizar correctamente dentro del pill
        var min = (int)Math.Ceiling((padding * 2f) + 16f);
        return Math.Clamp(desired, min, maxWidth);
    }

    private static int ComputePlainTextWidthTight(string text, int height, int maxWidth)
    {
        maxWidth = Math.Max(1, maxWidth);
        height = Math.Max(1, height);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = Math.Max(14f, height * 0.80f),
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        var padding = Math.Max(8f, height * 0.15f);
        var textWidth = paint.MeasureText(text ?? string.Empty);
        var desired = (int)Math.Ceiling(textWidth + (padding * 2f));

        var min = (int)Math.Ceiling((padding * 2f) + 16f);
        return Math.Clamp(desired, min, maxWidth);
    }

    private static async Task<StorageFile> CreatePillLabelImageAsync(
        string text,
        SKColor backgroundColor,
        SKColor textColor,
        int width,
        int height,
        string prefix,
        CancellationToken cancellationToken)
        => await CreatePillLabelImageAsync(text, backgroundColor, textColor, width, height, prefix, rounded: true, cancellationToken);

    private static async Task<StorageFile> CreatePillLabelImageAsync(
        string text,
        SKColor backgroundColor,
        SKColor textColor,
        int width,
        int height,
        string prefix,
        bool rounded,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = new SKBitmap(width, height, true);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var radius = rounded ? (height / 2f) : 0f;
        using (var bgPaint = new SKPaint { Color = backgroundColor, IsAntialias = true })
        {
            canvas.DrawRoundRect(new SKRect(0, 0, width, height), radius, radius, bgPaint);
        }

        using var textPaint = new SKPaint
        {
            Color = textColor,
            IsAntialias = true,
            TextSize = Math.Max(16f, height * 0.52f),
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        var padding = Math.Max(14f, height * 0.22f);
        var maxTextWidth = Math.Max(0f, width - (padding * 2f));
        var line = EllipsizeText(textPaint, text, maxTextWidth);

        var bounds = new SKRect();
        textPaint.MeasureText(line, ref bounds);
        var x = (width - bounds.Width) / 2f - bounds.Left;
        var y = (height - bounds.Height) / 2f - bounds.Top;
        canvas.DrawText(line, x, y, textPaint);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.png");
        using (var fileStream = System.IO.File.Create(tempPath))
        {
            data.SaveTo(fileStream);
            await fileStream.FlushAsync(cancellationToken);
        }

        return await StorageFile.GetFileFromPathAsync(tempPath);
    }

    private static async Task<StorageFile> CreatePlainTextImageAsync(
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
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            Color = textColor,
            IsAntialias = true,
            TextSize = Math.Max(14f, height * 0.80f),
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        var padding = Math.Max(8f, height * 0.15f);
        var maxTextWidth = Math.Max(0f, width - (padding * 2f));
        var line = EllipsizeText(paint, text, maxTextWidth);

        var bounds = new SKRect();
        paint.MeasureText(line, ref bounds);
        var x = (width - bounds.Width) / 2f - bounds.Left;
        var y = (height - bounds.Height) / 2f - bounds.Top;
        canvas.DrawText(line, x, y, paint);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.png");
        using (var fileStream = System.IO.File.Create(tempPath))
        {
            data.SaveTo(fileStream);
            await fileStream.FlushAsync(cancellationToken);
        }

        return await StorageFile.GetFileFromPathAsync(tempPath);
    }

    private static string BuildInfoText(string? category, int? section, string? time, string? penalties)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(category))
            parts.Add(category);
        if (section.HasValue)
            parts.Add($"Sec. {section.Value}");
        if (!string.IsNullOrWhiteSpace(time))
            parts.Add(time);
        if (!string.IsNullOrWhiteSpace(penalties))
            parts.Add(penalties);
        return string.Join(" | ", parts);
    }

    private static int ComputeOverlayWidthApprox(string athleteName, string infoText, double scale, int panelWidthPx)
    {
        var nameFontSize = 16.0 * scale;
        var infoFontSize = 12.0 * scale;
        var horizontalPadding = 24.0 * scale;
        var minWidth = Math.Max(120.0, 120.0 * scale);
        var maxWidth = Math.Max(120.0, panelWidthPx - (20.0 * scale));

        var nameWidth = !string.IsNullOrEmpty(athleteName)
            ? athleteName.Length * nameFontSize * 0.55
            : 0;
        var infoWidth = !string.IsNullOrEmpty(infoText)
            ? infoText.Length * infoFontSize * 0.55
            : 0;

        var desired = Math.Max(nameWidth, infoWidth) + horizontalPadding;
        var clamped = Math.Min(Math.Max(desired, minWidth), maxWidth);
        return (int)Math.Round(clamped);
    }

    private static async Task<StorageFile> CreateAthleteInfoOverlayImageAsync(
        string athleteName,
        string infoText,
        int width,
        int height,
        string prefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = new SKBitmap(width, height, true);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var cornerRadius = Math.Max(6f, height * 0.12f);
        using (var bgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 255), IsAntialias = true })
        {
            canvas.DrawRoundRect(new SKRect(0, 0, width, height), cornerRadius, cornerRadius, bgPaint);
        }

        static string Ellipsize(SKPaint paint, string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            if (paint.MeasureText(text) <= maxWidth)
                return text;

            const string ellipsis = "…";
            var low = 0;
            var high = text.Length;
            while (low < high)
            {
                var mid = (low + high) / 2;
                var candidate = text.Substring(0, mid) + ellipsis;
                if (paint.MeasureText(candidate) <= maxWidth)
                    low = mid + 1;
                else
                    high = mid;
            }

            var len = Math.Max(0, low - 1);
            return len <= 0 ? ellipsis : text.Substring(0, len) + ellipsis;
        }

        var padding = Math.Max(12f, height * 0.18f);
        var nameFontSize = Math.Max(18f, height * 0.34f);
        var infoFontSize = Math.Max(13f, height * 0.24f);

        using var namePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = nameFontSize,
            Typeface = SKTypeface.FromFamilyName("Arial")
        };
        using var infoPaint = new SKPaint
        {
            Color = new SKColor(180, 180, 180, 255),
            IsAntialias = true,
            TextSize = infoFontSize,
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        // Posiciones similares a macOS: info arriba, nombre debajo
        var infoTop = height * 0.18f;
        var nameTop = height * 0.56f;

        var maxTextWidth = Math.Max(0f, width - (padding * 2f));

        if (!string.IsNullOrWhiteSpace(infoText))
        {
            var line = Ellipsize(infoPaint, infoText, maxTextWidth);
            var fm = infoPaint.FontMetrics;
            canvas.DrawText(line, padding, infoTop - fm.Ascent, infoPaint);
        }

        if (!string.IsNullOrWhiteSpace(athleteName))
        {
            var line = Ellipsize(namePaint, athleteName, maxTextWidth);
            var fm = namePaint.FontMetrics;
            canvas.DrawText(line, padding, nameTop - fm.Ascent, namePaint);
        }

        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.png");
        using (var fileStream = System.IO.File.Create(tempPath))
        {
            data.SaveTo(fileStream);
            await fileStream.FlushAsync(cancellationToken);
        }

        return await StorageFile.GetFileFromPathAsync(tempPath);
    }

    public async Task<VideoExportResult> ExportParallelVideosAsync(
        ParallelVideoExportParams parameters,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] ExportParallelVideosAsync starting");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Video1Path: {parameters.Video1Path}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Video2Path: {parameters.Video2Path}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] OutputPath: {parameters.OutputPath}");
            
            // Verificar que los archivos existen
            if (!System.IO.File.Exists(parameters.Video1Path))
            {
                return new VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"Video 1 no encontrado: {parameters.Video1Path}"
                };
            }
            if (!System.IO.File.Exists(parameters.Video2Path))
            {
                return new VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"Video 2 no encontrado: {parameters.Video2Path}"
                };
            }
            
            // Obtener archivos de video
            var video1File = await StorageFile.GetFileFromPathAsync(parameters.Video1Path);
            var video2File = await StorageFile.GetFileFromPathAsync(parameters.Video2Path);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Files loaded successfully");

            // Crear clips de media
            var clip1 = await MediaClip.CreateFromFileAsync(video1File);
            var clip2 = await MediaClip.CreateFromFileAsync(video2File);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Clips created - Clip1 Duration: {clip1.OriginalDuration}, Clip2 Duration: {clip2.OriginalDuration}");

            if (parameters.SyncByLaps &&
                parameters.Video1LapBoundaries?.Count >= 2 &&
                parameters.Video2LapBoundaries?.Count >= 2)
            {
                var boundaries1 = parameters.Video1LapBoundaries;
                var boundaries2 = parameters.Video2LapBoundaries;
                var segmentCount = Math.Min(boundaries1.Count, boundaries2.Count) - 1;

                // En Windows.Media.Editing, MediaOverlay.Position usa coordenadas en píxeles.
                // Para un split screen correcto (sin recortes), usamos un fondo negro como base
                // y ponemos ambos vídeos como overlays, cada uno en su panel.
                uint syncOutputWidth = parameters.IsHorizontalLayout ? 3840u : 1920u;
                uint syncOutputHeight = parameters.IsHorizontalLayout ? 1080u : 2160u;
                var halfW = (double)syncOutputWidth / 2.0;
                var halfH = (double)syncOutputHeight / 2.0;

                // Estilo como referencia: barra negra inferior + overlays dentro de esa barra.
                var uiScale = syncOutputHeight / 1080.0;
                var bottomBarH = parameters.IsHorizontalLayout
                    ? (int)Math.Clamp(Math.Round(160 * uiScale), 110, 260)
                    : 0;
                var videoAreaH = (double)syncOutputHeight - bottomBarH;
                var bottomBarY = videoAreaH;

                var panelW = parameters.IsHorizontalLayout ? halfW : syncOutputWidth;
                var panelH = parameters.IsHorizontalLayout ? videoAreaH : (videoAreaH / 2.0);

                // Para el modo horizontal: la barra inferior se divide en 2 columnas (v1/v2).
                // En modo vertical no usamos barra inferior; los labels van sobre los vídeos.
                var barPanelW = halfW;
                var bar1X = 0.0;
                var bar2X = halfW;

                var pos1 = parameters.IsHorizontalLayout
                    ? new WinFoundation.Rect(0, 0, halfW, videoAreaH)
                    : new WinFoundation.Rect(0, 0, syncOutputWidth, panelH);
                var pos2 = parameters.IsHorizontalLayout
                    ? new WinFoundation.Rect(halfW, 0, halfW, videoAreaH)
                    : new WinFoundation.Rect(0, panelH, syncOutputWidth, panelH);

                var syncBackgroundFile = await CreateBlackBackgroundImageAsync(
                    (int)syncOutputWidth,
                    (int)syncOutputHeight,
                    "parallel_sync_bg",
                    cancellationToken);

                var syncComposition = new MediaComposition();
                var overlayLayer = new MediaOverlayLayer();

                // Nombres en la barra inferior (solo modo horizontal)
                var nameScale = uiScale;
                var nameH = (int)Math.Max(26, Math.Round(30 * nameScale));
                var nameW = (int)Math.Min(barPanelW * 0.80, barPanelW - (20 * nameScale));

                StorageFile? v1NameFile = null;
                StorageFile? v2NameFile = null;
                if (parameters.IsHorizontalLayout)
                {
                    if (!string.IsNullOrWhiteSpace(parameters.Video1AthleteName))
                        v1NameFile = await CreatePlainTextImageAsync(parameters.Video1AthleteName!, SKColors.White, nameW, nameH, "v1_name", cancellationToken);
                    if (!string.IsNullOrWhiteSpace(parameters.Video2AthleteName))
                        v2NameFile = await CreatePlainTextImageAsync(parameters.Video2AthleteName!, SKColors.White, nameW, nameH, "v2_name", cancellationToken);
                }

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

                    // Base: fondo negro por segmento
                    var bgClip = await MediaClip.CreateFromImageFileAsync(syncBackgroundFile, segDuration);
                    syncComposition.Clips.Add(bgClip);

                    // Video 1 (overlay)
                    var segClip1 = await MediaClip.CreateFromFileAsync(video1File);
                    segClip1.TrimTimeFromStart = start1;
                    segClip1.TrimTimeFromEnd = segClip1.OriginalDuration - end1;

                    overlayLayer.Overlays.Add(new MediaOverlay(segClip1)
                    {
                        Position = pos1,
                        Delay = cursor,
                        Opacity = 1.0
                    });

                    // Video 2 (overlay)
                    var segClip2 = await MediaClip.CreateFromFileAsync(video2File);
                    segClip2.TrimTimeFromStart = start2;
                    segClip2.TrimTimeFromEnd = segClip2.OriginalDuration - end2;

                    overlayLayer.Overlays.Add(new MediaOverlay(segClip2)
                    {
                        Position = pos2,
                        Delay = cursor,
                        Opacity = 1.0
                    });

                    // Padding freeze (video 1)
                    if (segDuration > dur1)
                    {
                        var pad = segDuration - dur1;
                        var stillFile = await CreateStillImageAsync(clip1, end1, "lap1", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        overlayLayer.Overlays.Add(new MediaOverlay(stillClip)
                        {
                            Position = pos1,
                            Delay = cursor + dur1,
                            Opacity = 1.0
                        });
                    }

                    // Padding freeze (video 2)
                    if (segDuration > dur2)
                    {
                        var pad = segDuration - dur2;
                        var stillFile = await CreateStillImageAsync(clip2, end2, "lap2", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        overlayLayer.Overlays.Add(new MediaOverlay(stillClip)
                        {
                            Position = pos2,
                            Delay = cursor + dur2
                        });
                    }

                    // ===== Overlays de parciales por lap + diferencia (siempre por encima de los freezes) =====
                    var green = new SKColor(52, 199, 89);
                    var red = new SKColor(255, 59, 48);
                    var white = SKColors.White;

                    var is1Better = dur1 < dur2;
                    var is2Better = dur2 < dur1;
                    var lapColor1 = is1Better ? green : (is2Better ? red : white);
                    var lapColor2 = is2Better ? green : (is1Better ? red : white);

                    var diff = dur1 - dur2;
                    if (diff < TimeSpan.Zero) diff = diff.Negate();

                    var lapText1 = $"Lap {i + 1}: {FormatLap(dur1)}";
                    var lapText2 = $"Lap {i + 1}: {FormatLap(dur2)}";
                    var diffText = $"Δ -{FormatLap(diff)}";

                    if (parameters.IsHorizontalLayout)
                    {
                        // Lap boxes en la barra inferior (centradas en cada panel)
                        var lapBoxH = (int)Math.Max(46, Math.Round(60 * uiScale));
                        var lapBoxW = (int)Math.Min(Math.Round(520 * uiScale), barPanelW - (40 * uiScale));

                        var lapBoxY = bottomBarY + (bottomBarH * 0.20);
                        var nameY = bottomBarY + (bottomBarH * 0.68);

                        var lap1X = bar1X + (barPanelW - lapBoxW) / 2.0;
                        var lap2X = bar2X + (barPanelW - lapBoxW) / 2.0;

                        var lapRect1 = new WinFoundation.Rect(lap1X, lapBoxY, lapBoxW, lapBoxH);
                        var lapRect2 = new WinFoundation.Rect(lap2X, lapBoxY, lapBoxW, lapBoxH);

                        // Delta pill abajo, centrado, entre los dos labels de lap
                        var deltaW = (int)Math.Max(140, Math.Round(180 * uiScale));
                        var deltaH = (int)Math.Max(34, Math.Round(42 * uiScale));
                        var deltaX = (syncOutputWidth / 2.0) - (deltaW / 2.0);
                        var deltaY = lapBoxY + (lapBoxH - deltaH) / 2.0;
                        var diffRect = new WinFoundation.Rect(deltaX, deltaY, deltaW, deltaH);

                        var lapBg1 = Darken(lapColor1, 0.35f);
                        var lapBg2 = Darken(lapColor2, 0.35f);

                        var lapImg1 = await CreatePillLabelImageAsync(lapText1, lapBg1, lapColor1, lapBoxW, lapBoxH, "lap_t1", cancellationToken);
                        var lapClip1 = await MediaClip.CreateFromImageFileAsync(lapImg1, segDuration);
                        overlayLayer.Overlays.Add(new MediaOverlay(lapClip1) { Position = lapRect1, Delay = cursor, Opacity = 1.0 });

                        var lapImg2 = await CreatePillLabelImageAsync(lapText2, lapBg2, lapColor2, lapBoxW, lapBoxH, "lap_t2", cancellationToken);
                        var lapClip2 = await MediaClip.CreateFromImageFileAsync(lapImg2, segDuration);
                        overlayLayer.Overlays.Add(new MediaOverlay(lapClip2) { Position = lapRect2, Delay = cursor, Opacity = 1.0 });

                        var diffImg = await CreatePillLabelImageAsync(diffText, new SKColor(0, 0, 0, 220), SKColors.White, deltaW, deltaH, "lap_diff", cancellationToken);
                        var diffClip = await MediaClip.CreateFromImageFileAsync(diffImg, segDuration);
                        overlayLayer.Overlays.Add(new MediaOverlay(diffClip) { Position = diffRect, Delay = cursor, Opacity = 1.0 });

                        // Nombres: centrados en la barra inferior, debajo de los laps
                        if (v1NameFile != null)
                        {
                            var clip = await MediaClip.CreateFromImageFileAsync(v1NameFile, segDuration);
                            overlayLayer.Overlays.Add(new MediaOverlay(clip)
                            {
                                Position = new WinFoundation.Rect(bar1X + (barPanelW - nameW) / 2.0, nameY, nameW, nameH),
                                Delay = cursor,
                                Opacity = 1.0
                            });
                        }
                        if (v2NameFile != null)
                        {
                            var clip = await MediaClip.CreateFromImageFileAsync(v2NameFile, segDuration);
                            overlayLayer.Overlays.Add(new MediaOverlay(clip)
                            {
                                Position = new WinFoundation.Rect(bar2X + (barPanelW - nameW) / 2.0, nameY, nameW, nameH),
                                Delay = cursor,
                                Opacity = 1.0
                            });
                        }
                    }
                    else
                    {
                        // Modo vertical (stacked): labels sobre los vídeos.
                        var pad = Math.Max(12.0, Math.Round(18 * uiScale));
                        var gap = Math.Max(6.0, Math.Round(8 * uiScale));

                        // Lap pill (más pequeño, estilo referencia)
                        var vertLapH = (int)Math.Clamp(Math.Round(46 * uiScale), 34, 84);
                        var vertLapMaxW = (int)Math.Min(
                            Math.Round(420 * uiScale),
                            Math.Round(syncOutputWidth * 0.48));
                        vertLapMaxW = (int)Math.Min(vertLapMaxW, syncOutputWidth - (pad * 2.0));

                        // Nombre (texto blanco sin caja) bajo el pill
                        var vertNameH = (int)Math.Clamp(Math.Round(22 * uiScale), 18, 50);
                        var vertNameMaxW = (int)Math.Min(
                            Math.Round(420 * uiScale),
                            Math.Round(syncOutputWidth * 0.48));
                        vertNameMaxW = (int)Math.Min(vertNameMaxW, syncOutputWidth - (pad * 2.0));

                        var v1Name = string.IsNullOrWhiteSpace(parameters.Video1AthleteName) ? "" : parameters.Video1AthleteName!.Trim();
                        var v2Name = string.IsNullOrWhiteSpace(parameters.Video2AthleteName) ? "" : parameters.Video2AthleteName!.Trim();

                        // Ajustar anchos al texto (con padding) para que el borde no sea enorme
                        var v1LapW = ComputePillWidthTight(lapText1, vertLapH, vertLapMaxW);
                        var v2LapW = ComputePillWidthTight(lapText2, vertLapH, vertLapMaxW);
                        var v1NameW = string.IsNullOrWhiteSpace(v1Name) ? 0 : ComputePlainTextWidthTight(v1Name, vertNameH, vertNameMaxW);
                        var v2NameW = string.IsNullOrWhiteSpace(v2Name) ? 0 : ComputePlainTextWidthTight(v2Name, vertNameH, vertNameMaxW);

                        // Posiciones (como pides):
                        // - Video superior: esquina inferior derecha
                        // - Video inferior: esquina superior derecha
                        var blockH = vertLapH + gap + vertNameH;
                        var rightLapX1 = pos1.X + pos1.Width - pad - v1LapW;
                        var rightLapX2 = pos2.X + pos2.Width - pad - v2LapW;
                        var rightNameX1 = pos1.X + pos1.Width - pad - Math.Max(1, v1NameW);
                        var rightNameX2 = pos2.X + pos2.Width - pad - Math.Max(1, v2NameW);

                        var v1BlockTop = (pos1.Y + pos1.Height) - pad - blockH;
                        var v1LapRect = new WinFoundation.Rect(rightLapX1, v1BlockTop, v1LapW, vertLapH);
                        var v1NameRect = new WinFoundation.Rect(rightNameX1, v1BlockTop + vertLapH + gap, Math.Max(1, v1NameW), vertNameH);

                        var v2BlockTop = pos2.Y + pad;
                        var v2LapRect = new WinFoundation.Rect(rightLapX2, v2BlockTop, v2LapW, vertLapH);
                        var v2NameRect = new WinFoundation.Rect(rightNameX2, v2BlockTop + vertLapH + gap, Math.Max(1, v2NameW), vertNameH);

                        // Delta: texto blanco en la mitad izquierda (sobre el split)
                        var deltaH = (int)Math.Clamp(Math.Round(34 * uiScale), 22, 70);
                        var deltaW = (int)Math.Clamp(Math.Round(240 * uiScale), 140, 520);
                        var splitY = pos2.Y;
                        var deltaX = pos1.X + pad;
                        var deltaY = splitY - (deltaH / 2.0);
                        if (deltaY < pad) deltaY = pad;
                        if (deltaY + deltaH > syncOutputHeight - pad) deltaY = syncOutputHeight - pad - deltaH;
                        var diffRect = new WinFoundation.Rect(deltaX, deltaY, deltaW, deltaH);

                        var lapBg1 = Darken(lapColor1, 0.35f);
                        var lapBg2 = Darken(lapColor2, 0.35f);

                        var v1LapImg = await CreatePillLabelImageAsync(lapText1, lapBg1, lapColor1, v1LapW, vertLapH, "v1_lap", rounded: false, cancellationToken);
                        var v1LapClip = await MediaClip.CreateFromImageFileAsync(v1LapImg, segDuration);
                        overlayLayer.Overlays.Add(new MediaOverlay(v1LapClip) { Position = v1LapRect, Delay = cursor, Opacity = 1.0 });

                        if (!string.IsNullOrWhiteSpace(v1Name))
                        {
                            var v1NameImg = await CreatePlainTextImageAsync(v1Name, SKColors.White, Math.Max(1, v1NameW), vertNameH, "v1_name_v", cancellationToken);
                            var v1NameClip = await MediaClip.CreateFromImageFileAsync(v1NameImg, segDuration);
                            overlayLayer.Overlays.Add(new MediaOverlay(v1NameClip) { Position = v1NameRect, Delay = cursor, Opacity = 1.0 });
                        }

                        var v2LapImg = await CreatePillLabelImageAsync(lapText2, lapBg2, lapColor2, v2LapW, vertLapH, "v2_lap", rounded: false, cancellationToken);
                        var v2LapClip = await MediaClip.CreateFromImageFileAsync(v2LapImg, segDuration);
                        overlayLayer.Overlays.Add(new MediaOverlay(v2LapClip) { Position = v2LapRect, Delay = cursor, Opacity = 1.0 });

                        if (!string.IsNullOrWhiteSpace(v2Name))
                        {
                            var v2NameImg = await CreatePlainTextImageAsync(v2Name, SKColors.White, Math.Max(1, v2NameW), vertNameH, "v2_name_v", cancellationToken);
                            var v2NameClip = await MediaClip.CreateFromImageFileAsync(v2NameImg, segDuration);
                            overlayLayer.Overlays.Add(new MediaOverlay(v2NameClip) { Position = v2NameRect, Delay = cursor, Opacity = 1.0 });
                        }

                        var diffImg = await CreatePlainTextImageAsync(diffText, SKColors.White, deltaW, deltaH, "v_delta_txt", cancellationToken);
                        var diffClip = await MediaClip.CreateFromImageFileAsync(diffImg, segDuration);
                        overlayLayer.Overlays.Add(new MediaOverlay(diffClip) { Position = diffRect, Delay = cursor, Opacity = 1.0 });
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

            // Determinar resolución de salida
            uint outputWidth, outputHeight;
            if (parameters.IsHorizontalLayout)
            {
                // Lado a lado: 3840x1080 (dos 1920x1080)
                outputWidth = 3840;
                outputHeight = 1080;
            }
            else
            {
                // Arriba/abajo: 1920x2160 (dos 1920x1080 apilados)
                outputWidth = 1920;
                outputHeight = 2160;
            }

            // Crear fondo negro como clip base
            // En Windows.Media.Editing, los overlays se superponen sobre el clip base,
            // así que para hacer side-by-side, necesitamos un fondo vacío y ambos videos como overlays.
            var backgroundFile = await CreateBlackBackgroundImageAsync(
                (int)outputWidth, 
                (int)outputHeight, 
                "parallel_bg", 
                cancellationToken);
            var backgroundClip = await MediaClip.CreateFromImageFileAsync(backgroundFile, minDuration);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Background clip created, duration: {backgroundClip.OriginalDuration}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Clip1 trimmed duration: {clip1.TrimmedDuration}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Clip2 trimmed duration: {clip2.TrimmedDuration}");

            // Crear composición con el fondo negro
            var composition = new MediaComposition();
            composition.Clips.Add(backgroundClip);

            // Crear overlay layer para ambos videos
            var overlayTrack = new MediaOverlayLayer();
            
            // Configurar posición del video 1 según el layout
            var overlay1 = new MediaOverlay(clip1);
            overlay1.Position = parameters.IsHorizontalLayout
                ? new WinFoundation.Rect(0, 0, (double)outputWidth / 2.0, outputHeight)
                : new WinFoundation.Rect(0, 0, outputWidth, (double)outputHeight / 2.0);
            overlay1.Delay = TimeSpan.Zero;
            overlay1.Opacity = 1.0; // Asegurar opacidad completa
            overlayTrack.Overlays.Add(overlay1);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Overlay1 added - Position: {overlay1.Position}, Opacity: {overlay1.Opacity}");
            
            // Configurar posición del video 2 según el layout
            var overlay2 = new MediaOverlay(clip2);
            overlay2.Position = parameters.IsHorizontalLayout
                ? new WinFoundation.Rect((double)outputWidth / 2.0, 0, (double)outputWidth / 2.0, outputHeight)
                : new WinFoundation.Rect(0, (double)outputHeight / 2.0, outputWidth, (double)outputHeight / 2.0);
            overlay2.Delay = TimeSpan.Zero;
            overlay2.Opacity = 1.0; // Asegurar opacidad completa
            overlayTrack.Overlays.Add(overlay2);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Overlay2 added - Position: {overlay2.Position}, Opacity: {overlay2.Opacity}");

            // Overlays de info por vídeo (equivalente a macOS)
            var infoText1 = BuildInfoText(parameters.Video1Category, parameters.Video1Section, parameters.Video1Time, parameters.Video1Penalties);
            var infoText2 = BuildInfoText(parameters.Video2Category, parameters.Video2Section, parameters.Video2Time, parameters.Video2Penalties);

            var panelWidthPx = parameters.IsHorizontalLayout ? (int)(outputWidth / 2) : (int)outputWidth;
            var panelHeightPx = parameters.IsHorizontalLayout ? (int)outputHeight : (int)(outputHeight / 2);
            var scale2 = panelHeightPx / 1080.0;
            var labelH = (int)Math.Max(80, Math.Round(85 * scale2));

            var labelW1 = !string.IsNullOrWhiteSpace(parameters.Video1AthleteName)
                ? ComputeOverlayWidthApprox(parameters.Video1AthleteName!, infoText1, scale2, panelWidthPx)
                : Math.Max(140, panelWidthPx - (int)Math.Round(20 * scale2));
            var labelW2 = !string.IsNullOrWhiteSpace(parameters.Video2AthleteName)
                ? ComputeOverlayWidthApprox(parameters.Video2AthleteName!, infoText2, scale2, panelWidthPx)
                : Math.Max(140, panelWidthPx - (int)Math.Round(20 * scale2));

            if (!string.IsNullOrWhiteSpace(parameters.Video1AthleteName))
            {
                var labelFile1 = await CreateAthleteInfoOverlayImageAsync(
                    parameters.Video1AthleteName!,
                    infoText1,
                    labelW1,
                    labelH,
                    "p_v1_label",
                    cancellationToken);
                var labelClip1 = await MediaClip.CreateFromImageFileAsync(labelFile1, minDuration);
                overlayTrack.Overlays.Add(new MediaOverlay(labelClip1)
                {
                    Position = parameters.IsHorizontalLayout
                        ? new WinFoundation.Rect(0 + 10, 0 + outputHeight - labelH - 10, labelW1, labelH)
                        : new WinFoundation.Rect(0 + 10, 0 + (outputHeight / 2) - labelH - 10, labelW1, labelH),
                    Delay = TimeSpan.Zero,
                    Opacity = 1.0
                });
            }

            if (!string.IsNullOrWhiteSpace(parameters.Video2AthleteName))
            {
                var labelFile2 = await CreateAthleteInfoOverlayImageAsync(
                    parameters.Video2AthleteName!,
                    infoText2,
                    labelW2,
                    labelH,
                    "p_v2_label",
                    cancellationToken);
                var labelClip2 = await MediaClip.CreateFromImageFileAsync(labelFile2, minDuration);
                overlayTrack.Overlays.Add(new MediaOverlay(labelClip2)
                {
                    Position = parameters.IsHorizontalLayout
                        ? new WinFoundation.Rect((double)outputWidth / 2.0 + 10, 0 + outputHeight - labelH - 10, labelW2, labelH)
                        : new WinFoundation.Rect(0 + 10, outputHeight - labelH - 10, labelW2, labelH),
                    Delay = TimeSpan.Zero,
                    Opacity = 1.0
                });
            }

            composition.OverlayLayers.Add(overlayTrack);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] OverlayLayers count: {composition.OverlayLayers.Count}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Overlays in layer: {overlayTrack.Overlays.Count}");
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Layout: {(parameters.IsHorizontalLayout ? "Horizontal" : "Vertical")}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Output size: {outputWidth}x{outputHeight}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Video1 Position(px): {overlay1.Position}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Video2 Position(px): {overlay2.Position}");

            // Configurar encoding
            var encodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            encodingProfile.Video!.Width = outputWidth;
            encodingProfile.Video.Height = outputHeight;

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
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Export completed successfully: {parameters.OutputPath}");
            
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
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] ExportParallelVideosAsync ERROR: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] StackTrace: {ex.StackTrace}");
            return new VideoExportResult
            {
                Success = false,
                ErrorMessage = $"{ex.GetType().Name}: {ex.Message}"
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
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] ExportQuadVideosAsync starting");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Video1Path: {parameters.Video1Path}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Video2Path: {parameters.Video2Path}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Video3Path: {parameters.Video3Path}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Video4Path: {parameters.Video4Path}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] OutputPath: {parameters.OutputPath}");
            
            // Verificar que los archivos existen
            if (!System.IO.File.Exists(parameters.Video1Path))
            {
                return new VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"Video 1 no encontrado: {parameters.Video1Path}"
                };
            }
            if (!System.IO.File.Exists(parameters.Video2Path))
            {
                return new VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"Video 2 no encontrado: {parameters.Video2Path}"
                };
            }
            if (!System.IO.File.Exists(parameters.Video3Path))
            {
                return new VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"Video 3 no encontrado: {parameters.Video3Path}"
                };
            }
            if (!System.IO.File.Exists(parameters.Video4Path))
            {
                return new VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"Video 4 no encontrado: {parameters.Video4Path}"
                };
            }
            
            // Obtener archivos de video
            var video1File = await StorageFile.GetFileFromPathAsync(parameters.Video1Path);
            var video2File = await StorageFile.GetFileFromPathAsync(parameters.Video2Path);
            var video3File = await StorageFile.GetFileFromPathAsync(parameters.Video3Path);
            var video4File = await StorageFile.GetFileFromPathAsync(parameters.Video4Path);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Files loaded successfully");

            // Crear clips de media
            var clip1 = await MediaClip.CreateFromFileAsync(video1File);
            var clip2 = await MediaClip.CreateFromFileAsync(video2File);
            var clip3 = await MediaClip.CreateFromFileAsync(video3File);
            var clip4 = await MediaClip.CreateFromFileAsync(video4File);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Clips created");

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

                // En Windows.Media.Editing, MediaOverlay.Position usa coordenadas en píxeles.
                // Para un grid 2x2 correcto, usamos fondo negro como base y los 4 vídeos como overlays.
                const uint syncOutputWidth = 1920;
                const uint syncOutputHeight = 1080;
                var halfW = (double)syncOutputWidth / 2.0;
                var halfH = (double)syncOutputHeight / 2.0;

                var pos1 = new WinFoundation.Rect(0, 0, halfW, halfH);
                var pos2 = new WinFoundation.Rect(halfW, 0, halfW, halfH);
                var pos3 = new WinFoundation.Rect(0, halfH, halfW, halfH);
                var pos4 = new WinFoundation.Rect(halfW, halfH, halfW, halfH);

                var syncBackgroundFile = await CreateBlackBackgroundImageAsync(
                    (int)syncOutputWidth,
                    (int)syncOutputHeight,
                    "quad_sync_bg",
                    cancellationToken);

                var syncComposition = new MediaComposition();
                var syncOverlayLayer = new MediaOverlayLayer();

                // Overlays de info por vídeo (equivalente a macOS)
                var v1InfoText = BuildInfoText(parameters.Video1Category, parameters.Video1Section, parameters.Video1Time, parameters.Video1Penalties);
                var v2InfoText = BuildInfoText(parameters.Video2Category, parameters.Video2Section, parameters.Video2Time, parameters.Video2Penalties);
                var v3InfoText = BuildInfoText(parameters.Video3Category, parameters.Video3Section, parameters.Video3Time, parameters.Video3Penalties);
                var v4InfoText = BuildInfoText(parameters.Video4Category, parameters.Video4Section, parameters.Video4Time, parameters.Video4Penalties);

                var panelW = (int)Math.Round(halfW);
                var panelH = (int)Math.Round(halfH);
                var scale = panelH / 1080.0;
                var overlayH = (int)Math.Max(36, Math.Round(60 * scale));

                StorageFile? v1LabelFile = null;
                StorageFile? v2LabelFile = null;
                StorageFile? v3LabelFile = null;
                StorageFile? v4LabelFile = null;

                var v1LabelW = 0;
                var v2LabelW = 0;
                var v3LabelW = 0;
                var v4LabelW = 0;

                if (!string.IsNullOrWhiteSpace(parameters.Video1AthleteName))
                {
                    v1LabelW = ComputeOverlayWidthApprox(parameters.Video1AthleteName!, v1InfoText, scale, panelW);
                    v1LabelFile = await CreateAthleteInfoOverlayImageAsync(parameters.Video1AthleteName!, v1InfoText, v1LabelW, overlayH, "q_v1_label", cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(parameters.Video2AthleteName))
                {
                    v2LabelW = ComputeOverlayWidthApprox(parameters.Video2AthleteName!, v2InfoText, scale, panelW);
                    v2LabelFile = await CreateAthleteInfoOverlayImageAsync(parameters.Video2AthleteName!, v2InfoText, v2LabelW, overlayH, "q_v2_label", cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(parameters.Video3AthleteName))
                {
                    v3LabelW = ComputeOverlayWidthApprox(parameters.Video3AthleteName!, v3InfoText, scale, panelW);
                    v3LabelFile = await CreateAthleteInfoOverlayImageAsync(parameters.Video3AthleteName!, v3InfoText, v3LabelW, overlayH, "q_v3_label", cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(parameters.Video4AthleteName))
                {
                    v4LabelW = ComputeOverlayWidthApprox(parameters.Video4AthleteName!, v4InfoText, scale, panelW);
                    v4LabelFile = await CreateAthleteInfoOverlayImageAsync(parameters.Video4AthleteName!, v4InfoText, v4LabelW, overlayH, "q_v4_label", cancellationToken);
                }

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

                    // Base: fondo negro por segmento
                    var bgClip = await MediaClip.CreateFromImageFileAsync(syncBackgroundFile, segDuration);
                    syncComposition.Clips.Add(bgClip);

                    // Videos 1-4 como overlays, cada uno en su cuadrante
                    var segClip1 = await MediaClip.CreateFromFileAsync(video1File);
                    segClip1.TrimTimeFromStart = s1;
                    segClip1.TrimTimeFromEnd = segClip1.OriginalDuration - e1;
                    syncOverlayLayer.Overlays.Add(new MediaOverlay(segClip1) { Position = pos1, Delay = cursor, Opacity = 1.0 });

                    var segClip2 = await MediaClip.CreateFromFileAsync(video2File);
                    segClip2.TrimTimeFromStart = s2;
                    segClip2.TrimTimeFromEnd = segClip2.OriginalDuration - e2;
                    syncOverlayLayer.Overlays.Add(new MediaOverlay(segClip2) { Position = pos2, Delay = cursor, Opacity = 1.0 });

                    var segClip3 = await MediaClip.CreateFromFileAsync(video3File);
                    segClip3.TrimTimeFromStart = s3;
                    segClip3.TrimTimeFromEnd = segClip3.OriginalDuration - e3;
                    syncOverlayLayer.Overlays.Add(new MediaOverlay(segClip3) { Position = pos3, Delay = cursor, Opacity = 1.0 });

                    var segClip4 = await MediaClip.CreateFromFileAsync(video4File);
                    segClip4.TrimTimeFromStart = s4;
                    segClip4.TrimTimeFromEnd = segClip4.OriginalDuration - e4;
                    syncOverlayLayer.Overlays.Add(new MediaOverlay(segClip4) { Position = pos4, Delay = cursor, Opacity = 1.0 });

                    // Etiquetas por cuadrante
                    if (v1LabelFile != null)
                    {
                        var w = Math.Min(v1LabelW, (int)Math.Round(pos1.Width - 20));
                        var labelClip = await MediaClip.CreateFromImageFileAsync(v1LabelFile, segDuration);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(labelClip)
                        {
                            Position = new WinFoundation.Rect(pos1.X + 10, pos1.Y + pos1.Height - overlayH - 10, w, overlayH),
                            Delay = cursor,
                            Opacity = 1.0
                        });
                    }
                    if (v2LabelFile != null)
                    {
                        var w = Math.Min(v2LabelW, (int)Math.Round(pos2.Width - 20));
                        var labelClip = await MediaClip.CreateFromImageFileAsync(v2LabelFile, segDuration);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(labelClip)
                        {
                            Position = new WinFoundation.Rect(pos2.X + 10, pos2.Y + pos2.Height - overlayH - 10, w, overlayH),
                            Delay = cursor,
                            Opacity = 1.0
                        });
                    }
                    if (v3LabelFile != null)
                    {
                        var w = Math.Min(v3LabelW, (int)Math.Round(pos3.Width - 20));
                        var labelClip = await MediaClip.CreateFromImageFileAsync(v3LabelFile, segDuration);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(labelClip)
                        {
                            Position = new WinFoundation.Rect(pos3.X + 10, pos3.Y + pos3.Height - overlayH - 10, w, overlayH),
                            Delay = cursor,
                            Opacity = 1.0
                        });
                    }
                    if (v4LabelFile != null)
                    {
                        var w = Math.Min(v4LabelW, (int)Math.Round(pos4.Width - 20));
                        var labelClip = await MediaClip.CreateFromImageFileAsync(v4LabelFile, segDuration);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(labelClip)
                        {
                            Position = new WinFoundation.Rect(pos4.X + 10, pos4.Y + pos4.Height - overlayH - 10, w, overlayH),
                            Delay = cursor,
                            Opacity = 1.0
                        });
                    }

                    // Padding base (video 1)
                    if (segDuration > d1)
                    {
                        var pad = segDuration - d1;
                        var stillFile = await CreateStillImageAsync(clip1, e1, "lapq1", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(stillClip) { Position = pos1, Delay = cursor + d1, Opacity = 1.0 });
                    }

                    // Padding overlays 2-4
                    if (segDuration > d2)
                    {
                        var pad = segDuration - d2;
                        var stillFile = await CreateStillImageAsync(clip2, e2, "lapq2", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(stillClip) { Position = pos2, Delay = cursor + d2, Opacity = 1.0 });
                    }
                    if (segDuration > d3)
                    {
                        var pad = segDuration - d3;
                        var stillFile = await CreateStillImageAsync(clip3, e3, "lapq3", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(stillClip) { Position = pos3, Delay = cursor + d3, Opacity = 1.0 });
                    }
                    if (segDuration > d4)
                    {
                        var pad = segDuration - d4;
                        var stillFile = await CreateStillImageAsync(clip4, e4, "lapq4", cancellationToken);
                        var stillClip = await MediaClip.CreateFromImageFileAsync(stillFile, pad);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(stillClip) { Position = pos4, Delay = cursor + d4, Opacity = 1.0 });
                    }

                    cursor += segDuration;
                }

                syncComposition.OverlayLayers.Add(syncOverlayLayer);

                // Configurar encoding - 2x2 grid a 1920x1080 total
                var syncEncodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
                syncEncodingProfile.Video!.Width = syncOutputWidth;
                syncEncodingProfile.Video.Height = syncOutputHeight;

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

            // Crear fondo negro como clip base (2x2 grid a 1920x1080 total)
            // En Windows.Media.Editing, los overlays se superponen sobre el clip base,
            // así que para hacer un grid 2x2, necesitamos un fondo vacío y los 4 videos como overlays.
            const uint outputWidth = 1920;
            const uint outputHeight = 1080;
            
            var backgroundFile = await CreateBlackBackgroundImageAsync(
                (int)outputWidth, 
                (int)outputHeight, 
                "quad_bg", 
                cancellationToken);
            var backgroundClip = await MediaClip.CreateFromImageFileAsync(backgroundFile, minDuration);

            // Crear composición con el fondo negro
            var composition = new MediaComposition();
            composition.Clips.Add(backgroundClip);

            // Crear overlay layer para los 4 videos
            var overlayTrack = new MediaOverlayLayer();

            var quadHalfW = (double)outputWidth / 2.0;
            var quadHalfH = (double)outputHeight / 2.0;

            // Clip 1 - Arriba izquierda
            var overlay1 = new MediaOverlay(clip1)
            {
                Position = new WinFoundation.Rect(0, 0, quadHalfW, quadHalfH),
                Delay = TimeSpan.Zero
            };
            overlayTrack.Overlays.Add(overlay1);

            // Clip 2 - Arriba derecha
            var overlay2 = new MediaOverlay(clip2)
            {
                Position = new WinFoundation.Rect(quadHalfW, 0, quadHalfW, quadHalfH),
                Delay = TimeSpan.Zero
            };
            overlayTrack.Overlays.Add(overlay2);

            // Clip 3 - Abajo izquierda
            var overlay3 = new MediaOverlay(clip3)
            {
                Position = new WinFoundation.Rect(0, quadHalfH, quadHalfW, quadHalfH),
                Delay = TimeSpan.Zero
            };
            overlayTrack.Overlays.Add(overlay3);

            // Clip 4 - Abajo derecha
            var overlay4 = new MediaOverlay(clip4)
            {
                Position = new WinFoundation.Rect(quadHalfW, quadHalfH, quadHalfW, quadHalfH),
                Delay = TimeSpan.Zero
            };
            overlayTrack.Overlays.Add(overlay4);

            // Overlays de info por vídeo (equivalente a macOS)
            var q1Info = BuildInfoText(parameters.Video1Category, parameters.Video1Section, parameters.Video1Time, parameters.Video1Penalties);
            var q2Info = BuildInfoText(parameters.Video2Category, parameters.Video2Section, parameters.Video2Time, parameters.Video2Penalties);
            var q3Info = BuildInfoText(parameters.Video3Category, parameters.Video3Section, parameters.Video3Time, parameters.Video3Penalties);
            var q4Info = BuildInfoText(parameters.Video4Category, parameters.Video4Section, parameters.Video4Time, parameters.Video4Penalties);

            var panelW2 = (int)Math.Round(quadHalfW);
            var panelH2 = (int)Math.Round(quadHalfH);
            var scaleQ = panelH2 / 1080.0;
            var labelHQ = (int)Math.Max(40, Math.Round(70 * scaleQ));

            async Task AddQuadLabelAsync(string? athleteName, string info, double x, double y)
            {
                if (string.IsNullOrWhiteSpace(athleteName))
                    return;

                var labelW = ComputeOverlayWidthApprox(athleteName!, info, scaleQ, panelW2);
                var file = await CreateAthleteInfoOverlayImageAsync(athleteName!, info, labelW, labelHQ, "quad_label", cancellationToken);
                var clip = await MediaClip.CreateFromImageFileAsync(file, minDuration);
                overlayTrack.Overlays.Add(new MediaOverlay(clip)
                {
                    Position = new WinFoundation.Rect(x + 10, y + panelH2 - labelHQ - 10, labelW, labelHQ),
                    Delay = TimeSpan.Zero,
                    Opacity = 1.0
                });
            }

            await AddQuadLabelAsync(parameters.Video1AthleteName, q1Info, 0, 0);
            await AddQuadLabelAsync(parameters.Video2AthleteName, q2Info, quadHalfW, 0);
            await AddQuadLabelAsync(parameters.Video3AthleteName, q3Info, 0, quadHalfH);
            await AddQuadLabelAsync(parameters.Video4AthleteName, q4Info, quadHalfW, quadHalfH);

            composition.OverlayLayers.Add(overlayTrack);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Quad layout - 4 videos in 2x2 grid");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Output size: {outputWidth}x{outputHeight}");

            // Configurar encoding - 2x2 grid a 1920x1080 total
            var encodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            encodingProfile.Video!.Width = outputWidth;
            encodingProfile.Video.Height = outputHeight;

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
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Quad export completed successfully: {parameters.OutputPath}");
            
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
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] ExportQuadVideosAsync ERROR: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] StackTrace: {ex.StackTrace}");
            return new VideoExportResult
            {
                Success = false,
                ErrorMessage = $"{ex.GetType().Name}: {ex.Message}"
            };
        }
    }
}
