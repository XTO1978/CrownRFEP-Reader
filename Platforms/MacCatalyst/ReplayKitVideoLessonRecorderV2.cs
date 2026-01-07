#if MACCATALYST
using AVFoundation;
using AudioToolbox;
using CoreMedia;
using CoreVideo;
using Foundation;
using ReplayKit;
using CrownRFEP_Reader.Services;
using System.Runtime.InteropServices;

namespace CrownRFEP_Reader.Platforms.MacCatalyst;

/// <summary>
/// Grabador de videolecciones usando RPScreenRecorder.StartCapture para obtener
/// sample buffers de video y micrófono por separado, y luego muxearlos en un MP4.
/// Esta aproximación evita el problema de MacCatalyst donde AVAudioRecorder/AVAudioEngine
/// reciben ceros del micrófono aunque el permiso esté concedido.
/// </summary>
public sealed class ReplayKitVideoLessonRecorderV2 : IVideoLessonRecorder
{
    private string? _currentOutputPath;
    private bool _cameraEnabled = true;
    private bool _microphoneEnabled = true;

    private AVAssetWriter? _assetWriter;
    private AVAssetWriterInput? _videoInput;
    private AVAssetWriterInput? _audioInput;
    private AVAssetWriterInputPixelBufferAdaptor? _pixelBufferAdaptor;

    private bool _sessionStarted;
    private readonly object _writerLock = new();
    private int _videoFramesWritten;
    private int _audioSamplesWritten;
    private float _audioPeakLevel;

    public bool IsRecording => RPScreenRecorder.SharedRecorder.Recording;

    public async Task<bool> EnsurePermissionsAsync(bool cameraEnabled, bool microphoneEnabled, CancellationToken cancellationToken = default)
    {
        if (microphoneEnabled)
        {
            var micGranted = await EnsureMicrophonePermissionAsync(cancellationToken).ConfigureAwait(false);
            if (!micGranted)
                return false;
        }

        if (cameraEnabled)
        {
            var cameraGranted = await EnsureCameraPermissionAsync(cancellationToken).ConfigureAwait(false);
            if (!cameraGranted)
                return false;
        }

        return true;
    }

    public void SetOptions(bool cameraEnabled, bool microphoneEnabled)
    {
        _cameraEnabled = cameraEnabled;
        _microphoneEnabled = microphoneEnabled;

        var recorder = RPScreenRecorder.SharedRecorder;
        recorder.CameraEnabled = cameraEnabled;
        recorder.MicrophoneEnabled = microphoneEnabled;
    }

