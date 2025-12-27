using Microsoft.UI.Xaml;
using System.Runtime.ExceptionServices;
using CrownRFEP_Reader.Platforms.Windows;

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
		
		// Capturar excepciones no manejadas de WinUI/XAML
		this.UnhandledException += (sender, e) =>
		{
			System.Diagnostics.Debug.WriteLine($"=== UNHANDLED WINUI EXCEPTION ===");
			System.Diagnostics.Debug.WriteLine($"Message: {e.Message}");
			System.Diagnostics.Debug.WriteLine($"Exception: {e.Exception}");
			System.Diagnostics.Debug.WriteLine($"StackTrace: {e.Exception?.StackTrace}");
			System.Diagnostics.Debug.WriteLine($"InnerException: {e.Exception?.InnerException}");
			System.Diagnostics.Debug.WriteLine($"=== END EXCEPTION ===");
			
			// Marcar como manejada para evitar crash (solo si no es fatal)
			// NOTA: Esto puede ocultar errores reales, usar solo para debugging
			e.Handled = true;
		};

		// Capturar excepciones de tareas no observadas
		TaskScheduler.UnobservedTaskException += (sender, e) =>
		{
			System.Diagnostics.Debug.WriteLine($"=== UNOBSERVED TASK EXCEPTION ===");
			System.Diagnostics.Debug.WriteLine($"Exception: {e.Exception}");
			System.Diagnostics.Debug.WriteLine($"=== END EXCEPTION ===");
			e.SetObserved();
		};

		// Capturar excepciones de primer nivel del dominio
		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			System.Diagnostics.Debug.WriteLine($"=== APPDOMAIN UNHANDLED EXCEPTION ===");
			System.Diagnostics.Debug.WriteLine($"IsTerminating: {e.IsTerminating}");
			System.Diagnostics.Debug.WriteLine($"ExceptionObject: {e.ExceptionObject}");
			System.Diagnostics.Debug.WriteLine($"=== END EXCEPTION ===");
		};

		// Capturar excepciones de primer chance para logging
		AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
		{
			// Solo loggear excepciones relacionadas con navegación o media
			var exType = e.Exception?.GetType().Name ?? "";
			if (exType.Contains("COMException") || 
			    exType.Contains("InvalidOperation") || 
			    e.Exception?.Message?.Contains("navigation", StringComparison.OrdinalIgnoreCase) == true ||
			    e.Exception?.Message?.Contains("Frame", StringComparison.OrdinalIgnoreCase) == true)
			{
				System.Diagnostics.Debug.WriteLine($"[FirstChance] {exType}: {e.Exception?.Message}");
			}
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}