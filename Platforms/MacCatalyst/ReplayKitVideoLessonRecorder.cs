#if MACCATALYST
using AVFoundation;
using AudioToolbox;
using Foundation;
using ReplayKit;
using CrownRFEP_Reader.Services;
using CoreMedia;
using System.Runtime.InteropServices;
using Plugin.Maui.Audio;
using CoreFoundation;

namespace CrownRFEP_Reader.Platforms.MacCatalyst;

public sealed class ReplayKitVideoLessonRecorder : IVideoLessonRecorder
{
    private const bool UsePluginMicCapture = false; // Preferimos AVCaptureSession dedicada; plugin como último fallback manual
    private string? _currentOutputPath;
    private string? _currentMicPath;
    private AVAudioRecorder? _micRecorder;
    private AVAudioEngine? _micEngine;
    private AVAudioFile? _micEngineFile;
    private int _micEngineBuffersWritten;
    private float _micEngineLastPeak;
    private int _micEngineBuffersSincePeakLog;
    private System.Timers.Timer? _micMeterTimer;
    
    // Para captura de mic via StartCapture
    private FileStream? _micPcmStream;
    private int _micSampleBuffersReceived;
    private float _micCaptureLastPeak;
    private bool _isCapturingMicViaSampleBuffers;
    
    // Para captura de mic via AVCaptureSession (alternativa más directa)
    private AVCaptureSession? _captureSession;
    private AVCaptureAudioDataOutput? _audioDataOutput;
    private AVAssetWriter? _avAudioWriter;
    private AVAssetWriterInput? _avAudioWriterInput;
    private DispatchQueue? _avAudioQueue;
    private bool _isAvCaptureMicActive;
    
    // Para captura de mic via Plugin.Maui.Audio
    private IAudioRecorder? _pluginAudioRecorder;

