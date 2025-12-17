using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class VideoPlayerPage : ContentPage
{
    public VideoPlayerPage(VideoPlayerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Detener el video cuando se sale de la página
        mediaElement.Stop();
    }
}
