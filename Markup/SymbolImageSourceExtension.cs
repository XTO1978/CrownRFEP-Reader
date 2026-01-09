using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Maui.Controls.Xaml;

namespace CrownRFEP_Reader.Markup;

/// <summary>
/// Markup extension that provides an ImageSource from an SF Symbol name.
/// On iOS/MacCatalyst, it uses SF Symbols; on Windows, it maps to Segoe Fluent Icons.
/// </summary>
[ContentProperty(nameof(SymbolName))]
public class SymbolImageSourceExtension : IMarkupExtension<ImageSource?>
{
    /// <summary>
    /// The SF Symbol name (e.g., "pencil", "trash", "gearshape").
    /// </summary>
    public string? SymbolName { get; set; }

    /// <summary>
    /// The color for the icon. Defaults to White.
    /// </summary>
    public Color? Color { get; set; }

    /// <summary>
    /// The size of the icon. Defaults to 16.
    /// </summary>
    public double Size { get; set; } = 16;

    // Mapping from SF Symbol names to Segoe Fluent Icons glyphs
    private static readonly Dictionary<string, string> WindowsGlyphMap = new()
    {
        // Common icons
        ["pencil"] = "\uE70F",           // Edit
        ["trash"] = "\uE74D",            // Delete
        ["gearshape"] = "\uE713",        // Settings
        ["gear"] = "\uE713",             // Settings
        ["plus"] = "\uE710",             // Add
        ["minus"] = "\uE738",            // Remove
        ["checkmark"] = "\uE73E",        // Checkmark
        ["xmark"] = "\uE711",            // Close
        ["arrow.clockwise"] = "\uE72C",  // Refresh
        ["link"] = "\uE71B",             // Link
        ["folder"] = "\uE8B7",           // Folder
        ["folder.badge.plus"] = "\uE8F4", // Folder with plus
        ["doc"] = "\uE8A5",              // Document
        ["photo"] = "\uEB9F",            // Photo
        ["video"] = "\uE714",            // Video
        ["play"] = "\uE768",             // Play
        ["pause"] = "\uE769",            // Pause
        ["stop"] = "\uE71A",             // Stop
        ["square.and.arrow.up"] = "\uE72D", // Share/Export
        ["square.and.arrow.down"] = "\uE896", // Import/Download
        ["ellipsis"] = "\uE712",         // More
        ["info.circle"] = "\uE946",      // Info
        ["exclamationmark.triangle"] = "\uE7BA", // Warning
        ["star"] = "\uE734",             // Star outline
        ["star.fill"] = "\uE735",        // Star filled
        ["heart"] = "\uEB51",            // Heart outline
        ["heart.fill"] = "\uEB52",       // Heart filled
        ["tag"] = "\uE8EC",              // Tag
        ["clock"] = "\uE823",            // Clock/History
        ["calendar"] = "\uE787",         // Calendar
        ["person"] = "\uE77B",           // Person
        ["person.2"] = "\uE716",         // People
        ["magnifyingglass"] = "\uE721",  // Search
        ["slider.horizontal.3"] = "\uE713", // Sliders/Settings
        ["arrow.up"] = "\uE74A",         // Arrow up
        ["arrow.down"] = "\uE74B",       // Arrow down
        ["arrow.left"] = "\uE72B",       // Arrow left
        ["arrow.right"] = "\uE72A",      // Arrow right
        ["chevron.up"] = "\uE70E",       // Chevron up
        ["chevron.down"] = "\uE70D",     // Chevron down
        ["chevron.left"] = "\uE76B",     // Chevron left
        ["chevron.right"] = "\uE76C",    // Chevron right
        ["books.vertical"] = "\uE8F1",   // Library
        ["book"] = "\uE82D",             // Book
        ["eye"] = "\uE7B3",              // Eye/View
        ["eye.slash"] = "\uE7B4",        // Eye off
        ["lock"] = "\uE72E",             // Lock
        ["lock.open"] = "\uE785",        // Unlock
        ["paperclip"] = "\uE723",        // Attach
        ["bolt"] = "\uE945",             // Bolt/Flash
        ["wand.and.stars"] = "\uE8D4",   // Magic wand
    };

    public ImageSource? ProvideValue(IServiceProvider serviceProvider)
    {
        try
        {
            if (string.IsNullOrEmpty(SymbolName))
                return null;

            var color = Color ?? Colors.White;

#if IOS || MACCATALYST
            // IMPORTANTE: no usar UIKit para reescalar aquí. En MacCatalyst, si esto lanza
            // durante el click secundario (UIContextMenuInteraction), el proceso aborta.
            return CreateAppleImageSource(SymbolName);
#elif WINDOWS
            return CreateWindowsImageSource(SymbolName, color, Size);
#else
            return null;
#endif
        }
        catch
        {
            return null;
        }
    }

    object? IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
    {
        return ProvideValue(serviceProvider);
    }

#if IOS || MACCATALYST
    private static ImageSource? CreateAppleImageSource(string symbolName)
    {
        // En iOS/MacCatalyst, para menús contextuales es crucial usar assets pequeños
        // (≈16px) porque el tamaño intrínseco del icono afecta directamente a la altura
        // del item del menú.
        var imageBaseNameMap = new Dictionary<string, string>
        {
            // Usamos versiones pequeñas para no inflar el menú contextual
            ["pencil"] = "editar16",
            ["trash"] = "basura16",
            ["gearshape"] = "personalizar16",
            ["gear"] = "personalizar16",
            ["wrench.and.screwdriver"] = "personalizar16",
        };

        if (!imageBaseNameMap.TryGetValue(symbolName, out var imageBaseName))
            return null;

        return ImageSource.FromFile(imageBaseName);
    }
#endif

#if WINDOWS
    private static ImageSource CreateWindowsImageSource(string symbolName, Color color, double size)
    {
        var glyph = WindowsGlyphMap.TryGetValue(symbolName, out var g) ? g : "\uE712"; // Fallback to ellipsis
        return new FontImageSource
        {
            FontFamily = "Segoe Fluent Icons",
            Glyph = glyph,
            Color = color,
            Size = size
        };
    }
#endif
}