    public async Task StartAsync(string outputFilePath, CancellationToken cancellationToken = default)
    {
        if (_microphoneEnabled)
        {
            var micGranted = await EnsureMicrophonePermissionAsync(cancellationToken).ConfigureAwait(false);
            if (!micGranted)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorderV2), "Permiso de micrófono denegado.");
                throw new InvalidOperationException("Permiso de micrófono denegado. Actívalo en Ajustes/Privacidad (Micrófono) y reintenta.");
            }
        }

        var recorder = RPScreenRecorder.SharedRecorder;

        if (recorder.Recording)
            throw new InvalidOperationException("ReplayKit ya está grabando.");

        // Habilitar micrófono en ReplayKit para que StartCapture proporcione samples de mic
        recorder.MicrophoneEnabled = _microphoneEnabled;
        recorder.CameraEnabled = _cameraEnabled;

        _currentOutputPath = outputFilePath;
        _sessionStarted = false;
        _videoFramesWritten = 0;
        _audioSamplesWritten = 0;
        _audioPeakLevel = 0f;

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? FileSystem.AppDataDirectory);
        TryDeleteFile(outputFilePath);

        var tcs = new TaskCompletionSource();

        recorder.StartCapture((sampleBuffer, sampleBufferType, error) =>
        {
            if (error != null)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorderV2), $"StartCapture handler error: {error.LocalizedDescription}");
                return;
            }

            try
            {
                ProcessSampleBuffer(sampleBuffer, sampleBufferType);
            }
            catch (Exception ex)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorderV2), $"ProcessSampleBuffer exception: {ex.Message}");
            }
        }, microphoneError =>
        {
            if (microphoneError != null)
            {
                tcs.TrySetException(new NSErrorException(microphoneError));
            }
            else
            {
                AppLog.Info(nameof(ReplayKitVideoLessonRecorderV2), "StartCapture iniciado correctamente");
                tcs.TrySetResult();
            }
        });

        await tcs.Task.ConfigureAwait(false);
    }

    private static Task<bool> EnsureCameraPermissionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
            if (status == AVAuthorizationStatus.Authorized)
                return Task.FromResult(true);

            if (status == AVAuthorizationStatus.Denied || status == AVAuthorizationStatus.Restricted)
                return Task.FromResult(false);

            if (status == AVAuthorizationStatus.NotDetermined)
            {
                return AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video)
                    .WaitAsync(cancellationToken);
            }

            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
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

        recorder.StopCapture(async error =>
        {
            if (error != null)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorderV2), $"StopCapture error: {error.LocalizedDescription}");
            }

            try
            {
                await FinishWritingAsync().ConfigureAwait(false);
                AppLog.Info(nameof(ReplayKitVideoLessonRecorderV2), $"StopAsync completado | videoFrames={_videoFramesWritten} | audioSamples={_audioSamplesWritten} | audioPeak={_audioPeakLevel:F6} | path='{_currentOutputPath}'");
            }
            catch (Exception ex)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorderV2), $"FinishWriting error: {ex.Message}");
            }

            _currentOutputPath = null;
            tcs.TrySetResult();
        });

        return tcs.Task;
    }

    private void ProcessSampleBuffer(CMSampleBuffer sampleBuffer, RPSampleBufferType sampleBufferType)
    {
        if (sampleBuffer == null)
            return;

        lock (_writerLock)
        {
            // Lazy init del writer con el primer buffer de video (para obtener dimensiones)
            if (_assetWriter == null && sampleBufferType == RPSampleBufferType.Video)
            {
                InitializeAssetWriter(sampleBuffer);
            }

            if (_assetWriter == null)
                return;

            // Iniciar sesión con el primer timestamp
            if (!_sessionStarted)
            {
                var pts = sampleBuffer.PresentationTimeStamp;
                // CMTime no tiene IsValid en .NET, validamos que Seconds > 0 o sea un time válido
                if (pts.Seconds >= 0)
                {
                    _assetWriter.StartSessionAtSourceTime(pts);
                    _sessionStarted = true;
                    AppLog.Info(nameof(ReplayKitVideoLessonRecorderV2), $"Session started at {pts.Seconds:F3}s");
                }
            }

            if (!_sessionStarted)
                return;

            switch (sampleBufferType)
            {
                case RPSampleBufferType.Video:
                    if (_videoInput != null && _videoInput.ReadyForMoreMediaData)
                    {
                        if (_videoInput.AppendSampleBuffer(sampleBuffer))
                            _videoFramesWritten++;
                    }
                    break;

                case RPSampleBufferType.AudioMic:
                    if (_audioInput != null && _audioInput.ReadyForMoreMediaData)
                    {
                        // Calcular peak para diagnóstico
                        UpdateAudioPeak(sampleBuffer);

                        if (_audioInput.AppendSampleBuffer(sampleBuffer))
                            _audioSamplesWritten++;
                    }
                    break;

                // Ignoramos AudioApp (audio del sistema)
            }
        }
    }

    private void InitializeAssetWriter(CMSampleBuffer videoSample)
    {
        try
        {
            var outputUrl = NSUrl.FromFilename(_currentOutputPath!);
            NSError? writerError;
            _assetWriter = new AVAssetWriter(outputUrl, AVFileTypes.Mpeg4.GetConstant()!, out writerError);
            if (writerError != null)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorderV2), $"AVAssetWriter init error: {writerError.LocalizedDescription}");
                _assetWriter = null;
                return;
            }

            // Video input - usamos null para que use codec por defecto
            var formatDesc = videoSample.GetVideoFormatDescription();
            var dimensions = formatDesc?.Dimensions ?? new CMVideoDimensions { Width = 1920, Height = 1080 };

            _videoInput = AVAssetWriterInput.Create(AVMediaTypes.Video.GetConstant()!, (AudioSettings?)null);
            _videoInput.ExpectsMediaDataInRealTime = true;

            if (_assetWriter.CanAddInput(_videoInput))
                _assetWriter.AddInput(_videoInput);

            // Audio input (mic)
            if (_microphoneEnabled)
            {
                var audioSettings = new AudioSettings
                {
                    Format = AudioFormatType.MPEG4AAC,
                    SampleRate = 44100,
                    NumberChannels = 1,
                    EncoderBitRate = 128000
                };

                _audioInput = AVAssetWriterInput.Create(AVMediaTypes.Audio.GetConstant()!, audioSettings);
                _audioInput.ExpectsMediaDataInRealTime = true;

                if (_assetWriter.CanAddInput(_audioInput))
                    _assetWriter.AddInput(_audioInput);
            }

            _assetWriter.StartWriting();
            AppLog.Info(nameof(ReplayKitVideoLessonRecorderV2), $"AssetWriter initialized | dimensions={dimensions.Width}x{dimensions.Height} | hasMicInput={_audioInput != null}");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorderV2), $"InitializeAssetWriter exception: {ex.Message}");
            _assetWriter = null;
        }
    }

    private void UpdateAudioPeak(CMSampleBuffer audioSample)
    {
        try
        {
            var blockBuffer = audioSample.GetDataBuffer();
            if (blockBuffer == null)
                return;

            var dataLength = (int)blockBuffer.DataLength;
            if (dataLength == 0)
                return;

            // Obtener puntero a los datos directamente
            nuint offset = 0;
            nuint lengthAtOffset = 0;
            nuint totalLength = 0;
            IntPtr dataPointer = IntPtr.Zero;

            var status = blockBuffer.GetDataPointer(offset, out lengthAtOffset, out totalLength, ref dataPointer);
            if (status != CMBlockBufferError.None || dataPointer == IntPtr.Zero || totalLength == 0)
                return;

            // Copiar a array manejado
            var data = new byte[(int)totalLength];
            Marshal.Copy(dataPointer, data, 0, (int)totalLength);

            // Asumimos PCM 16-bit signed
            int sampleCount = data.Length / 2;
            float maxSample = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(data, i * 2);
                float normalized = Math.Abs(sample) / 32768f;
                if (normalized > maxSample)
                    maxSample = normalized;
            }

            if (maxSample > _audioPeakLevel)
                _audioPeakLevel = maxSample;

            // Log periódico
            if (_audioSamplesWritten > 0 && _audioSamplesWritten % 100 == 0)
            {
                AppLog.Info(nameof(ReplayKitVideoLessonRecorderV2), $"AudioMic samples={_audioSamplesWritten} | peak={_audioPeakLevel:F4}");
            }
        }
        catch
        {
            // Best-effort
        }
    }

    private async Task FinishWritingAsync()
    {
        AVAssetWriter? writer;
        lock (_writerLock)
        {
            writer = _assetWriter;
            _assetWriter = null;
        }

        if (writer == null)
            return;

        try
        {
            _videoInput?.MarkAsFinished();
            _audioInput?.MarkAsFinished();

            var tcs = new TaskCompletionSource();
            writer.FinishWriting(() => tcs.TrySetResult());
            await tcs.Task.ConfigureAwait(false);

            if (writer.Status == AVAssetWriterStatus.Failed)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorderV2), $"AssetWriter failed: {writer.Error?.LocalizedDescription}");
            }
        }
        finally
        {
            _videoInput?.Dispose();
            _audioInput?.Dispose();
            writer.Dispose();

            _videoInput = null;
            _audioInput = null;
        }
    }

    private static Task<bool> EnsureMicrophonePermissionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            var permission = session.RecordPermission;

            if (permission == AVAudioSessionRecordPermission.Granted)
                return Task.FromResult(true);

            if (permission == AVAudioSessionRecordPermission.Denied)
                return Task.FromResult(false);

            if (permission != AVAudioSessionRecordPermission.Undetermined)
                return Task.FromResult(false);

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var _ = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            session.RequestRecordPermission(granted => tcs.TrySetResult(granted));

            return tcs.Task;
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort
        }
    }
}
#endif
