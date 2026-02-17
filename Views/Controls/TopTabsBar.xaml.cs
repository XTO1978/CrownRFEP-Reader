using Microsoft.Maui.Controls;
using System.Linq;
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
    
    // ─── Settings dropdown ───────────────────────────────────────
    private void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        var isOpen = SettingsDropdownPanel.IsVisible;
        SettingsDropdownPanel.IsVisible = !isOpen;
        SettingsDismissOverlay.IsVisible = !isOpen;
    }

    private void DismissSettingsDropdown()
    {
        SettingsDropdownPanel.IsVisible = false;
        SettingsDismissOverlay.IsVisible = false;
    }

    private void OnSettingsDismissTapped(object? sender, TappedEventArgs e) => DismissSettingsDropdown();

    private async void OnSettingsThemeTapped(object? sender, TappedEventArgs e)
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Tema", "Cambio de tema claro/oscuro (próximamente)", "OK");
    }

    private async void OnSettingsLanguageTapped(object? sender, TappedEventArgs e)
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Idioma", "Selección de idioma (próximamente)", "OK");
    }

    private async void OnSettingsStorageTapped(object? sender, TappedEventArgs e)
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Almacenamiento", "Gestión de almacenamiento (próximamente)", "OK");
    }

    private async void OnSettingsBackupTapped(object? sender, TappedEventArgs e)
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Datos y backup", "Export/import de datos y backups (próximamente)", "OK");
    }

    private async void OnSettingsAdminOrgTapped(object? sender, TappedEventArgs e)
    {
        DismissSettingsDropdown();
        var page = GetParentPage();
        if (page != null)
            await page.DisplayAlert("Administración", "Panel de administración de organización (próximamente)", "OK");
    }

    private async void OnSettingsAboutTapped(object? sender, TappedEventArgs e)
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
