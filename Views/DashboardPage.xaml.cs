using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Services;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Reflection;

#if MACCATALYST
using CoreGraphics;
using UIKit;
#endif

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#endif

#if WINDOWS
using CrownRFEP_Reader.Platforms.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
#endif

namespace CrownRFEP_Reader.Views;

public partial class DashboardPage : ContentPage, IShellNavigatingCleanup
{
    private readonly DashboardViewModel _viewModel;
    private NotifyCollectionChangedEventHandler? _smartFoldersChangedHandler;

    private double _sidebarLastMeasuredWidth = -1;

    private SessionRow? _contextMenuSessionRow;
    private SmartFolderDefinition? _contextMenuSmartFolder;
    private VideoClip? _contextMenuVideo;
    
    // Detección manual de doble clic (MacCatalyst no maneja bien NumberOfTapsRequired=2)
    private DateTime _lastVideoTapTime = DateTime.MinValue;
    private VideoClip? _lastTappedVideo;
    private const int DoubleClickThresholdMs = 800;
    
    // Posiciones actuales de cada video para scrubbing incremental
    private double _currentPosition0;
    private double _currentPosition1;
    private double _currentPosition2;
    private double _currentPosition3;
    private double _currentPosition4;
    
    // Indica si esta página está activa
    private bool _isPageActive;

    private bool _isPageLoaded;

    private double _lastVideoGalleryMeasuredWidth = -1;
    private int _lastVideoGalleryComputedSpan = -1;

#if MACCATALYST
	private UITapGestureRecognizer? _globalDismissTapRecognizer;
    private UILongPressGestureRecognizer? _globalDismissPressRecognizer;
	private UIView? _globalDismissHostView;
#endif

#if WINDOWS
    private FrameworkElement? _windowsGlobalDismissHostElement;
    private PointerEventHandler? _windowsGlobalDismissPointerHandler;
#endif

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        AppLog.Info("DashboardPage", "CTOR");

        // Suscribirse a eventos de hover para video preview
        HoverVideoPreviewBehavior.VideoHoverStarted += OnVideoHoverStarted;
        HoverVideoPreviewBehavior.VideoHoverEnded += OnVideoHoverEnded;
        HoverVideoPreviewBehavior.VideoHoverMoved += OnVideoHoverMoved;
        
        // Suscribirse a eventos de scrubbing
        VideoScrubBehavior.ScrubUpdated += OnScrubUpdated;

        try
        {
            if (SubItemContextMenusLayer != null)
                SubItemContextMenusLayer.IsVisible = false;
        }
        catch { }
        VideoScrubBehavior.ScrubEnded += OnScrubEnded;

#if IOS
        // Suscribirse a eventos de long press para preview en iOS
        LongPressVideoPreviewBehavior.VideoLongPressStarted += OnVideoLongPressStarted;
        LongPressVideoPreviewBehavior.VideoLongPressEnded += OnVideoLongPressEnded;
#endif
        
#if MACCATALYST
#endif

		// Hover preview: usamos PrecisionVideoPlayer (AVPlayerLayer). Autoplay+loop aquí.
		HoverPreviewPlayer.MediaOpened += OnHoverPreviewOpened;
		HoverPreviewPlayer.MediaEnded += OnHoverPreviewEnded;

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;

