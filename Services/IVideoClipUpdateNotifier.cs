namespace CrownRFEP_Reader.Services;

public interface IVideoClipUpdateNotifier
{
    event EventHandler<int>? VideoClipUpdated;
    void NotifyVideoClipUpdated(int videoClipId);
}
