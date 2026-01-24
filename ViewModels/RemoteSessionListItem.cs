using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.ViewModels;

public sealed class RemoteSessionListItem : INotifyPropertyChanged
{
    public int SessionId { get; }
    public string Title { get; }
    public string? Place { get; }
    public DateTime SessionDate { get; }
    public string? Coach { get; }
    public int VideoCount { get; }
    public DateTime LastModified { get; }

    public string PlaceDateText
    {
        get
        {
            var place = string.IsNullOrWhiteSpace(Place) ? "—" : Place;
            return $"{place} · {SessionDate:dd/MM}";
        }
    }

    public string CoachText
        => string.IsNullOrWhiteSpace(Coach) ? "—" : Coach;

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

    public RemoteSessionListItem(int sessionId, string title, string? place, DateTime sessionDate, string? coach, int videoCount, DateTime lastModified)
    {
        SessionId = sessionId;
        Title = title;
        Place = place;
        SessionDate = sessionDate;
        Coach = coach;
        VideoCount = videoCount;
        LastModified = lastModified;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
