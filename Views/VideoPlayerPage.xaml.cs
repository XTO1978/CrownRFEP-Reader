using CrownRFEP_Reader.ViewModels;

#if MACCATALYST
using CrownRFEP_Reader.Platforms.MacCatalyst;
#endif

namespace CrownRFEP_Reader.Views;

public partial class VideoPlayerPage : ContentPage
{
    private readonly VideoPlayerViewModel _viewModel;

    public VideoPlayerPage(VideoPlayerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

#if MACCATALYST
        KeyPressHandler.SpaceBarPressed += OnSpaceBarPressed;
        KeyPressHandler.ArrowLeftPressed += OnArrowLeftPressed;
        KeyPressHandler.ArrowRightPressed += OnArrowRightPressed;
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

#if MACCATALYST
        KeyPressHandler.SpaceBarPressed -= OnSpaceBarPressed;
        KeyPressHandler.ArrowLeftPressed -= OnArrowLeftPressed;
        KeyPressHandler.ArrowRightPressed -= OnArrowRightPressed;
#endif

        // Detener el video cuando se sale de la página
        mediaElement.Stop();
    }

#if MACCATALYST
    private void OnSpaceBarPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.PlayPauseCommand.Execute(null);
        });
    }

    private void OnArrowLeftPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Retroceder ~1 frame (aproximadamente 33ms para 30fps)
            var newPosition = mediaElement.Position - TimeSpan.FromMilliseconds(33);
            if (newPosition < TimeSpan.Zero)
                newPosition = TimeSpan.Zero;
            mediaElement.SeekTo(newPosition);
        });
    }

    private void OnArrowRightPressed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Avanzar ~1 frame (aproximadamente 33ms para 30fps)
            var newPosition = mediaElement.Position + TimeSpan.FromMilliseconds(33);
            if (newPosition > mediaElement.Duration)
                newPosition = mediaElement.Duration;
            mediaElement.SeekTo(newPosition);
        });
    }
#endif
}
