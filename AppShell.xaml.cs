using CrownRFEP_Reader.Views;

namespace CrownRFEP_Reader;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Registrar rutas para navegación
		Routing.RegisterRoute(nameof(SessionDetailPage), typeof(SessionDetailPage));
		Routing.RegisterRoute(nameof(AthleteDetailPage), typeof(AthleteDetailPage));
		Routing.RegisterRoute(nameof(VideoPlayerPage), typeof(VideoPlayerPage));
		Routing.RegisterRoute(nameof(ParallelPlayerPage), typeof(ParallelPlayerPage));
		Routing.RegisterRoute(nameof(SinglePlayerPage), typeof(SinglePlayerPage));
		Routing.RegisterRoute(nameof(SessionsPage), typeof(SessionsPage));
		Routing.RegisterRoute(nameof(AthletesPage), typeof(AthletesPage));
	}
}
