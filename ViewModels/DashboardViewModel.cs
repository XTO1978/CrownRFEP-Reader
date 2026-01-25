using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace CrownRFEP_Reader.ViewModels;

/// <summary>
/// Modelo que combina una sesión con su diario asociado para la vista del calendario
/// </summary>
public class SessionTypeOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = "";

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class PlaceOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = "";

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class SessionWithDiary : INotifyPropertyChanged
{
    private SessionDiary? _diary;
    private int _editValoracionFisica = 3;
    private int _editValoracionMental = 3;
    private int _editValoracionTecnica = 3;
    private string _editNotas = "";
    private ObservableCollection<VideoClip> _videos = new();

    public Session Session { get; set; } = null!;

    /// <summary>Vídeos de la sesión (máximo 6 para la mini galería)</summary>
    public ObservableCollection<VideoClip> Videos
    {
        get => _videos;
        set
        {
            _videos = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVideos));
            OnPropertyChanged(nameof(VideoCount));
            OnPropertyChanged(nameof(ExtraVideosCount));
            OnPropertyChanged(nameof(HasExtraVideos));
        }
    }

    public bool HasVideos => Videos.Count > 0;
    public int VideoCount { get; set; }
    public int ExtraVideosCount => Math.Max(0, VideoCount - 6);
    public bool HasExtraVideos => ExtraVideosCount > 0;

    public SessionDiary? Diary
    {
        get => _diary;
        set
        {
            _diary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiary));
            OnPropertyChanged(nameof(HasValoraciones));
            // Inicializar valores de edición desde el diario existente
            if (_diary != null)
            {
                EditValoracionFisica = _diary.ValoracionFisica > 0 ? _diary.ValoracionFisica : 3;
                EditValoracionMental = _diary.ValoracionMental > 0 ? _diary.ValoracionMental : 3;
                EditValoracionTecnica = _diary.ValoracionTecnica > 0 ? _diary.ValoracionTecnica : 3;
                EditNotas = _diary.Notas ?? "";
            }
        }
    }

    public bool HasDiary => Diary != null;
    public bool HasValoraciones => Diary != null &&
        (Diary.ValoracionFisica > 0 || Diary.ValoracionMental > 0 || Diary.ValoracionTecnica > 0);
    public bool HasNotas => Diary != null && !string.IsNullOrWhiteSpace(Diary.Notas);

    // Propiedades para el formulario de edición
    public int EditValoracionFisica
    {
        get => _editValoracionFisica;
        set { _editValoracionFisica = value; OnPropertyChanged(); }
    }

    public int EditValoracionMental
    {
        get => _editValoracionMental;
        set { _editValoracionMental = value; OnPropertyChanged(); }
    }

    public int EditValoracionTecnica
    {
        get => _editValoracionTecnica;
        set { _editValoracionTecnica = value; OnPropertyChanged(); }
    }

    public string EditNotas
    {
        get => _editNotas;
        set { _editNotas = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Modelo para los iconos del picker con estado de selección
/// </summary>
public class IconPickerItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = "";

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// ViewModel para la página principal / Dashboard
/// </summary>
public class DashboardViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private readonly ITrashService _trashService;
    private int _trashItemCount;
    private readonly CrownFileService _crownFileService;
    private readonly StatisticsService _statisticsService;
    private readonly ThumbnailService _thumbnailService;
    private readonly ITableExportService _tableExportService;
    private readonly ImportProgressService? _importProgressService;

    public LayoutStateViewModel Layout { get; }
    public BatchEditViewModel BatchEdit { get; }
    public SmartFoldersViewModel SmartFolders { get; }
    public RemoteLibraryViewModel Remote { get; }
    public DiaryWellnessViewModel Diary { get; }
    public VideosViewModel Videos { get; }

    private DashboardStats? _stats;
    private Session? _selectedSession;
    private int _favoriteSessionsCount;

    private GridLength _mainPanelWidth = new(2.5, GridUnitType.Star);
    private GridLength _rightPanelWidth = new(1.2, GridUnitType.Star);
    private GridLength _rightSplitterWidth = new(8);
    private string _importProgressText = "";

    // Importación en segundo plano
    private bool _isBackgroundImporting;
    private int _backgroundImportProgress;
    private string _backgroundImportText = "";
    private string _importingSessionName = "";
    private string _importingCurrentFile = "";

    private readonly Dictionary<string, bool> _sessionGroupExpandedState = new();

    private int _importProgressValue;
    private bool _isImporting;

    // Nueva sesión manual
    private bool _isAddingNewSession;
    private string _newSessionName = "";
    private string _newSessionType = "Gimnasio";
    private string _newSessionLugar = "";
    private readonly ICloudBackendService _cloudBackendService;
    private readonly StorageMigrationService? _migrationService;

    // Popup de personalización de icono/color
    private SmartFolderDefinition? _iconColorPickerTargetSmartFolder;
    private SessionRow? _iconColorPickerTargetSession;
    private string _selectedPickerIcon = "oar.2.crossed";
    private string _selectedPickerColor = "#FF6DDDFF";

    // Edición de sesión individual
    private bool _showSessionEditPopup;
    private SessionRow? _editingSessionRow;
    private string? _sessionEditName;
    private string? _sessionEditLugar;
    private string? _sessionEditTipoSesion;

    // Sincronización cloud
    private bool _isSyncing;
    private int _syncProgress;
    private string _syncStatusText = "Sincronización cloud";
    private int _pendingSyncCount;
    private bool _isSessionsExpanded = true;
    private bool _showNewSessionSidebarPopup;

    public DashboardStats? Stats
    {
        get => _stats;
        set => SetProperty(ref _stats, value);
    }

    public Session? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                if (value != null)
                {
                    IsAllGallerySelected = false;
                    IsVideoLessonsSelected = false;
                    IsDiaryViewSelected = false;
                    IsFavoriteVideosSelected = false;
                    // Desactivar secciones remotas
                    Remote.ClearRemoteSections();
                }
                else
                {
                    // Deseleccionar el SessionRow actual cuando SelectedSession = null
                    if (_selectedSessionListItem is SessionRow oldRow)
                    {
                        oldRow.IsSelected = false;
                    }
                    _selectedSessionListItem = null;
                    OnPropertyChanged(nameof(SelectedSessionListItem));
                }
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(HasSpecificSessionSelected));
                OnPropertyChanged(nameof(CanShowRecordButton));
                _ = Videos.LoadSelectedSessionVideosAsync(value);
                Videos.NotifySelectionChanged();

                // Recargar datos de la pestaña activa
                if (Layout.IsDiaryTabSelected)
                {
                    _ = Diary.LoadSessionDiaryAsync(value);
                }
            }
        }
    }

    public bool IsAllGallerySelected
    {
        get => Layout.IsAllGallerySelected;
        set
        {
            if (Layout.IsAllGallerySelected == value)
                return;

            Layout.IsAllGallerySelected = value;

            if (value)
            {
                IsVideoLessonsSelected = false;
                IsDiaryViewSelected = false;
                IsFavoriteSessionsSelected = false;
                IsFavoriteVideosSelected = false;
                Videos.ClearSectionTimes();
                // Desactivar secciones remotas
                Remote.ClearRemoteSections();
            }

            OnPropertyChanged(nameof(SelectedSessionTitle));
            OnPropertyChanged(nameof(ShowSectionTimesTable));
            OnPropertyChanged(nameof(HasSpecificSessionSelected));
        }
    }

    public bool IsVideoLessonsSelected
    {
        get => Layout.IsVideoLessonsSelected;
        set
        {
            if (Layout.IsVideoLessonsSelected == value)
                return;

            Layout.IsVideoLessonsSelected = value;

            if (value)
            {
                IsAllGallerySelected = false;
                IsDiaryViewSelected = false;
                IsFavoriteSessionsSelected = false;
                IsFavoriteVideosSelected = false;
                Videos.ClearSectionTimes();
                // Desactivar secciones remotas
                Remote.ClearRemoteSections();
            }

            UpdateRightPanelLayout();
            OnPropertyChanged(nameof(SelectedSessionTitle));
            OnPropertyChanged(nameof(ShowSectionTimesTable));
            OnPropertyChanged(nameof(HasSpecificSessionSelected));
            OnPropertyChanged(nameof(ShowVideoGallery));
        }
    }

    public bool IsDiaryViewSelected
    {
        get => Layout.IsDiaryViewSelected;
        set
        {
            if (Layout.IsDiaryViewSelected == value)
                return;

            Layout.IsDiaryViewSelected = value;

            if (value)
            {
                IsAllGallerySelected = false;
                IsVideoLessonsSelected = false;
                IsFavoriteSessionsSelected = false;
                IsFavoriteVideosSelected = false;
                SelectedSession = null;
                Videos.ClearSectionTimes();
                // Desactivar secciones remotas
                Remote.ClearRemoteSections();
                _ = Diary.LoadDiaryViewDataAsync();
            }

            UpdateRightPanelLayout();
            OnPropertyChanged(nameof(SelectedSessionTitle));
            OnPropertyChanged(nameof(ShowSectionTimesTable));
            OnPropertyChanged(nameof(HasSpecificSessionSelected));
            OnPropertyChanged(nameof(ShowVideoGallery));
        }
    }

    public bool ShowVideoGallery => Videos.ShowVideoGallery;

    public bool ShowSectionTimesTable => Videos.ShowSectionTimesTable;

    public bool HasSpecificSessionSelected => SelectedSession != null;

    public bool CanShowRecordButton => SelectedSession != null;

    public bool IsAddingNewSession
    {
        get => _isAddingNewSession;
        set => SetProperty(ref _isAddingNewSession, value);
    }

    public string NewSessionName
    {
        get => _newSessionName;
        set => SetProperty(ref _newSessionName, value);
    }

    public string NewSessionType
    {
        get => _newSessionType;
        set => SetProperty(ref _newSessionType, value);
    }

    public string NewSessionLugar
    {
        get => _newSessionLugar;
        set => SetProperty(ref _newSessionLugar, value);
    }

    public ObservableCollection<SessionTypeOption> SessionTypeOptions { get; } = new();
    public ObservableCollection<PlaceOption> SessionPlaceOptions { get; } = new();

    public bool ShowNewSessionSidebarPopup
    {
        get => _showNewSessionSidebarPopup;
        set => SetProperty(ref _showNewSessionSidebarPopup, value);
    }

    public bool IsSessionsExpanded
    {
        get => _isSessionsExpanded;
        set => SetProperty(ref _isSessionsExpanded, value);
    }

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

    public SmartFolderDefinition? IconColorPickerTargetSmartFolder
    {
        get => _iconColorPickerTargetSmartFolder;
        private set => SetProperty(ref _iconColorPickerTargetSmartFolder, value);
    }

    public SessionRow? IconColorPickerTargetSession
    {
        get => _iconColorPickerTargetSession;
        private set => SetProperty(ref _iconColorPickerTargetSession, value);
    }

    public bool IsPickerForSmartFolder => IconColorPickerTargetSmartFolder != null;
    public bool IsPickerForSession => IconColorPickerTargetSession != null;

    public string IconColorPickerTitle
        => IsPickerForSmartFolder
            ? $"Carpeta: {IconColorPickerTargetSmartFolder?.Name ?? "Carpeta"}"
            : $"Sesión: {IconColorPickerTargetSession?.Session?.DisplayName ?? "Sesión"}";

    public ObservableCollection<IconPickerItem> AvailableIcons { get; } = new()
    {
        new IconPickerItem { Name = "oar.2.crossed" },
        new IconPickerItem { Name = "star" },
        new IconPickerItem { Name = "star.fill" },
        new IconPickerItem { Name = "heart" },
        new IconPickerItem { Name = "heart.fill" },
        new IconPickerItem { Name = "bolt.fill" },
        new IconPickerItem { Name = "waveform.path.ecg" },
        new IconPickerItem { Name = "sun.max.fill" },
        new IconPickerItem { Name = "cloud.sun.fill" },
        new IconPickerItem { Name = "cloud.fill" },
        new IconPickerItem { Name = "cloud.heavyrain.fill" },
        new IconPickerItem { Name = "moon.fill" },
        new IconPickerItem { Name = "sparkles" },
        new IconPickerItem { Name = "eye" },
        new IconPickerItem { Name = "eye.fill" },
        new IconPickerItem { Name = "eye.slash" },
        new IconPickerItem { Name = "plus" },
        new IconPickerItem { Name = "plus.circle" },
        new IconPickerItem { Name = "minus" },
        new IconPickerItem { Name = "line.3.horizontal" },
        new IconPickerItem { Name = "line.3.horizontal.circle" },
        new IconPickerItem { Name = "line.3.horizontal.decrease" },
        new IconPickerItem { Name = "chart.pie" },
        new IconPickerItem { Name = "face.smiling" },
        new IconPickerItem { Name = "face.smiling.fill" }
    };

    public string SelectedPickerIcon
    {
        get => _selectedPickerIcon;
        private set => SetProperty(ref _selectedPickerIcon, value);
    }

    public string SelectedPickerColor
    {
        get => _selectedPickerColor;
        private set => SetProperty(ref _selectedPickerColor, value);
    }

    public GridLength RightPanelWidth
    {
        get => _rightPanelWidth;
        set => SetProperty(ref _rightPanelWidth, value);
    }

    public GridLength MainPanelWidth
    {
        get => _mainPanelWidth;
        set => SetProperty(ref _mainPanelWidth, value);
    }

    public GridLength RightSplitterWidth
    {
        get => _rightSplitterWidth;
        set => SetProperty(ref _rightSplitterWidth, value);
    }

    private void UpdateRightPanelLayout()
    {
        if (IsVideoLessonsSelected)
        {
            MainPanelWidth = new GridLength(2.5, GridUnitType.Star);
            RightSplitterWidth = new GridLength(0);
            RightPanelWidth = new GridLength(0);
        }
        else if (IsDiaryViewSelected)
        {
            // En Diario, el modo aislado no aplica.
            MainPanelWidth = new GridLength(2.5, GridUnitType.Star);
            RightSplitterWidth = new GridLength(8);
            RightPanelWidth = new GridLength(1.2, GridUnitType.Star);
        }
        else if (Layout.IsQuickAnalysisIsolatedMode)
        {
            // Modo aislado:
            // - El sidebar izquierdo se mantiene fijo.
            // - Del espacio restante, la columna derecha (reproductores) ocupa 1/2 (1*)
            //   y el contenido principal el otro 1/2 (1*).
            // Nota: los anchos pueden ser ajustados por el usuario mediante el splitter;
            // aquí solo nos aseguramos de que el splitter esté visible.
            RightSplitterWidth = new GridLength(8);
        }
        else
        {
            MainPanelWidth = new GridLength(2.5, GridUnitType.Star);
            RightSplitterWidth = new GridLength(8);
            RightPanelWidth = new GridLength(1.2, GridUnitType.Star);
        }
    }

    public bool IsSessionsListExpanded
    {
        get => Layout.IsSessionsListExpanded;
        set
        {
            if (Layout.IsSessionsListExpanded == value)
                return;

            Layout.IsSessionsListExpanded = value;
            // La visibilidad se controla llenando/vaciando la colección
            SyncVisibleSessionRows();
        }
    }

    public bool IsFavoritesExpanded
    {
        get => Layout.IsFavoritesExpanded;
        set => Layout.IsFavoritesExpanded = value;
    }

    public bool IsFavoriteSessionsSelected
    {
        get => Layout.IsFavoriteSessionsSelected;
        set
        {
            if (Layout.IsFavoriteSessionsSelected == value)
                return;

            Layout.IsFavoriteSessionsSelected = value;
            if (value)
            {
                IsFavoriteVideosSelected = false;
                IsAllGallerySelected = false;
                IsVideoLessonsSelected = false;
                IsDiaryViewSelected = false;
                Remote.ClearRemoteSections();
                SyncVisibleSessionRows();
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(ShowVideoGallery));
            }
        }
    }

    public bool IsFavoriteVideosSelected
    {
        get => Layout.IsFavoriteVideosSelected;
        set
        {
            if (Layout.IsFavoriteVideosSelected == value)
                return;

            Layout.IsFavoriteVideosSelected = value;
            if (value)
            {
                IsFavoriteSessionsSelected = false;
                IsAllGallerySelected = false;
                IsVideoLessonsSelected = false;
                IsDiaryViewSelected = false;
                SelectedSession = null;
                Remote.ClearRemoteSections();
                OnPropertyChanged(nameof(SelectedSessionTitle));
                OnPropertyChanged(nameof(ShowVideoGallery));
            }
        }
    }

    public int FavoriteSessionsCount
    {
        get => _favoriteSessionsCount;
        private set => SetProperty(ref _favoriteSessionsCount, value);
    }

    public bool IsUserLibraryExpanded
    {
        get => Layout.IsUserLibraryExpanded;
        set
        {
            if (Layout.IsUserLibraryExpanded == value)
                return;

            Layout.IsUserLibraryExpanded = value;
            // La visibilidad se controla llenando/vaciando la colección
            SyncVisibleSessionRows();
            SyncVisibleRecentSessions();
        }
    }

    public string SelectedSessionTitle => Layout.IsRemoteAllGallerySelected
        ? $"Galería {Remote.RemoteLibraryDisplayName}"
        : (Remote.IsRemoteSessionSelected
            ? (Remote.RemoteSessions.FirstOrDefault(s => s.SessionId == Remote.SelectedRemoteSessionId)?.Title ?? $"Sesión {Remote.SelectedRemoteSessionId}")
            : (IsDiaryViewSelected
                ? "Diario Personal"
                : (IsFavoriteVideosSelected
                    ? "Favoritos (vídeos)"
                    : (IsFavoriteSessionsSelected && SelectedSession == null
                        ? "Favoritos (sesiones)"
                        : (IsVideoLessonsSelected
                            ? "Videolecciones"
                            : (IsAllGallerySelected
                                ? "Galería General"
                                : (SelectedSession?.DisplayName ?? "Selecciona una sesión")))))));

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
        set => SetProperty(ref _isImporting, value);
    }

    /// <summary>
    /// Indica si hay una importación ejecutándose en segundo plano
    /// </summary>
    public bool IsBackgroundImporting
    {
        get => _isBackgroundImporting;
        set => SetProperty(ref _isBackgroundImporting, value);
    }

    /// <summary>
    /// Porcentaje de progreso de la importación en segundo plano (0-100)
    /// </summary>
    public int BackgroundImportProgress
    {
        get => _backgroundImportProgress;
        set => SetProperty(ref _backgroundImportProgress, value);
    }

    /// <summary>
    /// Texto descriptivo de la importación en segundo plano
    /// </summary>
    public string BackgroundImportText
    {
        get => _backgroundImportText;
        set => SetProperty(ref _backgroundImportText, value);
    }

    /// <summary>
    /// Nombre de la sesión que se está importando
    /// </summary>
    public string ImportingSessionName
    {
        get => _importingSessionName;
        set => SetProperty(ref _importingSessionName, value);
    }

    /// <summary>
    /// Archivo actual que se está procesando
    /// </summary>
    public string ImportingCurrentFile
    {
        get => _importingCurrentFile;
        set => SetProperty(ref _importingCurrentFile, value);
    }

    /// <summary>
    /// Progreso de importación como valor entre 0 y 1 para ProgressBar
    /// </summary>
    public double BackgroundImportProgressNormalized => BackgroundImportProgress / 100.0;

    public bool IsStatsTabSelected
    {
        get => Layout.IsStatsTabSelected;
        set
        {
            if (Layout.IsStatsTabSelected == value)
                return;

            Layout.IsStatsTabSelected = value;
            if (value)
            {
                IsCrudTechTabSelected = false;
                Layout.IsDiaryTabSelected = false;
            }
        }
    }

    public bool IsCrudTechTabSelected
    {
        get => Layout.IsCrudTechTabSelected;
        set
        {
            if (Layout.IsCrudTechTabSelected == value)
                return;

            Layout.IsCrudTechTabSelected = value;
            if (value)
            {
                IsStatsTabSelected = false;
                Layout.IsDiaryTabSelected = false;
            }
        }
    }


    public bool IsVideoSelected(int videoId) => BatchEdit.IsVideoSelected(videoId);

    public ObservableCollection<Session> RecentSessions { get; } = new();
    public ObservableCollection<Session> VisibleRecentSessions { get; } = new();
    public ObservableCollection<Session> FavoriteSessions { get; } = new();
    public ObservableCollection<SessionsListRow> SessionRows { get; } = new();

    private SessionsListRow? _selectedSessionListItem;
    public SessionsListRow? SelectedSessionListItem
    {
        get => _selectedSessionListItem;
        set
        {
            // Si seleccionan una cabecera, anulamos la selección para evitar estados raros.
            if (value is SessionGroupHeaderRow)
            {
                if (_selectedSessionListItem != null)
                {
                    _selectedSessionListItem = null;
                    OnPropertyChanged();
                }
                return;
            }

            var oldRow = _selectedSessionListItem as SessionRow;
            if (SetProperty(ref _selectedSessionListItem, value))
            {
                // Actualizar IsSelected en los SessionRow
                if (oldRow != null)
                    oldRow.IsSelected = false;

                if (value is SessionRow newRow)
                {
                    newRow.IsSelected = true;
                    SelectedSession = newRow.Session;
                    
                    // Limpiar carpeta inteligente activa al seleccionar una sesión
                    SmartFolders.ClearActiveSmartFolder();
                }
            }
        }
    }

    private void SyncVisibleRecentSessions()
    {
        VisibleRecentSessions.Clear();

        if (!IsUserLibraryExpanded || !IsSessionsListExpanded)
            return;

        foreach (var session in RecentSessions)
            VisibleRecentSessions.Add(session);
    }

    private void SyncVisibleSessionGroups()
    {
        // Obsoleto: se usaba para CollectionView agrupado.
    }

    private void SyncVisibleSessionRows()
    {
        SessionRows.Clear();

        // La visibilidad se controla llenando/vaciando la colección
        if (!IsUserLibraryExpanded || !IsSessionsListExpanded)
            return;

        var sessionsSnapshot = IsFavoriteSessionsSelected
            ? RecentSessions.Where(s => s.IsFavorite == 1).ToList()
            : RecentSessions.ToList();
        var groups = BuildSessionGroups(sessionsSnapshot);

        foreach (var g in groups)
        {
            SessionRows.Add(new SessionGroupHeaderRow(g.Key, g.Title, g.IsExpanded, g.TotalCount));

            if (!g.IsExpanded)
                continue;

            foreach (var s in g)
            {
                // Aplicar personalizaciones de icono y color
                ApplySessionCustomization(s);
                
                var row = new SessionRow(s);
                // Marcar como seleccionado si corresponde a la sesión actual (comparar por Id)
                if (SelectedSession != null && s.Id == SelectedSession.Id)
                {
                    row.IsSelected = true;
                    _selectedSessionListItem = row; // Sincronizar referencia
                    SelectedSession = s; // Actualizar referencia a la nueva instancia
                }
                SessionRows.Add(row);
            }
        }
    }

    private void ReplaceRecentSession(Session session)
    {
        var existing = RecentSessions.FirstOrDefault(s => s.Id == session.Id);
        if (existing == null)
            return;

        var index = RecentSessions.IndexOf(existing);
        if (index >= 0)
            RecentSessions[index] = session;
    }

    private void RemoveRecentSession(int sessionId)
    {
        var existing = RecentSessions.FirstOrDefault(s => s.Id == sessionId);
        if (existing != null)
            RecentSessions.Remove(existing);
    }

    private async Task EnsureSessionVisibleAsync(Session session)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (RecentSessions.Any(s => s.Id == session.Id))
                return;

            var insertIndex = 0;
            while (insertIndex < RecentSessions.Count && RecentSessions[insertIndex].Fecha > session.Fecha)
                insertIndex++;

            RecentSessions.Insert(insertIndex, session);

            if (!IsSessionsListExpanded)
                IsSessionsListExpanded = true;

            SyncVisibleSessionRows();
        });
    }

    private async Task RemoveLocalVideoClipAsync(VideoClip localVideo)
    {
        try
        {
            if (!string.IsNullOrEmpty(localVideo.LocalClipPath) && File.Exists(localVideo.LocalClipPath))
            {
                try { File.Delete(localVideo.LocalClipPath); } catch { }
            }

            if (!string.IsNullOrEmpty(localVideo.LocalThumbnailPath) && File.Exists(localVideo.LocalThumbnailPath))
            {
                try { File.Delete(localVideo.LocalThumbnailPath); } catch { }
            }

            await _databaseService.DeleteVideoClipAsync(localVideo.Id);
            _databaseService.InvalidateCache();

            await MainThread.InvokeOnMainThreadAsync(() => Videos.RemoveVideoFromCaches(localVideo));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando referencia local: {ex.Message}");
        }
    }

    private async Task RemoveLocalSessionIfEmptyAsync(int sessionId)
    {
        try
        {
            var db = await _databaseService.GetConnectionAsync();
            var remaining = await db.Table<VideoClip>().Where(v => v.SessionId == sessionId).CountAsync();
            if (remaining > 0)
                return;

            await _databaseService.DeleteSessionCascadeAsync(sessionId, deleteSessionFiles: false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var session = RecentSessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                    RecentSessions.Remove(session);

                if (SelectedSession?.Id == sessionId)
                    SelectedSession = null;

                SyncVisibleSessionRows();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Remote] Error eliminando sesión local vacía: {ex.Message}");
        }
    }

    private void ToggleSessionGroupExpanded(string? groupKey)
    {
        if (string.IsNullOrWhiteSpace(groupKey))
            return;

        var current = _sessionGroupExpandedState.TryGetValue(groupKey, out var v) && v;
        _sessionGroupExpandedState[groupKey] = !current;
        SyncVisibleSessionRows();
    }

    private List<SessionGroup> BuildSessionGroups(List<Session> sessions)
    {
        var culture = new CultureInfo("es-ES");
        var today = DateTime.Today;

        DateTime StartOfWeek(DateTime d)
        {
            // Semana empieza en lunes
            var diff = ((int)d.DayOfWeek + 6) % 7;
            return d.Date.AddDays(-diff);
        }

        var startThisWeek = StartOfWeek(today);
        var startLastWeek = startThisWeek.AddDays(-7);
        var startTwoWeeksAgo = startThisWeek.AddDays(-14);

        string BucketKey(DateTime d)
        {
            if (d.Date == today) return "today";
            if (d.Date == today.AddDays(-1)) return "yesterday";
            if (d.Date >= startThisWeek) return "this_week";
            if (d.Date >= startLastWeek) return "last_week";
            if (d.Date >= startTwoWeeksAgo) return "two_weeks";
            return $"month_{d:yyyy_MM}";
        }

        string BucketTitle(string key)
        {
            return key switch
            {
                "today" => "Hoy",
                "yesterday" => "Ayer",
                "this_week" => "Esta semana",
                "last_week" => "Semana pasada",
                "two_weeks" => "Hace dos semanas",
                _ when key.StartsWith("month_") =>
                    DateTime.ParseExact(key[6..], "yyyy_MM", CultureInfo.InvariantCulture)
                        .ToString("MMMM yyyy", culture),
                _ => key
            };
        }

        int BucketSortOrder(string key)
        {
            // Menor = más arriba
            return key switch
            {
                "today" => 0,
                "yesterday" => 1,
                "this_week" => 2,
                "last_week" => 3,
                "two_weeks" => 4,
                _ => 100
            };
        }

        var grouped = sessions
            .Select(s => new { Session = s, Date = s.FechaDateTime })
            .GroupBy(x => BucketKey(x.Date))
            .ToList();

        var result = new List<SessionGroup>();

        var relativeGroups = grouped
            .Where(g => !g.Key.StartsWith("month_"))
            .OrderBy(g => BucketSortOrder(g.Key));

        foreach (var g in relativeGroups)
        {
            var sessionsInGroup = g
                .Select(x => x.Session)
                .OrderByDescending(s => s.FechaDateTime)
                .ThenByDescending(s => s.Id)
                .ToList();

            var expanded = _sessionGroupExpandedState.TryGetValue(g.Key, out var v) ? v : true;
            result.Add(new SessionGroup(g.Key, BucketTitle(g.Key), sessionsInGroup, expanded));
        }

        var monthGroups = grouped
            .Where(g => g.Key.StartsWith("month_"))
            .OrderByDescending(g => g.Key); // month_YYYY_MM ordenable como string

        foreach (var g in monthGroups)
        {
            var sessionsInGroup = g
                .Select(x => x.Session)
                .OrderByDescending(s => s.FechaDateTime)
                .ThenByDescending(s => s.Id)
                .ToList();

            // Por defecto, meses colapsados
            var expanded = _sessionGroupExpandedState.TryGetValue(g.Key, out var v) ? v : false;
            result.Add(new SessionGroup(g.Key, BucketTitle(g.Key), sessionsInGroup, expanded));
        }

        return result;
    }

    public ObservableCollection<SectionStats> SelectedSessionSectionStats { get; } = new();
    public ObservableCollection<SessionAthleteTimeRow> SelectedSessionAthleteTimes { get; } = new();

    // Datos (DB) de la sesión seleccionada
    public ObservableCollection<Input> SelectedSessionInputs { get; } = new();
    public ObservableCollection<Valoracion> SelectedSessionValoraciones { get; } = new();

    // Series para gráfico (duración total por sección, en minutos)
    public ObservableCollection<double> SelectedSessionSectionDurationMinutes { get; } = new();
    public ObservableCollection<string> SelectedSessionSectionLabels { get; } = new();

    // Etiquetas (tags): vídeos etiquetados por TagId
    public ObservableCollection<double> SelectedSessionTagVideoCounts { get; } = new();
    public ObservableCollection<string> SelectedSessionTagLabels { get; } = new();

    // Penalizaciones: conteo de tags 2 y 50
    public ObservableCollection<double> SelectedSessionPenaltyCounts { get; } = new();
    public ObservableCollection<string> SelectedSessionPenaltyLabels { get; } = new();

    // ==================== ESTADÍSTICAS ABSOLUTAS (CONTADORES) ====================
    private int _totalEventTagsCount;
    private int _totalUniqueEventTagsCount;
    private int _totalLabelTagsCount;
    private int _totalUniqueLabelTagsCount;
    private int _labeledVideosCount;
    private int _totalVideosForLabeling;
    private double _avgTagsPerSession;

    /// <summary>Total de eventos de tag establecidos (Input.IsEvent = 1)</summary>
    public int TotalEventTagsCount
    {
        get => _totalEventTagsCount;
        set => SetProperty(ref _totalEventTagsCount, value);
    }

    /// <summary>Número de tipos de eventos únicos usados</summary>
    public int TotalUniqueEventTagsCount
    {
        get => _totalUniqueEventTagsCount;
        set => SetProperty(ref _totalUniqueEventTagsCount, value);
    }

    /// <summary>Total de etiquetas asignadas (Input.IsEvent = 0)</summary>
    public int TotalLabelTagsCount
    {
        get => _totalLabelTagsCount;
        set => SetProperty(ref _totalLabelTagsCount, value);
    }

    /// <summary>Número de etiquetas únicas usadas</summary>
    public int TotalUniqueLabelTagsCount
    {
        get => _totalUniqueLabelTagsCount;
        set => SetProperty(ref _totalUniqueLabelTagsCount, value);
    }

    /// <summary>Vídeos con nombre/etiqueta asignada</summary>
    public int LabeledVideosCount
    {
        get => _labeledVideosCount;
        set => SetProperty(ref _labeledVideosCount, value);
    }

    /// <summary>Total de vídeos en contexto para calcular porcentaje</summary>
    public int TotalVideosForLabeling
    {
        get => _totalVideosForLabeling;
        set => SetProperty(ref _totalVideosForLabeling, value);
    }

    /// <summary>Media de etiquetas por sesión</summary>
    public double AvgTagsPerSession
    {
        get => _avgTagsPerSession;
        set => SetProperty(ref _avgTagsPerSession, value);
    }

    /// <summary>Texto formateado de vídeos etiquetados</summary>
    public string LabeledVideosText => TotalVideosForLabeling > 0 
        ? $"{LabeledVideosCount} de {TotalVideosForLabeling}"
        : "0";

    /// <summary>Porcentaje de vídeos etiquetados</summary>
    public double LabeledVideosPercentage => TotalVideosForLabeling > 0
        ? (double)LabeledVideosCount / TotalVideosForLabeling * 100
        : 0;

    /// <summary>Top tags de eventos más usados</summary>
    // ==================== ESTADÍSTICAS USUARIO ====================
    private UserAthleteStats? _userAthleteStats;
    private int? _referenceAthleteId;
    private string? _referenceAthleteName;

    public UserAthleteStats? UserAthleteStats
    {
        get => _userAthleteStats;
        set => SetProperty(ref _userAthleteStats, value);
    }

    public int? ReferenceAthleteId
    {
        get => _referenceAthleteId;
        set => SetProperty(ref _referenceAthleteId, value);
    }

    public string? ReferenceAthleteName
    {
        get => _referenceAthleteName;
        set => SetProperty(ref _referenceAthleteName, value);
    }

    public bool HasReferenceAthlete => ReferenceAthleteId.HasValue;
    public bool HasUserStats => UserAthleteStats?.HasData == true;

    public ObservableCollection<AthleteComparisonRow> AthleteComparison { get; } = new();

    // Estadísticas personales extendidas
    private UserPersonalStats? _userPersonalStats;
    public UserPersonalStats? UserPersonalStats
    {
        get => _userPersonalStats;
        set => SetProperty(ref _userPersonalStats, value);
    }
    
    // Propiedades para binding de gráficos de valoraciones
    public ObservableCollection<double> ValoracionFisicoValues { get; } = new();
    public ObservableCollection<string> ValoracionFisicoLabels { get; } = new();
    public ObservableCollection<double> ValoracionMentalValues { get; } = new();
    public ObservableCollection<string> ValoracionMentalLabels { get; } = new();
    public ObservableCollection<double> ValoracionTecnicoValues { get; } = new();
    public ObservableCollection<string> ValoracionTecnicoLabels { get; } = new();
    
    // Propiedades para binding de gráfico de medias por sesión
    public ObservableCollection<double> SessionAverageValues { get; } = new();
    public ObservableCollection<string> SessionAverageLabels { get; } = new();
    
    // Propiedades para binding de evolución de penalizaciones
    public ObservableCollection<double> PenaltyEvolutionValues { get; } = new();
    public ObservableCollection<string> PenaltyEvolutionLabels { get; } = new();

    private async Task LoadUserAthleteStatsAsync()
    {
        if (!ReferenceAthleteId.HasValue)
        {
            UserAthleteStats = null;
            UserPersonalStats = null;
            AthleteComparison.Clear();
            ClearPersonalStatsCharts();
            return;
        }

        try
        {
            int? sessionId = IsAllGallerySelected ? null : SelectedSession?.Id;

            // Cargar estadísticas del atleta de referencia
            UserAthleteStats = await _statisticsService.GetUserAthleteStatsAsync(ReferenceAthleteId.Value, sessionId);

            // Cargar comparativa con otros atletas
            var comparison = await _statisticsService.GetAthleteComparisonAsync(ReferenceAthleteId.Value, sessionId, 5);
            AthleteComparison.Clear();
            foreach (var row in comparison)
            {
                AthleteComparison.Add(row);
            }

            // Cargar estadísticas personales extendidas
            UserPersonalStats = await _statisticsService.GetUserPersonalStatsAsync(ReferenceAthleteId.Value, sessionId);
            UpdatePersonalStatsCharts();

            OnPropertyChanged(nameof(HasUserStats));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando estadísticas de usuario: {ex.Message}");
        }
    }

    private void ClearPersonalStatsCharts()
    {
        ValoracionFisicoValues.Clear();
        ValoracionFisicoLabels.Clear();
        ValoracionMentalValues.Clear();
        ValoracionMentalLabels.Clear();
        ValoracionTecnicoValues.Clear();
        ValoracionTecnicoLabels.Clear();
        SessionAverageValues.Clear();
        SessionAverageLabels.Clear();
        PenaltyEvolutionValues.Clear();
        PenaltyEvolutionLabels.Clear();
    }

    private void UpdatePersonalStatsCharts()
    {
        ClearPersonalStatsCharts();
        
        if (UserPersonalStats == null) return;

        // Medias por sesión
        foreach (var avg in UserPersonalStats.SessionAverages)
        {
            SessionAverageValues.Add(avg.AverageTimeMs / 1000.0); // Convertir a segundos
            SessionAverageLabels.Add(avg.SessionDateShort);
        }

        // Evolución de valoraciones - Físico
        foreach (var point in UserPersonalStats.ValoracionEvolution.Fisico)
        {
            ValoracionFisicoValues.Add(point.Value);
            ValoracionFisicoLabels.Add(point.DateShort);
        }

        // Evolución de valoraciones - Mental
        foreach (var point in UserPersonalStats.ValoracionEvolution.Mental)
        {
            ValoracionMentalValues.Add(point.Value);
            ValoracionMentalLabels.Add(point.DateShort);
        }

        // Evolución de valoraciones - Técnico
        foreach (var point in UserPersonalStats.ValoracionEvolution.Tecnico)
        {
            ValoracionTecnicoValues.Add(point.Value);
            ValoracionTecnicoLabels.Add(point.DateShort);
        }

        // Evolución de penalizaciones
        foreach (var point in UserPersonalStats.PenaltyEvolution)
        {
            PenaltyEvolutionValues.Add(point.PenaltyCount);
            PenaltyEvolutionLabels.Add(point.DateShort);
        }
    }

    private async Task LoadReferenceAthleteAsync()
    {
        try
        {
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId.HasValue == true)
            {
                ReferenceAthleteId = profile.ReferenceAthleteId;

                var athletes = await _databaseService.GetAllAthletesAsync();
                var refAthlete = athletes.FirstOrDefault(a => a.Id == ReferenceAthleteId.Value);
                ReferenceAthleteName = refAthlete != null
                    ? $"{refAthlete.Nombre} {refAthlete.Apellido}".Trim()
                    : null;
            }
            else
            {
                ReferenceAthleteId = null;
                ReferenceAthleteName = null;
            }
            OnPropertyChanged(nameof(HasReferenceAthlete));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error cargando atleta de referencia: {ex.Message}");
        }
    }

    private void ClearLocalSelectionForRemote()
    {
        IsAllGallerySelected = false;
        IsVideoLessonsSelected = false;
        IsDiaryViewSelected = false;
        SelectedSession = null;
        OnPropertyChanged(nameof(SelectedSessionTitle));
    }

    public ICommand ImportCommand { get; }
    public ICommand ImportCrownFileCommand { get; }
    public ICommand CreateSessionFromVideosCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ViewSessionCommand { get; }
    public ICommand ViewAllSessionsCommand { get; }
    public ICommand ViewAthletesCommand { get; }
    public ICommand PlaySelectedVideoCommand { get; }
    public ICommand DeleteSelectedSessionCommand { get; }
    public ICommand ToggleSessionFavoriteCommand { get; }
    public ICommand ToggleVideoFavoriteCommand { get; }
    public ICommand SelectFavoriteSessionCommand { get; }
    public ICommand LoadMoreVideosCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand ToggleFilterItemCommand { get; }
    public ICommand ToggleSessionGroupExpandedCommand { get; }
    public ICommand SelectSessionRowCommand { get; }
    public ICommand TogglePlacesExpandedCommand { get; }
    public ICommand ToggleAthletesExpandedCommand { get; }
    public ICommand ToggleSectionsExpandedCommand { get; }
    public ICommand ToggleTagsExpandedCommand { get; }
    public ICommand SelectStatsTabCommand { get; }
    public ICommand SelectCrudTechTabCommand { get; }
    public ICommand ToggleAddNewSessionCommand { get; }
    public ICommand CreateNewSessionCommand { get; }
    public ICommand CancelNewSessionCommand { get; }
    public ICommand SelectSessionTypeCommand { get; }
    public ICommand OpenCameraRecordingCommand { get; }
    public ICommand OpenNewSessionSidebarPopupCommand { get; }
    public ICommand CancelNewSessionSidebarPopupCommand { get; }
    public ICommand CreateSessionAndRecordCommand { get; }

    public ICommand OpenIconColorPickerForSessionCommand { get; }
    public ICommand CloseIconColorPickerCommand { get; }
    public ICommand SelectPickerIconCommand { get; }
    public ICommand SelectPickerColorCommand { get; }
    public ICommand ToggleSessionsExpansionCommand { get; }
    public ICommand RenameSessionCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand SetSessionIconCommand { get; }
    public ICommand SetSessionColorCommand { get; }
    public ICommand SelectPlaceOptionCommand { get; }
    public ICommand RecordForSelectedSessionCommand { get; }
    public ICommand ExportSelectedSessionCommand { get; }
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
    public ICommand ToggleSectionTimesViewCommand { get; }
    public ICommand OpenDetailedTimesPopupCommand { get; }
    public ICommand CloseDetailedTimesPopupCommand { get; }
    public ICommand ClearDetailedTimesAthleteCommand { get; }
    public ICommand ToggleAthleteDropdownCommand { get; }
    public ICommand SelectDetailedTimesAthleteCommand { get; }
    public ICommand ToggleLapTimesModeCommand { get; }
    public ICommand SetLapTimesModeCommand { get; }
    public ICommand ExportDetailedTimesHtmlCommand { get; }
    public ICommand ExportDetailedTimesPdfCommand { get; }
    
    // Propiedades de edición de sesión individual
    public bool ShowSessionEditPopup
    {
        get => _showSessionEditPopup;
        set => SetProperty(ref _showSessionEditPopup, value);
    }

    public string? SessionEditName
    {
        get => _sessionEditName;
        set => SetProperty(ref _sessionEditName, value);
    }

    public string? SessionEditLugar
    {
        get => _sessionEditLugar;
        set => SetProperty(ref _sessionEditLugar, value);
    }

    public string? SessionEditTipoSesion
    {
        get => _sessionEditTipoSesion;
        set => SetProperty(ref _sessionEditTipoSesion, value);
    }

    // Comandos de edición de sesión
    public ICommand OpenSessionEditPopupCommand { get; }
    public ICommand CloseSessionEditPopupCommand { get; }
    public ICommand ApplySessionEditCommand { get; }

    public DashboardViewModel(
        DatabaseService databaseService,
        ITrashService trashService,
        CrownFileService crownFileService,
        StatisticsService statisticsService,
        ThumbnailService thumbnailService,
        ITableExportService tableExportService,
            VideosViewModel videos,
        LayoutStateViewModel layout,
        BatchEditViewModel batchEdit,
        SmartFoldersViewModel smartFolders,
        RemoteLibraryViewModel remote,
        DiaryWellnessViewModel diary,
        ICloudBackendService cloudBackendService,
        StorageMigrationService? migrationService = null,
        VideoExportNotifier? videoExportNotifier = null,
        ImportProgressService? importProgressService = null)
    {
        _databaseService = databaseService;
        _trashService = trashService;
        _crownFileService = crownFileService;
        _statisticsService = statisticsService;
        _thumbnailService = thumbnailService;
        _tableExportService = tableExportService;
        _cloudBackendService = cloudBackendService;
        _migrationService = migrationService;
        _importProgressService = importProgressService;
        Layout = layout;
        BatchEdit = batchEdit;
        SmartFolders = smartFolders;
        Remote = remote;
        Diary = diary;
    Videos = videos;

        // Suscribirse a eventos de exportación de video
        if (videoExportNotifier != null)
        {
            videoExportNotifier.VideoExported += OnVideoExported;
        }

        // Suscribirse a eventos de importación en segundo plano
        if (_importProgressService != null)
        {
            _importProgressService.ProgressChanged += OnImportProgressChanged;
            _importProgressService.ImportCompleted += OnImportCompleted;
            
            // Verificar si hay una importación en curso al iniciar
            if (_importProgressService.IsImporting && _importProgressService.CurrentTask != null)
            {
                IsBackgroundImporting = true;
                BackgroundImportProgress = _importProgressService.CurrentTask.Percentage;
                BackgroundImportText = _importProgressService.CurrentTask.Name;
            }
        }

        Title = "Dashboard";

        Layout.Configure(
            Videos.SelectAllGalleryAsync,
            Videos.ViewVideoLessonsAsync,
            ViewTrashAsync,
            () => IsDiaryViewSelected = true,
            () => IsFavoritesExpanded = !IsFavoritesExpanded,
            SelectFavoriteSessionsAsync,
            Videos.SelectFavoriteVideosAsync,
            () => IsUserLibraryExpanded = !IsUserLibraryExpanded,
            () => Layout.IsRemoteLibraryExpanded = !Layout.IsRemoteLibraryExpanded,
            () => IsSessionsListExpanded = !IsSessionsListExpanded,
            () => Layout.IsRemoteSessionsListExpanded = !Layout.IsRemoteSessionsListExpanded,
            () => Layout.IsRemoteSmartFoldersExpanded = !Layout.IsRemoteSmartFoldersExpanded);

        BatchEdit.Configure(
            () => Videos.SelectedSessionVideos.ToList(),
            () => Videos.FilteredVideosCache?.ToList(),
            () => Videos.AllVideosCache?.ToList(),
            () => SelectedSession,
            () => IsAllGallerySelected,
            async () =>
            {
                if (SelectedSession != null)
                    await Videos.LoadSelectedSessionVideosAsync(SelectedSession);
            },
            async () =>
            {
                if (IsAllGallerySelected)
                    await Videos.LoadAllVideosAsync();
            });

        SmartFolders.Configure(
            () => Videos.AllVideosCache,
            () => Videos.LoadAllVideosAsync(),
            () => Videos.ClearFilters(true),
            () => SelectedSession = null,
            () => IsAllGallerySelected = true,
            Videos.ApplySmartFolderFilteredResultsAsync,
            () => Videos.IncrementFiltersVersion(),
            () => Videos.ReadFiltersVersion(),
            visible => Layout.ShowSmartFolderSidebarPopup = visible,
            OpenIconColorPickerForSmartFolder);

        Remote.Configure(
            () => Layout.IsRemoteLibraryVisible,
            visible => Layout.IsRemoteLibraryVisible = visible,
            expanded => Layout.IsRemoteLibraryExpanded = expanded,
            () => Layout.IsRemoteAllGallerySelected,
            selected => Layout.IsRemoteAllGallerySelected = selected,
            () => Layout.IsRemoteVideoLessonsSelected,
            selected => Layout.IsRemoteVideoLessonsSelected = selected,
            () => Layout.IsRemoteTrashSelected,
            selected => Layout.IsRemoteTrashSelected = selected,
            () => Layout.IsAllGallerySelected,
            () => Layout.IsVideoLessonsSelected,
            () => Layout.IsDiaryViewSelected,
            () => Layout.IsFavoriteVideosSelected,
            () => Layout.IsFavoriteSessionsSelected,
            () => SelectedSession,
            session => SelectedSession = session,
            Videos.ClearLocalSelectionForRemote,
            () => Videos.NotifySelectionChanged(),
            () => OnPropertyChanged(nameof(SelectedSessionTitle)),
            () => Videos.LoadAllVideosAsync(),
            session => Videos.LoadSelectedSessionVideosAsync(session),
            () => Videos.AllVideosCache,
            video => Videos.InsertVideoIntoAllCache(video),
            video => Videos.RefreshVideoClipInGalleryAsync(video.Id),
            EnsureSessionVisibleAsync,
            () => RecentSessions,
            ReplaceRecentSession,
            RemoveRecentSession,
            SyncVisibleSessionRows,
            RemoveLocalVideoClipAsync,
            RemoveLocalSessionIfEmptyAsync,
            EnsurePlaceExistsAsync,
            video => Videos.PlaySelectedVideoAsync(video),
            () => Videos.NotifySelectedSessionVideosChanged());

        Diary.Configure(
            () => SelectedSession,
            () =>
            {
                Layout.IsDiaryTabSelected = true;
                Layout.IsStatsTabSelected = false;
                Layout.IsCrudTechTabSelected = false;
            });

        Videos.Configure(
            () => SelectedSession,
            session => SelectedSession = session,
            () => RecentSessions.ToList(),
            () => Stats,
            stats => Stats = stats,
            () => IsAllGallerySelected,
            value => IsAllGallerySelected = value,
            () => IsVideoLessonsSelected,
            value => IsVideoLessonsSelected = value,
            () => IsFavoriteVideosSelected,
            value => IsFavoriteVideosSelected = value,
            () => IsDiaryViewSelected,
            value => IsDiaryViewSelected = value,
            () => ReferenceAthleteId,
            RefreshTrashItemCountAsync,
            count => FavoriteSessionsCount = count);

        ImportCommand = new AsyncRelayCommand(ShowImportOptionsAsync);
        ImportCrownFileCommand = new AsyncRelayCommand(ImportCrownFileAsync);
        CreateSessionFromVideosCommand = new AsyncRelayCommand(OpenImportPageForVideosAsync);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        ViewSessionCommand = new AsyncRelayCommand<Session>(ViewSessionAsync);
        ViewAllSessionsCommand = new AsyncRelayCommand(ViewAllSessionsAsync);
        ViewAthletesCommand = new AsyncRelayCommand(ViewAthletesAsync);
        PlaySelectedVideoCommand = new AsyncRelayCommand<VideoClip>(Videos.PlaySelectedVideoAsync);
        DeleteSelectedSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync);
        ToggleSessionFavoriteCommand = new AsyncRelayCommand<SessionRow>(ToggleSessionFavoriteAsync);
        ToggleVideoFavoriteCommand = new AsyncRelayCommand<VideoClip>(Videos.ToggleVideoFavoriteAsync);
        SelectFavoriteSessionCommand = new RelayCommand<Session>(SelectFavoriteSession);
        LoadMoreVideosCommand = new AsyncRelayCommand(Videos.LoadMoreVideosAsync);
        ClearFiltersCommand = new RelayCommand(() => Videos.ClearFilters());
        ToggleFilterItemCommand = new RelayCommand<object?>(Videos.ToggleFilterItem);
        ToggleSessionGroupExpandedCommand = new RelayCommand<string>(ToggleSessionGroupExpanded);
        SelectSessionRowCommand = new RelayCommand<SessionRow>(row => { if (row != null) SelectedSessionListItem = row; });
        TogglePlacesExpandedCommand = new RelayCommand(() => Videos.IsPlacesExpanded = !Videos.IsPlacesExpanded);
        ToggleAthletesExpandedCommand = new RelayCommand(() => Videos.IsAthletesExpanded = !Videos.IsAthletesExpanded);
        ToggleSectionsExpandedCommand = new RelayCommand(() => Videos.IsSectionsExpanded = !Videos.IsSectionsExpanded);
        ToggleTagsExpandedCommand = new RelayCommand(() => Videos.IsTagsExpanded = !Videos.IsTagsExpanded);

        RenameSessionCommand = new AsyncRelayCommand<SessionRow>(RenameSessionAsync);
        DeleteSessionCommand = new AsyncRelayCommand<SessionRow>(DeleteSessionAsync);
        SetSessionIconCommand = new RelayCommand<object?>(SetSessionIcon);
        SetSessionColorCommand = new RelayCommand<object?>(SetSessionColor);

        // Icon/Color Picker Popup commands
        OpenIconColorPickerForSessionCommand = new RelayCommand<SessionRow>(OpenIconColorPickerForSession);
        CloseIconColorPickerCommand = new RelayCommand(CloseIconColorPicker);
        SelectPickerIconCommand = new RelayCommand<string>(SelectPickerIcon);
        SelectPickerColorCommand = new RelayCommand<string>(SelectPickerColor);
        ToggleSessionsExpansionCommand = new RelayCommand(ToggleSessionsExpansion);
        SelectPlaceOptionCommand = new RelayCommand<PlaceOption>(SelectPlaceOption);

        SelectStatsTabCommand = new RelayCommand(() => IsStatsTabSelected = true);
        SelectCrudTechTabCommand = new RelayCommand(() => IsCrudTechTabSelected = true);
        ToggleAddNewSessionCommand = new RelayCommand(() => 
        {
            IsAddingNewSession = !IsAddingNewSession;
            if (IsAddingNewSession)
            {
                // Valores por defecto
                NewSessionName = "";
                NewSessionType = SessionTypeOptions.FirstOrDefault()?.Name ?? "Entrenamiento";
                NewSessionLugar = "";
                // Reset selection
                foreach (var opt in SessionTypeOptions)
                    opt.IsSelected = opt.Name == NewSessionType;
            }
        });
        CreateNewSessionCommand = new AsyncRelayCommand(CreateNewSessionAsync);
        CancelNewSessionCommand = new RelayCommand(() => IsAddingNewSession = false);
        OpenCameraRecordingCommand = new AsyncRelayCommand(OpenCameraRecordingAsync);
        OpenNewSessionSidebarPopupCommand = new RelayCommand(() =>
        {
            ShowNewSessionSidebarPopup = true;
            // Valores por defecto
            NewSessionName = "";
            NewSessionType = SessionTypeOptions.FirstOrDefault()?.Name ?? "Entrenamiento";
            NewSessionLugar = "";
            foreach (var opt in SessionTypeOptions)
                opt.IsSelected = opt.Name == NewSessionType;
        });
        CancelNewSessionSidebarPopupCommand = new RelayCommand(() => ShowNewSessionSidebarPopup = false);
        CreateSessionAndRecordCommand = new AsyncRelayCommand(CreateSessionAndRecordAsync);

        SmartFolders.LoadSmartFoldersFromPreferences();
        LoadSessionCustomizationsFromPreferences();
        LoadNasConnectionFromPreferences();

        RecordForSelectedSessionCommand = new AsyncRelayCommand(RecordForSelectedSessionAsync);
        ExportSelectedSessionCommand = new AsyncRelayCommand(ExportSelectedSessionAsync);
        SelectSessionTypeCommand = new RelayCommand<SessionTypeOption>(option => 
        {
            if (option != null)
            {
                foreach (var opt in SessionTypeOptions)
                    opt.IsSelected = opt == option;
                NewSessionType = option.Name;
            }
        });

        VideoTapCommand = new AsyncRelayCommand<VideoClip>(Videos.OnVideoTappedAsync);
        VideoLessonTapCommand = new AsyncRelayCommand<VideoLesson>(Videos.OnVideoLessonTappedAsync);
        ShareVideoLessonCommand = new AsyncRelayCommand<VideoLesson>(Videos.ShareVideoLessonAsync);
        DeleteVideoLessonCommand = new AsyncRelayCommand<VideoLesson>(Videos.DeleteVideoLessonAsync);
        PlayAsPlaylistCommand = new AsyncRelayCommand(Videos.PlayAsPlaylistAsync);
        ShareSelectedVideosCommand = new AsyncRelayCommand(Videos.ShareSelectedVideosAsync);
        DeleteSelectedVideosCommand = new AsyncRelayCommand(Videos.DeleteSelectedVideosAsync);
        PlayParallelAnalysisCommand = new AsyncRelayCommand(Videos.PlayParallelAnalysisAsync);
        PreviewParallelAnalysisCommand = new AsyncRelayCommand(Videos.PreviewParallelAnalysisAsync);
        ClearParallelAnalysisCommand = new RelayCommand(Videos.ClearParallelAnalysis);
        DropOnScreen1Command = new RelayCommand<VideoClip>(video => Videos.ParallelVideo1 = video);
        DropOnScreen2Command = new RelayCommand<VideoClip>(video => Videos.ParallelVideo2 = video);
        ToggleQuickAnalysisIsolatedModeCommand = new RelayCommand(() => Videos.IsQuickAnalysisIsolatedMode = !Videos.IsQuickAnalysisIsolatedMode);
        
        // Comandos de edición de sesión individual
        OpenSessionEditPopupCommand = new RelayCommand<SessionRow>(OpenSessionEditPopup);
        CloseSessionEditPopupCommand = new RelayCommand(() => ShowSessionEditPopup = false);
        ApplySessionEditCommand = new AsyncRelayCommand(ApplySessionEditAsync);
        
        // Comando para alternar vista de tiempos
        ToggleSectionTimesViewCommand = new RelayCommand(() => Videos.ShowSectionTimesDifferences = !Videos.ShowSectionTimesDifferences);
        
        // Comandos para popup de tiempos detallados
        OpenDetailedTimesPopupCommand = new AsyncRelayCommand(Videos.OpenDetailedTimesPopupAsync);
        CloseDetailedTimesPopupCommand = new RelayCommand(() => Videos.ShowDetailedTimesPopup = false);
        ClearDetailedTimesAthleteCommand = new RelayCommand(() =>
        {
            Videos.SelectedDetailedTimesAthlete = null;
            Videos.IsAthleteDropdownExpanded = false;
        });
        ToggleAthleteDropdownCommand = new RelayCommand(() => Videos.IsAthleteDropdownExpanded = !Videos.IsAthleteDropdownExpanded);
        SelectDetailedTimesAthleteCommand = new RelayCommand<AthletePickerItem>(athlete =>
        {
            Videos.SelectedDetailedTimesAthlete = athlete;
            Videos.IsAthleteDropdownExpanded = false;
        });
        ToggleLapTimesModeCommand = new RelayCommand(() => Videos.ShowCumulativeTimes = !Videos.ShowCumulativeTimes);
        SetLapTimesModeCommand = new RelayCommand<string>(mode =>
        {
            Videos.ShowCumulativeTimes = string.Equals(mode, "acum", StringComparison.OrdinalIgnoreCase);
        });

        ExportDetailedTimesHtmlCommand = new AsyncRelayCommand(Videos.ExportDetailedTimesHtmlAsync);
        ExportDetailedTimesPdfCommand = new AsyncRelayCommand(Videos.ExportDetailedTimesPdfAsync);
    }

    public int TrashItemCount
    {
        get => _trashItemCount;
        private set => SetProperty(ref _trashItemCount, value);
    }

    private async Task RefreshTrashItemCountAsync()
    {
        try
        {
            var sessions = await _trashService.GetTrashedSessionsAsync();
            var videos = await _trashService.GetTrashedVideosAsync();
            TrashItemCount = (sessions?.Count ?? 0) + (videos?.Count ?? 0);
        }
        catch
        {
            // Best-effort: no bloquear la UI por fallos de conteo.
        }
    }

    /// <summary>
    /// Manejador para cuando se exporta un video comparativo
    /// </summary>
    private async void OnVideoExported(object? sender, VideoExportedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Video exportado: SessionId={e.SessionId}, ClipId={e.VideoClipId}");
            
            // Si estamos viendo la galería general, recargar
            if (IsAllGallerySelected)
            {
                await Videos.LoadAllVideosAsync();
            }
            // Si estamos viendo la sesión donde se exportó el video, recargar esa sesión
            else if (SelectedSession?.Id == e.SessionId)
            {
                await Videos.LoadSelectedSessionVideosAsync(SelectedSession);
            }
            // Si no estamos viendo esa sesión, agregar el video al cache para que aparezca cuando se seleccione
            else if (Videos.AllVideosCache != null)
            {
                // Cargar el nuevo clip de la base de datos
                var newClip = await _databaseService.GetVideoClipByIdAsync(e.VideoClipId);
                if (newClip != null)
                {
                    Videos.InsertVideoIntoAllCache(newClip);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Error al procesar video exportado: {ex.Message}");
        }
    }

    /// <summary>
    /// Manejador para cambios de progreso en la importación en segundo plano
    /// </summary>
    private void OnImportProgressChanged(object? sender, ImportTask task)
    {
        IsBackgroundImporting = !task.IsCompleted;
        BackgroundImportProgress = task.Percentage;
        BackgroundImportText = $"{task.Name} ({task.ProcessedFiles}/{task.TotalFiles})";
        ImportingSessionName = task.Name.Replace("Sesión: ", "");
        ImportingCurrentFile = task.CurrentFile;
        OnPropertyChanged(nameof(BackgroundImportProgressNormalized));
    }

    /// <summary>
    /// Manejador para cuando se completa una importación en segundo plano
    /// </summary>
    private async void OnImportCompleted(object? sender, ImportTask task)
    {
        System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] OnImportCompleted llamado. HasError={task.HasError}, SessionId={task.CreatedSessionId}");
        
        if (task.HasError)
        {
            IsBackgroundImporting = false;
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Importación fallida: {task.ErrorMessage}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Importación completada: {task.Name}, SessionId={task.CreatedSessionId}");
        
        try
        {
            // Forzar recarga de sesiones directamente para evitar conflictos con IsBusy
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Recargando sesiones... IsBusy={IsBusy}");
            
            var stats = await _statisticsService.GetDashboardStatsAsync();
            
            // Actualizar en el hilo principal
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Stats = stats;
                RecentSessions.Clear();
                foreach (var session in stats.RecentSessions)
                {
                    RecentSessions.Add(session);
                }

                SyncFavoriteSessionsFromRecent();
                
                // Asegurar que la lista esté expandida para mostrar la nueva sesión
                if (!IsSessionsListExpanded)
                {
                    IsSessionsListExpanded = true;
                }

                SyncVisibleSessionRows();

                System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Sesiones recargadas: {RecentSessions.Count} total, filas={SessionRows.Count}");
            });
            
            // Ahora ocultamos el indicador de importación
            IsBackgroundImporting = false;
            
            // Si se creó una sesión, seleccionarla automáticamente
            if (task.CreatedSessionId.HasValue)
            {
                var newSession = await _databaseService.GetSessionByIdAsync(task.CreatedSessionId.Value);
                if (newSession != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Seleccionando nueva sesión: {newSession.DisplayName}");
                    SelectedSession = newSession;
                }
            }
            
            // Limpiar la tarea completada después de un breve momento
            await Task.Delay(3000);
            _importProgressService?.ClearCompletedTask();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Error al procesar importación completada: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[DashboardViewModel] Stack trace: {ex.StackTrace}");
            IsBackgroundImporting = false;
        }
    }

    private async Task OnVideoLessonTappedAsync(VideoLesson? lesson)
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

    private static string NormalizeSpaces(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var parts = value
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', parts);
    }

    

    private static ComparisonLayout DetermineLayoutFromOccupiedSlots(IReadOnlyCollection<int> occupiedSlots)
    {
        if (occupiedSlots == null || occupiedSlots.Count <= 1)
            return ComparisonLayout.Single;

        if (occupiedSlots.Count >= 3)
            return ComparisonLayout.Quad2x2;

        // Exactamente 2 slots: decidir si están en la misma fila o columna
        var has1 = occupiedSlots.Contains(1);
        var has2 = occupiedSlots.Contains(2);
        var has3 = occupiedSlots.Contains(3);
        var has4 = occupiedSlots.Contains(4);

        // Misma fila: (1,2) o (3,4) → Horizontal 2x1
        if ((has1 && has2) || (has3 && has4))
            return ComparisonLayout.Horizontal2x1;

        // Misma columna o diagonal: (1,3), (2,4), (1,4), (2,3) → Vertical 1x2
        // Todas las demás combinaciones de 2 slots usan layout vertical
        return ComparisonLayout.Vertical1x2;
    }

    private void ToggleSessionsExpansion()
    {
        IsSessionsExpanded = !IsSessionsExpanded;
    }

    private async Task SelectFavoriteSessionsAsync()
    {
        SelectedSession = null;
        SmartFolders.ClearActiveSmartFolder();

        IsFavoriteSessionsSelected = true;

        if (!IsSessionsListExpanded)
            IsSessionsListExpanded = true;

        SyncFavoriteSessionsFromRecent();
        SyncVisibleSessionRows();
        OnPropertyChanged(nameof(SelectedSessionTitle));
        await Videos.RefreshFavoritesCountsAsync();
    }

    private void SyncFavoriteSessionsFromRecent()
    {
        FavoriteSessions.Clear();
        foreach (var session in RecentSessions.Where(s => s.IsFavorite == 1))
            FavoriteSessions.Add(session);
    }

    private void SelectFavoriteSession(Session? session)
    {
        if (session == null)
            return;

        IsFavoriteSessionsSelected = true;
        if (!IsSessionsListExpanded)
            IsSessionsListExpanded = true;

        SelectedSession = session;
        SyncVisibleSessionRows();
        OnPropertyChanged(nameof(SelectedSessionTitle));
    }

    private async Task ToggleSessionFavoriteAsync(SessionRow? row)
    {
        if (row?.Session == null)
            return;

        try
        {
            var session = row.Session;
            session.IsFavorite = session.IsFavorite == 1 ? 0 : 1;
            await _databaseService.SaveSessionAsync(session);

            if (IsFavoriteSessionsSelected && session.IsFavorite == 0 && SelectedSession?.Id == session.Id)
                SelectedSession = null;

            SyncFavoriteSessionsFromRecent();
            SyncVisibleSessionRows();
            await Videos.RefreshFavoritesCountsAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardVM", "ToggleSessionFavoriteAsync error", ex);
        }
    }

    private async Task DeleteSelectedSessionAsync()
    {
        var session = SelectedSession;
        if (session == null)
        {
            await Shell.Current.DisplayAlert("Eliminar", "Selecciona una sesión primero.", "OK");
            return;
        }

        var confirm = await Shell.Current.DisplayAlert(
            "Eliminar sesión",
            $"¿Seguro que quieres mover a la papelera la sesión '{session.DisplayName}'?\n\nPodrás restaurarla durante 30 días.",
            "Eliminar",
            "Cancelar");

        if (!confirm)
            return;

        var deleted = false;
        try
        {
            IsBusy = true;
            await _trashService.MoveSessionToTrashAsync(session.Id);
            SelectedSession = null;
            deleted = true;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudo eliminar la sesión: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }

        // Refrescar la lista de sesiones (LoadDataAsync se auto-gestiona con IsBusy,
        // así que debe llamarse cuando IsBusy ya esté a false).
        if (deleted)
            await LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        if (IsBusy) return;

        try
        {
            AppLog.Info("DashboardVM", "LoadDataAsync START");
            IsBusy = true;
            var stats = await _statisticsService.GetDashboardStatsAsync();
            Stats = stats;
            AppLog.Info("DashboardVM", $"LoadDataAsync Stats loaded | RecentSessions={stats?.RecentSessions?.Count ?? 0}");

            await Videos.RefreshVideoLessonsCountAsync();

            RecentSessions.Clear();
            foreach (var session in stats?.RecentSessions ?? new List<Session>())
            {
                RecentSessions.Add(session);
            }

            SyncFavoriteSessionsFromRecent();

            SyncVisibleSessionRows();

            // Cargar el atleta de referencia del perfil del usuario
            await LoadReferenceAthleteAsync();

            // Si la sesión seleccionada ya no existe (p.ej. tras borrar), limpiar panel derecho.
            if (SelectedSession != null && !RecentSessions.Any(s => s.Id == SelectedSession.Id))
            {
                SelectedSession = null;
            }

            // Recargar videolecciones si están seleccionadas (al volver de SinglePlayerPage)
            if (IsVideoLessonsSelected)
            {
                await Videos.ViewVideoLessonsAsync();
            }
            // Por defecto, mostrar Galería General al iniciar (solo si no hay ninguna vista activa)
            else if (SelectedSession == null && !IsAllGallerySelected && !IsDiaryViewSelected)
            {
                AppLog.Info("DashboardVM", "LoadDataAsync SelectAllGalleryAsync...");
                await Videos.SelectAllGalleryAsync();
                AppLog.Info("DashboardVM", "LoadDataAsync SelectAllGalleryAsync done");
            }

            await RefreshTrashItemCountAsync();
            await Videos.RefreshFavoritesCountsAsync();
            
            // Verificar videos pendientes de sincronización
            await Remote.RefreshPendingSyncAsync();

            await LoadSessionFormOptionsAsync();
            
            AppLog.Info("DashboardVM", "LoadDataAsync END");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar los datos: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSessionFormOptionsAsync()
    {
        await LoadSessionTypeOptionsAsync();
        await LoadPlaceOptionsAsync();
    }

    private async Task LoadSessionTypeOptionsAsync()
    {
        try
        {
            var sessionTypes = await _databaseService.GetAllSessionTypesAsync();
            if (sessionTypes.Count == 0)
            {
                var defaultTypes = new[]
                {
                    "Cuartos",
                    "Medios",
                    "Largos",
                    "Gimnasio",
                    "Aguas tranquilas",
                    "Carrera",
                    "Ciclismo",
                    "Esquí de fondo",
                    "Natación",
                    "Recuperación",
                    "Otro"
                };

                foreach (var typeName in defaultTypes)
                {
                    await _databaseService.SaveSessionTypeAsync(new SessionType { TipoSesion = typeName });
                }

                sessionTypes = await _databaseService.GetAllSessionTypesAsync();
            }

            SessionTypeOptions.Clear();
            foreach (var type in sessionTypes)
            {
                if (string.IsNullOrWhiteSpace(type.TipoSesion)) continue;
                SessionTypeOptions.Add(new SessionTypeOption { Name = type.TipoSesion.Trim() });
            }

            if (SessionTypeOptions.Count > 0)
            {
                var selected = SessionTypeOptions.FirstOrDefault(o => o.Name == NewSessionType);
                if (selected == null)
                {
                    selected = SessionTypeOptions[0];
                    NewSessionType = selected.Name;
                }

                foreach (var opt in SessionTypeOptions)
                    opt.IsSelected = opt == selected;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SessionTypeOptions] Error: {ex.Message}");
        }
    }

    private async Task LoadPlaceOptionsAsync()
    {
        try
        {
            var places = await _databaseService.GetAllPlacesAsync();

            SessionPlaceOptions.Clear();
            foreach (var place in places)
            {
                if (string.IsNullOrWhiteSpace(place.NombreLugar)) continue;
                SessionPlaceOptions.Add(new PlaceOption { Name = place.NombreLugar.Trim() });
            }

            if (!string.IsNullOrWhiteSpace(NewSessionLugar))
            {
                var selected = SessionPlaceOptions.FirstOrDefault(p => p.Name == NewSessionLugar.Trim());
                if (selected != null)
                {
                    foreach (var opt in SessionPlaceOptions)
                        opt.IsSelected = opt == selected;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaceOptions] Error: {ex.Message}");
        }
    }

    private void SelectPlaceOption(PlaceOption? option)
    {
        if (option == null) return;

        foreach (var opt in SessionPlaceOptions)
            opt.IsSelected = opt == option;

        NewSessionLugar = option.Name;
    }

    private void UpdatePlaceSelectionStates(string? placeName)
    {
        if (SessionPlaceOptions.Count == 0) return;

        var normalized = placeName?.Trim();
        foreach (var option in SessionPlaceOptions)
        {
            option.IsSelected = !string.IsNullOrWhiteSpace(normalized) && option.Name == normalized;
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

    private async Task PlaySelectedVideoAsync(VideoClip? video)
    {
        AppLog.Info("DashboardVM", $"⏱️ PlaySelectedVideoAsync INICIADO - video.Id={video?.Id}, video.ClipPath={video?.ClipPath}");
        
        if (video == null) return;

        var videoPath = video.LocalClipPath;

        if (!IsStreamingUrl(videoPath))
        {
            // Fallback: construir ruta local desde la carpeta de la sesión
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
                    // Ignorar: si falla, se mostrará el error estándar
                }
            }

            // Último recurso: usar ClipPath si fuera una ruta real
            if (string.IsNullOrWhiteSpace(videoPath))
                videoPath = video.ClipPath;
        }

        // Si no hay archivo local y el video está en la organización, intentar streaming
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

        // Obtener la página del contenedor DI para poder pasar el VideoClip completo
        var navWatch = System.Diagnostics.Stopwatch.StartNew();
        AppLog.Info("DashboardVM", "⏱️ OpenVideoAsync: Obteniendo página DI...");
        
        var playerPage = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<SinglePlayerPage>();
        AppLog.Info("DashboardVM", $"⏱️ GetService<SinglePlayerPage>: {navWatch.ElapsedMilliseconds}ms");
        
        if (playerPage?.BindingContext is SinglePlayerViewModel vm)
        {
            // Asegurar que el video tiene la información de Session y Atleta cargada
            if (video.Session == null && video.SessionId > 0)
            {
                video.Session = SelectedSession ?? await _databaseService.GetSessionByIdAsync(video.SessionId);
            }
            if (video.Atleta == null && video.AtletaId > 0)
            {
                video.Atleta = await _databaseService.GetAthleteByIdAsync(video.AtletaId);
            }
            // Cargar los tags del video
            if (video.Tags == null && video.Id > 0)
            {
                video.Tags = await _databaseService.GetTagsForVideoAsync(video.Id);
            }
            
            AppLog.Info("DashboardVM", $"⏱️ Pre-carga datos video: {navWatch.ElapsedMilliseconds}ms");
            
            // Actualizar la ruta local si fue resuelta
            video.LocalClipPath = videoPath;
            
            // NAVEGAR PRIMERO para mostrar la página rápidamente
            AppLog.Info("DashboardVM", "⏱️ Iniciando navegación PushAsync...");
            var pushStart = System.Diagnostics.Stopwatch.StartNew();
            
            // Verificar si la página ya está en el stack de navegación
            var navStack = Shell.Current.Navigation.NavigationStack;
            var isAlreadyInStack = navStack.Contains(playerPage);
            AppLog.Info("DashboardVM", $"⏱️ NavigationStack.Count={navStack.Count}, isAlreadyInStack={isAlreadyInStack}");
            
            // Si no está en el stack, hacer push
            if (!isAlreadyInStack)
            {
                await Shell.Current.Navigation.PushAsync(playerPage);
            }
            AppLog.Info("DashboardVM", $"⏱️ PushAsync completado: {pushStart.ElapsedMilliseconds}ms");
            
            // LUEGO inicializar el ViewModel (la página ya está visible o se acaba de mostrar)
            AppLog.Info("DashboardVM", "⏱️ Iniciando InitializeWithVideoAsync...");
            await vm.InitializeWithVideoAsync(video);
            
            AppLog.Info("DashboardVM", $"⏱️ OpenVideoAsync TOTAL: {navWatch.ElapsedMilliseconds}ms");
        }
        else
        {
            // Fallback: navegación por URL
            await Shell.Current.GoToAsync($"{nameof(SinglePlayerPage)}?videoPath={Uri.EscapeDataString(videoPath)}");
        }
    }

    private async Task ShowImportOptionsAsync()
    {
        var action = await Shell.Current.DisplayActionSheet(
            "¿Cómo deseas importar?",
            "Cancelar",
            null,
            "Desde archivo .crown",
            "Crear sesión desde vídeos");

        if (action == "Desde archivo .crown")
        {
            await ImportCrownFileAsync();
        }
        else if (action == "Crear sesión desde vídeos")
        {
            await OpenImportPageForVideosAsync();
        }
    }

    private static async Task OpenImportPageForVideosAsync()
    {
        await Shell.Current.GoToAsync(nameof(ImportPage));
    }

    private async Task ImportCrownFileAsync()
    {
        if (IsImporting) return; // Evitar múltiples importaciones simultáneas
        
        try
        {
            IsImporting = true;
            ImportProgressValue = 0;
            ImportProgressText = "Abriendo selector de archivos...";

            // Permite que la UI pinte el estado antes de abrir el picker.
            await Task.Yield();

            System.Diagnostics.Debug.WriteLine("Iniciando selección de archivo...");

            // El picker nativo MacCrownFilePicker usa runModal que es síncrono
            // y responde inmediatamente cuando el usuario selecciona o cancela.
            // No es necesario timeout ya que el picker siempre responde.
            var filePath = await _crownFileService.PickCrownFilePathAsync();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                System.Diagnostics.Debug.WriteLine("No se seleccionó ningún archivo (o no se pudo acceder a él)");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Archivo seleccionado: {filePath}");

            ImportProgressText = "Iniciando importación...";

            var progress = new Progress<ImportProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ImportProgressText = p.Message;
                    ImportProgressValue = p.Percentage;
                });
            });

            var result = await _crownFileService.ImportCrownFileAsync(filePath, progress);

            if (result.Success)
            {
                await Shell.Current.DisplayAlert(
                    "Importación Exitosa",
                    $"Sesión '{result.SessionName}' importada correctamente.\n" +
                    $"Videos: {result.VideosImported}\n" +
                    $"Atletas: {result.AthletesImported}",
                    "OK");

                await LoadDataAsync();
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", result.ErrorMessage ?? "Error desconocido", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error en importación: {ex}");
            await Shell.Current.DisplayAlert("Error", $"Error al importar: {ex.Message}", "OK");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsImporting = false;
                ImportProgressText = "";
                ImportProgressValue = 0;
            });
        }
    }

    private async Task ViewSessionAsync(Session? session)
    {
        if (session == null) return;
        await Shell.Current.GoToAsync($"{nameof(SessionDetailPage)}?sessionId={session.Id}");
    }

    private async Task ViewAllSessionsAsync()
    {
        await Shell.Current.GoToAsync(nameof(SessionsPage));
    }

    private static async Task ViewTrashAsync()
    {
        await Shell.Current.GoToAsync(nameof(TrashPage));
    }

    private async Task ViewAthletesAsync()
    {
        await Shell.Current.GoToAsync(nameof(AthletesPage));
    }

    private async Task CreateNewSessionAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewSessionName))
            {
                await Shell.Current.DisplayAlert("Error", "Por favor, introduce un nombre para la sesión.", "OK");
                return;
            }

            var coachName = await GetCurrentCoachNameAsync();

            // Crear la nueva sesión con la fecha seleccionada en el calendario
            var newSession = new Session
            {
                NombreSesion = NewSessionName,
                TipoSesion = NewSessionType,
                Lugar = NewSessionLugar,
                Coach = coachName,
                Fecha = new DateTimeOffset(Diary.SelectedDiaryDate.Date.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute)).ToUnixTimeSeconds(),
                IsMerged = 0
            };

            await _databaseService.SaveSessionAsync(newSession);

            // Cerrar el formulario
            IsAddingNewSession = false;

            // Recargar los datos del mes
            var profile = await _databaseService.GetUserProfileAsync();
            if (profile?.ReferenceAthleteId != null)
            {
                await Diary.RefreshDiaryForSelectedDateAsync();
            }

            await Shell.Current.DisplayAlert("Sesión creada", $"La sesión '{NewSessionName}' se ha creado correctamente. Ahora puedes añadir tus valoraciones y notas.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creando sesión: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo crear la sesión: {ex.Message}", "OK");
        }
    }

    private async Task OpenCameraRecordingAsync()
    {
        try
        {
            // Cerrar el formulario de nueva sesión si está abierto
            IsAddingNewSession = false;

            // Navegar a la página de cámara con los parámetros de la sesión
            var parameters = new Dictionary<string, object>
            {
                { "SessionName", NewSessionName ?? $"Sesión {Diary.SelectedDiaryDate:dd/MM/yyyy}" },
                { "SessionType", NewSessionType ?? "Entrenamiento" },
                { "Place", NewSessionLugar ?? "" },
                { "Date", Diary.SelectedDiaryDate }
            };

            await Shell.Current.GoToAsync(nameof(Views.CameraPage), parameters);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error abriendo cámara: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo abrir la cámara: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Crea una nueva sesión desde el popup de la barra lateral y abre la cámara para grabar
    /// </summary>
    private async Task CreateSessionAndRecordAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewSessionLugar))
            {
                await Shell.Current.DisplayAlert("Error", "Por favor, introduce el lugar de la sesión.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(NewSessionName))
            {
                await Shell.Current.DisplayAlert("Error", "Por favor, introduce el nombre de la sesión.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(NewSessionType))
            {
                await Shell.Current.DisplayAlert("Error", "Por favor, selecciona el tipo de sesión.", "OK");
                return;
            }

            // Crear la sesión sin requerir deportista
            var sessionName = NewSessionName.Trim();

            // Intentar obtener deportista de referencia o el primero disponible (opcional)
            string participantes = "";
            try
            {
                var profile = await _databaseService.GetUserProfileAsync();
                int? athleteId = profile?.ReferenceAthleteId;
                
                if (athleteId == null)
                {
                    var athletes = await _databaseService.GetAllAthletesAsync();
                    if (athletes != null && athletes.Count > 0)
                    {
                        athleteId = athletes[0].Id;
                    }
                }
                
                if (athleteId.HasValue)
                {
                    participantes = athleteId.Value.ToString();
                }
            }
            catch
            {
                // Ignorar errores al obtener deportista - la sesión se creará sin él
            }

            var coachName = await GetCurrentCoachNameAsync();

            var session = new Session
            {
                NombreSesion = sessionName,
                TipoSesion = NewSessionType.Trim(),
                Lugar = NewSessionLugar.Trim(),
                Coach = coachName,
                Fecha = DateTimeOffset.Now.ToUnixTimeSeconds(),
                Participantes = participantes
            };

            await _databaseService.SaveSessionAsync(session);

            await EnsurePlaceExistsAsync(NewSessionLugar.Trim());
            
            // Cerrar el popup
            ShowNewSessionSidebarPopup = false;

            // Recargar las sesiones para mostrar la nueva (en background)
            _ = LoadDataAsync();

            // Navegar a la cámara con el SessionId
            var parameters = new Dictionary<string, object>
            {
                { "SessionId", session.Id },
                { "SessionName", session.NombreSesion ?? session.DisplayName },
                { "SessionType", session.TipoSesion ?? "Entrenamiento" },
                { "Place", session.Lugar ?? "" },
                { "Date", session.FechaDateTime }
            };

            await Shell.Current.GoToAsync(nameof(Views.CameraPage), parameters);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creando sesión y grabando: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo crear la sesión: {ex.Message}", "OK");
        }
    }

    private async Task<string?> GetCurrentCoachNameAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_cloudBackendService.CurrentUserName))
            {
                return _cloudBackendService.CurrentUserName;
            }

            var profile = await _databaseService.GetUserProfileAsync();
            if (profile == null) return null;

            var name = profile.NombreCompleto;
            return string.IsNullOrWhiteSpace(name) ? profile.Nombre : name;
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsurePlaceExistsAsync(string placeName)
    {
        if (string.IsNullOrWhiteSpace(placeName)) return;

        var places = await _databaseService.GetAllPlacesAsync();
        if (places.Any(p => string.Equals(p.NombreLugar?.Trim(), placeName, StringComparison.OrdinalIgnoreCase))) return;

        await _databaseService.SavePlaceAsync(new Place { NombreLugar = placeName });
        await LoadPlaceOptionsAsync();
    }

    /// <summary>
    /// Abre la cámara para grabar videos en la sesión actualmente seleccionada
    /// </summary>
    private async Task RecordForSelectedSessionAsync()
    {
        try
        {
            if (SelectedSession == null)
            {
                await Shell.Current.DisplayAlert("Error", "No hay ninguna sesión seleccionada", "OK");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "SessionId", SelectedSession.Id },
                { "SessionName", SelectedSession.NombreSesion ?? SelectedSession.DisplayName },
                { "SessionType", SelectedSession.TipoSesion ?? "Entrenamiento" },
                { "Place", SelectedSession.Lugar ?? "" },
                { "Date", SelectedSession.FechaDateTime }
            };

            await Shell.Current.GoToAsync(nameof(Views.CameraPage), parameters);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error abriendo cámara para sesión: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo abrir la cámara: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Exporta la sesión seleccionada a un archivo .crown
    /// </summary>
    private async Task ExportSelectedSessionAsync()
    {
        try
        {
            if (SelectedSession == null)
            {
                await Shell.Current.DisplayAlert("Error", "No hay ninguna sesión seleccionada", "OK");
                return;
            }

            // Mostrar indicador de progreso
            var progress = new Progress<ImportProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ImportProgressText = p.Message;
                    ImportProgressValue = p.Percentage;
                });
            });

            IsImporting = true;
            ImportProgressText = "Preparando exportación...";
            ImportProgressValue = 0;

            var crownFileService = new CrownFileService(_databaseService);
            var result = await crownFileService.ExportSessionAsync(SelectedSession.Id, progress);

            IsImporting = false;

            if (result.Success && !string.IsNullOrEmpty(result.FilePath))
            {
#if IOS
                // Compartir el archivo usando Share API
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = $"Exportar {result.SessionName}",
                    File = new ShareFile(result.FilePath)
                });
