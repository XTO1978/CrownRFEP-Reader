using CrownRFEP_Reader.ViewModels;
using System.Collections.Specialized;

namespace CrownRFEP_Reader.Views;

[QueryProperty(nameof(SessionId), "SessionId")]
[QueryProperty(nameof(SessionName), "SessionName")]
[QueryProperty(nameof(SessionType), "SessionType")]
[QueryProperty(nameof(Place), "Place")]
[QueryProperty(nameof(SessionDate), "Date")]
public partial class CameraPage : ContentPage
{
    private readonly CameraViewModel _viewModel;

    // Propiedades de navegación
    public int SessionId { get; set; }
    public string? SessionName { get; set; }
    public string? SessionType { get; set; }
    public string? Place { get; set; }
    public DateTime SessionDate { get; set; }

    public CameraPage(CameraViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        // Suscribirse a cambios en la colección de eventos para auto-scroll
        _viewModel.Events.CollectionChanged += OnEventsCollectionChanged;
    }

    private void OnEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add || 
            e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Hacer scroll al final cuando se añaden nuevos eventos
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(50); // Pequeña espera para que se renderice el nuevo elemento
                await EventsScrollView.ScrollToAsync(EventsStack, ScrollToPosition.End, false);
            });
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Configurar la sesión con los parámetros de navegación
        _viewModel.SetSessionInfo(SessionId, SessionName, SessionType, Place, SessionDate);

        // Inicializar la cámara cuando aparece la página
        await _viewModel.InitializeAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        
        try
        {
            // Desuscribirse de eventos
            _viewModel.Events.CollectionChanged -= OnEventsCollectionChanged;
            
            // Limpiar el preview handle para desconectar el AVCaptureVideoPreviewLayer
            _viewModel.CameraPreviewHandle = null;
            
            // Esperar a que se liberen los recursos antes de continuar con la navegación
            await _viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CameraPage OnDisappearing error: {ex.Message}");
        }
    }

    /// <summary>
    /// Maneja el gesto de pinch para zoom
    /// </summary>
    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Running)
        {
            var newZoom = _viewModel.ZoomFactor * e.Scale;
            _viewModel.ZoomFactor = Math.Clamp(newZoom, _viewModel.MinZoom, _viewModel.MaxZoom);
        }
    }

    /// <summary>
    /// Comando para establecer zoom a 1x
    /// </summary>
    public void SetZoom1x()
    {
        _viewModel.ZoomFactor = 1.0;
    }

    /// <summary>
    /// Comando para establecer zoom a 2x
    /// </summary>
    public void SetZoom2x()
    {
        _viewModel.ZoomFactor = 2.0;
    }
}
