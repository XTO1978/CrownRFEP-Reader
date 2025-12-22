using Foundation;
using UIKit;
using ObjCRuntime;

namespace CrownRFEP_Reader.Platforms.MacCatalyst;

/// <summary>
/// Gestiona eventos de teclado para MacCatalyst
/// </summary>
public static class KeyPressHandler
{
    /// <summary>
    /// Evento que se dispara cuando se presiona la barra espaciadora
    /// </summary>
    public static event EventHandler? SpaceBarPressed;

    /// <summary>
    /// Evento que se dispara cuando se presiona Suprimir (Delete)
    /// </summary>
    public static event EventHandler? DeletePressed;

    /// <summary>
    /// Evento que se dispara cuando se presiona Retroceso (Backspace)
    /// </summary>
    public static event EventHandler? BackspacePressed;

    /// <summary>
    /// Invocar el evento de barra espaciadora
    /// </summary>
    public static void OnSpaceBarPressed()
    {
        SpaceBarPressed?.Invoke(null, EventArgs.Empty);
    }

    public static void OnDeletePressed()
    {
        DeletePressed?.Invoke(null, EventArgs.Empty);
    }

    public static void OnBackspacePressed()
    {
        BackspacePressed?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>
/// UIWindow personalizado que captura eventos de teclado
/// </summary>
public class KeyboardAwareWindow : UIWindow
{
    public KeyboardAwareWindow(UIWindowScene windowScene) : base(windowScene)
    {
    }

    public KeyboardAwareWindow() : base()
    {
    }

    public override UIKeyCommand[] KeyCommands
    {
        get
        {
            var playPauseCommand = UIKeyCommand.Create(
                new NSString(" "),
                0,
                new Selector("handleSpaceKey"));
            playPauseCommand.Title = "Play/Pause";

            var deleteCommand = UIKeyCommand.Create(
                new NSString("\u007F"),
                0,
                new Selector("handleDeleteKey"));
            deleteCommand.Title = "Delete";

            var backspaceCommand = UIKeyCommand.Create(
                new NSString("\b"),
                0,
                new Selector("handleBackspaceKey"));
            backspaceCommand.Title = "Backspace";

            return new[] { playPauseCommand, deleteCommand, backspaceCommand };
        }
    }

    [Export("handleSpaceKey")]
    public void HandleSpaceKey()
    {
        KeyPressHandler.OnSpaceBarPressed();
    }

    [Export("handleDeleteKey")]
    public void HandleDeleteKey()
    {
        KeyPressHandler.OnDeletePressed();
    }

    [Export("handleBackspaceKey")]
    public void HandleBackspaceKey()
    {
        KeyPressHandler.OnBackspacePressed();
    }
}
