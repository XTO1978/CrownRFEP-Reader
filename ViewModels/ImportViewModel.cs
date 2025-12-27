using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;

#if WINDOWS
using Windows.Storage;
using Windows.Storage.Streams;
#endif

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// Modelo para representar un video pendiente de importar
/// </summary>
public class PendingVideoItem : BaseViewModel
{
    private bool _isSelected;

    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string Extension { get; set; } = "";
    public long FileSize { get; set; }
    public string FileSizeFormatted => FormatFileSize(FileSize);
    public string Icon => GetIconForExtension(Extension);
    public string IconColor => "#FF6DDDFF";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string GetIconForExtension(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            "mp4" or "m4v" or "mpg" or "mpeg" => "film",
            "mov" or "mts" or "m2ts" => "video",
            "avi" or "divx" or "xvid" => "film.stack",
            "mkv" or "webm" or "ogv" => "play.rectangle.on.rectangle",
            "wmv" or "asf" => "tv",
            "flv" or "f4v" => "play.tv",
            "vob" or "ts" => "opticaldisc",
            "3gp" or "rm" or "rmvb" => "iphone",
            "dv" or "mxf" => "video.badge.waveform",
            "gif" => "photo",
            _ => "doc"
        };
    }
}

/// <summary>
/// ViewModel para la página de importación con explorador de archivos y creación de sesiones customizadas
/// </summary>
public class ImportViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private readonly CrownFileService _crownFileService;
    private readonly ThumbnailService _thumbnailService;
    private readonly ImportProgressService _importProgressService;

    private CancellationTokenSource? _importCts;

    private FileSystemItem? _selectedItem;
    private int _folderFilesRequestId;
    private string _importProgressText = "";
    private int _importProgressValue;
    private bool _isImporting;
    private string _currentPath = "";
    private bool _isDragOver;
    private string _customSessionName = "";
    private string _customSessionLocation = "";
    private DateTime _customSessionDate = DateTime.Today;

    // Extensiones de video soportadas
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".3gp",
        ".mpg", ".mpeg", ".mts", ".m2ts", ".ts", ".vob", ".ogv", ".divx", ".xvid",
        ".asf", ".rm", ".rmvb", ".dv", ".f4v", ".mxf", ".gif"
    };

    public ImportViewModel(DatabaseService databaseService, CrownFileService crownFileService, ThumbnailService thumbnailService, ImportProgressService importProgressService)
    {
        _databaseService = databaseService;
        _crownFileService = crownFileService;
        _thumbnailService = thumbnailService;
        _importProgressService = importProgressService;

        // Comandos del explorador
        ToggleExpandCommand = new Command<FileSystemItem>(async (item) => await ToggleExpandAsync(item));
        SelectItemCommand = new Command<FileSystemItem>(SelectItem);
        ImportSelectedCommand = new Command(async () => await ImportSelectedAsync(), () => CanImportSelected);
        NavigateUpCommand = new Command(NavigateUp, () => CanNavigateUp);
        RefreshCommand = new Command(async () => await LoadRootItemsAsync());
        ImportFilePickerCommand = new Command(async () => await ImportWithFilePickerAsync());

        // Comandos para videos externos y sesión customizada
        AddVideosFromExplorerCommand = new Command(AddSelectedVideoFromExplorer, () => SelectedItem?.IsVideoFile == true);
        AddVideoFromFolderListCommand = new Command<FileSystemItem>(AddVideoFromFolderList);
        AddAllFolderVideosCommand = new Command(AddAllFolderVideos, () => FolderFiles.Count > 0);
        AddVideosWithPickerCommand = new Command(async () => await AddVideosWithPickerAsync());
        RemoveVideoCommand = new Command<PendingVideoItem>(RemoveVideo);
        ClearAllVideosCommand = new Command(ClearAllVideos, () => PendingVideos.Count > 0);
        CreateCustomSessionCommand = new Command(async () => await CreateCustomSessionAsync(), () => CanCreateCustomSession);
        CancelImportCommand = new Command(CancelImport, () => IsImporting);
        FilesDroppedCommand = new Command<IEnumerable<string>>(OnFilesDropped);

        // Cargar elementos raíz
        _ = LoadRootItemsAsync();
    }

    #region Propiedades

    /// <summary>
    /// Items del TreeView (estructura plana para CollectionView)
    /// </summary>
    public ObservableCollection<FileSystemItem> FlatItems { get; } = new();

    /// <summary>
    /// Items raíz (drives/carpetas principales)
    /// </summary>
    public ObservableCollection<FileSystemItem> RootItems { get; } = new();

    /// <summary>
    /// Archivos de video en la carpeta seleccionada
    /// </summary>
    public ObservableCollection<FileSystemItem> FolderFiles { get; } = new();

    /// <summary>
    /// Videos pendientes de agregar a la sesión customizada
    /// </summary>
    public ObservableCollection<PendingVideoItem> PendingVideos { get; } = new();

    /// <summary>
    /// Información de la carpeta seleccionada
    /// </summary>
    public string SelectedFolderInfo => SelectedItem?.IsDirectory == true 
        ? $"📂 {SelectedItem.Name}" 
        : (SelectedItem != null ? $"📄 {SelectedItem.Name}" : "Selecciona una carpeta");

    /// <summary>
    /// Número de archivos de video en la carpeta seleccionada
    /// </summary>
    public string FolderFilesInfo => FolderFiles.Count switch
    {
        0 => "Sin archivos de video",
        1 => "1 archivo de video",
        _ => $"{FolderFiles.Count} archivos de video"
    };

    /// <summary>
    /// Obtiene los archivos de video marcados (checked) en la lista de la carpeta
    /// </summary>
    public IEnumerable<FileSystemItem> GetCheckedFolderFiles()
    {
        return FolderFiles.Where(f => f.IsChecked).ToList();
    }

    /// <summary>
    /// Indica si hay archivos siendo arrastrados sobre la zona de drop
    /// </summary>
    public bool IsDragOver
    {
        get => _isDragOver;
        set => SetProperty(ref _isDragOver, value);
    }

    /// <summary>
    /// Nombre para la sesión customizada
    /// </summary>
    public string CustomSessionName
    {
        get => _customSessionName;
        set
        {
            if (SetProperty(ref _customSessionName, value))
            {
                OnPropertyChanged(nameof(CanCreateCustomSession));
                ((Command)CreateCustomSessionCommand).ChangeCanExecute();
            }
        }
    }

    /// <summary>
    /// Ubicación de la sesión customizada (obligatoria)
    /// </summary>
    public string CustomSessionLocation
    {
        get => _customSessionLocation;
        set
        {
            if (SetProperty(ref _customSessionLocation, value))
            {
                OnPropertyChanged(nameof(CanCreateCustomSession));
                ((Command)CreateCustomSessionCommand).ChangeCanExecute();
            }
        }
    }

    /// <summary>
    /// Fecha de la sesión customizada
    /// </summary>
    public DateTime CustomSessionDate
    {
        get => _customSessionDate;
        set => SetProperty(ref _customSessionDate, value);
    }

    /// <summary>
    /// Indica si se puede crear una sesión customizada
    /// </summary>
    public bool CanCreateCustomSession => 
        PendingVideos.Count > 0 && 
        !string.IsNullOrWhiteSpace(CustomSessionName) && 
        !string.IsNullOrWhiteSpace(CustomSessionLocation) &&
        !IsImporting;

    /// <summary>
    /// Texto informativo sobre los videos pendientes
    /// </summary>
    public string PendingVideosInfo => PendingVideos.Count switch
    {
        0 => "Arrastra videos aquí o usa los botones para añadir",
        1 => "1 video listo para crear sesión",
        _ => $"{PendingVideos.Count} videos listos para crear sesión"
    };

    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            var previous = _selectedItem;
            if (SetProperty(ref _selectedItem, value))
            {
                if (previous != null)
                {
                    previous.IsSelected = false;
                }

                if (value != null)
                {
                    value.IsSelected = true;
                    CurrentPath = value.FullPath;
                    _ = LoadFolderFilesAsync(value);
                }
                else
                {
                    CurrentPath = "";
                    FolderFiles.Clear();
                }

                OnPropertyChanged(nameof(CanImportSelected));
                OnPropertyChanged(nameof(SelectedItemInfo));
                OnPropertyChanged(nameof(SelectedFolderInfo));
                OnPropertyChanged(nameof(FolderFilesInfo));
                ((Command)ImportSelectedCommand).ChangeCanExecute();
                ((Command)AddVideosFromExplorerCommand).ChangeCanExecute();
            }
        }
    }

    public string CurrentPath
    {
        get => _currentPath;
        set => SetProperty(ref _currentPath, value);
    }

    public string SelectedItemInfo => SelectedItem != null 
        ? (SelectedItem.IsCrownFile ? $"Archivo Crown: {SelectedItem.Name}" : SelectedItem.Name)
        : "Ningún archivo seleccionado";

    public bool CanImportSelected => SelectedItem?.IsCrownFile == true && !IsImporting;
    public bool CanNavigateUp => !string.IsNullOrEmpty(CurrentPath);

    public string ImportProgressText
    {
        get => _importProgressText;
        set => SetProperty(ref _importProgressText, value);
    }

    public int ImportProgressValue
    {
        get => _importProgressValue;
        set => SetProperty(ref _importProgressValue, value);
    }

    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            if (SetProperty(ref _isImporting, value))
            {
                OnPropertyChanged(nameof(CanImportSelected));
                OnPropertyChanged(nameof(CanCreateCustomSession));
                ((Command)ImportSelectedCommand).ChangeCanExecute();
                ((Command)CreateCustomSessionCommand).ChangeCanExecute();
                ((Command)CancelImportCommand).ChangeCanExecute();
            }
        }
    }

    #endregion

    #region Comandos

    // Comandos del explorador de archivos
    public ICommand ToggleExpandCommand { get; }
    public ICommand SelectItemCommand { get; }
    public ICommand ImportSelectedCommand { get; }
    public ICommand NavigateUpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ImportFilePickerCommand { get; }

    // Comandos para videos externos y sesión customizada
    public ICommand AddVideosFromExplorerCommand { get; }
    public ICommand AddVideoFromFolderListCommand { get; }
    public ICommand AddVideosWithPickerCommand { get; }
    public ICommand RemoveVideoCommand { get; }
    public ICommand ClearAllVideosCommand { get; }
    public ICommand CreateCustomSessionCommand { get; }
    public ICommand CancelImportCommand { get; }
    public ICommand FilesDroppedCommand { get; }
    public ICommand AddAllFolderVideosCommand { get; }

    #endregion

    #region Métodos públicos

    /// <summary>
    /// Carga los elementos raíz del sistema de archivos
    /// </summary>
    public async Task LoadRootItemsAsync()
    {
        try
        {
            IsBusy = true;
            RootItems.Clear();
            FlatItems.Clear();

            await Task.Run(() =>
            {
                // En macOS, empezamos con el directorio home y algunos accesos rápidos
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var documentsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var downloadsDir = Path.Combine(homeDir, "Downloads");

                var quickAccess = new List<(string Name, string Path, string Icon)>
                {
                    ("Escritorio", desktopDir, "menubar.dock.rectangle"),
                    ("Documentos", documentsDir, "doc.on.doc"),
                    ("Descargas", downloadsDir, "arrow.down.circle"),
                    ("Inicio", homeDir, "house"),
                    ("Raíz", "/", "externaldrive.fill")
                };

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var (name, path, icon) in quickAccess)
                    {
                        if (Directory.Exists(path))
                        {
                            var item = new FileSystemItem
                            {
                                Name = name,
                                FullPath = path,
                                IsDirectory = true,
                                IsDrive = path == "/",
                                Level = 0
                            };
                            RootItems.Add(item);
                            FlatItems.Add(item);
                        }
                    }
                });
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading root items: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Métodos privados

    private async Task ToggleExpandAsync(FileSystemItem item)
    {
        if (!item.CanExpand) return;

        if (item.IsExpanded)
        {
            // Colapsar: remover hijos de la lista plana
            CollapseItem(item);
        }
        else
        {
            // Expandir: cargar hijos si es necesario
            await ExpandItemAsync(item);
        }
    }

    private async Task ExpandItemAsync(FileSystemItem item)
    {
        if (!item.CanExpand) return;

        item.IsLoading = true;

        try
        {
            if (!item.ChildrenLoaded)
            {
                await LoadChildrenAsync(item);
            }

            item.IsExpanded = true;

            // Insertar hijos en la lista plana después del padre
            var parentIndex = FlatItems.IndexOf(item);
            if (parentIndex >= 0)
            {
                InsertChildrenFlat(item, parentIndex + 1);
            }
        }
        finally
        {
            item.IsLoading = false;
        }
    }

    private void CollapseItem(FileSystemItem item)
    {
        item.IsExpanded = false;

        // Remover todos los descendientes de la lista plana
        RemoveDescendantsFlat(item);
    }

    private async Task LoadChildrenAsync(FileSystemItem parent)
    {
        parent.Children.Clear();

        var children = await Task.Run(() =>
        {
            var result = new List<FileSystemItem>();
            try
            {
                var dirInfo = new DirectoryInfo(parent.FullPath);

                var directories = dirInfo.GetDirectories()
                    .Where(d => !d.Name.StartsWith("."))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

                var files = dirInfo.GetFiles()
                    .Where(f => !f.Name.StartsWith(".") &&
                               (f.Extension.Equals(".crown", StringComparison.OrdinalIgnoreCase) ||
                                VideoExtensions.Contains(f.Extension)))
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var dir in directories)
                {
                    result.Add(new FileSystemItem
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Level = parent.Level + 1,
                        Parent = parent
                    });
                }

                foreach (var file in files)
                {
                    result.Add(new FileSystemItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Level = parent.Level + 1,
                        Parent = parent
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"No access to: {parent.FullPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading children: {ex.Message}");
            }

            return result;
        });

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            foreach (var child in children)
            {
                parent.Children.Add(child);
            }
            parent.ChildrenLoaded = true;
        });
    }

    private void InsertChildrenFlat(FileSystemItem parent, int startIndex)
    {
        var index = startIndex;
        foreach (var child in parent.Children)
        {
            FlatItems.Insert(index++, child);

            // Si el hijo estaba expandido, insertar sus hijos también
            if (child.IsExpanded && child.Children.Count > 0)
            {
                index = InsertExpandedChildrenRecursive(child, index);
            }
        }
    }

    private int InsertExpandedChildrenRecursive(FileSystemItem parent, int startIndex)
    {
        var index = startIndex;
        foreach (var child in parent.Children)
        {
            FlatItems.Insert(index++, child);
            if (child.IsExpanded && child.Children.Count > 0)
            {
                index = InsertExpandedChildrenRecursive(child, index);
            }
        }
        return index;
    }

    private void RemoveDescendantsFlat(FileSystemItem parent)
    {
        var toRemove = new List<FileSystemItem>();
        CollectDescendants(parent, toRemove);

        foreach (var item in toRemove)
        {
            FlatItems.Remove(item);
        }
    }

    private void CollectDescendants(FileSystemItem parent, List<FileSystemItem> descendants)
    {
        foreach (var child in parent.Children)
        {
            descendants.Add(child);
            if (child.Children.Count > 0)
            {
                CollectDescendants(child, descendants);
            }
        }
    }

    private void SelectItem(FileSystemItem item)
    {
        if (item == null) return;
        SelectedItem = item;
    }

    /// <summary>
    /// Carga los archivos de video de la carpeta seleccionada
    /// </summary>
    private async Task LoadFolderFilesAsync(FileSystemItem item)
    {
        var requestId = Interlocked.Increment(ref _folderFilesRequestId);

        FolderFiles.Clear();
        OnPropertyChanged(nameof(SelectedFolderInfo));
        OnPropertyChanged(nameof(FolderFilesInfo));

        // Si es un archivo, mostrar la carpeta padre
        var folderPath = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath);
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        await Task.Run(() =>
        {
            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                var files = dirInfo.GetFiles()
                    .Where(f => !f.Name.StartsWith(".") && VideoExtensions.Contains(f.Extension))
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (requestId != _folderFilesRequestId)
                    {
                        return;
                    }

                    // Crear un HashSet de rutas de videos pendientes para búsqueda rápida
                    var pendingPaths = new HashSet<string>(
                        PendingVideos.Select(v => v.FullPath),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var file in files)
                    {
                        var fileItem = new FileSystemItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false,
                            Level = 0,
                            IsAddedToSession = pendingPaths.Contains(file.FullName)
                        };
                        FolderFiles.Add(fileItem);
                    }
                    OnPropertyChanged(nameof(FolderFilesInfo));
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading folder files: {ex.Message}");
            }
        });
    }

    private void NavigateUp()
    {
        if (SelectedItem?.Parent != null)
        {
            SelectItem(SelectedItem.Parent);
        }
    }

    private async Task ImportSelectedAsync()
    {
        if (SelectedItem == null || !SelectedItem.IsCrownFile) return;

        await ImportFileAsync(SelectedItem.FullPath);
    }

    private async Task ImportWithFilePickerAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecciona un archivo .crown o video",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.macOS, new[] { "public.data", "public.movie", "public.video", "public.mpeg", "public.avi", "com.apple.quicktime-movie" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.data", "public.movie", "public.video", "public.mpeg", "public.avi", "com.apple.quicktime-movie" } },
                    { DevicePlatform.iOS, new[] { "public.data", "public.movie", "public.video", "public.mpeg", "public.avi", "com.apple.quicktime-movie" } },
                    { DevicePlatform.WinUI, new[] { ".crown", ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".mpg", ".mpeg", ".m4v", ".mts", ".m2ts" } },
                    { DevicePlatform.Android, new[] { "video/*", "application/octet-stream" } }
                })
            });

            if (result != null)
            {
                var extension = Path.GetExtension(result.FullPath).ToLowerInvariant();
                
                if (extension == ".crown")
                {
                    // Importar archivo .crown
                    await ImportFileAsync(result.FullPath);
                }
                else if (VideoExtensions.Contains(extension))
                {
                    // Añadir video a la lista de pendientes
                    AddVideoFile(result.FullPath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error picking file: {ex.Message}");
        }
    }

    private async Task ImportFileAsync(string filePath)
    {
        try
        {
            IsImporting = true;
            ImportProgressText = "Preparando importación...";
            ImportProgressValue = 0;

            var progress = new Progress<ImportProgress>(p =>
            {
                ImportProgressValue = p.Percentage;
                ImportProgressText = p.Message;
            });

            await _crownFileService.ImportCrownFileAsync(filePath, progress);

            ImportProgressText = "¡Importación completada!";
            ImportProgressValue = 100;

            // Esperar un momento y resetear
            await Task.Delay(2000);
            ImportProgressText = "";
            ImportProgressValue = 0;
        }
        catch (Exception ex)
        {
            ImportProgressText = $"Error: {ex.Message}";
            await Task.Delay(3000);
            ImportProgressText = "";
        }
        finally
        {
            IsImporting = false;
        }
    }

    #endregion

    #region Métodos para videos externos y sesión customizada

    /// <summary>
    /// Maneja los archivos soltados en la zona de drop
    /// </summary>
    private void OnFilesDropped(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            AddVideoFile(path);
        }
    }

    /// <summary>
    /// Añade el video seleccionado del explorador
    /// </summary>
    private void AddSelectedVideoFromExplorer()
    {
        if (SelectedItem?.IsVideoFile == true)
        {
            AddVideoFile(SelectedItem.FullPath);
        }
    }

    /// <summary>
    /// Añade un video desde la lista de archivos de carpeta
    /// </summary>
    private void AddVideoFromFolderList(FileSystemItem item)
    {
        if (item?.IsVideoFile == true)
        {
            AddVideoFile(item.FullPath);
        }
    }

    /// <summary>
    /// Añade todos los videos de la carpeta actual
    /// </summary>
    private void AddAllFolderVideos()
    {
        foreach (var item in FolderFiles.ToList())
        {
            AddVideoFile(item.FullPath);
        }
    }

    /// <summary>
    /// Abre un selector de archivos para añadir videos
    /// </summary>
    private async Task AddVideosWithPickerAsync()
    {
        try
        {
            var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Selecciona archivos de video",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.macOS, new[] { "public.movie", "public.video", "public.mpeg", "public.mpeg-4", "public.avi", "com.apple.quicktime-movie", "public.3gpp", "org.webmproject.webm", "com.microsoft.windows-media-wmv" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.movie", "public.video", "public.mpeg", "public.mpeg-4", "public.avi", "com.apple.quicktime-movie", "public.3gpp", "org.webmproject.webm", "com.microsoft.windows-media-wmv" } },
                    { DevicePlatform.iOS, new[] { "public.movie", "public.video", "public.mpeg", "public.mpeg-4", "public.avi", "com.apple.quicktime-movie" } },
                    { DevicePlatform.WinUI, new[] { ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".mpg", ".mpeg", ".m4v", ".flv", ".3gp", ".mts", ".m2ts", ".ts", ".vob" } },
                    { DevicePlatform.Android, new[] { "video/*" } }
                })
            });

            if (results != null)
            {
                foreach (var file in results)
                {
                    AddVideoFile(file.FullPath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error picking videos: {ex.Message}");
        }
    }

    /// <summary>
    /// Añade un archivo de video a la lista de pendientes
    /// </summary>
    private void AddVideoFile(string filePath)
    {
        // Verificar si es un video válido
        var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (!VideoExtensions.Contains("." + extension))
        {
            System.Diagnostics.Debug.WriteLine($"Not a video file: {filePath}");
            return;
        }

        // Verificar si ya existe
        if (PendingVideos.Any(v => v.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Obtener información del archivo
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists) return;

        var pendingVideo = new PendingVideoItem
        {
            FileName = fileInfo.Name,
            FullPath = filePath,
            Extension = extension,
            FileSize = fileInfo.Length
        };

        PendingVideos.Add(pendingVideo);
        UpdatePendingVideosState();
        UpdateFolderFileAddedState(filePath, true);
    }

    /// <summary>
    /// Elimina un video de la lista de pendientes
    /// </summary>
    private void RemoveVideo(PendingVideoItem video)
    {
        PendingVideos.Remove(video);
        UpdatePendingVideosState();
        UpdateFolderFileAddedState(video.FullPath, false);
    }

    /// <summary>
    /// Limpia todos los videos pendientes
    /// </summary>
    private void ClearAllVideos()
    {
        // Desmarcar todos los archivos en FolderFiles
        foreach (var file in FolderFiles)
        {
            file.IsAddedToSession = false;
        }
        
        PendingVideos.Clear();
        UpdatePendingVideosState();
    }

    private void CancelImport()
    {
        if (!IsImporting)
        {
            return;
        }

        ImportProgressText = "Cancelando importación...";
        _importCts?.Cancel();
    }

    /// <summary>
    /// Actualiza el estado IsAddedToSession de un archivo en FolderFiles
    /// </summary>
    private void UpdateFolderFileAddedState(string filePath, bool isAdded)
    {
        var folderFile = FolderFiles.FirstOrDefault(f => 
            f.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        
        if (folderFile != null)
        {
            folderFile.IsAddedToSession = isAdded;
        }
    }

    /// <summary>
    /// Actualiza el estado de los comandos relacionados con videos pendientes
    /// </summary>
    private void UpdatePendingVideosState()
    {
        OnPropertyChanged(nameof(PendingVideosInfo));
        OnPropertyChanged(nameof(CanCreateCustomSession));
        ((Command)ClearAllVideosCommand).ChangeCanExecute();
        ((Command)CreateCustomSessionCommand).ChangeCanExecute();
    }

    /// <summary>
    /// Crea una sesión customizada con los videos pendientes
    /// </summary>
    private async Task CreateCustomSessionAsync()
    {
        if (!CanCreateCustomSession) return;

        var successCount = 0;
        var failedVideos = new List<string>();
        var totalVideos = PendingVideos.Count;

        // Iniciar tarea en el servicio de progreso (para que continúe en segundo plano)
        var importTask = _importProgressService.StartImport(
            $"Sesión: {CustomSessionName}",
            totalVideos);

        try
        {
            IsImporting = true;
            ImportProgressText = "Creando sesión customizada...";
            ImportProgressValue = 0;

            _importCts?.Cancel();
            _importCts?.Dispose();
            _importCts = new CancellationTokenSource();
            var cancellationToken = _importCts.Token;

            // Crear directorio para la sesión
            var sessionDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrownRFEP",
                "CustomSessions",
                $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
            
            Directory.CreateDirectory(sessionDir);
            
            // Crear directorio para videos y thumbnails
            var videosDir = Path.Combine(sessionDir, "videos");
            var thumbnailsDir = Path.Combine(sessionDir, "thumbnails");
            Directory.CreateDirectory(videosDir);
            Directory.CreateDirectory(thumbnailsDir);

            ImportProgressValue = 5;

            // Crear la sesión
            var session = new Session
            {
                NombreSesion = CustomSessionName,
                Lugar = CustomSessionLocation,
                Fecha = new DateTimeOffset(CustomSessionDate).ToUnixTimeSeconds(),
                TipoSesion = "Custom",
                IsMerged = 0,
                PathSesion = sessionDir
            };

            await _databaseService.SaveSessionAsync(session);
            ImportProgressValue = 10;
            ImportProgressText = "Sesión creada, copiando videos...";

            // Procesar los videos
            var currentVideo = 0;

            foreach (var pendingVideo in PendingVideos.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                currentVideo++;
                
                // Actualizar progreso en el servicio
                _importProgressService.UpdateProgress(currentVideo, pendingVideo.FileName);
                
                // Calcular progreso: 10-60% para copiar videos, 60-90% para thumbnails
                var copyProgress = 10 + (int)((currentVideo / (double)totalVideos) * 50);
                ImportProgressValue = copyProgress;
                ImportProgressText = $"Copiando video {currentVideo}/{totalVideos}: {pendingVideo.FileName}";

                // Generar nombre estandarizado: CROWN{número}.{extensión}
                var videoExtension = Path.GetExtension(pendingVideo.FullPath).ToLowerInvariant();
                var standardVideoName = $"CROWN{currentVideo}{videoExtension}";
                var localVideoPath = Path.Combine(videosDir, standardVideoName);

                // Verificar que el archivo origen existe y tiene tamaño válido.
                // En Windows, archivos "en la nube" (iCloud/OneDrive) pueden reportar 0 bytes mientras no estén hidratados.
                long expectedSize = 0;
                var sourceReady = false;

                for (var probeAttempt = 1; probeAttempt <= 2 && !sourceReady; probeAttempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var sourceInfo = new FileInfo(pendingVideo.FullPath);
                        if (!sourceInfo.Exists)
                        {
                            break;
                        }

                        expectedSize = sourceInfo.Length;
                        if (expectedSize > 0)
                        {
                            sourceReady = true;
                            break;
                        }

                        if (OperatingSystem.IsWindows())
                        {
                            var resolved = await TryResolveCloudFileAsync(pendingVideo.FullPath, pendingVideo.FileName, copyProgress, cancellationToken);
                            if (resolved == CloudFileResolution.CancelImport)
                            {
                                throw new OperationCanceledException("Importación cancelada por el usuario (archivo en la nube no disponible)");
                            }

                            if (resolved == CloudFileResolution.RetryNow)
                            {
                                continue;
                            }
                        }

                        break;
                    }
                    catch (IOException ioEx) when (IsCloudProviderNotRunning(ioEx))
                    {
                        var resolved = await TryResolveCloudFileAsync(pendingVideo.FullPath, pendingVideo.FileName, copyProgress, cancellationToken);
                        if (resolved == CloudFileResolution.CancelImport)
                        {
                            throw new OperationCanceledException("Importación cancelada por el usuario (archivo en la nube no disponible)");
                        }

                        if (resolved == CloudFileResolution.RetryNow)
                        {
                            continue;
                        }

                        break;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        break;
                    }
                }

                if (!sourceReady)
                {
                    System.Diagnostics.Debug.WriteLine($"Source file missing/not hydrated: {pendingVideo.FullPath}");
                    failedVideos.Add($"{pendingVideo.FileName} (archivo origen no encontrado / no descargado)");
                    continue;
                }
                var copySuccess = false;
                string? copyFailureDetail = null;

                // Copiar el video a la carpeta de la sesión con reintentos
                for (int attempt = 1; attempt <= 3 && !copySuccess; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        ImportProgressText = $"Copiando video {currentVideo}/{totalVideos}: {pendingVideo.FileName}" + 
                            (attempt > 1 ? $" (intento {attempt})" : "");

                        await CopyVideoFileAtomicAsync(pendingVideo.FullPath, localVideoPath, cancellationToken);

                        // Verificar que el archivo se copió correctamente
                        var destInfo = new FileInfo(localVideoPath);
                        if (destInfo.Exists && destInfo.Length == expectedSize)
                        {
                            copySuccess = true;
                            System.Diagnostics.Debug.WriteLine($"Video copied successfully: {pendingVideo.FullPath} -> {localVideoPath} ({destInfo.Length} bytes)");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Copy verification failed: expected {expectedSize} bytes, got {(destInfo.Exists ? destInfo.Length : 0)} bytes");
                            if (attempt < 3)
                                await Task.Delay(500, cancellationToken); // Esperar antes de reintentar
                        }
                    }
                    catch (IOException ioEx) when (IsCloudProviderNotRunning(ioEx))
                    {
                        System.Diagnostics.Debug.WriteLine($"Error copying video (attempt {attempt}) [CloudProviderNotRunning 0x{ioEx.HResult:X8}]: {ioEx.Message}");

                        var resolved = await TryResolveCloudFileAsync(pendingVideo.FullPath, pendingVideo.FileName, copyProgress, cancellationToken);
                        if (resolved == CloudFileResolution.CancelImport)
                        {
                            throw new OperationCanceledException("Importación cancelada por el usuario (archivo en la nube no disponible)");
                        }

                        if (resolved == CloudFileResolution.RetryNow)
                        {
                            // Reintentar (consumir intento normal para evitar bucle infinito)
                            continue;
                        }

                        copyFailureDetail = "Archivo en la nube no descargado.";
                        break;
                    }
                    catch (IOException ioEx)
                    {
                        copyFailureDetail = ioEx.Message;
                        System.Diagnostics.Debug.WriteLine($"Error copying video (attempt {attempt}) [IOException 0x{ioEx.HResult:X8}]: {ioEx.Message}");
                        if (attempt < 3)
                            await Task.Delay(500, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        copyFailureDetail = ex.Message;
                        System.Diagnostics.Debug.WriteLine($"Error copying video (attempt {attempt}): {ex.Message}");
                        if (attempt < 3)
                            await Task.Delay(500, cancellationToken);
                    }
                }

                if (!copySuccess)
                {
                    failedVideos.Add($"{pendingVideo.FileName} ({copyFailureDetail ?? "error al copiar"})");
                    continue;
                }

                // Generar nombre único para thumbnail con estándar CROWN
                var thumbnailFileName = $"CROWN{currentVideo}_thumb.jpg";
                var thumbnailPath = Path.Combine(thumbnailsDir, thumbnailFileName);

                // Generar thumbnail desde el video copiado
                var thumbProgress = 60 + (int)((currentVideo / (double)totalVideos) * 30);
                ImportProgressValue = thumbProgress;
                ImportProgressText = $"Generando miniatura {currentVideo}/{totalVideos}...";
                var thumbnailGenerated = await _thumbnailService.GenerateThumbnailAsync(localVideoPath, thumbnailPath);

                // Obtener duración del video
                var duration = await _thumbnailService.GetVideoDurationAsync(localVideoPath);

                // Obtener tamaño del archivo copiado
                var fileSize = new FileInfo(localVideoPath).Length;

                // Crear el video clip con todos los datos
                var videoClip = new VideoClip
                {
                    SessionId = session.Id,
                    ClipPath = standardVideoName,               // Nombre estandarizado (relativo)
                    LocalClipPath = localVideoPath,             // Path completo del video copiado
                    ThumbnailPath = thumbnailFileName,          // Nombre del archivo de thumbnail
                    LocalThumbnailPath = thumbnailGenerated ? thumbnailPath : null, // Path completo del thumbnail
                    ClipDuration = duration,
                    ClipSize = fileSize,
                    CreationDate = DateTimeOffset.Now.ToUnixTimeSeconds(),
                    AtletaId = 0, // Sin atleta asignado
                    Section = currentVideo
                };

                await _databaseService.SaveVideoClipAsync(videoClip);
                successCount++;

                System.Diagnostics.Debug.WriteLine($"Video clip saved: {videoClip.ClipPath}, LocalPath: {videoClip.LocalClipPath}, Thumbnail: {videoClip.LocalThumbnailPath ?? "No generado"}");
            }

            ImportProgressValue = 95;
            ImportProgressText = "Finalizando...";

            // Actualizar la sesión con el path definitivo
            session.PathSesion = sessionDir;
            await _databaseService.SaveSessionAsync(session);

            ImportProgressValue = 100;

            // Notificar al servicio que la importación terminó
            _importProgressService.CompleteImport(session.Id);

            // Mostrar resumen
            if (failedVideos.Count == 0)
            {
                ImportProgressText = $"¡Sesión '{CustomSessionName}' creada con {successCount} videos!";
            }
            else
            {
                ImportProgressText = $"Sesión creada: {successCount} videos OK, {failedVideos.Count} fallidos";
                System.Diagnostics.Debug.WriteLine($"Failed videos: {string.Join(", ", failedVideos)}");
                
                // Mostrar alerta con los videos fallidos
                await Shell.Current.DisplayAlert(
                    "Importación completada con errores",
                    $"Se importaron {successCount} de {totalVideos} videos.\n\nVideos fallidos:\n• " + string.Join("\n• ", failedVideos),
                    "OK");
            }

            // Limpiar estado después de mostrar el mensaje de éxito
            await Task.Delay(failedVideos.Count > 0 ? 1000 : 2500);
            ClearAllVideos();
            CustomSessionName = "";
            CustomSessionLocation = "";
            CustomSessionDate = DateTime.Today;
            ImportProgressText = "";
            ImportProgressValue = 0;
        }
        catch (OperationCanceledException)
        {
            _importProgressService.FailImport("Cancelado por el usuario");
            ImportProgressText = "Importación cancelada.";
            await Task.Delay(1000);
            ImportProgressText = "";
        }
        catch (Exception ex)
        {
            _importProgressService.FailImport(ex.Message);
            ImportProgressText = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Error creating custom session: {ex}");
            await Shell.Current.DisplayAlert("Error", $"Error al crear la sesión: {ex.Message}", "OK");
            await Task.Delay(1000);
            ImportProgressText = "";
        }
        finally
        {
            IsImporting = false;

            _importCts?.Dispose();
            _importCts = null;
        }
    }

    private static async Task CopyVideoFileAtomicAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        // Temp único por intento: evita colisiones/locks con .tmp fijo (iCloud/AV/Indexador)
        var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            const int bufferSize = 1024 * 1024;

            // Copiar con streams async + sequential scan.
            // Nota: FileShare.* es cross-platform; en Windows ayuda cuando el origen está "en uso".
            await using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destStream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
                await destStream.FlushAsync(cancellationToken);
            }

            // Reemplazo (mismo directorio) para evitar archivos parciales.
            // Pequeño retry: en Windows, AV/indexadores pueden enganchar el archivo justo al cerrar.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, destinationPath, true);
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    await Task.Delay(200 * attempt, cancellationToken);
                }
            }
        }
        finally
        {
            // Si el Move falló, intentar limpiar el temp
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort */ }
            }
        }
    }

    private static bool IsCloudProviderNotRunning(IOException ex)
    {
        // Windows HRESULT: 0x8007016A = "The cloud file provider is not running."
        const int HRESULT_CLOUD_FILE_PROVIDER_NOT_RUNNING = unchecked((int)0x8007016A);

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (ex.HResult == HRESULT_CLOUD_FILE_PROVIDER_NOT_RUNNING)
        {
            return true;
        }

        // Fallback por si el HRESULT no llega bien (localización del mensaje)
        var message = ex.Message ?? string.Empty;
        return message.Contains("cloud file provider", StringComparison.OrdinalIgnoreCase)
               || message.Contains("proveedor de archivos de nube", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCloudProviderNotRunning(Exception ex)
    {
        if (ex is IOException ioEx)
        {
            return IsCloudProviderNotRunning(ioEx);
        }

        const int HRESULT_CLOUD_FILE_PROVIDER_NOT_RUNNING = unchecked((int)0x8007016A);
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (ex.HResult == HRESULT_CLOUD_FILE_PROVIDER_NOT_RUNNING)
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("cloud file provider", StringComparison.OrdinalIgnoreCase)
               || message.Contains("proveedor de archivos de nube", StringComparison.OrdinalIgnoreCase);
    }

    private enum CloudFileResolution
    {
        SkipVideo = 0,
        RetryNow = 1,
        CancelImport = 2,
    }

    private async Task<CloudFileResolution> TryResolveCloudFileAsync(string sourcePath, string displayName)
    {
        return await TryResolveCloudFileAsync(sourcePath, displayName, baseProgressValue: ImportProgressValue, CancellationToken.None);
    }

    private async Task<CloudFileResolution> TryResolveCloudFileAsync(string sourcePath, string displayName, int baseProgressValue, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return CloudFileResolution.SkipVideo;
        }

        // Primero intentar espera corta (30 segundos) por si el archivo se está descargando
        try
        {
            var becameAvailable = await WaitForLocalFileAccessAsync(
                sourcePath,
                displayName,
                timeout: TimeSpan.FromSeconds(30),
                baseProgressValue,
                cancellationToken);

            if (becameAvailable)
            {
                return CloudFileResolution.RetryNow;
            }
        }
        catch (OperationCanceledException)
        {
            return CloudFileResolution.CancelImport;
        }

        // El archivo no se descargó automáticamente - preguntar al usuario
        return await ShowCloudFileDialogAsync(sourcePath, displayName, baseProgressValue, cancellationToken);
    }

    private async Task<CloudFileResolution> ShowCloudFileDialogAsync(string sourcePath, string displayName, int baseProgressValue, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderPath = Path.GetDirectoryName(sourcePath) ?? sourcePath;
            
            // Mostrar diálogo con opciones
            var result = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                return await Shell.Current.DisplayActionSheet(
                    $"El archivo '{displayName}' está en la nube y no se puede descargar automáticamente.\n\nAbre la app de iCloud/OneDrive/Dropbox, descarga el archivo manualmente, y pulsa 'Reintentar'.",
                    "Cancelar importación",
                    null,
                    "Abrir ubicación en Explorador",
                    "Reintentar",
                    "Omitir este archivo");
            });

            if (result == "Cancelar importación")
            {
                return CloudFileResolution.CancelImport;
            }

            if (result == "Omitir este archivo")
            {
                return CloudFileResolution.SkipVideo;
            }

            if (result == "Abrir ubicación en Explorador")
            {
                try
                {
                    // Abrir Explorador de Windows en la carpeta del archivo
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{sourcePath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error opening explorer: {ex.Message}");
                }
                
                // Continuar el bucle para mostrar el diálogo de nuevo
                continue;
            }

            if (result == "Reintentar")
            {
                // Intentar acceder al archivo de nuevo
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ImportProgressText = $"Verificando disponibilidad: {displayName}";
                    ImportProgressValue = baseProgressValue;
                });

                try
                {
                    var becameAvailable = await WaitForLocalFileAccessAsync(
                        sourcePath,
                        displayName,
                        timeout: TimeSpan.FromSeconds(10),
                        baseProgressValue,
                        cancellationToken);

                    if (becameAvailable)
                    {
                        return CloudFileResolution.RetryNow;
                    }
                }
                catch (OperationCanceledException)
                {
                    return CloudFileResolution.CancelImport;
                }

                // Sigue sin estar disponible - volver a mostrar diálogo
                continue;
            }

            // Cualquier otra respuesta (cerrar diálogo, etc.) = omitir
            return CloudFileResolution.SkipVideo;
        }
    }

    private async Task<bool> WaitForLocalFileAccessAsync(string filePath, string displayName, TimeSpan timeout, int baseProgressValue, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;

        var informedAboutProvider = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsed = DateTime.UtcNow - start;
            if (elapsed >= timeout)
            {
                return false;
            }

            var seconds = (int)elapsed.TotalSeconds;
            var ratio = Math.Clamp(elapsed.TotalMilliseconds / Math.Max(1, timeout.TotalMilliseconds), 0, 1);
            var bump = (int)Math.Round(ratio * 5);
            var progress = Math.Min(99, baseProgressValue + bump);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ImportProgressText = $"Esperando descarga ({seconds}s): {displayName}";
                ImportProgressValue = progress;
            });

            // Intentar forzar hidratación/descarga en Windows (iCloud/OneDrive) antes de abrir con FileStream.
            // Esto ayuda cuando el proveedor sólo descarga bajo demanda.
            if (OperatingSystem.IsWindows() && (seconds == 0 || seconds % 3 == 0))
            {
                try
                {
                    _ = await TryHydrateCloudFileAsync(filePath, cancellationToken);
                }
                catch (Exception ex) when (IsCloudProviderNotRunning(ex))
                {
                    if (!informedAboutProvider)
                    {
                        informedAboutProvider = true;
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            ImportProgressText = $"Proveedor de nube no activo. Abre iCloud/OneDrive y espera: {displayName}";
                        });
                    }
                }
                catch
                {
                    // best-effort
                }
            }

            try
            {
                await using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                // Si el proveedor devuelve un placeholder "vacío", no seguimos todavía.
                // (En iCloud/OneDrive, el tamaño puede ser 0 hasta que se descargue.)
                try
                {
                    var length = stream.Length;
                    if (length > 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignore; se reintentará
                }

                try
                {
                    var info = new FileInfo(filePath);
                    if (info.Exists && info.Length > 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignore; se reintentará
                }

                await Task.Delay(750, cancellationToken);
            }
            catch (IOException ioEx) when (IsCloudProviderNotRunning(ioEx))
            {
                if (!informedAboutProvider)
                {
                    informedAboutProvider = true;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        ImportProgressText = $"Proveedor de nube no activo. Abre iCloud/OneDrive y espera: {displayName}";
                    });
                }

                await Task.Delay(750, cancellationToken);
            }
            catch (IOException)
            {
                await Task.Delay(750, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(750, cancellationToken);
            }
        }
    }

    private static async Task<bool> TryHydrateCloudFileAsync(string filePath, CancellationToken cancellationToken)
    {
#if WINDOWS
        try
        {
            // StorageFile suele activar la descarga/hidratación para placeholders en la nube.
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenReadAsync();

            // Forzar una lectura mínima para disparar la descarga real.
            // (Algunos proveedores no hidratan sólo con "abrir".)
            var input = stream.GetInputStreamAt(0);
            var buffer = new global::Windows.Storage.Streams.Buffer(1);
            _ = await input.ReadAsync(buffer, 1, InputStreamOptions.None).AsTask(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    #endregion
}