        _smartFoldersChangedHandler = (_, __) =>
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    return;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        SidebarSessionsCollectionView?.InvalidateMeasure();
                    }
                    catch { }
                });
            }
            catch { }
        };

        try
        {
            _viewModel.SmartFolders.CollectionChanged += _smartFoldersChangedHandler;
        }
        catch { }

        try
        {
            if (!OperatingSystem.IsWindows())
                SidebarSessionsCollectionView?.InvalidateMeasure();
        }
        catch { }


        try
        {
            if (SidebarSessionsCollectionView != null)
            {
                SidebarSessionsCollectionView.SizeChanged += OnSidebarSessionsSizeChanged;
                // Forzar un primer ajuste (por si ya hay tamaño en este punto)
                OnSidebarSessionsSizeChanged(this, EventArgs.Empty);
            }
        }
        catch { }

        try
        {
            if (VideoGallery != null)
            {
                VideoGallery.SizeChanged += OnVideoGallerySizeChanged;
                OnVideoGallerySizeChanged(VideoGallery, EventArgs.Empty);
            }

            if (VideoLessonsGallery != null)
            {
                VideoLessonsGallery.SizeChanged += OnVideoGallerySizeChanged;
                OnVideoGallerySizeChanged(VideoLessonsGallery, EventArgs.Empty);
            }
        }
        catch { }

                // Preview players (drop zones): cuando abren media, ocultamos miniatura.
                PreviewPlayer1Q.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer2Q.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer3Q.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer4Q.MediaOpened += OnPreviewPlayerMediaOpened;

            // Marcar "ready" solo cuando hay avance real de tiempo (evita quedarse gris).
            PreviewPlayer1Q.PositionChanged += OnPreviewPlayerPositionChanged;
            PreviewPlayer2Q.PositionChanged += OnPreviewPlayerPositionChanged;
            PreviewPlayer3Q.PositionChanged += OnPreviewPlayerPositionChanged;
            PreviewPlayer4Q.PositionChanged += OnPreviewPlayerPositionChanged;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        // En el arranque, a veces OnAppearing ocurre antes de que existan los PlatformView.
        // Reintentar aquí aumenta la fiabilidad del cierre “tap/click fuera”.
        EnsureGlobalDismissHandlersAttached();
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _isPageLoaded = true;
        EnsureGlobalDismissHandlersAttached();
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _isPageLoaded = false;
    }

    private void EnsureGlobalDismissHandlersAttached(int retries = 10)
    {
        try
        {
            var allAttached = true;

#if MACCATALYST
            if (_globalDismissTapRecognizer == null && _globalDismissPressRecognizer == null)
                TryAttachGlobalDismissRecognizer();
            allAttached &= (_globalDismissTapRecognizer != null || _globalDismissPressRecognizer != null);
#endif

#if WINDOWS
            if (_windowsGlobalDismissHostElement == null)
                TryAttachWindowsGlobalDismissPointerHandler();
            allAttached &= (_windowsGlobalDismissHostElement != null);
#endif

            if (allAttached)
                return;

            if (retries <= 0)
                return;

            // Solo reintentar mientras la página esté “en vida” (visible o cargada).
            if (!_isPageActive && !_isPageLoaded)
                return;

            try
            {
                Dispatcher.DispatchDelayed(
                    TimeSpan.FromMilliseconds(150),
                    () => EnsureGlobalDismissHandlersAttached(retries - 1));
            }
            catch { }
        }
        catch { }
    }

    private void UpdateSubItemContextMenusLayerVisibility()
    {
        try
        {
            if (SubItemContextMenusLayer == null)
                return;

            var anyVisible = (SessionRowContextMenu?.IsVisible ?? false)
                || (SmartFolderContextMenu?.IsVisible ?? false)
                || (VideoItemContextMenu?.IsVisible ?? false);

            SubItemContextMenusLayer.IsVisible = anyVisible;
        }
        catch { }
    }

    ~DashboardPage()
    {
        HoverVideoPreviewBehavior.VideoHoverStarted -= OnVideoHoverStarted;
        HoverVideoPreviewBehavior.VideoHoverEnded -= OnVideoHoverEnded;
        HoverVideoPreviewBehavior.VideoHoverMoved -= OnVideoHoverMoved;
        VideoScrubBehavior.ScrubUpdated -= OnScrubUpdated;
        VideoScrubBehavior.ScrubEnded -= OnScrubEnded;
        
#if IOS
        LongPressVideoPreviewBehavior.VideoLongPressStarted -= OnVideoLongPressStarted;
        LongPressVideoPreviewBehavior.VideoLongPressEnded -= OnVideoLongPressEnded;
#endif
        
#if MACCATALYST
        KeyPressHandler.SpaceBarPressed -= OnSpaceBarPressed;
#endif

		try { HoverPreviewPlayer.MediaOpened -= OnHoverPreviewOpened; } catch { }
		try { HoverPreviewPlayer.MediaEnded -= OnHoverPreviewEnded; } catch { }

        try
        {
            if (_smartFoldersChangedHandler != null)
                _viewModel.SmartFolders.CollectionChanged -= _smartFoldersChangedHandler;
        }
        catch { }

        try { SidebarSessionsCollectionView.SizeChanged -= OnSidebarSessionsSizeChanged; } catch { }

        try { VideoGallery.SizeChanged -= OnVideoGallerySizeChanged; } catch { }
        try { VideoLessonsGallery.SizeChanged -= OnVideoGallerySizeChanged; } catch { }

        try { PreviewPlayer1Q.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer2Q.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer3Q.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer4Q.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }

        try { PreviewPlayer1Q.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
        try { PreviewPlayer2Q.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
        try { PreviewPlayer3Q.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
        try { PreviewPlayer4Q.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
    }

    private void OnSidebarSessionsSizeChanged(object? sender, EventArgs e)
    {
        try
        {
            if (SidebarSessionsCollectionView == null || SidebarFixedItemsHeader == null)
                return;

            var width = SidebarSessionsCollectionView.Width;
            if (width <= 0)
                return;

            if (Math.Abs(_sidebarLastMeasuredWidth - width) <= 0.5)
                return;

            _sidebarLastMeasuredWidth = width;

            // Ajustar el header para que se re-mida con el ancho actual del sidebar.
            if (Math.Abs(SidebarFixedItemsHeader.WidthRequest - width) > 0.5)
                SidebarFixedItemsHeader.WidthRequest = width;

            // En WinUI, invalidaciones de medida dentro de SizeChanged pueden entrar en bucle (freeze).
            if (OperatingSystem.IsWindows())
                return;

            // Re-medición para que los layouts recalculen truncado y columnas.
            SidebarFixedItemsHeader.InvalidateMeasure();
            SidebarSessionsCollectionView.InvalidateMeasure();
        }
        catch { }
    }

    private void OnQuickAnalysisPlayPauseTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (!_isPageActive)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { TogglePlayPause(); } catch { }
            });
        }
        catch { }
    }

    private void OnVideoGallerySizeChanged(object? sender, EventArgs e)
    {
        try
        {
            if (BindingContext is not DashboardViewModel vm)
                return;

            if (sender is not VisualElement element)
                return;

            var width = element.Width;
            if (width <= 0)
                return;

            // Evitar recalcular en bucle por micro-variaciones.
            if (Math.Abs(_lastVideoGalleryMeasuredWidth - width) <= 0.5)
                return;

            _lastVideoGalleryMeasuredWidth = width;

            var span = CalculateVideoGallerySpan(width);
            if (span == _lastVideoGalleryComputedSpan)
                return;

            _lastVideoGalleryComputedSpan = span;

            if (vm.VideoGalleryColumnSpan != span)
                vm.VideoGalleryColumnSpan = span;
        }
        catch { }
    }

    private static int CalculateVideoGallerySpan(double availableWidth)
    {
        // Objetivo: variar columnas según el ancho real disponible.
        // - Máximo 4, mínimo 1.
        // - Elegimos el mayor número de columnas que deje un ancho mínimo por item.
        const int maxSpan = 4;
        const double minItemWidth = 170; // ajustado para mantener 4 columnas en anchos habituales
        const double itemSpacing = 6;    // aproximación (SpacingXSmall es 4/6 según plataforma)

        for (var span = maxSpan; span >= 1; span--)
        {
            var totalSpacing = itemSpacing * (span - 1);
            var itemWidth = (availableWidth - totalSpacing) / span;
            if (itemWidth >= minItemWidth)
                return span;
        }

        return 1;
    }

    private void OnAllGalleryTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (BindingContext is DashboardViewModel vm)
            {
                vm.SelectAllGalleryCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnAllGalleryTapped error", ex);
        }
    }

    private void OnVideoLessonsTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (BindingContext is DashboardViewModel vm)
            {
                vm.ViewVideoLessonsCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnVideoLessonsTapped error", ex);
        }
    }

    private void OnDiaryTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (BindingContext is DashboardViewModel vm)
            {
                vm.ViewDiaryCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnDiaryTapped error", ex);
        }
    }

    private void OnTrashTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (BindingContext is DashboardViewModel vm)
            {
                vm.ViewTrashCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnTrashTapped error", ex);
        }
    }

    private void OnSmartFoldersToggleTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (BindingContext is DashboardViewModel vm)
            {
                vm.ToggleSmartFoldersExpansionCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSmartFoldersToggleTapped error", ex);
        }
    }

    private void OnAddSmartFolderTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (BindingContext is DashboardViewModel vm)
            {
                vm.OpenSmartFolderSidebarPopupCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnAddSmartFolderTapped error", ex);
        }
    }

    private void OnUserLibraryMenuTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (UserLibraryContextMenuOverlay == null || UserLibraryContextMenu == null || RootGrid == null || UserLibraryHeaderBorder == null)
                return;

            if (UserLibraryContextMenuOverlay.IsVisible)
            {
                HideUserLibraryContextMenu();
                return;
            }

            HideSessionsContextMenu();
            HideSessionRowContextMenu();
            HideSmartFolderContextMenu();

            // Posicionar el menú al lado del item "Mi biblioteca".
            var anchor = UserLibraryHeaderBorder;
            var anchorPos = GetPositionRelativeTo(anchor, RootGrid);

            UserLibraryContextMenu.TranslationX = anchorPos.X + anchor.Width + 8;
            UserLibraryContextMenu.TranslationY = anchorPos.Y;

            UserLibraryContextMenuOverlay.IsVisible = true;
            UpdateGlobalDismissOverlayVisibility();
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnUserLibraryMenuTapped error", ex);
        }
    }

    private void OnUserLibraryContextMenuDismissTapped(object? sender, TappedEventArgs e) => HideUserLibraryContextMenu();

    private void OnUserLibraryMenuCreateTapped(object? sender, TappedEventArgs e) => HideUserLibraryContextMenu();
    private void OnUserLibraryMenuImportTapped(object? sender, TappedEventArgs e) => HideUserLibraryContextMenu();
    private void OnUserLibraryMenuRefreshTapped(object? sender, TappedEventArgs e) => HideUserLibraryContextMenu();
    private void OnUserLibraryMenuMergeTapped(object? sender, TappedEventArgs e) => HideUserLibraryContextMenu();

    private void HideUserLibraryContextMenu()
    {
        try
        {
            if (UserLibraryContextMenuOverlay != null)
                UserLibraryContextMenuOverlay.IsVisible = false;
        }
        catch { }

        UpdateGlobalDismissOverlayVisibility();
    }

    private void OnSessionsMenuTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (SessionsContextMenuOverlay == null || SessionsContextMenu == null || RootGrid == null || SessionsHeaderBorder == null)
                return;

            if (SessionsContextMenuOverlay.IsVisible)
            {
                HideSessionsContextMenu();
                return;
            }

            HideUserLibraryContextMenu();
            HideSessionRowContextMenu();
            HideSmartFolderContextMenu();

            // Posicionar el menú al lado del item "Sesiones".
            var anchor = SessionsHeaderBorder;
            var anchorPos = GetPositionRelativeTo(anchor, RootGrid);

            SessionsContextMenu.TranslationX = anchorPos.X + anchor.Width + 8;
            SessionsContextMenu.TranslationY = anchorPos.Y;

            SessionsContextMenuOverlay.IsVisible = true;
            UpdateGlobalDismissOverlayVisibility();
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSessionsMenuTapped error", ex);
        }
    }

    private void OnSessionsContextMenuDismissTapped(object? sender, TappedEventArgs e) => HideSessionsContextMenu();

    private void OnSessionsMenuImportCrownTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSessionsContextMenu();

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.ImportCrownFileCommand?.CanExecute(null) ?? false)
                    vm.ImportCrownFileCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSessionsMenuImportCrownTapped error", ex);
        }
    }

    private void OnSessionsMenuNewFromVideosTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSessionsContextMenu();

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.CreateSessionFromVideosCommand?.CanExecute(null) ?? false)
                    vm.CreateSessionFromVideosCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSessionsMenuNewFromVideosTapped error", ex);
        }
    }

    private void HideSessionsContextMenu()
    {
        try
        {
            if (SessionsContextMenuOverlay != null)
                SessionsContextMenuOverlay.IsVisible = false;
        }
        catch { }

        UpdateGlobalDismissOverlayVisibility();
    }

    private void OnGlobalTapToDismissContextMenus(object? sender, TappedEventArgs e)
    {
        try
        {
            HideAllContextMenus();
        }
        catch { }
    }

    private void OnContextMenuSurfaceTapped(object? sender, TappedEventArgs e)
    {
        // Intencionalmente vacío: consume el tap para que no llegue al handler global.
    }

    private void OnSessionRowPrimaryTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideAllContextMenus();

            if (sender is VisualElement anchor && anchor.BindingContext is SessionRow row)
            {
                if (BindingContext is DashboardViewModel vm && vm.SelectSessionRowCommand?.CanExecute(row) == true)
                    vm.SelectSessionRowCommand.Execute(row);
            }
        }
        catch { }
    }

    private void OnSmartFolderPrimaryTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideAllContextMenus();

            if (sender is VisualElement anchor && anchor.BindingContext is SmartFolderDefinition folder)
            {
                if (BindingContext is DashboardViewModel vm && vm.SelectSmartFolderCommand?.CanExecute(folder) == true)
                    vm.SelectSmartFolderCommand.Execute(folder);
            }
        }
        catch { }
    }

    private static bool IsContextMenuButtonSender(object? sender)
        => sender is VisualElement ve && string.Equals(ve.ClassId, "ContextMenuButton", StringComparison.Ordinal);

    private static bool IsSecondaryTap(TappedEventArgs e)
    {
        try
        {
            var buttonsProp = e.GetType().GetProperty("Buttons", BindingFlags.Instance | BindingFlags.Public);
            if (buttonsProp == null)
                return true;

            var buttonsValue = buttonsProp.GetValue(e);
            if (buttonsValue == null)
                return true;

            // We avoid depending on a specific enum type across MAUI versions.
            var text = buttonsValue.ToString();
            if (!string.IsNullOrWhiteSpace(text) && text.Contains("Secondary", StringComparison.OrdinalIgnoreCase))
                return true;

            if (buttonsValue is int intMask)
                return (intMask & 2) != 0;
        }
        catch
        {
            // If we can't determine it, don't block the menu.
        }

        return true;
    }

    private void OnSessionRowContextMenuTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            // iOS: the context menu must only open from the explicit ellipsis button.
            if (DeviceInfo.Platform == DevicePlatform.iOS && !IsContextMenuButtonSender(sender))
                return;

            // Non-iOS: if we can detect buttons, only honor Secondary taps.
            if (DeviceInfo.Platform != DevicePlatform.iOS && !IsContextMenuButtonSender(sender) && !IsSecondaryTap(e))
                return;

            if (SessionRowContextMenu == null || SubItemContextMenusLayer == null)
                return;

            if (sender is not VisualElement anchor)
                return;

            if (anchor.BindingContext is not SessionRow row)
                return;

            if (SessionRowContextMenu.IsVisible && ReferenceEquals(_contextMenuSessionRow, row))
            {
                HideSessionRowContextMenu();
                return;
            }

            _contextMenuSessionRow = row;

            // En desktop y también en iOS: el botón derecho/menú debe seleccionar el subitem.
            if (BindingContext is DashboardViewModel vm && vm.SelectSessionRowCommand?.CanExecute(row) == true)
                vm.SelectSessionRowCommand.Execute(row);

            HideUserLibraryContextMenu();
            HideSessionsContextMenu();
            HideSmartFolderContextMenu();

            var clickPos = TryGetTappedPosition(e, SubItemContextMenusLayer);
            if (clickPos.HasValue)
            {
                PositionContextMenuAtPoint(SessionRowContextMenu, clickPos.Value, SubItemContextMenusLayer, xOffset: 10, yOffset: 10);
            }
            else
            {
                var anchorPos = GetPositionRelativeTo(anchor, SubItemContextMenusLayer);
                SessionRowContextMenu.TranslationX = anchorPos.X + anchor.Width + 8;
                SessionRowContextMenu.TranslationY = anchorPos.Y;
            }

            SessionRowContextMenu.IsVisible = true;
            UpdateSubItemContextMenusLayerVisibility();
            UpdateGlobalDismissOverlayVisibility();
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSessionRowContextMenuTapped error", ex);
        }
    }

    private void HideSessionRowContextMenu()
    {
        try
        {
            if (SessionRowContextMenu != null)
                SessionRowContextMenu.IsVisible = false;
        }
        catch { }

        UpdateSubItemContextMenusLayerVisibility();
        UpdateGlobalDismissOverlayVisibility();
    }

    private void OnSessionRowMenuEditTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSessionRowContextMenu();

            if (_contextMenuSessionRow == null)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.OpenSessionEditPopupCommand?.CanExecute(_contextMenuSessionRow) ?? false)
                    vm.OpenSessionEditPopupCommand.Execute(_contextMenuSessionRow);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSessionRowMenuEditTapped error", ex);
        }
    }

    private void OnSessionRowMenuCustomizeTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSessionRowContextMenu();

            if (_contextMenuSessionRow == null)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.OpenIconColorPickerForSessionCommand?.CanExecute(_contextMenuSessionRow) ?? false)
                    vm.OpenIconColorPickerForSessionCommand.Execute(_contextMenuSessionRow);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSessionRowMenuCustomizeTapped error", ex);
        }
    }

    private void OnSessionRowMenuDeleteTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSessionRowContextMenu();

            if (_contextMenuSessionRow == null)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.DeleteSessionCommand?.CanExecute(_contextMenuSessionRow) ?? false)
                    vm.DeleteSessionCommand.Execute(_contextMenuSessionRow);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSessionRowMenuDeleteTapped error", ex);
        }
    }

    private void OnSessionRowMenuExportTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSessionRowContextMenu();

            if (_contextMenuSessionRow == null)
                return;

            if (BindingContext is not DashboardViewModel vm)
                return;

            // Asegurar que ExportSelectedSessionCommand exporta la sesión del subitem pulsado.
            if (vm.SelectSessionRowCommand?.CanExecute(_contextMenuSessionRow) == true)
                vm.SelectSessionRowCommand.Execute(_contextMenuSessionRow);

            if (vm.ExportSelectedSessionCommand?.CanExecute(null) == true)
                vm.ExportSelectedSessionCommand.Execute(null);
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSessionRowMenuExportTapped error", ex);
        }
    }

    private void OnSmartFolderContextMenuTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            // iOS: the context menu must only open from the explicit ellipsis button.
            if (DeviceInfo.Platform == DevicePlatform.iOS && !IsContextMenuButtonSender(sender))
                return;

            // Non-iOS: if we can detect buttons, only honor Secondary taps.
            if (DeviceInfo.Platform != DevicePlatform.iOS && !IsContextMenuButtonSender(sender) && !IsSecondaryTap(e))
                return;

            if (SmartFolderContextMenu == null || SubItemContextMenusLayer == null)
                return;

            if (sender is not VisualElement anchor)
                return;

            if (anchor.BindingContext is not SmartFolderDefinition folder)
                return;

            if (SmartFolderContextMenu.IsVisible && ReferenceEquals(_contextMenuSmartFolder, folder))
            {
                HideSmartFolderContextMenu();
                return;
            }

            _contextMenuSmartFolder = folder;

            // El menú contextual debe seleccionar el item.
            if (BindingContext is DashboardViewModel vm && vm.SelectSmartFolderCommand?.CanExecute(folder) == true)
                vm.SelectSmartFolderCommand.Execute(folder);

            HideUserLibraryContextMenu();
            HideSessionsContextMenu();
            HideSessionRowContextMenu();

            var clickPos = TryGetTappedPosition(e, SubItemContextMenusLayer);
            if (clickPos.HasValue)
            {
                PositionContextMenuAtPoint(SmartFolderContextMenu, clickPos.Value, SubItemContextMenusLayer, xOffset: 10, yOffset: 10);
            }
            else
            {
                var anchorPos = GetPositionRelativeTo(anchor, SubItemContextMenusLayer);
                SmartFolderContextMenu.TranslationX = anchorPos.X + anchor.Width + 8;
                SmartFolderContextMenu.TranslationY = anchorPos.Y;
            }

            SmartFolderContextMenu.IsVisible = true;
            UpdateSubItemContextMenusLayerVisibility();
            UpdateGlobalDismissOverlayVisibility();
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSmartFolderContextMenuTapped error", ex);
        }
    }

    private static Point? TryGetTappedPosition(TappedEventArgs e, VisualElement relativeTo)
    {
        try
        {
            // .NET MAUI ha ido introduciendo helpers tipo GetPosition(...)
            // Dependiendo de la versión, puede devolver Point o Point?. Para evitar depender de firma exacta,
            // usamos reflexión.
            var methods = e.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
            foreach (var method in methods)
            {
                if (!string.Equals(method.Name, "GetPosition", StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1)
                    continue;

                if (!parameters[0].ParameterType.IsInstanceOfType(relativeTo))
                    continue;

                var result = method.Invoke(e, new object?[] { relativeTo });
                // Nota: si la API devuelve Point?, al boxearse llega como Point (HasValue) o null (sin valor).
                if (result is Point p)
                    return p;
            }
        }
        catch { }

        return null;
    }

    private static void PositionContextMenuAtPoint(VisualElement menu, Point point, VisualElement relativeTo, double xOffset, double yOffset)
    {
        var x = point.X + xOffset;
        var y = point.Y + yOffset;

        try
        {
            // Clamp a los límites visibles del RootGrid (si hay medidas ya calculadas)
            var maxX = relativeTo.Width;
            var maxY = relativeTo.Height;
            var menuWidth = menu.WidthRequest > 0 ? menu.WidthRequest : menu.Width;
            var menuHeight = menu.HeightRequest > 0 ? menu.HeightRequest : menu.Height;

            if (maxX > 0 && menuWidth > 0)
                x = Math.Max(0, Math.Min(x, maxX - menuWidth));

            if (maxY > 0 && menuHeight > 0)
                y = Math.Max(0, Math.Min(y, maxY - menuHeight));
        }
        catch { }

        menu.TranslationX = x;
        menu.TranslationY = y;
    }

    private void HideSmartFolderContextMenu()
    {
        try
        {
            if (SmartFolderContextMenu != null)
                SmartFolderContextMenu.IsVisible = false;
        }
        catch { }

        UpdateSubItemContextMenusLayerVisibility();
        UpdateGlobalDismissOverlayVisibility();
    }

    private void HideAllContextMenus()
    {
        HideUserLibraryContextMenu();
        HideSessionsContextMenu();
        HideSessionRowContextMenu();
        HideSmartFolderContextMenu();
        HideVideoItemContextMenu();

        UpdateSubItemContextMenusLayerVisibility();
        UpdateGlobalDismissOverlayVisibility();
    }

    private void UpdateGlobalDismissOverlayVisibility()
    {
        try
        {
            if (GlobalDismissOverlay == null)
                return;

            // En MacCatalyst y Windows usamos detectores globales nativos para cerrar menús,
            // y evitamos una capa que bloquee clicks secundarios.
            if (DeviceInfo.Platform == DevicePlatform.MacCatalyst || DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                GlobalDismissOverlay.IsVisible = false;
                return;
            }

            var anyVisible = (UserLibraryContextMenuOverlay?.IsVisible ?? false)
                || (SessionsContextMenuOverlay?.IsVisible ?? false)
                || (SessionRowContextMenu?.IsVisible ?? false)
                || (SmartFolderContextMenu?.IsVisible ?? false);

            GlobalDismissOverlay.IsVisible = anyVisible;
        }
        catch { }
    }

    private void OnSmartFolderMenuEditTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSmartFolderContextMenu();

            if (_contextMenuSmartFolder == null)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.RenameSmartFolderCommand?.CanExecute(_contextMenuSmartFolder) ?? false)
                    vm.RenameSmartFolderCommand.Execute(_contextMenuSmartFolder);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSmartFolderMenuEditTapped error", ex);
        }
    }

    private void OnSmartFolderMenuCustomizeTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSmartFolderContextMenu();

            if (_contextMenuSmartFolder == null)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.OpenIconColorPickerForSmartFolderCommand?.CanExecute(_contextMenuSmartFolder) ?? false)
                    vm.OpenIconColorPickerForSmartFolderCommand.Execute(_contextMenuSmartFolder);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSmartFolderMenuCustomizeTapped error", ex);
        }
    }

    private void OnSmartFolderMenuDeleteTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideSmartFolderContextMenu();

            if (_contextMenuSmartFolder == null)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.DeleteSmartFolderCommand?.CanExecute(_contextMenuSmartFolder) ?? false)
                    vm.DeleteSmartFolderCommand.Execute(_contextMenuSmartFolder);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnSmartFolderMenuDeleteTapped error", ex);
        }
    }

    #region Video Item Context Menu

    private void OnVideoItemContextMenuTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (VideoItemContextMenu == null || SubItemContextMenusLayer == null)
                return;

            if (sender is not VisualElement anchor)
                return;

            // Buscar el VideoClip en el BindingContext
            var video = FindVideoClipFromContext(anchor);
            if (video == null)
                return;

            if (VideoItemContextMenu.IsVisible && ReferenceEquals(_contextMenuVideo, video))
            {
                HideVideoItemContextMenu();
                return;
            }

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.IsMultiSelectMode)
                {
                    if (!video.IsSelected)
                        vm.ToggleVideoSelectionCommand.Execute(video);
                }
                else
                {
                    if (!video.IsSelected)
                        vm.SelectSingleVideo(video);
                }
            }

            _contextMenuVideo = video;

            HideUserLibraryContextMenu();
            HideSessionsContextMenu();
            HideSessionRowContextMenu();
            HideSmartFolderContextMenu();

            var clickPos = TryGetTappedPosition(e, SubItemContextMenusLayer);
            if (clickPos.HasValue)
            {
                PositionContextMenuAtPoint(VideoItemContextMenu, clickPos.Value, SubItemContextMenusLayer, xOffset: 10, yOffset: 10);
            }
            else
            {
                var anchorPos = GetPositionRelativeTo(anchor, SubItemContextMenusLayer);
                VideoItemContextMenu.TranslationX = anchorPos.X + anchor.Width + 8;
                VideoItemContextMenu.TranslationY = anchorPos.Y;
            }

            VideoItemContextMenu.IsVisible = true;
            UpdateSubItemContextMenusLayerVisibility();
            UpdateGlobalDismissOverlayVisibility();
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnVideoItemContextMenuTapped error", ex);
        }
    }

    /// <summary>
    /// Clic simple en video item: selecciona/deselecciona el video (solo en MacCatalyst/Windows).
    /// En iOS, este handler no se ejecuta (NumberOfTapsRequired=99).
    /// Detecta doble clic manualmente porque MacCatalyst no maneja bien múltiples TapGestureRecognizers.
    /// </summary>
    private void OnVideoItemSingleTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (sender is not VisualElement anchor)
                return;

            var video = FindVideoClipFromContext(anchor);
            if (video == null)
                return;

            var now = DateTime.Now;
            var timeSinceLastTap = (now - _lastVideoTapTime).TotalMilliseconds;
            var isSameVideo = _lastTappedVideo?.Id == video.Id;
            
            // Detectar doble clic: mismo video, dentro del umbral de tiempo
            if (isSameVideo && timeSinceLastTap < DoubleClickThresholdMs)
            {
                AppLog.Info("DashboardPage", $"⏱️ DOBLE CLIC detectado - video.Id={video.Id}, delta={timeSinceLastTap:F0}ms");
                
                // Reset para evitar triple-clic
                _lastVideoTapTime = DateTime.MinValue;
                _lastTappedVideo = null;
                
                // Ejecutar comando de apertura de video
                if (BindingContext is DashboardViewModel vm)
                {
                    vm.VideoTapCommand.Execute(video);
                }
                return;
            }
            
            // Registrar este tap para posible doble clic
            _lastVideoTapTime = now;
            _lastTappedVideo = video;
            
            AppLog.Info("DashboardPage", $"⏱️ Clic simple - video.Id={video.Id}");

            if (BindingContext is DashboardViewModel vm2)
            {
                if (vm2.IsMultiSelectMode)
                {
                    // En multiselección, alternar selección sin afectar a otros
                    vm2.ToggleVideoSelectionCommand.Execute(video);
                    return;
                }

                // Selección simple: alternar y mantener selección única
                if (video.IsSelected)
                {
                    video.IsSelected = false;
                    vm2.UpdateVideoSelectionState(video);
                }
                else
                {
                    vm2.SelectSingleVideo(video);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnVideoItemSingleTapped error", ex);
        }
    }

    private static VideoClip? FindVideoClipFromContext(VisualElement element)
    {
        Element? current = element;
        while (current != null)
        {
            if (current.BindingContext is VideoClip video)
                return video;
            current = current.Parent;
        }
        return null;
    }

    private void HideVideoItemContextMenu()
    {
        try
        {
            if (VideoItemContextMenu != null)
                VideoItemContextMenu.IsVisible = false;
        }
        catch { }

        UpdateSubItemContextMenusLayerVisibility();
        UpdateGlobalDismissOverlayVisibility();
    }

    private void OnVideoItemMenuOpenTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideVideoItemContextMenu();

            if (_contextMenuVideo == null)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                if (vm.VideoTapCommand?.CanExecute(_contextMenuVideo) ?? false)
                    vm.VideoTapCommand.Execute(_contextMenuVideo);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnVideoItemMenuOpenTapped error", ex);
        }
    }

    private void OnVideoItemMenuEditTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideVideoItemContextMenu();

            if (_contextMenuVideo == null)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                // Seleccionar solo este video para edición (sin requerir modo multiselección)
                vm.SelectSingleVideoForEdit(_contextMenuVideo);
                
                // Ahora ejecutar el comando de editar
                if (vm.EditVideoDetailsCommand?.CanExecute(null) ?? false)
                    vm.EditVideoDetailsCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnVideoItemMenuEditTapped error", ex);
        }
    }

    private async void OnVideoItemMenuShareTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideVideoItemContextMenu();

            if (_contextMenuVideo == null)
                return;

            var videoPath = _contextMenuVideo.LocalClipPath;
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                await DisplayAlert("Error", "El archivo de video no está disponible.", "OK");
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = _contextMenuVideo.DisplayLine1 ?? "Video",
                File = new ShareFile(videoPath)
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnVideoItemMenuShareTapped error", ex);
        }
    }

    private async void OnVideoItemMenuDeleteTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            HideVideoItemContextMenu();

            if (_contextMenuVideo == null)
                return;

            var confirm = await DisplayAlert(
                "Eliminar video",
                $"¿Seguro que quieres eliminar este video?\n\n{_contextMenuVideo.DisplayLine1}",
                "Eliminar",
                "Cancelar");

            if (!confirm)
                return;

            if (BindingContext is DashboardViewModel vm)
            {
                // Seleccionar solo este video para eliminación (sin requerir modo multiselección)
                vm.SelectSingleVideoForEdit(_contextMenuVideo);
                
                // Ahora ejecutar el comando de eliminar
                if (vm.DeleteSelectedVideosCommand?.CanExecute(null) ?? false)
                    vm.DeleteSelectedVideosCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnVideoItemMenuDeleteTapped error", ex);
        }
    }

    #endregion

    private static Point GetPositionRelativeTo(VisualElement element, VisualElement relativeTo)
    {
        var x = 0.0;
        var y = 0.0;

        VisualElement? current = element;
        while (current != null && current != relativeTo)
        {
            x += current.X;
            y += current.Y;
            current = current.Parent as VisualElement;
        }

        return new Point(x, y);
    }

    private void OnPreviewPlayerMediaOpened(object? sender, EventArgs e)
    {
        try
        {
            // Solo responder si la página está activa
            if (!_isPageActive) return;
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnPreviewPlayerMediaOpened error", ex);
        }
    }

    private void OnPreviewPlayerPositionChanged(object? sender, TimeSpan position)
    {
        try
        {
            if (!_isPageActive) return;

            // Considerar "listo" cuando vemos callbacks de posición (normalmente implica playback).
            if (sender == PreviewPlayer1Q)
            {
                _viewModel.IsPreviewPlayer1Ready = true;
            }
            else if (sender == PreviewPlayer2Q)
            {
                _viewModel.IsPreviewPlayer2Ready = true;
            }
            else if (sender == PreviewPlayer3Q)
            {
                _viewModel.IsPreviewPlayer3Ready = true;
            }
            else if (sender == PreviewPlayer4Q)
            {
                _viewModel.IsPreviewPlayer4Ready = true;
            }
        }
        catch { }
    }