    private bool _cameraEnabled = true;
    private bool _microphoneEnabled = true;

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
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), "Permiso de micrófono denegado: la videolección se grabaría sin audio.");
                throw new InvalidOperationException("Permiso de micrófono denegado. Actívalo en Ajustes/Privacidad (Micrófono) y reintenta.");
            }

            // Warm-up del micrófono: forzar activación del hardware antes de grabar
            await WarmUpMicrophoneAsync().ConfigureAwait(false);
        }

        TryConfigureAudioSessionForRecording();

        var recorder = RPScreenRecorder.SharedRecorder;

        if (recorder.Recording)
            throw new InvalidOperationException("ReplayKit ya está grabando. Detén la grabación actual antes de iniciar otra.");

        // Desactivamos el mic de ReplayKit (silencioso en Catalyst) y capturamos en paralelo
        recorder.MicrophoneEnabled = false;
        recorder.CameraEnabled = _cameraEnabled;
        
        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"StartAsync: MicrophoneEnabled={recorder.MicrophoneEnabled}, CameraEnabled={recorder.CameraEnabled}");

        _currentOutputPath = outputFilePath;
        _currentMicPath = null;
        _isCapturingMicViaSampleBuffers = false;
        _micSampleBuffersReceived = 0;
        _micCaptureLastPeak = 0f;

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? FileSystem.AppDataDirectory);

        if (File.Exists(outputFilePath))
            File.Delete(outputFilePath);

        // Si queremos fallback a Plugin/AVAudioEngine, activarlo aquí.
        if (_microphoneEnabled && UsePluginMicCapture)
        {
            try
            {
                var dir = Path.GetDirectoryName(outputFilePath) ?? FileSystem.AppDataDirectory;
                var baseName = Path.GetFileNameWithoutExtension(outputFilePath);
                var micPath = Path.Combine(dir, $"{baseName}_mic.pcm");
                _currentMicPath = micPath;

                TryDeleteFile(micPath);

                await StartMicCaptureViaSampleBuffersAsync(micPath);
            }
            catch (Exception ex)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"No se pudo iniciar captura de mic vía StartCapture: {ex.Message}");
                _currentMicPath = null;
                _isCapturingMicViaSampleBuffers = false;
            }
        }
        else if (_microphoneEnabled)
        {
            // Captura dedicada de audio mediante AVCaptureSession escribiendo PCM
            try
            {
                var dir = Path.GetDirectoryName(outputFilePath) ?? FileSystem.AppDataDirectory;
                var baseName = Path.GetFileNameWithoutExtension(outputFilePath);
                var micPath = Path.Combine(dir, $"{baseName}_mic.pcm");
                _currentMicPath = micPath;

                TryDeleteFile(micPath);

                var started = await StartMicCaptureViaAvCaptureSessionAsync(micPath).ConfigureAwait(false);
                if (!started)
                {
                    AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), "AVCaptureSession mic start failed; mic audio will be absent.");
                    _currentMicPath = null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"No se pudo iniciar captura de mic con AVCaptureSession: {ex.Message}");
                _currentMicPath = null;
            }
        }

        var tcs = new TaskCompletionSource();

        recorder.StartRecording(error =>
        {
            if (error != null)
                tcs.SetException(new NSErrorException(error));
            else
                tcs.SetResult();
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
                // Nota: no hay cancelación real del prompt; el token solo cancela el await.
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

        if (string.IsNullOrWhiteSpace(_currentOutputPath))
        {
            tcs.SetException(new InvalidOperationException("No hay ruta de salida para la grabación."));
            return tcs.Task;
        }

        // Primero detenemos el micrófono paralelo para flush del archivo.
        StopMicrophoneRecording();
        StopMicCaptureViaSampleBuffers();

        var outputPath = _currentOutputPath;
        var micPath = _currentMicPath;

        var url = NSUrl.FromFilename(outputPath);
        recorder.StopRecording(url, async error =>
        {
            if (error != null)
            {
                tcs.SetException(new NSErrorException(error));
                return;
            }

            try
            {
                // Si tenemos micro grabado, lo muxeamos sobre el MP4 generado.
                if (_microphoneEnabled && IsUsableFile(micPath) && File.Exists(outputPath))
                {
                    AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"StopAsync: mic file usable ({new FileInfo(micPath!).Length} bytes), proceeding to mux");
                    await TryMuxMicIntoVideoAsync(outputPath, micPath!, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"StopAsync: mic file NOT usable or missing | micPath='{micPath}' | exists={micPath != null && File.Exists(micPath)} | size={(micPath != null && File.Exists(micPath) ? new FileInfo(micPath).Length : 0)}");
                }
            }
            catch (Exception ex)
            {
                // No hacemos fallar StopAsync si el mux falla: preferimos conservar el video.
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"Mux de micrófono falló (se conserva el vídeo): {ex.Message}");
            }
            finally
            {
                // Limpieza de temporal de mic
                // Si el mic sigue siendo silencioso, conservamos el archivo para inspección.
                try
                {
                    var micPeak = _isCapturingMicViaSampleBuffers ? _micCaptureLastPeak : _micEngineLastPeak;
                    var keepMic = micPeak <= 0.0001f;
                    if (keepMic)
                    {
                        AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"StopAsync: mic parece silencioso (peak={micPeak:F6}); se conserva el archivo | micPath='{micPath}'");
                    }
                    else
                    {
                        TryDeleteFile(micPath);
                    }
                }
                catch
                {
                    TryDeleteFile(micPath);
                }
                _currentMicPath = null;
                _isCapturingMicViaSampleBuffers = false;

                // IMPORTANTE: Tras grabar con PlayAndRecord, restaurar la sesión a Playback.
                // Si no, al reproducir vídeos el sistema puede reactivar el input y mostrar
                // el icono naranja del micrófono en macOS.
                TryConfigureAudioSessionForPlayback();
                tcs.SetResult();
            }
        });

        return tcs.Task;
    }

    private static void TryConfigureAudioSessionForRecording()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            NSError? error;

            // Desactivar primero para forzar reinicio limpio del hardware de audio
            session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out error);

            // Preferimos el micro interno y un modo pensado para vídeo.
            var inputs = session.AvailableInputs;
            var builtinMic = inputs?.FirstOrDefault(i => i.PortType == AVAudioSession.PortBuiltInMic);

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"AudioSession: availableInputs={inputs?.Length ?? 0} | builtinMic={(builtinMic != null ? builtinMic.PortName : "null")}");

            // PlayAndRecord + VideoRecording da acceso estable al micro.
            // AllowBluetooth permite usar AirPods si están conectados.
            session.SetCategory(
                AVAudioSessionCategory.PlayAndRecord, 
                AVAudioSessionCategoryOptions.DefaultToSpeaker | AVAudioSessionCategoryOptions.AllowBluetooth, 
                out error);
            if (error != null)
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"AudioSession SetCategory error: {error.LocalizedDescription}");

            session.SetMode(AVAudioSession.ModeVideoRecording, out error);
            if (error != null)
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"AudioSession SetMode error: {error.LocalizedDescription}");

            // Ajustar muestreo y buffer para recibir frames de audio.
            session.SetPreferredSampleRate(44100, out error);
            session.SetPreferredIOBufferDuration(0.005, out error); // Buffer más pequeño para activación rápida

            if (builtinMic != null)
            {
                session.SetPreferredInput(builtinMic, out error);
                if (error != null)
                    AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"AudioSession SetPreferredInput error: {error.LocalizedDescription}");
            }

            // Activar la sesión - esto debería "despertar" el hardware
            session.SetActive(true, out error);
            if (error != null)
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"AudioSession SetActive error: {error.LocalizedDescription}");

            // Log estado final
            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), 
                $"AudioSession configured: category={session.Category} | mode={session.Mode} | " +
                $"inputAvailable={session.InputAvailable} | currentInput={session.CurrentRoute?.Inputs?.FirstOrDefault()?.PortName ?? "none"}");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"TryConfigureAudioSessionForRecording exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Fuerza la activación del hardware del micrófono usando AudioQueue (CoreAudio).
    /// Este es el mismo mecanismo que usa Preferencias del Sistema para mostrar el medidor de nivel.
    /// </summary>
    private static async Task WarmUpMicrophoneAsync()
    {
        try
        {
            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "WarmUpMicrophone: starting mic hardware warm-up via AudioQueue...");

            // 1) Primero activar AVAudioSession
            var session = AVAudioSession.SharedInstance();
            NSError? error;
            session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out error);
            session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker | AVAudioSessionCategoryOptions.AllowBluetooth, out error);
            session.SetMode(AVAudioSession.ModeVideoRecording, out error);
            session.SetActive(true, out error);

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"WarmUpMicrophone: AVAudioSession activated | inputAvailable={session.InputAvailable}");

            // 2) Usar AudioQueue para abrir el micrófono igual que Preferencias del Sistema
            var warmUpCompleted = await WarmUpWithAudioQueueAsync().ConfigureAwait(false);
            
            if (!warmUpCompleted)
            {
                // Fallback a AVCaptureSession si AudioQueue falla
                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "WarmUpMicrophone: AudioQueue failed, trying AVCaptureSession fallback...");
                await WarmUpWithAVCaptureSessionAsync().ConfigureAwait(false);
            }

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "WarmUpMicrophone: warm-up completed");
        }
        catch (Exception ex)
        {
            AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"WarmUpMicrophone failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Warm-up usando InputAudioQueue (CoreAudio bajo nivel) - mismo mecanismo que Preferencias del Sistema.
    /// </summary>
    private static async Task<bool> WarmUpWithAudioQueueAsync()
    {
        try
        {
            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "WarmUpWithInputAudioQueue: starting...");

            // Formato de audio: PCM 16-bit mono 44100 Hz
            var audioFormat = new AudioStreamBasicDescription
            {
                SampleRate = 44100,
                Format = AudioFormatType.LinearPCM,
                FormatFlags = AudioFormatFlags.LinearPCMIsSignedInteger | AudioFormatFlags.LinearPCMIsPacked,
                BitsPerChannel = 16,
                ChannelsPerFrame = 1,
                BytesPerFrame = 2,
                FramesPerPacket = 1,
                BytesPerPacket = 2
            };

            var samplesReceived = 0;
            float peakLevel = 0f;
            var bufferSize = 4096;

            // Crear InputAudioQueue con callback
            using var inputQueue = new InputAudioQueue(audioFormat);
            
            inputQueue.InputCompleted += (sender, e) =>
            {
                try
                {
                    samplesReceived++;
                    
                    // Calcular peak del buffer
                    unsafe
                    {
                        var dataPtr = (short*)e.IntPtrBuffer;
                        var sampleCount = bufferSize / 2; // 16-bit = 2 bytes per sample
                        
                        for (int i = 0; i < sampleCount; i++)
                        {
                            var sample = Math.Abs(dataPtr[i]) / 32768f;
                            if (sample > peakLevel)
                                peakLevel = sample;
                        }
                    }

                    // Re-encolar el buffer
                    inputQueue.EnqueueBuffer(e.IntPtrBuffer, bufferSize, e.PacketDescriptions);
                }
                catch
                {
                    // Ignorar errores en callback
                }
            };

            // Alocar y encolar buffers
            const int numBuffers = 3;

            for (int i = 0; i < numBuffers; i++)
            {
                inputQueue.AllocateBuffer(bufferSize, out IntPtr bufferPtr);
                inputQueue.EnqueueBuffer(bufferPtr, bufferSize, null);
            }

            // Iniciar captura
            var startStatus = inputQueue.Start();
            if (startStatus != AudioQueueStatus.Ok)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"WarmUpWithInputAudioQueue: Start failed | status={startStatus}");
                return false;
            }

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "WarmUpWithInputAudioQueue: queue started, capturing for 500ms...");

            // Capturar durante 500ms para activar el hardware completamente
            await Task.Delay(500).ConfigureAwait(false);

            // Detener
            inputQueue.Stop(true);

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"WarmUpWithInputAudioQueue: completed | samplesReceived={samplesReceived} | peakLevel={peakLevel:F4}");

            return samplesReceived > 0 || peakLevel > 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"WarmUpWithInputAudioQueue exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fallback warm-up usando AVCaptureSession.
    /// </summary>
    private static async Task WarmUpWithAVCaptureSessionAsync()
    {
        try
        {
            var tempSession = new AVCaptureSession();
            tempSession.BeginConfiguration();

            var audioDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Audio);
            if (audioDevice == null)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), "WarmUpWithAVCaptureSession: no audio device found");
                return;
            }

            NSError? inputError;
            var audioInput = AVCaptureDeviceInput.FromDevice(audioDevice, out inputError);
            if (audioInput == null)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"WarmUpWithAVCaptureSession: failed to create input | error={inputError?.LocalizedDescription}");
                return;
            }

            if (tempSession.CanAddInput(audioInput))
                tempSession.AddInput(audioInput);

            var tempOutput = new AVCaptureAudioDataOutput();
            if (tempSession.CanAddOutput(tempOutput))
                tempSession.AddOutput(tempOutput);

            tempSession.CommitConfiguration();
            tempSession.StartRunning();

            await Task.Delay(300).ConfigureAwait(false);

            tempSession.StopRunning();
            tempSession.Dispose();
            tempOutput.Dispose();
            audioInput.Dispose();

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "WarmUpWithAVCaptureSession: completed");
        }
        catch (Exception ex)
        {
            AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"WarmUpWithAVCaptureSession exception: {ex.Message}");
        }
    }

    private static void TryConfigureAudioSessionForPlayback()
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            NSError? error;

            // Desactivar primero para soltar el input si estuviera en uso.
            session.SetActive(false, out error);

            // Playback: no requiere micrófono.
            session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.MixWithOthers, out error);
            session.SetActive(true, out error);
        }
        catch
        {
            // Best-effort.
        }
    }

    #region Captura de Mic vía StartCapture (Sample Buffers)

    /// <summary>
    /// Inicia captura dedicada de micrófono (orden de preferencia: AVCaptureSession -> Plugin -> AVAudioEngine).
    /// </summary>
    private async Task StartMicCaptureViaSampleBuffersAsync(string pcmOutputPath)
    {
        _isCapturingMicViaSampleBuffers = true;
        _micCaptureLastPeak = 0f;
        _currentMicPath = pcmOutputPath; // guardamos como PCM; luego convertiremos a WAV al muxear

        // 1) Intentar captura con AVCaptureSession (acceso más directo al dispositivo)
        if (await StartMicCaptureViaAvCaptureSessionAsync(pcmOutputPath).ConfigureAwait(false))
        {
            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "StartMicCapture: usando AVCaptureSession dedicada (PCM)");
            return;
        }

        // 2) Fallback a Plugin.Maui.Audio si está habilitado
        if (UsePluginMicCapture)
        {
            LogAudioSessionState("before Plugin.Maui.Audio start");

            try
            {
                var audioManager = AudioManager.Current;
                _pluginAudioRecorder = audioManager.CreateRecorder();

                var wavPath = Path.ChangeExtension(pcmOutputPath, ".wav");
                _currentMicPath = wavPath;

                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "StartMicCapture: starting Plugin.Maui.Audio recorder");

                await _pluginAudioRecorder.StartAsync(wavPath);

                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"StartMicCapture: Plugin.Maui.Audio started | path='{wavPath}'");
                return;
            }
            catch (Exception ex)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"Plugin.Maui.Audio start error: {ex.Message}");
                _pluginAudioRecorder = null;
            }
        }

        // 3) Último recurso: AVAudioEngine tap
        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "StartMicCapture: fallback a AVAudioEngine tap");
        await StartMicCaptureViaAVAudioEngineAsync(pcmOutputPath);
    }
    
    private async Task StartMicCaptureViaAVAudioEngineAsync(string pcmOutputPath)
    {
        _micPcmStream = new FileStream(pcmOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _micSampleBuffersReceived = 0;
        _currentMicPath = pcmOutputPath;
        
        try
        {
            var engine = new AVAudioEngine();
            var inputNode = engine.InputNode;
            var format = inputNode.GetBusOutputFormat(0);
            
            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"StartMicCapture: AVAudioEngine | format={format.SampleRate}Hz, {format.ChannelCount}ch");
            
            inputNode.InstallTapOnBus(0, 4096, format, (buffer, when) =>
            {
                try
                {
                    ProcessMicSampleBuffer(buffer);
                }
                catch (Exception ex)
                {
                    AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"ProcessMicSampleBuffer error: {ex.Message}");
                }
            });

            engine.Prepare();
            
            NSError? startError;
            engine.StartAndReturnError(out startError);
            if (startError != null)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"AVAudioEngine start error: {startError.LocalizedDescription}");
                _isCapturingMicViaSampleBuffers = false;
                _micPcmStream?.Dispose();
                _micPcmStream = null;
                return;
            }

            _micEngine = engine;
            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "StartMicCapture: AVAudioEngine started successfully");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"StartMicCapture exception: {ex.Message}");
            _isCapturingMicViaSampleBuffers = false;
            _micPcmStream?.Dispose();
            _micPcmStream = null;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Captura dedicada de audio usando AVCaptureSession y escribe PCM al archivo.
    /// </summary>
    private async Task<bool> StartMicCaptureViaAvCaptureSessionAsync(string pcmOutputPath)
    {
        _micPcmStream = new FileStream(pcmOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _micSampleBuffersReceived = 0;
        _micCaptureLastPeak = 0f;
        _isCapturingMicViaSampleBuffers = true;

        var started = await TryStartAVCaptureSessionAsync().ConfigureAwait(false);
        if (!started)
        {
            _micPcmStream?.Dispose();
            _micPcmStream = null;
            _isCapturingMicViaSampleBuffers = false;
        }

        return started;
    }
    
    private async Task<bool> TryStartAVCaptureSessionAsync()
    {
        _captureSession = new AVCaptureSession();
        _captureSession.BeginConfiguration();
        
        // Buscar dispositivo de audio (micrófono)
        var audioDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Audio);
        if (audioDevice == null)
        {
            AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), "AVCaptureSession: no audio device found");
            _captureSession.Dispose();
            _captureSession = null;
            return false;
        }
        
        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"AVCaptureSession: found audio device | name='{audioDevice.LocalizedName}' | uid='{audioDevice.UniqueID}'");
        
        // Crear input
        NSError? inputError;
        var audioInput = AVCaptureDeviceInput.FromDevice(audioDevice, out inputError);
        if (audioInput == null || inputError != null)
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"AVCaptureSession: input creation failed | error={inputError?.LocalizedDescription}");
            _captureSession.Dispose();
            _captureSession = null;
            return false;
        }
        
        if (!_captureSession.CanAddInput(audioInput))
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), "AVCaptureSession: cannot add audio input");
            audioInput.Dispose();
            _captureSession.Dispose();
            _captureSession = null;
            return false;
        }
        
        _captureSession.AddInput(audioInput);
        
        // Crear output
        _audioDataOutput = new AVCaptureAudioDataOutput();
        var queue = new CoreFoundation.DispatchQueue("micCaptureQueue");
        _audioDataOutput.SetSampleBufferDelegate(new AudioDataOutputDelegate(this), queue);
        
        if (!_captureSession.CanAddOutput(_audioDataOutput))
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), "AVCaptureSession: cannot add audio output");
            _audioDataOutput.Dispose();
            _audioDataOutput = null;
            _captureSession.Dispose();
            _captureSession = null;
            return false;
        }
        
        _captureSession.AddOutput(_audioDataOutput);
        _captureSession.CommitConfiguration();
        _captureSession.StartRunning();

        _isAvCaptureMicActive = true;
        
        return true;
    }
    
    private class AudioDataOutputDelegate : AVCaptureAudioDataOutputSampleBufferDelegate
    {
        private readonly ReplayKitVideoLessonRecorder _recorder;
        
        public AudioDataOutputDelegate(ReplayKitVideoLessonRecorder recorder)
        {
            _recorder = recorder;
        }
        
        public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            _recorder.ProcessCaptureSessionAudioBuffer(sampleBuffer);
        }
    }
    
    private void ProcessCaptureSessionAudioBuffer(CMSampleBuffer sampleBuffer)
    {
        if (sampleBuffer == null)
            return;
            
        _micSampleBuffersReceived++;
        
        try
        {
            var blockBuffer = sampleBuffer.GetDataBuffer();
            if (blockBuffer == null)
                return;
                
            var dataLength = (int)blockBuffer.DataLength;
            if (dataLength == 0)
                return;
            
            // Obtener puntero a los datos
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

            // Escribir PCM al archivo
            try
            {
                _micPcmStream?.Write(data, 0, data.Length);
            }
            catch
            {
                // best-effort
            }

            // Calcular peak (asumiendo PCM 16-bit)
            int sampleCount = data.Length / 2;
            float maxSample = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(data, i * 2);
                float normalized = Math.Abs(sample) / 32768f;
                if (normalized > maxSample)
                    maxSample = normalized;
            }

            if (maxSample > _micCaptureLastPeak)
                _micCaptureLastPeak = maxSample;
            
            // Log periódico
            if (_micSampleBuffersReceived % 50 == 0)
            {
                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"AVCaptureSession mic: buffers={_micSampleBuffersReceived} | peak={_micCaptureLastPeak:F4}");
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void ProcessMicSampleBuffer(AVAudioPcmBuffer buffer)
    {
        if (buffer == null || buffer.FrameLength == 0)
            return;

        _micSampleBuffersReceived++;

        // Obtener los datos float del buffer
        var floatData = buffer.FloatChannelData;
        if (floatData == IntPtr.Zero)
            return;

        int frameCount = (int)buffer.FrameLength;
        int channelCount = (int)buffer.Format.ChannelCount;

        // Leer los samples del primer canal
        float[] samples = new float[frameCount];
        Marshal.Copy(Marshal.ReadIntPtr(floatData), samples, 0, frameCount);

        // Calcular peak
        float maxSample = 0f;
        for (int i = 0; i < frameCount; i++)
        {
            float abs = Math.Abs(samples[i]);
            if (abs > maxSample) maxSample = abs;
        }

        if (maxSample > _micCaptureLastPeak)
            _micCaptureLastPeak = maxSample;

        // Convertir a PCM 16-bit y escribir
        byte[] pcmBytes = new byte[frameCount * 2];
        for (int i = 0; i < frameCount; i++)
        {
            short pcmSample = (short)(samples[i] * 32767f);
            pcmBytes[i * 2] = (byte)(pcmSample & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
        }

        try
        {
            _micPcmStream?.Write(pcmBytes, 0, pcmBytes.Length);
        }
        catch
        {
            // Best effort
        }

        // Log periódico
        if (_micSampleBuffersReceived % 100 == 0)
        {
            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"MicCapture: buffers={_micSampleBuffersReceived} | peak={_micCaptureLastPeak:F4}");
        }
    }

    private async void StopMicCaptureViaSampleBuffers()
    {
        if (!_isCapturingMicViaSampleBuffers)
            return;

        try
        {
            // Detener Plugin.Maui.Audio si está activo
            if (_pluginAudioRecorder != null)
            {
                try
                {
                    var audioSource = await _pluginAudioRecorder.StopAsync();
                    if (audioSource != null)
                    {
                        var duration = audioSource.GetAudioStream()?.Length ?? 0;
                        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"StopMicCapture: Plugin.Maui.Audio stopped | duration={duration} bytes");
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"Plugin.Maui.Audio stop error: {ex.Message}");
                }
                _pluginAudioRecorder = null;
            }
            
            // Detener AVCaptureSession si está activo
            if (_captureSession != null)
            {
                _captureSession.StopRunning();
                _captureSession.Dispose();
                _captureSession = null;
                _isAvCaptureMicActive = false;
            }
            
            if (_audioDataOutput != null)
            {
                _audioDataOutput.Dispose();
                _audioDataOutput = null;
            }
            
            // Detener AVAudioEngine si está activo
            if (_micEngine != null)
            {
                _micEngine.InputNode.RemoveTapOnBus(0);
                _micEngine.Stop();
                _micEngine.Dispose();
                _micEngine = null;
            }

            _micPcmStream?.Flush();
            _micPcmStream?.Dispose();
            _micPcmStream = null;

            _isCapturingMicViaSampleBuffers = false;

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"StopMicCapture: buffers={_micSampleBuffersReceived} | peak={_micCaptureLastPeak:F4}");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"StopMicCapture error: {ex.Message}");
        }
    }

    #endregion

    private async Task StartMicrophoneRecordingAsync(string outputPath)
    {
        try
        {
            StopMicrophoneRecording();

            // Diagnóstico previo: permisos/ruta/inputs (si grabamos silencio suele ser por permiso/ruta)
            LogAudioSessionState("before mic start");

            // IMPORTANTE: Activar sesión de audio ANTES de crear el recorder
            var session = AVAudioSession.SharedInstance();
            NSError? sessionError;
            
            // Usamos PlayAndRecord con DefaultToSpeaker para asegurar que el mic se active
            session.SetCategory(
                AVAudioSessionCategory.PlayAndRecord,
                AVAudioSessionCategoryOptions.DefaultToSpeaker | AVAudioSessionCategoryOptions.AllowBluetooth,
                out sessionError);
            
            if (sessionError != null)
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"SetCategory error (continuamos): {sessionError.LocalizedDescription}");
            
            session.SetActive(true, out sessionError);
            if (sessionError != null)
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"SetActive error (continuamos): {sessionError.LocalizedDescription}");

            // En algunos MacCatalyst, el input puede quedar sin enrutar; intentamos elegir un mic explícito.
            TrySelectPreferredMicInput(session);

            // Diagnóstico tras activar y seleccionar input
            LogAudioSessionState("after audio session activation");

            // Alternativa 1 (preferida): AVAudioEngine + tap
            // En algunos MacCatalyst, AVAudioRecorder puede quedarse en silencio constante (-120 dB)
            // aunque el permiso y la ruta sean correctos.
            try
            {
                await StartMicrophoneRecordingWithAudioEngineAsync(outputPath).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"AVAudioEngine mic capture falló, probando AVAudioRecorder: {ex.Message}");
                StopMicrophoneEngine();
            }

            var url = NSUrl.FromFilename(outputPath);
            NSError? error;

            // Usar formato CAF (Core Audio Format) con PCM - compatible con AVFoundation
            // CAF es nativo de Apple y funciona perfectamente con AVAssetExportSession
            var settings = new AudioSettings
            {
                Format = AudioFormatType.LinearPCM,
                SampleRate = 44100,
                NumberChannels = 1,
                LinearPcmBitDepth = 16,
                LinearPcmFloat = false,
                LinearPcmBigEndian = false
            };

            _micRecorder = AVAudioRecorder.Create(url, settings, out error);
            if (error != null)
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"AVAudioRecorder.Create error: {error.LocalizedDescription}");
                throw new NSErrorException(error);
            }

            if (_micRecorder == null)
                throw new InvalidOperationException("No se pudo crear AVAudioRecorder.");

            _micRecorder.MeteringEnabled = true;

            if (!_micRecorder.PrepareToRecord())
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorder), "PrepareToRecord devolvió false");
                throw new InvalidOperationException("PrepareToRecord devolvió false.");
            }

            if (!_micRecorder.Record())
            {
                AppLog.Error(nameof(ReplayKitVideoLessonRecorder), "Record devolvió false");
                throw new InvalidOperationException("Record devolvió false.");
            }

            // Verificar que realmente está grabando
            await Task.Delay(100);
            var isRecording = _micRecorder.Recording;

            try
            {
                _micRecorder.UpdateMeters();
                var avg = _micRecorder.AveragePower(0);
                var peak = _micRecorder.PeakPower(0);
                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"Mic paralelo iniciado | path='{outputPath}' | isRecording={isRecording} | avgPower={avg:F1}dB | peakPower={peak:F1}dB");

                if (avg <= -119.0f && peak <= -119.0f)
                    LogAudioSessionState("mic metering silent at start");
            }
            catch
            {
                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"Mic paralelo iniciado | path='{outputPath}' | isRecording={isRecording}");
            }

            // Log de nivel cada ~1s para detectar si estamos grabando silencio
            try
            {
                _micMeterTimer?.Stop();
                _micMeterTimer?.Dispose();

                _micMeterTimer = new System.Timers.Timer(1000);
                _micMeterTimer.AutoReset = true;
                _micMeterTimer.Elapsed += (_, __) =>
                {
                    try
                    {
                        if (_micRecorder == null || !_micRecorder.Recording)
                            return;
                        _micRecorder.UpdateMeters();
                        var avg = _micRecorder.AveragePower(0);
                        var peak = _micRecorder.PeakPower(0);
                        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"Mic level | avgPower={avg:F1}dB | peakPower={peak:F1}dB");

                        if (avg <= -119.0f && peak <= -119.0f)
                            LogAudioSessionState("mic metering still silent");
                    }
                    catch
                    {
                        // Best-effort
                    }
                };
                _micMeterTimer.Start();
            }
            catch
            {
                // Best-effort
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"StartMicrophoneRecording falló: {ex.Message}", ex);
            StopMicrophoneRecording();
            throw;
        }
    }

    private Task StartMicrophoneRecordingWithAudioEngineAsync(string outputPath)
    {
        try
        {
            StopMicrophoneEngine();

            var url = NSUrl.FromFilename(outputPath);

            var engine = new AVAudioEngine();
            var input = engine.InputNode;
            if (input == null)
                throw new InvalidOperationException("AVAudioEngine.InputNode es null");

            // Usar el formato nativo del input para evitar conversiones.
            var inputFormat = input.GetBusOutputFormat(0);
            if (inputFormat == null)
                throw new InvalidOperationException("No se pudo obtener el formato de entrada del mic");

            NSError? fileError;
            var settings = new AudioSettings
            {
                Format = AudioFormatType.LinearPCM,
                SampleRate = inputFormat.SampleRate,
                NumberChannels = (int)inputFormat.ChannelCount,
                LinearPcmBitDepth = 16,
                LinearPcmFloat = false,
                LinearPcmBigEndian = false
            };

            // AVAudioFile escribe un CAF/PCM válido para luego muxearlo como AVAsset.
            var file = new AVAudioFile(url, settings, out fileError);
            if (fileError != null)
                throw new NSErrorException(fileError);

            _micEngineBuffersWritten = 0;
            _micEngineLastPeak = 0f;
            _micEngineBuffersSincePeakLog = 0;

            input.InstallTapOnBus(0, 1024, inputFormat, (buffer, when) =>
            {
                try
                {
                    if (_micEngineFile == null)
                        return;

                    NSError? writeError;
                    _micEngineFile.WriteFromBuffer(buffer, out writeError);
                    if (writeError == null)
                        System.Threading.Interlocked.Increment(ref _micEngineBuffersWritten);

                    // Medición muy ligera de peak (0..1 aprox) para confirmar si hay señal.
                    // Si el peak se queda en ~0 constante, el mic está entregando ceros.
                    try
                    {
                        var pcm = buffer as AVAudioPcmBuffer;
                        if (pcm != null)
                        {
                            var frameLength = (int)pcm.FrameLength;
                            if (frameLength > 0)
                            {
                                // Preferimos Int16 si está disponible.
                                var int16ChannelsPtr = (IntPtr)pcm.Int16ChannelData;
                                if (int16ChannelsPtr != IntPtr.Zero)
                                {
                                    var channel0Ptr = Marshal.ReadIntPtr(int16ChannelsPtr);
                                    var count = Math.Min(frameLength, 1024);
                                    var samples = new short[count];
                                    Marshal.Copy(channel0Ptr, samples, 0, count);
                                    int peak = 0;
                                    for (int i = 0; i < count; i++)
                                    {
                                        int v = samples[i];
                                        if (v < 0) v = -v;
                                        if (v > peak) peak = v;
                                    }
                                    _micEngineLastPeak = peak / 32768f;
                                }
                                else
                                {
                                    // Si el input viene como Float32.
                                    var floatChannelsPtr = (IntPtr)pcm.FloatChannelData;
                                    if (floatChannelsPtr != IntPtr.Zero)
                                    {
                                        var channel0Ptr = Marshal.ReadIntPtr(floatChannelsPtr);
                                        var count = Math.Min(frameLength, 1024);
                                        var samples = new float[count];
                                        Marshal.Copy(channel0Ptr, samples, 0, count);
                                        float peak = 0f;
                                        for (int i = 0; i < count; i++)
                                        {
                                            var v = samples[i];
                                            if (v < 0) v = -v;
                                            if (v > peak) peak = v;
                                        }
                                        _micEngineLastPeak = peak;
                                    }
                                }
                            }
                        }

                        _micEngineBuffersSincePeakLog++;
                        if (_micEngineBuffersSincePeakLog >= 10)
                        {
                            _micEngineBuffersSincePeakLog = 0;
                            // Loguear ocasionalmente para ver si sube al hablar.
                            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"Mic (AVAudioEngine) peak={_micEngineLastPeak:F6}");
                        }
                    }
                    catch
                    {
                        // Best-effort
                    }
                }
                catch
                {
                    // Best-effort (no reventar el hilo de audio)
                }
            });

            engine.Prepare();

            NSError? startError;
            engine.StartAndReturnError(out startError);
            if (startError != null)
                throw new NSErrorException(startError);

            _micEngine = engine;
            _micEngineFile = file;

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"Mic (AVAudioEngine) iniciado | path='{outputPath}' | sampleRate={inputFormat.SampleRate:F0} | channels={inputFormat.ChannelCount}");

            // Reutilizamos el timer para dar señales de vida del engine
            try
            {
                _micMeterTimer?.Stop();
                _micMeterTimer?.Dispose();

                _micMeterTimer = new System.Timers.Timer(1000);
                _micMeterTimer.AutoReset = true;
                _micMeterTimer.Elapsed += (_, __) =>
                {
                    try
                    {
                        var written = System.Threading.Volatile.Read(ref _micEngineBuffersWritten);
                        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"Mic (AVAudioEngine) buffersWritten={written}");
                    }
                    catch
                    {
                        // Best-effort
                    }
                };
                _micMeterTimer.Start();
            }
            catch
            {
                // Best-effort
            }

            return Task.CompletedTask;
        }
        catch
        {
            StopMicrophoneEngine();
            throw;
        }
    }

    private void StopMicrophoneRecording()
    {
        try
        {
            try
            {
                _micMeterTimer?.Stop();
                _micMeterTimer?.Dispose();
            }
            catch
            {
                // Best-effort
            }

            _micMeterTimer = null;

            if (_micRecorder != null)
            {
                var wasRecording = _micRecorder.Recording;
                _micRecorder.Stop();
                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"Mic paralelo detenido | wasRecording={wasRecording}");
            }

            // Si estábamos con AVAudioEngine
            StopMicrophoneEngine();
        }
        catch (Exception ex)
        {
            AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"StopMicrophoneRecording.Stop error: {ex.Message}");
        }

        try
        {
            _micRecorder?.Dispose();
        }
        catch
        {
            // Best-effort
        }

        _micRecorder = null;

        // Asegurar que no se queda la sesión en PlayAndRecord tras parar el mic.
        TryConfigureAudioSessionForPlayback();
    }

    private void StopMicrophoneEngine()
    {
        try
        {
            var engine = _micEngine;
            if (engine != null)
            {
                try
                {
                    engine.InputNode?.RemoveTapOnBus(0);
                }
                catch
                {
                    // Best-effort
                }

                try
                {
                    engine.Stop();
                }
                catch
                {
                    // Best-effort
                }
            }
        }
        catch
        {
            // Best-effort
        }

        try
        {
            _micEngineFile?.Dispose();
        }
        catch
        {
            // Best-effort
        }

        try
        {
            _micEngine?.Dispose();
        }
        catch
        {
            // Best-effort
        }

        _micEngineFile = null;
        _micEngine = null;
    }

    private static void TrySelectPreferredMicInput(AVAudioSession session)
    {
        try
        {
            // En MacCatalyst, AvailableInputs puede ser null dependiendo del entorno.
            var inputs = session.AvailableInputs;
            if (inputs == null || inputs.Length == 0)
                return;

            // Preferimos un mic "built-in" si aparece; si no, el primero.
            AVAudioSessionPortDescription? chosen = null;
            foreach (var input in inputs)
            {
                var type = input?.PortType?.ToString() ?? string.Empty;
                if (type.Contains("Built", StringComparison.OrdinalIgnoreCase) && type.Contains("Mic", StringComparison.OrdinalIgnoreCase))
                {
                    chosen = input;
                    break;
                }
                if (type.Contains("Mic", StringComparison.OrdinalIgnoreCase))
                    chosen ??= input;
            }

            chosen ??= inputs[0];

            NSError? error;
            session.SetPreferredInput(chosen, out error);
            if (error != null)
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"SetPreferredInput error (continuamos): {error.LocalizedDescription}");
            else
                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"Preferred mic input set | portType='{chosen.PortType}' | name='{chosen.PortName}'");
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void LogAudioSessionState(string stage)
    {
        try
        {
            var session = AVAudioSession.SharedInstance();
            var category = session.Category?.ToString() ?? "";

            string permission;
            try
            {
                permission = session.RecordPermission.ToString();
            }
            catch
            {
                permission = "unknown";
            }

            string routeSummary;
            try
            {
                var route = session.CurrentRoute;
                var inputs = route?.Inputs?.Select(i => $"{i?.PortType}/{i?.PortName}").ToArray() ?? Array.Empty<string>();
                var outputs = route?.Outputs?.Select(o => $"{o?.PortType}/{o?.PortName}").ToArray() ?? Array.Empty<string>();
                routeSummary = $"inputs=[{string.Join("; ", inputs)}] outputs=[{string.Join("; ", outputs)}]";
            }
            catch
            {
                routeSummary = "route=unavailable";
            }

            string inputsSummary;
            try
            {
                var avail = session.AvailableInputs?.Select(i => $"{i?.PortType}/{i?.PortName}").ToArray() ?? Array.Empty<string>();
                inputsSummary = avail.Length == 0 ? "availableInputs=[]" : $"availableInputs=[{string.Join("; ", avail)}]";
            }
            catch
            {
                inputsSummary = "availableInputs=unavailable";
            }

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"AudioSession | stage='{stage}' | category='{category}' | inputAvailable={session.InputAvailable} | recordPermission={permission} | {inputsSummary} | {routeSummary}");
        }
        catch
        {
            // Best-effort.
        }
    }

    private static bool IsUsableFile(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            if (!File.Exists(path))
                return false;
            return new FileInfo(path).Length > 1024; // evita ficheros vacíos/cabecera
        }
        catch
        {
            return false;
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
    /// Convierte un archivo PCM raw a WAV con cabecera estándar.
    /// </summary>
    private static bool TryConvertPcmToWav(string pcmPath, string wavPath, int sampleRate, int channels, int bitsPerSample)
    {
        try
        {
            var pcmData = File.ReadAllBytes(pcmPath);
            if (pcmData.Length == 0)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), "TryConvertPcmToWav: PCM file is empty");
                return false;
            }

            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            int blockAlign = channels * (bitsPerSample / 8);

            using var fs = new FileStream(wavPath, FileMode.Create);
            using var bw = new BinaryWriter(fs);

            // RIFF header
            bw.Write(new char[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + pcmData.Length);  // ChunkSize
            bw.Write(new char[] { 'W', 'A', 'V', 'E' });

            // fmt subchunk
            bw.Write(new char[] { 'f', 'm', 't', ' ' });
            bw.Write(16);  // Subchunk1Size (16 for PCM)
            bw.Write((short)1);  // AudioFormat (1 = PCM)
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write((short)blockAlign);
            bw.Write((short)bitsPerSample);

            // data subchunk
            bw.Write(new char[] { 'd', 'a', 't', 'a' });
            bw.Write(pcmData.Length);
            bw.Write(pcmData);

            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"TryConvertPcmToWav: created WAV | pcmBytes={pcmData.Length} | duration={(float)pcmData.Length / byteRate:F2}s");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"TryConvertPcmToWav error: {ex.Message}");
            return false;
        }
    }

    private static async Task TryMuxMicIntoVideoAsync(string videoPath, string micPath, CancellationToken cancellationToken)
    {
        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync BEGIN | video='{videoPath}' | mic='{micPath}'");
        
        // Si el archivo de mic es PCM raw, convertirlo a WAV primero
        string actualMicPath = micPath;
        if (micPath.EndsWith(".pcm", StringComparison.OrdinalIgnoreCase))
        {
            var wavPath = Path.ChangeExtension(micPath, ".wav");
            if (TryConvertPcmToWav(micPath, wavPath, 44100, 1, 16))
            {
                actualMicPath = wavPath;
                AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync: converted PCM to WAV | size={new FileInfo(wavPath).Length}");
            }
            else
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), "TryMuxMicIntoVideoAsync: failed to convert PCM to WAV");
                return;
            }
        }
        
        // En MacCatalyst, ReplayKit típicamente NO incluye audio en su grabación.
        // Usamos SOLO el audio del micrófono como única pista de audio para asegurar
        // que el reproductor lo reproduzca (muchos reproductores solo leen la primera pista).
        var videoUrl = NSUrl.FromFilename(videoPath);
        var micUrl = NSUrl.FromFilename(actualMicPath);

        var videoAsset = AVAsset.FromUrl(videoUrl);
        var micAsset = AVAsset.FromUrl(micUrl);

        var videoTrack = videoAsset.TracksWithMediaType(AVMediaTypes.Video.GetConstant()!).FirstOrDefault();
        if (videoTrack == null)
        {
            AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), "TryMuxMicIntoVideoAsync: no video track found");
            return;
        }

        var allMicTracks = micAsset.TracksWithMediaType(AVMediaTypes.Audio.GetConstant()!);
        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync: mic file has {allMicTracks.Length} audio tracks");
        
        var micTrack = allMicTracks.FirstOrDefault();
        if (micTrack == null)
        {
            AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), "TryMuxMicIntoVideoAsync: no mic audio track found - check file format");
            // Intentar listar todos los tracks del archivo mic para diagnóstico
            var allTracks = micAsset.Tracks;
            foreach (var t in allTracks)
            {
                AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"  - Track found: mediaType={t.MediaType}");
            }
            return;
        }
        
        // Verificar si el video original ya tiene audio
        var existingAudioTrack = videoAsset.TracksWithMediaType(AVMediaTypes.Audio.GetConstant()!).FirstOrDefault();
        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), 
            $"TryMuxMicIntoVideoAsync: videoTrack OK | micTrack OK | micDuration={micAsset.Duration.Seconds:F2}s | hasExistingAudio={existingAudioTrack != null}");

        var composition = new AVMutableComposition();
        NSError? error;

        // 1) Video track
        var compVideo = composition.AddMutableTrack(AVMediaTypes.Video.GetConstant()!, 0);
        if (compVideo == null)
            return;

        compVideo.PreferredTransform = videoTrack.PreferredTransform;

        // En algunos casos, AVAsset.Duration puede venir como 0/indefinida hasta que el asset se carga.
        // La duración de la pista suele ser más fiable para rangos de inserción.
        var videoDuration = videoTrack.TimeRange.Duration;
        if (videoDuration.Seconds <= 0)
            videoDuration = videoAsset.Duration;

        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync: durations | videoDuration={videoDuration.Seconds:F2}s | micAssetDuration={micAsset.Duration.Seconds:F2}s");
        var videoRange = new CMTimeRange { Start = CMTime.Zero, Duration = videoDuration };
        compVideo.InsertTimeRange(videoRange, videoTrack, CMTime.Zero, out error);
        if (error != null)
            throw new NSErrorException(error);

        // 2) Audio: SOLO usamos el micrófono como única pista de audio
        //    Esto garantiza que el reproductor lo reproduzca correctamente
        var compAudio = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);
        if (compAudio == null)
            return;

        var micDuration = micTrack.TimeRange.Duration;
        if (micDuration.Seconds <= 0)
            micDuration = micAsset.Duration;

        // Mux "de atrás adelante": alineamos el FIN del audio con el FIN del vídeo.
        // Esto ayuda cuando el mic empieza antes (warmup) o cuando ReplayKit/AVCapture
        // entregan buffers iniciales silenciosos que desplazan el audio.
        var audioDuration = (micDuration.Seconds > 0 && micDuration.Seconds < videoDuration.Seconds)
            ? micDuration
            : videoDuration;

        // Rango fuente: últimos `audioDuration` segundos del mic
        var micTrackStart = micTrack.TimeRange.Start;
        var micTrackEnd = micTrack.TimeRange.Start + micDuration;
        var desiredMicStart = micTrackEnd - audioDuration;
        if (desiredMicStart < micTrackStart)
            desiredMicStart = micTrackStart;

        var audioSourceRange = new CMTimeRange { Start = desiredMicStart, Duration = audioDuration };

        // Posición destino: para que termine a la vez que el vídeo
        var insertAt = videoDuration - audioDuration;
        if (insertAt.Seconds < 0)
            insertAt = CMTime.Zero;

        AppLog.Info(nameof(ReplayKitVideoLessonRecorder),
            $"TryMuxMicIntoVideoAsync: inserting mic audio (align end) | micDuration={micDuration.Seconds:F2}s | videoDuration={videoDuration.Seconds:F2}s | insertDuration={audioDuration.Seconds:F2}s | sourceStart={audioSourceRange.Start.Seconds:F2}s | insertAt={insertAt.Seconds:F2}s");

        compAudio.InsertTimeRange(audioSourceRange, micTrack, insertAt, out error);
        if (error != null)
        {
            AppLog.Error(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync: InsertTimeRange error: {error.LocalizedDescription}");
            throw new NSErrorException(error);
        }
        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), "TryMuxMicIntoVideoAsync: mic audio inserted successfully");

        // AudioMix para asegurar volumen correcto (y elevar si el mic viene bajo)
        var micParams = AVMutableAudioMixInputParameters.FromTrack(compAudio);
        micParams.SetVolume(3f, CMTime.Zero);
        
        var audioMix = AVMutableAudioMix.Create();
        audioMix.InputParameters = new AVAudioMixInputParameters[] { micParams };

        var tempPath = Path.Combine(Path.GetDirectoryName(videoPath) ?? FileSystem.AppDataDirectory,
            $"{Path.GetFileNameWithoutExtension(videoPath)}_mux_tmp.mp4");
        TryDeleteFile(tempPath);

        var export = new AVAssetExportSession(composition, AVAssetExportSessionPreset.HighestQuality);
        export.OutputUrl = NSUrl.FromFilename(tempPath);
        export.OutputFileType = AVFileTypes.Mpeg4.GetConstant()!;
        export.ShouldOptimizeForNetworkUse = true;
        export.AudioMix = audioMix;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        export.ExportAsynchronously(() =>
        {
            if (export.Status == AVAssetExportSessionStatus.Completed)
                tcs.TrySetResult(true);
            else
                tcs.TrySetException(new InvalidOperationException(export.Error?.LocalizedDescription ?? $"Export failed: {export.Status}"));
        });

        await tcs.Task.ConfigureAwait(false);

        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync: export completed, replacing original");
        
        // Reemplazar vídeo original
        TryDeleteFile(videoPath);
        File.Move(tempPath, videoPath);

        // Probe rápido del MP4 final para confirmar pistas de audio
        try
        {
            using var finalAsset = AVAsset.FromUrl(NSUrl.FromFilename(videoPath));
            var finalAudioTracks = finalAsset.GetTracks(AVMediaTypes.Audio);
            AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync: final probe | audioTracks={finalAudioTracks?.Length ?? 0} | duration={finalAsset.Duration.Seconds:F2}s");
        }
        catch (Exception ex)
        {
            AppLog.Warn(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync: final probe failed: {ex.Message}");
        }
        
        AppLog.Info(nameof(ReplayKitVideoLessonRecorder), $"TryMuxMicIntoVideoAsync END | final size={new FileInfo(videoPath).Length}");
    }

    private static Task<bool> EnsureMicrophonePermissionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // En MacCatalyst 17+ Apple movió permisos a AVAudioApplication.
            // Si está disponible, lo usamos para evitar quedarnos grabando silencio.
            var audioAppType = Type.GetType("AVFoundation.AVAudioApplication, Microsoft.MacCatalyst");
            if (audioAppType != null)
            {
                var requestMethod = audioAppType.GetMethod("RequestRecordPermission", new[] { typeof(Action<bool>) });
                if (requestMethod != null)
                {
                    var appTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var appRegistration = cancellationToken.Register(() => appTcs.TrySetCanceled(cancellationToken));

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            requestMethod.Invoke(null, new object[] { new Action<bool>(granted => appTcs.TrySetResult(granted)) });
                        }
                        catch
                        {
                            appTcs.TrySetResult(false);
                        }
                    });

                    return appTcs.Task;
                }
            }

            var session = AVAudioSession.SharedInstance();

            // En MacCatalyst, ReplayKit puede no disparar el prompt de mic automáticamente.
            // Si no pedimos el permiso, podemos acabar con MP4 sin pista de micro sin error explícito.
            var permission = session.RecordPermission;

            if (permission == AVAudioSessionRecordPermission.Granted)
                return Task.FromResult(true);

            if (permission == AVAudioSessionRecordPermission.Denied)
                return Task.FromResult(false);

            if (permission != AVAudioSessionRecordPermission.Undetermined)
                return Task.FromResult(false);

            var sessionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var sessionRegistration = cancellationToken.Register(() => sessionTcs.TrySetCanceled(cancellationToken));
            session.RequestRecordPermission(granted => sessionTcs.TrySetResult(granted));

            return sessionTcs.Task;
        }
        catch
        {
            // Si no podemos consultar permisos (API no disponible, etc.), evitamos grabaciones silenciosas.
            return Task.FromResult(false);
        }
    }
}
#endif
