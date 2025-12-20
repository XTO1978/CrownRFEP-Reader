using Microsoft.Maui.Controls;

namespace CrownRFEP_Reader.Behaviors;

/// <summary>
/// Behavior para crear una zona de drop para archivos
/// </summary>
public class DropZoneBehavior : Behavior<View>
{
    public static readonly BindableProperty DropCommandProperty = BindableProperty.Create(
        nameof(DropCommand),
        typeof(Command<IEnumerable<string>>),
        typeof(DropZoneBehavior));

    public static readonly BindableProperty IsDragOverProperty = BindableProperty.Create(
        nameof(IsDragOver),
        typeof(bool),
        typeof(DropZoneBehavior),
        defaultValue: false,
        defaultBindingMode: BindingMode.OneWayToSource);

    public Command<IEnumerable<string>>? DropCommand
    {
        get => (Command<IEnumerable<string>>?)GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    public bool IsDragOver
    {
        get => (bool)GetValue(IsDragOverProperty);
        set => SetValue(IsDragOverProperty, value);
    }

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        
        // Configurar drop gesture recognizer
        var dropGesture = new DropGestureRecognizer();
        dropGesture.DragOver += OnDragOver;
        dropGesture.DragLeave += OnDragLeave;
        dropGesture.Drop += OnDrop;
        
        bindable.GestureRecognizers.Add(dropGesture);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);
        
        var dropGesture = bindable.GestureRecognizers.OfType<DropGestureRecognizer>().FirstOrDefault();
        if (dropGesture != null)
        {
            dropGesture.DragOver -= OnDragOver;
            dropGesture.DragLeave -= OnDragLeave;
            dropGesture.Drop -= OnDrop;
            bindable.GestureRecognizers.Remove(dropGesture);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        IsDragOver = true;
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        IsDragOver = false;
    }

    private async void OnDrop(object? sender, DropEventArgs e)
    {
        IsDragOver = false;
        
        try
        {
            var data = e.Data;
            if (data == null) return;

            var filePaths = new List<string>();

            // Intentar obtener archivos del data package
            var properties = data.Properties;
            if (properties != null)
            {
                // Primero verificar si es un drag interno con VideoFiles
                if (properties.TryGetValue("VideoFiles", out var videoFilesObj) && videoFilesObj is IEnumerable<string> videoFiles)
                {
                    filePaths.AddRange(videoFiles.Where(File.Exists));
                    System.Diagnostics.Debug.WriteLine($"Internal drag detected with {filePaths.Count} video files");
                }
                else
                {
                    // Buscar rutas de archivos en las propiedades
                    foreach (var prop in properties)
                    {
                        System.Diagnostics.Debug.WriteLine($"Drop property: {prop.Key} = {prop.Value}");
                        
                        if (prop.Value is string path && File.Exists(path))
                        {
                            filePaths.Add(path);
                        }
                        else if (prop.Value is IEnumerable<string> paths)
                        {
                            filePaths.AddRange(paths.Where(File.Exists));
                        }
                    }
                }
            }

            // Si no hay archivos aún, intentar obtener texto que podría ser una ruta
            if (filePaths.Count == 0)
            {
                var text = await data.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    // Puede ser múltiples rutas separadas por líneas
                    var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var path = line.Trim();
                        // Limpiar prefijos de URL file://
                        if (path.StartsWith("file://"))
                        {
                            path = Uri.UnescapeDataString(path.Substring(7));
                        }
                        
                        if (File.Exists(path))
                        {
                            filePaths.Add(path);
                        }
                    }
                }
            }

            if (filePaths.Count > 0)
            {
                // Eliminar duplicados
                var uniquePaths = filePaths.Distinct().ToList();
                DropCommand?.Execute(uniquePaths);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing drop: {ex.Message}");
        }
    }
}
