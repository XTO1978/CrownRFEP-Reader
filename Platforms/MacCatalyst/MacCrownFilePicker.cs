using Foundation;
using ObjCRuntime;
using System.Runtime.InteropServices;

namespace CrownRFEP_Reader.Platforms.MacCatalyst;

/// <summary>
/// Selector de archivos nativo para MacCatalyst usando NSOpenPanel via ObjC runtime.
/// UIDocumentPickerViewController tiene problemas conocidos en MacCatalyst con MAUI.
/// </summary>
public static class MacCrownFilePicker
{
    // Selectores de ObjC para NSOpenPanel
    private static readonly Selector SelOpenPanel = new("openPanel");
    private static readonly Selector SelSetTitle = new("setTitle:");
    private static readonly Selector SelSetCanChooseFiles = new("setCanChooseFiles:");
    private static readonly Selector SelSetCanChooseDirectories = new("setCanChooseDirectories:");
    private static readonly Selector SelSetAllowsMultipleSelection = new("setAllowsMultipleSelection:");
    private static readonly Selector SelRunModal = new("runModal");
    private static readonly Selector SelUrl = new("URL");

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nint(IntPtr receiver, IntPtr selector);

    public static Task<string?> PickToCacheAsync()
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // NSOpenPanel.runModal DEBE ejecutarse en el hilo principal
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("MacCrownFilePicker: Creando NSOpenPanel via ObjC runtime...");

                // Obtener clase NSOpenPanel
                var nsOpenPanelClass = objc_getClass("NSOpenPanel");
                if (nsOpenPanelClass == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("MacCrownFilePicker: No se pudo obtener clase NSOpenPanel");
                    tcs.TrySetResult(null);
                    return;
                }

                // Crear instancia: [NSOpenPanel openPanel]
                var panel = objc_msgSend_IntPtr(nsOpenPanelClass, SelOpenPanel.Handle);
                if (panel == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("MacCrownFilePicker: No se pudo crear NSOpenPanel");
                    tcs.TrySetResult(null);
                    return;
                }

                // Configurar panel
                var title = new NSString("Seleccionar archivo .crown");
                objc_msgSend_void_IntPtr(panel, SelSetTitle.Handle, title.Handle);
                objc_msgSend_void_bool(panel, SelSetCanChooseFiles.Handle, true);
                objc_msgSend_void_bool(panel, SelSetCanChooseDirectories.Handle, false);
                objc_msgSend_void_bool(panel, SelSetAllowsMultipleSelection.Handle, false);

                System.Diagnostics.Debug.WriteLine("MacCrownFilePicker: Mostrando panel (runModal)...");

                // Mostrar panel de forma modal: [panel runModal]
                // Retorna 1 (NSModalResponseOK) si el usuario seleccionó archivo
                var result = objc_msgSend_nint(panel, SelRunModal.Handle);

                System.Diagnostics.Debug.WriteLine($"MacCrownFilePicker: runModal retornó {result}");

                if (result == 1) // NSModalResponseOK
                {
                    // Obtener URL: [panel URL]
                    var urlPtr = objc_msgSend_IntPtr(panel, SelUrl.Handle);
                    if (urlPtr != IntPtr.Zero)
                    {
                        var url = Runtime.GetNSObject<NSUrl>(urlPtr);
                        var destPath = CopyPickedUrlToCache(url);
                        System.Diagnostics.Debug.WriteLine($"MacCrownFilePicker: Archivo copiado a {destPath}");
                        tcs.TrySetResult(destPath);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("MacCrownFilePicker: URL es null");
                        tcs.TrySetResult(null);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("MacCrownFilePicker: Usuario canceló");
                    tcs.TrySetResult(null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MacCrownFilePicker error: {ex}");
                tcs.TrySetResult(null);
            }
        });

        return tcs.Task;
    }

    private static string? CopyPickedUrlToCache(NSUrl? url)
    {
        if (url == null)
        {
            return null;
        }

        try
        {
            var started = url.StartAccessingSecurityScopedResource();
            try
            {
                var sourcePath = url.Path;
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    System.Diagnostics.Debug.WriteLine($"MacCrownFilePicker: Archivo no existe: {sourcePath}");
                    return null;
                }

                var fileName = Path.GetFileName(sourcePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = "import.crown";
                }

                var destPath = Path.Combine(FileSystem.CacheDirectory, $"import_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
                File.Copy(sourcePath, destPath, overwrite: true);

                System.Diagnostics.Debug.WriteLine($"MacCrownFilePicker: Copiado {sourcePath} -> {destPath}");
                return destPath;
            }
            finally
            {
                if (started)
                {
                    url.StopAccessingSecurityScopedResource();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MacCrownFilePicker copy error: {ex}");
            return null;
        }
    }
}