#if MACCATALYST || WINDOWS
    private void OnSpaceBarPressed(object? sender, EventArgs e)
    {
        // Solo responder si la página está activa
        if (!_isPageActive) return;
        MainThread.BeginInvokeOnMainThread(TogglePlayPause);
    }
#endif

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;

        // Asegura que no queda ningún menú contextual visible al volver a la página.
        HideAllContextMenus();

#if MACCATALYST
        TryAttachGlobalDismissRecognizer();
#endif

#if WINDOWS
        TryAttachWindowsGlobalDismissPointerHandler();
#endif

        // Extra: en el arranque, puede que el Handler/PlatformView aún no esté listo.
        EnsureGlobalDismissHandlersAttached();

#if MACCATALYST || WINDOWS
#if WINDOWS
    KeyPressHandler.EnsureAttached();
#endif
    KeyPressHandler.SpaceBarPressed += OnSpaceBarPressed;
#endif

        AppLog.Info(
            "DashboardPage",
            $"OnAppearing | IsVideoLessonsSelected={_viewModel.IsVideoLessonsSelected} | NavStack={Shell.Current?.Navigation?.NavigationStack?.Count} | ModalStack={Shell.Current?.Navigation?.ModalStack?.Count}");

        _ = LogHeartbeatsAsync();
        
        // Limpiar los recuadros de preview al volver a la página
        _viewModel.ClearPreviewVideos();
        
        // Refrescar estadísticas si hay cambios pendientes (al volver de SinglePlayerPage)
        _ = _viewModel.RefreshPendingStatsAsync();
        
        _ = LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            await _viewModel.LoadDataAsync();
            AppLog.Info("DashboardPage", "OnAppearing finished LoadDataAsync");
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "LoadDataAsync threw", ex);
        }
    }

    private async Task LogHeartbeatsAsync()
    {
        try
        {
            await Task.Delay(250);
            AppLog.Info("DashboardPage", "Heartbeat +250ms");
            await Task.Delay(750);
            AppLog.Info("DashboardPage", "Heartbeat +1s");
            await Task.Delay(2000);
            AppLog.Info("DashboardPage", "Heartbeat +3s");
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "Heartbeat task error", ex);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isPageActive = false;

        // Cerrar cualquier menú contextual abierto al abandonar la página
        HideAllContextMenus();

#if MACCATALYST
		TryDetachGlobalDismissRecognizer();
#endif

#if WINDOWS
        TryDetachWindowsGlobalDismissPointerHandler();
#endif

#if MACCATALYST || WINDOWS
    KeyPressHandler.SpaceBarPressed -= OnSpaceBarPressed;
#endif

        AppLog.Info(
            "DashboardPage",
            $"OnDisappearing | NavStack={Shell.Current?.Navigation?.NavigationStack?.Count} | ModalStack={Shell.Current?.Navigation?.ModalStack?.Count}");
        
        // Pausar y limpiar todos los videos al salir para evitar conflictos de jerarquía de ViewControllers
        CleanupAllVideos();
    }

