using Microsoft.Maui.Controls;
using MauiGridLength = Microsoft.Maui.GridLength;
using MauiGridUnitType = Microsoft.Maui.GridUnitType;
using MauiThickness = Microsoft.Maui.Thickness;
using MauiPointerEventArgs = Microsoft.Maui.Controls.PointerEventArgs;
#if WINDOWS
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using WinUIElement = Microsoft.UI.Xaml.UIElement;
#endif
#if MACCATALYST
using AppKit;
#endif

namespace CrownRFEP_Reader.Views.Controls;

/// <summary>
/// Control que permite redimensionar columnas de un Grid arrastrando.
/// Muestra una previsualización mientras se arrastra y aplica los cambios al soltar.
/// </summary>
public class GridSplitter : ContentView
{
    private double _leftColumnStartWidth;
    private double _rightColumnStartWidth;
    private double _pendingLeftWidth;
    private double _pendingRightWidth;
    private bool _leftStartWasStar;
    private bool _rightStartWasStar;
    private bool _leftStartWasAbsolute;
    private bool _rightStartWasAbsolute;
    private Grid? _parentGrid;
    private BoxView? _previewLine;
    private readonly BoxView _visualIndicator;

    public static readonly BindableProperty LeftColumnIndexProperty =
        BindableProperty.Create(nameof(LeftColumnIndex), typeof(int), typeof(GridSplitter), -1);

    public static readonly BindableProperty RightColumnIndexProperty =
        BindableProperty.Create(nameof(RightColumnIndex), typeof(int), typeof(GridSplitter), -1);

    public static readonly BindableProperty MinLeftWidthProperty =
        BindableProperty.Create(nameof(MinLeftWidth), typeof(double), typeof(GridSplitter), 100.0);

    public static readonly BindableProperty MinRightWidthProperty =
        BindableProperty.Create(nameof(MinRightWidth), typeof(double), typeof(GridSplitter), 100.0);

