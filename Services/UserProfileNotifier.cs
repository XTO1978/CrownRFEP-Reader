namespace CrownRFEP_Reader.Services;

public class UserProfileNotifier
{
    public event EventHandler? ProfileSaved;

    public void NotifyProfileSaved()
    {
        ProfileSaved?.Invoke(this, EventArgs.Empty);
    }
}