#if WINDOWS
    private void TryAttachWindowsGlobalDismissPointerHandler()
    {
        try
        {
            if (_windowsGlobalDismissHostElement != null)
                return;

            if (RootGrid?.Handler?.PlatformView is not FrameworkElement rootElement)
                return;

            _windowsGlobalDismissHostElement = rootElement;
            _windowsGlobalDismissPointerHandler = new PointerEventHandler(OnWindowsGlobalPointerPressed);

            // Escuchar incluso si algún control marca el evento como handled.
            rootElement.AddHandler(UIElement.PointerPressedEvent, _windowsGlobalDismissPointerHandler, handledEventsToo: true);
        }
        catch { }
    }

    private void TryDetachWindowsGlobalDismissPointerHandler()
    {
        try
        {
            if (_windowsGlobalDismissHostElement != null && _windowsGlobalDismissPointerHandler != null)
                _windowsGlobalDismissHostElement.RemoveHandler(UIElement.PointerPressedEvent, _windowsGlobalDismissPointerHandler);
        }
        catch { }

        _windowsGlobalDismissHostElement = null;
        _windowsGlobalDismissPointerHandler = null;
    }

    private void OnWindowsGlobalPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            var anyVisible = (UserLibraryContextMenuOverlay?.IsVisible ?? false)
                || (SessionsContextMenuOverlay?.IsVisible ?? false)
                || (SessionRowContextMenu?.IsVisible ?? false)
                || (SmartFolderContextMenu?.IsVisible ?? false);
            if (!anyVisible)
                return;

            if (RootGrid?.Handler?.PlatformView is not FrameworkElement rootElement)
                return;

            var pos = e.GetCurrentPoint(rootElement).Position;
            var point = new Microsoft.Maui.Graphics.Point(pos.X, pos.Y);

            // Si el click es dentro de un menú visible, NO lo cerramos aquí.
            if (IsPointInsideVisibleMenu(point, SessionRowContextMenu))
                return;
            if (IsPointInsideVisibleMenu(point, SmartFolderContextMenu))
                return;
            if (IsPointInsideVisibleMenu(point, UserLibraryContextMenu))
                return;
            if (IsPointInsideVisibleMenu(point, SessionsContextMenu))
                return;

            HideUserLibraryContextMenu();
            HideSessionsContextMenu();
            HideSessionRowContextMenu();
            HideSmartFolderContextMenu();
        }
        catch { }
    }

    private static bool IsPointInsideVisibleMenu(Microsoft.Maui.Graphics.Point point, VisualElement? menu)
    {
        if (menu == null || !menu.IsVisible)
            return false;

        // Los menús se posicionan por TranslationX/Y en coordenadas del root.
        var left = menu.TranslationX;
        var top = menu.TranslationY;

        var width = menu.Width > 0 ? menu.Width : menu.WidthRequest;
        var height = menu.Height > 0 ? menu.Height : menu.HeightRequest;

        if (width <= 0 || height <= 0)
            return false;

        return point.X >= left && point.X <= (left + width)
            && point.Y >= top && point.Y <= (top + height);
    }
