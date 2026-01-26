using System;

namespace CrownRFEP_Reader.ViewModels.SinglePlayer.Components;

internal sealed class PlaybackCore
{
    public bool IsPlaying { get; set; }
    public bool IsMuted { get; set; } = true;
    public TimeSpan CurrentPosition { get; set; }
    public TimeSpan Duration { get; set; }
    public double Progress { get; set; }
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool IsDraggingSlider { get; set; }
    public bool ShowOverlay { get; set; } = true;

    public void TogglePlayPause(Action onPlayRequested, Action onPauseRequested, Action onPlayActivated)
    {
        IsPlaying = !IsPlaying;
        if (IsPlaying)
        {
            onPlayActivated();
            onPlayRequested();
        }
        else
        {
            onPauseRequested();
        }
    }

    public void Stop(Action onStopRequested, Action onStopActivated)
    {
        IsPlaying = false;
        CurrentPosition = TimeSpan.Zero;
        onStopActivated();
        onStopRequested();
    }

    public void Seek(double seconds, Func<double> getDurationSeconds, Action<double> onSeekRequested)
    {
        var newPosition = CurrentPosition.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, getDurationSeconds()));
        onSeekRequested(newPosition);
    }

    public void StepForward(Action onFrameForwardRequested)
    {
        IsPlaying = false;
        onFrameForwardRequested();
    }

    public void StepBackward(Action onFrameBackwardRequested)
    {
        IsPlaying = false;
        onFrameBackwardRequested();
    }

    public void SetSpeed(string? speedStr, Action<double> onSpeedChanged)
    {
        if (double.TryParse(speedStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var speed))
        {
            PlaybackSpeed = speed;
            onSpeedChanged(speed);
        }
    }

    public void UpdateProgress(Action<double> setProgress)
    {
        if (IsDraggingSlider)
            return;

        if (Duration.TotalSeconds > 0)
            setProgress(CurrentPosition.TotalSeconds / Duration.TotalSeconds);
        else
            setProgress(0);
    }
}
