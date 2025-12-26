using System.Collections.ObjectModel;
using CrownRFEP_Reader.Models;

namespace CrownRFEP_Reader.ViewModels;

public class SessionGroup : ObservableCollection<Session>
{
    public string Key { get; }
    public string Title { get; }

    public int TotalCount { get; }

    // Ojo: no mutamos los items del grupo al expandir/colapsar.
    // El expand/collapse se resuelve recreando los grupos en DashboardViewModel.
    public bool IsExpanded { get; }

    public SessionGroup(string key, string title, IEnumerable<Session> sessions, bool isExpanded)
    {
        Key = key;
        Title = title;
        IsExpanded = isExpanded;

        // Materializamos para poder contar sin enumerar dos veces.
        var list = sessions as IList<Session> ?? sessions.ToList();
        TotalCount = list.Count;

        if (!IsExpanded)
            return;

        foreach (var s in list)
            Add(s);
    }
}
