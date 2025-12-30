using CrownRFEP_Reader.ViewModels;
using CrownRFEP_Reader.Models;
#if IOS
using UIKit;
using UniformTypeIdentifiers;
using Foundation;
#endif

namespace CrownRFEP_Reader.Views;

public partial class ImportPage : ContentPage
{
    private ImportViewModel ViewModel => (ImportViewModel)BindingContext;

    public ImportPage(ImportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        
#if IOS
        // En iOS: reorganizar layout a 2 columnas
        // Columna 0: Videos para Sesión (antes columna 2)
        // Columna 1: Crear Sesión (antes parte de columna 2)
        // Ocultar: Árbol de carpetas (columna 0) y Archivos (columna 1)
        
        // Ocultar columnas originales
        TreeColumn.Width = new GridLength(0);
        FilesColumn.Width = new GridLength(0);
        TreeBorder.IsVisible = false;
        FilesBorder.IsVisible = false;
        
        // Reorganizar columna de sesión
        SessionColumn.Width = new GridLength(1, GridUnitType.Star);
        
        // Mover SessionGrid a columna 0 y usar 2 columnas
        Grid.SetColumn(SessionGrid, 0);
        Grid.SetColumnSpan(SessionGrid, 3);
        
        // Cambiar layout del SessionGrid a horizontal (2 columnas)
        SessionGrid.RowDefinitions.Clear();
        SessionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SessionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        
        // Mover DropZone a columna 0
        Grid.SetRow(DropZoneBorder, 0);
        Grid.SetColumn(DropZoneBorder, 0);
        
        // Mover CreateSession a columna 1
        Grid.SetRow(CreateSessionBorder, 0);
        Grid.SetColumn(CreateSessionBorder, 1);
        CreateSessionBorder.Margin = new Thickness(8, 0, 0, 0);
#endif
    }

#if IOS
    private void OnOpenPickerClicked(object? sender, EventArgs e)
    {
        var allowedTypes = new[]
        {
            UTTypes.Movie,
            UTTypes.Video,
            UTTypes.Mpeg,
            UTTypes.Mpeg4Movie,
            UTTypes.Avi,
            UTTypes.QuickTimeMovie
        };

        var picker = new UIDocumentPickerViewController(allowedTypes, false)
        {
            AllowsMultipleSelection = true,
            ModalPresentationStyle = UIModalPresentationStyle.FormSheet
        };

        picker.DidPickDocumentAtUrls += async (s, args) =>
        {
            foreach (var url in args.Urls)
            {
                var accessing = url.StartAccessingSecurityScopedResource();
                try
                {
                    if (!string.IsNullOrEmpty(url.Path))
                    {
                        // Copiar el archivo al directorio de documentos de la app
                        var documentsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        var importedVideosDir = Path.Combine(documentsDir, "ImportedVideos");
                        
                        if (!Directory.Exists(importedVideosDir))
                        {
                            Directory.CreateDirectory(importedVideosDir);
                        }
                        
                        var fileName = Path.GetFileName(url.Path);
                        var destPath = Path.Combine(importedVideosDir, fileName);
                        
                        // Si ya existe, añadir sufijo único
                        if (File.Exists(destPath))
                        {
                            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                            var ext = Path.GetExtension(fileName);
                            destPath = Path.Combine(importedVideosDir, $"{nameWithoutExt}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
                        }
                        
                        // Copiar el archivo
                        await Task.Run(() => File.Copy(url.Path, destPath));
                        
                        // Añadir la ruta local
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            ViewModel.AddVideoFromPath(destPath);
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error copying file: {ex.Message}");
                }
                finally
                {
                    if (accessing)
                    {
                        url.StopAccessingSecurityScopedResource();
                    }
                }
            }
        };

        var viewController = GetCurrentViewController();
        viewController?.PresentViewController(picker, true, null);
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
#else
    private void OnOpenPickerClicked(object? sender, EventArgs e)
    {
        // No usado en otras plataformas
    }
#endif

    /// <summary>
    /// Maneja el inicio del drag de archivos de video desde la lista
    /// </summary>
    private void OnVideoDragStarting(object? sender, DragStartingEventArgs e)
    {
        // Obtener el item actual desde el BindingContext del Border
        FileSystemItem? currentItem = null;
        if (sender is View view && view.BindingContext is FileSystemItem item)
        {
            currentItem = item;
        }

        var filesToDrag = new List<string>();

        // Obtener archivos seleccionados (checked)
        var checkedFiles = ViewModel.GetCheckedFolderFiles().ToList();

        if (checkedFiles.Count > 0)
        {
            // Si hay archivos seleccionados, arrastrar todos los seleccionados
            filesToDrag.AddRange(checkedFiles.Select(f => f.FullPath));

            // Si el item actual no está en la selección, añadirlo también
            if (currentItem != null && !checkedFiles.Contains(currentItem))
            {
                filesToDrag.Add(currentItem.FullPath);
            }
        }
        else if (currentItem != null)
        {
            // Si no hay selección, solo arrastrar el item actual
            filesToDrag.Add(currentItem.FullPath);
        }

        if (filesToDrag.Count > 0)
        {
            // Establecer los datos del drag
            e.Data.Text = string.Join("\n", filesToDrag);
            e.Data.Properties["VideoFiles"] = filesToDrag;
            e.Data.Properties["IsInternalDrag"] = true;

            System.Diagnostics.Debug.WriteLine($"Drag starting with {filesToDrag.Count} files");
        }
        else
        {
            e.Cancel = true;
        }
    }
}
