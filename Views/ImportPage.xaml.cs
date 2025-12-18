using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class ImportPage : ContentPage
{
    public ImportPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
