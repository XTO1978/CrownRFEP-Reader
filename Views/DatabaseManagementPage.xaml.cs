using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class DatabaseManagementPage : ContentPage
{
    private readonly DatabaseManagementViewModel _viewModel;
    private bool _isFirstAppear = true;

    public DatabaseManagementPage(DatabaseManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        if (_isFirstAppear)
        {
            _isFirstAppear = false;
            // Pequeño delay para permitir que el layout se inicialice
            await Task.Delay(50);
        }
        
        await _viewModel.LoadAllDataAsync();
    }
}
