using CrownRFEP_Reader.Helpers;
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
		// Inicializar el helper de escalado de UI
		if (activationState?.Context?.Services != null)
		{
			var scalingService = activationState.Context.Services.GetService<IUIScalingService>();
			if (scalingService != null)
			{
				UIScaleHelper.Instance.Initialize(scalingService);
			}

			// Purgado best-effort de papelera (30 días)
			var trashService = activationState.Context.Services.GetService<ITrashService>();
			if (trashService != null)
			{
				_ = Task.Run(async () =>
				{
					try { await trashService.PurgeExpiredAsync(); } catch { }
				});
			}
		}

#if MACCATALYST
		// Solicitar permiso de micrófono al inicio para que aparezca en Preferencias del Sistema
		_ = Task.Run(async () =>
		{
			try
			{
				await RequestMicrophonePermissionAsync();
			}
			catch (Exception ex)
			{
				AppLog.Error("App", "Error solicitando permiso de micrófono", ex);
			}
		});
#endif

		return new Window(new AppShell());
	}

#if MACCATALYST
	private static async Task RequestMicrophonePermissionAsync()
	{
		AppLog.Info("App", "Solicitando permiso de micrófono al sistema (AVCaptureDevice)...");
		
		try
		{
			// Usar AVCaptureDevice que funciona mejor en MacCatalyst
			var authStatus = AVFoundation.AVCaptureDevice.GetAuthorizationStatus(AVFoundation.AVAuthorizationMediaType.Audio);
			AppLog.Info("App", $"Estado actual del permiso de micrófono (AVCaptureDevice): {authStatus}");
			
			if (authStatus == AVFoundation.AVAuthorizationStatus.NotDetermined)
			{
				AppLog.Info("App", "Permiso no determinado, solicitando con AVCaptureDevice...");
				
				var granted = await AVFoundation.AVCaptureDevice.RequestAccessForMediaTypeAsync(AVFoundation.AVAuthorizationMediaType.Audio);
				AppLog.Info("App", $"Resultado de solicitud de permiso (AVCaptureDevice): {(granted ? "CONCEDIDO" : "DENEGADO")}");
			}
			else if (authStatus == AVFoundation.AVAuthorizationStatus.Denied)
			{
				AppLog.Warn("App", "El permiso de micrófono está DENEGADO. El usuario debe habilitarlo en Preferencias del Sistema.");
			}
			else if (authStatus == AVFoundation.AVAuthorizationStatus.Authorized)
			{
				AppLog.Info("App", "El permiso de micrófono ya está concedido.");
			}
			else if (authStatus == AVFoundation.AVAuthorizationStatus.Restricted)
			{
				AppLog.Warn("App", "El permiso de micrófono está RESTRINGIDO por políticas del sistema.");
			}
		}
		catch (Exception ex)
		{
			AppLog.Error("App", "Error en RequestMicrophonePermissionAsync", ex);
		}
	}
#endif
}