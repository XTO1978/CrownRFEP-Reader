using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace CrownRFEP_Reader.ViewModels;

public class VideosViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService;
    private readonly ITrashService _trashService;
    private readonly StatisticsService _statisticsService;
    private readonly ThumbnailService _thumbnailService;
    private readonly ITableExportService _tableExportService;
    private readonly ICloudBackendService _cloudBackendService;
    private readonly IVideoClipUpdateNotifier _videoClipUpdateNotifier;
    private readonly LayoutStateViewModel _layout;
    private readonly BatchEditViewModel _batchEdit;
    private readonly SmartFoldersViewModel _smartFolders;
    private readonly RemoteLibraryViewModel _remote;

    private Func<Session?>? _getSelectedSession;
    private Action<Session?>? _setSelectedSession;
    private Func<List<Session>>? _getRecentSessionsSnapshot;
    private Func<DashboardStats?>? _getStats;
    private Action<DashboardStats?>? _setStats;
    private Func<bool>? _getIsAllGallerySelected;
    private Action<bool>? _setIsAllGallerySelected;
    private Func<bool>? _getIsVideoLessonsSelected;
    private Action<bool>? _setIsVideoLessonsSelected;
    private Func<bool>? _getIsFavoriteVideosSelected;
    private Action<bool>? _setIsFavoriteVideosSelected;
    private Func<bool>? _getIsDiaryViewSelected;
    private Action<bool>? _setIsDiaryViewSelected;
    private Func<int?>? _getReferenceAthleteId;
    private Func<Task>? _refreshTrashItemCountAsync;
    private Action<int>? _setFavoriteSessionsCount;

    private int _videoLessonsCount;
    private int _favoriteVideosCount;
    private bool _isLoadingSelectedSessionVideos;
    private List<VideoClip>? _favoriteVideosCache;

    private int _videoGalleryColumnSpan = DeviceInfo.Current.Platform == DevicePlatform.iOS ? 3 : 4;

    private bool _isSingleVideoMode;
    private bool _isQuadVideoMode;

    private bool _isHorizontalOrientation = true;

    private VideoClip? _parallelVideo1;
    private VideoClip? _parallelVideo2;
    private VideoClip? _parallelVideo3;
    private VideoClip? _parallelVideo4;
    private bool _isPreviewMode;

    private bool _isPreviewPlayer1Ready;
    private bool _isPreviewPlayer2Ready;
    private bool _isPreviewPlayer3Ready;
    private bool _isPreviewPlayer4Ready;

    private bool _hasStatsUpdatePending;
    private readonly HashSet<int> _modifiedVideoIds = new();

    private const int PageSize = 40;
    private int _currentPage;
    private bool _hasMoreVideos;
    private bool _isLoadingMore;
    private List<VideoClip>? _allVideosCache;
    private List<VideoClip>? _filteredVideosCache;
    private List<Input>? _allInputsCache;
    private List<Tag>? _allTagsCache;

    private DateTime? _selectedFilterDateFrom;
    private DateTime? _selectedFilterDateTo;

    private CancellationTokenSource? _selectedSessionVideosCts;

    private VideoClip? _hoverVideo;

    private string? _expandedFilter;

    // ==================== ESTADÍSTICAS ABSOLUTAS (CONTADORES) ====================
    private int _totalEventTagsCount;
    private int _totalUniqueEventTagsCount;
    private int _totalLabelTagsCount;
    private int _totalUniqueLabelTagsCount;
    private int _labeledVideosCount;
    private int _totalVideosForLabeling;
    private double _avgTagsPerSession;

    // ==================== TABLA DE TIEMPOS POR SECCIÓN ====================
    private bool _hasSectionTimes;
    private bool _showSectionTimesDifferences;

    // ==================== POPUP TABLA DETALLADA DE TIEMPOS ====================
    private bool _showDetailedTimesPopup;
    private string _detailedTimesHtml = string.Empty;
    private ReportOptions _reportOptions = ReportOptions.FullAnalysis();
    private bool _showReportOptionsPanel;
    private AthletePickerItem? _selectedDetailedTimesAthlete;
    private bool _isAthleteDropdownExpanded;
    private bool _isLoadingDetailedTimes;
    private bool _isExportingDetailedTimes;
    private bool _showCumulativeTimes;

    public VideosViewModel(
        DatabaseService databaseService,
        ITrashService trashService,
        StatisticsService statisticsService,
        ThumbnailService thumbnailService,
        ITableExportService tableExportService,
        ICloudBackendService cloudBackendService,
        IVideoClipUpdateNotifier videoClipUpdateNotifier,
        LayoutStateViewModel layout,
        BatchEditViewModel batchEdit,
        SmartFoldersViewModel smartFolders,
        RemoteLibraryViewModel remote)
    {
        _databaseService = databaseService;
        _trashService = trashService;
        _statisticsService = statisticsService;
        _thumbnailService = thumbnailService;
        _tableExportService = tableExportService;
        _cloudBackendService = cloudBackendService;
        _videoClipUpdateNotifier = videoClipUpdateNotifier;
        _layout = layout;
        _batchEdit = batchEdit;
        _smartFolders = smartFolders;
        _remote = remote;

        VideoTapCommand = new AsyncRelayCommand<VideoClip>(OnVideoTappedAsync);
        VideoLessonTapCommand = new AsyncRelayCommand<VideoLesson>(OnVideoLessonTappedAsync);
        ShareVideoLessonCommand = new AsyncRelayCommand<VideoLesson>(ShareVideoLessonAsync);
        DeleteVideoLessonCommand = new AsyncRelayCommand<VideoLesson>(DeleteVideoLessonAsync);
        PlayAsPlaylistCommand = new AsyncRelayCommand(PlayAsPlaylistAsync);
        ShareSelectedVideosCommand = new AsyncRelayCommand(ShareSelectedVideosAsync);
        DeleteSelectedVideosCommand = new AsyncRelayCommand(DeleteSelectedVideosAsync);
        PlayParallelAnalysisCommand = new AsyncRelayCommand(PlayParallelAnalysisAsync);
        PreviewParallelAnalysisCommand = new AsyncRelayCommand(PreviewParallelAnalysisAsync);
        ClearParallelAnalysisCommand = new RelayCommand(ClearParallelAnalysis);
        DropOnScreen1Command = new RelayCommand<VideoClip>(video => ParallelVideo1 = video);
        DropOnScreen2Command = new RelayCommand<VideoClip>(video => ParallelVideo2 = video);
        ToggleQuickAnalysisIsolatedModeCommand = new RelayCommand(() => IsQuickAnalysisIsolatedMode = !IsQuickAnalysisIsolatedMode);
        LoadMoreVideosCommand = new AsyncRelayCommand(LoadMoreVideosAsync);
        ClearFiltersCommand = new RelayCommand(() => ClearFilters());
        ToggleFilterItemCommand = new RelayCommand<object?>(ToggleFilterItem);
        TogglePlacesExpandedCommand = new RelayCommand(() => IsPlacesExpanded = !IsPlacesExpanded);
        ToggleAthletesExpandedCommand = new RelayCommand(() => IsAthletesExpanded = !IsAthletesExpanded);
        ToggleSectionsExpandedCommand = new RelayCommand(() => IsSectionsExpanded = !IsSectionsExpanded);
        ToggleTagsExpandedCommand = new RelayCommand(() => IsTagsExpanded = !IsTagsExpanded);

        ToggleSectionTimesViewCommand = new RelayCommand(() => ShowSectionTimesDifferences = !ShowSectionTimesDifferences);
        OpenDetailedTimesPopupCommand = new AsyncRelayCommand(OpenDetailedTimesPopupAsync);
        CloseDetailedTimesPopupCommand = new RelayCommand(() => ShowDetailedTimesPopup = false);
        ClearDetailedTimesAthleteCommand = new RelayCommand(() =>
        {
            SelectedDetailedTimesAthlete = null;
            IsAthleteDropdownExpanded = false;
        });
        ToggleAthleteDropdownCommand = new RelayCommand(() => IsAthleteDropdownExpanded = !IsAthleteDropdownExpanded);
        SelectDetailedTimesAthleteCommand = new RelayCommand<AthletePickerItem>(athlete =>
        {
            SelectedDetailedTimesAthlete = athlete;
            IsAthleteDropdownExpanded = false;
        });
        ToggleLapTimesModeCommand = new RelayCommand(() => ShowCumulativeTimes = !ShowCumulativeTimes);
        SetLapTimesModeCommand = new RelayCommand<string>(mode =>
        {
            ShowCumulativeTimes = string.Equals(mode, "acum", StringComparison.OrdinalIgnoreCase);
        });

        ExportDetailedTimesHtmlCommand = new AsyncRelayCommand(ExportDetailedTimesHtmlAsync);
        ExportDetailedTimesPdfCommand = new AsyncRelayCommand(ExportDetailedTimesPdfAsync);

        _layout.PropertyChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(ShowVideoGallery));
            OnPropertyChanged(nameof(ShowSectionTimesTable));
            OnPropertyChanged(nameof(TotalFilteredVideoCount));
            OnPropertyChanged(nameof(TotalAvailableVideoCount));
            OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
            OnPropertyChanged(nameof(VideoCountDisplayText));
        };

        SelectedSessionVideos.CollectionChanged += (_, __) => OnPropertyChanged(nameof(VideoCountDisplayText));
        VideoLessons.CollectionChanged += (_, __) =>
        {
            VideoLessonsCount = VideoLessons.Count;
            OnPropertyChanged(nameof(VideoCountDisplayText));
        };

        _videoClipUpdateNotifier.VideoClipUpdated += HandleVideoClipUpdated;
    }

    private async void HandleVideoClipUpdated(object? sender, int videoClipId)
    {
        await RefreshVideoClipInGalleryAsync(videoClipId);
        _modifiedVideoIds.Add(videoClipId);
        _hasStatsUpdatePending = true;
    }

    public void Configure(
        Func<Session?> getSelectedSession,
        Action<Session?> setSelectedSession,
        Func<List<Session>> getRecentSessionsSnapshot,
        Func<DashboardStats?> getStats,
        Action<DashboardStats?> setStats,
        Func<bool> getIsAllGallerySelected,
        Action<bool> setIsAllGallerySelected,
        Func<bool> getIsVideoLessonsSelected,
        Action<bool> setIsVideoLessonsSelected,
        Func<bool> getIsFavoriteVideosSelected,
        Action<bool> setIsFavoriteVideosSelected,
        Func<bool> getIsDiaryViewSelected,
        Action<bool> setIsDiaryViewSelected,
        Func<int?> getReferenceAthleteId,
        Func<Task> refreshTrashItemCountAsync,
        Action<int> setFavoriteSessionsCount)
    {
        _getSelectedSession = getSelectedSession;
        _setSelectedSession = setSelectedSession;
        _getRecentSessionsSnapshot = getRecentSessionsSnapshot;
        _getStats = getStats;
        _setStats = setStats;
        _getIsAllGallerySelected = getIsAllGallerySelected;
        _setIsAllGallerySelected = setIsAllGallerySelected;
        _getIsVideoLessonsSelected = getIsVideoLessonsSelected;
        _setIsVideoLessonsSelected = setIsVideoLessonsSelected;
        _getIsFavoriteVideosSelected = getIsFavoriteVideosSelected;
        _setIsFavoriteVideosSelected = setIsFavoriteVideosSelected;
        _getIsDiaryViewSelected = getIsDiaryViewSelected;
        _setIsDiaryViewSelected = setIsDiaryViewSelected;
        _getReferenceAthleteId = getReferenceAthleteId;
        _refreshTrashItemCountAsync = refreshTrashItemCountAsync;
        _setFavoriteSessionsCount = setFavoriteSessionsCount;
    }

    private Session? SelectedSession => _getSelectedSession?.Invoke();
    private bool IsAllGallerySelected => _getIsAllGallerySelected?.Invoke() ?? false;
    private bool IsVideoLessonsSelected => _getIsVideoLessonsSelected?.Invoke() ?? false;
    private bool IsFavoriteVideosSelected => _getIsFavoriteVideosSelected?.Invoke() ?? false;
    private bool IsDiaryViewSelected => _getIsDiaryViewSelected?.Invoke() ?? false;
    private int? ReferenceAthleteId => _getReferenceAthleteId?.Invoke();

    public ObservableCollection<VideoClip> SelectedSessionVideos { get; } = new();
    public ObservableCollection<VideoLesson> VideoLessons { get; } = new();

    public int VideoLessonsCount
    {
        get => _videoLessonsCount;
        private set => SetProperty(ref _videoLessonsCount, value);
    }

    public int FavoriteVideosCount
    {
        get => _favoriteVideosCount;
        private set => SetProperty(ref _favoriteVideosCount, value);
    }

    public int AllGalleryItemCount => _allVideosCache?.Count ?? _getStats?.Invoke()?.TotalVideos ?? 0;

    public bool IsLoadingSelectedSessionVideos
    {
        get => _isLoadingSelectedSessionVideos;
        private set => SetProperty(ref _isLoadingSelectedSessionVideos, value);
    }

    public bool ShowVideoGallery => !IsVideoLessonsSelected && !IsDiaryViewSelected && !_remote.IsAnyRemoteSectionSelected;

    public int VideoGalleryColumnSpan
    {
        get => _videoGalleryColumnSpan;
        set
        {
            var clamped = Math.Clamp(value, 1, 4);
            SetProperty(ref _videoGalleryColumnSpan, clamped);
        }
    }

    public bool IsSingleVideoMode
    {
        get => _isSingleVideoMode;
        set
        {
            if (SetProperty(ref _isSingleVideoMode, value))
            {
                if (value)
                {
                    _isQuadVideoMode = false;
                    OnPropertyChanged(nameof(IsQuadVideoMode));
                }
                OnPropertyChanged(nameof(IsParallelVideoMode));
                if (value)
                {
                    ParallelVideo2 = null;
                    ParallelVideo3 = null;
                    ParallelVideo4 = null;
                }
            }
        }
    }

    public bool IsParallelVideoMode
    {
        get => !_isSingleVideoMode && !_isQuadVideoMode;
        set
        {
            if (value && !IsParallelVideoMode)
            {
                _isSingleVideoMode = false;
                _isQuadVideoMode = false;
                OnPropertyChanged(nameof(IsSingleVideoMode));
                OnPropertyChanged(nameof(IsQuadVideoMode));
                OnPropertyChanged(nameof(IsParallelVideoMode));
                ParallelVideo3 = null;
                ParallelVideo4 = null;
            }
        }
    }

    public bool IsQuadVideoMode
    {
        get => _isQuadVideoMode;
        set
        {
            if (SetProperty(ref _isQuadVideoMode, value))
            {
                if (value)
                {
                    _isSingleVideoMode = false;
                    OnPropertyChanged(nameof(IsSingleVideoMode));
                }
                OnPropertyChanged(nameof(IsParallelVideoMode));
            }
        }
    }

    public bool IsQuickAnalysisIsolatedMode
    {
        get => _layout.IsQuickAnalysisIsolatedMode;
        set => _layout.IsQuickAnalysisIsolatedMode = value;
    }

    public bool IsHorizontalOrientation
    {
        get => _isHorizontalOrientation;
        set
        {
            if (SetProperty(ref _isHorizontalOrientation, value))
            {
                OnPropertyChanged(nameof(IsVerticalOrientation));
            }
        }
    }

    public bool IsVerticalOrientation
    {
        get => !_isHorizontalOrientation;
        set
        {
            if (value != !_isHorizontalOrientation)
            {
                IsHorizontalOrientation = !value;
            }
        }
    }

    public VideoClip? ParallelVideo1
    {
        get => _parallelVideo1;
        set
        {
            if (SetProperty(ref _parallelVideo1, value))
            {
                IsPreviewPlayer1Ready = false;
                OnPropertyChanged(nameof(HasParallelVideo1));
                OnPropertyChanged(nameof(ParallelVideo1ClipPath));
                OnPropertyChanged(nameof(ParallelVideo1ThumbnailPath));
                OnPropertyChanged(nameof(ShowParallelVideo1Thumbnail));
                if (value != null)
                    _ = RefreshPreviewModeAsync();
            }
        }
    }

    public VideoClip? ParallelVideo2
    {
        get => _parallelVideo2;
        set
        {
            if (SetProperty(ref _parallelVideo2, value))
            {
                IsPreviewPlayer2Ready = false;
                OnPropertyChanged(nameof(HasParallelVideo2));
                OnPropertyChanged(nameof(ParallelVideo2ClipPath));
                OnPropertyChanged(nameof(ParallelVideo2ThumbnailPath));
                OnPropertyChanged(nameof(ShowParallelVideo2Thumbnail));
                if (value != null)
                    _ = RefreshPreviewModeAsync();
            }
        }
    }

    public VideoClip? ParallelVideo3
    {
        get => _parallelVideo3;
        set
        {
            if (SetProperty(ref _parallelVideo3, value))
            {
                IsPreviewPlayer3Ready = false;
                OnPropertyChanged(nameof(HasParallelVideo3));
                OnPropertyChanged(nameof(ParallelVideo3ClipPath));
                OnPropertyChanged(nameof(ParallelVideo3ThumbnailPath));
                OnPropertyChanged(nameof(ShowParallelVideo3Thumbnail));
                if (value != null)
                    _ = RefreshPreviewModeAsync();
            }
        }
    }

    public VideoClip? ParallelVideo4
    {
        get => _parallelVideo4;
        set
        {
            if (SetProperty(ref _parallelVideo4, value))
            {
                IsPreviewPlayer4Ready = false;
                OnPropertyChanged(nameof(HasParallelVideo4));
                OnPropertyChanged(nameof(ParallelVideo4ClipPath));
                OnPropertyChanged(nameof(ParallelVideo4ThumbnailPath));
                OnPropertyChanged(nameof(ShowParallelVideo4Thumbnail));
                if (value != null)
                    _ = RefreshPreviewModeAsync();
            }
        }
    }

    public bool HasParallelVideo1 => _parallelVideo1 != null;
    public bool HasParallelVideo2 => _parallelVideo2 != null;
    public bool HasParallelVideo3 => _parallelVideo3 != null;
    public bool HasParallelVideo4 => _parallelVideo4 != null;

    public string? ParallelVideo1ClipPath => _parallelVideo1?.LocalClipPath ?? _parallelVideo1?.ClipPath;
    public string? ParallelVideo1ThumbnailPath => _parallelVideo1?.LocalThumbnailPath ?? _parallelVideo1?.ThumbnailPath;
    public string? ParallelVideo2ClipPath => _parallelVideo2?.LocalClipPath ?? _parallelVideo2?.ClipPath;
    public string? ParallelVideo2ThumbnailPath => _parallelVideo2?.LocalThumbnailPath ?? _parallelVideo2?.ThumbnailPath;
    public string? ParallelVideo3ClipPath => _parallelVideo3?.LocalClipPath ?? _parallelVideo3?.ClipPath;
    public string? ParallelVideo3ThumbnailPath => _parallelVideo3?.LocalThumbnailPath ?? _parallelVideo3?.ThumbnailPath;
    public string? ParallelVideo4ClipPath => _parallelVideo4?.LocalClipPath ?? _parallelVideo4?.ClipPath;
    public string? ParallelVideo4ThumbnailPath => _parallelVideo4?.LocalThumbnailPath ?? _parallelVideo4?.ThumbnailPath;

    public bool IsPreviewMode
    {
        get => _isPreviewMode;
        set
        {
            if (SetProperty(ref _isPreviewMode, value))
            {
                OnPropertyChanged(nameof(ShowParallelVideo1Thumbnail));
                OnPropertyChanged(nameof(ShowParallelVideo2Thumbnail));
                OnPropertyChanged(nameof(ShowParallelVideo3Thumbnail));
                OnPropertyChanged(nameof(ShowParallelVideo4Thumbnail));
            }
        }
    }

    public bool IsPreviewPlayer1Ready
    {
        get => _isPreviewPlayer1Ready;
        set
        {
            if (SetProperty(ref _isPreviewPlayer1Ready, value))
                OnPropertyChanged(nameof(ShowParallelVideo1Thumbnail));
        }
    }

    public bool IsPreviewPlayer2Ready
    {
        get => _isPreviewPlayer2Ready;
        set
        {
            if (SetProperty(ref _isPreviewPlayer2Ready, value))
                OnPropertyChanged(nameof(ShowParallelVideo2Thumbnail));
        }
    }

    public bool IsPreviewPlayer3Ready
    {
        get => _isPreviewPlayer3Ready;
        set
        {
            if (SetProperty(ref _isPreviewPlayer3Ready, value))
                OnPropertyChanged(nameof(ShowParallelVideo3Thumbnail));
        }
    }

    public bool IsPreviewPlayer4Ready
    {
        get => _isPreviewPlayer4Ready;
        set
        {
            if (SetProperty(ref _isPreviewPlayer4Ready, value))
                OnPropertyChanged(nameof(ShowParallelVideo4Thumbnail));
        }
    }

    public bool ShowParallelVideo1Thumbnail => HasParallelVideo1 && (!IsPreviewMode || !IsPreviewPlayer1Ready);
    public bool ShowParallelVideo2Thumbnail => HasParallelVideo2 && (!IsPreviewMode || !IsPreviewPlayer2Ready);
    public bool ShowParallelVideo3Thumbnail => HasParallelVideo3 && (!IsPreviewMode || !IsPreviewPlayer3Ready);
    public bool ShowParallelVideo4Thumbnail => HasParallelVideo4 && (!IsPreviewMode || !IsPreviewPlayer4Ready);

    public VideoClip? HoverVideo
    {
        get => _hoverVideo;
        set
        {
            if (SetProperty(ref _hoverVideo, value))
                OnPropertyChanged(nameof(HasHoverVideo));
        }
    }

    public bool HasHoverVideo => _hoverVideo != null;

    public bool HasMoreVideos
    {
        get => _hasMoreVideos;
        private set => SetProperty(ref _hasMoreVideos, value);
    }

    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        private set => SetProperty(ref _isLoadingMore, value);
    }

    public ObservableCollection<PlaceFilterItem> FilterPlaces { get; } = new();
    public ObservableCollection<AthleteFilterItem> FilterAthletes { get; } = new();
    public ObservableCollection<SectionFilterItem> FilterSections { get; } = new();
    public ObservableCollection<TagFilterItem> FilterTagItems { get; } = new();

    public bool IsPlacesExpanded
    {
        get => _expandedFilter == "Places";
        set { if (value) ExpandFilter("Places"); else if (_expandedFilter == "Places") _expandedFilter = null; OnPropertyChanged(); }
    }

    public bool IsAthletesExpanded
    {
        get => _expandedFilter == "Athletes";
        set { if (value) ExpandFilter("Athletes"); else if (_expandedFilter == "Athletes") _expandedFilter = null; OnPropertyChanged(); }
    }

    public bool IsSectionsExpanded
    {
        get => _expandedFilter == "Sections";
        set { if (value) ExpandFilter("Sections"); else if (_expandedFilter == "Sections") _expandedFilter = null; OnPropertyChanged(); }
    }

    public bool IsTagsExpanded
    {
        get => _expandedFilter == "Tags";
        set { if (value) ExpandFilter("Tags"); else if (_expandedFilter == "Tags") _expandedFilter = null; OnPropertyChanged(); }
    }

    public string SelectedPlacesSummary => GetSelectedPlacesSummary();
    public string SelectedAthletesSummary => GetSelectedAthletesSummary();
    public string SelectedSectionsSummary => GetSelectedSectionsSummary();
    public string SelectedTagsSummary => GetSelectedTagsSummary();

    public DateTime? SelectedFilterDateFrom
    {
        get => _selectedFilterDateFrom;
        set
        {
            if (SetProperty(ref _selectedFilterDateFrom, value))
                _ = ApplyFiltersAsync();
        }
    }

    public DateTime? SelectedFilterDateTo
    {
        get => _selectedFilterDateTo;
        set
        {
            if (SetProperty(ref _selectedFilterDateTo, value))
                _ = ApplyFiltersAsync();
        }
    }

    public ObservableCollection<SectionStats> SelectedSessionSectionStats { get; } = new();
    public ObservableCollection<SessionAthleteTimeRow> SelectedSessionAthleteTimes { get; } = new();
    public ObservableCollection<Input> SelectedSessionInputs { get; } = new();
    public ObservableCollection<Valoracion> SelectedSessionValoraciones { get; } = new();
    public ObservableCollection<double> SelectedSessionSectionDurationMinutes { get; } = new();
    public ObservableCollection<string> SelectedSessionSectionLabels { get; } = new();
    public ObservableCollection<double> SelectedSessionTagVideoCounts { get; } = new();
    public ObservableCollection<string> SelectedSessionTagLabels { get; } = new();
    public ObservableCollection<double> SelectedSessionPenaltyCounts { get; } = new();
    public ObservableCollection<string> SelectedSessionPenaltyLabels { get; } = new();

    public int TotalEventTagsCount
    {
        get => _totalEventTagsCount;
        set => SetProperty(ref _totalEventTagsCount, value);
    }

    public int TotalUniqueEventTagsCount
    {
        get => _totalUniqueEventTagsCount;
        set => SetProperty(ref _totalUniqueEventTagsCount, value);
    }

    public int TotalLabelTagsCount
    {
        get => _totalLabelTagsCount;
        set => SetProperty(ref _totalLabelTagsCount, value);
    }

    public int TotalUniqueLabelTagsCount
    {
        get => _totalUniqueLabelTagsCount;
        set => SetProperty(ref _totalUniqueLabelTagsCount, value);
    }

    public int LabeledVideosCount
    {
        get => _labeledVideosCount;
        set => SetProperty(ref _labeledVideosCount, value);
    }

    public int TotalVideosForLabeling
    {
        get => _totalVideosForLabeling;
        set => SetProperty(ref _totalVideosForLabeling, value);
    }

    public double AvgTagsPerSession
    {
        get => _avgTagsPerSession;
        set => SetProperty(ref _avgTagsPerSession, value);
    }

    public string LabeledVideosText => TotalVideosForLabeling > 0
        ? $"{LabeledVideosCount} de {TotalVideosForLabeling}"
        : "0";

    public double LabeledVideosPercentage => TotalVideosForLabeling > 0
        ? (double)LabeledVideosCount / TotalVideosForLabeling * 100
        : 0;

    public ObservableCollection<TagUsageRow> TopEventTags { get; } = new();
    public ObservableCollection<double> TopEventTagValues { get; } = new();
    public ObservableCollection<string> TopEventTagNames { get; } = new();
    public ObservableCollection<TagUsageRow> TopLabelTags { get; } = new();
    public ObservableCollection<double> TopLabelTagValues { get; } = new();
    public ObservableCollection<string> TopLabelTagNames { get; } = new();

    public ObservableCollection<SectionWithAthleteRows> SectionTimes { get; } = new();

    public bool HasSectionTimes
    {
        get => _hasSectionTimes;
        set => SetProperty(ref _hasSectionTimes, value);
    }

    public bool ShowSectionTimesTable => !IsAllGallerySelected && !IsVideoLessonsSelected && SelectedSession != null && HasSectionTimes;

    public bool ShowSectionTimesDifferences
    {
        get => _showSectionTimesDifferences;
        set
        {
            if (SetProperty(ref _showSectionTimesDifferences, value))
            {
                OnPropertyChanged(nameof(ShowSectionTimesAbsolute));
                if (value && ReferenceAthleteId.HasValue)
                {
                    UpdateSectionTimeDifferences();
                }
            }
        }
    }

    public bool ShowSectionTimesAbsolute => !ShowSectionTimesDifferences;

    public ICommand ToggleSectionTimesViewCommand { get; }

    public bool ShowDetailedTimesPopup
    {
        get => _showDetailedTimesPopup;
        set => SetProperty(ref _showDetailedTimesPopup, value);
    }

    public string DetailedTimesHtml
    {
        get => _detailedTimesHtml;
        set => SetProperty(ref _detailedTimesHtml, value);
    }

    public ObservableCollection<SectionWithDetailedAthleteRows> DetailedSectionTimes { get; } = new();

    public ReportOptions ReportOptions
    {
        get => _reportOptions;
        set
        {
            if (SetProperty(ref _reportOptions, value))
            {
                RegenerateDetailedTimesHtml();
            }
        }
    }

    public bool ShowReportOptionsPanel
    {
        get => _showReportOptionsPanel;
        set => SetProperty(ref _showReportOptionsPanel, value);
    }

    public ICommand ToggleReportOptionsPanelCommand => new RelayCommand(() => ShowReportOptionsPanel = !ShowReportOptionsPanel);

    public ICommand ApplyFullAnalysisPresetCommand => new RelayCommand(() =>
    {
        ReportOptions = ReportOptions.FullAnalysis();
        OnPropertyChanged(nameof(ReportOptions));
        RegenerateDetailedTimesHtml();
    });

    public ICommand ApplyQuickSummaryPresetCommand => new RelayCommand(() =>
    {
        ReportOptions = ReportOptions.QuickSummary();
        OnPropertyChanged(nameof(ReportOptions));
        RegenerateDetailedTimesHtml();
    });

    public ICommand ApplyAthleteReportPresetCommand => new RelayCommand(() =>
    {
        ReportOptions = ReportOptions.AthleteReport();
        OnPropertyChanged(nameof(ReportOptions));
        RegenerateDetailedTimesHtml();
    });

    public ICommand UpdateReportOptionCommand => new RelayCommand<string>(_ => RegenerateDetailedTimesHtml());

    public ObservableCollection<AthletePickerItem> DetailedTimesAthletes { get; } = new();

    public AthletePickerItem? SelectedDetailedTimesAthlete
    {
        get => _selectedDetailedTimesAthlete;
        set
        {
            if (SetProperty(ref _selectedDetailedTimesAthlete, value))
            {
                OnPropertyChanged(nameof(HasSelectedDetailedTimesAthlete));
                OnPropertyChanged(nameof(SelectedDetailedTimesAthleteName));
                RegenerateDetailedTimesHtml();
            }
        }
    }

    public bool HasSelectedDetailedTimesAthlete => SelectedDetailedTimesAthlete != null;

    public string SelectedDetailedTimesAthleteName => SelectedDetailedTimesAthlete?.DisplayName ?? "Selecciona atleta";

    public bool IsAthleteDropdownExpanded
    {
        get => _isAthleteDropdownExpanded;
        set => SetProperty(ref _isAthleteDropdownExpanded, value);
    }

    public ICommand ToggleAthleteDropdownCommand { get; }
    public ICommand SelectDetailedTimesAthleteCommand { get; }
    public ICommand ClearDetailedTimesAthleteCommand { get; }

    public bool IsLoadingDetailedTimes
    {
        get => _isLoadingDetailedTimes;
        set => SetProperty(ref _isLoadingDetailedTimes, value);
    }

    public bool IsExportingDetailedTimes
    {
        get => _isExportingDetailedTimes;
        set => SetProperty(ref _isExportingDetailedTimes, value);
    }

    public bool HasDetailedTimesWithLaps => DetailedSectionTimes.Any(s => s.HasAnyLaps);

    public ICommand OpenDetailedTimesPopupCommand { get; }
    public ICommand CloseDetailedTimesPopupCommand { get; }

    public bool ShowCumulativeTimes
    {
        get => _showCumulativeTimes;
        set
        {
            if (SetProperty(ref _showCumulativeTimes, value))
            {
                OnPropertyChanged(nameof(ShowLapTimes));
                OnPropertyChanged(nameof(LapTimeModeText));
            }
        }
    }

    public bool ShowLapTimes => !ShowCumulativeTimes;

    public string LapTimeModeText => ShowCumulativeTimes ? "Acum." : "Parcial";

    public ICommand ToggleLapTimesModeCommand { get; }
    public ICommand SetLapTimesModeCommand { get; }

    public ICommand ExportDetailedTimesHtmlCommand { get; }
    public ICommand ExportDetailedTimesPdfCommand { get; }

    public int TotalFilteredVideoCount => IsVideoLessonsSelected
        ? VideoLessons.Count
        : ((_filteredVideosCache ?? _allVideosCache)?.Count ?? SelectedSessionVideos.Count);

    public int TotalAvailableVideoCount => IsVideoLessonsSelected
        ? VideoLessons.Count
        : (_allVideosCache?.Count ?? SelectedSessionVideos.Count);

    public double TotalFilteredDurationSeconds => IsVideoLessonsSelected
        ? 0
        : ((_filteredVideosCache ?? _allVideosCache)?.Sum(v => v.ClipDuration) ?? SelectedSessionVideos.Sum(v => v.ClipDuration));

    public double SelectedSessionTotalDurationSeconds => TotalFilteredDurationSeconds;

    public string SelectedSessionTotalDurationFormatted
    {
        get
        {
            var totalSeconds = SelectedSessionTotalDurationSeconds;
            var ts = TimeSpan.FromSeconds(totalSeconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }

    public string VideoCountDisplayText
    {
        get
        {
            if (IsVideoLessonsSelected)
            {
                var shownLessons = VideoLessons.Count;
                return $"{shownLessons} videolecciones";
            }

            var shown = SelectedSessionVideos.Count;
            var total = TotalFilteredVideoCount;
            return shown == total
                ? $"{shown} vídeos"
                : $"{shown} de {total} vídeos";
        }
    }

    public List<VideoClip>? AllVideosCache => _allVideosCache;
    public List<VideoClip>? FilteredVideosCache => _filteredVideosCache;

    public void InsertVideoIntoAllCache(VideoClip video)
    {
        _allVideosCache?.Insert(0, video);
    }

    public void RemoveVideoFromCaches(VideoClip video)
    {
        if (video == null)
            return;

        if (_allVideosCache != null)
        {
            var cached = _allVideosCache.FirstOrDefault(v => v.Id == video.Id);
            if (cached != null)
                _allVideosCache.Remove(cached);
        }

        if (_filteredVideosCache != null)
        {
            var cached = _filteredVideosCache.FirstOrDefault(v => v.Id == video.Id);
            if (cached != null)
                _filteredVideosCache.Remove(cached);
        }

        if (_favoriteVideosCache != null)
        {
            var cached = _favoriteVideosCache.FirstOrDefault(v => v.Id == video.Id);
            if (cached != null)
                _favoriteVideosCache.Remove(cached);
        }

        var existing = SelectedSessionVideos.FirstOrDefault(v => v.Id == video.Id);
        if (existing != null)
            SelectedSessionVideos.Remove(existing);

        OnPropertyChanged(nameof(AllGalleryItemCount));
        OnPropertyChanged(nameof(VideoCountDisplayText));
        OnPropertyChanged(nameof(TotalFilteredVideoCount));
        OnPropertyChanged(nameof(TotalAvailableVideoCount));
        OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
    }

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(ShowVideoGallery));
        OnPropertyChanged(nameof(ShowSectionTimesTable));
        OnPropertyChanged(nameof(VideoCountDisplayText));
        OnPropertyChanged(nameof(TotalFilteredVideoCount));
        OnPropertyChanged(nameof(TotalAvailableVideoCount));
        OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
    }

    public void NotifySelectedSessionVideosChanged()
    {
        OnPropertyChanged(nameof(SelectedSessionVideos));
        OnPropertyChanged(nameof(VideoCountDisplayText));
    }

    public int IncrementFiltersVersion() => Interlocked.Increment(ref _filtersVersion);

    public int ReadFiltersVersion() => Volatile.Read(ref _filtersVersion);

    public ICommand VideoTapCommand { get; }
    public ICommand VideoLessonTapCommand { get; }
    public ICommand ShareVideoLessonCommand { get; }
    public ICommand DeleteVideoLessonCommand { get; }
    public ICommand PlayAsPlaylistCommand { get; }
    public ICommand ShareSelectedVideosCommand { get; }
    public ICommand DeleteSelectedVideosCommand { get; }
    public ICommand PlayParallelAnalysisCommand { get; }
    public ICommand PreviewParallelAnalysisCommand { get; }
    public ICommand ClearParallelAnalysisCommand { get; }
    public ICommand DropOnScreen1Command { get; }
    public ICommand DropOnScreen2Command { get; }
    public ICommand ToggleQuickAnalysisIsolatedModeCommand { get; }
    public ICommand LoadMoreVideosCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ToggleFilterItemCommand { get; }
    public ICommand TogglePlacesExpandedCommand { get; }
    public ICommand ToggleAthletesExpandedCommand { get; }
    public ICommand ToggleSectionsExpandedCommand { get; }
    public ICommand ToggleTagsExpandedCommand { get; }

    public async Task SetParallelVideoSlotAsync(int slot, VideoClip video)
    {
        if (video == null) return;

        var videoCopy = new VideoClip
        {
            Id = video.Id,
            SessionId = video.SessionId,
            AtletaId = video.AtletaId,
            Section = video.Section,
            CreationDate = video.CreationDate,
            ClipPath = video.ClipPath,
            LocalClipPath = video.LocalClipPath,
            ThumbnailPath = video.ThumbnailPath,
            LocalThumbnailPath = video.LocalThumbnailPath,
            ComparisonName = video.ComparisonName,
            ClipDuration = video.ClipDuration,
            ClipSize = video.ClipSize,
            IsDeleted = video.IsDeleted,
            DeletedAtUtc = video.DeletedAtUtc,
            Atleta = video.Atleta,
            Session = video.Session,
            Tags = video.Tags,
            EventTags = video.EventTags
        };

        await ResolveVideoPathsAsync(videoCopy);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            switch (slot)
            {
                case 1: ParallelVideo1 = videoCopy; break;
                case 2: ParallelVideo2 = videoCopy; break;
                case 3: ParallelVideo3 = videoCopy; break;
                case 4: ParallelVideo4 = videoCopy; break;
            }
        });
    }

    private async Task ResolveVideoPathsAsync(VideoClip video)
    {
        try
        {
            var session = await _databaseService.GetSessionByIdAsync(video.SessionId);
            if (session == null || string.IsNullOrWhiteSpace(session.PathSesion))
                return;

            var videoPath = video.LocalClipPath;
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                var normalized = (video.ClipPath ?? "").Replace('\\', '/');
                var fileName = Path.GetFileName(normalized);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = $"CROWN{video.Id}.mp4";

                var candidate = Path.Combine(session.PathSesion, "videos", fileName);
                if (File.Exists(candidate))
                    video.LocalClipPath = candidate;
                else if (!string.IsNullOrWhiteSpace(video.ClipPath))
                    video.LocalClipPath = video.ClipPath;
            }

            var thumbnailPath = video.LocalThumbnailPath;
            if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
            {
                var normalized = (video.ThumbnailPath ?? "").Replace('\\', '/');
                var fileName = Path.GetFileName(normalized);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = $"CROWN{video.Id}.jpg";

                var candidate = Path.Combine(session.PathSesion, "thumbnails", fileName);
                if (File.Exists(candidate))
                    video.LocalThumbnailPath = candidate;
                else if (!string.IsNullOrWhiteSpace(video.ThumbnailPath))
                    video.LocalThumbnailPath = video.ThumbnailPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Videos] Error resolving video paths: {ex.Message}");
        }
    }

    public void ClearPreviewVideos()
    {
        ParallelVideo1 = null;
        ParallelVideo2 = null;
        ParallelVideo3 = null;
        ParallelVideo4 = null;
        IsPreviewMode = false;

        IsPreviewPlayer1Ready = false;
        IsPreviewPlayer2Ready = false;
        IsPreviewPlayer3Ready = false;
        IsPreviewPlayer4Ready = false;
    }

    private async Task RefreshPreviewModeAsync()
    {
        IsPreviewMode = false;
        await Task.Delay(50);
        IsPreviewMode = true;
    }

    private void ExpandFilter(string filterName)
    {
        _expandedFilter = filterName;
        OnPropertyChanged(nameof(IsPlacesExpanded));
        OnPropertyChanged(nameof(IsAthletesExpanded));
        OnPropertyChanged(nameof(IsSectionsExpanded));
        OnPropertyChanged(nameof(IsTagsExpanded));
    }

    private string GetSelectedPlacesSummary()
    {
        var selected = FilterPlaces.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return "Todos los lugares";
        if (selected.Count == 1) return selected[0].DisplayName;
        return $"{selected.Count} lugares";
    }

    private string GetSelectedAthletesSummary()
    {
        var selected = FilterAthletes.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return "Todos los deportistas";
        if (selected.Count == 1) return selected[0].DisplayName;
        return $"{selected.Count} deportistas";
    }

    private string GetSelectedSectionsSummary()
    {
        var selected = FilterSections.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return "Todas las secciones";
        if (selected.Count == 1) return selected[0].DisplayName;
        return $"{selected.Count} secciones";
    }

    private string GetSelectedTagsSummary()
    {
        var selected = FilterTagItems.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0) return "Todos los tags";
        if (selected.Count == 1) return selected[0].DisplayName;
        return $"{selected.Count} tags";
    }

    public void ClearFilters(bool skipApplyFilters = false)
    {
        _suppressFilterSelectionChanged = true;
        try
        {
            foreach (var place in FilterPlaces) place.IsSelected = false;
            foreach (var athlete in FilterAthletes) athlete.IsSelected = false;
            foreach (var section in FilterSections) section.IsSelected = false;
            foreach (var tag in FilterTagItems) tag.IsSelected = false;
        }
        finally
        {
            _suppressFilterSelectionChanged = false;
        }

        _selectedFilterDateFrom = null;
        _selectedFilterDateTo = null;

        OnPropertyChanged(nameof(SelectedFilterDateFrom));
        OnPropertyChanged(nameof(SelectedFilterDateTo));
        OnPropertyChanged(nameof(SelectedPlacesSummary));
        OnPropertyChanged(nameof(SelectedAthletesSummary));
        OnPropertyChanged(nameof(SelectedSectionsSummary));
        OnPropertyChanged(nameof(SelectedTagsSummary));

        if (!skipApplyFilters)
            _ = ApplyFiltersAsync();
    }

    public void ToggleFilterItem(object? item)
    {
        try
        {
            if (item == null)
                return;

            var prop = item.GetType().GetProperty("IsSelected");
            if (prop == null || prop.PropertyType != typeof(bool) || !prop.CanWrite)
                return;

            var current = (bool)(prop.GetValue(item) ?? false);
            prop.SetValue(item, !current);
        }
        catch
        {
        }
    }

    private bool _suppressFilterSelectionChanged;
    private int _filtersVersion;

    private async Task ApplyFiltersAsync()
    {
        if ((!IsAllGallerySelected && !IsFavoriteVideosSelected) || _allVideosCache == null)
            return;

        var version = Interlocked.Increment(ref _filtersVersion);

        var baseVideos = IsFavoriteVideosSelected
            ? (_favoriteVideosCache ?? _allVideosCache.Where(v => v.IsFavorite == 1).ToList())
            : (_smartFolders.ActiveSmartFolder != null && _smartFolders.SmartFolderFilteredVideosCache != null
                ? _smartFolders.SmartFolderFilteredVideosCache
                : _allVideosCache);
        var allVideos = baseVideos;
        var sessionsSnapshot = _getRecentSessionsSnapshot?.Invoke() ?? new List<Session>();
        var inputsSnapshot = _allInputsCache;

        var selectedPlaces = FilterPlaces.Where(p => p.IsSelected).Select(p => p.Value).ToList();
        var selectedAthletes = FilterAthletes.Where(a => a.IsSelected).Select(a => a.Value.Id).ToList();
        var selectedSections = FilterSections.Where(s => s.IsSelected).Select(s => s.Value).ToList();
        var selectedTags = FilterTagItems.Where(t => t.IsSelected).Select(t => t.Value.Id).ToList();

        await Task.Yield();

        var (filteredList, statsSnapshot) = await Task.Run(() =>
        {
            IEnumerable<VideoClip> query = allVideos;

            if (selectedPlaces.Any())
            {
                var sessionsInPlaces = sessionsSnapshot
                    .Where(s =>
                    {
                        var lugar = s.Lugar;
                        return !string.IsNullOrEmpty(lugar) && selectedPlaces.Contains(lugar);
                    })
                    .Select(s => s.Id)
                    .ToHashSet();
                query = query.Where(v => sessionsInPlaces.Contains(v.SessionId));
            }

            var filterFrom = SelectedFilterDateFrom;
            if (filterFrom is DateTime from)
                query = query.Where(v => v.CreationDateTime >= from);

            var filterTo = SelectedFilterDateTo;
            if (filterTo is DateTime to)
                query = query.Where(v => v.CreationDateTime <= to.AddDays(1));

            if (selectedAthletes.Any())
                query = query.Where(v => selectedAthletes.Contains(v.AtletaId));

            if (selectedSections.Any())
                query = query.Where(v => selectedSections.Contains(v.Section));

            if (selectedTags.Any() && inputsSnapshot != null)
            {
                var videoIdsWithTags = inputsSnapshot
                    .Where(i => selectedTags.Contains(i.InputTypeId))
                    .Select(i => i.VideoId)
                    .ToHashSet();
                query = query.Where(v => videoIdsWithTags.Contains(v.Id));
            }

            var list = query.ToList();
            var snapshot = BuildGalleryStatsSnapshot(list);
            return (list, snapshot);
        });

        if (version != Volatile.Read(ref _filtersVersion))
            return;

        _filteredVideosCache = filteredList;

        _currentPage = 0;

        var filteredCache = _filteredVideosCache ?? new List<VideoClip>();
        var firstBatch = filteredCache.Take(PageSize).ToList();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, CancellationToken.None);
        });
        _currentPage = 1;
        HasMoreVideos = (_filteredVideosCache?.Count ?? 0) > PageSize;

        await ApplyGalleryStatsSnapshotAsync(statsSnapshot, CancellationToken.None);

        OnPropertyChanged(nameof(TotalFilteredVideoCount));
        OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
    }

    public async Task ApplySmartFolderFilteredResultsAsync(List<VideoClip> filtered)
    {
        _filteredVideosCache = filtered;

        _currentPage = 0;
        var firstBatch = filtered.Take(PageSize).ToList();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, CancellationToken.None);
        });
        _currentPage = 1;
        HasMoreVideos = filtered.Count > PageSize;

        var snapshot = await Task.Run(() => BuildGalleryStatsSnapshot(filtered));
        await ApplyGalleryStatsSnapshotAsync(snapshot, CancellationToken.None);

        var uniqueSessionIds = filtered.Select(v => v.SessionId).Distinct().Count();
        var uniqueAthleteIds = filtered.Where(v => v.AtletaId != 0).Select(v => v.AtletaId).Distinct().Count();
        var totalDuration = filtered.Sum(v => v.ClipDuration);

        _setStats?.Invoke(new DashboardStats
        {
            TotalSessions = uniqueSessionIds,
            TotalVideos = filtered.Count,
            TotalAthletes = uniqueAthleteIds,
            TotalDurationSeconds = totalDuration
        });

        OnPropertyChanged(nameof(TotalFilteredVideoCount));
        OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
        OnPropertyChanged(nameof(VideoCountDisplayText));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
    }

    private static async Task ReplaceCollectionInBatchesAsync<T>(
        ObservableCollection<T> target,
        IEnumerable<T> items,
        CancellationToken ct,
        int batchSize = 20)
    {
        target.Clear();

        var i = 0;
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested)
                return;

            target.Add(item);
            i++;

            if (i % batchSize == 0)
                await Task.Yield();
        }
    }

    private static GalleryStatsSnapshot BuildGalleryStatsSnapshot(List<VideoClip> clips)
    {
        var sectionStats = clips
            .GroupBy(v => v.Section)
            .OrderBy(g => g.Key)
            .Select(g => new SectionStats
            {
                Section = g.Key,
                VideoCount = g.Count(),
                TotalDuration = g.Sum(v => v.ClipDuration)
            })
            .ToList();
        var sectionMinutes = sectionStats
            .Select(s => Math.Round(s.TotalDuration / 60.0, 1))
            .ToList();
        var sectionLabels = sectionStats.Select(s => s.Section.ToString()).ToList();

        var byAthlete = clips
            .Where(v => v.AtletaId != 0)
            .GroupBy(v => v.AtletaId)
            .Select(g => new SessionAthleteTimeRow(
                g.FirstOrDefault()?.Atleta?.NombreCompleto ?? $"Atleta {g.Key}",
                g.Count(),
                g.Sum(v => v.ClipDuration),
                g.Any() ? g.Average(v => v.ClipDuration) : 0))
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        return new GalleryStatsSnapshot(sectionStats, sectionMinutes, sectionLabels, byAthlete);
    }

    private async Task ApplyGalleryStatsSnapshotAsync(GalleryStatsSnapshot snapshot, CancellationToken ct)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (ct.IsCancellationRequested)
                return;

            SelectedSessionSectionStats.Clear();
            SelectedSessionAthleteTimes.Clear();
            SelectedSessionSectionDurationMinutes.Clear();
            SelectedSessionSectionLabels.Clear();
            SelectedSessionTagVideoCounts.Clear();
            SelectedSessionTagLabels.Clear();

            var i = 0;
            foreach (var s in snapshot.SectionStats)
            {
                if (ct.IsCancellationRequested)
                    return;

                SelectedSessionSectionStats.Add(s);
                i++;
                if (i % 20 == 0)
                    await Task.Yield();
            }

            foreach (var m in snapshot.SectionDurationMinutes)
                SelectedSessionSectionDurationMinutes.Add(m);
            foreach (var l in snapshot.SectionLabels)
                SelectedSessionSectionLabels.Add(l);

            i = 0;
            foreach (var row in snapshot.AthleteTimes)
            {
                if (ct.IsCancellationRequested)
                    return;

                SelectedSessionAthleteTimes.Add(row);
                i++;
                if (i % 20 == 0)
                    await Task.Yield();
            }
        });
    }

    private sealed record GalleryStatsSnapshot(
        List<SectionStats> SectionStats,
        List<double> SectionDurationMinutes,
        List<string> SectionLabels,
        List<SessionAthleteTimeRow> AthleteTimes);

    public async Task LoadAllVideosAsync()
    {
        _selectedSessionVideosCts?.Cancel();
        _selectedSessionVideosCts?.Dispose();
        _selectedSessionVideosCts = new CancellationTokenSource();
        var ct = _selectedSessionVideosCts.Token;

        SelectedSessionVideos.Clear();
        SelectedSessionSectionStats.Clear();
        SelectedSessionAthleteTimes.Clear();
        SelectedSessionSectionDurationMinutes.Clear();
        SelectedSessionSectionLabels.Clear();
        SelectedSessionTagVideoCounts.Clear();
        SelectedSessionTagLabels.Clear();
        SelectedSessionPenaltyCounts.Clear();
        SelectedSessionPenaltyLabels.Clear();
        SelectedSessionInputs.Clear();
        SelectedSessionValoraciones.Clear();

        _currentPage = 0;
        _allVideosCache = null;
        _filteredVideosCache = null;
        HasMoreVideos = false;

        OnPropertyChanged(nameof(AllGalleryItemCount));

        ClearFilters();

        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));

        try
        {
            IsLoadingSelectedSessionVideos = true;
            await Task.Yield();

            _allVideosCache = await _databaseService.GetAllVideoClipsAsync();
            if (ct.IsCancellationRequested) return;

            OnPropertyChanged(nameof(AllGalleryItemCount));

            var sessionIds = _allVideosCache?.Select(c => c.SessionId).Distinct().ToList() ?? new List<int>();
            var sessionsDict = new Dictionary<int, Session>();
            foreach (var sessionId in sessionIds)
            {
                var session = await _databaseService.GetSessionByIdAsync(sessionId);
                if (session != null)
                    sessionsDict[sessionId] = session;
            }

            if (_allVideosCache != null)
            {
                foreach (var clip in _allVideosCache)
                {
                    if (sessionsDict.TryGetValue(clip.SessionId, out var session))
                        clip.Session = session;
                }
            }

            _filteredVideosCache = _allVideosCache;
            var filteredCache = _filteredVideosCache ?? new List<VideoClip>();
            var firstBatch = filteredCache.Take(PageSize).ToList();
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, ct);
            });
            _currentPage = 1;
            HasMoreVideos = (_filteredVideosCache?.Count ?? 0) > PageSize;

            var filterTask = LoadFilterOptionsAsync();

            var snapshot = await Task.Run(() => BuildGalleryStatsSnapshot(filteredCache), ct);
            if (!ct.IsCancellationRequested)
                await ApplyGalleryStatsSnapshotAsync(snapshot, ct);

            await LoadAbsoluteTagStatsAsync(null);

            await filterTask;

            OnPropertyChanged(nameof(TotalFilteredVideoCount));
            OnPropertyChanged(nameof(TotalAvailableVideoCount));
            OnPropertyChanged(nameof(VideoCountDisplayText));
            OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
            OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
            OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));

            _smartFolders.UpdateSmartFolderVideoCounts();
        }
        catch (Exception ex)
        {
            AppLog.Error("VideosViewModel", "Error loading all videos", ex);
        }
        finally
        {
            IsLoadingSelectedSessionVideos = false;
        }
    }

    public async Task LoadSelectedSessionVideosAsync(Session? session)
    {
        _selectedSessionVideosCts?.Cancel();
        _selectedSessionVideosCts?.Dispose();
        _selectedSessionVideosCts = new CancellationTokenSource();
        var ct = _selectedSessionVideosCts.Token;

        SelectedSessionVideos.Clear();
        SelectedSessionSectionStats.Clear();
        SelectedSessionAthleteTimes.Clear();
        SelectedSessionSectionDurationMinutes.Clear();
        SelectedSessionSectionLabels.Clear();
        SelectedSessionTagVideoCounts.Clear();
        SelectedSessionTagLabels.Clear();
        SelectedSessionPenaltyCounts.Clear();
        SelectedSessionPenaltyLabels.Clear();
        SelectedSessionInputs.Clear();
        SelectedSessionValoraciones.Clear();

        _currentPage = 0;
        _allVideosCache = null;
        _filteredVideosCache = null;
        HasMoreVideos = false;

        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
        if (session == null) return;

        try
        {
            IsLoadingSelectedSessionVideos = true;
            var clips = await _databaseService.GetVideoClipsBySessionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            foreach (var clip in clips)
            {
                clip.Session = session;
            }

            _allVideosCache = clips;

            var firstBatch = clips.Take(PageSize).ToList();
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, ct);
            });
            _currentPage = 1;
            HasMoreVideos = clips.Count > PageSize;

            var sectionStats = await _statisticsService.GetVideosBySectionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var s in sectionStats)
                {
                    SelectedSessionSectionStats.Add(s);
                    SelectedSessionSectionDurationMinutes.Add(Math.Round(s.TotalDuration / 60.0, 1));
                    SelectedSessionSectionLabels.Add(s.Section.ToString());

                    i++;
                    if (i % 20 == 0)
                        await Task.Yield();
                }
            });

            var byAthlete = clips
                .Where(v => v.AtletaId != 0)
                .GroupBy(v => v.AtletaId)
                .Select(g => new
                {
                    AthleteName = g.FirstOrDefault()?.Atleta?.NombreCompleto ?? $"Atleta {g.Key}",
                    VideoCount = g.Count(),
                    TotalSeconds = g.Sum(v => v.ClipDuration),
                    AvgSeconds = g.Any() ? g.Average(v => v.ClipDuration) : 0
                })
                .OrderByDescending(x => x.TotalSeconds)
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var row in byAthlete)
                {
                    SelectedSessionAthleteTimes.Add(new SessionAthleteTimeRow(
                        row.AthleteName,
                        row.VideoCount,
                        row.TotalSeconds,
                        row.AvgSeconds));

                    i++;
                    if (i % 30 == 0)
                        await Task.Yield();
                }
            });

            var inputs = await _databaseService.GetInputsBySessionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var input in inputs.OrderBy(i => i.InputDateTime))
                {
                    SelectedSessionInputs.Add(input);
                    i++;
                    if (i % 50 == 0)
                        await Task.Yield();
                }
            });

            var tagStats = inputs
                .GroupBy(i => i.InputTypeId)
                .Select(g => new
                {
                    TagId = g.Key,
                    Assignments = g.Count(),
                    VideoCount = g.Select(x => x.VideoId).Distinct().Count()
                })
                .OrderByDescending(x => x.VideoCount)
                .ThenBy(x => x.TagId)
                .Take(12)
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var t in tagStats)
                {
                    SelectedSessionTagLabels.Add(t.TagId.ToString());
                    SelectedSessionTagVideoCounts.Add(t.VideoCount);
                }
            });

            var penalty2 = inputs.Count(i => i.InputTypeId == 2);
            var penalty50 = inputs.Count(i => i.InputTypeId == 50);
            SelectedSessionPenaltyLabels.Add("2");
            SelectedSessionPenaltyLabels.Add("50");
            SelectedSessionPenaltyCounts.Add(penalty2);
            SelectedSessionPenaltyCounts.Add(penalty50);

            await LoadAbsoluteTagStatsAsync(session.Id);
            await LoadAthleteSectionTimesAsync(session.Id);

            var valoraciones = await _databaseService.GetValoracionesBySessionAsync(session.Id);
            if (ct.IsCancellationRequested) return;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var v in valoraciones.OrderBy(v => v.InputDateTime))
                {
                    SelectedSessionValoraciones.Add(v);
                    i++;
                    if (i % 50 == 0)
                        await Task.Yield();
                }
            });

            OnPropertyChanged(nameof(TotalFilteredVideoCount));
            OnPropertyChanged(nameof(TotalAvailableVideoCount));
            OnPropertyChanged(nameof(VideoCountDisplayText));
            OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
            OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar los vídeos: {ex.Message}", "OK");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoadingSelectedSessionVideos = false;
        }
    }

    private async Task LoadFilterOptionsAsync()
    {
        await Task.Yield();

        var sessionsSnapshot = _getRecentSessionsSnapshot?.Invoke() ?? new List<Session>();
        var allVideosSnapshot = _allVideosCache?.ToList();

        FilterPlaces.Clear();
        var places = await Task.Run(() => sessionsSnapshot
            .Where(s => !string.IsNullOrEmpty(s.Lugar))
            .Select(s => s.Lugar!)
            .Distinct()
            .OrderBy(p => p)
            .ToList());
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var i = 0;
            foreach (var place in places)
            {
                var item = new PlaceFilterItem(place);
                item.SelectionChanged += OnFilterSelectionChanged;
                FilterPlaces.Add(item);

                i++;
                if (i % 30 == 0)
                    await Task.Yield();
            }
        });

        FilterAthletes.Clear();
        var athletes = await _databaseService.GetAllAthletesAsync();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var i = 0;
            foreach (var athlete in athletes.OrderBy(a => a.NombreCompleto))
            {
                var item = new AthleteFilterItem(athlete);
                item.SelectionChanged += OnFilterSelectionChanged;
                FilterAthletes.Add(item);

                i++;
                if (i % 30 == 0)
                    await Task.Yield();
            }
        });

        FilterSections.Clear();
        if (allVideosSnapshot != null)
        {
            var sections = await Task.Run(() => allVideosSnapshot
                .Select(v => v.Section)
                .Distinct()
                .OrderBy(s => s)
                .ToList());
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var section in sections)
                {
                    var item = new SectionFilterItem(section);
                    item.SelectionChanged += OnFilterSelectionChanged;
                    FilterSections.Add(item);

                    i++;
                    if (i % 30 == 0)
                        await Task.Yield();
                }
            });
        }

        OnPropertyChanged(nameof(SelectedPlacesSummary));
        OnPropertyChanged(nameof(SelectedAthletesSummary));
        OnPropertyChanged(nameof(SelectedSectionsSummary));

        _allInputsCache = await _databaseService.GetAllInputsAsync();
        _allTagsCache = await _databaseService.GetAllTagsAsync();

        FilterTagItems.Clear();
        if (_allInputsCache != null && _allTagsCache != null)
        {
            var usedTagIds = _allInputsCache
                .Select(i => i.InputTypeId)
                .Distinct()
                .ToHashSet();

            var tags = _allTagsCache
                .Where(t => usedTagIds.Contains(t.Id) && !string.IsNullOrEmpty(t.NombreTag))
                .OrderBy(t => t.NombreTag);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var tag in tags)
                {
                    var item = new TagFilterItem(tag);
                    item.SelectionChanged += OnFilterSelectionChanged;
                    FilterTagItems.Add(item);

                    i++;
                    if (i % 30 == 0)
                        await Task.Yield();
                }
            });
        }

        OnPropertyChanged(nameof(SelectedTagsSummary));
    }

    private void OnFilterSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressFilterSelectionChanged)
            return;

        OnPropertyChanged(nameof(SelectedPlacesSummary));
        OnPropertyChanged(nameof(SelectedAthletesSummary));
        OnPropertyChanged(nameof(SelectedSectionsSummary));
        OnPropertyChanged(nameof(SelectedTagsSummary));
        _ = ApplyFiltersAsync();
    }

    public async Task LoadMoreVideosAsync()
    {
        var videosSource = _filteredVideosCache ?? _allVideosCache;

        if (IsLoadingMore || !HasMoreVideos || videosSource == null)
            return;

        try
        {
            IsLoadingMore = true;

            await Task.Delay(100);

            var skip = _currentPage * PageSize;
            var nextBatch = videosSource.Skip(skip).Take(PageSize).ToList();

            var ct = _selectedSessionVideosCts?.Token ?? CancellationToken.None;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var i = 0;
                foreach (var clip in nextBatch)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    SelectedSessionVideos.Add(clip);
                    i++;
                    if (i % 20 == 0)
                        await Task.Yield();
                }
            });

            _currentPage++;
            HasMoreVideos = videosSource.Count > (_currentPage * PageSize);
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private async Task LoadVideoLessonsAsync(CancellationToken ct)
    {
        await MainThread.InvokeOnMainThreadAsync(() => VideoLessons.Clear());

        var lessons = await _databaseService.GetAllVideoLessonsAsync();
        if (ct.IsCancellationRequested)
            return;

        var sessionNameCache = new Dictionary<int, string?>();

        var thumbnailsDir = Path.Combine(FileSystem.AppDataDirectory, "videoLessonThumbs");
        Directory.CreateDirectory(thumbnailsDir);

        var thumbTasks = new List<Task>();
        using var thumbSemaphore = new SemaphoreSlim(2);

        for (var idx = 0; idx < lessons.Count; idx++)
        {
            var lesson = lessons[idx];
            if (ct.IsCancellationRequested)
                return;

            if (!sessionNameCache.TryGetValue(lesson.SessionId, out var sessionName))
            {
                var session = lesson.SessionId > 0 ? await _databaseService.GetSessionByIdAsync(lesson.SessionId) : null;
                sessionName = session?.DisplayName;
                sessionNameCache[lesson.SessionId] = sessionName;
            }
            lesson.SessionDisplayName = sessionName;

            var thumbPath = Path.Combine(thumbnailsDir, $"lesson_{lesson.Id}.jpg");
            lesson.LocalThumbnailPath = File.Exists(thumbPath) ? thumbPath : null;

            await MainThread.InvokeOnMainThreadAsync(() => VideoLessons.Add(lesson));

            if (!File.Exists(thumbPath))
            {
                thumbTasks.Add(Task.Run(async () =>
                {
                    await thumbSemaphore.WaitAsync(ct);
                    try
                    {
                        if (!File.Exists(thumbPath))
                            await _thumbnailService.GenerateThumbnailAsync(lesson.FilePath, thumbPath);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        thumbSemaphore.Release();
                    }

                    if (ct.IsCancellationRequested)
                        return;

                    if (File.Exists(thumbPath))
                    {
                        lesson.LocalThumbnailPath = thumbPath;
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            var currentIndex = VideoLessons.IndexOf(lesson);
                            if (currentIndex >= 0)
                                VideoLessons[currentIndex] = lesson;
                        });
                    }
                }, ct));
            }

            if (idx % 10 == 0)
                await Task.Yield();
        }

        try
        {
            await Task.WhenAll(thumbTasks);
        }
        catch
        {
        }

        VideoLessonsCount = VideoLessons.Count;
    }

    public async Task RefreshVideoLessonsCountAsync()
    {
        try
        {
            VideoLessonsCount = await _databaseService.GetVideoLessonsCountAsync();
        }
        catch
        {
            VideoLessonsCount = VideoLessons.Count;
        }
    }

    public async Task OnVideoLessonTappedAsync(VideoLesson? lesson)
    {
        if (lesson == null)
            return;

        if (string.IsNullOrWhiteSpace(lesson.FilePath) || !File.Exists(lesson.FilePath))
        {
            await Shell.Current.DisplayAlert("Archivo no encontrado", "No se encontró el vídeo de esta videolección en el dispositivo.", "OK");
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(SinglePlayerPage)}?videoPath={Uri.EscapeDataString(lesson.FilePath)}");
    }

    public async Task ShareVideoLessonAsync(VideoLesson? lesson)
    {
        if (lesson == null)
            return;

        if (string.IsNullOrWhiteSpace(lesson.FilePath) || !File.Exists(lesson.FilePath))
        {
            await Shell.Current.DisplayAlert("Archivo no encontrado", "No se encontró el vídeo de esta videolección.", "OK");
            return;
        }

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = lesson.DisplayTitle ?? "Videolección",
            File = new ShareFile(lesson.FilePath)
        });
    }

    public async Task DeleteVideoLessonAsync(VideoLesson? lesson)
    {
        if (lesson == null)
            return;

        var confirm = await Shell.Current.DisplayAlert(
            "Eliminar videolección",
            $"¿Estás seguro de que quieres eliminar \"{lesson.DisplayTitle}\"?",
            "Eliminar",
            "Cancelar");

        if (!confirm)
            return;

        try
        {
            if (!string.IsNullOrWhiteSpace(lesson.FilePath) && File.Exists(lesson.FilePath))
                File.Delete(lesson.FilePath);

            var thumbPath = Path.Combine(FileSystem.AppDataDirectory, "videoLessonThumbs", $"lesson_{lesson.Id}.jpg");
            if (File.Exists(thumbPath))
                File.Delete(thumbPath);

            await _databaseService.DeleteVideoLessonAsync(lesson);

            await MainThread.InvokeOnMainThreadAsync(() => VideoLessons.Remove(lesson));
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo eliminar la videolección: {ex.Message}", "OK");
        }
    }

    public async Task RefreshVideoClipInGalleryAsync(int videoId)
    {
        try
        {
            var existingVideo = SelectedSessionVideos.FirstOrDefault(v => v.Id == videoId);
            if (existingVideo == null) return;

            var index = SelectedSessionVideos.IndexOf(existingVideo);
            if (index < 0) return;

            var updatedVideo = await _databaseService.GetVideoClipByIdAsync(videoId);
            if (updatedVideo == null) return;

            if (updatedVideo.AtletaId > 0)
            {
                updatedVideo.Atleta = await _databaseService.GetAthleteByIdAsync(updatedVideo.AtletaId);
            }
            if (updatedVideo.SessionId > 0)
            {
                updatedVideo.Session = await _databaseService.GetSessionByIdAsync(updatedVideo.SessionId);
            }

            await _databaseService.HydrateTagsForClips(new List<VideoClip> { updatedVideo });

            updatedVideo.IsSelected = existingVideo.IsSelected;

            if (_allVideosCache != null)
            {
                var cacheIndex = _allVideosCache.FindIndex(v => v.Id == videoId);
                if (cacheIndex >= 0)
                    _allVideosCache[cacheIndex] = updatedVideo;
            }
            if (_filteredVideosCache != null)
            {
                var filteredIndex = _filteredVideosCache.FindIndex(v => v.Id == videoId);
                if (filteredIndex >= 0)
                    _filteredVideosCache[filteredIndex] = updatedVideo;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SelectedSessionVideos[index] = updatedVideo;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al actualizar video en galería: {ex.Message}");
        }
    }

    public async Task PlayAsPlaylistAsync()
    {
        var selectedVideos = _batchEdit.GetSelectedVideos().OrderBy(v => v.CreationDate).ToList();

        if (selectedVideos.Count == 0)
        {
            await Shell.Current.DisplayAlert("Playlist", "No hay vídeos seleccionados.", "OK");
            return;
        }

        var singlePage = App.Current?.Handler?.MauiContext?.Services.GetService<Views.SinglePlayerPage>();
        if (singlePage?.BindingContext is SinglePlayerViewModel singleVm)
        {
            await singleVm.InitializeWithPlaylistAsync(selectedVideos, 0);
            await Shell.Current.Navigation.PushAsync(singlePage);
        }
        else
        {
            var firstVideo = selectedVideos.First();
            await Shell.Current.GoToAsync($"{nameof(SinglePlayerPage)}?videoPath={Uri.EscapeDataString(firstVideo.ClipPath ?? "")}");
        }
    }

    public async Task DeleteSelectedVideosAsync()
    {
        var selectedVideos = _batchEdit.GetSelectedVideos();

        if (selectedVideos.Count == 0)
        {
            await Shell.Current.DisplayAlert("Eliminar", "No hay vídeos seleccionados.", "OK");
            return;
        }

        var confirm = await Shell.Current.DisplayAlert(
            "Eliminar vídeos",
            $"¿Mover {selectedVideos.Count} vídeo(s) a la papelera?\n\nPodrás restaurarlos desde la Papelera durante 30 días.",
            "Mover a papelera",
            "Cancelar");

        if (!confirm)
            return;

        try
        {
            foreach (var video in selectedVideos)
            {
                await _trashService.MoveVideoToTrashAsync(video.Id);
            }

            _batchEdit.ClearVideoSelection();

            if (_refreshTrashItemCountAsync != null)
                await _refreshTrashItemCountAsync();

            if (SelectedSession != null)
                await LoadSelectedSessionVideosAsync(SelectedSession);
            else if (IsAllGallerySelected)
                await LoadAllVideosAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudieron eliminar los vídeos: {ex.Message}", "OK");
        }
    }

    public async Task ShareSelectedVideosAsync()
    {
        var selectedVideos = _batchEdit.GetSelectedVideos();

        if (selectedVideos.Count == 0)
        {
            await Shell.Current.DisplayAlert("Compartir", "No hay vídeos seleccionados.", "OK");
            return;
        }

        var existingFiles = selectedVideos
            .Select(v => !string.IsNullOrWhiteSpace(v.LocalClipPath) && File.Exists(v.LocalClipPath)
                ? v.LocalClipPath
                : v.ClipPath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => new ShareFile(path!))
            .ToList();

        if (existingFiles.Count == 0)
        {
            await Shell.Current.DisplayAlert("Compartir", "No se encontraron los archivos de vídeo.", "OK");
            return;
        }

        await Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = $"Compartir {existingFiles.Count} vídeo(s)",
            Files = existingFiles
        });
    }

    public async Task PlayParallelAnalysisAsync()
    {
        if (!HasParallelVideo1 && !HasParallelVideo2 && !HasParallelVideo3 && !HasParallelVideo4)
        {
            await Shell.Current.DisplayAlert("Análisis",
                "Arrastra al menos un vídeo a las áreas de análisis.", "OK");
            return;
        }

        IsPreviewMode = false;

        var singlePage = App.Current?.Handler?.MauiContext?.Services.GetService<Views.SinglePlayerPage>();
        if (singlePage?.BindingContext is SinglePlayerViewModel singleVm)
        {
            var slotVideos = new List<(int slot, VideoClip clip)>();
            if (ParallelVideo1 != null) slotVideos.Add((1, ParallelVideo1));
            if (ParallelVideo2 != null) slotVideos.Add((2, ParallelVideo2));
            if (ParallelVideo3 != null) slotVideos.Add((3, ParallelVideo3));
            if (ParallelVideo4 != null) slotVideos.Add((4, ParallelVideo4));

            if (slotVideos.Count == 0) return;

            var mainSlot = slotVideos[0];
            VideoClip mainVideo = mainSlot.clip;

            var occupiedSlots = slotVideos.Select(v => v.slot).ToHashSet();
            var layout = DetermineLayoutFromOccupiedSlots(occupiedSlots);

            var comparisonVideos = slotVideos
                .Where(v => v.slot != mainSlot.slot)
                .OrderBy(v => v.slot)
                .Select(v => (VideoClip?)v.clip)
                .ToList();

            await singleVm.InitializeWithComparisonAsync(
                mainVideo,
                comparisonVideos.ElementAtOrDefault(0),
                comparisonVideos.ElementAtOrDefault(1),
                comparisonVideos.ElementAtOrDefault(2),
                layout);

            await Shell.Current.Navigation.PushAsync(singlePage);
        }
    }

    private static ComparisonLayout DetermineLayoutFromOccupiedSlots(IReadOnlyCollection<int> occupiedSlots)
    {
        if (occupiedSlots == null || occupiedSlots.Count <= 1)
            return ComparisonLayout.Single;

        if (occupiedSlots.Count >= 3)
            return ComparisonLayout.Quad2x2;

        var has1 = occupiedSlots.Contains(1);
        var has2 = occupiedSlots.Contains(2);
        var has3 = occupiedSlots.Contains(3);
        var has4 = occupiedSlots.Contains(4);

        if ((has1 && has2) || (has3 && has4))
            return ComparisonLayout.Horizontal2x1;

        return ComparisonLayout.Vertical1x2;
    }

    public async Task PreviewParallelAnalysisAsync()
    {
        IsPreviewMode = !IsPreviewMode;
        await Task.CompletedTask;
    }

    public void ClearParallelAnalysis()
    {
        IsPreviewMode = false;
        ParallelVideo1 = null;
        ParallelVideo2 = null;
        ParallelVideo3 = null;
        ParallelVideo4 = null;
    }

    public async Task OnVideoTappedAsync(VideoClip? video)
    {
        if (video == null) return;

        if (_batchEdit.IsMultiSelectMode)
        {
            _batchEdit.ToggleVideoSelection(video);
        }
        else
        {
            await PlaySelectedVideoAsync(video);
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

    public async Task PlaySelectedVideoAsync(VideoClip? video)
    {
        if (video == null) return;

        var videoPath = video.LocalClipPath;

        if (!IsStreamingUrl(videoPath))
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                try
                {
                    var session = SelectedSession ?? await _databaseService.GetSessionByIdAsync(video.SessionId);
                    if (!string.IsNullOrWhiteSpace(session?.PathSesion))
                    {
                        var normalized = (video.ClipPath ?? "").Replace('\\', '/');
                        var fileName = Path.GetFileName(normalized);
                        if (string.IsNullOrWhiteSpace(fileName))
                            fileName = $"CROWN{video.Id}.mp4";

                        var candidate = Path.Combine(session.PathSesion, "videos", fileName);
                        if (File.Exists(candidate))
                            videoPath = candidate;
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(videoPath))
                videoPath = video.ClipPath;
        }

        if (!IsStreamingUrl(videoPath) && (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath)))
        {
            if (video.IsRemoteAvailable && _cloudBackendService.IsAuthenticated)
            {
                var remotePath = NormalizeRemotePath(video.ClipPath);
                var streamingUrl = await GetStreamingUrlAsync(remotePath);
                if (!string.IsNullOrWhiteSpace(streamingUrl))
                {
                    videoPath = streamingUrl;
                }
            }
        }

        if (string.IsNullOrEmpty(videoPath) || (!IsStreamingUrl(videoPath) && !File.Exists(videoPath)))
        {
            await Shell.Current.DisplayAlert("Error", "El archivo de video no está disponible", "OK");
            return;
        }

        var playerPage = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<SinglePlayerPage>();
        if (playerPage?.BindingContext is SinglePlayerViewModel vm)
        {
            if (video.Session == null && video.SessionId > 0)
            {
                video.Session = SelectedSession ?? await _databaseService.GetSessionByIdAsync(video.SessionId);
            }
            if (video.Atleta == null && video.AtletaId > 0)
            {
                video.Atleta = await _databaseService.GetAthleteByIdAsync(video.AtletaId);
            }
            if (video.Tags == null && video.Id > 0)
            {
                video.Tags = await _databaseService.GetTagsForVideoAsync(video.Id);
            }

            video.LocalClipPath = videoPath;

            var navStack = Shell.Current.Navigation.NavigationStack;
            var isAlreadyInStack = navStack.Contains(playerPage);

            if (!isAlreadyInStack)
            {
                await Shell.Current.Navigation.PushAsync(playerPage);
            }

            await vm.InitializeWithVideoAsync(video);
        }
        else
        {
            await Shell.Current.GoToAsync($"{nameof(SinglePlayerPage)}?videoPath={Uri.EscapeDataString(videoPath)}");
        }
    }

    public async Task SelectAllGalleryAsync()
    {
        _setSelectedSession?.Invoke(null);

        _smartFolders.ClearActiveSmartFolder();

        _remote.ClearRemoteSections();

        _setIsAllGallerySelected?.Invoke(true);

        await LoadAllVideosAsync();
    }

    public async Task SelectFavoriteVideosAsync()
    {
        _setSelectedSession?.Invoke(null);
        _smartFolders.ClearActiveSmartFolder();

        _setIsFavoriteVideosSelected?.Invoke(true);

        await EnsureAllVideosCacheAsync();
        await RefreshFavoriteVideosViewAsync();
        await RefreshFavoritesCountsAsync();
    }

    private async Task EnsureAllVideosCacheAsync()
    {
        if (_allVideosCache != null)
            return;

        _allVideosCache = await _databaseService.GetAllVideoClipsAsync();

        var sessionIds = _allVideosCache?.Select(c => c.SessionId).Distinct().ToList() ?? new List<int>();
        var sessionsDict = new Dictionary<int, Session>();
        foreach (var sessionId in sessionIds)
        {
            var session = await _databaseService.GetSessionByIdAsync(sessionId);
            if (session != null)
                sessionsDict[sessionId] = session;
        }

        if (_allVideosCache != null)
        {
            foreach (var clip in _allVideosCache)
            {
                if (sessionsDict.TryGetValue(clip.SessionId, out var session))
                    clip.Session = session;
            }
        }
    }

    private async Task RefreshFavoriteVideosViewAsync()
    {
        _selectedSessionVideosCts?.Cancel();
        _selectedSessionVideosCts?.Dispose();
        _selectedSessionVideosCts = new CancellationTokenSource();
        var ct = _selectedSessionVideosCts.Token;

        SelectedSessionVideos.Clear();
        SelectedSessionSectionStats.Clear();
        SelectedSessionAthleteTimes.Clear();
        SelectedSessionSectionDurationMinutes.Clear();
        SelectedSessionSectionLabels.Clear();
        SelectedSessionTagVideoCounts.Clear();
        SelectedSessionTagLabels.Clear();
        SelectedSessionPenaltyCounts.Clear();
        SelectedSessionPenaltyLabels.Clear();
        SelectedSessionInputs.Clear();
        SelectedSessionValoraciones.Clear();

        _currentPage = 0;
        _favoriteVideosCache = _allVideosCache?.Where(v => v.IsFavorite == 1).ToList() ?? new List<VideoClip>();
        _filteredVideosCache = _favoriteVideosCache;

        var firstBatch = _favoriteVideosCache.Take(PageSize).ToList();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await ReplaceCollectionInBatchesAsync(SelectedSessionVideos, firstBatch, ct);
        });
        _currentPage = 1;
        HasMoreVideos = _favoriteVideosCache.Count > PageSize;

        var snapshot = await Task.Run(() => BuildGalleryStatsSnapshot(_favoriteVideosCache), ct);
        if (!ct.IsCancellationRequested)
            await ApplyGalleryStatsSnapshotAsync(snapshot, ct);

        OnPropertyChanged(nameof(TotalFilteredVideoCount));
        OnPropertyChanged(nameof(TotalAvailableVideoCount));
        OnPropertyChanged(nameof(VideoCountDisplayText));
        OnPropertyChanged(nameof(TotalFilteredDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationSeconds));
        OnPropertyChanged(nameof(SelectedSessionTotalDurationFormatted));
    }

    public async Task RefreshFavoritesCountsAsync()
    {
        try
        {
            var db = await _databaseService.GetConnectionAsync();
            var favoriteSessionsCount = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sesion WHERE is_deleted = 0 AND is_favorite = 1;");
            FavoriteVideosCount = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM videoClip v INNER JOIN sesion s ON s.id = v.SessionID " +
                "WHERE v.is_deleted = 0 AND s.is_deleted = 0 AND v.is_favorite = 1;");

            _setFavoriteSessionsCount?.Invoke(favoriteSessionsCount);
        }
        catch
        {
        }
    }

    public async Task ViewVideoLessonsAsync()
    {
        try
        {
            IsLoadingSelectedSessionVideos = true;

            _remote.ClearRemoteSections();

            _setSelectedSession?.Invoke(null);
            _setIsAllGallerySelected?.Invoke(false);
            _setIsVideoLessonsSelected?.Invoke(true);

            _selectedSessionVideosCts?.Cancel();
            _selectedSessionVideosCts?.Dispose();
            _selectedSessionVideosCts = new CancellationTokenSource();
            var ct = _selectedSessionVideosCts.Token;

            _batchEdit.IsMultiSelectMode = false;
            _batchEdit.IsSelectAllActive = false;

            _currentPage = 0;
            _allVideosCache = null;
            _filteredVideosCache = null;
            HasMoreVideos = false;

            await LoadVideoLessonsAsync(ct);
        }
        catch (Exception ex)
        {
            if (_selectedSessionVideosCts?.Token.IsCancellationRequested != true)
                await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar las videolecciones: {ex.Message}", "OK");
        }
        finally
        {
            if (_selectedSessionVideosCts?.Token.IsCancellationRequested != true)
                IsLoadingSelectedSessionVideos = false;
        }
    }

    public async Task ToggleVideoFavoriteAsync(VideoClip? video)
    {
        if (video == null)
            return;

        try
        {
            video.IsFavorite = video.IsFavorite == 1 ? 0 : 1;
            await _databaseService.SaveVideoClipAsync(video);

            if (_allVideosCache != null)
            {
                var cached = _allVideosCache.FirstOrDefault(v => v.Id == video.Id);
                if (cached != null)
                    cached.IsFavorite = video.IsFavorite;
            }

            if (IsFavoriteVideosSelected)
            {
                await RefreshFavoriteVideosViewAsync();
            }

            await RefreshFavoritesCountsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("VideosViewModel", "ToggleVideoFavoriteAsync error", ex);
        }
    }

    public void ClearLocalSelectionForRemote()
    {
        _setIsAllGallerySelected?.Invoke(false);
        _setIsVideoLessonsSelected?.Invoke(false);
        _setIsDiaryViewSelected?.Invoke(false);
        _setSelectedSession?.Invoke(null);
    }

    public async Task RefreshPendingStatsAsync()
    {
        if (!_hasStatsUpdatePending)
            return;

        _hasStatsUpdatePending = false;
        _modifiedVideoIds.Clear();

        try
        {
            _smartFolders.UpdateSmartFolderVideoCounts();

            if (SelectedSession != null)
            {
                await LoadAbsoluteTagStatsAsync(SelectedSession.Id);
                await LoadAthleteSectionTimesAsync(SelectedSession.Id);
            }
            else if (IsAllGallerySelected)
            {
                await LoadAbsoluteTagStatsAsync(null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refrescando estadísticas pendientes: {ex.Message}");
        }
    }

    private async Task LoadAbsoluteTagStatsAsync(int? sessionId)
    {
        try
        {
            var stats = await _statisticsService.GetAbsoluteTagStatsAsync(sessionId);

            TotalEventTagsCount = stats.TotalEventTags;
            TotalUniqueEventTagsCount = stats.UniqueEventTags;
            TotalLabelTagsCount = stats.TotalLabelTags;
            TotalUniqueLabelTagsCount = stats.UniqueLabelTags;
            LabeledVideosCount = stats.LabeledVideos;
            TotalVideosForLabeling = stats.TotalVideos;
            AvgTagsPerSession = stats.AvgTagsPerSession;

            TopEventTags.Clear();
            TopEventTagValues.Clear();
            TopEventTagNames.Clear();
            foreach (var tag in stats.TopEventTags)
            {
                TopEventTags.Add(tag);
                TopEventTagValues.Add(tag.UsageCount);
                TopEventTagNames.Add(tag.TagName);
            }

            TopLabelTags.Clear();
            TopLabelTagValues.Clear();
            TopLabelTagNames.Clear();
            foreach (var tag in stats.TopLabelTags)
            {
                TopLabelTags.Add(tag);
                TopLabelTagValues.Add(tag.UsageCount);
                TopLabelTagNames.Add(tag.TagName);
            }

            OnPropertyChanged(nameof(LabeledVideosText));
            OnPropertyChanged(nameof(LabeledVideosPercentage));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando estadísticas absolutas: {ex.Message}");
        }
    }

    private async Task LoadAthleteSectionTimesAsync(int sessionId)
    {
        try
        {
            var sections = await _statisticsService.GetAthleteSectionTimesAsync(sessionId);

            SectionTimes.Clear();
            foreach (var section in sections)
            {
                SectionTimes.Add(section);
            }

            HasSectionTimes = SectionTimes.Count > 0;
            OnPropertyChanged(nameof(ShowSectionTimesTable));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando tiempos por sección: {ex.Message}");
            HasSectionTimes = false;
            OnPropertyChanged(nameof(ShowSectionTimesTable));
        }
    }

    public void ClearSectionTimes()
    {
        SectionTimes.Clear();
        HasSectionTimes = false;
        OnPropertyChanged(nameof(ShowSectionTimesTable));
    }

    public async Task OpenDetailedTimesPopupAsync()
    {
        var session = SelectedSession;
        if (session == null) return;

        IsLoadingDetailedTimes = true;
        ShowDetailedTimesPopup = true;
        IsAthleteDropdownExpanded = false;

        try
        {
            var detailedTimes = await _statisticsService.GetDetailedAthleteSectionTimesAsync(session.Id);

            DetailedSectionTimes.Clear();
            foreach (var section in detailedTimes)
                DetailedSectionTimes.Add(section);

            OnPropertyChanged(nameof(HasDetailedTimesWithLaps));

            DetailedTimesAthletes.Clear();

            var uniqueAthletes = detailedTimes
                .SelectMany(s => s.Athletes)
                .GroupBy(a => a.AthleteId)
                .Select(g => new {
                    AthleteId = g.Key,
                    AthleteName = g.First().AthleteName,
                    TotalRuns = g.Count()
                })
                .OrderBy(a => a.AthleteName)
                .ToList();

            foreach (var athlete in uniqueAthletes)
            {
                var suffix = athlete.TotalRuns > 1 ? $" ({athlete.TotalRuns} mangas)" : "";
                DetailedTimesAthletes.Add(new AthletePickerItem(
                    athlete.AthleteId,
                    athlete.AthleteName + suffix,
                    videoId: null,
                    attemptNumber: 0,
                    hasMultipleAttempts: athlete.TotalRuns > 1));
            }

            if (ReferenceAthleteId.HasValue)
            {
                SelectedDetailedTimesAthlete = DetailedTimesAthletes
                    .FirstOrDefault(a => a.Id == ReferenceAthleteId.Value && a.AttemptNumber == 0)
                    ?? DetailedTimesAthletes.FirstOrDefault(a => a.Id == ReferenceAthleteId.Value);
            }
            else
            {
                SelectedDetailedTimesAthlete = null;
            }

            RegenerateDetailedTimesHtml();
        }
        catch (Exception ex)
        {
            AppLog.Error("VideosViewModel", $"Error cargando tiempos detallados: {ex.Message}", ex);
        }
        finally
        {
            IsLoadingDetailedTimes = false;
        }
    }

    private void RegenerateDetailedTimesHtml()
    {
        var session = SelectedSession;
        if (session == null || DetailedSectionTimes.Count == 0)
            return;

        var refId = SelectedDetailedTimesAthlete?.Id;
        var refVideoId = SelectedDetailedTimesAthlete?.VideoId;
        var refName = SelectedDetailedTimesAthlete?.DisplayName;

        var reportData = SessionReportService.GenerateReportData(
            session,
            DetailedSectionTimes.ToList(),
            refId,
            refVideoId);

        DetailedTimesHtml = _tableExportService.BuildSessionReportHtml(
            reportData,
            ReportOptions,
            refId,
            refVideoId,
            refName);
    }

    private void UpdateSectionTimeDifferences()
    {
        if (!ReferenceAthleteId.HasValue) return;

        var refAthleteId = ReferenceAthleteId.Value;

        foreach (var section in SectionTimes)
        {
            var refAthleteRow = section.Athletes.FirstOrDefault(a => a.AthleteId == refAthleteId);
            long refTotalMs = refAthleteRow?.TotalMs ?? 0;

            foreach (var athlete in section.Athletes)
            {
                athlete.SetReferenceDifference(refTotalMs, athlete.AthleteId == refAthleteId);
            }
        }

        OnPropertyChanged(nameof(SectionTimes));
    }

    public async Task ExportDetailedTimesHtmlAsync()
    {
        await ExportDetailedTimesAsync("html");
    }

    public async Task ExportDetailedTimesPdfAsync()
    {
        await ExportDetailedTimesAsync("pdf");
    }

    private async Task ExportDetailedTimesAsync(string format)
    {
        var session = SelectedSession;
        if (session == null)
        {
            await Shell.Current.DisplayAlert("Error", "No hay ninguna sesión seleccionada", "OK");
            return;
        }

        if (IsLoadingDetailedTimes || IsExportingDetailedTimes)
            return;

        try
        {
            IsExportingDetailedTimes = true;

            var sections = DetailedSectionTimes.ToList();
            if (sections.Count == 0)
            {
                var detailedTimes = await _statisticsService.GetDetailedAthleteSectionTimesAsync(session.Id);
                sections = detailedTimes;
            }

            if (sections.Count == 0)
            {
                await Shell.Current.DisplayAlert("Exportación", "No hay datos de tiempos para exportar", "OK");
                return;
            }

            string filePath;
            var refId = SelectedDetailedTimesAthlete?.Id;
            var refVideoId = SelectedDetailedTimesAthlete?.VideoId;
            var refName = SelectedDetailedTimesAthlete?.DisplayName;

            if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
                filePath = await _tableExportService.ExportDetailedSectionTimesToPdfAsync(session, sections, refId, refVideoId, refName);
            else
                filePath = await _tableExportService.ExportDetailedSectionTimesToHtmlAsync(session, sections, refId, refVideoId, refName);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Exportar {session.DisplayName}",
                File = new ShareFile(filePath)
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("VideosViewModel", $"Error exportando tabla ({format}): {ex.Message}", ex);
            await Shell.Current.DisplayAlert("Error", $"No se pudo exportar la tabla a {format.ToUpperInvariant()}: {ex.Message}", "OK");
        }
        finally
        {
            IsExportingDetailedTimes = false;
        }
    }
}
