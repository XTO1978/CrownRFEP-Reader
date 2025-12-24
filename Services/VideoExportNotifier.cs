namespace CrownRFEP_Reader.Services;

/// <summary>
/// Notificador para informar cuando se exporta un video nuevo
/// </summary>
public class VideoExportNotifier
{
    /// <summary>
    /// Evento que se dispara cuando se exporta un video
    /// </summary>
    public event EventHandler<VideoExportedEventArgs>? VideoExported;

    /// <summary>
    /// Notifica que se ha exportado un video
    /// </summary>
    public void NotifyVideoExported(int sessionId, int videoClipId)
    {
        VideoExported?.Invoke(this, new VideoExportedEventArgs(sessionId, videoClipId));
    }
}

/// <summary>
/// Argumentos del evento de video exportado
/// </summary>
public class VideoExportedEventArgs : EventArgs
{
    public int SessionId { get; }
    public int VideoClipId { get; }

    public VideoExportedEventArgs(int sessionId, int videoClipId)
    {
        SessionId = sessionId;
        VideoClipId = videoClipId;
    }
}
