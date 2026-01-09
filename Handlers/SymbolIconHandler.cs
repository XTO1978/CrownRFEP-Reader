using Microsoft.Maui.Handlers;
using CrownRFEP_Reader.Views.Controls;

#if IOS || MACCATALYST
using Microsoft.Maui.Platform;
using UIKit;

namespace CrownRFEP_Reader.Handlers;

public class SymbolIconHandler : ViewHandler<SymbolIcon, UIImageView>
{
    public static readonly IPropertyMapper<SymbolIcon, SymbolIconHandler> Mapper =
        new PropertyMapper<SymbolIcon, SymbolIconHandler>(ViewMapper)
        {
            [nameof(SymbolIcon.SymbolName)] = MapSymbolName,
            [nameof(SymbolIcon.TintColor)] = MapTintColor,
        };

    public SymbolIconHandler() : base(Mapper)
    {
    }

    protected override UIImageView CreatePlatformView()
    {
        var imageView = new UIImageView
        {
            ContentMode = UIViewContentMode.ScaleAspectFit
        };

        return imageView;
    }

    protected override void ConnectHandler(UIImageView platformView)
    {
        base.ConnectHandler(platformView);
        UpdateImage();
        UpdateTint();
    }

    protected override void DisconnectHandler(UIImageView platformView)
    {
        platformView.Image = null;
        base.DisconnectHandler(platformView);
    }

    public static void MapSymbolName(SymbolIconHandler handler, SymbolIcon view) => handler.UpdateImage();
    public static void MapTintColor(SymbolIconHandler handler, SymbolIcon view) => handler.UpdateTint();

    private void UpdateImage()
    {
        if (PlatformView == null || VirtualView == null) return;

        var symbolName = VirtualView.SymbolName;
        if (string.IsNullOrWhiteSpace(symbolName))
        {
            PlatformView.Image = null;
            return;
        }

        var image = UIImage.GetSystemImage(symbolName);
        PlatformView.Image = image?.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);
    }

    private void UpdateTint()
    {
        if (PlatformView == null || VirtualView == null) return;
        PlatformView.TintColor = VirtualView.TintColor.ToPlatform();
    }
}

#elif WINDOWS
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CustomSymbolIcon = CrownRFEP_Reader.Views.Controls.SymbolIcon;

namespace CrownRFEP_Reader.Handlers;

public class SymbolIconHandler : ViewHandler<CustomSymbolIcon, FontIcon>
{
    /// <summary>
    /// Mapeo completo de SF Symbols (Apple) a Segoe Fluent Icons (Windows).
    /// Los códigos de glyph corresponden a caracteres Unicode de Segoe Fluent Icons.
    /// Referencia: https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font
    /// </summary>
    private static readonly Dictionary<string, string> SfSymbolToFluentIconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // === NAVIGATION & ARROWS ===
        ["chevron.left"] = "\uE76B",           // ChevronLeft
        ["chevron.right"] = "\uE76C",          // ChevronRight
        ["chevron.up"] = "\uE70E",             // ChevronUp
        ["chevron.down"] = "\uE70D",           // ChevronDown
        ["chevron.up.chevron.down"] = "\uE021", // ChevronUpDown (or use custom)
        ["arrow.left"] = "\uE72B",             // Back
        ["arrow.right"] = "\uE72A",            // Forward
        ["arrow.up"] = "\uE74A",               // Up
        ["arrow.down"] = "\uE74B",             // Down
        ["arrow.turn.down.right"] = "\uE7A6",  // RepeatAll (closest match)
        ["arrow.triangle.branch"] = "\uE8D4",  // BranchFork
        ["arrow.clockwise"] = "\uE72C",       // Refresh
        
