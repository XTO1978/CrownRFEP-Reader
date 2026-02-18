using Microsoft.Maui.Controls;
using System.Linq;
using CrownRFEP_Reader.Behaviors;
using CrownRFEP_Reader.Services;
using CrownRFEP_Reader.ViewModels;
#if MACCATALYST
using CoreGraphics;
using Microsoft.Maui.Handlers;
using UIKit;
#endif

namespace CrownRFEP_Reader.Views.Controls;

public partial class TopTabsBar : Microsoft.Maui.Controls.ContentView
{
    private UserProfileNotifier? _userProfileNotifier;
    private Grid? _settingsOverlay;
    private string _selectedLanguage = "es"; // default español
    private VerticalStackLayout? _languageSubItems;
    private Label? _languageArrowLabel;

    public TopTabsBar()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_userProfileNotifier != null)
            _userProfileNotifier.ProfileSaved -= OnUserProfileSaved;
        _userProfileNotifier = null;
        DismissSettingsDropdown();
    }

    private async void OnUserProfileSaved(object? sender, EventArgs e)
    {
        await RefreshProfileButtonTextAsync();
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
        _userProfileNotifier = services?.GetService<UserProfileNotifier>();
        if (_userProfileNotifier != null)
        {
            _userProfileNotifier.ProfileSaved -= OnUserProfileSaved;
            _userProfileNotifier.ProfileSaved += OnUserProfileSaved;
        }

        await RefreshProfileButtonTextAsync();
    }

    private async Task RefreshProfileButtonTextAsync()
    {
        try
        {
            var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
            var databaseService = services?.GetService<DatabaseService>();
            if (databaseService == null)
            {
                if (ProfileNameLabel != null)
                    ProfileNameLabel.Text = "Yo";
                if (ProfilePhotoImage != null)
                    ProfilePhotoImage.IsVisible = false;
                return;
            }

            var profile = await databaseService.GetUserProfileAsync();

            var nombre = profile?.Nombre?.Trim();
            var apellidos = profile?.Apellidos?.Trim();

            // Formato pedido: Nombre Apellido
            string fullName = $"{nombre ?? string.Empty} {apellidos ?? string.Empty}".Trim();
            var text = string.IsNullOrWhiteSpace(fullName) ? "Yo" : fullName;
            if (ProfileNameLabel != null)
                ProfileNameLabel.Text = text;

            var fotoPath = profile?.FotoPath;
            if (ProfilePhotoImage != null)
            {
                if (!string.IsNullOrWhiteSpace(fotoPath))
                {
                    ProfilePhotoImage.Source = ImageSource.FromFile(fotoPath);
                    ProfilePhotoImage.IsVisible = true;
                }
                else
                {
                    ProfilePhotoImage.Source = null;
                    ProfilePhotoImage.IsVisible = false;
                }
            }
        }
        catch
        {
            if (ProfileNameLabel != null)
                ProfileNameLabel.Text = "Yo";
            if (ProfilePhotoImage != null)
            {
                ProfilePhotoImage.Source = null;
                ProfilePhotoImage.IsVisible = false;
            }
        }
    }

    private static async Task GoToRootAsync(string rootRoute)
    {
        try
        {
            // Navegación absoluta al root de cada sección.
            if (Shell.Current != null)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.GoToAsync($"//{rootRoute}");
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error to {rootRoute}: {ex.Message}");
        }
    }

    private async void OnDashboardClicked(object sender, EventArgs e) => await GoToRootAsync("dashboard");
    private async void OnSessionsClicked(object sender, EventArgs e) => await GoToRootAsync("sessions");
    private async void OnAthletesClicked(object sender, EventArgs e) => await GoToRootAsync("athletes");
    private async void OnStatsClicked(object sender, EventArgs e) => await GoToRootAsync("stats");
    private async void OnImportClicked(object sender, EventArgs e) => await GoToRootAsync("import");
    
    // ─── Settings dropdown (programmatic overlay) ──────────────
    private void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        if (_settingsOverlay != null)
        {
            DismissSettingsDropdown();
            return;
        }
        ShowSettingsDropdown();
    }

    private void ShowSettingsDropdown()
    {
        var page = GetParentPage() as ContentPage;
        if (page?.Content is not Grid pageGrid)
            return;

        // Calcular cuántas filas/columnas tiene el Grid de la página
        int rowSpan = Math.Max(1, pageGrid.RowDefinitions.Count);
        int colSpan = Math.Max(1, pageGrid.ColumnDefinitions.Count);

        _settingsOverlay = new Grid
        {
            InputTransparent = true,
            CascadeInputTransparent = false,
            ZIndex = 2000,
        };
        Grid.SetRowSpan(_settingsOverlay, rowSpan);
        Grid.SetColumnSpan(_settingsOverlay, colSpan);

        // Fondo transparente que captura taps para cerrar
        var dismissBox = new BoxView
        {
            BackgroundColor = Colors.Transparent,
            InputTransparent = false,
        };
        dismissBox.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => DismissSettingsDropdown()),
        });
        _settingsOverlay.Children.Add(dismissBox);

        // Calcular la posición del botón Settings respecto a la página
        double leftOffset = 0;
        double topOffset = 50; // altura del header
        try
        {
            // Recorrer el árbol visual acumulando X/Y hasta llegar al pageGrid
            double accX = SettingsButton.Frame.X;
            double accY = SettingsButton.Frame.Y;
            double btnHeight = SettingsButton.Frame.Height;
            
            Element? current = SettingsButton.Parent;
            while (current != null && current != pageGrid && current is VisualElement ve)
            {
                accX += ve.Frame.X;
                accY += ve.Frame.Y;
                current = ve.Parent;
            }
            leftOffset = accX;
            topOffset = accY + btnHeight + 2;
        }
        catch
        {
            // Fallback: posicionar centrado
            leftOffset = (pageGrid.Width - 280) / 2;
        }

        // Panel del dropdown
        var dropdownPanel = BuildSettingsPanel();
        dropdownPanel.HorizontalOptions = LayoutOptions.Start;
        dropdownPanel.VerticalOptions = LayoutOptions.Start;
        dropdownPanel.Margin = new Thickness(leftOffset, topOffset, 0, 0);
        dropdownPanel.InputTransparent = false;
        _settingsOverlay.Children.Add(dropdownPanel);

        pageGrid.Children.Add(_settingsOverlay);
    }

    private void DismissSettingsDropdown()
    {
        if (_settingsOverlay == null) return;

        var page = GetParentPage() as ContentPage;
        if (page?.Content is Grid pageGrid)
            pageGrid.Children.Remove(_settingsOverlay);

        _settingsOverlay = null;
    }

    private Border BuildSettingsPanel()
    {
        var panel = new Border
        {
            Padding = new Thickness(8),
            BackgroundColor = Color.FromArgb("#FF2A2A2A"),
            Stroke = new SolidColorBrush(Color.FromArgb("#FF6A6A6A")),
            StrokeThickness = 0.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            WidthRequest = 280,
        };

        var stack = new VerticalStackLayout { Spacing = 2 };

        // ── Apariencia ──
        stack.Children.Add(CreateSectionHeader("APARIENCIA"));
        stack.Children.Add(CreateMenuItem("moon.fill", "Tema claro / oscuro", OnSettingsThemeAction));
        stack.Children.Add(CreateLanguageMenuItem());
        _languageSubItems = CreateLanguageSubItems();
        _languageSubItems.IsVisible = false;
        stack.Children.Add(_languageSubItems);
        stack.Children.Add(CreateSeparator());

        // ── Datos ──
        stack.Children.Add(CreateSectionHeader("DATOS"));
        stack.Children.Add(CreateMenuItem("internaldrive", "Almacenamiento", OnSettingsStorageAction));
        stack.Children.Add(CreateMenuItem("arrow.clockwise.circle", "Datos y backup", OnSettingsBackupAction));
        stack.Children.Add(CreateSeparator());

        // ── Organización ──
        stack.Children.Add(CreateSectionHeader("ORGANIZACIÓN"));
        stack.Children.Add(CreateMenuItem("building.2", "Administración de organización", OnSettingsAdminOrgAction));
        stack.Children.Add(CreateSeparator());

        // ── Info ──
        stack.Children.Add(CreateMenuItem("info.circle", "Acerca de CrownRFEP", OnSettingsAboutAction));

        panel.Content = stack;
        return panel;
    }

    private static Label CreateSectionHeader(string text) => new Label
    {
        Text = text,
        FontSize = 10,
        TextColor = Color.FromArgb("#FF888888"),
        FontAttributes = FontAttributes.Bold,
        Margin = new Thickness(10, 6, 0, 4),
    };

    private static BoxView CreateSeparator() => new BoxView
    {
        HeightRequest = 1,
        BackgroundColor = Color.FromArgb("#FF3A3A3A"),
        Margin = new Thickness(8, 4),
    };

    private Border CreateMenuItem(string symbolName, string label, Action action)
    {
        var border = new Border
        {
            Padding = DeviceInfo.Platform == DevicePlatform.iOS
                ? new Thickness(10, 2, 4, 2)
                : new Thickness(10, 6, 4, 6),
            BackgroundColor = Colors.Transparent,
            Stroke = null,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        };
        border.Behaviors.Add(new SidebarHoverBehavior());

        var row = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center };
        row.Children.Add(new SymbolIcon
        {
            HeightRequest = 18,
            WidthRequest = 18,
            SymbolName = symbolName,
            TintColor = Color.FromArgb("#FFAAAAAA"),
            VerticalOptions = LayoutOptions.Center,
        });
        row.Children.Add(new Label
        {
            Text = label,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
        });

        border.Content = row;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();
        border.GestureRecognizers.Add(tap);

        return border;
    }

    // ─── Settings menu actions ───────────────────────────────────
    private async void OnSettingsThemeAction()
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Tema", "Cambio de tema claro/oscuro (próximamente)", "OK");
    }

    private Border CreateLanguageMenuItem()
    {
        var border = new Border
        {
            Padding = DeviceInfo.Platform == DevicePlatform.iOS
                ? new Thickness(10, 2, 4, 2)
                : new Thickness(10, 6, 4, 6),
            BackgroundColor = Colors.Transparent,
            Stroke = null,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        };
        border.Behaviors.Add(new SidebarHoverBehavior());

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            VerticalOptions = LayoutOptions.Center,
        };

        var left = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center };
        left.Children.Add(new SymbolIcon
        {
            HeightRequest = 18, WidthRequest = 18,
            SymbolName = "globe",
            TintColor = Color.FromArgb("#FFAAAAAA"),
            VerticalOptions = LayoutOptions.Center,
        });
        left.Children.Add(new Label
        {
            Text = "Idioma",
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
        });
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        _languageArrowLabel = new Label
        {
            Text = "▶",
            FontSize = 10,
            TextColor = Color.FromArgb("#FF888888"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(_languageArrowLabel, 1);
        row.Children.Add(_languageArrowLabel);

        border.Content = row;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ToggleLanguageSubItems();
        border.GestureRecognizers.Add(tap);

        return border;
    }

    private void ToggleLanguageSubItems()
    {
        if (_languageSubItems == null) return;
        _languageSubItems.IsVisible = !_languageSubItems.IsVisible;
        if (_languageArrowLabel != null)
            _languageArrowLabel.Text = _languageSubItems.IsVisible ? "▼" : "▶";
    }

    private VerticalStackLayout CreateLanguageSubItems()
    {
        // Leer preferencia guardada
        if (Preferences.Default.ContainsKey("app_language"))
            _selectedLanguage = Preferences.Default.Get("app_language", "es");

        var stack = new VerticalStackLayout { Spacing = 0, Margin = new Thickness(30, 0, 0, 0) };
        stack.Children.Add(CreateLanguageOption("Español", "es"));
        stack.Children.Add(CreateLanguageOption("English", "en"));
        return stack;
    }

    private Border CreateLanguageOption(string label, string code)
    {
        var border = new Border
        {
            Padding = DeviceInfo.Platform == DevicePlatform.iOS
                ? new Thickness(10, 2, 4, 2)
                : new Thickness(10, 5, 4, 5),
            BackgroundColor = Colors.Transparent,
            Stroke = null,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        };
        border.Behaviors.Add(new SidebarHoverBehavior());

        var row = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };

        var checkLabel = new Label
        {
            Text = _selectedLanguage == code ? "✓" : " ",
            TextColor = Color.FromArgb("#FF4CAF50"),
            FontSize = 14,
            WidthRequest = 16,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        row.Children.Add(checkLabel);
        row.Children.Add(new Label
        {
            Text = label,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
        });

        border.Content = row;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnLanguageSelected(code);
        border.GestureRecognizers.Add(tap);

        return border;
    }

    private async void OnLanguageSelected(string code)
    {
        _selectedLanguage = code;
        Preferences.Default.Set("app_language", code);
        DismissSettingsDropdown();

        var langName = code == "es" ? "Español" : "English";
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Idioma", $"Idioma seleccionado: {langName}\nEl cambio se aplicará completamente al reiniciar la app.", "OK");
    }

    private async void OnSettingsStorageAction()
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Almacenamiento", "Gestión de almacenamiento (próximamente)", "OK");
    }

    private async void OnSettingsBackupAction()
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Datos y backup", "Export/import de datos y backups (próximamente)", "OK");
    }

    private async void OnSettingsAdminOrgAction()
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Administración", "Panel de administración de organización (próximamente)", "OK");
    }

    private async void OnSettingsAboutAction()
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        var version = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;
        var build = Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString;
        if (page != null)
            await page.DisplayAlert("Acerca de", $"CrownRFEP Reader\nVersión {version} ({build})", "OK");
    }

    private Page? GetParentPage()
    {
        Element? current = this;
        while (current != null)
        {
            if (current is Page page)
                return page;
            current = current.Parent;
        }
        return null;
    }

    // ─── Profile ──────────────────────────────────────────────────
    private async void OnProfileTapped(object sender, TappedEventArgs e)
    {
        try
        {
            await ShowProfileAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profile navigation error: {ex.Message}");
        }
    }

    private async Task ShowProfileAsync()
    {
        try
        {
            await RefreshProfileButtonTextAsync();

            var profilePage = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<UserProfilePage>();
            if (profilePage == null)
            {
                System.Diagnostics.Debug.WriteLine("ShowProfileAsync: profilePage is null");
                return;
            }

            // Asegurar que SIEMPRE se muestran los datos persistidos al abrir el modal.
            if (profilePage.BindingContext is UserProfileViewModel vm)
                await vm.LoadProfileAsync();

#if MACCATALYST
        var mauiContext = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext;
        if (mauiContext == null)
        {
            var nav = Shell.Current?.Navigation;
            if (nav != null)
                await nav.PushModalAsync(profilePage);
            return;
        }

        var root = UIApplication.SharedApplication
            .ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow)
            ?.RootViewController;

        if (root == null)
        {
            var nav = Shell.Current?.Navigation;
            if (nav != null)
                await nav.PushModalAsync(profilePage);
            return;
        }

        var presenter = root;
        while (presenter.PresentedViewController != null)
            presenter = presenter.PresentedViewController;

        var handler = profilePage.Handler ?? Microsoft.Maui.Platform.ElementExtensions.ToHandler(profilePage, mauiContext);
        if (handler is not PageHandler pageHandler)
        {
            var nav = Shell.Current?.Navigation;
            if (nav != null)
                await nav.PushModalAsync(profilePage);
            return;
        }

        var vc = pageHandler.ViewController;
        if (vc == null)
        {
            var nav = Shell.Current?.Navigation;
            if (nav != null)
                await nav.PushModalAsync(profilePage);
            return;
        }

        vc.ModalPresentationStyle = UIModalPresentationStyle.FormSheet;
        vc.PreferredContentSize = new CGSize(800, 900);

        await presenter.PresentViewControllerAsync(vc, true);
#else
        // Otras plataformas: navegación modal estándar de MAUI
        var nav = Shell.Current?.Navigation;
        if (nav != null)
            await nav.PushModalAsync(profilePage);
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowProfileAsync error: {ex.Message}");
        }
    }
}
