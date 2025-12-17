using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class AthleteDetailPage : ContentPage
{
    public AthleteDetailPage(AthleteDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
