using CrownRFEP_Reader.ViewModels;
#if MACCATALYST
using System.Linq;
using UIKit;
#endif

namespace CrownRFEP_Reader.Views;

public partial class UserProfilePage : ContentPage
{
    public UserProfilePage(UserProfileViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        if (BindingContext is UserProfileViewModel vm)
        {
            await vm.LoadProfileAsync();
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
#if MACCATALYST
        // Si se presentó con UIKit (FormSheet), no siempre está en el stack de Navigation de MAUI.
        var root = UIApplication.SharedApplication
            .ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow)
            ?.RootViewController;
        if (root != null)
        {
            var presenter = root;
            while (presenter.PresentedViewController != null)
                presenter = presenter.PresentedViewController;

            // Si hay algo presentado, cerrarlo.
            if (presenter != root)
            {
                await presenter.DismissViewControllerAsync(true);
                return;
            }
        }
#endif

        await Navigation.PopModalAsync();
    }
}
