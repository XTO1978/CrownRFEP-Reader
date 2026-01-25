using System.Windows.Input;

namespace CrownRFEP_Reader.ViewModels;

public class LayoutStateViewModel : ObservableObject
{
    private bool _isAllGallerySelected;
    private bool _isVideoLessonsSelected;
    private bool _isDiaryViewSelected;
    private bool _isUserLibraryExpanded = true;
    private bool _isRemoteLibraryVisible = true;
    private bool _isRemoteLibraryExpanded = true;
    private bool _isRemoteAllGallerySelected;
    private bool _isRemoteVideoLessonsSelected;
    private bool _isRemoteTrashSelected;
    private bool _isRemoteSmartFoldersExpanded = true;
    private bool _isRemoteSessionsListExpanded = true;
    private bool _isSessionsListExpanded = true;
    private bool _isFavoritesExpanded = true;
    private bool _isFavoriteSessionsSelected;
    private bool _isFavoriteVideosSelected;
    private bool _isStatsTabSelected;
    private bool _isCrudTechTabSelected = true;
    private bool _isDiaryTabSelected;
    private bool _isQuickAnalysisIsolatedMode;
    private bool _showNewSessionSidebarPopup;
    private bool _showSmartFolderSidebarPopup;
    private bool _showIconColorPickerPopup;

    public void Configure(
        Func<Task> selectAllGalleryAsync,
        Func<Task> viewVideoLessonsAsync,
        Func<Task> viewTrashAsync,
        Action viewDiary,
        Action toggleFavoritesExpanded,
        Func<Task> selectFavoriteSessionsAsync,
        Func<Task> selectFavoriteVideosAsync,
        Action toggleUserLibraryExpanded,
        Action toggleRemoteLibraryExpanded,
        Action toggleSessionsListExpanded,
        Action toggleRemoteSessionsListExpanded,
        Action toggleRemoteSmartFoldersExpanded)
    {
        SelectAllGalleryCommand = new AsyncRelayCommand(selectAllGalleryAsync);
        ViewVideoLessonsCommand = new AsyncRelayCommand(viewVideoLessonsAsync);
        ViewTrashCommand = new AsyncRelayCommand(viewTrashAsync);
        ViewDiaryCommand = new RelayCommand(viewDiary);
        ToggleFavoritesExpandedCommand = new RelayCommand(toggleFavoritesExpanded);
        SelectFavoriteSessionsCommand = new AsyncRelayCommand(selectFavoriteSessionsAsync);
        SelectFavoriteVideosCommand = new AsyncRelayCommand(selectFavoriteVideosAsync);
        ToggleUserLibraryExpandedCommand = new RelayCommand(toggleUserLibraryExpanded);
        ToggleRemoteLibraryExpandedCommand = new RelayCommand(toggleRemoteLibraryExpanded);
        ToggleSessionsListExpandedCommand = new RelayCommand(toggleSessionsListExpanded);
        ToggleRemoteSessionsListExpandedCommand = new RelayCommand(toggleRemoteSessionsListExpanded);
        ToggleRemoteSmartFoldersExpandedCommand = new RelayCommand(toggleRemoteSmartFoldersExpanded);
    }

    public bool IsAllGallerySelected
    {
        get => _isAllGallerySelected;
        set => SetProperty(ref _isAllGallerySelected, value);
    }

    public bool IsVideoLessonsSelected
    {
        get => _isVideoLessonsSelected;
        set => SetProperty(ref _isVideoLessonsSelected, value);
    }

    public bool IsDiaryViewSelected
    {
        get => _isDiaryViewSelected;
        set => SetProperty(ref _isDiaryViewSelected, value);
    }

    public bool IsUserLibraryExpanded
    {
        get => _isUserLibraryExpanded;
        set => SetProperty(ref _isUserLibraryExpanded, value);
    }

    public bool IsRemoteLibraryVisible
    {
        get => _isRemoteLibraryVisible;
        set => SetProperty(ref _isRemoteLibraryVisible, value);
    }

