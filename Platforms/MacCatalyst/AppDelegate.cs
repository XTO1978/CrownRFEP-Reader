using Foundation;
using UIKit;
using ObjCRuntime;

namespace CrownRFEP_Reader;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override void BuildMenu(IUIMenuBuilder builder)
	{
		base.BuildMenu(builder);
		
		// Añadir comando de teclado para barra espaciadora (Play/Pause)
		var playPauseCommand = UIKeyCommand.Create(
			new NSString(" "),
			0,
			new Selector("handleSpaceKeyPress"));
		playPauseCommand.Title = "Play/Pause";
		
		var playPauseMenu = UIMenu.Create(
			"",  // Título vacío para que sea invisible
			null,
			UIMenuIdentifier.None,
			UIMenuOptions.DisplayInline,
			new UIMenuElement[] { playPauseCommand });
		
		// Insertar después del menú View
		builder.InsertSiblingMenuBefore(playPauseMenu, UIMenuIdentifier.View.GetConstant()!);
	}

	[Export("handleSpaceKeyPress")]
	public void HandleSpaceKeyPress()
	{
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnSpaceBarPressed();
	}
}
