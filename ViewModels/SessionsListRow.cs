using CrownRFEP_Reader.Models;

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

public sealed class SessionRow : SessionsListRow
{
    public Session Session { get; }

    public SessionRow(Session session)
    {
        Session = session;
    }
}
