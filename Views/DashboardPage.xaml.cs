using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;
using CrownRFEP_Reader.Controls;
using CrownRFEP_Reader.Services;
using System.Collections.Specialized;
using System.Reflection;

#if MACCATALYST
using CoreGraphics;
using UIKit;
#endif

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#endif

namespace CrownRFEP_Reader.Views;

public partial class DashboardPage : ContentPage, IShellNavigatingCleanup
{
    private readonly DashboardViewModel _viewModel;
    private NotifyCollectionChangedEventHandler? _smartFoldersChangedHandler;

    private SessionRow? _contextMenuSessionRow;
    private SmartFolderDefinition? _contextMenuSmartFolder;
    
    // Posiciones actuales de cada video para scrubbing incremental
    private double _currentPosition0;
    private double _currentPosition1;
    private double _currentPosition2;
    private double _currentPosition3;
    private double _currentPosition4;
    
    // Indica si esta página está activa
    private bool _isPageActive;

#if MACCATALYST
	private UITapGestureRecognizer? _globalDismissTapRecognizer;
	private UIView? _globalDismissHostView;
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
        VideoScrubBehavior.ScrubEnded += OnScrubEnded;
        
#if IOS
        // Suscribirse a eventos de long press para preview en iOS
        LongPressVideoPreviewBehavior.VideoLongPressStarted += OnVideoLongPressStarted;
        LongPressVideoPreviewBehavior.VideoLongPressEnded += OnVideoLongPressEnded;
#endif
        
#if MACCATALYST
        // Suscribirse a eventos de teclado
        KeyPressHandler.SpaceBarPressed += OnSpaceBarPressed;
#endif

		// Hover preview: usamos PrecisionVideoPlayer (AVPlayerLayer). Autoplay+loop aquí.
		HoverPreviewPlayer.MediaOpened += OnHoverPreviewOpened;
		HoverPreviewPlayer.MediaEnded += OnHoverPreviewEnded;

