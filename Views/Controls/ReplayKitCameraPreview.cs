namespace CrownRFEP_Reader.Views.Controls;

// Control MAUI para mostrar el preview de cámara que expone ReplayKit (MacCatalyst).
public sealed class ReplayKitCameraPreview : View
{
	public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
		nameof(IsActive),
		typeof(bool),
		typeof(ReplayKitCameraPreview),
		false);

	public bool IsActive
	{
		get => (bool)GetValue(IsActiveProperty);
		set => SetValue(IsActiveProperty, value);
	}
}
