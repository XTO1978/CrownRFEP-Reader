using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CrownRFEP_Reader.Platforms.Windows;

/// <summary>
/// Gestiona eventos de teclado globales para Windows (WinUI)
/// </summary>
public static class KeyPressHandler
{
    private static Microsoft.UI.Xaml.Window? _mainWindow;
    private static bool _isAttached;

    /// <summary>
    /// Evento que se dispara cuando se presiona la barra espaciadora
    /// </summary>
    public static event EventHandler? SpaceBarPressed;

    /// <summary>
    /// Evento que se dispara cuando se presiona la flecha izquierda
    /// </summary>
    public static event EventHandler? ArrowLeftPressed;

    /// <summary>
    /// Evento que se dispara cuando se presiona la flecha derecha
    /// </summary>
    public static event EventHandler? ArrowRightPressed;

    /// <summary>
    /// Evento que se dispara cuando se presiona Delete
    /// </summary>
    public static event EventHandler? DeletePressed;

    /// <summary>
    /// Evento que se dispara cuando se presiona Backspace
    /// </summary>
    public static event EventHandler? BackspacePressed;

    /// <summary>
    /// Intenta adjuntar el handler a la ventana principal.
    /// Debe llamarse desde cada página que necesite capturar teclas.
    /// </summary>
    public static void EnsureAttached()
    {
        if (_isAttached)
            return;

        try
        {
            // Obtener la ventana principal de MAUI
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows?.FirstOrDefault();
            if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window)
            {
                _mainWindow = window;
                
                // Usar el evento de contenido para capturar teclas
                if (window.Content is UIElement rootElement)
                {
                    rootElement.PreviewKeyDown -= OnPreviewKeyDown;
                    rootElement.PreviewKeyDown += OnPreviewKeyDown;
                    _isAttached = true;
                    System.Diagnostics.Debug.WriteLine("KeyPressHandler: Attached to window content");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"KeyPressHandler: Failed to attach - {ex.Message}");
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // No procesar si ya fue manejado
        if (e.Handled)
            return;

        switch (e.Key)
        {
            case VirtualKey.Space:
                // Verificar que hay suscriptores antes de marcar como handled
                if (SpaceBarPressed != null)
                {
                    OnSpaceBarPressed();
                    e.Handled = true;
                }
                break;

            case VirtualKey.Left:
                if (ArrowLeftPressed != null)
                {
                    OnArrowLeftPressed();
                    e.Handled = true;
                }
                break;

            case VirtualKey.Right:
                if (ArrowRightPressed != null)
                {
                    OnArrowRightPressed();
                    e.Handled = true;
                }
                break;

            case VirtualKey.Delete:
                if (DeletePressed != null)
                {
                    OnDeletePressed();
                    e.Handled = true;
                }
                break;

            case VirtualKey.Back:
                if (BackspacePressed != null)
                {
                    OnBackspacePressed();
                    e.Handled = true;
                }
                break;
        }
    }

    public static void OnSpaceBarPressed()
    {
        SpaceBarPressed?.Invoke(null, EventArgs.Empty);
    }

    public static void OnArrowLeftPressed()
    {
        ArrowLeftPressed?.Invoke(null, EventArgs.Empty);
    }

    public static void OnArrowRightPressed()
    {
        ArrowRightPressed?.Invoke(null, EventArgs.Empty);
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
