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
        => await CreatePillLabelImageAsync(text, backgroundColor, textColor, width, height, prefix, rounded: false, cancellationToken);

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

        var cornerRadius = 0f;
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
            
            // Configurar posiciones según el layout.
            // Para mantener paridad con el modo SyncByLaps:
            // - Horizontal: dejar una barra negra inferior para los nombres.
            // - Vertical: nombres sobre los vídeos (esquinas), sin barra.
            var uiScale2 = outputHeight / 1080.0;
            var bottomBarH2 = parameters.IsHorizontalLayout
                ? (int)Math.Clamp(Math.Round(160 * uiScale2), 110, 260)
                : 0;
            var videoAreaH2 = (double)outputHeight - bottomBarH2;
            var bottomBarY2 = videoAreaH2;

            var panel1Rect = parameters.IsHorizontalLayout
                ? new WinFoundation.Rect(0, 0, (double)outputWidth / 2.0, videoAreaH2)
                : new WinFoundation.Rect(0, 0, outputWidth, (double)outputHeight / 2.0);
            var panel2Rect = parameters.IsHorizontalLayout
                ? new WinFoundation.Rect((double)outputWidth / 2.0, 0, (double)outputWidth / 2.0, videoAreaH2)
                : new WinFoundation.Rect(0, (double)outputHeight / 2.0, outputWidth, (double)outputHeight / 2.0);

            // Configurar posición del video 1 según el layout
            var overlay1 = new MediaOverlay(clip1);
            overlay1.Position = panel1Rect;
            overlay1.Delay = TimeSpan.Zero;
            overlay1.Opacity = 1.0; // Asegurar opacidad completa
            overlayTrack.Overlays.Add(overlay1);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Overlay1 added - Position: {overlay1.Position}, Opacity: {overlay1.Opacity}");
            
            // Configurar posición del video 2 según el layout
            var overlay2 = new MediaOverlay(clip2);
            overlay2.Position = panel2Rect;
            overlay2.Delay = TimeSpan.Zero;
            overlay2.Opacity = 1.0; // Asegurar opacidad completa
            overlayTrack.Overlays.Add(overlay2);
            
            System.Diagnostics.Debug.WriteLine($"[WindowsVideoComposition] Overlay2 added - Position: {overlay2.Position}, Opacity: {overlay2.Opacity}");

            // Labels de atletas: mismo formato y colocación que en SyncByLaps.
            if (parameters.IsHorizontalLayout)
            {
                var barPanelW2 = (double)outputWidth / 2.0;
                var nameH2 = (int)Math.Max(26, Math.Round(30 * uiScale2));
                var nameW2 = (int)Math.Min(barPanelW2 * 0.80, barPanelW2 - (20 * uiScale2));
                nameW2 = Math.Max(1, nameW2);
                var nameY2 = bottomBarY2 + (bottomBarH2 * 0.68);

                if (!string.IsNullOrWhiteSpace(parameters.Video1AthleteName))
                {
                    var nameImg = await CreatePlainTextImageAsync(parameters.Video1AthleteName!.Trim(), SKColors.White, nameW2, nameH2, "p_v1_name", cancellationToken);
                    var nameClip = await MediaClip.CreateFromImageFileAsync(nameImg, minDuration);
                    overlayTrack.Overlays.Add(new MediaOverlay(nameClip)
                    {
                        Position = new WinFoundation.Rect(0 + (barPanelW2 - nameW2) / 2.0, nameY2, nameW2, nameH2),
                        Delay = TimeSpan.Zero,
                        Opacity = 1.0
                    });
                }

                if (!string.IsNullOrWhiteSpace(parameters.Video2AthleteName))
                {
                    var nameImg = await CreatePlainTextImageAsync(parameters.Video2AthleteName!.Trim(), SKColors.White, nameW2, nameH2, "p_v2_name", cancellationToken);
                    var nameClip = await MediaClip.CreateFromImageFileAsync(nameImg, minDuration);
                    overlayTrack.Overlays.Add(new MediaOverlay(nameClip)
                    {
                        Position = new WinFoundation.Rect(((double)outputWidth / 2.0) + (barPanelW2 - nameW2) / 2.0, nameY2, nameW2, nameH2),
                        Delay = TimeSpan.Zero,
                        Opacity = 1.0
                    });
                }
            }
            else
            {
                // Modo vertical (stacked): colocar nombres igual que en SyncByLaps (debajo del pill, aunque aquí no haya pill).
                var pad = Math.Max(12.0, Math.Round(18 * uiScale2));
                var gap = Math.Max(6.0, Math.Round(8 * uiScale2));

                var vertLapH = (int)Math.Clamp(Math.Round(46 * uiScale2), 34, 84);
                var vertNameH = (int)Math.Clamp(Math.Round(22 * uiScale2), 18, 50);
                var vertNameMaxW = (int)Math.Min(Math.Round(420 * uiScale2), Math.Round(outputWidth * 0.48));
                vertNameMaxW = (int)Math.Min(vertNameMaxW, outputWidth - (pad * 2.0));

                var v1Name = string.IsNullOrWhiteSpace(parameters.Video1AthleteName) ? "" : parameters.Video1AthleteName!.Trim();
                var v2Name = string.IsNullOrWhiteSpace(parameters.Video2AthleteName) ? "" : parameters.Video2AthleteName!.Trim();
                var v1NameW = string.IsNullOrWhiteSpace(v1Name) ? 0 : ComputePlainTextWidthTight(v1Name, vertNameH, vertNameMaxW);
                var v2NameW = string.IsNullOrWhiteSpace(v2Name) ? 0 : ComputePlainTextWidthTight(v2Name, vertNameH, vertNameMaxW);

                var posTop = panel1Rect;
                var posBottom = panel2Rect;

                var blockH = vertLapH + gap + vertNameH;

                var rightNameX1 = posTop.X + posTop.Width - pad - Math.Max(1, v1NameW);
                var rightNameX2 = posBottom.X + posBottom.Width - pad - Math.Max(1, v2NameW);

                var v1BlockTop = (posTop.Y + posTop.Height) - pad - blockH;
                var v2BlockTop = posBottom.Y + pad;

                var v1NameRect = new WinFoundation.Rect(rightNameX1, v1BlockTop + vertLapH + gap, Math.Max(1, v1NameW), vertNameH);
                var v2NameRect = new WinFoundation.Rect(rightNameX2, v2BlockTop + vertLapH + gap, Math.Max(1, v2NameW), vertNameH);

                if (!string.IsNullOrWhiteSpace(v1Name))
                {
                    var v1NameImg = await CreatePlainTextImageAsync(v1Name, SKColors.White, Math.Max(1, v1NameW), vertNameH, "p_v1_name_v", cancellationToken);
                    var v1NameClip = await MediaClip.CreateFromImageFileAsync(v1NameImg, minDuration);
                    overlayTrack.Overlays.Add(new MediaOverlay(v1NameClip) { Position = v1NameRect, Delay = TimeSpan.Zero, Opacity = 1.0 });
                }

                if (!string.IsNullOrWhiteSpace(v2Name))
                {
                    var v2NameImg = await CreatePlainTextImageAsync(v2Name, SKColors.White, Math.Max(1, v2NameW), vertNameH, "p_v2_name_v", cancellationToken);
                    var v2NameClip = await MediaClip.CreateFromImageFileAsync(v2NameImg, minDuration);
                    overlayTrack.Overlays.Add(new MediaOverlay(v2NameClip) { Position = v2NameRect, Delay = TimeSpan.Zero, Opacity = 1.0 });
                }
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

                // Labels por cuadrante (mismo estilo que doble): pill de lap + nombre debajo.
                // Ubicación pedida:
                // - Columna 0: esquina inferior derecha
                // - Columna 1: esquina inferior izquierda
                var panelW = (int)Math.Round(halfW);
                var panelH = (int)Math.Round(halfH);

                var quadUiScale = Math.Clamp(panelH / 540.0, 0.60, 1.40);
                var quadPad = Math.Max(10.0, Math.Round(14 * quadUiScale));
                var quadGap = Math.Max(6.0, Math.Round(8 * quadUiScale));

                var quadLapH = (int)Math.Clamp(Math.Round(44 * quadUiScale), 32, 72);
                var quadNameH = (int)Math.Clamp(Math.Round(20 * quadUiScale), 16, 44);

                var quadLapMaxW = (int)Math.Min(Math.Round(panelW * 0.92), Math.Round(520 * quadUiScale));
                quadLapMaxW = Math.Max(1, Math.Min(quadLapMaxW, (int)Math.Round(panelW - (quadPad * 2.0))));

                var quadNameMaxW = (int)Math.Min(Math.Round(panelW * 0.92), Math.Round(520 * quadUiScale));
                quadNameMaxW = Math.Max(1, Math.Min(quadNameMaxW, (int)Math.Round(panelW - (quadPad * 2.0))));

                var v1Name = (parameters.Video1AthleteName ?? string.Empty).Trim();
                var v2Name = (parameters.Video2AthleteName ?? string.Empty).Trim();
                var v3Name = (parameters.Video3AthleteName ?? string.Empty).Trim();
                var v4Name = (parameters.Video4AthleteName ?? string.Empty).Trim();

                StorageFile? v1NameFile = null;
                StorageFile? v2NameFile = null;
                StorageFile? v3NameFile = null;
                StorageFile? v4NameFile = null;

                var v1NameW = 0;
                var v2NameW = 0;
                var v3NameW = 0;
                var v4NameW = 0;

                if (!string.IsNullOrWhiteSpace(v1Name))
                {
                    v1NameW = ComputePlainTextWidthTight(v1Name, quadNameH, quadNameMaxW);
                    v1NameFile = await CreatePlainTextImageAsync(v1Name, SKColors.White, v1NameW, quadNameH, "q_name_1", cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(v2Name))
                {
                    v2NameW = ComputePlainTextWidthTight(v2Name, quadNameH, quadNameMaxW);
                    v2NameFile = await CreatePlainTextImageAsync(v2Name, SKColors.White, v2NameW, quadNameH, "q_name_2", cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(v3Name))
                {
                    v3NameW = ComputePlainTextWidthTight(v3Name, quadNameH, quadNameMaxW);
                    v3NameFile = await CreatePlainTextImageAsync(v3Name, SKColors.White, v3NameW, quadNameH, "q_name_3", cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(v4Name))
                {
                    v4NameW = ComputePlainTextWidthTight(v4Name, quadNameH, quadNameMaxW);
                    v4NameFile = await CreatePlainTextImageAsync(v4Name, SKColors.White, v4NameW, quadNameH, "q_name_4", cancellationToken);
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

                    // ===== Overlays de parciales por lap + nombres (siempre por encima de los freezes) =====
                    var green = new SKColor(52, 199, 89);
                    var red = new SKColor(255, 59, 48);
                    var white = SKColors.White;

                    var minDur = new[] { d1, d2, d3, d4 }.Min();
                    var maxDur = new[] { d1, d2, d3, d4 }.Max();
                    var anyDiff = minDur != maxDur;

                    SKColor PickLapColor(TimeSpan dur)
                    {
                        if (!anyDiff) return white;
                        if (dur == minDur) return green;
                        if (dur == maxDur) return red;
                        return white;
                    }

                    var lapColor1 = PickLapColor(d1);
                    var lapColor2 = PickLapColor(d2);
                    var lapColor3 = PickLapColor(d3);
                    var lapColor4 = PickLapColor(d4);

                    var lapText1 = $"Lap {i + 1}: {FormatLap(d1)}";
                    var lapText2 = $"Lap {i + 1}: {FormatLap(d2)}";
                    var lapText3 = $"Lap {i + 1}: {FormatLap(d3)}";
                    var lapText4 = $"Lap {i + 1}: {FormatLap(d4)}";

                    // Delta global (más rápido vs más lento) centrado en el grid, como en QuadPlayer.
                    var diff = maxDur - minDur;
                    if (diff < TimeSpan.Zero)
                        diff = -diff;
                    var diffText = $"Δ {FormatLap(diff)}";

                    var availableW = (int)Math.Max(1, Math.Round(pos1.Width - (quadPad * 2.0)));
                    var lapW1 = Math.Min(ComputePillWidthTight(lapText1, quadLapH, quadLapMaxW), availableW);
                    var lapW2 = Math.Min(ComputePillWidthTight(lapText2, quadLapH, quadLapMaxW), availableW);
                    var lapW3 = Math.Min(ComputePillWidthTight(lapText3, quadLapH, quadLapMaxW), availableW);
                    var lapW4 = Math.Min(ComputePillWidthTight(lapText4, quadLapH, quadLapMaxW), availableW);

                    // Mostrar nombre solo si cabe en alto dentro del panel.
                    bool CanShowName(WinFoundation.Rect p) =>
                        (v1NameFile != null || v2NameFile != null || v3NameFile != null || v4NameFile != null)
                        && (quadLapH + quadGap + quadNameH) <= (p.Height - (quadPad * 2.0));

                    var showName1 = v1NameFile != null && CanShowName(pos1);
                    var showName2 = v2NameFile != null && CanShowName(pos2);
                    var showName3 = v3NameFile != null && CanShowName(pos3);
                    var showName4 = v4NameFile != null && CanShowName(pos4);

                    async Task AddLapAndNameOverlayAsync(
                        WinFoundation.Rect panel,
                        bool anchorRight,
                        bool anchorTop,
                        string lapText,
                        SKColor lapColor,
                        int lapW,
                        StorageFile? nameFile,
                        int nameW,
                        bool showName,
                        string prefix)
                    {
                        var lapBg = Darken(lapColor, 0.35f);
                        var blockH = showName ? (quadLapH + quadGap + quadNameH) : quadLapH;
                        var xLapMin = panel.X + quadPad;
                        var xLapMax = panel.X + panel.Width - quadPad - lapW;
                        var lapX = anchorRight ? xLapMax : xLapMin;
                        lapX = Math.Clamp(lapX, xLapMin, xLapMax);

                        var yTop = anchorTop
                            ? (panel.Y + quadPad)
                            : (panel.Y + panel.Height - quadPad - blockH);
                        var yMin = panel.Y + quadPad;
                        var yMax = panel.Y + panel.Height - quadPad - blockH;
                        yTop = Math.Clamp(yTop, yMin, yMax);

                        // Lap pill
                        var lapImg = await CreatePillLabelImageAsync(lapText, lapBg, lapColor, lapW, quadLapH, prefix, cancellationToken);
                        var lapClip = await MediaClip.CreateFromImageFileAsync(lapImg, segDuration);
                        syncOverlayLayer.Overlays.Add(new MediaOverlay(lapClip)
                        {
                            Position = new WinFoundation.Rect(lapX, yTop, lapW, quadLapH),
                            Delay = cursor,
                            Opacity = 1.0
                        });

                        if (showName && nameFile != null && nameW > 0)
                        {
                            var clampedNameW = Math.Min(nameW, (int)Math.Max(1, Math.Round(panel.Width - (quadPad * 2.0))));
                            var xNameMin = panel.X + quadPad;
                            var xNameMax = panel.X + panel.Width - quadPad - clampedNameW;
                            var nameX = anchorRight ? xNameMax : xNameMin;
                            nameX = Math.Clamp(nameX, xNameMin, xNameMax);

                            var nameClip = await MediaClip.CreateFromImageFileAsync(nameFile, segDuration);
                            syncOverlayLayer.Overlays.Add(new MediaOverlay(nameClip)
                            {
                                Position = new WinFoundation.Rect(nameX, yTop + quadLapH + quadGap, clampedNameW, quadNameH),
                                Delay = cursor,
                                Opacity = 1.0
                            });
                        }
                    }

                    // Column 0 (pos1,pos3): bottom-right. Column 1 (pos2,pos4): bottom-left.
                    await AddLapAndNameOverlayAsync(pos1, anchorRight: true, anchorTop: false, lapText1, lapColor1, lapW1, v1NameFile, v1NameW, showName1, $"q_lap_1_{i + 1}");
                    await AddLapAndNameOverlayAsync(pos2, anchorRight: false, anchorTop: false, lapText2, lapColor2, lapW2, v2NameFile, v2NameW, showName2, $"q_lap_2_{i + 1}");

                    // Fila inferior: arriba del cuadrante para alinear los 4 labels en la junta central
                    await AddLapAndNameOverlayAsync(pos3, anchorRight: true, anchorTop: true, lapText3, lapColor3, lapW3, v3NameFile, v3NameW, showName3, $"q_lap_3_{i + 1}");
                    await AddLapAndNameOverlayAsync(pos4, anchorRight: false, anchorTop: true, lapText4, lapColor4, lapW4, v4NameFile, v4NameW, showName4, $"q_lap_4_{i + 1}");

                    // Delta central (fondo oscuro + texto blanco)
                    var deltaH = quadLapH;
                    var deltaMaxW = (int)Math.Max(1, Math.Round(syncOutputWidth * 0.60));
                    var deltaW = ComputePillWidthTight(diffText, deltaH, deltaMaxW);
                    var deltaBg = new SKColor(0, 0, 0, 220);
                    var deltaImg = await CreatePillLabelImageAsync(diffText, deltaBg, white, deltaW, deltaH, $"q_delta_{i + 1}", cancellationToken);
                    var deltaClip = await MediaClip.CreateFromImageFileAsync(deltaImg, segDuration);
                    syncOverlayLayer.Overlays.Add(new MediaOverlay(deltaClip)
                    {
                        Position = new WinFoundation.Rect((syncOutputWidth - deltaW) / 2.0, (syncOutputHeight - deltaH) / 2.0, deltaW, deltaH),
                        Delay = cursor,
                        Opacity = 1.0
                    });

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

            var panelW2 = (int)Math.Round(quadHalfW);
            var panelH2 = (int)Math.Round(quadHalfH);
            // Labels de atletas (solo nombre) con el mismo estilo/posición que en SyncByLaps.
            var quadUiScale2 = Math.Clamp(panelH2 / 540.0, 0.60, 1.40);
            var quadPad2 = Math.Max(10.0, Math.Round(14 * quadUiScale2));
            var quadGap2 = Math.Max(6.0, Math.Round(8 * quadUiScale2));
            var quadLapH2 = (int)Math.Clamp(Math.Round(44 * quadUiScale2), 32, 72);
            var quadNameH2 = (int)Math.Clamp(Math.Round(20 * quadUiScale2), 16, 44);

            var quadNameMaxW2 = (int)Math.Min(Math.Round(panelW2 * 0.92), Math.Round(520 * quadUiScale2));
            quadNameMaxW2 = Math.Max(1, Math.Min(quadNameMaxW2, (int)Math.Round(panelW2 - (quadPad2 * 2.0))));

            var quadPanel1 = new WinFoundation.Rect(0, 0, quadHalfW, quadHalfH);
            var quadPanel2 = new WinFoundation.Rect(quadHalfW, 0, quadHalfW, quadHalfH);
            var quadPanel3 = new WinFoundation.Rect(0, quadHalfH, quadHalfW, quadHalfH);
            var quadPanel4 = new WinFoundation.Rect(quadHalfW, quadHalfH, quadHalfW, quadHalfH);

            async Task AddQuadNameAsync(WinFoundation.Rect panel, string? athleteName, bool anchorRight, bool anchorTop, string prefix)
            {
                var name = (athleteName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    return;

                var nameW = ComputePlainTextWidthTight(name, quadNameH2, quadNameMaxW2);
                var blockH = quadLapH2 + quadGap2 + quadNameH2;

                var yTop = anchorTop
                    ? (panel.Y + quadPad2)
                    : (panel.Y + panel.Height - quadPad2 - blockH);
                var yMin = panel.Y + quadPad2;
                var yMax = panel.Y + panel.Height - quadPad2 - blockH;
                yTop = Math.Clamp(yTop, yMin, yMax);

                var xMin = panel.X + quadPad2;
                var xMax = panel.X + panel.Width - quadPad2 - nameW;
                var x = anchorRight ? xMax : xMin;
                x = Math.Clamp(x, xMin, xMax);

                var nameImg = await CreatePlainTextImageAsync(name, SKColors.White, nameW, quadNameH2, prefix, cancellationToken);
                var nameClip = await MediaClip.CreateFromImageFileAsync(nameImg, minDuration);
                overlayTrack.Overlays.Add(new MediaOverlay(nameClip)
                {
                    Position = new WinFoundation.Rect(x, yTop + quadLapH2 + quadGap2, nameW, quadNameH2),
                    Delay = TimeSpan.Zero,
                    Opacity = 1.0
                });
            }

            // Column 0: bottom-right. Column 1: bottom-left. Fila inferior anclada arriba para alinear en la junta central.
            await AddQuadNameAsync(quadPanel1, parameters.Video1AthleteName, anchorRight: true, anchorTop: false, "q_name_1_nosync");
            await AddQuadNameAsync(quadPanel2, parameters.Video2AthleteName, anchorRight: false, anchorTop: false, "q_name_2_nosync");
            await AddQuadNameAsync(quadPanel3, parameters.Video3AthleteName, anchorRight: true, anchorTop: true, "q_name_3_nosync");
            await AddQuadNameAsync(quadPanel4, parameters.Video4AthleteName, anchorRight: false, anchorTop: true, "q_name_4_nosync");

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
