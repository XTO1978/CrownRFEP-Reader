using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.Models;

/// <summary>
/// Representa un elemento del sistema de archivos (archivo o carpeta) para el TreeView
/// </summary>
public class FileSystemItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isLoading;
    private bool _childrenLoaded;
    private bool _isChecked;
    private bool _isAddedToSession;

    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public bool IsDrive { get; set; }
    public int Level { get; set; }
    public FileSystemItem? Parent { get; set; }
    
    /// <summary>
    /// Extensión del archivo (sin el punto)
    /// </summary>
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(FullPath).TrimStart('.').ToLowerInvariant();
    
    /// <summary>
    /// Indica si es un archivo .crown
    /// </summary>
    public bool IsCrownFile => !IsDirectory && Extension == "crown";

    /// <summary>
    /// Extensiones de video soportadas
    /// </summary>
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mov", "m4v", "avi", "mkv", "webm", "wmv", "flv", "3gp"
    };

    /// <summary>
    /// Indica si es un archivo de video
    /// </summary>
    public bool IsVideoFile => !IsDirectory && VideoExtensions.Contains(Extension);

    public ObservableCollection<FileSystemItem> Children { get; set; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpanderIcon));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ChildrenLoaded
    {
        get => _childrenLoaded;
        set
        {
            if (_childrenLoaded != value)
            {
                _childrenLoaded = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Indica si el archivo ya ha sido añadido a la lista de videos pendientes para la sesión
    /// </summary>
    public bool IsAddedToSession
    {
        get => _isAddedToSession;
        set
        {
            if (_isAddedToSession != value)
            {
                _isAddedToSession = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Indica si el item puede expandirse (es directorio y tiene o puede tener hijos)
    /// </summary>
    public bool CanExpand => IsDirectory || IsDrive;

    /// <summary>
    /// Icono SF Symbol para el expander
    /// </summary>
    public string ExpanderIcon => IsExpanded ? "chevron.down" : "chevron.right";

    /// <summary>
    /// Icono SF Symbol para el tipo de archivo/carpeta
    /// </summary>
    public string Icon
    {
        get
        {
            if (IsDrive) return "externaldrive.fill";
            if (IsDirectory) return IsExpanded ? "folder.fill" : "folder";
            
            return Extension switch
            {
                "crown" => "doc.badge.gearshape.fill",
                "mp4" or "mov" or "avi" or "mkv" => "film",
                "jpg" or "jpeg" or "png" or "gif" or "heic" => "photo",
                "pdf" => "doc.text.fill",
                "zip" or "rar" or "7z" => "doc.zipper",
                _ => "doc"
            };
        }
    }

    /// <summary>
    /// Color del icono
    /// </summary>
    public string IconColor
    {
        get
        {
            if (IsDrive) return "#FF888888";
            if (IsDirectory) return "#FF6DDDFF";
            if (IsCrownFile) return "#FF6DDDFF";
            return "#FFB1B1B1";
        }
    }

    /// <summary>
    /// Ancho en píxeles para indentación según nivel
    /// </summary>
    public double Indentation => Level * 20;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