        // === MEDIA PLAYBACK ===
        ["play.fill"] = "\uE768",              // Play
        ["pause.fill"] = "\uE769",             // Pause
        ["stop.fill"] = "\uE71A",              // Stop
        ["play.circle.fill"] = "\uE768",       // Play (no circle variant, using solid)
        ["backward.frame.fill"] = "\uE892",    // Previous (frame back)
        ["forward.frame.fill"] = "\uE893",     // Next (frame forward)
        ["gobackward.5"] = "\uED3C",           // Rewind (5 sec back - using rewind icon)
        ["goforward.5"] = "\uED3D",            // FastForward (5 sec forward)
        ["play.rectangle.on.rectangle"] = "\uE8B2", // TwoPagesView
        ["film"] = "\uE8B2",                   // Video
        ["film.stack"] = "\uE8B2",             // Video stack
        ["video"] = "\uE714",                  // Video
        ["video.fill"] = "\uE714",             // Video filled
        ["record.circle"] = "\uE7C8",          // Record
        
        // === FILES & FOLDERS ===
        ["folder"] = "\uE8B7",                 // Folder
        ["folder.fill"] = "\uED41",            // FolderFilled
        ["folder.badge.plus"] = "\uE8F4",      // NewFolder
        ["plus.rectangle.on.folder"] = "\uE8F4", // NewFolder (create new library)
        ["arrow.triangle.merge"] = "\uE8C8",   // Merge (combine libraries)
        ["doc"] = "\uE8A5",                    // Document
        ["doc.fill"] = "\uE8A5",               // Document filled
        ["doc.text"] = "\uE8A5",               // Document with text
        ["square.and.arrow.down"] = "\uE896",  // Download
        ["square.and.arrow.up"] = "\uE898",    // Share/Upload
        ["link"] = "\uE71B",                  // Link
        
        // === CHECKMARKS & STATUS ===
        ["checkmark"] = "\uE73E",              // Accept/Checkmark
        ["checkmark.circle"] = "\uE73E",       // CheckMark
        ["checkmark.circle.fill"] = "\uE73E",  // CheckMark filled
        ["xmark"] = "\uE711",                  // Cancel/Close
        ["xmark.circle"] = "\uE711",           // Cancel circle
        ["xmark.circle.fill"] = "\uE711",      // Cancel circle filled
        
        // === EDITING ===
        ["pencil"] = "\uE70F",                 // Edit
        ["pencil.circle"] = "\uE70F",          // Edit circle
        ["trash"] = "\uE74D",                  // Delete
        ["trash.fill"] = "\uE74D",             // Delete filled
        ["trash.circle"] = "\uE74D",           // Delete circle
        ["scribble"] = "\uED63",               // InkingTool
        ["textformat"] = "\uE8D2",             // Font
        ["paintpalette"] = "\uE790",           // Color palette
        
        // === SHAPES ===
        ["rectangle"] = "\uE739",              // Rectangle
        ["rectangle.on.rectangle"] = "\uE8F4", // CopyTo
        ["rectangle.split.2x1"] = "\uE89F",    // DockLeft (split horizontal)
        ["rectangle.split.1x2"] = "\uE8A0",    // DockBottom (split vertical)
        ["rectangle.grid.2x2"] = "\uE8A9",     // ViewAll (grid)
        ["circle"] = "\uEA3A",                 // StatusCircle
        ["circle.fill"] = "\uEA3B",            // StatusCircleFilled
        ["square.on.circle"] = "\uE7F4",       // CalendarReply (closest match)
        ["line.diagonal"] = "\uE879",          // Line (diagonal)
        
        // === TIME & CALENDAR ===
        ["calendar"] = "\uE787",               // Calendar
        ["calendar.badge.exclamationmark"] = "\uE787", // Calendar with badge
        ["clock"] = "\uE823",                  // Clock
        ["clock.fill"] = "\uE823",             // Clock filled
        ["clock.badge.checkmark"] = "\uE823",  // Clock with checkmark
        ["timer"] = "\uE916",                  // Stopwatch/Timer
        
        // === LOCATION ===
        ["location"] = "\uE81D",               // MapPin
        ["location.fill"] = "\uE81D",          // MapPin filled
        