#endif

#if MACCATALYST
    private void TryAttachGlobalDismissRecognizer()
    {
        try
        {
            if (_globalDismissTapRecognizer != null || _globalDismissPressRecognizer != null)
                return;

            UIView? hostView = null;
            if (Handler?.PlatformView is UIViewController vc)
                hostView = vc.View;
            else if (Handler?.PlatformView is UIView v)
                hostView = v;

            if (hostView == null)
                return;

            _globalDismissHostView = hostView;
            _globalDismissTapRecognizer = new UITapGestureRecognizer(OnGlobalNativeTapped)
            {
                CancelsTouchesInView = false,
                DelaysTouchesBegan = false,
                DelaysTouchesEnded = false,
                // Permitir reconocer simultáneamente con otros gestos (no bloquear right-click)
                ShouldRecognizeSimultaneously = (_, _) => true
            };

            // Detecta el "press" desde el inicio (más fiable que Tap cuando otros controles consumen el click).
            _globalDismissPressRecognizer = new UILongPressGestureRecognizer(OnGlobalNativePressed)
            {
                MinimumPressDuration = 0,
                CancelsTouchesInView = false,
                DelaysTouchesBegan = false,
                DelaysTouchesEnded = false,
                ShouldRecognizeSimultaneously = (_, _) => true
            };

            // En MacCatalyst, muchos eventos de ratón llegan como IndirectPointer.
            // Permitimos explícitamente esos touch types para que el cierre funcione en toda la UI.
            try
            {
                var allowedTouchTypes = new List<Foundation.NSNumber>
                {
                    Foundation.NSNumber.FromInt32((int)UITouchType.Direct),
                    Foundation.NSNumber.FromInt32((int)UITouchType.Indirect)
                };

                // Evitar valores hardcodeados (en algunos runtimes 5 dispara NSInvalidArgumentException).
                // Si el enum existe en este target, lo añadimos por nombre.
                if (Enum.TryParse<UITouchType>("IndirectPointer", out var indirectPointer))
                    allowedTouchTypes.Add(Foundation.NSNumber.FromInt32((int)indirectPointer));

                _globalDismissTapRecognizer.AllowedTouchTypes = new Foundation.NSNumber[]
                {
                    allowedTouchTypes[0],
                    allowedTouchTypes[1],
                    // Si existe IndirectPointer, irá como 3er elemento
                    // (si no existe, repetimos Indirect para no dejar array vacío)
                    allowedTouchTypes.Count > 2 ? allowedTouchTypes[2] : allowedTouchTypes[1]
                };
                _globalDismissPressRecognizer.AllowedTouchTypes = new Foundation.NSNumber[]
                {
                    allowedTouchTypes[0],
                    allowedTouchTypes[1],
                    allowedTouchTypes.Count > 2 ? allowedTouchTypes[2] : allowedTouchTypes[1]
                };
            }
            catch { }

            hostView.AddGestureRecognizer(_globalDismissTapRecognizer);
            hostView.AddGestureRecognizer(_globalDismissPressRecognizer);
        }
        catch { }
    }

    private void TryDetachGlobalDismissRecognizer()
    {
        try
        {
            if (_globalDismissHostView != null)
            {
                if (_globalDismissTapRecognizer != null)
                    _globalDismissHostView.RemoveGestureRecognizer(_globalDismissTapRecognizer);
                if (_globalDismissPressRecognizer != null)
                    _globalDismissHostView.RemoveGestureRecognizer(_globalDismissPressRecognizer);
            }
        }
        catch { }

        try { _globalDismissTapRecognizer?.Dispose(); } catch { }
        try { _globalDismissPressRecognizer?.Dispose(); } catch { }
        _globalDismissTapRecognizer = null;
        _globalDismissPressRecognizer = null;
        _globalDismissHostView = null;
    }

    private void OnGlobalNativeTapped()
    {
        try
        {
            if (_globalDismissTapRecognizer == null || _globalDismissHostView == null)
                return;

            var anyVisible = (UserLibraryContextMenuOverlay?.IsVisible ?? false)
                || (SessionsContextMenuOverlay?.IsVisible ?? false)
                || (SessionRowContextMenu?.IsVisible ?? false)
                || (SmartFolderContextMenu?.IsVisible ?? false);
            if (!anyVisible)
                return;

            var host = _globalDismissHostView;
            var location = _globalDismissTapRecognizer.LocationInView(host);

            // Si el click es dentro de un menú visible, NO lo cerramos aquí (los items ya lo cierran al ejecutar).
            if (IsPointInsideVisibleMenu(host, location, SessionRowContextMenu))
                return;
            if (IsPointInsideVisibleMenu(host, location, SmartFolderContextMenu))
                return;
            if (IsPointInsideVisibleMenu(host, location, UserLibraryContextMenu))
                return;
            if (IsPointInsideVisibleMenu(host, location, SessionsContextMenu))
                return;

            HideUserLibraryContextMenu();
            HideSessionsContextMenu();
            HideSessionRowContextMenu();
            HideSmartFolderContextMenu();
        }
        catch { }
    }

    private void OnGlobalNativePressed(UILongPressGestureRecognizer recognizer)
    {
        try
        {
            if (_globalDismissHostView == null)
                return;

            // Solo al comenzar, para no disparar repetidamente.
            if (recognizer.State != UIGestureRecognizerState.Began)
                return;

            var anyVisible = (UserLibraryContextMenuOverlay?.IsVisible ?? false)
                || (SessionsContextMenuOverlay?.IsVisible ?? false)
                || (SessionRowContextMenu?.IsVisible ?? false)
                || (SmartFolderContextMenu?.IsVisible ?? false);
            if (!anyVisible)
                return;

            var host = _globalDismissHostView;
            var location = recognizer.LocationInView(host);

            // Si el click es dentro de un menú visible, NO lo cerramos aquí.
            if (IsPointInsideVisibleMenu(host, location, SessionRowContextMenu))
                return;
            if (IsPointInsideVisibleMenu(host, location, SmartFolderContextMenu))
                return;
            if (IsPointInsideVisibleMenu(host, location, UserLibraryContextMenu))
                return;
            if (IsPointInsideVisibleMenu(host, location, SessionsContextMenu))
                return;

            HideUserLibraryContextMenu();
            HideSessionsContextMenu();
            HideSessionRowContextMenu();
            HideSmartFolderContextMenu();
        }
        catch { }
    }

    private static bool IsPointInsideVisibleMenu(UIView host, CGPoint locationInHost, VisualElement? menuElement)
    {
        try
        {
            if (menuElement == null || !menuElement.IsVisible)
                return false;
            if (menuElement.Handler?.PlatformView is not UIView menuView)
                return false;

            var pInMenu = menuView.ConvertPointFromView(locationInHost, host);
            return menuView.PointInside(pInMenu, null);
        }
        catch
        {
            return false;
        }
    }
