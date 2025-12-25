using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CrownRFEP_Reader.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
		
		// Capturar excepciones no manejadas para debugging
		this.UnhandledException += (sender, e) =>
		{
			System.Diagnostics.Debug.WriteLine($"=== UNHANDLED EXCEPTION ===");
			System.Diagnostics.Debug.WriteLine($"Message: {e.Message}");
			System.Diagnostics.Debug.WriteLine($"Exception: {e.Exception}");
			System.Diagnostics.Debug.WriteLine($"StackTrace: {e.Exception?.StackTrace}");
			System.Diagnostics.Debug.WriteLine($"=== END EXCEPTION ===");
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

