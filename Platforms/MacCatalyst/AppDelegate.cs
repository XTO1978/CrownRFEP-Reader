using Foundation;
using UIKit;
using ObjCRuntime;

namespace CrownRFEP_Reader;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
	{
		var result = base.FinishedLaunching(application, launchOptions);
		ConfigureTitleBar();
		return result;
	}

	private void ConfigureTitleBar()
	{
		// Esperar a que la ventana esté disponible
		MainThread.BeginInvokeOnMainThread(async () =>
		{
			await Task.Delay(100);
			
			try
			{
				var scenes = UIApplication.SharedApplication.ConnectedScenes;
				foreach (var scene in scenes)
				{
					if (scene is UIWindowScene windowScene)
					{
						// Configurar la titlebar para modo oscuro
						var titlebar = windowScene.Titlebar;
						if (titlebar != null)
						{
							// Ocultar el título para un look más limpio
							titlebar.TitleVisibility = UITitlebarTitleVisibility.Hidden;
						}

						// Forzar modo oscuro en todas las ventanas
						foreach (var window in windowScene.Windows)
						{
							window.OverrideUserInterfaceStyle = UIUserInterfaceStyle.Dark;
						}
					}
				}
				
				System.Diagnostics.Debug.WriteLine("[AppDelegate] Titlebar configurada con modo oscuro");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[AppDelegate] Error configurando titlebar: {ex.Message}");
			}
		});
	}

	public override UIKeyCommand[] KeyCommands
	{
		get
		{
			var commands = new List<UIKeyCommand>();
			
			// Espacio para Play/Pause
			var spaceCommand = UIKeyCommand.Create(
				new NSString(" "), 0, new Selector("handleSpaceKey"));
			spaceCommand.Title = "Play/Pause";
			commands.Add(spaceCommand);
			
			// Flechas izquierda/derecha para frame by frame
			var leftCommand = UIKeyCommand.Create(
				UIKeyCommand.LeftArrow, 0, new Selector("handleLeftArrowKey"));
			leftCommand.Title = "Frame Backward";
			commands.Add(leftCommand);
			
			var rightCommand = UIKeyCommand.Create(
				UIKeyCommand.RightArrow, 0, new Selector("handleRightArrowKey"));
			rightCommand.Title = "Frame Forward";
			commands.Add(rightCommand);
			
			// Delete y Backspace
			var deleteCommand = UIKeyCommand.Create(
				new NSString("\u007F"), 0, new Selector("handleDeleteKey"));
			deleteCommand.Title = "Delete";
			commands.Add(deleteCommand);
			
			var backspaceCommand = UIKeyCommand.Create(
				new NSString("\b"), 0, new Selector("handleBackspaceKey"));
			backspaceCommand.Title = "Backspace";
			commands.Add(backspaceCommand);
			
			return commands.ToArray();
		}
	}

	public override bool CanBecomeFirstResponder => true;

	[Export("handleSpaceKey")]
	public void HandleSpaceKey()
	{
		System.Diagnostics.Debug.WriteLine(">>> AppDelegate: Space key pressed!");
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnSpaceBarPressed();
	}

	[Export("handleLeftArrowKey")]
	public void HandleLeftArrowKey()
	{
		System.Diagnostics.Debug.WriteLine(">>> AppDelegate: Left Arrow key pressed!");
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnArrowLeftPressed();
	}

	[Export("handleRightArrowKey")]
	public void HandleRightArrowKey()
	{
		System.Diagnostics.Debug.WriteLine(">>> AppDelegate: Right Arrow key pressed!");
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnArrowRightPressed();
	}

	[Export("handleDeleteKey")]
	public void HandleDeleteKey()
	{
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnDeletePressed();
	}

	[Export("handleBackspaceKey")]
	public void HandleBackspaceKey()
	{
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnBackspacePressed();
	}
}