        _smartFoldersChangedHandler = (_, __) =>
        {
            try
            {
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
        _viewModel.SmartFolders.CollectionChanged += _smartFoldersChangedHandler;

        try { SidebarSessionsCollectionView?.InvalidateMeasure(); } catch { }


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

                // Preview players (drop zones): cuando abren media, ocultamos miniatura.
                PreviewPlayerSingle.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer1H.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer2H.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer1V.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer2V.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer1Q.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer2Q.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer3Q.MediaOpened += OnPreviewPlayerMediaOpened;
                PreviewPlayer4Q.MediaOpened += OnPreviewPlayerMediaOpened;

        // Marcar "ready" solo cuando hay avance real de tiempo (evita quedarse gris).
        PreviewPlayerSingle.PositionChanged += OnPreviewPlayerPositionChanged;
        PreviewPlayer1H.PositionChanged += OnPreviewPlayerPositionChanged;
        PreviewPlayer2H.PositionChanged += OnPreviewPlayerPositionChanged;
        PreviewPlayer1V.PositionChanged += OnPreviewPlayerPositionChanged;
        PreviewPlayer2V.PositionChanged += OnPreviewPlayerPositionChanged;
        PreviewPlayer1Q.PositionChanged += OnPreviewPlayerPositionChanged;
        PreviewPlayer2Q.PositionChanged += OnPreviewPlayerPositionChanged;
        PreviewPlayer3Q.PositionChanged += OnPreviewPlayerPositionChanged;
        PreviewPlayer4Q.PositionChanged += OnPreviewPlayerPositionChanged;
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

        try { PreviewPlayerSingle.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer1H.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer2H.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer1V.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer2V.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer1Q.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer2Q.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer3Q.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }
        try { PreviewPlayer4Q.MediaOpened -= OnPreviewPlayerMediaOpened; } catch { }

        try { PreviewPlayerSingle.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
        try { PreviewPlayer1H.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
        try { PreviewPlayer2H.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
        try { PreviewPlayer1V.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
        try { PreviewPlayer2V.PositionChanged -= OnPreviewPlayerPositionChanged; } catch { }
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

            // Ajustar el header para que se re-mida con el ancho actual del sidebar.
            if (Math.Abs(SidebarFixedItemsHeader.WidthRequest - width) > 0.5)
                SidebarFixedItemsHeader.WidthRequest = width;

            // Re-medición para que los layouts recalculen truncado y columnas.
            SidebarFixedItemsHeader.InvalidateMeasure();
            SidebarSessionsCollectionView.InvalidateMeasure();
        }
        catch { }
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

    private void OnSessionRowContextMenuTapped(object? sender, TappedEventArgs e)
    {
        try
        {
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

    private void OnSmartFolderContextMenuTapped(object? sender, TappedEventArgs e)
    {
        try
        {
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
    }

    private void HideAllContextMenus()
    {
        HideUserLibraryContextMenu();
        HideSessionsContextMenu();
        HideSessionRowContextMenu();
        HideSmartFolderContextMenu();
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
            if (sender == PreviewPlayerSingle || sender == PreviewPlayer1H || sender == PreviewPlayer1V || sender == PreviewPlayer1Q)
            {
                _viewModel.IsPreviewPlayer1Ready = true;
            }
            else if (sender == PreviewPlayer2H || sender == PreviewPlayer2V || sender == PreviewPlayer2Q)
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

#if MACCATALYST
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

#if MACCATALYST
		TryAttachGlobalDismissRecognizer();
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

        AppLog.Info(
            "DashboardPage",
            $"OnDisappearing | NavStack={Shell.Current?.Navigation?.NavigationStack?.Count} | ModalStack={Shell.Current?.Navigation?.ModalStack?.Count}");
        
        // Pausar y limpiar todos los videos al salir para evitar conflictos de jerarquía de ViewControllers
        CleanupAllVideos();
    }

#if MACCATALYST
    private void TryAttachGlobalDismissRecognizer()
    {
        try
        {
            if (_globalDismissTapRecognizer != null)
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

            hostView.AddGestureRecognizer(_globalDismissTapRecognizer);
        }
        catch { }
    }

    private void TryDetachGlobalDismissRecognizer()
    {
        try
        {
            if (_globalDismissHostView != null && _globalDismissTapRecognizer != null)
                _globalDismissHostView.RemoveGestureRecognizer(_globalDismissTapRecognizer);
        }
        catch { }

        try { _globalDismissTapRecognizer?.Dispose(); } catch { }
        _globalDismissTapRecognizer = null;
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
        }
#endif

        // Fallback: anclar al borde derecho del icono del ojo
        if (targetX <= 0 && targetY <= 0)
        {
            var anchor = GetApproximatePositionRelativeToPage(e.SourceView);
            targetX = anchor.X + Math.Max(0, e.SourceView?.Width ?? 0);
            targetY = anchor.Y;
        }

        // Offset tipo menú contextual
        targetX += 12;
        targetY += 12;

        // Clamp para no salirse por la derecha/abajo
        var previewWidth = HoverPreviewContainer.WidthRequest > 0 ? HoverPreviewContainer.WidthRequest : 480;
        var previewHeight = HoverPreviewContainer.Height > 0
            ? HoverPreviewContainer.Height
            : (previewWidth / 1.777) + 44; // 16:9 + fila de texto

        var containerWidth = RootGrid?.Width > 0 ? RootGrid.Width : this.Width;
        var containerHeight = RootGrid?.Height > 0 ? RootGrid.Height : this.Height;

        var maxX = Math.Max(0, containerWidth - previewWidth - 12);
        var maxY = Math.Max(0, containerHeight - previewHeight - 12);

        targetX = Math.Clamp(targetX, 12, maxX);
        targetY = Math.Clamp(targetY, 12, maxY);

        HoverPreviewContainer.TranslationX = targetX;
        HoverPreviewContainer.TranslationY = targetY;
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
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            _viewModel.ParallelVideo1 = video;
        }
    }

    private void OnDropScreen2(object? sender, DropEventArgs e)
    {
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            _viewModel.ParallelVideo2 = video;
        }
    }

    private void OnDropScreen3(object? sender, DropEventArgs e)
    {
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            _viewModel.ParallelVideo3 = video;
        }
    }

    private void OnDropScreen4(object? sender, DropEventArgs e)
    {
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            _viewModel.ParallelVideo4 = video;
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
        // Modo cuádruple
        if (_viewModel.IsQuadVideoMode)
        {
            return index switch
            {
                1 => PreviewPlayer1Q,
                2 => PreviewPlayer2Q,
                3 => PreviewPlayer3Q,
                4 => PreviewPlayer4Q,
                _ => null
            };
        }
        
        // Modo único o paralelo
        return index switch
        {
            0 => PreviewPlayerSingle,
            1 => _viewModel.IsHorizontalOrientation ? PreviewPlayer1H : PreviewPlayer1V,
            2 => _viewModel.IsHorizontalOrientation ? PreviewPlayer2H : PreviewPlayer2V,
            _ => null
        };
    }

    // Toggle Play/Pause para todos los videos activos
    public void TogglePlayPause()
    {
        AppLog.Info("DashboardPage", $"TogglePlayPause | IsPreviewMode={_viewModel.IsPreviewMode} | HasVideo1={_viewModel.HasParallelVideo1} | IsSingle={_viewModel.IsSingleVideoMode} | IsQuad={_viewModel.IsQuadVideoMode} | IsHorizontal={_viewModel.IsHorizontalOrientation}");

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

        if (_viewModel.IsSingleVideoMode)
        {
            TogglePlayer(PreviewPlayerSingle);
        }
        else if (_viewModel.IsQuadVideoMode)
        {
            // Modo cuádruple: controlar los 4 videos
            TogglePlayer(PreviewPlayer1Q);
            TogglePlayer(PreviewPlayer2Q);
            TogglePlayer(PreviewPlayer3Q);
            TogglePlayer(PreviewPlayer4Q);
        }
        else
        {
            // Modo paralelo: controlar ambos videos
            if (_viewModel.IsHorizontalOrientation)
            {
                AppLog.Info("DashboardPage", $"TogglePlayPause Parallel H | VM.ClipPath1={_viewModel.ParallelVideo1ClipPath ?? "[null]"} | VM.ClipPath2={_viewModel.ParallelVideo2ClipPath ?? "[null]"}");
                TogglePlayer(PreviewPlayer1H);
                TogglePlayer(PreviewPlayer2H);
            }
            else
            {
                AppLog.Info("DashboardPage", $"TogglePlayPause Parallel V | VM.ClipPath1={_viewModel.ParallelVideo1ClipPath ?? "[null]"} | VM.ClipPath2={_viewModel.ParallelVideo2ClipPath ?? "[null]"}");
                TogglePlayer(PreviewPlayer1V);
                TogglePlayer(PreviewPlayer2V);
            }
        }
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
        PreviewPlayerSingle?.Pause();
        PreviewPlayer1H?.Pause();
        PreviewPlayer2H?.Pause();
        PreviewPlayer1V?.Pause();
        PreviewPlayer2V?.Pause();
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
            PreparePlayerForCleanup(PreviewPlayerSingle);
            PreparePlayerForCleanup(PreviewPlayer1H);
            PreparePlayerForCleanup(PreviewPlayer2H);
            PreparePlayerForCleanup(PreviewPlayer1V);
            PreparePlayerForCleanup(PreviewPlayer2V);
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
