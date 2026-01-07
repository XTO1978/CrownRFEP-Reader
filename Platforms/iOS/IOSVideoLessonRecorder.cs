#if IOS
using AVFoundation;
using AudioToolbox;
using CoreMedia;
using CoreVideo;
using Foundation;
using ReplayKit;
using CrownRFEP_Reader.Services;
using System.Runtime.InteropServices;

namespace CrownRFEP_Reader.Platforms.iOS;

/// <summary>
/// Grabador de videolecciones para iOS usando ReplayKit.
/// Usa StartCapture para obtener sample buffers y escribirlos a un MP4.
/// </summary>
public sealed class IOSVideoLessonRecorder : IVideoLessonRecorder
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
                AppLog.Warn(nameof(IOSVideoLessonRecorder), "Permiso de micrófono denegado.");
                throw new InvalidOperationException("Permiso de micrófono denegado. Actívalo en Ajustes/Privacidad (Micrófono) y reintenta.");
            }
        }

        TryConfigureAudioSessionForRecording();

        var recorder = RPScreenRecorder.SharedRecorder;

        if (recorder.Recording)
            throw new InvalidOperationException("ReplayKit ya está grabando.");

        if (!recorder.Available)
            throw new InvalidOperationException("La grabación de pantalla no está disponible en este dispositivo.");

        recorder.MicrophoneEnabled = _microphoneEnabled;
        recorder.CameraEnabled = _cameraEnabled;

        _currentOutputPath = outputFilePath;
        _sessionStarted = false;
        _videoFramesWritten = 0;
        _audioSamplesWritten = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? FileSystem.AppDataDirectory);
        TryDeleteFile(outputFilePath);

        var tcs = new TaskCompletionSource();

        AppLog.Info(nameof(IOSVideoLessonRecorder), $"StartAsync: MicrophoneEnabled={recorder.MicrophoneEnabled}, CameraEnabled={recorder.CameraEnabled}, Path={outputFilePath}");

        recorder.StartCapture((sampleBuffer, sampleBufferType, error) =>
        {
            if (error != null)
            {
                AppLog.Error(nameof(IOSVideoLessonRecorder), $"StartCapture handler error: {error.LocalizedDescription}");
                return;
            }

            try
            {
                ProcessSampleBuffer(sampleBuffer, sampleBufferType);
            }
            catch (Exception ex)
            {
                AppLog.Error(nameof(IOSVideoLessonRecorder), $"ProcessSampleBuffer exception: {ex.Message}");
            }
        }, microphoneError =>
        {
            if (microphoneError != null)
            {
                tcs.TrySetException(new NSErrorException(microphoneError));
            }
            else
            {
                AppLog.Info(nameof(IOSVideoLessonRecorder), "StartCapture iniciado correctamente");
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
                AppLog.Error(nameof(IOSVideoLessonRecorder), $"StopCapture error: {error.LocalizedDescription}");
            }

            try
            {
                await FinishWritingAsync().ConfigureAwait(false);
                AppLog.Info(nameof(IOSVideoLessonRecorder), $"StopAsync completado | videoFrames={_videoFramesWritten} | audioSamples={_audioSamplesWritten} | path='{_currentOutputPath}'");
            }
            catch (Exception ex)
            {
                AppLog.Error(nameof(IOSVideoLessonRecorder), $"FinishWriting error: {ex.Message}");
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

        // Pre-validación sin lock para evitar contención innecesaria
        if (_assetWriter == null && sampleBufferType != RPSampleBufferType.Video)
            return;

        lock (_writerLock)
        {
            // Lazy init del writer con el primer buffer de video
            if (_assetWriter == null && sampleBufferType == RPSampleBufferType.Video)
            {
                AppLog.Info(nameof(IOSVideoLessonRecorder), $"First video buffer received, initializing AssetWriter");
                InitializeAssetWriter(sampleBuffer);
            }

            if (_assetWriter == null)
                return;

            // Iniciar sesión con el primer timestamp
            if (!_sessionStarted)
            {
                var pts = sampleBuffer.PresentationTimeStamp;
                if (pts.Seconds >= 0)
                {
                    _assetWriter.StartSessionAtSourceTime(pts);
                    _sessionStarted = true;
                    AppLog.Info(nameof(IOSVideoLessonRecorder), $"Session started at {pts.Seconds:F3}s");
                }
            }

            if (!_sessionStarted)
                return;

            switch (sampleBufferType)
            {
                case RPSampleBufferType.Video:
                    ProcessVideoBuffer(sampleBuffer);
                    break;

                case RPSampleBufferType.AudioMic:
                    ProcessAudioBuffer(sampleBuffer);
                    break;
            }
        }
    }

    private void ProcessVideoBuffer(CMSampleBuffer sampleBuffer)
    {
        if (_videoInput == null || _pixelBufferAdaptor == null)
            return;

        if (!_videoInput.ReadyForMoreMediaData)
            return;

        var imageBuffer = sampleBuffer.GetImageBuffer();
        if (imageBuffer is not CVPixelBuffer pixelBuffer)
            return;

        var presentationTime = sampleBuffer.PresentationTimeStamp;
        bool appended = _pixelBufferAdaptor.AppendPixelBufferWithPresentationTime(pixelBuffer, presentationTime);
        
        if (appended)
        {
            _videoFramesWritten++;
            if (_videoFramesWritten == 1)
                AppLog.Info(nameof(IOSVideoLessonRecorder), "First video frame written successfully via PixelBufferAdaptor");
        }
        else if (_videoFramesWritten == 0)
        {
            AppLog.Warn(nameof(IOSVideoLessonRecorder), $"Failed to append pixel buffer | WriterStatus={_assetWriter?.Status}");
        }
    }

    private void ProcessAudioBuffer(CMSampleBuffer sampleBuffer)
    {
        if (_audioInput == null || !_audioInput.ReadyForMoreMediaData)
            return;

        if (_audioInput.AppendSampleBuffer(sampleBuffer))
            _audioSamplesWritten++;
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
                AppLog.Error(nameof(IOSVideoLessonRecorder), $"AVAssetWriter init error: {writerError.LocalizedDescription}");
                _assetWriter = null;
                return;
            }

            // Obtener formato del video sample
            var formatDesc = videoSample.GetVideoFormatDescription();
            var dimensions = formatDesc?.Dimensions ?? new CMVideoDimensions { Width = 1920, Height = 1080 };
            
            // Escalar resolución para mejor rendimiento (máximo 1080p)
            int maxDimension = 1920;
            int width = dimensions.Width;
            int height = dimensions.Height;
            
            if (width > maxDimension || height > maxDimension)
            {
                float scale = (float)maxDimension / Math.Max(width, height);
                width = (int)(width * scale);
                height = (int)(height * scale);
                // Asegurar que sean múltiplos de 2 para codificación
                width = width - (width % 2);
                height = height - (height % 2);
            }
            
            AppLog.Info(nameof(IOSVideoLessonRecorder), $"Video dimensions: {dimensions.Width}x{dimensions.Height} -> {width}x{height}");

            // Crear video settings con compresión optimizada para rendimiento
            var compressionProperties = new NSDictionary(
                AVVideo.AverageBitRateKey, new NSNumber(4_000_000), // 4 Mbps
                AVVideo.ExpectedSourceFrameRateKey, new NSNumber(30),
                AVVideo.ProfileLevelKey, AVVideo.ProfileLevelH264HighAutoLevel
            );
            
            var videoOutputSettings = new NSDictionary(
                AVVideo.CodecKey, AVVideoCodecType.H264.GetConstant(),
                AVVideo.WidthKey, new NSNumber(width),
                AVVideo.HeightKey, new NSNumber(height),
                AVVideo.CompressionPropertiesKey, compressionProperties
            );

            // Crear video input usando NSObject factory
            _videoInput = CreateVideoWriterInput(AVMediaTypes.Video.GetConstant()!, videoOutputSettings);
            
            if (_videoInput == null)
            {
                AppLog.Error(nameof(IOSVideoLessonRecorder), "Failed to create video input");
                return;
            }
            _videoInput.ExpectsMediaDataInRealTime = true;
            
            // Crear pixel buffer adaptor para mayor compatibilidad
            var sourcePixelBufferAttributes = new NSDictionary(
                CVPixelBuffer.PixelFormatTypeKey, new NSNumber((int)CVPixelFormatType.CV32BGRA)
            );
            _pixelBufferAdaptor = new AVAssetWriterInputPixelBufferAdaptor(_videoInput, sourcePixelBufferAttributes);

            if (_assetWriter.CanAddInput(_videoInput))
            {
                _assetWriter.AddInput(_videoInput);
                AppLog.Info(nameof(IOSVideoLessonRecorder), "Video input added to writer");
            }
            else
            {
                AppLog.Error(nameof(IOSVideoLessonRecorder), "Cannot add video input to writer");
            }

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
            
            if (_assetWriter.Status != AVAssetWriterStatus.Writing)
            {
                AppLog.Error(nameof(IOSVideoLessonRecorder), $"AssetWriter failed to start writing | Status={_assetWriter.Status} | Error={_assetWriter.Error?.LocalizedDescription ?? "none"}");
            }
            else
            {
                AppLog.Info(nameof(IOSVideoLessonRecorder), $"AssetWriter initialized and writing | hasMicInput={_audioInput != null}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSVideoLessonRecorder), $"InitializeAssetWriter exception: {ex.Message}");
            _assetWriter = null;
        }
    }

    private async Task FinishWritingAsync()
    {
        AVAssetWriter? writer;
        string? outputPath;
        lock (_writerLock)
        {
            writer = _assetWriter;
            outputPath = _currentOutputPath;
            _assetWriter = null;
        }

        AppLog.Info(nameof(IOSVideoLessonRecorder), $"FinishWritingAsync | writer={(writer != null ? "present" : "null")} | videoFrames={_videoFramesWritten} | audioSamples={_audioSamplesWritten}");

        if (writer == null)
        {
            AppLog.Warn(nameof(IOSVideoLessonRecorder), "FinishWritingAsync: No AssetWriter to finish - no video frames were received");
            return;
        }

        try
        {
            _videoInput?.MarkAsFinished();
            _audioInput?.MarkAsFinished();

            var tcs = new TaskCompletionSource();
            writer.FinishWriting(() => tcs.TrySetResult());
            await tcs.Task.ConfigureAwait(false);

            if (writer.Status == AVAssetWriterStatus.Failed)
            {
                AppLog.Error(nameof(IOSVideoLessonRecorder), $"AssetWriter failed: {writer.Error?.LocalizedDescription}");
            }
            else if (writer.Status == AVAssetWriterStatus.Completed)
            {
                // Verificar que el archivo existe
                if (!string.IsNullOrEmpty(outputPath) && File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    AppLog.Info(nameof(IOSVideoLessonRecorder), $"FinishWritingAsync: File created successfully | size={fileInfo.Length} bytes | path='{outputPath}'");
                }
                else
                {
                    AppLog.Warn(nameof(IOSVideoLessonRecorder), $"FinishWritingAsync: AssetWriter completed but file not found at '{outputPath}'");
                }
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

    private static void TryConfigureAudioSessionForRecording()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            NSError? error;

            session.SetCategory(
                AVAudioSessionCategory.PlayAndRecord,
                AVAudioSessionCategoryOptions.DefaultToSpeaker | 
                AVAudioSessionCategoryOptions.AllowBluetooth |
                AVAudioSessionCategoryOptions.MixWithOthers,
                out error);

            if (error != null)
            {
                AppLog.Warn(nameof(IOSVideoLessonRecorder), $"SetCategory error: {error.LocalizedDescription}");
            }

            session.SetActive(true, out error);
            if (error != null)
            {
                AppLog.Warn(nameof(IOSVideoLessonRecorder), $"SetActive error: {error.LocalizedDescription}");
            }

            AppLog.Info(nameof(IOSVideoLessonRecorder), $"AudioSession configured: Category={session.Category}, Mode={session.Mode}");
        }
        catch (Exception ex)
        {
            AppLog.Warn(nameof(IOSVideoLessonRecorder), $"TryConfigureAudioSessionForRecording exception: {ex.Message}");
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

    /// <summary>
    /// Crea un AVAssetWriterInput para video usando P/Invoke directo.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    private static AVAssetWriterInput? CreateVideoWriterInput(string mediaType, NSDictionary outputSettings)
    {
        try
        {
            // Obtener clase AVAssetWriterInput
            var classHandle = ObjCRuntime.Class.GetHandle("AVAssetWriterInput");
            if (classHandle == IntPtr.Zero)
                return null;

            // alloc
            var allocSel = ObjCRuntime.Selector.GetHandle("alloc");
            var instance = objc_msgSend(classHandle, allocSel);
            if (instance == IntPtr.Zero)
                return null;

            // initWithMediaType:outputSettings:
            var initSel = ObjCRuntime.Selector.GetHandle("initWithMediaType:outputSettings:");
            using var mediaTypeString = new NSString(mediaType);
            instance = objc_msgSend_IntPtr_IntPtr(instance, initSel, mediaTypeString.Handle, outputSettings.Handle);
            
            if (instance == IntPtr.Zero)
                return null;

            return ObjCRuntime.Runtime.GetNSObject<AVAssetWriterInput>(instance);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSVideoLessonRecorder), $"CreateVideoWriterInput failed: {ex.Message}");
            return null;
        }
    }
}
#endif
