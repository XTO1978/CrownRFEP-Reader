#if WINDOWS
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CrownRFEP_Reader.Platforms.Windows;

internal sealed class NaudioWavRecorder : IDisposable
{
    private readonly IWaveIn _capture;
    private readonly string _outputPath;
    private readonly WaveFormat _outputFormat;

    private WaveFileWriter? _writer;
    private readonly object _gate = new();
    private TaskCompletionSource<bool>? _stoppedTcs;
    private bool _started;
    private bool _disposed;

    private NaudioWavRecorder(IWaveIn capture, string outputPath, WaveFormat outputFormat)
    {
        _capture = capture;
        _outputPath = outputPath;
        _outputFormat = outputFormat;

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public static NaudioWavRecorder CreateLoopback(string outputPath)
    {
        var capture = new WasapiLoopbackCapture();
        return CreateWithPcm16Output(capture, outputPath);
    }

    public static NaudioWavRecorder CreateMicrophone(string outputPath)
    {
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        var capture = new WasapiCapture(device);
        return CreateWithPcm16Output(capture, outputPath);
    }

    private static NaudioWavRecorder CreateWithPcm16Output(IWaveIn capture, string outputPath)
    {
        var input = capture.WaveFormat;
        var pcm16 = new WaveFormat(input.SampleRate, 16, input.Channels);
        return new NaudioWavRecorder(capture, outputPath, pcm16);
    }

    public void Start()
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_started)
                return;

            _stoppedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                // Ensure fresh file
                if (File.Exists(_outputPath))
                    File.Delete(_outputPath);

                _writer = new WaveFileWriter(_outputPath, _outputFormat);
                _capture.StartRecording();
                _started = true;

                Debug.WriteLine($"[NaudioWavRecorder] Started '{_outputPath}' | in={_capture.WaveFormat} | out={_outputFormat}");
            }
            catch
            {
                SafeDisposeWriter();
                throw;
            }
        }
    }

    public Task StopAsync()
    {
        TaskCompletionSource<bool>? tcs;

        lock (_gate)
        {
            tcs = _stoppedTcs;
            if (!_started)
                return Task.CompletedTask;
        }

        try
        {
            _capture.StopRecording();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NaudioWavRecorder] StopRecording error: {ex.Message}");
            // If StopRecording fails, still attempt to finalize file.
            lock (_gate)
            {
                _stoppedTcs?.TrySetResult(true);
            }
        }

        return tcs?.Task ?? Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            if (_writer == null)
                return;

            // Convert input to PCM16 if needed.
            var input = _capture.WaveFormat;

            if (input.Encoding == WaveFormatEncoding.IeeeFloat && input.BitsPerSample == 32)
            {
                var sampleCount = e.BytesRecorded / 4;
                var outBytes = new byte[sampleCount * 2];

                for (int i = 0; i < sampleCount; i++)
                {
                    var floatValue = BitConverter.ToSingle(e.Buffer, i * 4);
                    floatValue = Math.Max(-1f, Math.Min(1f, floatValue));
                    short pcm = (short)(floatValue * 32767f);
                    outBytes[i * 2] = (byte)(pcm & 0xFF);
                    outBytes[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
                }

                _writer.Write(outBytes, 0, outBytes.Length);
                return;
            }

            if (input.Encoding == WaveFormatEncoding.Pcm && input.BitsPerSample == 16)
            {
                _writer.Write(e.Buffer, 0, e.BytesRecorded);
                return;
            }

            // Fallback: write raw bytes (best-effort). This should be rare.
            _writer.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_gate)
        {
            try
            {
                if (e.Exception != null)
                    Debug.WriteLine($"[NaudioWavRecorder] RecordingStopped exception: {e.Exception.Message}");

                SafeDisposeWriter();

                _started = false;
                _stoppedTcs?.TrySetResult(true);

                try
                {
                    if (File.Exists(_outputPath))
                    {
                        var size = new FileInfo(_outputPath).Length;
                        Debug.WriteLine($"[NaudioWavRecorder] Finalized '{_outputPath}' | bytes={size}");
                    }
                }
                catch { }
            }
            finally
            {
                _stoppedTcs = null;
            }
        }
    }

    private void SafeDisposeWriter()
    {
        try { _writer?.Flush(); } catch { }
        try { _writer?.Dispose(); } catch { }
        _writer = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NaudioWavRecorder));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try { _capture.DataAvailable -= OnDataAvailable; } catch { }
        try { _capture.RecordingStopped -= OnRecordingStopped; } catch { }

        try { _capture.StopRecording(); } catch { }
        try { _capture.Dispose(); } catch { }

        lock (_gate)
        {
            SafeDisposeWriter();
            _stoppedTcs?.TrySetResult(true);
            _stoppedTcs = null;
        }
    }
}
#endif
