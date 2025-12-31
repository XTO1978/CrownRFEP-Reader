using CrownRFEP_Reader.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CrownRFEP_Reader.ViewModels;

public abstract class SessionsListRow;

public sealed class SessionGroupHeaderRow : SessionsListRow
{
    public string Key { get; }
    public string Title { get; }
    public bool IsExpanded { get; }
    public int TotalCount { get; }

    public SessionGroupHeaderRow(string key, string title, bool isExpanded, int totalCount)
    {
        Key = key;
        Title = title;
        IsExpanded = isExpanded;
        TotalCount = totalCount;
    }
}

public sealed class SessionRow : SessionsListRow, INotifyPropertyChanged
{
    public Session Session { get; }

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

    public SessionRow(Session session)
    {
        Session = session;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
