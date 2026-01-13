using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Views.Controls;

public partial class AppFooter : ContentView
{
    private StatusBarService? _statusBarService;
    private UserProfileNotifier? _userProfileNotifier;
    private DatabaseService? _databaseService;
    private Grid? _popupOverlay;
    private CollectionView? _popupLogsCollectionView;
    private Label? _popupLogCountLabel;
    private Label? _popupLastActivityLabel;

    // ==================== BINDABLE PROPERTIES PARA SINCRONIZACIÓN ====================
    
    /// <summary>
    /// Indica si se muestra la información de sincronización de videos
    /// </summary>
    public static readonly BindableProperty ShowSyncInfoProperty = BindableProperty.Create(
        nameof(ShowSyncInfo),
        typeof(bool),
        typeof(AppFooter),
        false,
        propertyChanged: OnShowSyncInfoChanged);

    public bool ShowSyncInfo
    {
        get => (bool)GetValue(ShowSyncInfoProperty);
        set => SetValue(ShowSyncInfoProperty, value);
    }

    /// <summary>
    /// Texto de estado de sincronización
    /// </summary>
    public static readonly BindableProperty SyncStatusTextProperty = BindableProperty.Create(
        nameof(SyncStatusText),
        typeof(string),
        typeof(AppFooter),
        string.Empty,
        propertyChanged: OnSyncTextChanged);

    public string SyncStatusText
    {
        get => (string)GetValue(SyncStatusTextProperty);
        set => SetValue(SyncStatusTextProperty, value);
    }

    /// <summary>
    /// Texto de delta de sincronización
    /// </summary>
    public static readonly BindableProperty SyncDeltaTextProperty = BindableProperty.Create(
        nameof(SyncDeltaText),
        typeof(string),
        typeof(AppFooter),
        string.Empty,
        propertyChanged: OnSyncTextChanged);

    public string SyncDeltaText
    {
        get => (string)GetValue(SyncDeltaTextProperty);
        set => SetValue(SyncDeltaTextProperty, value);
    }