        // === PEOPLE & PROFILES ===
        ["person.circle"] = "\uE77B",          // Contact
        ["person.fill"] = "\uE77B",            // Contact
        ["person.badge.plus"] = "\uE8FA",      // AddFriend
        ["figure.pool.swim"] = "\uE913",       // Running/Sports (closest match)
        
        // === CAMERA & PHOTO ===
        ["camera"] = "\uE722",                 // Camera
        ["camera.fill"] = "\uE722",            // Camera filled
        ["photo"] = "\uE91B",                  // Photo
        ["photo.fill"] = "\uE91B",             // Photo filled
        ["photo.on.rectangle.angled"] = "\uE91B", // Photo on rectangle
        ["mic.fill"] = "\uE720",               // Microphone
        
        // === INFO & HELP ===
        ["info.circle"] = "\uE946",            // Info
        ["questionmark.circle"] = "\uE897",    // Help
        ["exclamationmark.triangle"] = "\uE7BA", // Warning
        
        // === BOOK & NOTES ===
        ["book"] = "\uE82D",                   // Library
        ["book.fill"] = "\uE82D",              // Library filled
        ["note.text"] = "\uE70B",              // Paste (note icon)
        
        // === TAGS & LABELS ===
        ["tag"] = "\uE8EC",                    // Tag
        ["tag.fill"] = "\uE8EC",               // Tag filled
        
        // === WEATHER ===
        ["sun.max.fill"] = "\uE706",           // PartlyCloudyDay (sun icon)
        ["cloud.fill"] = "\uE753",             // Cloud
        ["cloud.sun.fill"] = "\uE9BD",         // PartlyCloudyDay
        ["cloud.heavyrain.fill"] = "\uE9C3",   // RainShowersDay
        ["moon.fill"] = "\uE708",              // ClearNight
        ["sparkles"] = "\uE734",               // Brightness (sparkle effect)
        
        // === HEALTH & WELLNESS ===
        ["heart.fill"] = "\uEB51",             // Heart
        ["heart"] = "\uEB51",                  // Heart outline
        ["waveform.path.ecg"] = "\uE95A",      // HeartRate/ECG
        ["bolt.fill"] = "\uE945",              // Lightning/Energy
        
        // === MISC UI ===
        ["star"] = "\uE734",                   // FavoriteStar
        ["star.fill"] = "\uE735",              // FavoriteStarFill
        ["eye"] = "\uE7B3",                    // View
        ["eye.fill"] = "\uE7B3",               // View filled
        ["eye.slash"] = "\uED1A",              // Hide
        ["plus"] = "\uE710",                   // Add
        ["plus.circle"] = "\uE710",            // Add circle
        ["minus"] = "\uE738",                  // Remove
        ["line.3.horizontal"] = "\uE700",      // GlobalNavButton (hamburger)
        ["line.3.horizontal.circle"] = "\uE700", // GlobalNavButton
        ["line.3.horizontal.decrease"] = "\uE71C", // Filter
        ["chart.pie"] = "\uE9D9",              // PieChart (using diagnostic icon)
        
        // === ROWING/SPORTS SPECIFIC ===
        ["oar.2.crossed"] = "\uE913",          // Running (closest sports icon)
        
