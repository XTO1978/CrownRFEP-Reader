using Microsoft.Maui.Controls;

namespace CrownRFEP_Reader.Controls;

/// <summary>
/// Control que embebe un explorador de archivos nativo.
/// En iOS usa UIDocumentBrowserViewController embebido.
/// En otras plataformas muestra un placeholder.
/// </summary>
public class EmbeddedFileBrowser : View
{
    public static readonly BindableProperty AllowedFileTypesProperty =
        BindableProperty.Create(nameof(AllowedFileTypes), typeof(string[]), typeof(EmbeddedFileBrowser),
            new[] { "public.movie", "public.video" });

    public static readonly BindableProperty AllowMultipleSelectionProperty =
        BindableProperty.Create(nameof(AllowMultipleSelection), typeof(bool), typeof(EmbeddedFileBrowser), true);

    /// <summary>
    /// Tipos de archivo permitidos (UTTypes para iOS)
    /// </summary>
    public string[] AllowedFileTypes
    {
        get => (string[])GetValue(AllowedFileTypesProperty);
        set => SetValue(AllowedFileTypesProperty, value);
    }

    /// <summary>
    /// Permitir selección múltiple
    /// </summary>
    public bool AllowMultipleSelection
    {
        get => (bool)GetValue(AllowMultipleSelectionProperty);
        set => SetValue(AllowMultipleSelectionProperty, value);
    }

    /// <summary>
    /// Evento cuando se seleccionan archivos
    /// </summary>
    public event EventHandler<FilesSelectedEventArgs>? FilesSelected;

    /// <summary>
    /// Invoca el evento FilesSelected
    /// </summary>
    public void OnFilesSelected(string[] filePaths)
    {
        FilesSelected?.Invoke(this, new FilesSelectedEventArgs(filePaths));
    }
}

/// <summary>
/// Argumentos del evento FilesSelected
/// </summary>
public class FilesSelectedEventArgs : EventArgs
{
    public string[] FilePaths { get; }

    public FilesSelectedEventArgs(string[] filePaths)
    {
        FilePaths = filePaths;
    }
}
