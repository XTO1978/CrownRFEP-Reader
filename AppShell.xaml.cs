using CrownRFEP_Reader.Views;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		Navigating += OnShellNavigating;
		Navigated += OnShellNavigated;

		// Registrar rutas para navegación
		Routing.RegisterRoute(nameof(SessionDetailPage), typeof(SessionDetailPage));
		Routing.RegisterRoute(nameof(AthleteDetailPage), typeof(AthleteDetailPage));
		Routing.RegisterRoute(nameof(VideoPlayerPage), typeof(VideoPlayerPage));
		Routing.RegisterRoute(nameof(ParallelPlayerPage), typeof(ParallelPlayerPage));
		Routing.RegisterRoute(nameof(SinglePlayerPage), typeof(SinglePlayerPage));
		Routing.RegisterRoute(nameof(SessionsPage), typeof(SessionsPage));
		Routing.RegisterRoute(nameof(AthletesPage), typeof(AthletesPage));
		Routing.RegisterRoute(nameof(VideoLessonsPage), typeof(VideoLessonsPage));
		Routing.RegisterRoute(nameof(ImportPage), typeof(ImportPage));
	}

	private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
	{
		try
		{
			AppLog.Info("Shell", $"Navigating: Source={e.Source}, Current='{e.Current?.Location}', Target='{e.Target?.Location}', CanCancel={e.CanCancel}");
		}
		catch (Exception ex)
		{
			AppLog.Error("Shell", "Error logging Navigating", ex);
		}
	}

	private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		try
		{
			AppLog.Info("Shell", $"Navigated: Source={e.Source}, Current='{e.Current?.Location}', Previous='{e.Previous?.Location}'");
		}
		catch (Exception ex)
		{
			AppLog.Error("Shell", "Error logging Navigated", ex);
		}
	}
}
