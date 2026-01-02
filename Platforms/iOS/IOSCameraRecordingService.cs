#if IOS
using AVFoundation;
using CoreMedia;
using Foundation;
using UIKit;
using CrownRFEP_Reader.Services;

namespace CrownRFEP_Reader.Platforms.iOS;

/// <summary>
/// Servicio de cámara de alta precisión para iOS.
/// Usa AVCaptureSession directamente para máximo control sobre la grabación.
/// </summary>
public sealed class IOSCameraRecordingService : ICameraRecordingService, IDisposable
{
    private AVCaptureSession? _captureSession;
    private AVCaptureDevice? _currentDevice;
    private AVCaptureDeviceInput? _deviceInput;
    private AVCaptureMovieFileOutput? _movieOutput;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    private AVCaptureAudioDataOutput? _audioOutput;

    private string? _currentOutputPath;
    private bool _isRecording;
    private bool _isPreviewing;
    private bool _isUsingFrontCamera;
    private double _currentZoom = 1.0;
    private bool _disposed;

    private readonly object _sessionLock = new();
    private MovieFileOutputDelegate? _outputDelegate;

    public bool IsAvailable
    {
        get
        {
            try
            {
                var discovery = AVCaptureDeviceDiscoverySession.Create(
                    new[] { AVCaptureDeviceType.BuiltInWideAngleCamera },
                    AVMediaTypes.Video,
                    AVCaptureDevicePosition.Unspecified);
                return discovery.Devices.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsRecording => _isRecording;
    public bool IsPreviewing => _isPreviewing;
    public double CurrentZoom => _currentZoom;

    public double MinZoom => 1.0;
    public double MaxZoom => _currentDevice?.MaxAvailableVideoZoomFactor ?? 10.0;

    public event EventHandler<bool>? RecordingStateChanged;
    public event EventHandler<double>? ZoomChanged;

    public async Task<bool> StartPreviewAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Solicitar permiso de cámara
            var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
            if (status == AVAuthorizationStatus.NotDetermined)
            {
                var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
                if (!granted)
                {
                    AppLog.Warn(nameof(IOSCameraRecordingService), "Permiso de cámara denegado");
                    return false;
                }
            }
            else if (status != AVAuthorizationStatus.Authorized)
            {
                AppLog.Warn(nameof(IOSCameraRecordingService), $"Cámara no autorizada: {status}");
                return false;
            }

            // Solicitar permiso de micrófono
            var audioStatus = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Audio);
            if (audioStatus == AVAuthorizationStatus.NotDetermined)
            {
                await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Audio);
            }

            lock (_sessionLock)
            {
                if (_captureSession != null)
                {
                    _captureSession.StopRunning();
                    _captureSession.Dispose();
                }

                _captureSession = new AVCaptureSession();
                _captureSession.SessionPreset = AVCaptureSession.Preset1920x1080;

                // Configurar cámara trasera por defecto
                _currentDevice = GetCamera(AVCaptureDevicePosition.Back);
                if (_currentDevice == null)
                {
                    _currentDevice = GetCamera(AVCaptureDevicePosition.Front);
                    _isUsingFrontCamera = true;
                }

                if (_currentDevice == null)
                {
                    AppLog.Error(nameof(IOSCameraRecordingService), "No se encontró ninguna cámara");
                    return false;
                }

                // Configurar input de video
                NSError? error;
                _deviceInput = AVCaptureDeviceInput.FromDevice(_currentDevice, out error);
                if (error != null || _deviceInput == null)
                {
                    AppLog.Error(nameof(IOSCameraRecordingService), $"Error creando input de cámara: {error?.LocalizedDescription}");
                    return false;
                }

                if (_captureSession.CanAddInput(_deviceInput))
                {
                    _captureSession.AddInput(_deviceInput);
                }

                // Configurar input de audio
                var audioDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Audio);
                if (audioDevice != null)
                {
                    var audioInput = AVCaptureDeviceInput.FromDevice(audioDevice, out error);
                    if (audioInput != null && _captureSession.CanAddInput(audioInput))
                    {
                        _captureSession.AddInput(audioInput);
                    }
                }

                // Configurar output de video para grabación
                _movieOutput = new AVCaptureMovieFileOutput();
                _movieOutput.MovieFragmentInterval = CMTime.Invalid; // Sin fragmentación
                
                if (_captureSession.CanAddOutput(_movieOutput))
                {
                    _captureSession.AddOutput(_movieOutput);
                }

                // Configurar preview layer
                _previewLayer = new AVCaptureVideoPreviewLayer(_captureSession)
                {
                    VideoGravity = AVLayerVideoGravity.ResizeAspectFill
                };

                // Configurar orientación del preview layer
                if (_previewLayer.Connection != null && _previewLayer.Connection.SupportsVideoOrientation)
                {
                    _previewLayer.Connection.VideoOrientation = GetDeviceVideoOrientation();
                }

                // Iniciar sesión
                _captureSession.StartRunning();
                _isPreviewing = true;

                AppLog.Info(nameof(IOSCameraRecordingService), $"Preview iniciado. MaxZoom: {MaxZoom:F1}x");
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSCameraRecordingService), "Error iniciando preview", ex);
            return false;
        }
    }

    public Task StopPreviewAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_sessionLock)
            {
                if (_captureSession != null && _captureSession.Running)
                {
                    _captureSession.StopRunning();
                }
                _isPreviewing = false;
            }

            AppLog.Info(nameof(IOSCameraRecordingService), "Preview detenido");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSCameraRecordingService), "Error deteniendo preview", ex);
        }

        return Task.CompletedTask;
    }

    public Task StartRecordingAsync(string outputFilePath, CancellationToken cancellationToken = default)
    {
        if (_isRecording)
        {
            throw new InvalidOperationException("Ya hay una grabación en curso");
        }

        if (_movieOutput == null || _captureSession == null || !_captureSession.Running)
        {
            throw new InvalidOperationException("La cámara no está en preview");
        }

        try
        {
            _currentOutputPath = outputFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? FileSystem.AppDataDirectory);

            // Eliminar archivo si existe
            if (File.Exists(outputFilePath))
            {
                File.Delete(outputFilePath);
            }

            var outputUrl = NSUrl.FromFilename(outputFilePath);
            _outputDelegate = new MovieFileOutputDelegate(this);

            // Configurar conexión de video para mejor calidad y orientación correcta
            var connection = _movieOutput.Connections.FirstOrDefault(c => c.InputPorts.Any(p => p.MediaType == AVMediaTypes.Video.GetConstant()));
            if (connection != null)
            {
                // IMPORTANTE: Configurar la orientación del video para grabación
                if (connection.SupportsVideoOrientation)
                {
                    var orientation = GetDeviceVideoOrientation();
                    connection.VideoOrientation = orientation;
                    AppLog.Info(nameof(IOSCameraRecordingService), $"Video orientation set to: {orientation}");
                }
                
                // Configurar mirroring para cámara frontal
                if (_isUsingFrontCamera && connection.SupportsVideoMirroring)
                {
                    connection.AutomaticallyAdjustsVideoMirroring = false;
                    connection.VideoMirrored = true;
                }
                
                if (connection.SupportsVideoStabilization)
                {
                    connection.PreferredVideoStabilizationMode = AVCaptureVideoStabilizationMode.Auto;
                }
            }

            _movieOutput.StartRecordingToOutputFile(outputUrl, _outputDelegate);
            _isRecording = true;
            RecordingStateChanged?.Invoke(this, true);

            AppLog.Info(nameof(IOSCameraRecordingService), $"Grabación iniciada: {outputFilePath}");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSCameraRecordingService), "Error iniciando grabación", ex);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<string?> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRecording || _movieOutput == null)
        {
            return Task.FromResult<string?>(null);
        }

        var tcs = new TaskCompletionSource<string?>();

        try
        {
            // El delegate manejará la finalización
            if (_outputDelegate != null)
            {
                _outputDelegate.CompletionHandler = (path) =>
                {
                    tcs.TrySetResult(path);
                };
            }

            _movieOutput.StopRecording();
            _isRecording = false;
            RecordingStateChanged?.Invoke(this, false);

            AppLog.Info(nameof(IOSCameraRecordingService), "Grabación detenida");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSCameraRecordingService), "Error deteniendo grabación", ex);
            tcs.TrySetResult(_currentOutputPath);
        }

        return tcs.Task;
    }

    public Task SetZoomAsync(double zoomFactor)
    {
        if (_currentDevice == null)
            return Task.CompletedTask;

        try
        {
            var clampedZoom = Math.Clamp(zoomFactor, MinZoom, MaxZoom);

            NSError? error;
            if (_currentDevice.LockForConfiguration(out error))
            {
                // Usar ramp para transición suave como la cámara nativa de iOS
                _currentDevice.RampToVideoZoom((nfloat)clampedZoom, 5.0f);
                _currentDevice.UnlockForConfiguration();

                _currentZoom = clampedZoom;
                ZoomChanged?.Invoke(this, clampedZoom);
            }
            else if (error != null)
            {
                AppLog.Warn(nameof(IOSCameraRecordingService), $"Error configurando zoom: {error.LocalizedDescription}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSCameraRecordingService), "Error estableciendo zoom", ex);
        }

        return Task.CompletedTask;
    }

    public async Task SwitchCameraAsync()
    {
        if (_captureSession == null || _deviceInput == null)
            return;

        try
        {
            var newPosition = _isUsingFrontCamera 
                ? AVCaptureDevicePosition.Back 
                : AVCaptureDevicePosition.Front;

            var newDevice = GetCamera(newPosition);
            if (newDevice == null)
            {
                AppLog.Warn(nameof(IOSCameraRecordingService), "No se encontró la cámara alternativa");
                return;
            }

            lock (_sessionLock)
            {
                _captureSession.BeginConfiguration();

                // Remover input actual
                _captureSession.RemoveInput(_deviceInput);

                // Agregar nuevo input
                NSError? error;
                var newInput = AVCaptureDeviceInput.FromDevice(newDevice, out error);
                if (error != null || newInput == null)
                {
                    // Revertir al input anterior
                    _captureSession.AddInput(_deviceInput);
                    _captureSession.CommitConfiguration();
                    return;
                }

                if (_captureSession.CanAddInput(newInput))
                {
                    _captureSession.AddInput(newInput);
                    _deviceInput = newInput;
                    _currentDevice = newDevice;
                    _isUsingFrontCamera = !_isUsingFrontCamera;
                }
                else
                {
                    _captureSession.AddInput(_deviceInput);
                }

                _captureSession.CommitConfiguration();
            }

            // Resetear zoom para la nueva cámara
            _currentZoom = 1.0;
            await SetZoomAsync(1.0);

            AppLog.Info(nameof(IOSCameraRecordingService), $"Cámara cambiada a: {(_isUsingFrontCamera ? "frontal" : "trasera")}");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSCameraRecordingService), "Error cambiando cámara", ex);
        }
    }

    public object? GetPreviewHandle()
    {
        return _previewLayer;
    }

    /// <summary>
    /// Obtiene la orientación de video correcta basada en la orientación actual del dispositivo.
    /// En iOS, la cámara trasera tiene una orientación nativa de LandscapeLeft,
    /// por lo que necesitamos mapear correctamente las orientaciones.
    /// </summary>
    private AVCaptureVideoOrientation GetDeviceVideoOrientation()
    {
        var windowScene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .FirstOrDefault();

        if (windowScene == null)
            return AVCaptureVideoOrientation.Portrait;

        // Mapeo directo: UIInterfaceOrientation -> AVCaptureVideoOrientation
        // La clave es que ambos usan el mismo sistema de referencia
        return windowScene.InterfaceOrientation switch
        {
            UIInterfaceOrientation.Portrait => AVCaptureVideoOrientation.Portrait,
            UIInterfaceOrientation.PortraitUpsideDown => AVCaptureVideoOrientation.PortraitUpsideDown,
            UIInterfaceOrientation.LandscapeLeft => AVCaptureVideoOrientation.LandscapeLeft,
            UIInterfaceOrientation.LandscapeRight => AVCaptureVideoOrientation.LandscapeRight,
            _ => AVCaptureVideoOrientation.Portrait
        };
    }

    private AVCaptureDevice? GetCamera(AVCaptureDevicePosition position)
    {
        try
        {
            var discovery = AVCaptureDeviceDiscoverySession.Create(
                new[] { AVCaptureDeviceType.BuiltInWideAngleCamera },
                AVMediaTypes.Video,
                position);

            return discovery.Devices.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            lock (_sessionLock)
            {
                if (_isRecording && _movieOutput != null)
                {
                    _movieOutput.StopRecording();
                    _isRecording = false;
                }

                if (_captureSession != null)
                {
                    if (_captureSession.Running)
                    {
                        _captureSession.StopRunning();
                    }

                    _captureSession.Dispose();
                    _captureSession = null;
                }

                _previewLayer?.Dispose();
                _previewLayer = null;

                _movieOutput?.Dispose();
                _movieOutput = null;

                _deviceInput?.Dispose();
                _deviceInput = null;
            }

            AppLog.Info(nameof(IOSCameraRecordingService), "Recursos liberados");
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(IOSCameraRecordingService), "Error liberando recursos", ex);
        }
    }

    /// <summary>
    /// Delegate para manejar eventos de grabación de video
    /// </summary>
    private sealed class MovieFileOutputDelegate : AVCaptureFileOutputRecordingDelegate
    {
        private readonly IOSCameraRecordingService _service;
        public Action<string?>? CompletionHandler { get; set; }

        public MovieFileOutputDelegate(IOSCameraRecordingService service)
        {
            _service = service;
        }

        public override void DidStartRecording(AVCaptureFileOutput captureOutput, NSUrl outputFileUrl, NSObject[] connections)
        {
            AppLog.Info(nameof(MovieFileOutputDelegate), $"Grabación iniciada: {outputFileUrl.Path}");
        }

        public override void FinishedRecording(AVCaptureFileOutput captureOutput, NSUrl outputFileUrl, NSObject[] connections, NSError? error)
        {
            if (error != null)
            {
                AppLog.Error(nameof(MovieFileOutputDelegate), $"Error en grabación: {error.LocalizedDescription}");
            }
            else
            {
                AppLog.Info(nameof(MovieFileOutputDelegate), $"Grabación finalizada: {outputFileUrl.Path}");
            }

            CompletionHandler?.Invoke(outputFileUrl.Path);
        }
    }
}
#endif
