using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

#if DEBUG
		// FirstChanceException puede ser ruidoso: filtramos por namespaces típicos del problema.
		AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
		{
			try
			{
				var typeName = e.Exception.GetType().FullName ?? string.Empty;
				var msg = e.Exception.Message ?? string.Empty;

				// Filtrado pragmático para evitar spam masivo.
				if (
					typeName.Contains("AVFoundation", StringComparison.OrdinalIgnoreCase) ||
					typeName.Contains("CoreMedia", StringComparison.OrdinalIgnoreCase) ||
					typeName.Contains("CoreFoundation", StringComparison.OrdinalIgnoreCase) ||
					typeName.Contains("ObjCRuntime", StringComparison.OrdinalIgnoreCase) ||
					typeName.Contains("Microsoft.Maui", StringComparison.OrdinalIgnoreCase) ||
					msg.Contains("AV", StringComparison.OrdinalIgnoreCase) ||
					msg.Contains("disposed", StringComparison.OrdinalIgnoreCase) ||
					msg.Contains("ObjectDisposed", StringComparison.OrdinalIgnoreCase))
				{
					AppLog.Warn("App", $"FirstChanceException: {typeName}: {msg}");
				}
			}
			catch
			{
				// no-op
			}
		};
#endif

		// Logs de excepciones globales: ayudan a ver el crash en consola.
		AppDomain.CurrentDomain.UnhandledException += (s, e) =>
		{
			try
			{
				AppLog.Error("App", $"UnhandledException (IsTerminating={e.IsTerminating})", e.ExceptionObject as Exception);
			}
			catch
			{
				// no-op
			}
		};

		TaskScheduler.UnobservedTaskException += (s, e) =>
		{
			try
			{
				AppLog.Error("App", "UnobservedTaskException", e.Exception);
				e.SetObserved();
			}
			catch
			{
				// no-op
			}
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}