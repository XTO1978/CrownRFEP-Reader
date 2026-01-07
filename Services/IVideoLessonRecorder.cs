namespace CrownRFEP_Reader.Services;

public interface IVideoLessonRecorder
{
    bool IsRecording { get; }

    Task<bool> EnsurePermissionsAsync(bool cameraEnabled, bool microphoneEnabled, CancellationToken cancellationToken = default);

    Task StartAsync(string outputFilePath, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
