#if IOS
using UIKit;
using UniformTypeIdentifiers;
using Microsoft.Maui.Handlers;
using CrownRFEP_Reader.Controls;
using Foundation;
using ObjCRuntime;

namespace CrownRFEP_Reader.Handlers;

/// <summary>
/// Handler para EmbeddedFileBrowser en iOS.
/// Embebe UIDocumentBrowserViewController directamente como vista.
/// </summary>
public class EmbeddedFileBrowserHandler : ViewHandler<EmbeddedFileBrowser, UIView>
{
    private UIView? _containerView;
    private UIDocumentBrowserViewController? _documentBrowser;
    private DocumentBrowserDelegate? _browserDelegate;

    public static IPropertyMapper<EmbeddedFileBrowser, EmbeddedFileBrowserHandler> Mapper =
        new PropertyMapper<EmbeddedFileBrowser, EmbeddedFileBrowserHandler>(ViewMapper)
        {
            [nameof(EmbeddedFileBrowser.AllowedFileTypes)] = MapAllowedFileTypes,
            [nameof(EmbeddedFileBrowser.AllowMultipleSelection)] = MapAllowMultipleSelection
        };

    public EmbeddedFileBrowserHandler() : base(Mapper) { }

    protected override UIView CreatePlatformView()
    {
        _containerView = new UIView
        {
            BackgroundColor = UIColor.FromRGB(0x1E, 0x1E, 0x1E),
            ClipsToBounds = true
        };

        // Crear el UIDocumentBrowserViewController
        CreateDocumentBrowser();

        return _containerView;
    }

    private void CreateDocumentBrowser()
    {
        if (_containerView == null || VirtualView == null) return;

        var allowedTypes = VirtualView.AllowedFileTypes
            .Select(t => UTType.CreateFromIdentifier(t))
            .Where(t => t != null)
            .Cast<UTType>()
            .ToArray();

        if (allowedTypes.Length == 0)
        {
            allowedTypes = new[] { UTTypes.Movie };
        }

        _documentBrowser = new UIDocumentBrowserViewController(allowedTypes)
        {
            AllowsDocumentCreation = false,
            AllowsPickingMultipleItems = VirtualView.AllowMultipleSelection,
            BrowserUserInterfaceStyle = UIDocumentBrowserUserInterfaceStyle.Dark
        };

        // Usar delegado para manejar la selección
        _browserDelegate = new DocumentBrowserDelegate(OnDocumentsPicked);
        _documentBrowser.Delegate = _browserDelegate;

        // Embeber el UIDocumentBrowserViewController como vista hija
        var parentViewController = GetCurrentViewController();
        if (parentViewController != null && _documentBrowser.View != null)
        {
            parentViewController.AddChildViewController(_documentBrowser);
            
            _documentBrowser.View.Frame = _containerView.Bounds;
            _documentBrowser.View.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
            _containerView.AddSubview(_documentBrowser.View);
            
            _documentBrowser.DidMoveToParentViewController(parentViewController);
        }
    }

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);
        
        // Actualizar el frame cuando la vista esté lista
        platformView.LayoutSubviews();
        if (_documentBrowser?.View != null)
        {
            _documentBrowser.View.Frame = platformView.Bounds;
        }
    }

    private void OnDocumentsPicked(NSUrl[] urls)
    {
        var filePaths = new List<string>();

        foreach (var url in urls)
        {
            // Iniciar acceso de seguridad al archivo
            var accessing = url.StartAccessingSecurityScopedResource();
            try
            {
                if (!string.IsNullOrEmpty(url.Path))
                {
                    filePaths.Add(url.Path);
                }
            }
            finally
            {
                if (accessing)
                {
                    url.StopAccessingSecurityScopedResource();
                }
            }
        }

        if (filePaths.Count > 0)
        {
            VirtualView?.OnFilesSelected(filePaths.ToArray());
        }
    }

    private UIViewController? GetCurrentViewController()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);

        var rootViewController = window?.RootViewController;
        
        while (rootViewController?.PresentedViewController != null)
        {
            rootViewController = rootViewController.PresentedViewController;
        }

        return rootViewController;
    }

    private static void MapAllowedFileTypes(EmbeddedFileBrowserHandler handler, EmbeddedFileBrowser view)
    {
        // Recrear el browser si cambian los tipos
    }

    private static void MapAllowMultipleSelection(EmbeddedFileBrowserHandler handler, EmbeddedFileBrowser view)
    {
        if (handler._documentBrowser != null)
        {
            handler._documentBrowser.AllowsPickingMultipleItems = view.AllowMultipleSelection;
        }
    }

    protected override void DisconnectHandler(UIView platformView)
    {
        if (_documentBrowser != null)
        {
            _documentBrowser.Delegate = null;
            _documentBrowser.View?.RemoveFromSuperview();
            _documentBrowser.RemoveFromParentViewController();
            _documentBrowser = null;
        }
        _browserDelegate = null;
        base.DisconnectHandler(platformView);
    }
}

/// <summary>
/// Delegado para UIDocumentBrowserViewController
/// </summary>
public class DocumentBrowserDelegate : UIDocumentBrowserViewControllerDelegate
{
    private readonly Action<NSUrl[]> _onDocumentsPicked;

    public DocumentBrowserDelegate(Action<NSUrl[]> onDocumentsPicked)
    {
        _onDocumentsPicked = onDocumentsPicked;
    }

    public override void DidPickDocumentsAtUrls(UIDocumentBrowserViewController controller, NSUrl[] documentUrls)
    {
        _onDocumentsPicked?.Invoke(documentUrls);
    }
}
#endif
