using CommunityToolkit.Maui;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.ViewModels;
using CrownRFEP_Reader.Views;
using CrownRFEP_Reader.Views.Controls;
using CrownRFEP_Reader.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#endif
#if MACCATALYST
using UIKit;
using Foundation;
#endif

namespace CrownRFEP_Reader;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseMauiCommunityToolkitMediaElement()
			.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler(typeof(SymbolIcon), typeof(SymbolIconHandler));

#if MACCATALYST
				handlers.AddHandler(typeof(ReplayKitCameraPreview), typeof(ReplayKitCameraPreviewHandler));
#endif
				
#if MACCATALYST || IOS
				handlers.AddHandler(typeof(PrecisionVideoPlayer), typeof(PrecisionVideoPlayerHandler));
				
				// Forzar texto blanco en DatePicker para MacCatalyst
				DatePickerHandler.Mapper.AppendToMapping("WhiteTextColor", (handler, view) =>
				{
					if (handler.PlatformView is UIDatePicker picker)
					{
						picker.TintColor = UIColor.White;
						// Forzar estilo compacto con texto visible
						picker.PreferredDatePickerStyle = UIDatePickerStyle.Compact;
						// Intentar establecer el color del texto mediante el trait
						picker.OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark;
					}
				});
#endif
			})
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Servicios
		builder.Services.AddSingleton<DatabaseService>();
		builder.Services.AddSingleton<UserProfileNotifier>();
		builder.Services.AddSingleton<StatusBarService>();
		builder.Services.AddSingleton<CrownFileService>();
		builder.Services.AddSingleton<StatisticsService>();
		builder.Services.AddSingleton<ThumbnailService>();

#if MACCATALYST || IOS
		builder.Services.AddSingleton<IVideoLessonRecorder, ReplayKitVideoLessonRecorder>();
#elif WINDOWS
		builder.Services.AddSingleton<IVideoLessonRecorder, CrownRFEP_Reader.Platforms.Windows.WindowsVideoLessonRecorder>();
#else
		builder.Services.AddSingleton<IVideoLessonRecorder, NullVideoLessonRecorder>();
#endif

		// HealthKit solo está disponible en iOS real (iPhone/iPad), no en macOS/MacCatalyst
#if IOS && !MACCATALYST
		builder.Services.AddSingleton<IHealthKitService, AppleHealthKitService>();
#else
		builder.Services.AddSingleton<IHealthKitService, StubHealthKitService>();
#endif

		// ViewModels
		builder.Services.AddSingleton<DashboardViewModel>();
		builder.Services.AddSingleton<ImportViewModel>();
		builder.Services.AddSingleton<SessionsViewModel>();
		builder.Services.AddTransient<SessionDetailViewModel>();
		builder.Services.AddSingleton<AthletesViewModel>();
		builder.Services.AddTransient<AthleteDetailViewModel>();
		builder.Services.AddTransient<VideoPlayerViewModel>();
		builder.Services.AddTransient<ParallelPlayerViewModel>();
		builder.Services.AddTransient<SinglePlayerViewModel>();
		builder.Services.AddSingleton<StatisticsViewModel>();
		builder.Services.AddSingleton<UserProfileViewModel>();
		builder.Services.AddSingleton<VideoLessonsViewModel>();

		// Páginas
		builder.Services.AddSingleton<DashboardPage>();
		builder.Services.AddSingleton<ImportPage>();
		builder.Services.AddSingleton<SessionsPage>();
		builder.Services.AddTransient<SessionDetailPage>();
		builder.Services.AddSingleton<AthletesPage>();
		builder.Services.AddTransient<AthleteDetailPage>();
		builder.Services.AddTransient<VideoPlayerPage>();
		builder.Services.AddTransient<ParallelPlayerPage>();
		builder.Services.AddTransient<SinglePlayerPage>();
		builder.Services.AddSingleton<StatisticsPage>();
		builder.Services.AddSingleton<UserProfilePage>();
		builder.Services.AddSingleton<VideoLessonsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();
		
		// Conectar StatusBarService al DatabaseService para logging
		var databaseService = app.Services.GetService<DatabaseService>();
		var statusBarService = app.Services.GetService<StatusBarService>();
		if (databaseService != null && statusBarService != null)
		{
			databaseService.SetStatusBarService(statusBarService);
		}

		return app;
	}
}