#endif

    public async Task PrepareForShellNavigationAsync()
    {
        try
        {
            if (Microsoft.Maui.ApplicationModel.MainThread.IsMainThread)
                PrepareForShellNavigation();
            else
                await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(PrepareForShellNavigation);
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "PrepareForShellNavigationAsync error", ex);
        }
    }

    private void PrepareForShellNavigation()
    {
        try
        {
            AppLog.Info("DashboardPage", "PrepareForShellNavigation | stopping hover + cleaning players");

            // Hover preview: parar y soltar el Source ANTES del cambio de ShellItem.
            try { HoverPreviewPlayer?.PrepareForCleanup(); } catch { }
            try { HoverPreviewPlayer?.Stop(); } catch { }

            // Forzar que el binding deje de referenciar el vídeo
            _viewModel.HoverVideo = null;

            // Limpia también los reproductores custom
            CleanupAllVideos();
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "PrepareForShellNavigation error", ex);
        }
    }

    private void OnHoverPreviewOpened(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        // Autoplay
        try { HoverPreviewPlayer.Play(); } catch { }
    }

    private void OnHoverPreviewEnded(object? sender, EventArgs e)
    {
        if (!_isPageActive) return;
        // Loop simple
        try
        {
            HoverPreviewPlayer.SeekTo(TimeSpan.Zero);
            HoverPreviewPlayer.Play();
        }
        catch
        {
            // best effort
        }
    }

    private void OnVideoHoverStarted(object? sender, HoverVideoEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.HoverVideo = e.Video;

            try
            {
                PositionHoverPreview(e);
            }
            catch { }
        });
    }

    private void OnVideoHoverEnded(object? sender, HoverVideoEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.HoverVideo = null;
        });
    }

    private void OnVideoHoverMoved(object? sender, HoverVideoEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // Solo reposicionar si el preview está activo
                if (_viewModel.HasHoverVideo)
                    PositionHoverPreview(e);
            }
            catch { }
        });
    }

    private void PositionHoverPreview(HoverVideoEventArgs e)
    {
        if (HoverPreviewContainer == null)
            return;

        // Si todavía no estamos activos, no reposicionar (evita glitches durante navegación)
        if (!_isPageActive)
            return;

        var targetX = 0.0;
        var targetY = 0.0;
        var hasPointerLocation = false;
        var pointerX = 0.0;
        var pointerY = 0.0;

#if MACCATALYST
        // Preferimos coordenadas reales del puntero si están disponibles
        if (e.PointerLocationInSourceView is Point pointerInSource
            && e.SourceView?.Handler?.PlatformView is UIView sourceUIView
            && RootGrid?.Handler?.PlatformView is UIView rootUIView)
        {
            var p = new CGPoint(pointerInSource.X, pointerInSource.Y);
            var pInRoot = sourceUIView.ConvertPointToView(p, rootUIView);
            targetX = pInRoot.X;
            targetY = pInRoot.Y;
            hasPointerLocation = true;
            pointerX = targetX;
            pointerY = targetY;
        }
#endif

#if WINDOWS
        // WinUI: convertir coordenadas del puntero (dentro del icono) al RootGrid
        if (e.PointerLocationInSourceView is Point pointerInSourceWin)
        {
            var sourceUi = e.SourceView?.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement;
            var rootUi = RootGrid?.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement;
            if (sourceUi != null && rootUi != null)
            {
                try
                {
                    var transform = sourceUi.TransformToVisual(rootUi);
                    var pInRoot = transform.TransformPoint(new Windows.Foundation.Point(pointerInSourceWin.X, pointerInSourceWin.Y));
                    targetX = pInRoot.X;
                    targetY = pInRoot.Y;
                    hasPointerLocation = true;
                    pointerX = targetX;
                    pointerY = targetY;
                }
                catch
                {
                    // fallback below
                }
            }
        }
#endif

        // Fallback: anclar al borde derecho del icono del ojo
        if (targetX <= 0 && targetY <= 0)
        {
            var fallbackX = 0.0;
            var fallbackY = 0.0;

#if WINDOWS
            // WinUI: anclar con transform real (incluye offsets de scroll/virtualización)
            var sourceUiFallback = e.SourceView?.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement;
            var rootUiFallback = RootGrid?.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement;
            if (sourceUiFallback != null && rootUiFallback != null)
            {
                try
                {
                    var transform = sourceUiFallback.TransformToVisual(rootUiFallback);
                    var origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    fallbackX = origin.X + Math.Max(0, e.SourceView?.Width ?? 0);
                    fallbackY = origin.Y;
                }
                catch
                {
                    fallbackX = 0;
                    fallbackY = 0;
                }
            }
#endif

            if (fallbackX <= 0 && fallbackY <= 0)
            {
                var anchor = GetApproximatePositionRelativeToPage(e.SourceView);
                fallbackX = anchor.X + Math.Max(0, e.SourceView?.Width ?? 0);
                fallbackY = anchor.Y;
            }

            targetX = fallbackX;
            targetY = fallbackY;
        }

        const double margin = 12;
        const double pad = 12;

        // Tamaño estimado del popup (si aún no ha medido, usamos 16:9 + 1 fila)
        var previewWidth = HoverPreviewContainer.Width > 0
            ? HoverPreviewContainer.Width
            : (HoverPreviewContainer.WidthRequest > 0 ? HoverPreviewContainer.WidthRequest : 480);

        var previewHeight = HoverPreviewContainer.Height > 0
            ? HoverPreviewContainer.Height
            : (previewWidth / 1.777) + 44;

        var containerWidth = RootGrid?.Width > 0 ? RootGrid.Width : this.Width;
        var containerHeight = RootGrid?.Height > 0 ? RootGrid.Height : this.Height;

        var maxX = Math.Max(margin, containerWidth - previewWidth - margin);
        var maxY = Math.Max(margin, containerHeight - previewHeight - margin);

        // Intentar colocar en una esquina alrededor del puntero/ancla para evitar cubrir el puntero
        var candidates = new (double X, double Y)[]
        {
            (targetX + pad, targetY + pad),
            (targetX - previewWidth - pad, targetY + pad),
            (targetX + pad, targetY - previewHeight - pad),
            (targetX - previewWidth - pad, targetY - previewHeight - pad),
        };

        var placedX = candidates[0].X;
        var placedY = candidates[0].Y;
        var found = false;

        foreach (var c in candidates)
        {
            if (c.X >= margin && c.X <= maxX && c.Y >= margin && c.Y <= maxY)
            {
                placedX = c.X;
                placedY = c.Y;
                found = true;
                break;
            }
        }

        if (!found)
        {
            // Clamp básico
            placedX = Math.Clamp(placedX, margin, maxX);
            placedY = Math.Clamp(placedY, margin, maxY);

            // Si el clamping lo mete debajo del puntero, intentamos empujarlo al otro lado
            if (hasPointerLocation)
            {
                var overlapsPointer = pointerX >= placedX && pointerX <= (placedX + previewWidth)
                    && pointerY >= placedY && pointerY <= (placedY + previewHeight);

                if (overlapsPointer)
                {
                    var tryLeftX = Math.Clamp(pointerX - previewWidth - pad, margin, maxX);
                    var tryTopY = Math.Clamp(pointerY - previewHeight - pad, margin, maxY);

                    // Preferimos mover en X; si no resuelve, movemos en Y.
                    placedX = tryLeftX;
                    overlapsPointer = pointerX >= placedX && pointerX <= (placedX + previewWidth)
                        && pointerY >= placedY && pointerY <= (placedY + previewHeight);

                    if (overlapsPointer)
                        placedY = tryTopY;
                }
            }
        }

        HoverPreviewContainer.TranslationX = placedX;
        HoverPreviewContainer.TranslationY = placedY;
    }

    private static Point GetApproximatePositionRelativeToPage(VisualElement? element)
    {
        double x = 0;
        double y = 0;

        Element? current = element;
        while (current is VisualElement ve)
        {
            x += ve.X;
            y += ve.Y;
            current = ve.Parent;
        }

        return new Point(x, y);
    }

