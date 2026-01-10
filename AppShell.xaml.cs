using CrownRFEP_Reader.Views;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader;

public partial class AppShell : Shell
{
	private int _cleanupInProgress;

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
		Routing.RegisterRoute(nameof(TrashPage), typeof(TrashPage));
		Routing.RegisterRoute(nameof(ImportPage), typeof(ImportPage));
		Routing.RegisterRoute(nameof(CameraPage), typeof(CameraPage));
	}

	private async void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
	{
		ShellNavigatingDeferral? deferral = null;
		if (e.CanCancel)
			deferral = e.GetDeferral();

		try
		{
			AppLog.Info("Shell", $"Navigating: Source={e.Source}, Current='{e.Current?.Location}', Target='{e.Target?.Location}', CanCancel={e.CanCancel}");

			// Evita re-entradas si se disparan varias navegaciones seguidas
			if (Interlocked.Exchange(ref _cleanupInProgress, 1) == 1)
				return;

			// El crash reportado ocurre típicamente al cambiar de ShellItem/Section/Content.
			if (e.Source is ShellNavigationSource.ShellItemChanged or ShellNavigationSource.ShellSectionChanged or ShellNavigationSource.ShellContentChanged)
			{
				var current = GetLeafPage(CurrentPage);
				if (current is IShellNavigatingCleanup cleanup)
				{
					await cleanup.PrepareForShellNavigationAsync();
					// Deja un tick para que iOS procese el teardown del player antes del cambio de VC
#if IOS
					await Task.Delay(16);
#endif
				}
			}
		}
		catch (Exception ex)
		{
			AppLog.Error("Shell", "Error logging Navigating", ex);
		}
		finally
		{
			Interlocked.Exchange(ref _cleanupInProgress, 0);
			deferral?.Complete();
		}
	}

	private static Page? GetLeafPage(Page? page)
	{
		while (true)
		{
			switch (page)
			{
				case NavigationPage nav:
					page = nav.CurrentPage;
					continue;
				case TabbedPage tabs:
					page = tabs.CurrentPage;
					continue;
				case FlyoutPage flyout:
					page = flyout.Detail;
					continue;
				default:
					return page;
			}
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
