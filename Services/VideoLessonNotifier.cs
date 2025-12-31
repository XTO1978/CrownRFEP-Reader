namespace CrownRFEP_Reader.Services;

/// <summary>
/// Notificador para informar cuando se crea o modifica una videolección
/// </summary>
public class VideoLessonNotifier
{
    /// <summary>
    /// Evento que se dispara cuando se crea una videolección
    /// </summary>
    public event EventHandler<VideoLessonCreatedEventArgs>? VideoLessonCreated;

    /// <summary>
    /// Notifica que se ha creado una videolección
    /// </summary>
    public void NotifyVideoLessonCreated(int videoLessonId, int sessionId)
    {
        VideoLessonCreated?.Invoke(this, new VideoLessonCreatedEventArgs(videoLessonId, sessionId));
    }
}

/// <summary>
/// Argumentos del evento de videolección creada
/// </summary>
public class VideoLessonCreatedEventArgs : EventArgs
{
    public int VideoLessonId { get; }
    public int SessionId { get; }

    public VideoLessonCreatedEventArgs(int videoLessonId, int sessionId)
    {
        VideoLessonId = videoLessonId;
        SessionId = sessionId;
    }
}
