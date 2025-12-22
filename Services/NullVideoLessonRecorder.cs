namespace CrownRFEP_Reader.Services;

public sealed class NullVideoLessonRecorder : IVideoLessonRecorder
{
    public bool IsRecording => false;

    public Task StartAsync(string outputFilePath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("La grabación de videolecciones no está soportada en esta plataforma.");

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
