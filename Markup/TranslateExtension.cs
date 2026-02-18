using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Markup;

/// <summary>
/// Markup extension para traducir cadenas en XAML.
/// Uso: Text="{markup:Translate Common_Delete}"
/// </summary>
[ContentProperty(nameof(Key))]
public class TranslateExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider)
    {
        return LocalizationService.Instance.GetString(Key);
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
    {
        return ProvideValue(serviceProvider);
    }
}
