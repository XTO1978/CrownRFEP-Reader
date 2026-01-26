using Microsoft.Maui.Controls;

namespace CrownRFEP_Reader.Views.Controls;

public partial class DashboardRemoteLibraryView : ContentView
{
    public event EventHandler<TappedEventArgs>? RemoteLibraryHeaderContextMenu;
    public event EventHandler<TappedEventArgs>? SessionsMenuNewSessionTapped;
    public event EventHandler<TappedEventArgs>? RemoteSessionContextMenuTapped;

    public DashboardRemoteLibraryView()
    {
        InitializeComponent();
    }

    private void OnRemoteLibraryHeaderContextMenuInternal(object? sender, TappedEventArgs e)
        => RemoteLibraryHeaderContextMenu?.Invoke(sender, e);

    private void OnSessionsMenuNewSessionTappedInternal(object? sender, TappedEventArgs e)
        => SessionsMenuNewSessionTapped?.Invoke(sender, e);

    private void OnRemoteSessionContextMenuTappedInternal(object? sender, TappedEventArgs e)
        => RemoteSessionContextMenuTapped?.Invoke(sender, e);
}
