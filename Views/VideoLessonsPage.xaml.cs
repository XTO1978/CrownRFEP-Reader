using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.ViewModels;

namespace CrownRFEP_Reader.Views;

public partial class VideoLessonsPage : ContentPage
{
    private readonly VideoLessonsViewModel _viewModel;

    public VideoLessonsPage(VideoLessonsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnLessonTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not VideoLesson lesson)
            return;

        if (string.IsNullOrWhiteSpace(lesson.FilePath) || !File.Exists(lesson.FilePath))
        {
            await DisplayAlert("Archivo no encontrado", "No se encontró el vídeo de esta videolección en el dispositivo.", "OK");
            return;
        }

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Videolección",
            File = new ShareFile(lesson.FilePath)
        });
    }
}
