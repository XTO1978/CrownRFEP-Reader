namespace CrownRFEP_Reader.Services;

public sealed class VideoClipUpdateNotifier : IVideoClipUpdateNotifier
{
    public event EventHandler<int>? VideoClipUpdated;

    public void NotifyVideoClipUpdated(int videoClipId)
        => VideoClipUpdated?.Invoke(this, videoClipId);
}
