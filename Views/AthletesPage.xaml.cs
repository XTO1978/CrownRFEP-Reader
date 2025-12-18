using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class AthletesPage : ContentPage
{
    private readonly AthletesViewModel _viewModel;

    public AthletesPage(AthletesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAthletesAsync();
    }

    private void OnAthleteCheckChanged(object? sender, CheckedChangedEventArgs e)
    {
        _viewModel.UpdateSelectedCount();
    }
}
