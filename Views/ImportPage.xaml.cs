using CrownRFEP_Reader.ViewModels;
using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Views;

public partial class ImportPage : ContentPage
{
    private ImportViewModel ViewModel => (ImportViewModel)BindingContext;

    public ImportPage(ImportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

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
