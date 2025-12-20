#if MACCATALYST
using Foundation;
using UIKit;
using UniformTypeIdentifiers;
using ObjCRuntime;
using Microsoft.Maui.Platform;

namespace CrownRFEP_Reader.Platforms.MacCatalyst;

/// <summary>
/// Controlador de interacción para manejar drop de archivos desde Finder
/// </summary>
public class FileDropInteraction : UIDropInteraction
{
    public FileDropInteraction(IUIDropInteractionDelegate interactionDelegate) 
        : base(interactionDelegate)
    {
    }
}

/// <summary>
/// Delegado para manejar eventos de drop de archivos
/// </summary>
public class FileDropInteractionDelegate : NSObject, IUIDropInteractionDelegate
{
    private readonly Action<IEnumerable<string>> _onFilesDropped;

    public FileDropInteractionDelegate(Action<IEnumerable<string>> onFilesDropped)
    {
        _onFilesDropped = onFilesDropped;
    }

    /// <summary>
    /// Indica si la sesión de drop puede ser manejada
    /// </summary>
    [Export("dropInteraction:canHandleSession:")]
    public bool CanHandleSession(UIDropInteraction interaction, IUIDropSession session)
    {
        // Aceptamos archivos (UTType File)
        return session.CanLoadObjects(new Class(typeof(NSUrl)));
    }

    /// <summary>
    /// Proporciona una propuesta de drop
    /// </summary>
    [Export("dropInteraction:sessionDidUpdate:")]
    public UIDropProposal SessionDidUpdate(UIDropInteraction interaction, IUIDropSession session)
    {
        // Copiar los archivos
        return new UIDropProposal(UIDropOperation.Copy);
    }

    /// <summary>
    /// Maneja el drop de archivos
    /// </summary>
    [Export("dropInteraction:performDrop:")]
    public void PerformDrop(UIDropInteraction interaction, IUIDropSession session)
    {
        var filePaths = new List<string>();
        var loadedCount = 0;
        var totalItems = (int)session.Items.Length;

        if (totalItems == 0)
        {
            return;
        }

        foreach (var item in session.Items)
        {
            var provider = item.ItemProvider;
            
            // Intentar cargar como URL de archivo
            if (provider.HasItemConformingTo(UTTypes.FileUrl.Identifier))
            {
                provider.LoadItem(UTTypes.FileUrl.Identifier, null, (data, error) =>
                {
                    if (data is NSUrl url && url.IsFileUrl)
                    {
                        var path = url.Path;
                        if (!string.IsNullOrEmpty(path))
                        {
                            lock (filePaths)
                            {
                                filePaths.Add(path);
                            }
                        }
                    }
                    
                    Interlocked.Increment(ref loadedCount);
                    if (loadedCount >= totalItems && filePaths.Count > 0)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _onFilesDropped(filePaths);
                        });
                    }
                });
            }
            else if (provider.HasItemConformingTo(UTTypes.Movie.Identifier))
            {
                // También aceptar videos directamente
                provider.LoadItem(UTTypes.Movie.Identifier, null, (data, error) =>
                {
                    if (data is NSUrl url)
                    {
                        var path = url.Path ?? url.AbsoluteString;
                        if (!string.IsNullOrEmpty(path) && path.StartsWith("file://"))
                        {
                            path = Uri.UnescapeDataString(path.Substring(7));
                        }
                        
                        if (!string.IsNullOrEmpty(path))
                        {
                            lock (filePaths)
                            {
                                filePaths.Add(path);
                            }
                        }
                    }
                    
                    Interlocked.Increment(ref loadedCount);
                    if (loadedCount >= totalItems && filePaths.Count > 0)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _onFilesDropped(filePaths);
                        });
                    }
                });
            }
            else
            {
                Interlocked.Increment(ref loadedCount);
            }
        }
    }
}

/// <summary>
/// Helper estático para añadir soporte de drop a una vista
/// </summary>
public static class FileDropHelper
{
    public static void EnableFileDrop(UIView view, Action<IEnumerable<string>> onFilesDropped)
    {
        var dropDelegate = new FileDropInteractionDelegate(onFilesDropped);
        var dropInteraction = new FileDropInteraction(dropDelegate);
        view.AddInteraction(dropInteraction);
    }
}
#endif