#if IOS
    private void OnVideoLongPressStarted(object? sender, LongPressVideoEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.HoverVideo = e.Video;
        });
    }

    private void OnVideoLongPressEnded(object? sender, LongPressVideoEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.HoverVideo = null;
        });
    }
#endif

    private void OnFilterChipTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (sender is not BindableObject bindable)
                return;

            var ctx = bindable.BindingContext;
            if (ctx == null)
                return;

            // Todos los items de filtro heredan de FilterItem<T> (genérico).
            // No podemos hacer un cast directo sin conocer T, así que usamos reflexión.
            var prop = ctx.GetType().GetProperty("IsSelected");
            if (prop == null || prop.PropertyType != typeof(bool) || !prop.CanWrite)
                return;

            var current = (bool)(prop.GetValue(ctx) ?? false);
            prop.SetValue(ctx, !current);
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "OnFilterChipTapped error", ex);
        }
    }

    private void OnDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is GestureRecognizer gestureRecognizer && 
            gestureRecognizer.Parent is View view && 
            view.BindingContext is VideoClip video)
        {
            e.Data.Properties["VideoClip"] = video;
        }
    }

    private void OnDropScreen1(object? sender, DropEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen1 called");
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen1: got video Id={video.Id}");
            _ = _viewModel.SetParallelVideoSlotAsync(1, video);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen1: no VideoClip in drop data");
        }
    }

    private void OnDropScreen2(object? sender, DropEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen2 called");
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen2: got video Id={video.Id}");
            _ = _viewModel.SetParallelVideoSlotAsync(2, video);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen2: no VideoClip in drop data");
        }
    }

    private void OnDropScreen3(object? sender, DropEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen3 called");
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen3: got video Id={video.Id}");
            _ = _viewModel.SetParallelVideoSlotAsync(3, video);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen3: no VideoClip in drop data");
        }
    }

    private void OnDropScreen4(object? sender, DropEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen4 called");
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen4: got video Id={video.Id}");
            _ = _viewModel.SetParallelVideoSlotAsync(4, video);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Dashboard] OnDropScreen4: no VideoClip in drop data");
        }
    }

    private void OnScrubUpdated(object? sender, VideoScrubEventArgs e)
    {
        if (!_isPageActive) return;
        
        var player = GetPlayerForIndex(e.VideoIndex);
        if (player == null) return;
        
        if (e.IsStart)
        {
            // Guardar posición actual y pausar
            switch (e.VideoIndex)
            {
                case 0: _currentPosition0 = player.Position.TotalMilliseconds; break;
                case 1: _currentPosition1 = player.Position.TotalMilliseconds; break;
                case 2: _currentPosition2 = player.Position.TotalMilliseconds; break;
                case 3: _currentPosition3 = player.Position.TotalMilliseconds; break;
                case 4: _currentPosition4 = player.Position.TotalMilliseconds; break;
            }
            player.Pause();
        }
        else
        {
            // Aplicar delta incremental
            ref double currentPos = ref _currentPosition0;
            if (e.VideoIndex == 1) currentPos = ref _currentPosition1;
            else if (e.VideoIndex == 2) currentPos = ref _currentPosition2;
            else if (e.VideoIndex == 3) currentPos = ref _currentPosition3;
            else if (e.VideoIndex == 4) currentPos = ref _currentPosition4;
            
            currentPos += e.DeltaMilliseconds;
            
            // Clampear dentro del rango válido
            if (player.Duration != TimeSpan.Zero)
            {
                currentPos = Math.Max(0, Math.Min(currentPos, player.Duration.TotalMilliseconds));
            }
            
            // Solo hacer seek si el video está cargado
            if (player.Duration != TimeSpan.Zero)
            {
                player.SeekTo(TimeSpan.FromMilliseconds(currentPos));
            }
        }
    }

    private void OnScrubEnded(object? sender, VideoScrubEventArgs e)
    {
        if (!_isPageActive) return;
        
        var player = GetPlayerForIndex(e.VideoIndex);
        player?.Pause();
    }

    private PrecisionVideoPlayer? GetPlayerForIndex(int index)
    {
        // Layout fijo 2x2
        return index switch
        {
            1 => PreviewPlayer1Q,
            2 => PreviewPlayer2Q,
            3 => PreviewPlayer3Q,
            4 => PreviewPlayer4Q,
            _ => null
        };
    }

    // Toggle Play/Pause para todos los videos activos
    public void TogglePlayPause()
    {
        AppLog.Info("DashboardPage", $"TogglePlayPause | IsPreviewMode={_viewModel.IsPreviewMode} | HasVideo1={_viewModel.HasParallelVideo1}");

        // Importante: los mini players solo se vuelven visibles cuando IsPreviewMode=true
        // (ver MultiTriggers en XAML). Si el usuario pulsa espacio para reproducir,
        // activamos preview mode para evitar que se oculte la miniatura y se quede el fondo gris.
        var enabledPreviewModeNow = false;
        if (!_viewModel.IsPreviewMode && (
                _viewModel.HasParallelVideo1 ||
                _viewModel.HasParallelVideo2 ||
                _viewModel.HasParallelVideo3 ||
                _viewModel.HasParallelVideo4))
        {
            _viewModel.IsPreviewMode = true;
            enabledPreviewModeNow = true;
            AppLog.Info("DashboardPage", "TogglePlayPause | Enabled IsPreviewMode, will retry after delay");
        }

        // Si acabamos de activar IsPreviewMode, dejamos un tick para que se apliquen triggers
        // (IsVisible) y el handler tenga tiempo de conectarse/cargar el Source.
        if (enabledPreviewModeNow)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await Task.Delay(16);
                    TogglePlayPause();
                }
                catch { }
            });
            return;
        }

        // Controlar los slots ocupados en 2x2
        if (_viewModel.HasParallelVideo1) TogglePlayer(PreviewPlayer1Q);
        if (_viewModel.HasParallelVideo2) TogglePlayer(PreviewPlayer2Q);
        if (_viewModel.HasParallelVideo3) TogglePlayer(PreviewPlayer3Q);
        if (_viewModel.HasParallelVideo4) TogglePlayer(PreviewPlayer4Q);
    }

    private void TogglePlayer(PrecisionVideoPlayer? player)
    {
        if (player == null) return;

        AppLog.Info("DashboardPage", $"TogglePlayer | Name={player.AutomationId} | Source={player.Source ?? "[null]"} | IsVisible={player.IsVisible} | IsPlaying={player.IsPlaying} | Handler={player.Handler?.GetType().Name ?? "[null]"}");

        if (player.IsPlaying)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }
    }

    private void PauseAllVideos()
    {
        PreviewPlayer1Q?.Pause();
        PreviewPlayer2Q?.Pause();
        PreviewPlayer3Q?.Pause();
        PreviewPlayer4Q?.Pause();
    }

    /// <summary>
    /// Limpia completamente todos los reproductores de video para evitar conflictos
    /// de jerarquía de ViewControllers en iOS al navegar entre páginas.
    /// IMPORTANTE: No asignamos Source=null directamente porque eso ROMPE el binding XAML.
    /// En su lugar, usamos PrepareForCleanup() que limpia el AVPlayer nativo sin tocar el binding,
    /// y luego ClearPreviewVideos() del ViewModel que propaga null via binding.
    /// </summary>
    private void CleanupAllVideos()
    {
        AppLog.Info("DashboardPage", "CleanupAllVideos BEGIN");
        try
        {
            // Primero pausar
            PauseAllVideos();

			// Hover preview (PrecisionVideoPlayer)
			try { HoverPreviewPlayer?.PrepareForCleanup(); } catch { }
			try { HoverPreviewPlayer?.Stop(); } catch { }

			// IMPORTANTE: NO asignar HoverPreviewPlayer.Source = null, porque rompe el binding XAML.
			// En su lugar, limpiar el valor en el ViewModel para que el binding propague null.
			_viewModel.HoverVideo = null;
            
            // Para los preview players del dashboard, solo llamamos PrepareForCleanup()
            // que limpia el AVPlayer nativo pero NO rompe el binding.
            // El binding se actualizará cuando ClearPreviewVideos() ponga ParallelVideoX = null.
            PreparePlayerForCleanup(PreviewPlayer1Q);
            PreparePlayerForCleanup(PreviewPlayer2Q);
            PreparePlayerForCleanup(PreviewPlayer3Q);
            PreparePlayerForCleanup(PreviewPlayer4Q);
            
            // Limpiar en el ViewModel - esto propagará null via binding a los controles
            _viewModel.ClearPreviewVideos();

            AppLog.Info("DashboardPage", "CleanupAllVideos END");
        }
        catch (Exception ex)
        {
            AppLog.Error("DashboardPage", "Error cleaning up videos", ex);
        }
    }
    
    private void PreparePlayerForCleanup(PrecisionVideoPlayer? player)
    {
        if (player != null)
        {
            try
            {
                player.PrepareForCleanup();
            }
            catch { }
        }
    }
    
    // Mantenemos ClearVideoSource por si se necesita en otro lugar, pero NO lo usamos
    // para los players con binding porque rompe el binding XAML.
    private void ClearVideoSource(PrecisionVideoPlayer? player)
    {
        if (player != null)
        {
            try
            {
                player.Source = null;
            }
            catch { }
        }
    }
}
