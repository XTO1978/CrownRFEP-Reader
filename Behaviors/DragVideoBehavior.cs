using Microsoft.Maui.Controls;
using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.Behaviors;

/// <summary>
/// Behavior para permitir arrastrar archivos de video desde la lista
/// </summary>
public class DragVideoBehavior : Behavior<View>
{
    public static readonly BindableProperty FileItemProperty = BindableProperty.Create(
        nameof(FileItem),
        typeof(FileSystemItem),
        typeof(DragVideoBehavior));

    public static readonly BindableProperty GetSelectedFilesProperty = BindableProperty.Create(
        nameof(GetSelectedFiles),
        typeof(Func<IEnumerable<FileSystemItem>>),
        typeof(DragVideoBehavior));

    /// <summary>
    /// El FileSystemItem asociado a este elemento
    /// </summary>
    public FileSystemItem? FileItem
    {
        get => (FileSystemItem?)GetValue(FileItemProperty);
        set => SetValue(FileItemProperty, value);
    }

    /// <summary>
    /// Función para obtener todos los archivos seleccionados (checked)
    /// </summary>
    public Func<IEnumerable<FileSystemItem>>? GetSelectedFiles
    {
        get => (Func<IEnumerable<FileSystemItem>>?)GetValue(GetSelectedFilesProperty);
        set => SetValue(GetSelectedFilesProperty, value);
    }

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        
        var dragGesture = new DragGestureRecognizer();
        dragGesture.DragStarting += OnDragStarting;
        bindable.GestureRecognizers.Add(dragGesture);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);
        
        var dragGesture = bindable.GestureRecognizers.OfType<DragGestureRecognizer>().FirstOrDefault();
        if (dragGesture != null)
        {
            dragGesture.DragStarting -= OnDragStarting;
            bindable.GestureRecognizers.Remove(dragGesture);
        }
    }

    private void OnDragStarting(object? sender, DragStartingEventArgs e)
    {
        var filesToDrag = new List<string>();
        
        // Obtener archivos seleccionados (checked)
        var selectedFiles = GetSelectedFiles?.Invoke()?.ToList();
        
        if (selectedFiles != null && selectedFiles.Count > 0)
        {
            // Si hay archivos seleccionados, arrastrar todos los seleccionados
            filesToDrag.AddRange(selectedFiles.Select(f => f.FullPath));
            
            // Si el item actual no está en la selección, añadirlo también
            if (FileItem != null && !selectedFiles.Contains(FileItem))
            {
                filesToDrag.Add(FileItem.FullPath);
            }
        }
        else if (FileItem != null)
        {
            // Si no hay selección, solo arrastrar el item actual
            filesToDrag.Add(FileItem.FullPath);
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
