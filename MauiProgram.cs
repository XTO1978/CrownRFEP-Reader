using CommunityToolkit.Maui;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.ViewModels;
using CrownRFEP_Reader.Views;
using CrownRFEP_Reader.Views.Controls;
using CrownRFEP_Reader.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Plugin.Maui.Audio;
#if MACCATALYST || IOS
using AVFoundation;
using Foundation;
#endif
#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
using UIKit;
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

#if MACCATALYST || IOS
				handlers.AddHandler(typeof(ReplayKitCameraPreview), typeof(ReplayKitCameraPreviewHandler));
#endif

#if IOS
				handlers.AddHandler(typeof(CameraPreview), typeof(CameraPreviewHandler));
#endif

#if WINDOWS
				handlers.AddHandler(typeof(WebcamPreview), typeof(WebcamPreviewHandler));
#endif
				
#if MACCATALYST || IOS || WINDOWS
				handlers.AddHandler(typeof(PrecisionVideoPlayer), typeof(PrecisionVideoPlayerHandler));
#endif

#if MACCATALYST
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

				// Desactivar highlight hover y selección nativo en CollectionView para MacCatalyst
				Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping("DisableNativeSelectionHighlight", (handler, view) =>
				{
					if (handler.PlatformView is UICollectionView collectionView)
					{
						// Configurar CollectionView para no mostrar highlight nativo
						collectionView.BackgroundColor = UIColor.Clear;
						
						// Limpiar backgrounds de celdas existentes
						CollectionViewSelectionFix.ClearCellBackgrounds(collectionView);
					}
				});

				// Handler para Border: asegurar fondo claro por defecto
				BorderHandler.Mapper.AppendToMapping("ClearBackground", (handler, view) =>
				{
					// No hacer nada aquí - el background se maneja en XAML
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
		builder.Services.AddSingleton<ITrashService, TrashService>();
		builder.Services.AddSingleton<UserProfileNotifier>();
		builder.Services.AddSingleton<VideoExportNotifier>();
		builder.Services.AddSingleton<VideoLessonNotifier>();
		builder.Services.AddSingleton<ImportProgressService>();
		builder.Services.AddSingleton<StatusBarService>();
		builder.Services.AddSingleton<CrownFileService>();
		builder.Services.AddSingleton<StatisticsService>();
		builder.Services.AddSingleton<ThumbnailService>();
		builder.Services.AddSingleton<ITableExportService, TableExportService>();
		
		// Plugin.Maui.Audio para grabación de micrófono
		builder.Services.AddSingleton(AudioManager.Current);

#if MACCATALYST
		// Usar el recorder original - V2 tiene incompatibilidades de API
		builder.Services.AddSingleton<IVideoLessonRecorder, ReplayKitVideoLessonRecorder>();
#elif IOS
		builder.Services.AddSingleton<IVideoLessonRecorder, CrownRFEP_Reader.Platforms.iOS.IOSVideoLessonRecorder>();
#elif WINDOWS
		builder.Services.AddSingleton<IVideoLessonRecorder, CrownRFEP_Reader.Platforms.Windows.WindowsVideoLessonRecorder>();
#else
		builder.Services.AddSingleton<IVideoLessonRecorder, NullVideoLessonRecorder>();
#endif

		// HealthKit - usar implementación nativa en Apple, stub en otras plataformas
#if MACCATALYST || IOS
		builder.Services.AddSingleton<IHealthKitService, AppleHealthKitService>();
#else
		builder.Services.AddSingleton<IHealthKitService, StubHealthKitService>();
#endif

		// Servicio de cámara para grabación de sesiones
#if IOS
		builder.Services.AddTransient<ICameraRecordingService, CrownRFEP_Reader.Platforms.iOS.IOSCameraRecordingService>();
#else
		builder.Services.AddTransient<ICameraRecordingService, NullCameraRecordingService>();
#endif

		// Servicio de composición de video
#if MACCATALYST
		builder.Services.AddSingleton<IVideoCompositionService, MacVideoCompositionService>();
#elif WINDOWS
		builder.Services.AddSingleton<IVideoCompositionService, CrownRFEP_Reader.Platforms.Windows.WindowsVideoCompositionService>();
#endif

		// Servicio de escalado de UI (adapta la UI a diferentes tamaños/densidades de pantalla)
#if WINDOWS
		builder.Services.AddSingleton<IUIScalingService, CrownRFEP_Reader.Platforms.Windows.WindowsUIScalingService>();
#else
		builder.Services.AddSingleton<IUIScalingService, DefaultUIScalingService>();
#endif

		// ViewModels
		builder.Services.AddSingleton<DashboardViewModel>();
		builder.Services.AddSingleton<ImportViewModel>();
		builder.Services.AddSingleton<SessionsViewModel>();
		builder.Services.AddSingleton<TrashViewModel>();
		builder.Services.AddTransient<SessionDetailViewModel>();
		builder.Services.AddSingleton<AthletesViewModel>();
		builder.Services.AddTransient<AthleteDetailViewModel>();
		builder.Services.AddTransient<VideoPlayerViewModel>();
		builder.Services.AddTransient<ParallelPlayerViewModel>();
		builder.Services.AddTransient<QuadPlayerViewModel>();
		builder.Services.AddTransient<SinglePlayerViewModel>();
		builder.Services.AddSingleton<StatisticsViewModel>();
		builder.Services.AddSingleton<UserProfileViewModel>();
		builder.Services.AddSingleton<VideoLessonsViewModel>();
		builder.Services.AddSingleton<DatabaseManagementViewModel>();
		builder.Services.AddTransient<CameraViewModel>();

		// Páginas
		builder.Services.AddSingleton<DashboardPage>();
		builder.Services.AddSingleton<ImportPage>();
		builder.Services.AddSingleton<SessionsPage>();
		builder.Services.AddSingleton<TrashPage>();
		builder.Services.AddTransient<SessionDetailPage>();
		builder.Services.AddSingleton<AthletesPage>();
		builder.Services.AddTransient<AthleteDetailPage>();
		builder.Services.AddSingleton<DatabaseManagementPage>();
		builder.Services.AddTransient<VideoPlayerPage>();
		builder.Services.AddTransient<ParallelPlayerPage>();
		builder.Services.AddTransient<QuadPlayerPage>();
		builder.Services.AddTransient<SinglePlayerPage>();
		builder.Services.AddSingleton<StatisticsPage>();
		builder.Services.AddSingleton<UserProfilePage>();
		builder.Services.AddSingleton<VideoLessonsPage>();
		builder.Services.AddTransient<CameraPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

#if MACCATALYST || IOS
		TryConfigureAppleAudioSessionForPlayback();
#endif
		
		// Conectar StatusBarService al DatabaseService para logging
		var databaseService = app.Services.GetService<DatabaseService>();
		var statusBarService = app.Services.GetService<StatusBarService>();
		if (databaseService != null && statusBarService != null)
		{
			databaseService.SetStatusBarService(statusBarService);
		}

		return app;
	}

#if MACCATALYST || IOS
	private static void TryConfigureAppleAudioSessionForPlayback()
	{
		try
		{
			var session = AVAudioSession.SharedInstance();
			NSError? error;

			// Playback: asegura salida de audio para AVPlayer/MediaElement.
			// MixWithOthers: evita cortar audio del sistema si no es necesario.
			session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.MixWithOthers, out error);
			session.SetActive(true, out error);
		}
		catch
		{
			// Best-effort: no bloquear arranque por configuración de audio.
		}
	}
#endif
}
