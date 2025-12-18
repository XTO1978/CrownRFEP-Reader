using Microsoft.Maui.Controls;
using System.Linq;
#if MACCATALYST
using CoreGraphics;
using Microsoft.Maui.Handlers;
using UIKit;
#endif

namespace CrownRFEP_Reader.Views.Controls;

public partial class TopTabsBar : Microsoft.Maui.Controls.ContentView
{
    public TopTabsBar()
    {
        InitializeComponent();
    }

    private static Task GoToRootAsync(string rootRoute)
    {
        // Navegación absoluta al root de cada sección.
        return Shell.Current.GoToAsync($"//{rootRoute}");
    }

    private async void OnDashboardClicked(object sender, EventArgs e) => await GoToRootAsync("dashboard");
    private async void OnSessionsClicked(object sender, EventArgs e) => await GoToRootAsync("sessions");
    private async void OnAthletesClicked(object sender, EventArgs e) => await GoToRootAsync("athletes");
    private async void OnStatsClicked(object sender, EventArgs e) => await GoToRootAsync("stats");
    private async void OnImportClicked(object sender, EventArgs e) => await GoToRootAsync("import");
    
    private async void OnProfileClicked(object sender, EventArgs e)
    {
        var profilePage = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<UserProfilePage>();
        if (profilePage == null) return;

        // Asegurar que SIEMPRE se muestran los datos persistidos al abrir el modal.
        if (profilePage.BindingContext is CrownRFEP_Reader.ViewModels.UserProfileViewModel vm)
            await vm.LoadProfileAsync();

#if MACCATALYST
        var mauiContext = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext;
        if (mauiContext == null)
        {
            await Shell.Current.Navigation.PushModalAsync(profilePage);
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
            await Shell.Current.Navigation.PushModalAsync(profilePage);
            return;
        }

        var presenter = root;
        while (presenter.PresentedViewController != null)
            presenter = presenter.PresentedViewController;

        var handler = profilePage.Handler ?? Microsoft.Maui.Platform.ElementExtensions.ToHandler(profilePage, mauiContext);
        if (handler is not PageHandler pageHandler)
        {
            await Shell.Current.Navigation.PushModalAsync(profilePage);
            return;
        }

        var vc = pageHandler.ViewController;
        if (vc == null)
        {
            await Shell.Current.Navigation.PushModalAsync(profilePage);
            return;
        }

        vc.ModalPresentationStyle = UIModalPresentationStyle.FormSheet;
        vc.PreferredContentSize = new CGSize(800, 900);

        await presenter.PresentViewControllerAsync(vc, true);
#else
        // Otras plataformas: navegación modal estándar de MAUI
        await Shell.Current.Navigation.PushModalAsync(profilePage);
#endif
    }
}
