namespace CrownRFEP_Reader.Views.Controls;

public class AccentThumbSlider : Slider
{
    public static readonly BindableProperty ThumbSizeProperty = BindableProperty.Create(
        nameof(ThumbSize),
        typeof(double),
        typeof(AccentThumbSlider),
        10d);

    public double ThumbSize
    {
        get => (double)GetValue(ThumbSizeProperty);
        set => SetValue(ThumbSizeProperty, value);
    }
}