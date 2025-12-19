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
    /// Invocar el evento de barra espaciadora
    /// </summary>
    public static void OnSpaceBarPressed()
    {
        SpaceBarPressed?.Invoke(null, EventArgs.Empty);
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
            var command = UIKeyCommand.Create(
                new NSString(" "), 
                0, 
                new Selector("handleSpaceKey"));
            command.Title = "Play/Pause";
            return new[] { command };
        }
    }

    [Export("handleSpaceKey")]
    public void HandleSpaceKey()
    {
        KeyPressHandler.OnSpaceBarPressed();
    }
}
