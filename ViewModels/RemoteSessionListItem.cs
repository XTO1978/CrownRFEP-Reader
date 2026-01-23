using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.ViewModels;

public sealed class RemoteSessionListItem : INotifyPropertyChanged
{
    public int SessionId { get; }
    public string Title { get; }
    public int VideoCount { get; }
    public DateTime LastModified { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public RemoteSessionListItem(int sessionId, string title, int videoCount, DateTime lastModified)
    {
        SessionId = sessionId;
        Title = title;
        VideoCount = videoCount;
        LastModified = lastModified;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
