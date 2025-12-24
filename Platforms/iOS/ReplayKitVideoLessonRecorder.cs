#if IOS
using Foundation;
using ReplayKit;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Platforms.iOS;

public sealed class ReplayKitVideoLessonRecorder : IVideoLessonRecorder
{
    private string? _currentOutputPath;

    private bool _cameraEnabled = true;
    private bool _microphoneEnabled = true;

    public bool IsRecording => RPScreenRecorder.SharedRecorder.Recording;

    public void SetOptions(bool cameraEnabled, bool microphoneEnabled)
    {
        _cameraEnabled = cameraEnabled;
        _microphoneEnabled = microphoneEnabled;

        var recorder = RPScreenRecorder.SharedRecorder;
        recorder.CameraEnabled = cameraEnabled;
        recorder.MicrophoneEnabled = microphoneEnabled;
    }

    public Task StartAsync(string outputFilePath, CancellationToken cancellationToken = default)
    {
        var recorder = RPScreenRecorder.SharedRecorder;

        if (recorder.Recording)
            return Task.FromException(new InvalidOperationException("ReplayKit ya está grabando. Detén la grabación actual antes de iniciar otra."));

        recorder.MicrophoneEnabled = _microphoneEnabled;
        recorder.CameraEnabled = _cameraEnabled;

        _currentOutputPath = outputFilePath;

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? FileSystem.AppDataDirectory);

        if (File.Exists(outputFilePath))
            File.Delete(outputFilePath);

        var tcs = new TaskCompletionSource();

        recorder.StartRecording(error =>
        {
            if (error != null)
                tcs.SetException(new NSErrorException(error));
            else
                tcs.SetResult();
        });

        return tcs.Task;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        var recorder = RPScreenRecorder.SharedRecorder;

        var tcs = new TaskCompletionSource();

        if (!recorder.Recording)
        {
            _currentOutputPath = null;
            tcs.SetResult();
            return tcs.Task;
        }

        if (string.IsNullOrWhiteSpace(_currentOutputPath))
        {
            tcs.SetException(new InvalidOperationException("No hay ruta de salida para la grabación."));
            return tcs.Task;
        }

        var url = NSUrl.FromFilename(_currentOutputPath);
        recorder.StopRecording(url, error =>
        {
            if (error != null)
                tcs.SetException(new NSErrorException(error));
            else
                tcs.SetResult();
        });

        return tcs.Task;
    }
}
#endif
