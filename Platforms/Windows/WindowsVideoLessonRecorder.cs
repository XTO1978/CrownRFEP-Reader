#if WINDOWS
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Platforms.Windows;

public sealed class WindowsVideoLessonRecorder : IVideoLessonRecorder
{
    public bool IsRecording => false;

    public Task StartAsync(string outputFilePath, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("La grabación de pantalla en Windows se implementará en una iteración posterior.");

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
#endif