    private static void OnShowSyncInfoChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AppFooter footer)
        {
            footer.UpdateSyncInfoVisibility();
        }
    }

    private static void OnSyncTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AppFooter footer)
        {
            footer.UpdateSyncLabels();
        }
    }

    private void UpdateSyncInfoVisibility()
    {
        if (SyncInfoPanel != null)
        {
            SyncInfoPanel.IsVisible = ShowSyncInfo;
        }
    }

    private void UpdateSyncLabels()
    {
        if (SyncStatusLabel != null)
        {
            SyncStatusLabel.Text = SyncStatusText;
        }
        if (SyncDeltaLabel != null)
        {
            SyncDeltaLabel.Text = SyncDeltaText;
        }
    }

    // ==================== FIN BINDABLE PROPERTIES ====================

    public AppFooter()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        
        _statusBarService = services?.GetService<StatusBarService>();
        _userProfileNotifier = services?.GetService<UserProfileNotifier>();
        _databaseService = services?.GetService<DatabaseService>();

        if (_statusBarService != null)
        {
            _statusBarService.PropertyChanged += OnStatusBarPropertyChanged;
            _statusBarService.DatabaseLogAdded += OnDatabaseLogAdded;
            RefreshFromStatusBar();
        }

        if (_userProfileNotifier != null)
        {
            _userProfileNotifier.ProfileSaved += OnUserProfileSaved;
        }

        // Cargar datos iniciales
        _ = RefreshAllDataAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_statusBarService != null)
        {
            _statusBarService.PropertyChanged -= OnStatusBarPropertyChanged;
            _statusBarService.DatabaseLogAdded -= OnDatabaseLogAdded;
        }
        
        if (_userProfileNotifier != null)
            _userProfileNotifier.ProfileSaved -= OnUserProfileSaved;

        // Limpiar popup si existe
        RemovePopupFromPage();

        _statusBarService = null;
        _userProfileNotifier = null;
        _databaseService = null;
    }

    private async void OnUserProfileSaved(object? sender, EventArgs e)
    {
        await RefreshUserProfileAsync();
    }

    private void OnStatusBarPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshFromStatusBar);
    }

    private void OnDatabaseLogAdded(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshPopupLogCount);
    }

    private void RefreshPopupLogCount()
    {
        if (_statusBarService == null) return;
        if (_popupLogCountLabel != null)
            _popupLogCountLabel.Text = $"{_statusBarService.DatabaseLogs.Count} entradas";
        if (_popupLastActivityLabel != null)
            _popupLastActivityLabel.Text = _statusBarService.LastDatabaseActivityText;
    }

    private void RefreshFromStatusBar()
    {
        if (_statusBarService == null) return;

        // Estado de la BD
        if (_statusBarService.IsDatabaseOk)
        {
            DatabaseStatusIcon.SymbolName = "checkmark.circle.fill";
            DatabaseStatusIcon.TintColor = Color.FromArgb("#FF4CAF50");
        }
        else
        {
            DatabaseStatusIcon.SymbolName = "exclamationmark.triangle.fill";
            DatabaseStatusIcon.TintColor = Color.FromArgb("#FFFF9800");
        }

        // Operación en curso
        var hasOperation = _statusBarService.HasCurrentOperation;
        OperationPanel.IsVisible = hasOperation;
        StatusLabel.IsVisible = !hasOperation;

        if (hasOperation)
        {
            OperationLabel.Text = _statusBarService.CurrentOperation;
            OperationIndicator.IsRunning = _statusBarService.IsOperationInProgress;
            
            if (_statusBarService.OperationProgress > 0)
                ProgressLabel.Text = $"({_statusBarService.OperationProgressPercent}%)";
            else
                ProgressLabel.Text = string.Empty;

            OperationProgressBar.Progress = _statusBarService.OperationProgress;
            OperationProgressBar.IsVisible = _statusBarService.OperationProgress > 0;
        }
        else
        {
            OperationProgressBar.IsVisible = false;
        }

        // Contadores
        VideoCountLabel.Text = _statusBarService.VideoCountText;
        SessionCountLabel.Text = _statusBarService.SessionCountText;

        // Usuario
        UserNameLabel.Text = _statusBarService.UserDisplayName;
        
        if (_statusBarService.HasUserPhoto)
        {
            UserPhotoImage.Source = ImageSource.FromFile(_statusBarService.UserPhotoPath);
            UserPhotoContainer.IsVisible = true;
            UserIconPlaceholder.IsVisible = false;
        }
        else
        {
            UserPhotoContainer.IsVisible = false;
            UserIconPlaceholder.IsVisible = true;
        }
    }

    private void OnDatabaseStatusTapped(object? sender, TappedEventArgs e)
    {
        ShowLogsPopup();
    }

    private void ShowLogsPopup()
    {
        // Buscar la página actual para añadir el overlay
        var page = GetParentContentPage();
        if (page?.Content is not Grid pageGrid)
        {
            return;
        }

        // Crear el overlay dinámicamente
        AddPopupToGrid(pageGrid);
    }

    private void AddPopupToGrid(Grid grid)
    {
        CreatePopupOverlay();
        if (_popupOverlay == null) return;

        // Configurar para cubrir todo el grid
        Grid.SetRowSpan(_popupOverlay, Math.Max(1, grid.RowDefinitions.Count));
        Grid.SetColumnSpan(_popupOverlay, Math.Max(1, grid.ColumnDefinitions.Count));
        
        grid.Children.Add(_popupOverlay);
        RefreshPopupLogCount();
    }

    private void CreatePopupOverlay()
    {
        if (_popupOverlay != null) return;

        // Crear CollectionView para logs
        _popupLogsCollectionView = new CollectionView
        {
            Margin = new Thickness(8),
            BackgroundColor = Colors.Transparent,
            ItemsSource = _statusBarService?.DatabaseLogs
        };

        _popupLogsCollectionView.ItemTemplate = new DataTemplate(() =>
        {
            var grid = new Grid
            {
                Padding = new Thickness(8, 4),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 8
            };

            var timestampLabel = new Label { FontSize = GetFontSize("FontSizeSmall"), TextColor = Color.FromArgb("#FF6A6A6A"), VerticalOptions = LayoutOptions.Center };
            timestampLabel.SetBinding(Label.TextProperty, "TimestampText");

            var levelLabel = new Label { FontSize = GetFontSize("FontSizeSmall"), VerticalOptions = LayoutOptions.Center };
            levelLabel.SetBinding(Label.TextProperty, "LevelText");
            Grid.SetColumn(levelLabel, 1);

            var messageLabel = new Label { FontSize = GetFontSize("FontSizeBody"), TextColor = Color.FromArgb("#FFB0B0B0"), LineBreakMode = LineBreakMode.TailTruncation, VerticalOptions = LayoutOptions.Center };
            messageLabel.SetBinding(Label.TextProperty, "Message");
            Grid.SetColumn(messageLabel, 2);

            grid.Children.Add(timestampLabel);
            grid.Children.Add(levelLabel);
            grid.Children.Add(messageLabel);

            return grid;
        });

        _popupLogsCollectionView.EmptyView = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 8,
            Children =
            {
                new SymbolIcon { SymbolName = "doc.text", TintColor = Color.FromArgb("#FF4A4A4A"), HeightRequest = GetIconSize("IconSizeXLarge"), WidthRequest = GetIconSize("IconSizeXLarge"), HorizontalOptions = LayoutOptions.Center },
                new Label { FontSize = GetFontSize("FontSizeBody"), HorizontalOptions = LayoutOptions.Center, Text = "Sin logs recientes", TextColor = Color.FromArgb("#FF6A6A6A") }
            }
        };

        // Header
        _popupLastActivityLabel = new Label
        {
            FontSize = GetFontSize("FontSizeSmall"),
            HorizontalOptions = LayoutOptions.Center,
            Text = "Sin actividad",
            TextColor = Color.FromArgb("#FF6A6A6A"),
            VerticalOptions = LayoutOptions.Center
        };

        var closeButton = new Border
        {
            Padding = new Thickness(8, 4),
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Content = new SymbolIcon { SymbolName = "xmark", TintColor = Color.FromArgb("#FF8A8A8A"), HeightRequest = GetIconSize("IconSizeSmall"), WidthRequest = GetIconSize("IconSizeSmall"), VerticalOptions = LayoutOptions.Center }
        };
        closeButton.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(CloseLogsPopup) });

        var headerGrid = new Grid
        {
            Padding = new Thickness(12, 10),
            BackgroundColor = Color.FromArgb("#FF252525"),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        var headerStack = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new SymbolIcon { SymbolName = "cylinder.split.1x2", TintColor = Color.FromArgb("#FF6DDDFF"), HeightRequest = GetIconSize("IconSizeMedium"), WidthRequest = GetIconSize("IconSizeMedium"), VerticalOptions = LayoutOptions.Center },
                new Label { FontSize = GetFontSize("FontSizeSubtitle"), Text = "Logs de Base de Datos", TextColor = Colors.White, VerticalOptions = LayoutOptions.Center }
            }
        };

        Grid.SetColumn(_popupLastActivityLabel, 1);
        Grid.SetColumn(closeButton, 2);

        headerGrid.Children.Add(headerStack);
        headerGrid.Children.Add(_popupLastActivityLabel);
        headerGrid.Children.Add(closeButton);

        // Footer
        _popupLogCountLabel = new Label
        {
            FontSize = GetFontSize("FontSizeSmall"),
            Text = "0 entradas",
            TextColor = Color.FromArgb("#FF6A6A6A"),
            VerticalOptions = LayoutOptions.Center
        };

        var clearButton = new Button
        {
            Padding = new Thickness(12, 4),
            BackgroundColor = Color.FromArgb("#FF2A2A2A"),
            CornerRadius = 4,
            FontSize = GetFontSize("FontSizeBody"),
            Text = "Limpiar",
            TextColor = Color.FromArgb("#FF8A8A8A")
        };
        clearButton.Clicked += OnPopupClearLogsTapped;

        var footerGrid = new Grid
        {
            Padding = new Thickness(12, 8),
            BackgroundColor = Color.FromArgb("#FF151515"),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        Grid.SetColumn(clearButton, 1);
        footerGrid.Children.Add(_popupLogCountLabel);
        footerGrid.Children.Add(clearButton);

        // Contenido del popup
        var contentGrid = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        Grid.SetRow(_popupLogsCollectionView, 1);
        Grid.SetRow(footerGrid, 2);
        contentGrid.Children.Add(headerGrid);
        contentGrid.Children.Add(_popupLogsCollectionView);
        contentGrid.Children.Add(footerGrid);

        var popupBorder = new Border
        {
            Margin = new Thickness(20, 20, 20, 56), // 56 para dejar espacio para el footer
            Padding = 0,
            BackgroundColor = Color.FromArgb("#FF1A1A1A"),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.End,
            Stroke = Color.FromArgb("#FF3A3A3A"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            WidthRequest = 450,
            HeightRequest = 320,
            Content = contentGrid
        };
        // Evitar que el clic en el popup lo cierre
        popupBorder.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { /* No hacer nada */ }) });

        // Overlay
        _popupOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#80000000"),
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
            ZIndex = 9999,
            InputTransparent = false
        };
        _popupOverlay.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(CloseLogsPopup) });
        _popupOverlay.Children.Add(popupBorder);
    }

    private void CloseLogsPopup()
    {
        RemovePopupFromPage();
    }

    private void RemovePopupFromPage()
    {
        if (_popupOverlay == null) return;

        var page = GetParentContentPage();
        if (page?.Content is Grid pageGrid && pageGrid.Children.Contains(_popupOverlay))
        {
            pageGrid.Children.Remove(_popupOverlay);
        }

        _popupOverlay = null;
        _popupLogsCollectionView = null;
        _popupLogCountLabel = null;
        _popupLastActivityLabel = null;
    }

    private ContentPage? GetParentContentPage()
    {
        Element? current = this;
        while (current != null)
        {
            if (current is ContentPage contentPage)
                return contentPage;
            current = current.Parent;
        }
        return null;
    }

    private void OnPopupClearLogsTapped(object? sender, EventArgs e)
    {
        _statusBarService?.ClearDatabaseLogs();
        RefreshPopupLogCount();
    }

    private async Task RefreshAllDataAsync()
    {
        await RefreshUserProfileAsync();
        await RefreshCountsAsync();
    }

    private async Task RefreshUserProfileAsync()
    {
        try
        {
            if (_databaseService == null || _statusBarService == null) return;

            var profile = await _databaseService.GetUserProfileAsync();
            
            var nombre = profile?.Nombre?.Trim();
            var apellidos = profile?.Apellidos?.Trim();
            var fullName = $"{nombre ?? string.Empty} {apellidos ?? string.Empty}".Trim();
            
            _statusBarService.UpdateUserInfo(fullName, profile?.FotoPath);
        }
        catch
        {
            // Ignorar errores al cargar perfil
        }
    }

    private async Task RefreshCountsAsync()
    {
        try
        {
            if (_databaseService == null || _statusBarService == null) return;

            var db = await _databaseService.GetConnectionAsync();
            var videoCount = await db.Table<Models.VideoClip>().CountAsync();
            var sessionCount = await db.Table<Models.Session>().CountAsync();
            
            _statusBarService.UpdateCounts(videoCount, sessionCount);
        }
        catch
        {
            // Ignorar errores al cargar contadores
        }
    }

    /// <summary>
    /// Método público para forzar la actualización de los contadores desde fuera
    /// </summary>
    public async Task RefreshCountsExternalAsync()
    {
        await RefreshCountsAsync();
    }

    /// <summary>
    /// Obtiene el tamaño de icono desde los recursos de la aplicación
    /// </summary>
    private static double GetIconSize(string resourceKey)
    {
        if (Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true && value is double size)
            return size;
        return 16; // Valor por defecto
    }

    /// <summary>
    /// Obtiene el tamaño de fuente desde los recursos de la aplicación
    /// </summary>
    private static double GetFontSize(string resourceKey)
    {
        if (Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true && value is double size)
            return size;
        return 14; // Valor por defecto
    }
}
