using Foundation;
using UIKit;
using ObjCRuntime;

namespace CrownRFEP_Reader;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

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
