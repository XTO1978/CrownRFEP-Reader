using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadDataAsync();
    }

    private void OnDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is GestureRecognizer gestureRecognizer && 
            gestureRecognizer.Parent is View view && 
            view.BindingContext is VideoClip video)
        {
            e.Data.Properties["VideoClip"] = video;
        }
    }

    private void OnDropScreen1(object? sender, DropEventArgs e)
    {
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            _viewModel.ParallelVideo1 = video;
        }
    }

    private void OnDropScreen2(object? sender, DropEventArgs e)
    {
        if (e.Data.Properties.TryGetValue("VideoClip", out var data) && data is VideoClip video)
        {
            _viewModel.ParallelVideo2 = video;
        }
    }
}