    public static readonly BindableProperty LeftColumnWidthProperty =
        BindableProperty.Create(
            nameof(LeftColumnWidth),
            typeof(MauiGridLength),
            typeof(GridSplitter),
            MauiGridLength.Auto,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty RightColumnWidthProperty =
        BindableProperty.Create(
            nameof(RightColumnWidth),
            typeof(MauiGridLength),
            typeof(GridSplitter),
            MauiGridLength.Auto,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Índice de la columna a la izquierda del splitter
    /// </summary>
    public int LeftColumnIndex
    {
        get => (int)GetValue(LeftColumnIndexProperty);
        set => SetValue(LeftColumnIndexProperty, value);
    }

    /// <summary>
    /// Índice de la columna a la derecha del splitter
    /// </summary>
    public int RightColumnIndex
    {
        get => (int)GetValue(RightColumnIndexProperty);
        set => SetValue(RightColumnIndexProperty, value);
    }

    /// <summary>
    /// Ancho mínimo de la columna izquierda
    /// </summary>
    public double MinLeftWidth
    {
        get => (double)GetValue(MinLeftWidthProperty);
        set => SetValue(MinLeftWidthProperty, value);
    }

    /// <summary>
    /// Ancho mínimo de la columna derecha
    /// </summary>
    public double MinRightWidth
    {
        get => (double)GetValue(MinRightWidthProperty);
        set => SetValue(MinRightWidthProperty, value);
    }

    /// <summary>
    /// Permite enlazar (TwoWay) el ancho de la columna izquierda para persistir el ajuste del usuario.
    /// </summary>
    public MauiGridLength LeftColumnWidth
    {
        get => (MauiGridLength)GetValue(LeftColumnWidthProperty);
        set => SetValue(LeftColumnWidthProperty, value);
    }

    /// <summary>
    /// Permite enlazar (TwoWay) el ancho de la columna derecha para persistir el ajuste del usuario.
    /// </summary>
    public MauiGridLength RightColumnWidth
    {
        get => (MauiGridLength)GetValue(RightColumnWidthProperty);
        set => SetValue(RightColumnWidthProperty, value);
    }

    public GridSplitter()
    {
        WidthRequest = 10;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        BackgroundColor = Colors.Transparent;

        // Indicador visual - línea visible en el centro
        _visualIndicator = new BoxView
        {
            BackgroundColor = Color.FromArgb("#FF4A4A4A"),
            WidthRequest = 0.8,
            Opacity = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Fill
        };
        Content = _visualIndicator;

        // PanGestureRecognizer para el arrastre
        var panGesture = new PanGestureRecognizer();
        panGesture.PanUpdated += OnPanUpdated;
        GestureRecognizers.Add(panGesture);

#if WINDOWS || MACCATALYST
    // PointerGestureRecognizer para feedback visual (hover) - Windows/MacCatalyst
        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerEntered += OnPointerEntered;
        pointerGesture.PointerExited += OnPointerExited;
        GestureRecognizers.Add(pointerGesture);
#endif
    }

    private void OnPointerEntered(object? sender, MauiPointerEventArgs e)
    {
        _visualIndicator.BackgroundColor = Color.FromArgb("#FF8A8A8A");
        _visualIndicator.WidthRequest = 4;
    _visualIndicator.Opacity = 1;
        SetHoverCursor(true);
    }

    private void OnPointerExited(object? sender, MauiPointerEventArgs e)
    {
        _visualIndicator.BackgroundColor = Color.FromArgb("#FF4A4A4A");
        _visualIndicator.WidthRequest = 0.8;
    _visualIndicator.Opacity = 0;
        SetHoverCursor(false);
    }

    private void SetHoverCursor(bool isResize)
    {
#if WINDOWS
        // TODO: Implement cursor change for Windows using native interop if needed
        // The visual indicator already provides feedback to the user
#elif MACCATALYST
        if (isResize)
            NSCursor.ResizeLeftRightCursor.Set();
        else
            NSCursor.ArrowCursor.Set();
#endif
    }

    private void ShowPreviewLine(double xPosition)
    {
        if (_parentGrid == null) return;

        if (_previewLine == null)
        {
            _previewLine = new BoxView
            {
                BackgroundColor = Color.FromArgb("#AA00AAFF"), // Azul semitransparente
                WidthRequest = 3,
                VerticalOptions = LayoutOptions.Fill,
                HorizontalOptions = LayoutOptions.Start,
                InputTransparent = true,
                ZIndex = 9999
            };
            
            // Añadir a todas las filas y columnas del grid para garantizar que esté por encima de todo
            Grid.SetRow(_previewLine, 0);
            Grid.SetRowSpan(_previewLine, _parentGrid.RowDefinitions.Count > 0 ? _parentGrid.RowDefinitions.Count : 1);
            Grid.SetColumnSpan(_previewLine, _parentGrid.ColumnDefinitions.Count);
            _parentGrid.Children.Add(_previewLine);
        }

        // Posicionar la línea de previsualización usando Margin
        _previewLine.Margin = new MauiThickness(xPosition - 1.5, 0, 0, 0);
        _previewLine.IsVisible = true;
    }

    private void HidePreviewLine()
    {
        if (_previewLine != null)
        {
            _previewLine.IsVisible = false;
            if (_parentGrid != null && _parentGrid.Children.Contains(_previewLine))
            {
                _parentGrid.Children.Remove(_previewLine);
            }
            _previewLine = null;
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _parentGrid = Parent as Grid;
                if (_parentGrid == null) return;

                _visualIndicator.BackgroundColor = Color.FromArgb("#FFAAAAAA");
                _visualIndicator.WidthRequest = 6;
                _visualIndicator.Opacity = 1;

                // Capturar anchos actuales de las columnas
                var leftIdx = LeftColumnIndex;
                var rightIdx = RightColumnIndex;

                _leftStartWasStar = false;
                _rightStartWasStar = false;
                _leftStartWasAbsolute = false;
                _rightStartWasAbsolute = false;

                if (leftIdx >= 0 && leftIdx < _parentGrid.ColumnDefinitions.Count)
                {
                    var w = _parentGrid.ColumnDefinitions[leftIdx].Width;
                    _leftStartWasStar = w.IsStar;
                    _leftStartWasAbsolute = w.IsAbsolute;
                }

                if (rightIdx >= 0 && rightIdx < _parentGrid.ColumnDefinitions.Count)
                {
                    var w = _parentGrid.ColumnDefinitions[rightIdx].Width;
                    _rightStartWasStar = w.IsStar;
                    _rightStartWasAbsolute = w.IsAbsolute;
                }

                if (leftIdx >= 0 && leftIdx < _parentGrid.ColumnDefinitions.Count)
                {
                    _leftColumnStartWidth = GetColumnActualWidth(_parentGrid, leftIdx);
                }

                if (rightIdx >= 0 && rightIdx < _parentGrid.ColumnDefinitions.Count)
                {
                    _rightColumnStartWidth = GetColumnActualWidth(_parentGrid, rightIdx);
                }

                _pendingLeftWidth = _leftColumnStartWidth;
                _pendingRightWidth = _rightColumnStartWidth;
                break;

            case GestureStatus.Running:
                if (_parentGrid == null) return;

                var deltaX = e.TotalX;
                var leftIndex = LeftColumnIndex;
                var rightIndex = RightColumnIndex;

                if (leftIndex < 0 || rightIndex >= _parentGrid.ColumnDefinitions.Count)
                    return;

                var newLeftWidth = _leftColumnStartWidth + deltaX;
                var newRightWidth = _rightColumnStartWidth - deltaX;

                // Aplicar límites mínimos
                if (newLeftWidth < MinLeftWidth)
                {
                    newLeftWidth = MinLeftWidth;
                    newRightWidth = _leftColumnStartWidth + _rightColumnStartWidth - MinLeftWidth;
                }
                if (newRightWidth < MinRightWidth)
                {
                    newRightWidth = MinRightWidth;
                    newLeftWidth = _leftColumnStartWidth + _rightColumnStartWidth - MinRightWidth;
                }

                _pendingLeftWidth = newLeftWidth;
                _pendingRightWidth = newRightWidth;

                // Calcular la posición X donde irá la nueva línea
                double previewX = 0;
                for (int i = 0; i < leftIndex; i++)
                {
                    previewX += GetColumnActualWidth(_parentGrid, i);
                }
                previewX += newLeftWidth;
                // Añadir el ancho del splitter
                previewX += WidthRequest / 2;

                ShowPreviewLine(previewX);
                break;

            case GestureStatus.Completed:
                // Aplicar los cambios finales
                if (_parentGrid != null && _pendingLeftWidth > 0 && _pendingRightWidth > 0)
                {
                    var lIdx = LeftColumnIndex;
                    var rIdx = RightColumnIndex;
                    
                    if (lIdx >= 0 && rIdx < _parentGrid.ColumnDefinitions.Count)
                    {
                        MauiGridLength newLeft;
                        MauiGridLength newRight;

                        // Si ambas columnas eran Star, devolvemos Star (ratio) para que el Grid
                        // siga rellenando el ancho disponible y no queden huecos ni overflow.
                        if (_leftStartWasStar && _rightStartWasStar)
                        {
                            var total = _pendingLeftWidth + _pendingRightWidth;
                            if (total <= 0)
                            {
                                newLeft = new MauiGridLength(1, MauiGridUnitType.Star);
                                newRight = new MauiGridLength(1, MauiGridUnitType.Star);
                            }
                            else
                            {
                                newLeft = new MauiGridLength(_pendingLeftWidth / total, MauiGridUnitType.Star);
                                newRight = new MauiGridLength(_pendingRightWidth / total, MauiGridUnitType.Star);
                            }
                        }
                        // Si una era Absolute y la otra Star, mantenemos la fija en Absolute
                        // y dejamos la otra en Star para que absorba el resto.
                        else if (_leftStartWasAbsolute && _rightStartWasStar)
                        {
                            newLeft = new MauiGridLength(_pendingLeftWidth, MauiGridUnitType.Absolute);
                            newRight = new MauiGridLength(1, MauiGridUnitType.Star);
                        }
                        else if (_leftStartWasStar && _rightStartWasAbsolute)
                        {
                            newLeft = new MauiGridLength(1, MauiGridUnitType.Star);
                            newRight = new MauiGridLength(_pendingRightWidth, MauiGridUnitType.Absolute);
                        }
                        else
                        {
                            // Fallback: usar absolutos.
                            newLeft = new MauiGridLength(_pendingLeftWidth, MauiGridUnitType.Absolute);
                            newRight = new MauiGridLength(_pendingRightWidth, MauiGridUnitType.Absolute);
                        }

                        // Si hay bindings TwoWay, actualizamos el VM para que el cambio persista.
                        // Si no hay bindings, aplicamos directamente al Grid.
                        var hasWidthBindings = IsSet(LeftColumnWidthProperty) && IsSet(RightColumnWidthProperty);
                        if (hasWidthBindings)
                        {
                            LeftColumnWidth = newLeft;
                            RightColumnWidth = newRight;
                        }
                        else
                        {
                            _parentGrid.ColumnDefinitions[lIdx].Width = newLeft;
                            _parentGrid.ColumnDefinitions[rIdx].Width = newRight;
                        }
                    }
                }

                HidePreviewLine();
                _visualIndicator.BackgroundColor = Color.FromArgb("#FF4A4A4A");
                _visualIndicator.WidthRequest = 0.8;
                _visualIndicator.Opacity = 0;
                _parentGrid = null;
                break;

            case GestureStatus.Canceled:
                HidePreviewLine();
                _visualIndicator.BackgroundColor = Color.FromArgb("#FF4A4A4A");
                _visualIndicator.WidthRequest = 0.8;
                _visualIndicator.Opacity = 0;
                _parentGrid = null;
                break;
        }
    }

    private static double GetColumnActualWidth(Grid grid, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= grid.ColumnDefinitions.Count)
            return 0;

        var col = grid.ColumnDefinitions[columnIndex];
        
        // Si es absoluto, devolver el valor
        if (col.Width.IsAbsolute)
            return col.Width.Value;

        // Estimar el ancho basado en proporciones
        double totalWidth = grid.Width;
        if (double.IsNaN(totalWidth) || totalWidth <= 0)
            totalWidth = 1400; // Valor por defecto

        var definitions = grid.ColumnDefinitions;
        double totalStars = 0;
        double totalAbsolute = 0;

        foreach (var c in definitions)
        {
            if (c.Width.IsStar)
                totalStars += c.Width.Value;
            else if (c.Width.IsAbsolute)
                totalAbsolute += c.Width.Value;
        }

        var remainingWidth = totalWidth - totalAbsolute;
        
        if (col.Width.IsStar && totalStars > 0)
            return remainingWidth * (col.Width.Value / totalStars);
        
        return col.Width.IsAuto ? 100 : 0;
    }
}
