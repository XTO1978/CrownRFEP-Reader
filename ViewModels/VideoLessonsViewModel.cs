using System.Collections.ObjectModel;
using System.Windows.Input;
using CrownRFEP_Reader.Models;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.ViewModels;

public sealed class VideoLessonsViewModel : BaseViewModel
{
    private readonly DatabaseService _databaseService;
    private readonly VideoLessonNotifier _videoLessonNotifier;

    public ObservableCollection<VideoLesson> Lessons { get; } = new();

    public ICommand RefreshCommand { get; }

    public VideoLessonsViewModel(DatabaseService databaseService, VideoLessonNotifier videoLessonNotifier)
    {
        _databaseService = databaseService;
        _videoLessonNotifier = videoLessonNotifier;
        Title = "Videolecciones";
        RefreshCommand = new AsyncRelayCommand(LoadAsync);

        // Suscribirse a notificaciones de nuevas videolecciones
        _videoLessonNotifier.VideoLessonCreated += OnVideoLessonCreated;
    }

    private async void OnVideoLessonCreated(object? sender, VideoLessonCreatedEventArgs e)
    {
        // Recargar la lista en el hilo principal
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await LoadAsync();
        });
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            var lessons = await _databaseService.GetAllVideoLessonsAsync();
            var sessions = await _databaseService.GetAllSessionsAsync();
            var sessionNameById = sessions.ToDictionary(s => s.Id, s => s.DisplayName);

            foreach (var lesson in lessons)
            {
                if (sessionNameById.TryGetValue(lesson.SessionId, out var name))
                    lesson.SessionDisplayName = name;
            }

            Lessons.Clear();
            foreach (var lesson in lessons)
                Lessons.Add(lesson);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"No se pudieron cargar las videolecciones: {ex.Message}", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
