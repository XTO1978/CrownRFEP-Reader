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

		var deleteCommand = UIKeyCommand.Create(
			new NSString("\u007F"),
			0,
			new Selector("handleDeleteKeyPress"));
		deleteCommand.Title = "Delete";

		var backspaceCommand = UIKeyCommand.Create(
			new NSString("\b"),
			0,
			new Selector("handleBackspaceKeyPress"));
		backspaceCommand.Title = "Backspace";
		
		var playPauseMenu = UIMenu.Create(
			"",  // Título vacío para que sea invisible
			null,
			UIMenuIdentifier.None,
			UIMenuOptions.DisplayInline,
			new UIMenuElement[] { playPauseCommand, deleteCommand, backspaceCommand });
		
		// Insertar después del menú View
		builder.InsertSiblingMenuBefore(playPauseMenu, UIMenuIdentifier.View.GetConstant()!);
	}

	[Export("handleSpaceKeyPress")]
	public void HandleSpaceKeyPress()
	{
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnSpaceBarPressed();
	}

	[Export("handleDeleteKeyPress")]
	public void HandleDeleteKeyPress()
	{
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnDeletePressed();
	}

	[Export("handleBackspaceKeyPress")]
	public void HandleBackspaceKeyPress()
	{
		CrownRFEP_Reader.Platforms.MacCatalyst.KeyPressHandler.OnBackspacePressed();
	}
}
