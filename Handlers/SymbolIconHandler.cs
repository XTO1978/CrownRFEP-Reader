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
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CrownRFEP_Reader.Handlers;

public class SymbolIconHandler : ViewHandler<SymbolIcon, FontIcon>
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

    protected override FontIcon CreatePlatformView()
    {
        var icon = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons")
        };

        return icon;
    }

    protected override void ConnectHandler(FontIcon platformView)
    {
        base.ConnectHandler(platformView);
        UpdateGlyph();
        UpdateTint();
    }

    public static void MapSymbolName(SymbolIconHandler handler, SymbolIcon view) => handler.UpdateGlyph();
    public static void MapTintColor(SymbolIconHandler handler, SymbolIcon view) => handler.UpdateTint();

    private void UpdateGlyph()
    {
        if (PlatformView == null || VirtualView == null) return;

        // Minimal mapping: feel free to extend as needed.
        PlatformView.Glyph = VirtualView.SymbolName switch
        {
            "calendar" => "\uE787",
            "location" => "\uE707",
            "video" => "\uE714",
            _ => "\uE10F" // placeholder
        };
    }

    private void UpdateTint()
    {
        if (PlatformView == null || VirtualView == null) return;
        var color = VirtualView.TintColor.ToPlatform();
        PlatformView.Foreground = new SolidColorBrush(color);
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
