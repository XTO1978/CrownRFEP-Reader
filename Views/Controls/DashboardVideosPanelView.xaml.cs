using Microsoft.Maui.Controls;

namespace CrownRFEP_Reader.Views.Controls;

public partial class DashboardVideosPanelView : ContentView
{
    public event EventHandler<TappedEventArgs>? VideoItemSingleTapped;
    public event EventHandler<TappedEventArgs>? VideoItemContextMenuTapped;
    public event EventHandler<ItemsViewScrolledEventArgs>? VideoGalleryScrolled;
    public event EventHandler<DragStartingEventArgs>? DragStarting;
    public event EventHandler<TappedEventArgs>? RemoteVideoContextMenu;

    public DashboardVideosPanelView()
    {
        InitializeComponent();
    }

    public CollectionView? VideoGalleryView => VideoGallery;

    public CollectionView? VideoLessonsGalleryView => VideoLessonsGallery;

    private void OnVideoItemSingleTappedInternal(object? sender, TappedEventArgs e)
        => VideoItemSingleTapped?.Invoke(sender, e);

    private void OnVideoItemContextMenuTappedInternal(object? sender, TappedEventArgs e)
        => VideoItemContextMenuTapped?.Invoke(sender, e);

    private void OnVideoGalleryScrolledInternal(object? sender, ItemsViewScrolledEventArgs e)
        => VideoGalleryScrolled?.Invoke(sender, e);

    private void OnDragStartingInternal(object? sender, DragStartingEventArgs e)
        => DragStarting?.Invoke(sender, e);

    private void OnRemoteVideoContextMenuInternal(object? sender, TappedEventArgs e)
        => RemoteVideoContextMenu?.Invoke(sender, e);
}
