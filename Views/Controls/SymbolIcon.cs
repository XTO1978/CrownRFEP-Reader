using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CrownRFEP_Reader.Views.Controls;

public class SymbolIcon : View
{
    public static readonly BindableProperty SymbolNameProperty = BindableProperty.Create(
        nameof(SymbolName),
        typeof(string),
        typeof(SymbolIcon),
        defaultValue: "calendar",
        propertyChanged: (bindable, _, _) => ((SymbolIcon)bindable).InvalidateMeasure());

    public static readonly BindableProperty TintColorProperty = BindableProperty.Create(
        nameof(TintColor),
        typeof(Color),
        typeof(SymbolIcon),
        defaultValue: Colors.White);

    /// <summary>
    /// SF Symbol name on Apple (e.g. "calendar", "location", "video").
    /// </summary>
    public string SymbolName
    {
        get => (string)GetValue(SymbolNameProperty);
        set => SetValue(SymbolNameProperty, value);
    }

    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }
}