        // === MOOD SYMBOLS ===
        ["face.smiling"] = "\uE76E",           // Emoji
        ["face.smiling.fill"] = "\uE76E",      // Emoji filled
    };

    public static readonly IPropertyMapper<CustomSymbolIcon, SymbolIconHandler> Mapper =
        new PropertyMapper<CustomSymbolIcon, SymbolIconHandler>(ViewHandler.ViewMapper)
        {
            [nameof(CustomSymbolIcon.SymbolName)] = MapSymbolName,
            [nameof(CustomSymbolIcon.TintColor)] = MapTintColor,
            [nameof(CustomSymbolIcon.HeightRequest)] = MapSize,
            [nameof(CustomSymbolIcon.WidthRequest)] = MapSize,
        };

    public SymbolIconHandler() : base(Mapper)
    {
    }

    protected override FontIcon CreatePlatformView()
    {
        var icon = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 12, // Tamaño por defecto pequeño para Windows
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
        };

        return icon;
    }

    protected override void ConnectHandler(FontIcon platformView)
    {
        base.ConnectHandler(platformView);
        UpdateGlyph();
        UpdateTint();
        UpdateSize();
    }

    public static void MapSymbolName(SymbolIconHandler handler, CustomSymbolIcon view) => handler.UpdateGlyph();
    public static void MapTintColor(SymbolIconHandler handler, CustomSymbolIcon view) => handler.UpdateTint();
    public static void MapSize(SymbolIconHandler handler, CustomSymbolIcon view) => handler.UpdateSize();

    private void UpdateSize()
    {
        if (PlatformView == null || VirtualView == null) return;

        // IMPORTANTE (WinUI/DPI): si igualamos FontSize a la caja (Height/Width),
        // algunos glyphs (p.ej. trash) pueden recortarse por métricas de la fuente.
        // Tratamos HeightRequest/WidthRequest como tamaño de caja y reducimos un poco FontSize.
        var requestedSize = VirtualView.HeightRequest;
        if (requestedSize <= 0 || double.IsNaN(requestedSize) || double.IsInfinity(requestedSize))
        {
            requestedSize = VirtualView.WidthRequest;
        }

        if (requestedSize > 0 && !double.IsNaN(requestedSize) && !double.IsInfinity(requestedSize))
        {
            PlatformView.Width = requestedSize;
            PlatformView.Height = requestedSize;

            // Margen de seguridad para evitar clipping en escalados altos.
            var fontSize = Math.Max(1, requestedSize * 0.9);
            PlatformView.FontSize = fontSize;
        }
        else
        {
            PlatformView.ClearValue(FrameworkElement.WidthProperty);
            PlatformView.ClearValue(FrameworkElement.HeightProperty);
            PlatformView.FontSize = 12;
        }
    }

    private void UpdateGlyph()
    {
        if (PlatformView == null || VirtualView == null) return;

        var symbolName = VirtualView.SymbolName;
        
        if (string.IsNullOrWhiteSpace(symbolName))
        {
            PlatformView.Glyph = "\uE946"; // Info icon as fallback
            return;
        }

        // Buscar en el mapeo
        if (SfSymbolToFluentIconMap.TryGetValue(symbolName, out var glyph))
        {
            PlatformView.Glyph = glyph;
        }
        else
        {
            // Para símbolos no mapeados, intentar encontrar uno similar o usar placeholder
            System.Diagnostics.Debug.WriteLine($"[SymbolIcon] Unmapped SF Symbol: {symbolName}");
            PlatformView.Glyph = GetFallbackGlyph(symbolName);
        }
    }

    /// <summary>
    /// Intenta encontrar un glyph similar basándose en palabras clave del nombre del símbolo.
    /// </summary>
    private static string GetFallbackGlyph(string symbolName)
    {
        var lower = symbolName.ToLowerInvariant();
        
        // Intentar encontrar por palabras clave
        if (lower.Contains("play")) return "\uE768";       // Play
        if (lower.Contains("pause")) return "\uE769";      // Pause
        if (lower.Contains("stop")) return "\uE71A";       // Stop
        if (lower.Contains("forward")) return "\uE893";    // Forward
        if (lower.Contains("backward")) return "\uE892";   // Backward
        if (lower.Contains("check")) return "\uE73E";      // Checkmark
        if (lower.Contains("xmark") || lower.Contains("close") || lower.Contains("cancel")) return "\uE711"; // Close
        if (lower.Contains("trash") || lower.Contains("delete")) return "\uE74D"; // Delete
        if (lower.Contains("edit") || lower.Contains("pencil")) return "\uE70F"; // Edit
        if (lower.Contains("folder")) return "\uE8B7";     // Folder
        if (lower.Contains("file") || lower.Contains("doc")) return "\uE8A5"; // Document
        if (lower.Contains("video") || lower.Contains("film")) return "\uE714"; // Video
        if (lower.Contains("photo") || lower.Contains("image")) return "\uE91B"; // Photo
        if (lower.Contains("camera")) return "\uE722";     // Camera
        if (lower.Contains("person") || lower.Contains("user")) return "\uE77B"; // Person
        if (lower.Contains("calendar")) return "\uE787";   // Calendar
        if (lower.Contains("clock") || lower.Contains("time")) return "\uE823"; // Clock
        if (lower.Contains("heart")) return "\uEB51";      // Heart
        if (lower.Contains("star")) return "\uE734";       // Star
        if (lower.Contains("arrow")) return "\uE72A";      // Arrow
        if (lower.Contains("chevron")) return "\uE70D";    // Chevron
        if (lower.Contains("circle")) return "\uEA3A";     // Circle
        if (lower.Contains("rectangle") || lower.Contains("square")) return "\uE739"; // Rectangle
        if (lower.Contains("plus") || lower.Contains("add")) return "\uE710"; // Add
        if (lower.Contains("minus") || lower.Contains("remove")) return "\uE738"; // Remove
        if (lower.Contains("search") || lower.Contains("find")) return "\uE721"; // Search
        if (lower.Contains("settings") || lower.Contains("gear")) return "\uE713"; // Settings
        if (lower.Contains("home") || lower.Contains("house")) return "\uE80F"; // Home
        if (lower.Contains("info")) return "\uE946";       // Info
        if (lower.Contains("warning") || lower.Contains("exclamation")) return "\uE7BA"; // Warning
        if (lower.Contains("share")) return "\uE72D";      // Share
        if (lower.Contains("download")) return "\uE896";   // Download
        if (lower.Contains("upload")) return "\uE898";     // Upload
        if (lower.Contains("mic")) return "\uE720";        // Microphone
        if (lower.Contains("speaker") || lower.Contains("volume")) return "\uE767"; // Volume
        if (lower.Contains("mute")) return "\uE74F";       // Mute
        if (lower.Contains("tag") || lower.Contains("label")) return "\uE8EC"; // Tag
        if (lower.Contains("book") || lower.Contains("library")) return "\uE82D"; // Book
        if (lower.Contains("note")) return "\uE70B";       // Note
        if (lower.Contains("chart") || lower.Contains("graph")) return "\uE9D9"; // Chart
        if (lower.Contains("sun")) return "\uE706";        // Sun
        if (lower.Contains("moon")) return "\uE708";       // Moon
        if (lower.Contains("cloud")) return "\uE753";      // Cloud
        if (lower.Contains("rain")) return "\uE9C3";       // Rain
        
        // Fallback genérico: icono de información
        return "\uE946";  // Info circle
    }

    private void UpdateTint()
    {
        if (PlatformView == null || VirtualView == null) return;
        var mauiColor = VirtualView.TintColor;
        var winColor = Windows.UI.Color.FromArgb(
            (byte)(mauiColor.Alpha * 255),
            (byte)(mauiColor.Red * 255),
            (byte)(mauiColor.Green * 255),
            (byte)(mauiColor.Blue * 255));
        PlatformView.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(winColor);
    }
}

#else
namespace CrownRFEP_Reader.Handlers;

// Fallback handler for other targets (Android/Tizen) so the project compiles.
public class SymbolIconHandler : ViewHandler<SymbolIcon, object>
{
    public static readonly IPropertyMapper<SymbolIcon, SymbolIconHandler> Mapper =
        new PropertyMapper<SymbolIcon, SymbolIconHandler>(ViewMapper);

    public SymbolIconHandler() : base(Mapper)
    {
    }

    protected override object CreatePlatformView() => new();
}
#endif
