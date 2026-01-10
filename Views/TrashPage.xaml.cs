using CrownRFEP_Reader.ViewModels;
using System.Threading.Tasks;

namespace CrownRFEP_Reader.Views;

public partial class TrashPage : ContentPage
{
    private readonly TrashViewModel _viewModel;
    private bool _isFirstAppear = true;

    public TrashPage(TrashViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_isFirstAppear)
        {
            _isFirstAppear = false;
            await Task.Delay(50);
        }

        await _viewModel.LoadAsync();
    }
}
