using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class SessionDetailPage : ContentPage
{
    public SessionDetailPage(SessionDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
