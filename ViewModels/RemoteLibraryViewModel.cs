using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace CrownRFEP_Reader.ViewModels;

public class RemoteLibraryViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService;
    private readonly ICloudBackendService _cloudBackendService;
    private readonly SyncService? _syncService;
    private readonly HttpClient _remoteMetadataHttpClient = new();

    private Func<bool> _getIsRemoteLibraryVisible = () => false;
    private Action<bool> _setIsRemoteLibraryVisible = _ => { };
    private Action<bool> _setIsRemoteLibraryExpanded = _ => { };

    private Func<bool> _getIsRemoteAllGallerySelected = () => false;
    private Action<bool> _setIsRemoteAllGallerySelected = _ => { };
    private Func<bool> _getIsRemoteVideoLessonsSelected = () => false;
    private Action<bool> _setIsRemoteVideoLessonsSelected = _ => { };
    private Func<bool> _getIsRemoteTrashSelected = () => false;
    private Action<bool> _setIsRemoteTrashSelected = _ => { };

    private Func<bool> _getIsAllGallerySelected = () => false;
    private Func<bool> _getIsVideoLessonsSelected = () => false;
    private Func<bool> _getIsDiaryViewSelected = () => false;
    private Func<bool> _getIsFavoriteVideosSelected = () => false;
    private Func<bool> _getIsFavoriteSessionsSelected = () => false;

    private Func<Session?> _getSelectedSession = () => null;
    private Action<Session?> _setSelectedSession = _ => { };
    private Action _clearLocalSelection = () => { };
    private Action _notifyShowVideoGalleryChanged = () => { };
    private Action _notifySelectedSessionTitleChanged = () => { };

    private Func<Task> _loadAllVideosAsync = () => Task.CompletedTask;
    private Func<Session?, Task> _loadSelectedSessionVideosAsync = _ => Task.CompletedTask;
    private Func<List<VideoClip>?> _getAllVideosCache = () => null;
    private Action<VideoClip> _insertAllVideosCache = _ => { };
    private Func<VideoClip, Task> _refreshVideoClipInGalleryAsync = _ => Task.CompletedTask;
    private Func<string, Task> _ensurePlaceExistsAsync = _ => Task.CompletedTask;
    private Func<VideoClip, Task> _playSelectedVideoAsync = _ => Task.CompletedTask;
    private Action _notifySelectedSessionVideosChanged = () => { };

    private Func<Session, Task> _ensureSessionVisibleAsync = _ => Task.CompletedTask;
    private Func<IReadOnlyList<Session>> _getRecentSessions = () => Array.Empty<Session>();
    private Action<Session> _replaceRecentSession = _ => { };
    private Action<int> _removeRecentSession = _ => { };
    private Action _syncVisibleSessionRows = () => { };

    private Func<VideoClip, Task> _removeLocalVideoClipAsync = _ => Task.CompletedTask;
    private Func<int, Task> _removeLocalSessionIfEmptyAsync = _ => Task.CompletedTask;

    private string _remoteLibraryDisplayName = "Organización";
    private string _remoteAllGalleryItemCount = "—";
    private string _remoteVideoLessonsCount = "—";
    private string _remoteTrashItemCount = "—";
    private ObservableCollection<SmartFolderDefinition> _remoteSmartFolders = new();
    private ObservableCollection<RemoteSessionListItem> _remoteSessions = new();
    private int _selectedRemoteSessionId;

    private ObservableCollection<RemoteVideoItem> _remoteVideos = new();
    private bool _isLoadingRemoteVideos;
    private List<CloudFileInfo>? _remoteFilesCache;

    private bool _isLoginRequired;
    private bool _isCloudLoginBusy;
    private string _cloudLoginEmail = "";
    private string _cloudLoginPassword = "";
    private string _cloudLoginStatusMessage = "";

    private bool _isRemoteMultiSelectMode;
    private bool _isRemoteSelectAllActive;

    private bool _isBusy;

    private bool _isSyncing;
    private int _syncProgress;
    private string _syncStatusText = "Sincronización cloud";
    private int _pendingSyncCount;

    public RemoteLibraryViewModel(
        DatabaseService databaseService,
        ICloudBackendService cloudBackendService,
        SyncService? syncService = null)
    {
        _databaseService = databaseService;
        _cloudBackendService = cloudBackendService;
        _syncService = syncService;

        ConnectNasCommand = new AsyncRelayCommand(ConnectSynologyNasAsync);
        RemoteSelectAllGalleryCommand = new AsyncRelayCommand(() => HandleRemoteSectionSelectedAsync("Galería General"));
        RemoteViewVideoLessonsCommand = new AsyncRelayCommand(() => HandleRemoteSectionSelectedAsync("Videolecciones"));
        RemoteViewTrashCommand = new AsyncRelayCommand(() => HandleRemoteSectionSelectedAsync("Papelera"));
        SelectRemoteSessionCommand = new AsyncRelayCommand<RemoteSessionListItem>(SelectRemoteSessionAsync);

        CloudLoginCommand = new AsyncRelayCommand(CloudLoginAsync);
        SyncAllCommand = new AsyncRelayCommand(SyncAllVideosAsync);
        SyncSessionCommand = new AsyncRelayCommand<Session>(SyncSessionAsync);
        UploadVideoCommand = new AsyncRelayCommand<VideoClip>(UploadVideoAsync);
        DownloadVideoCommand = new AsyncRelayCommand<VideoClip>(DownloadVideoAsync);
        CheckPendingSyncCommand = new AsyncRelayCommand(CheckPendingSyncAsync);

        AddRemoteVideoToLibraryCommand = new AsyncRelayCommand<RemoteVideoItem>(AddRemoteVideoToLibraryAsync);
        AddRemoteSessionToLibraryCommand = new AsyncRelayCommand<int>(AddRemoteSessionToLibraryAsync);
        DeleteRemoteVideoFromLibraryCommand = new AsyncRelayCommand<RemoteVideoItem>(DeleteRemoteVideoFromLibraryAsync);
        DeleteAllRemoteVideosFromLibraryCommand = new AsyncRelayCommand(DeleteAllRemoteVideosFromLibraryAsync);
        DownloadRemoteVideoCommand = new AsyncRelayCommand<RemoteVideoItem>(DownloadRemoteVideoAsync);
        PlayRemoteVideoCommand = new AsyncRelayCommand<RemoteVideoItem>(PlayRemoteVideoAsync);
        DeleteRemoteSessionFromCloudCommand = new AsyncRelayCommand<int>(DeleteRemoteSessionFromCloudAsync);

        ToggleRemoteMultiSelectModeCommand = new RelayCommand(ToggleRemoteMultiSelectMode);
        ToggleRemoteSelectAllCommand = new RelayCommand(ToggleRemoteSelectAll);
        ToggleRemoteVideoSelectionCommand = new RelayCommand<RemoteVideoItem>(ToggleRemoteVideoSelection);
        DownloadSelectedRemoteVideosCommand = new AsyncRelayCommand(DownloadSelectedRemoteVideosAsync);
        AddSelectedRemoteToLibraryCommand = new AsyncRelayCommand(AddSelectedRemoteToLibraryAsync);
        DeleteSelectedRemoteFromCloudCommand = new AsyncRelayCommand(DeleteSelectedRemoteFromCloudAsync);
        DeleteSelectedRemoteFromLibraryCommand = new AsyncRelayCommand(DeleteSelectedRemoteFromLibraryAsync);
    }

    public void Configure(
        Func<bool> getIsRemoteLibraryVisible,
        Action<bool> setIsRemoteLibraryVisible,
        Action<bool> setIsRemoteLibraryExpanded,
        Func<bool> getIsRemoteAllGallerySelected,
        Action<bool> setIsRemoteAllGallerySelected,
        Func<bool> getIsRemoteVideoLessonsSelected,
        Action<bool> setIsRemoteVideoLessonsSelected,
        Func<bool> getIsRemoteTrashSelected,
        Action<bool> setIsRemoteTrashSelected,
        Func<bool> getIsAllGallerySelected,
        Func<bool> getIsVideoLessonsSelected,
        Func<bool> getIsDiaryViewSelected,
        Func<bool> getIsFavoriteVideosSelected,
        Func<bool> getIsFavoriteSessionsSelected,
        Func<Session?> getSelectedSession,
        Action<Session?> setSelectedSession,
        Action clearLocalSelection,
        Action notifyShowVideoGalleryChanged,
        Action notifySelectedSessionTitleChanged,
        Func<Task> loadAllVideosAsync,
        Func<Session?, Task> loadSelectedSessionVideosAsync,
        Func<List<VideoClip>?> getAllVideosCache,
        Action<VideoClip> insertAllVideosCache,
        Func<VideoClip, Task> refreshVideoClipInGalleryAsync,
        Func<Session, Task> ensureSessionVisibleAsync,
        Func<IReadOnlyList<Session>> getRecentSessions,
        Action<Session> replaceRecentSession,
        Action<int> removeRecentSession,
        Action syncVisibleSessionRows,
        Func<VideoClip, Task> removeLocalVideoClipAsync,
        Func<int, Task> removeLocalSessionIfEmptyAsync,
        Func<string, Task> ensurePlaceExistsAsync,
        Func<VideoClip, Task> playSelectedVideoAsync,
        Action notifySelectedSessionVideosChanged)
    {
        _getIsRemoteLibraryVisible = getIsRemoteLibraryVisible;
        _setIsRemoteLibraryVisible = setIsRemoteLibraryVisible;
        _setIsRemoteLibraryExpanded = setIsRemoteLibraryExpanded;

        _getIsRemoteAllGallerySelected = getIsRemoteAllGallerySelected;
        _setIsRemoteAllGallerySelected = setIsRemoteAllGallerySelected;
        _getIsRemoteVideoLessonsSelected = getIsRemoteVideoLessonsSelected;
        _setIsRemoteVideoLessonsSelected = setIsRemoteVideoLessonsSelected;
        _getIsRemoteTrashSelected = getIsRemoteTrashSelected;
        _setIsRemoteTrashSelected = setIsRemoteTrashSelected;

        _getIsAllGallerySelected = getIsAllGallerySelected;
        _getIsVideoLessonsSelected = getIsVideoLessonsSelected;
        _getIsDiaryViewSelected = getIsDiaryViewSelected;
        _getIsFavoriteVideosSelected = getIsFavoriteVideosSelected;
        _getIsFavoriteSessionsSelected = getIsFavoriteSessionsSelected;

        _getSelectedSession = getSelectedSession;
        _setSelectedSession = setSelectedSession;
        _clearLocalSelection = clearLocalSelection;
        _notifyShowVideoGalleryChanged = notifyShowVideoGalleryChanged;
        _notifySelectedSessionTitleChanged = notifySelectedSessionTitleChanged;

        _loadAllVideosAsync = loadAllVideosAsync;
        _loadSelectedSessionVideosAsync = loadSelectedSessionVideosAsync;
        _getAllVideosCache = getAllVideosCache;
        _insertAllVideosCache = insertAllVideosCache;
        _refreshVideoClipInGalleryAsync = refreshVideoClipInGalleryAsync;

        _ensureSessionVisibleAsync = ensureSessionVisibleAsync;
        _getRecentSessions = getRecentSessions;
        _replaceRecentSession = replaceRecentSession;
        _removeRecentSession = removeRecentSession;
        _syncVisibleSessionRows = syncVisibleSessionRows;

        _removeLocalVideoClipAsync = removeLocalVideoClipAsync;
        _removeLocalSessionIfEmptyAsync = removeLocalSessionIfEmptyAsync;
        _ensurePlaceExistsAsync = ensurePlaceExistsAsync;
        _playSelectedVideoAsync = playSelectedVideoAsync;
        _notifySelectedSessionVideosChanged = notifySelectedSessionVideosChanged;
    }

    public string RemoteLibraryDisplayName
    {
        get => _remoteLibraryDisplayName;
        set => SetProperty(ref _remoteLibraryDisplayName, value);
    }

    public string RemoteAllGalleryItemCount
    {
        get => _remoteAllGalleryItemCount;
        set => SetProperty(ref _remoteAllGalleryItemCount, value);
    }

    public string RemoteVideoLessonsCount
    {
        get => _remoteVideoLessonsCount;
        set => SetProperty(ref _remoteVideoLessonsCount, value);
    }

    public string RemoteTrashItemCount
    {
        get => _remoteTrashItemCount;
        set => SetProperty(ref _remoteTrashItemCount, value);
    }

    public ObservableCollection<SmartFolderDefinition> RemoteSmartFolders
    {
        get => _remoteSmartFolders;
        set => SetProperty(ref _remoteSmartFolders, value);
    }

    public ObservableCollection<RemoteSessionListItem> RemoteSessions
    {
        get => _remoteSessions;
        set => SetProperty(ref _remoteSessions, value);
    }

    public ObservableCollection<RemoteVideoItem> RemoteVideos
    {
        get => _remoteVideos;
        set => SetProperty(ref _remoteVideos, value);
    }

    public bool IsLoadingRemoteVideos
    {
        get => _isLoadingRemoteVideos;
        set => SetProperty(ref _isLoadingRemoteVideos, value);
    }

    public bool IsRemoteMultiSelectMode
    {
        get => _isRemoteMultiSelectMode;
        set
        {
            if (SetProperty(ref _isRemoteMultiSelectMode, value))
            {
                if (!value)
                {
                    ClearRemoteVideoSelection();
                }
                OnPropertyChanged(nameof(SelectedRemoteVideoCount));
            }
        }
    }

    public bool IsRemoteSelectAllActive
    {
        get => _isRemoteSelectAllActive;
        set => SetProperty(ref _isRemoteSelectAllActive, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public int SelectedRemoteVideoCount => RemoteVideos.Count(v => v.IsSelected);

    public int SelectedRemoteSessionId
    {
        get => _selectedRemoteSessionId;
        set
        {
            if (SetProperty(ref _selectedRemoteSessionId, value))
            {
                UpdateRemoteSessionSelectionStates(value);
                OnPropertyChanged(nameof(IsRemoteSessionSelected));
                OnPropertyChanged(nameof(RemoteGalleryItems));
                OnPropertyChanged(nameof(ShowRemoteGallery));
                OnPropertyChanged(nameof(IsAnyRemoteSectionSelected));
                _notifySelectedSessionTitleChanged();
            }
        }
    }

    public bool IsRemoteSessionSelected => SelectedRemoteSessionId > 0;

    public IEnumerable<RemoteVideoItem> RemoteGalleryItems => SelectedRemoteSessionId > 0
        ? RemoteVideos.Where(v => v.SessionId == SelectedRemoteSessionId)
        : RemoteVideos;

    public bool IsAnyRemoteSectionSelected => IsRemoteAllGallerySelected || IsRemoteVideoLessonsSelected || IsRemoteTrashSelected || IsRemoteSessionSelected;

    public bool ShowRemoteGallery => IsRemoteAllGallerySelected || IsRemoteSessionSelected;

    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    public int SyncProgress
    {
        get => _syncProgress;
        set => SetProperty(ref _syncProgress, value);
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        set => SetProperty(ref _syncStatusText, value);
    }

    public int PendingSyncCount
    {
        get => _pendingSyncCount;
        set => SetProperty(ref _pendingSyncCount, value);
    }

    public bool HasPendingSync => PendingSyncCount > 0;

    public bool IsCloudAuthenticated => _cloudBackendService?.IsAuthenticated ?? false;

    public bool IsLoginRequired
    {
        get => _isLoginRequired;
        set => SetProperty(ref _isLoginRequired, value);
    }

    public bool IsCloudLoginBusy
    {
        get => _isCloudLoginBusy;
        set => SetProperty(ref _isCloudLoginBusy, value);
    }

    public string CloudLoginEmail
    {
        get => _cloudLoginEmail;
        set => SetProperty(ref _cloudLoginEmail, value);
    }

    public string CloudLoginPassword
    {
        get => _cloudLoginPassword;
        set => SetProperty(ref _cloudLoginPassword, value);
    }

    public string CloudLoginStatusMessage
    {
        get => _cloudLoginStatusMessage;
        set => SetProperty(ref _cloudLoginStatusMessage, value);
    }

    public bool CanDeleteRemoteSessions => IsOrgWriteRole(_cloudBackendService.CurrentUserRole);

    public bool CanWriteRemoteLibrary => IsOrgWriteRole(_cloudBackendService.CurrentUserRole);

    public ICommand ConnectNasCommand { get; }
    public ICommand RemoteSelectAllGalleryCommand { get; }
    public ICommand RemoteViewVideoLessonsCommand { get; }
    public ICommand RemoteViewTrashCommand { get; }
    public ICommand SelectRemoteSessionCommand { get; }

    public ICommand CloudLoginCommand { get; }
    public ICommand SyncAllCommand { get; }
    public ICommand SyncSessionCommand { get; }
    public ICommand UploadVideoCommand { get; }
    public ICommand DownloadVideoCommand { get; }
    public ICommand CheckPendingSyncCommand { get; }

    public ICommand AddRemoteVideoToLibraryCommand { get; }
    public ICommand AddRemoteSessionToLibraryCommand { get; }
    public ICommand DeleteRemoteVideoFromLibraryCommand { get; }
    public ICommand DeleteAllRemoteVideosFromLibraryCommand { get; }
    public ICommand DownloadRemoteVideoCommand { get; }
    public ICommand PlayRemoteVideoCommand { get; }
    public ICommand DeleteRemoteSessionFromCloudCommand { get; }

    public ICommand ToggleRemoteMultiSelectModeCommand { get; }
    public ICommand ToggleRemoteSelectAllCommand { get; }
    public ICommand ToggleRemoteVideoSelectionCommand { get; }
    public ICommand DownloadSelectedRemoteVideosCommand { get; }
    public ICommand AddSelectedRemoteToLibraryCommand { get; }
    public ICommand DeleteSelectedRemoteFromCloudCommand { get; }
    public ICommand DeleteSelectedRemoteFromLibraryCommand { get; }

    public void ClearRemoteSections()
    {
        if (IsRemoteAllGallerySelected || IsRemoteVideoLessonsSelected || IsRemoteTrashSelected || IsRemoteSessionSelected)
        {
            IsRemoteAllGallerySelected = false;
            IsRemoteVideoLessonsSelected = false;
            IsRemoteTrashSelected = false;
            ClearRemoteSessionSelection();
            OnPropertyChanged(nameof(ShowRemoteGallery));
            OnPropertyChanged(nameof(IsAnyRemoteSectionSelected));
            _notifyShowVideoGalleryChanged();
            _notifySelectedSessionTitleChanged();
        }
    }

    public async Task RestoreCloudSessionAsync()
    {
        await CheckAndRestoreCloudSessionAsync();
    }

    public async Task RefreshPendingSyncAsync()
    {
        await CheckPendingSyncAsync();
    }

    private static bool IsOrgWriteRole(string? role)
        => string.Equals(role, "admin_org", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "org_admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "coach", StringComparison.OrdinalIgnoreCase);

    private bool IsRemoteLibraryVisible
    {
        get => _getIsRemoteLibraryVisible();
        set => _setIsRemoteLibraryVisible(value);
    }

    private bool IsRemoteAllGallerySelected
    {
        get => _getIsRemoteAllGallerySelected();
        set => _setIsRemoteAllGallerySelected(value);
    }

    private bool IsRemoteVideoLessonsSelected
    {
        get => _getIsRemoteVideoLessonsSelected();
        set => _setIsRemoteVideoLessonsSelected(value);
    }

    private bool IsRemoteTrashSelected
    {
        get => _getIsRemoteTrashSelected();
        set => _setIsRemoteTrashSelected(value);
    }

    private async Task CloudLoginAsync()
    {
        if (IsCloudLoginBusy)
            return;

        if (string.IsNullOrWhiteSpace(CloudLoginEmail) || string.IsNullOrWhiteSpace(CloudLoginPassword))
        {
            CloudLoginStatusMessage = "Introduce email y contraseña.";
            return;
        }

        try
        {
            IsCloudLoginBusy = true;
            CloudLoginStatusMessage = "Iniciando sesión...";

            var result = await _cloudBackendService.LoginAsync(CloudLoginEmail.Trim(), CloudLoginPassword);

            if (!result.Success)
            {
                CloudLoginStatusMessage = result.ErrorMessage ?? "No se pudo iniciar sesión";
                return;
            }

            RemoteLibraryDisplayName = result.TeamName ?? "Organización";
            IsRemoteLibraryVisible = true;
            IsLoginRequired = false;
            CloudLoginStatusMessage = $"Sesión iniciada: {result.UserName}";
            CloudLoginPassword = string.Empty;
            OnPropertyChanged(nameof(IsCloudAuthenticated));

            await LoadTeamFilesAsync();
        }
        catch (Exception ex)
        {
            CloudLoginStatusMessage = $"No se pudo conectar: {ex.Message}";
        }
        finally
        {
            IsCloudLoginBusy = false;
        }
    }

    private async Task CheckAndRestoreCloudSessionAsync()
    {
        try
        {
            if (_cloudBackendService.IsAuthenticated)
            {
                RemoteLibraryDisplayName = _cloudBackendService.TeamName ?? "Organización";
                IsRemoteLibraryVisible = true;
                IsLoginRequired = false;
                OnPropertyChanged(nameof(IsCloudAuthenticated));
                System.Diagnostics.Debug.WriteLine($"[CloudBackend] Sesión restaurada para {_cloudBackendService.CurrentUserName}");

                await LoadTeamFilesAsync();
            }
            else
            {
                IsRemoteLibraryVisible = false;
                IsLoginRequired = true;
                OnPropertyChanged(nameof(IsCloudAuthenticated));
                System.Diagnostics.Debug.WriteLine("[CloudBackend] No hay sesión activa");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error verificando sesión: {ex.Message}");
            IsRemoteLibraryVisible = false;
            IsLoginRequired = true;
        }
    }

    private async Task LoadTeamFilesAsync()
    {
        try
        {
            var result = await _cloudBackendService.ListFilesAsync();
            if (result.Success && result.Files != null)
            {
                RemoteAllGalleryItemCount = result.Files.Count(f => !f.IsFolder).ToString();
                System.Diagnostics.Debug.WriteLine($"[CloudBackend] Cargados {result.Files.Count} archivos del equipo");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error cargando archivos: {ex.Message}");
        }
    }

    private async Task ConnectSynologyNasAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        var email = await page.DisplayPromptAsync(
            "Iniciar sesión en Equipo",
            "Email:",
            "Continuar",
            "Cancelar",
            "",
            100,
            Keyboard.Email);

        if (string.IsNullOrWhiteSpace(email))
            return;

        var password = await page.DisplayPromptAsync(
            "Iniciar sesión en Equipo",
            "Contraseña:",
            "Iniciar sesión",
            "Cancelar",
            "",
            100,
            Keyboard.Default);

        if (string.IsNullOrWhiteSpace(password))
            return;

        try
        {
            IsBusy = true;

            var result = await _cloudBackendService.LoginAsync(email, password);

            if (!result.Success)
            {
                await page.DisplayAlert("Error", result.ErrorMessage ?? "No se pudo iniciar sesión", "OK");
                return;
            }

            RemoteLibraryDisplayName = result.TeamName ?? "Organización";
            IsRemoteLibraryVisible = true;
            IsLoginRequired = false;
            OnPropertyChanged(nameof(IsCloudAuthenticated));

            await page.DisplayAlert("Sesión iniciada",
                $"Bienvenido, {result.UserName}\nEquipo: {result.TeamName}",
                "OK");

            await LoadTeamFilesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CloudBackend] Error: {ex.Message}");
            await page.DisplayAlert("Error", $"No se pudo conectar: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task HandleRemoteSectionSelectedAsync(string sectionName)
    {
        SetRemoteSelection(sectionName);
        _clearLocalSelection();

        if (!_cloudBackendService.IsAuthenticated)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlert("No autenticado", "Inicia sesión para acceder a los archivos del equipo.", "OK");
            }
            return;
        }

        if (sectionName == "Galería General")
        {
            await LoadRemoteGalleryAsync();
        }
        else
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlert("Biblioteca de organización", $"La sección '{sectionName}' estará disponible próximamente.", "OK");
            }
        }
    }

    private async Task LoadRemoteGalleryAsync()
    {
        if (IsLoadingRemoteVideos) return;

        try
        {
            IsLoadingRemoteVideos = true;
            RemoteVideos.Clear();

            System.Diagnostics.Debug.WriteLine("[Remote] Cargando galería remota...");

            var result = await _cloudBackendService.ListFilesAsync("sessions/", maxItems: 1000);

            if (!result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Error: {result.ErrorMessage}");
                return;
            }

            var files = result.Files ?? new List<CloudFileInfo>();
            _remoteFilesCache = files;

            var remoteSessionMetadata = await LoadRemoteSessionMetadataAsync(files);

            var videoFiles = files
                .Where(f => !f.IsFolder && f.Key.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastModified)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[Remote] Encontrados {videoFiles.Count} videos");

            var localVideos = await _databaseService.GetAllVideoClipsAsync();
            var localVideosByRemotePath = localVideos
                .Where(v => !string.IsNullOrEmpty(v.ClipPath))
                .ToDictionary(v => v.ClipPath!, v => v, StringComparer.OrdinalIgnoreCase);

            var metadataFiles = files
                .Where(f => !f.IsFolder && f.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && f.Key.Contains("/metadata/", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(f => NormalizeCloudKey(f.Key), f => f, StringComparer.OrdinalIgnoreCase);

            var remotePaths = new HashSet<string>(
                videoFiles.Select(f => f.Key.StartsWith("CrownRFEP/", StringComparison.OrdinalIgnoreCase)
                    ? f.Key.Substring("CrownRFEP/".Length)
                    : f.Key),
                StringComparer.OrdinalIgnoreCase);

            var orphanedLocal = localVideos
                .Where(v => string.Equals(v.Source, "remote", StringComparison.OrdinalIgnoreCase))
                .Where(v => !string.IsNullOrEmpty(v.ClipPath) && !remotePaths.Contains(v.ClipPath!))
                .ToList();

            if (orphanedLocal.Count > 0)
            {
                var orphanSessionIds = new HashSet<int>();
                foreach (var local in orphanedLocal)
                {
                    orphanSessionIds.Add(local.SessionId);
                    await _removeLocalVideoClipAsync(local);
                }

                foreach (var sessionId in orphanSessionIds)
                    await _removeLocalSessionIfEmptyAsync(sessionId);
            }

            foreach (var file in videoFiles)
            {
                VideoClip? linkedLocal = null;
                var relativePath = file.Key;
                if (relativePath.StartsWith("CrownRFEP/"))
                {
                    relativePath = relativePath.Substring("CrownRFEP/".Length);
                }

                if (localVideosByRemotePath.TryGetValue(relativePath, out var local))
                {
                    linkedLocal = local;
                }

                var remoteItem = RemoteVideoItem.FromCloudFile(file, linkedLocal);
                RemoteVideos.Add(remoteItem);

                if (remoteItem.LinkedLocalVideo?.EffectiveThumbnailPath == null &&
                    remoteItem.SessionId > 0 && remoteItem.VideoId > 0)
                {
                    _ = LoadRemoteThumbnailAsync(remoteItem);
                }
            }

            await ApplyRemoteVideoMetadataAsync(files, remoteSessionMetadata);

            var localSessions = await _databaseService.GetAllSessionsAsync();
            var localSessionsById = localSessions.ToDictionary(s => s.Id, s => s);
            var remoteSessionItems = RemoteVideos
                .Where(v => v.SessionId > 0)
                .GroupBy(v => v.SessionId)
                .Select(group =>
                {
                    var sessionId = group.Key;
                    localSessionsById.TryGetValue(sessionId, out var localSession);
                    remoteSessionMetadata.TryGetValue(sessionId, out var metadata);

                    var title = metadata?.SessionName
                        ?? localSession?.DisplayName
                        ?? $"Sesión {sessionId}";
                    var place = metadata?.Place ?? localSession?.Lugar;
                    var sessionDate = ResolveRemoteSessionDate(metadata, localSession?.FechaDateTime ?? group.Max(v => v.LastModified));
                    var coach = metadata?.Coach ?? localSession?.Coach;
                    return new RemoteSessionListItem(
                        sessionId,
                        title,
                        place,
                        sessionDate,
                        coach,
                        group.Count(),
                        group.Max(v => v.LastModified));
                })
                .OrderByDescending(item => item.LastModified)
                .ToList();

            RemoteSessions = new ObservableCollection<RemoteSessionListItem>(remoteSessionItems);
            if (SelectedRemoteSessionId > 0 && !RemoteSessions.Any(s => s.SessionId == SelectedRemoteSessionId))
            {
                SelectedRemoteSessionId = 0;
            }
            else
            {
                UpdateRemoteSessionSelectionStates(SelectedRemoteSessionId);
            }

            await SyncRemoteChangesToPersonalLibraryAsync(remoteSessionMetadata, metadataFiles);

            RemoteAllGalleryItemCount = RemoteVideos.Count.ToString();
            OnPropertyChanged(nameof(RemoteGalleryItems));
            OnPropertyChanged(nameof(IsAnyRemoteSectionSelected));
            _notifySelectedSessionTitleChanged();

            System.Diagnostics.Debug.WriteLine($"[Remote] Galería cargada: {RemoteVideos.Count} videos");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error cargando galería: {ex.Message}");
        }
        finally
        {
            IsLoadingRemoteVideos = false;
        }
    }

    private async Task LoadRemoteThumbnailAsync(RemoteVideoItem remoteItem)
    {
        try
        {
            var remoteThumbPath = $"sessions/{remoteItem.SessionId}/thumbnails/{remoteItem.VideoId}.jpg";
            var signResult = await _cloudBackendService.GetDownloadUrlAsync(remoteThumbPath, expirationMinutes: 60);
            if (signResult.Success && !string.IsNullOrWhiteSpace(signResult.Url))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    remoteItem.ThumbnailUrl = signResult.Url;
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error obteniendo thumbnail: {ex.Message}");
        }
    }

    private async Task<Dictionary<int, RemoteSessionMetadata>> LoadRemoteSessionMetadataAsync(List<CloudFileInfo> files)
    {
        var result = new Dictionary<int, RemoteSessionMetadata>();

        var metadataFiles = files
            .Where(f => !f.IsFolder && f.Key.EndsWith("session.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (metadataFiles.Count == 0)
        {
            return result;
        }

        foreach (var file in metadataFiles)
        {
            var normalizedKey = NormalizeCloudKey(file.Key);
            if (!TryParseSessionIdFromMetadataKey(normalizedKey, out var sessionId))
            {
                continue;
            }

            if (result.ContainsKey(sessionId))
            {
                continue;
            }

            try
            {
                var signResult = await _cloudBackendService.GetDownloadUrlAsync(normalizedKey, expirationMinutes: 10);
                if (!signResult.Success || string.IsNullOrWhiteSpace(signResult.Url))
                {
                    continue;
                }

                var json = await _remoteMetadataHttpClient.GetStringAsync(signResult.Url);
                var metadata = JsonSerializer.Deserialize<RemoteSessionMetadata>(json);
                if (metadata != null)
                {
                    if (metadata.SessionId == 0)
                    {
                        metadata.SessionId = sessionId;
                    }

                    result[sessionId] = metadata;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Error leyendo metadatos de sesión {sessionId}: {ex.Message}");
            }
        }

        return result;
    }

    private async Task ApplyRemoteVideoMetadataAsync(List<CloudFileInfo> files, Dictionary<int, RemoteSessionMetadata> sessionMetadata)
    {
        var metadataFiles = files
            .Where(f => !f.IsFolder && f.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && f.Key.Contains("/metadata/", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => NormalizeCloudKey(f.Key), f => f);

        if (metadataFiles.Count == 0)
        {
            return;
        }

        foreach (var remoteItem in RemoteVideos)
        {
            if (remoteItem.SessionId <= 0 || remoteItem.VideoId <= 0)
            {
                continue;
            }

            var metadataKey = $"sessions/{remoteItem.SessionId}/metadata/{remoteItem.VideoId}.json";
            if (!metadataFiles.TryGetValue(metadataKey, out _))
            {
                continue;
            }

            try
            {
                var metadata = await LoadRemoteVideoMetadataAsync(metadataKey);
                if (metadata == null)
                {
                    continue;
                }

                ApplyRemoteVideoMetadata(remoteItem, metadata, sessionMetadata);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Error aplicando metadatos de video {remoteItem.VideoId}: {ex.Message}");
            }
        }
    }

    private async Task<VideoSyncData?> LoadRemoteVideoMetadataAsync(string normalizedKey)
    {
        var signResult = await _cloudBackendService.GetDownloadUrlAsync(normalizedKey, expirationMinutes: 10);
        if (!signResult.Success || string.IsNullOrWhiteSpace(signResult.Url))
        {
            return null;
        }

        var json = await _remoteMetadataHttpClient.GetStringAsync(signResult.Url);
        return JsonSerializer.Deserialize<VideoSyncData>(json);
    }

    private void ApplyRemoteVideoMetadata(RemoteVideoItem remoteItem, VideoSyncData metadata, Dictionary<int, RemoteSessionMetadata> sessionMetadata)
    {
        var sessionName = sessionMetadata.TryGetValue(remoteItem.SessionId, out var sessionInfo)
            ? (sessionInfo.SessionName ?? remoteItem.SessionName)
            : remoteItem.SessionName;

        remoteItem.SessionName = string.IsNullOrWhiteSpace(sessionName) ? remoteItem.SessionNameFallback : sessionName;

        if (metadata.Video?.Section > 0)
        {
            remoteItem.Section = metadata.Video.Section;
        }

        ApplyRemoteVideoTags(remoteItem, metadata);

        var athleteName = BuildAthleteDisplayName(metadata.Athlete);
        var displayName = BuildRemoteVideoDisplayName(metadata.Video?.ComparisonName, athleteName, remoteItem.SessionName, remoteItem.FileName);
        remoteItem.FileName = displayName;

        if (metadata.Video?.CreationDate > 0)
        {
            remoteItem.LastModified = DateTimeOffset.FromUnixTimeSeconds(metadata.Video.CreationDate).LocalDateTime;
        }

        if (metadata.Video?.ClipSize > 0)
        {
            remoteItem.Size = metadata.Video.ClipSize;
        }
    }

    private static void ApplyRemoteVideoTags(RemoteVideoItem remoteItem, VideoSyncData metadata)
    {
        if (metadata.Tags.Count == 0 && metadata.Inputs.Count == 0)
        {
            return;
        }

        var tagMap = metadata.Tags
            .Where(t => t.Id > 0)
            .GroupBy(t => t.Id)
            .ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty);

        var tags = metadata.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => new Tag { Id = t.Id, NombreTag = t.Name })
            .ToList();

        var eventGroups = metadata.Inputs
            .Where(i => i.IsEvent == 1 && i.InputTypeId > 0)
            .GroupBy(i => i.InputTypeId)
            .ToList();

        var eventTags = new List<Tag>();
        foreach (var group in eventGroups)
        {
            var name = tagMap.TryGetValue(group.Key, out var tagName)
                ? tagName
                : group.Select(i => i.InputValue).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? $"Tag {group.Key}";
            eventTags.Add(new Tag
            {
                Id = group.Key,
                NombreTag = name,
                IsEventTag = true,
                EventCount = group.Count()
            });
        }

        remoteItem.Tags = tags.Count > 0 ? tags : null;
        remoteItem.EventTags = eventTags.Count > 0 ? eventTags : null;
    }

    private static string BuildAthleteDisplayName(AthleteSyncData? athlete)
    {
        if (athlete == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(athlete.Apellido))
        {
            var apellido = athlete.Apellido!.ToUpperInvariant();
            var nombre = athlete.Nombre ?? "";
            return $"{apellido} {nombre}".Trim();
        }

        return athlete.Nombre ?? string.Empty;
    }

    private static string BuildRemoteVideoDisplayName(string? comparisonName, string athleteName, string sessionName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(comparisonName))
        {
            return comparisonName;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(athleteName))
        {
            parts.Add(athleteName);
        }

        if (!string.IsNullOrWhiteSpace(sessionName))
        {
            parts.Add(sessionName);
        }

        return parts.Count > 0 ? string.Join(" - ", parts) : fallback;
    }

    private static DateTime ResolveRemoteSessionDate(RemoteSessionMetadata? metadata, DateTime fallback)
    {
        if (metadata == null)
        {
            return fallback;
        }

        if (metadata.SessionDateUtc.HasValue && metadata.SessionDateUtc.Value > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(metadata.SessionDateUtc.Value).LocalDateTime;
        }

        if (metadata.SessionDate.HasValue)
        {
            return metadata.SessionDate.Value;
        }

        return fallback;
    }

    private static string NormalizeCloudKey(string key)
    {
        return key.StartsWith("CrownRFEP/", StringComparison.OrdinalIgnoreCase)
            ? key.Substring("CrownRFEP/".Length)
            : key;
    }

    private static bool TryParseSessionIdFromMetadataKey(string key, out int sessionId)
    {
        sessionId = 0;
        var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        if (!string.Equals(parts[0], "sessions", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(parts[^1], "session.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(parts[1], out sessionId);
    }

    private sealed class RemoteSessionMetadata
    {
        [JsonPropertyName("sessionId")]
        public int SessionId { get; set; }

        [JsonPropertyName("sessionName")]
        public string? SessionName { get; set; }

        [JsonPropertyName("place")]
        public string? Place { get; set; }

        [JsonPropertyName("coach")]
        public string? Coach { get; set; }

        [JsonPropertyName("sessionType")]
        public string? SessionType { get; set; }

        [JsonPropertyName("sessionDateUtc")]
        public long? SessionDateUtc { get; set; }

        [JsonPropertyName("sessionDate")]
        public DateTime? SessionDate { get; set; }
    }

    private async Task AddRemoteVideoToLibraryAsync(RemoteVideoItem? remoteVideo)
    {
        if (remoteVideo == null) return;

        try
        {
            if (remoteVideo.LinkedLocalVideo != null)
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                await page?.DisplayAlert("Ya existe", "Este video ya está en tu biblioteca personal.", "OK")!;
                return;
            }

            var session = await EnsureRemoteSessionExistsAsync(remoteVideo);
            if (session == null)
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                await page?.DisplayAlert("Error", "No se pudo crear la sesión contenedora.", "OK")!;
                return;
            }

            var newVideo = new VideoClip
            {
                SessionId = session.Id,
                ClipPath = remoteVideo.Key.StartsWith("CrownRFEP/")
                    ? remoteVideo.Key.Substring("CrownRFEP/".Length)
                    : remoteVideo.Key,
                ThumbnailPath = remoteVideo.ThumbnailUrl,
                ClipSize = remoteVideo.Size,
                CreationDate = new DateTimeOffset(remoteVideo.LastModified).ToUnixTimeSeconds(),
                Source = "remote",
                IsSynced = 1,
                LastSyncUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LocalClipPath = null
            };

            await _databaseService.InsertVideoClipAsync(newVideo);

            remoteVideo.LinkedLocalVideo = newVideo;
            remoteVideo.IsLocallyAvailable = false;
            newVideo.Session = session;

            await ApplyRemoteMetadataToLocalVideoAsync(remoteVideo, newVideo);

            System.Diagnostics.Debug.WriteLine($"[Remote] Video {remoteVideo.VideoId} añadido a biblioteca personal (solo referencia)");

            if (_getIsAllGallerySelected())
            {
                await _loadAllVideosAsync();
            }
            else if (_getSelectedSession()?.Id == session.Id)
            {
                await _loadSelectedSessionVideosAsync(_getSelectedSession());
            }
            else
            {
                _insertAllVideosCache(newVideo);
            }

            await CheckPendingSyncAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error añadiendo a biblioteca: {ex.Message}");
        }
    }

    private async Task<Session?> EnsureRemoteSessionExistsAsync(RemoteVideoItem remoteVideo)
    {
        if (remoteVideo.SessionId <= 0)
            return null;

        var existing = await _databaseService.GetSessionByIdAsync(remoteVideo.SessionId);
        if (existing != null)
        {
            await _ensureSessionVisibleAsync(existing);
            return existing;
        }

        var sessionName = GetRemoteSessionName(remoteVideo);
        var sessionDate = GetRemoteSessionDate(remoteVideo);
        var sessionPlace = GetRemoteSessionPlace(remoteVideo);

        var matched = await FindMatchingLocalSessionAsync(sessionName, sessionDate, sessionPlace);
        if (matched != null)
        {
            await _ensureSessionVisibleAsync(matched);
            return matched;
        }

        var remoteSession = RemoteSessions.FirstOrDefault(s => s.SessionId == remoteVideo.SessionId);
        var session = new Session
        {
            Id = remoteVideo.SessionId,
            NombreSesion = sessionName,
            Lugar = sessionPlace,
            Coach = remoteSession?.Coach,
            Fecha = new DateTimeOffset(sessionDate).ToUnixTimeSeconds(),
            Participantes = ""
        };

        await _databaseService.InsertSessionWithIdAsync(session);

        if (!string.IsNullOrWhiteSpace(session.Lugar))
            await _ensurePlaceExistsAsync(session.Lugar);

        await _ensureSessionVisibleAsync(session);
        return session;
    }

    private string GetRemoteSessionName(RemoteVideoItem remoteVideo)
    {
        var remoteSession = RemoteSessions.FirstOrDefault(s => s.SessionId == remoteVideo.SessionId);
        if (!string.IsNullOrWhiteSpace(remoteSession?.Title))
            return remoteSession.Title;
        if (!string.IsNullOrWhiteSpace(remoteVideo.SessionName))
            return remoteVideo.SessionName;
        return $"Sesión {remoteVideo.SessionId}";
    }

    private DateTime GetRemoteSessionDate(RemoteVideoItem remoteVideo)
    {
        var remoteSession = RemoteSessions.FirstOrDefault(s => s.SessionId == remoteVideo.SessionId);
        return remoteSession?.SessionDate ?? remoteVideo.LastModified;
    }

    private string? GetRemoteSessionPlace(RemoteVideoItem remoteVideo)
        => RemoteSessions.FirstOrDefault(s => s.SessionId == remoteVideo.SessionId)?.Place;

    private async Task<Session?> FindMatchingLocalSessionAsync(string sessionName, DateTime sessionDate, string? sessionPlace)
    {
        var candidates = _getRecentSessions();
        var matched = candidates.FirstOrDefault(s => IsMatchingSession(s, sessionName, sessionDate, sessionPlace));
        if (matched != null)
            return matched;

        var allSessions = await _databaseService.GetAllSessionsAsync();
        return allSessions.FirstOrDefault(s => IsMatchingSession(s, sessionName, sessionDate, sessionPlace));
    }

    private static bool IsMatchingSession(Session session, string sessionName, DateTime sessionDate, string? sessionPlace)
    {
        var name = session.NombreSesion ?? session.DisplayName;
        if (!string.Equals(name?.Trim(), sessionName.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (session.Fecha > 0 && sessionDate != DateTime.MinValue)
        {
            var localDate = DateTimeOffset.FromUnixTimeSeconds(session.Fecha).LocalDateTime.Date;
            if (localDate != sessionDate.Date)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(sessionPlace))
        {
            if (!string.Equals(sessionPlace.Trim(), session.Lugar?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private async Task ApplyRemoteMetadataToLocalVideoAsync(
        RemoteVideoItem remoteVideo,
        VideoClip localVideo,
        bool replaceInputs = false,
        long? metadataLastModifiedUtc = null)
    {
        try
        {
            var metadataKey = $"sessions/{remoteVideo.SessionId}/metadata/{remoteVideo.VideoId}.json";
            var metadata = await LoadRemoteVideoMetadataAsync(metadataKey);
            if (metadata == null)
                return;

            if (replaceInputs)
                await _databaseService.DeleteInputsByVideoAsync(localVideo.Id);

            if (metadata.Video != null)
            {
                if (!string.IsNullOrWhiteSpace(metadata.Video.ComparisonName))
                    localVideo.ComparisonName = metadata.Video.ComparisonName;
                if (metadata.Video.Section > 0)
                    localVideo.Section = metadata.Video.Section;
                if (metadata.Video.ClipDuration > 0)
                    localVideo.ClipDuration = metadata.Video.ClipDuration;
                if (metadata.Video.ClipSize > 0)
                    localVideo.ClipSize = metadata.Video.ClipSize;
                if (!string.IsNullOrWhiteSpace(metadata.Video.ThumbnailPath)
                    && (metadata.Video.ThumbnailPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || metadata.Video.ThumbnailPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    localVideo.ThumbnailPath = metadata.Video.ThumbnailPath;
                }

                await _databaseService.UpdateVideoClipAsync(localVideo);
            }

            if (metadata.Athlete != null && metadata.Athlete.Id > 0)
            {
                var localAthlete = await _databaseService.GetAthleteByIdAsync(metadata.Athlete.Id);
                if (localAthlete == null)
                {
                    var newAthlete = new Athlete
                    {
                        Id = metadata.Athlete.Id,
                        Nombre = metadata.Athlete.Nombre ?? "",
                        Apellido = metadata.Athlete.Apellido ?? "",
                        Category = metadata.Athlete.Category,
                        CategoriaId = metadata.Athlete.CategoriaId,
                        Favorite = metadata.Athlete.Favorite
                    };
                    await _databaseService.SaveAthleteAsync(newAthlete);
                    localAthlete = newAthlete;
                }

                if (localVideo.AtletaId <= 0)
                {
                    localVideo.AtletaId = metadata.Athlete.Id;
                    await _databaseService.UpdateVideoClipAsync(localVideo);
                }

                localVideo.Atleta = localAthlete;
            }

            if (metadata.Inputs.Count > 0)
            {
                if (metadata.Tags.Count > 0)
                {
                    foreach (var tagDef in metadata.Tags)
                    {
                        if (tagDef.Id <= 0) continue;
                        await _databaseService.SaveTagAsync(new Tag
                        {
                            Id = tagDef.Id,
                            NombreTag = tagDef.Name
                        });
                    }
                }

                var eventGroups = metadata.Inputs
                    .Where(i => i.IsEvent == 1 && i.InputTypeId > 0)
                    .GroupBy(i => i.InputTypeId)
                    .ToList();

                foreach (var group in eventGroups)
                {
                    var existingEvent = await _databaseService.GetEventTagByIdAsync(group.Key);
                    if (existingEvent == null)
                    {
                        var name = metadata.Tags.FirstOrDefault(t => t.Id == group.Key)?.Name
                            ?? group.Select(i => i.InputValue).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                            ?? $"Tag {group.Key}";
                        await _databaseService.InsertEventTagAsync(new EventTagDefinition
                        {
                            Id = group.Key,
                            Nombre = name
                        });
                    }
                }

                foreach (var inputData in metadata.Inputs)
                {
                    var input = inputData.ToInput();
                    input.Id = 0;
                    input.VideoId = localVideo.Id;
                    input.SessionId = localVideo.SessionId;
                    await _databaseService.SaveInputAsync(input);
                }
            }
            else if (metadata.Tags.Count > 0)
            {
                foreach (var tag in metadata.Tags)
                {
                    if (tag.Id <= 0) continue;
                    await _databaseService.SaveTagAsync(new Tag
                    {
                        Id = tag.Id,
                        NombreTag = tag.Name
                    });
                    var input = new Input
                    {
                        Id = 0,
                        VideoId = localVideo.Id,
                        SessionId = localVideo.SessionId,
                        AthleteId = localVideo.AtletaId,
                        InputTypeId = tag.Id,
                        InputDateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        InputValue = tag.Name,
                        TimeStamp = 0,
                        IsEvent = 0
                    };
                    await _databaseService.SaveInputAsync(input);
                }
            }

            await _databaseService.DeleteExecutionTimingEventsByVideoAsync(localVideo.Id);
            var timingEvents = metadata.TimingEvents.Select(t =>
            {
                var evt = t.ToEvent();
                evt.Id = 0;
                evt.VideoId = localVideo.Id;
                evt.SessionId = localVideo.SessionId;
                return evt;
            }).ToList();

            if (timingEvents.Count > 0)
            {
                await _databaseService.InsertExecutionTimingEventsAsync(timingEvents);
                localVideo.HasTiming = true;
            }

            ApplyMetadataTagsToLocalVideo(localVideo, metadata);

            localVideo.LastSyncUtc = metadataLastModifiedUtc ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _databaseService.UpdateVideoClipAsync(localVideo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error aplicando metadatos a video local: {ex.Message}");
        }
    }

    private async Task SyncRemoteChangesToPersonalLibraryAsync(
        Dictionary<int, RemoteSessionMetadata> sessionMetadata,
        Dictionary<string, CloudFileInfo> metadataFiles)
    {
        try
        {
            var sessionsToSync = RemoteVideos
                .Where(v => v.SessionId > 0 && v.LinkedLocalVideo != null)
                .Select(v => v.SessionId)
                .Distinct()
                .ToList();
            if (sessionsToSync.Count == 0)
                return;

            var sessionsChanged = false;
            foreach (var sessionId in sessionsToSync)
            {
                if (!sessionMetadata.TryGetValue(sessionId, out var metadata))
                    continue;

                var session = await _databaseService.GetSessionByIdAsync(sessionId);
                if (session == null)
                    continue;

                var changed = false;
                if (!string.IsNullOrWhiteSpace(metadata.SessionName) && session.NombreSesion != metadata.SessionName)
                {
                    session.NombreSesion = metadata.SessionName;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(metadata.Place) && session.Lugar != metadata.Place)
                {
                    session.Lugar = metadata.Place;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(metadata.Coach) && session.Coach != metadata.Coach)
                {
                    session.Coach = metadata.Coach;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(metadata.SessionType) && session.TipoSesion != metadata.SessionType)
                {
                    session.TipoSesion = metadata.SessionType;
                    changed = true;
                }

                var newFecha = metadata.SessionDateUtc.HasValue && metadata.SessionDateUtc.Value > 0
                    ? metadata.SessionDateUtc.Value
                    : (metadata.SessionDate.HasValue
                        ? new DateTimeOffset(metadata.SessionDate.Value).ToUnixTimeSeconds()
                        : 0);

                if (newFecha > 0 && session.Fecha != newFecha)
                {
                    session.Fecha = newFecha;
                    changed = true;
                }

                if (!changed)
                    continue;

                await _databaseService.SaveSessionAsync(session);
                sessionsChanged = true;

                _replaceRecentSession(session);

                if (_getSelectedSession()?.Id == sessionId)
                    _setSelectedSession(session);
            }

            if (sessionsChanged)
            {
                _syncVisibleSessionRows();
                _notifySelectedSessionTitleChanged();
            }

            foreach (var remoteVideo in RemoteVideos
                .Where(v => v.LinkedLocalVideo == null && sessionsToSync.Contains(v.SessionId)))
            {
                await AddRemoteVideoToLibraryAsync(remoteVideo);
            }

            foreach (var remoteVideo in RemoteVideos
                .Where(v => v.LinkedLocalVideo != null && sessionsToSync.Contains(v.SessionId)))
            {
                var localVideo = remoteVideo.LinkedLocalVideo;
                if (localVideo == null)
                    continue;

                var metadataKey = $"sessions/{remoteVideo.SessionId}/metadata/{remoteVideo.VideoId}.json";
                if (!metadataFiles.TryGetValue(metadataKey, out var metadataFile))
                    continue;

                var metadataUtc = new DateTimeOffset(metadataFile.LastModified).ToUnixTimeSeconds();
                if (localVideo.LastSyncUtc > 0 && localVideo.LastSyncUtc >= metadataUtc)
                    continue;

                await ApplyRemoteMetadataToLocalVideoAsync(remoteVideo, localVideo, replaceInputs: true, metadataLastModifiedUtc: metadataUtc);

                if (_getSelectedSession()?.Id == localVideo.SessionId)
                {
                    await _refreshVideoClipInGalleryAsync(localVideo);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error sincronizando cambios remotos: {ex.Message}");
        }
    }

    private static void ApplyMetadataTagsToLocalVideo(VideoClip localVideo, VideoSyncData metadata)
    {
        if (metadata.Tags.Count == 0 && metadata.Inputs.Count == 0)
            return;

        var tagMap = metadata.Tags
            .Where(t => t.Id > 0)
            .GroupBy(t => t.Id)
            .ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty);

        var tags = metadata.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => new Tag { Id = t.Id, NombreTag = t.Name })
            .ToList();

        var eventGroups = metadata.Inputs
            .Where(i => i.IsEvent == 1 && i.InputTypeId > 0)
            .GroupBy(i => i.InputTypeId)
            .ToList();

        var eventTags = new List<Tag>();
        foreach (var group in eventGroups)
        {
            var name = tagMap.TryGetValue(group.Key, out var tagName)
                ? tagName
                : group.Select(i => i.InputValue).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? $"Tag {group.Key}";
            eventTags.Add(new Tag
            {
                Id = group.Key,
                NombreTag = name,
                IsEventTag = true,
                EventCount = group.Count()
            });
        }

        localVideo.Tags = tags.Count > 0 ? tags : null;
        localVideo.EventTags = eventTags.Count > 0 ? eventTags : null;
    }

    private async Task AddRemoteSessionToLibraryAsync(int sessionId)
    {
        if (sessionId <= 0) return;

        try
        {
            var videosToAdd = RemoteVideos
                .Where(v => v.SessionId == sessionId && v.LinkedLocalVideo == null)
                .ToList();

            if (videosToAdd.Count == 0)
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                await page?.DisplayAlert("Ya importada", "Todos los videos de esta sesión ya están en tu biblioteca personal.", "OK")!;
                return;
            }

            foreach (var remoteVideo in videosToAdd)
            {
                await AddRemoteVideoToLibraryAsync(remoteVideo);
            }

            var mainPage = Application.Current?.Windows.FirstOrDefault()?.Page;
            await mainPage?.DisplayAlert("Sesión añadida", $"Se han añadido {videosToAdd.Count} videos a tu biblioteca personal.", "OK")!;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error añadiendo sesión: {ex.Message}");
        }
    }

    private async Task DeleteRemoteSessionFromCloudAsync(int sessionId)
    {
        if (sessionId <= 0)
            return;

        if (!CanDeleteRemoteSessions)
        {
            await Shell.Current.DisplayAlert("Permisos", "No tienes permisos para eliminar sesiones de la organización.", "OK");
            return;
        }

        try
        {
            var prefix = $"sessions/{sessionId}/";
            var result = await _cloudBackendService.ListFilesAsync(prefix, maxItems: 5000);
            if (!result.Success || result.Files == null)
            {
                await Shell.Current.DisplayAlert("Error", "No se pudieron listar los archivos de la sesión.", "OK");
                return;
            }

            foreach (var file in result.Files.Where(f => !f.IsFolder))
            {
                try
                {
                    await _cloudBackendService.DeleteFileAsync(file.Key);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando archivo {file.Key}: {ex.Message}");
                }
            }

            var removedVideos = RemoteVideos.Where(v => v.SessionId == sessionId).ToList();
            var removedSession = RemoteSessions.FirstOrDefault(s => s.SessionId == sessionId);

            foreach (var video in removedVideos)
            {
                if (video.LinkedLocalVideo != null)
                    await _removeLocalVideoClipAsync(video.LinkedLocalVideo);

                video.LinkedLocalVideo = null;
                video.IsLocallyAvailable = false;
                video.LocalPath = null;
            }

            await _removeLocalSessionIfEmptyAsync(sessionId);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var video in removedVideos)
                {
                    RemoteVideos.Remove(video);
                }

                if (removedSession != null)
                    RemoteSessions.Remove(removedSession);

                if (SelectedRemoteSessionId == sessionId)
                {
                    SelectedRemoteSessionId = 0;
                    OnPropertyChanged(nameof(RemoteGalleryItems));
                }
            });

            if (_remoteFilesCache != null)
            {
                _remoteFilesCache = _remoteFilesCache
                    .Where(f => !f.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            await Shell.Current.DisplayAlert("Sesión eliminada", "La sesión se eliminó de la biblioteca de organización.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando sesión remota {sessionId}: {ex.Message}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo eliminar la sesión: {ex.Message}", "OK");
        }
    }

    private async Task DeleteRemoteVideoFromLibraryAsync(RemoteVideoItem? remoteVideo)
    {
        if (remoteVideo == null) return;

        try
        {
            if (remoteVideo.LinkedLocalVideo == null)
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                await page?.DisplayAlert("No está en biblioteca", "Este video no está en tu biblioteca personal.", "OK")!;
                return;
            }

            var mainPage = Application.Current?.Windows.FirstOrDefault()?.Page;
            var confirm = await mainPage?.DisplayAlert(
                "Quitar de biblioteca personal",
                $"¿Eliminar '{remoteVideo.FileName}' de tu biblioteca personal?\n\nEl video seguirá disponible en la organización.",
                "Eliminar", "Cancelar")!;

            if (!confirm) return;

            var videoId = remoteVideo.LinkedLocalVideo.Id;

            if (remoteVideo.IsLocallyAvailable && !string.IsNullOrEmpty(remoteVideo.LocalPath))
            {
                try
                {
                    if (File.Exists(remoteVideo.LocalPath))
                    {
                        File.Delete(remoteVideo.LocalPath);
                        System.Diagnostics.Debug.WriteLine($"[Remote] Archivo local eliminado: {remoteVideo.LocalPath}");
                    }
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando archivo: {fileEx.Message}");
                }
            }

            var localThumbPath = remoteVideo.LinkedLocalVideo.LocalThumbnailPath;
            if (!string.IsNullOrEmpty(localThumbPath) && File.Exists(localThumbPath))
            {
                try
                {
                    File.Delete(localThumbPath);
                }
                catch { }
            }

            await _databaseService.DeleteVideoClipAsync(videoId);
            _databaseService.InvalidateCache();

            remoteVideo.LinkedLocalVideo = null;
            remoteVideo.IsLocallyAvailable = false;
            remoteVideo.LocalPath = null;

            System.Diagnostics.Debug.WriteLine($"[Remote] Video {remoteVideo.VideoId} eliminado de biblioteca personal");

            await CheckPendingSyncAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando de biblioteca: {ex.Message}");
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            await page?.DisplayAlert("Error", $"No se pudo eliminar el video: {ex.Message}", "OK")!;
        }
    }

    private async Task DeleteAllRemoteVideosFromLibraryAsync()
    {
        try
        {
            var videosInLibrary = RemoteVideos.Where(v => v.LinkedLocalVideo != null).ToList();

            var mainPage = Application.Current?.Windows.FirstOrDefault()?.Page;
            var videoCountText = videosInLibrary.Count > 0
                ? $"\n\nSe eliminarán {videosInLibrary.Count} videos descargados del dispositivo."
                : "";

            var confirm = await mainPage?.DisplayAlert(
                "Eliminar biblioteca de organización",
                $"¿Desconectar de '{RemoteLibraryDisplayName}' y eliminar la biblioteca de organización del sistema?{videoCountText}\n\nPodrás volver a conectarte más tarde.",
                "Eliminar y desconectar", "Cancelar")!;

            if (!confirm) return;

            int deleted = 0;
            int errors = 0;

            foreach (var remoteVideo in videosInLibrary)
            {
                try
                {
                    if (remoteVideo.LinkedLocalVideo == null) continue;

                    var videoId = remoteVideo.LinkedLocalVideo.Id;

                    if (remoteVideo.IsLocallyAvailable && !string.IsNullOrEmpty(remoteVideo.LocalPath))
                    {
                        try
                        {
                            if (File.Exists(remoteVideo.LocalPath))
                            {
                                File.Delete(remoteVideo.LocalPath);
                            }
                        }
                        catch { }
                    }

                    var localThumbPath = remoteVideo.LinkedLocalVideo.LocalThumbnailPath;
                    if (!string.IsNullOrEmpty(localThumbPath) && File.Exists(localThumbPath))
                    {
                        try { File.Delete(localThumbPath); } catch { }
                    }

                    await _databaseService.DeleteVideoClipAsync(videoId);
                    deleted++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando video {remoteVideo.VideoId}: {ex.Message}");
                    errors++;
                }
            }

            try
            {
                await _cloudBackendService.LogoutAsync();
                System.Diagnostics.Debug.WriteLine("[Remote] Desconectado del cloud backend");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Error en logout: {ex.Message}");
            }

            RemoteVideos.Clear();

            IsRemoteLibraryVisible = false;
            _setIsRemoteLibraryExpanded(false);
            RemoteLibraryDisplayName = "Organización";
            RemoteAllGalleryItemCount = "—";
            IsLoginRequired = true;
            OnPropertyChanged(nameof(IsCloudAuthenticated));

            if (IsAnyRemoteSectionSelected)
            {
                IsRemoteAllGallerySelected = false;
                IsRemoteVideoLessonsSelected = false;
                IsRemoteTrashSelected = false;
                OnPropertyChanged(nameof(ShowRemoteGallery));
                OnPropertyChanged(nameof(IsAnyRemoteSectionSelected));
            }

            _databaseService.InvalidateCache();
            await CheckPendingSyncAsync();

            var resultPage = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (deleted > 0)
            {
                await resultPage?.DisplayAlert("Biblioteca eliminada",
                    $"Se desconectó de '{RemoteLibraryDisplayName}' y se eliminaron {deleted} videos del dispositivo.", "OK")!;
            }
            else
            {
                await resultPage?.DisplayAlert("Biblioteca eliminada",
                    "Se desconectó de la biblioteca de organización.", "OK")!;
            }

            System.Diagnostics.Debug.WriteLine($"[Remote] Biblioteca de organización eliminada. Videos eliminados: {deleted}, errores: {errors}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando biblioteca de organización: {ex.Message}");
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            await page?.DisplayAlert("Error", $"No se pudo eliminar la biblioteca de organización: {ex.Message}", "OK")!;
        }
    }

    private async Task DownloadRemoteVideoAsync(RemoteVideoItem? remoteVideo)
    {
        if (remoteVideo == null || _syncService == null) return;

        try
        {
            remoteVideo.IsDownloading = true;
            remoteVideo.DownloadProgress = 0;

            if (remoteVideo.LinkedLocalVideo == null)
            {
                await AddRemoteVideoToLibraryAsync(remoteVideo);
            }

            if (remoteVideo.LinkedLocalVideo == null) return;

            var progress = new Progress<double>(p => remoteVideo.DownloadProgress = p);
            var result = await _syncService.DownloadVideoAsync(remoteVideo.LinkedLocalVideo, progress);

            if (result.Success)
            {
                remoteVideo.IsLocallyAvailable = true;
                remoteVideo.LocalPath = result.LocalPath;
                System.Diagnostics.Debug.WriteLine($"[Remote] Video {remoteVideo.VideoId} descargado correctamente");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Error descargando: {result.ErrorMessage}");
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                await page?.DisplayAlert("Error", $"No se pudo descargar el video: {result.ErrorMessage}", "OK")!;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error descargando video: {ex.Message}");
        }
        finally
        {
            remoteVideo.IsDownloading = false;
        }
    }

    private static bool IsStreamingUrl(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.StartsWith("CrownRFEP/", StringComparison.OrdinalIgnoreCase)
            ? path.Substring("CrownRFEP/".Length)
            : path;
    }

    private async Task<string?> GetStreamingUrlAsync(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var result = await _cloudBackendService.GetDownloadUrlAsync(relativePath);
        return result.Success ? result.Url : null;
    }

    private async Task PlayRemoteVideoAsync(RemoteVideoItem? remoteVideo)
    {
        if (remoteVideo == null) return;

        try
        {
            var remotePath = NormalizeRemotePath(remoteVideo.Key);
            var streamingUrl = await GetStreamingUrlAsync(remotePath);
            if (!string.IsNullOrWhiteSpace(streamingUrl))
            {
                var clip = remoteVideo.LinkedLocalVideo ?? new VideoClip
                {
                    Id = 0,
                    SessionId = 0,
                    ClipPath = remotePath,
                    ClipSize = remoteVideo.Size,
                    CreationDate = new DateTimeOffset(remoteVideo.LastModified).ToUnixTimeSeconds(),
                    Source = "remote",
                    IsSynced = 1
                };

                if (remoteVideo.Section > 0)
                {
                    clip.Section = remoteVideo.Section;
                }

                if (remoteVideo.Tags != null)
                {
                    clip.Tags = remoteVideo.Tags.ToList();
                }

                if (remoteVideo.EventTags != null)
                {
                    clip.EventTags = remoteVideo.EventTags.ToList();
                }

                clip.LocalClipPath = streamingUrl;
                await _playSelectedVideoAsync(clip);
                return;
            }

            if (remoteVideo.IsLocallyAvailable && remoteVideo.LinkedLocalVideo != null)
            {
                await _playSelectedVideoAsync(remoteVideo.LinkedLocalVideo);
                return;
            }

            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            await page?.DisplayAlert("No disponible", "No se pudo obtener el stream del video.", "OK")!;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error reproduciendo video: {ex.Message}");
        }
    }

    private void SetRemoteSelection(string sectionName)
    {
        ClearRemoteSessionSelection();
        IsRemoteAllGallerySelected = sectionName == "Galería General";
        IsRemoteVideoLessonsSelected = sectionName == "Videolecciones";
        IsRemoteTrashSelected = sectionName == "Papelera";

        OnPropertyChanged(nameof(ShowRemoteGallery));
        OnPropertyChanged(nameof(IsAnyRemoteSectionSelected));
        _notifyShowVideoGalleryChanged();
        _notifySelectedSessionTitleChanged();
    }

    private async Task SelectRemoteSessionAsync(RemoteSessionListItem? sessionItem)
    {
        if (sessionItem == null) return;

        ClearRemoteVideoSelection();
        _clearLocalSelection();

        if (!_cloudBackendService.IsAuthenticated)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlert("No autenticado", "Inicia sesión para acceder a los archivos del equipo.", "OK");
            }
            return;
        }

        IsRemoteAllGallerySelected = false;
        IsRemoteVideoLessonsSelected = false;
        IsRemoteTrashSelected = false;
        SelectedRemoteSessionId = sessionItem.SessionId;

        if (RemoteVideos.Count == 0)
        {
            await LoadRemoteGalleryAsync();
        }
        else
        {
            OnPropertyChanged(nameof(RemoteGalleryItems));
        }
    }

    private void ClearRemoteSessionSelection()
    {
        if (SelectedRemoteSessionId > 0)
        {
            SelectedRemoteSessionId = 0;
        }

        UpdateRemoteSessionSelectionStates(0);
    }

    private void UpdateRemoteSessionSelectionStates(int selectedSessionId)
    {
        foreach (var session in RemoteSessions)
        {
            session.IsSelected = session.SessionId == selectedSessionId;
        }
    }

    private void ToggleRemoteMultiSelectMode()
    {
        IsRemoteMultiSelectMode = !IsRemoteMultiSelectMode;
        if (!IsRemoteMultiSelectMode)
        {
            ClearRemoteVideoSelection();
        }
    }

    private void ToggleRemoteSelectAll()
    {
        if (!IsRemoteMultiSelectMode)
        {
            IsRemoteMultiSelectMode = true;
        }

        IsRemoteSelectAllActive = !IsRemoteSelectAllActive;

        foreach (var video in RemoteVideos)
        {
            video.IsSelected = IsRemoteSelectAllActive;
        }
        OnPropertyChanged(nameof(SelectedRemoteVideoCount));
    }

    private void ToggleRemoteVideoSelection(RemoteVideoItem? video)
    {
        if (video == null || !IsRemoteMultiSelectMode) return;

        video.IsSelected = !video.IsSelected;
        OnPropertyChanged(nameof(SelectedRemoteVideoCount));

        IsRemoteSelectAllActive = RemoteVideos.All(v => v.IsSelected);
    }

    private void ClearRemoteVideoSelection()
    {
        foreach (var video in RemoteVideos)
        {
            video.IsSelected = false;
        }
        IsRemoteSelectAllActive = false;
        OnPropertyChanged(nameof(SelectedRemoteVideoCount));
    }

    private async Task DownloadSelectedRemoteVideosAsync()
    {
        var selectedVideos = RemoteVideos.Where(v => v.IsSelected).ToList();

        if (selectedVideos.Count == 0)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            await page?.DisplayAlert("Descargar", "No hay videos seleccionados.", "OK")!;
            return;
        }

        var page2 = Application.Current?.Windows.FirstOrDefault()?.Page;
        var confirm = await page2?.DisplayAlert(
            "Descargar videos",
            $"¿Descargar {selectedVideos.Count} video(s) al dispositivo?",
            "Descargar",
            "Cancelar")!;

        if (!confirm) return;

        int downloaded = 0;
        int errors = 0;

        foreach (var video in selectedVideos)
        {
            try
            {
                if (!video.IsLocallyAvailable)
                {
                    await DownloadRemoteVideoAsync(video);
                    if (video.IsLocallyAvailable)
                        downloaded++;
                }
                else
                {
                    downloaded++;
                }
            }
            catch
            {
                errors++;
            }
        }

        ClearRemoteVideoSelection();
        IsRemoteMultiSelectMode = false;

        var resultPage = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (errors > 0)
        {
            await resultPage?.DisplayAlert("Descarga completada",
                $"Se descargaron {downloaded} videos. {errors} fallaron.", "OK")!;
        }
        else
        {
            await resultPage?.DisplayAlert("Descarga completada",
                $"Se descargaron {downloaded} videos correctamente.", "OK")!;
        }
    }

    private async Task AddSelectedRemoteToLibraryAsync()
    {
        var selectedVideos = RemoteVideos.Where(v => v.IsSelected && v.LinkedLocalVideo == null).ToList();

        if (selectedVideos.Count == 0)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            await page?.DisplayAlert("Añadir a biblioteca personal", "No hay videos nuevos seleccionados para añadir.", "OK")!;
            return;
        }

        int added = 0;
        foreach (var video in selectedVideos)
        {
            try
            {
                await AddRemoteVideoToLibraryAsync(video);
                added++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Error añadiendo video {video.VideoId}: {ex.Message}");
            }
        }

        ClearRemoteVideoSelection();
        IsRemoteMultiSelectMode = false;

        var resultPage = Application.Current?.Windows.FirstOrDefault()?.Page;
        await resultPage?.DisplayAlert("Añadidos a biblioteca personal",
            $"Se añadieron {added} videos a tu biblioteca personal.", "OK")!;
    }

    private async Task DeleteSelectedRemoteFromCloudAsync()
    {
        var selectedVideos = RemoteVideos.Where(v => v.IsSelected).ToList();

        if (selectedVideos.Count == 0)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            await page?.DisplayAlert("Eliminar de la nube", "No hay videos seleccionados.", "OK")!;
            return;
        }

        var page2 = Application.Current?.Windows.FirstOrDefault()?.Page;
        var confirm = await page2?.DisplayAlert(
            "⚠️ Eliminar de la nube",
            $"¿Eliminar PERMANENTEMENTE {selectedVideos.Count} video(s) de la nube?\n\n" +
            "Esta acción es IRREVERSIBLE. Los archivos se eliminarán del servidor remoto.",
            "Eliminar permanentemente",
            "Cancelar")!;

        if (!confirm) return;

        var confirm2 = await page2?.DisplayAlert(
            "Confirmar eliminación",
            "¿Estás seguro? Esta acción no se puede deshacer.",
            "Sí, eliminar",
            "No, cancelar")!;

        if (!confirm2) return;

        int deleted = 0;
        int errors = 0;
        var affectedSessionIds = new HashSet<int>();

        foreach (var video in selectedVideos)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Intentando eliminar: Key={video.Key}");

                var deleteSuccess = await _cloudBackendService.DeleteFileAsync(video.Key);

                System.Diagnostics.Debug.WriteLine($"[Remote] Resultado eliminación: {deleteSuccess}");

                if (deleteSuccess)
                {
                    var thumbKey = video.Key.Replace("/videos/", "/thumbnails/").Replace(".mp4", ".jpg");
                    await _cloudBackendService.DeleteFileAsync(thumbKey);

                    var metaKey = video.Key.Replace("/videos/", "/metadata/").Replace(".mp4", ".json");
                    await _cloudBackendService.DeleteFileAsync(metaKey);

                    if (video.LinkedLocalVideo != null)
                    {
                        affectedSessionIds.Add(video.LinkedLocalVideo.SessionId);
                        await _removeLocalVideoClipAsync(video.LinkedLocalVideo);
                        video.LinkedLocalVideo = null;
                        video.IsLocallyAvailable = false;
                        video.LocalPath = null;
                    }

                    deleted++;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando {video.Key}");
                    errors++;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando video {video.VideoId}: {ex.Message}");
                errors++;
            }
        }
        foreach (var sessionId in affectedSessionIds)
            await _removeLocalSessionIfEmptyAsync(sessionId);

        await LoadRemoteGalleryAsync();

        ClearRemoteVideoSelection();
        IsRemoteMultiSelectMode = false;

        var resultPage = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (errors > 0)
        {
            await resultPage?.DisplayAlert("Eliminación completada",
                $"Se eliminaron {deleted} videos de la nube. {errors} fallaron.", "OK")!;
        }
        else
        {
            await resultPage?.DisplayAlert("Eliminación completada",
                $"Se eliminaron {deleted} videos de la nube permanentemente.", "OK")!;
        }
    }

    private async Task DeleteSelectedRemoteFromLibraryAsync()
    {
        var selectedVideos = RemoteVideos.Where(v => v.IsSelected && v.LinkedLocalVideo != null).ToList();

        if (selectedVideos.Count == 0)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            await page?.DisplayAlert("Eliminar de biblioteca personal", "No hay videos en biblioteca seleccionados.", "OK")!;
            return;
        }

        var page2 = Application.Current?.Windows.FirstOrDefault()?.Page;
        var confirm = await page2?.DisplayAlert(
            "Eliminar de biblioteca personal",
            $"¿Eliminar {selectedVideos.Count} video(s) de tu biblioteca personal?\n\n" +
            "Los videos permanecerán en la organización y podrás añadirlos de nuevo.",
            "Eliminar de biblioteca personal",
            "Cancelar")!;

        if (!confirm) return;

        int deleted = 0;

        foreach (var video in selectedVideos)
        {
            try
            {
                if (video.LinkedLocalVideo == null) continue;

                if (!string.IsNullOrEmpty(video.LocalPath) && File.Exists(video.LocalPath))
                {
                    try { File.Delete(video.LocalPath); } catch { }
                }

                var thumbPath = video.LinkedLocalVideo.LocalThumbnailPath;
                if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
                {
                    try { File.Delete(thumbPath); } catch { }
                }

                await _databaseService.DeleteVideoClipAsync(video.LinkedLocalVideo.Id);

                video.LinkedLocalVideo = null;
                video.IsLocallyAvailable = false;
                video.LocalPath = null;

                deleted++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando video {video.VideoId} de biblioteca: {ex.Message}");
            }
        }

        ClearRemoteVideoSelection();
        IsRemoteMultiSelectMode = false;

        var resultPage = Application.Current?.Windows.FirstOrDefault()?.Page;
        await resultPage?.DisplayAlert("Eliminación completada",
            $"Se eliminaron {deleted} videos de tu biblioteca personal.", "OK")!;
    }

    private async Task SyncAllVideosAsync()
    {
        if (_syncService == null)
        {
            await Shell.Current.DisplayAlert("Error", "Servicio de sincronización no disponible", "OK");
            return;
        }

        if (!_cloudBackendService.IsAuthenticated)
        {
            await Shell.Current.DisplayAlert("No autenticado", "Inicia sesión en el servidor para sincronizar", "OK");
            return;
        }

        try
        {
            IsSyncing = true;
            SyncStatusText = "Obteniendo videos pendientes...";

            var pendingVideos = await _databaseService.GetUnsyncedVideoClipsAsync();

            if (pendingVideos.Count == 0)
            {
                SyncStatusText = "Todo sincronizado";
                await Task.Delay(1500);
                return;
            }

            var progress = new Progress<(int current, int total, string message)>(p =>
            {
                SyncProgress = (int)((double)p.current / p.total * 100);
                SyncStatusText = p.message;
            });

            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < pendingVideos.Count; i++)
            {
                var video = pendingVideos[i];
                SyncStatusText = $"Subiendo video {i + 1} de {pendingVideos.Count}...";
                SyncProgress = (int)((double)(i + 1) / pendingVideos.Count * 100);

                var result = await _syncService.UploadVideoAsync(video);
                if (result.Success)
                {
                    successCount++;
                    await _syncService.UploadThumbnailAsync(video);
                }
                else
                {
                    failCount++;
                    System.Diagnostics.Debug.WriteLine($"[Sync] Error: {result.ErrorMessage}");
                }
            }

            SyncStatusText = $"Completado: {successCount} subidos, {failCount} errores";
            await CheckPendingSyncAsync();
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Sync] Exception: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private async Task SyncSessionAsync(Session? session)
    {
        if (session == null || _syncService == null) return;

        if (!_cloudBackendService.IsAuthenticated)
        {
            await Shell.Current.DisplayAlert("No autenticado", "Inicia sesión en el servidor para sincronizar", "OK");
            return;
        }

        try
        {
            IsSyncing = true;
            SyncStatusText = $"Sincronizando sesión {session.DisplayName}...";

            var progress = new Progress<(int current, int total, string message)>(p =>
            {
                SyncProgress = (int)((double)p.current / p.total * 100);
                SyncStatusText = p.message;
            });

            var result = await _syncService.SyncSessionAsync(session.Id, SyncDirection.Upload, progress);

            if (result.Success)
            {
                SyncStatusText = $"Sesión sincronizada: {result.SuccessCount} videos";
            }
            else
            {
                SyncStatusText = $"Errores: {result.FailedCount} de {result.TotalCount}";
            }

            await CheckPendingSyncAsync();
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private async Task UploadVideoAsync(VideoClip? video)
    {
        if (video == null || _syncService == null) return;

        if (!_cloudBackendService.IsAuthenticated)
        {
            await Shell.Current.DisplayAlert("No autenticado", "Inicia sesión para subir videos", "OK");
            return;
        }

        try
        {
            IsSyncing = true;
            SyncStatusText = "Subiendo video...";

            var progress = new Progress<double>(p =>
            {
                SyncProgress = (int)(p * 100);
            });

            var result = await _syncService.UploadVideoAsync(video, progress);

            if (result.Success)
            {
                SyncStatusText = "Video subido correctamente";
                _notifySelectedSessionVideosChanged();
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", result.ErrorMessage ?? "Error al subir", "OK");
            }

            await CheckPendingSyncAsync();
            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private async Task DownloadVideoAsync(VideoClip? video)
    {
        if (video == null || _syncService == null) return;

        if (!_cloudBackendService.IsAuthenticated)
        {
            await Shell.Current.DisplayAlert("No autenticado", "Inicia sesión para descargar videos", "OK");
            return;
        }

        try
        {
            IsSyncing = true;
            SyncStatusText = "Descargando video...";

            var progress = new Progress<double>(p =>
            {
                SyncProgress = (int)(p * 100);
            });

            var result = await _syncService.DownloadVideoAsync(video, progress);

            if (result.Success)
            {
                SyncStatusText = "Video descargado correctamente";
                _notifySelectedSessionVideosChanged();
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", result.ErrorMessage ?? "Error al descargar", "OK");
            }

            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private async Task CheckPendingSyncAsync()
    {
        try
        {
            var pendingVideos = await _databaseService.GetUnsyncedVideoClipsAsync();
            PendingSyncCount = pendingVideos.Count;
            OnPropertyChanged(nameof(HasPendingSync));

            if (PendingSyncCount == 0)
            {
                SyncStatusText = "Todo sincronizado ✓";
            }
            else if (!_cloudBackendService.IsAuthenticated)
            {
                SyncStatusText = "Inicia sesión para sincronizar";
            }
            else
            {
                SyncStatusText = "Sincronización cloud";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] Error checking pending: {ex.Message}");
            SyncStatusText = "Error verificando estado";
        }
    }
}