#else
                await Shell.Current.DisplayAlert("Exportación completada", 
                    $"Sesión '{result.SessionName}' exportada correctamente.\n{result.VideosExported} videos incluidos.\n\nArchivo: {result.FilePath}", "OK");
#endif
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", result.ErrorMessage ?? "Error desconocido al exportar", "OK");
            }
        }
        catch (Exception ex)
        {
            IsImporting = false;
            System.Diagnostics.Debug.WriteLine($"Error exportando sesión: {ex}");
            await Shell.Current.DisplayAlert("Error", $"No se pudo exportar la sesión: {ex.Message}", "OK");
        }
    }

    private void LoadNasConnectionFromPreferences()
    {
        // Verificar si hay una sesión activa en el backend
        _ = Remote.RestoreCloudSessionAsync();
    }
    #region Session Context Menu

    private const string SessionCustomizationsPreferencesKey = "SessionCustomizations";

    private Dictionary<int, (string Icon, string Color)> _sessionCustomizations = new();

    private void LoadSessionCustomizationsFromPreferences()
    {
        try
        {
            var json = Preferences.Get(SessionCustomizationsPreferencesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var parsed = JsonSerializer.Deserialize<Dictionary<int, SessionCustomization>>(json);
            if (parsed == null)
                return;

            _sessionCustomizations.Clear();
            foreach (var kvp in parsed)
                _sessionCustomizations[kvp.Key] = (kvp.Value.Icon, kvp.Value.Color);
        }
        catch
        {
            // Si falla la deserialización, ignorar.
        }
    }

    private void SaveSessionCustomizationsToPreferences()
    {
        try
        {
            var toSerialize = _sessionCustomizations.ToDictionary(
                kvp => kvp.Key,
                kvp => new SessionCustomization { Icon = kvp.Value.Icon, Color = kvp.Value.Color });
            var json = JsonSerializer.Serialize(toSerialize);
            Preferences.Set(SessionCustomizationsPreferencesKey, json);
        }
        catch
        {
            // Ignorar errores de persistencia.
        }
    }

    private void ApplySessionCustomization(Session session)
    {
        if (_sessionCustomizations.TryGetValue(session.Id, out var customization))
        {
            session.Icon = customization.Icon;
            session.IconColor = customization.Color;
        }
    }

    private void OpenSessionEditPopup(SessionRow? row)
    {
        if (row?.Session == null) return;

        _editingSessionRow = row;
        var session = row.Session;

        // Cargar valores actuales
        SessionEditName = session.NombreSesion ?? session.DisplayName;
        SessionEditLugar = session.Lugar ?? string.Empty;
        SessionEditTipoSesion = session.TipoSesion ?? string.Empty;

        ShowSessionEditPopup = true;
    }

    private async Task ApplySessionEditAsync()
    {
        if (_editingSessionRow?.Session == null)
        {
            ShowSessionEditPopup = false;
            return;
        }

        var session = _editingSessionRow.Session;
        var hasChanges = false;

        // Aplicar cambios
        var newName = NormalizeSpaces(SessionEditName);
        if (!string.IsNullOrEmpty(newName) && newName != session.NombreSesion)
        {
            session.NombreSesion = newName;
            hasChanges = true;
        }

        var newLugar = NormalizeSpaces(SessionEditLugar);
        if (newLugar != session.Lugar)
        {
            session.Lugar = newLugar;
            hasChanges = true;
        }

        var newTipo = NormalizeSpaces(SessionEditTipoSesion);
        if (newTipo != session.TipoSesion)
        {
            session.TipoSesion = newTipo;
            hasChanges = true;
        }

        if (hasChanges)
        {
            await _databaseService.SaveSessionAsync(session);
            SyncVisibleSessionRows();
        }

        _editingSessionRow = null;
        ShowSessionEditPopup = false;
    }

    private async Task RenameSessionAsync(SessionRow? row)
    {
        if (row?.Session == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var session = row.Session;
        var currentName = session.NombreSesion ?? session.DisplayName;

        var newName = await page.DisplayPromptAsync(
            "Renombrar sesión",
            "Introduce el nuevo nombre:",
            "Aceptar",
            "Cancelar",
            currentName,
            100,
            Keyboard.Text,
            currentName);

        if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
        {
            session.NombreSesion = newName;
            await _databaseService.SaveSessionAsync(session);
            SyncVisibleSessionRows();
        }
    }

    private async Task DeleteSessionAsync(SessionRow? row)
    {
        if (row?.Session == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var session = row.Session;
        var confirm = await page.DisplayAlert(
            "Eliminar sesión",
            $"¿Seguro que quieres mover a la papelera la sesión \"{session.DisplayName}\"?\n\nPodrás restaurarla durante 30 días.",
            "Eliminar",
            "Cancelar");

        if (confirm)
        {
            await _trashService.MoveSessionToTrashAsync(session.Id);
            RecentSessions.Remove(session);
            _sessionCustomizations.Remove(session.Id);
            SaveSessionCustomizationsToPreferences();
            SyncVisibleSessionRows();

            if (SelectedSession?.Id == session.Id)
            {
                SelectedSession = null;
            }
        }
    }

    private void SetSessionIcon(object? parameter)
    {
        if (parameter is ValueTuple<SessionRow, string> tuple)
        {
            var (row, icon) = tuple;
            if (row.Session != null)
            {
                row.Session.Icon = icon;
                _sessionCustomizations[row.Session.Id] = (icon, row.Session.IconColor);
                SaveSessionCustomizationsToPreferences();
            }
        }
    }

    private void SetSessionColor(object? parameter)
    {
        if (parameter is ValueTuple<SessionRow, string> tuple)
        {
            var (row, color) = tuple;
            if (row.Session != null)
            {
                row.Session.IconColor = color;
                _sessionCustomizations[row.Session.Id] = (row.Session.Icon, color);
                SaveSessionCustomizationsToPreferences();
            }
        }
    }

    private record SessionCustomization
    {
        public string Icon { get; init; } = "oar.2.crossed";
        public string Color { get; init; } = "#FF6DDDFF";
    }

    #endregion

    #region Icon/Color Picker Popup

    private void OpenIconColorPickerForSmartFolder(SmartFolderDefinition? folder)
    {
        if (folder == null) return;
        IconColorPickerTargetSession = null;
        IconColorPickerTargetSmartFolder = folder;
        OnPropertyChanged(nameof(IsPickerForSmartFolder));
        OnPropertyChanged(nameof(IsPickerForSession));
        OnPropertyChanged(nameof(IconColorPickerTitle));
        UpdateIconPickerSelection();
        Layout.ShowIconColorPickerPopup = true;
    }

    private void OpenIconColorPickerForSession(SessionRow? row)
    {
        if (row?.Session == null) return;
        IconColorPickerTargetSmartFolder = null;
        IconColorPickerTargetSession = row;
        OnPropertyChanged(nameof(IsPickerForSmartFolder));
        OnPropertyChanged(nameof(IsPickerForSession));
        OnPropertyChanged(nameof(IconColorPickerTitle));
        UpdateIconPickerSelection();
        Layout.ShowIconColorPickerPopup = true;
    }

    private void CloseIconColorPicker()
    {
        Layout.ShowIconColorPickerPopup = false;
        IconColorPickerTargetSmartFolder = null;
        IconColorPickerTargetSession = null;
    }

    private void UpdateIconPickerSelection()
    {
        var selectedIcon = IconColorPickerTargetSmartFolder?.Icon
            ?? IconColorPickerTargetSession?.Session?.Icon
            ?? "oar.2.crossed";
        var selectedColor = IconColorPickerTargetSmartFolder?.IconColor
            ?? IconColorPickerTargetSession?.Session?.IconColor
            ?? "#FF6DDDFF";

        SelectedPickerIcon = selectedIcon;
        SelectedPickerColor = selectedColor;
        foreach (var item in AvailableIcons)
        {
            item.IsSelected = string.Equals(item.Name, selectedIcon, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SelectPickerIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return;

        if (IconColorPickerTargetSmartFolder is { } folder)
        {
            folder.Icon = icon;
            SmartFolders.SaveSmartFoldersToPreferences();
        }
        else if (IconColorPickerTargetSession?.Session is { } session)
        {
            session.Icon = icon;
            _sessionCustomizations[session.Id] = (icon, session.IconColor);
            SaveSessionCustomizationsToPreferences();
        }
        UpdateIconPickerSelection();
    }

    private void SelectPickerColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;

        if (IconColorPickerTargetSmartFolder is { } folder)
        {
            folder.IconColor = color;
            SmartFolders.SaveSmartFoldersToPreferences();
        }
        else if (IconColorPickerTargetSession?.Session is { } session)
        {
            session.IconColor = color;
            _sessionCustomizations[session.Id] = (session.Icon, color);
            SaveSessionCustomizationsToPreferences();
        }
        SelectedPickerColor = color;
    }

    #endregion
}