    public bool IsRemoteLibraryExpanded
    {
        get => _isRemoteLibraryExpanded;
        set => SetProperty(ref _isRemoteLibraryExpanded, value);
    }

    public bool IsRemoteAllGallerySelected
    {
        get => _isRemoteAllGallerySelected;
        set => SetProperty(ref _isRemoteAllGallerySelected, value);
    }

    public bool IsRemoteVideoLessonsSelected
    {
        get => _isRemoteVideoLessonsSelected;
        set => SetProperty(ref _isRemoteVideoLessonsSelected, value);
    }

    public bool IsRemoteTrashSelected
    {
        get => _isRemoteTrashSelected;
        set => SetProperty(ref _isRemoteTrashSelected, value);
    }

    public bool IsRemoteSmartFoldersExpanded
    {
        get => _isRemoteSmartFoldersExpanded;
        set => SetProperty(ref _isRemoteSmartFoldersExpanded, value);
    }

    public bool IsRemoteSessionsListExpanded
    {
        get => _isRemoteSessionsListExpanded;
        set => SetProperty(ref _isRemoteSessionsListExpanded, value);
    }

    public bool IsSessionsListExpanded
    {
        get => _isSessionsListExpanded;
        set => SetProperty(ref _isSessionsListExpanded, value);
    }

    public bool IsFavoritesExpanded
    {
        get => _isFavoritesExpanded;
        set => SetProperty(ref _isFavoritesExpanded, value);
    }

    public bool IsFavoriteSessionsSelected
    {
        get => _isFavoriteSessionsSelected;
        set => SetProperty(ref _isFavoriteSessionsSelected, value);
    }

    public bool IsFavoriteVideosSelected
    {
        get => _isFavoriteVideosSelected;
        set => SetProperty(ref _isFavoriteVideosSelected, value);
    }

    public bool IsStatsTabSelected
    {
        get => _isStatsTabSelected;
        set => SetProperty(ref _isStatsTabSelected, value);
    }

    public bool IsCrudTechTabSelected
    {
        get => _isCrudTechTabSelected;
        set => SetProperty(ref _isCrudTechTabSelected, value);
    }

    public bool IsDiaryTabSelected
    {
        get => _isDiaryTabSelected;
        set => SetProperty(ref _isDiaryTabSelected, value);
    }

    public bool IsQuickAnalysisIsolatedMode
    {
        get => _isQuickAnalysisIsolatedMode;
        set => SetProperty(ref _isQuickAnalysisIsolatedMode, value);
    }

    public bool ShowNewSessionSidebarPopup
    {
        get => _showNewSessionSidebarPopup;
        set => SetProperty(ref _showNewSessionSidebarPopup, value);
    }

    public bool ShowSmartFolderSidebarPopup
    {
        get => _showSmartFolderSidebarPopup;
        set => SetProperty(ref _showSmartFolderSidebarPopup, value);
    }

    public bool ShowIconColorPickerPopup
    {
        get => _showIconColorPickerPopup;
        set => SetProperty(ref _showIconColorPickerPopup, value);
    }

    public ICommand SelectAllGalleryCommand { get; private set; } = default!;
    public ICommand ViewVideoLessonsCommand { get; private set; } = default!;
    public ICommand ViewTrashCommand { get; private set; } = default!;
    public ICommand ViewDiaryCommand { get; private set; } = default!;
    public ICommand ToggleFavoritesExpandedCommand { get; private set; } = default!;
    public ICommand SelectFavoriteSessionsCommand { get; private set; } = default!;
    public ICommand SelectFavoriteVideosCommand { get; private set; } = default!;
    public ICommand ToggleUserLibraryExpandedCommand { get; private set; } = default!;
    public ICommand ToggleRemoteLibraryExpandedCommand { get; private set; } = default!;
    public ICommand ToggleSessionsListExpandedCommand { get; private set; } = default!;
    public ICommand ToggleRemoteSessionsListExpandedCommand { get; private set; } = default!;
    public ICommand ToggleRemoteSmartFoldersExpandedCommand { get; private set; } = default!;
}
