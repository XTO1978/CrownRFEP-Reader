namespace CrownRFEP_Reader.Services;

public interface IVideoLessonRecorder
{
    bool IsRecording { get; }

    Task StartAsync(string outputFilePath, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
