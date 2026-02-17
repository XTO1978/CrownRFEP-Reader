namespace CrownRFEP_Reader.Behaviors;

/// <summary>
/// Behavior que ajusta automáticamente el FontSize de un Label para que el texto
/// sea lo más grande posible sin desbordar el ancho disponible del contenedor padre.
/// Soporta MaxLines > 1 (texto multilínea).
/// </summary>
public class AutoFitFontBehavior : Behavior<Label>
{
    private Label? _label;
    private bool _isAdjusting;

    /// <summary>Tamaño máximo de fuente permitido.</summary>
    public double MaxFontSize { get; set; } = 40;

    /// <summary>Tamaño mínimo de fuente permitido.</summary>
    public double MinFontSize { get; set; } = 10;

    /// <summary>Precisión del ajuste (diferencia mínima entre iteraciones).</summary>
    public double Precision { get; set; } = 0.5;

    protected override void OnAttachedTo(Label label)
    {
        base.OnAttachedTo(label);
        _label = label;

        label.PropertyChanged += OnLabelPropertyChanged;
        label.SizeChanged += OnLabelSizeChanged;

        // Intentar ajustar cuando el handler esté listo
        label.HandlerChanged += OnHandlerChanged;
    }

    protected override void OnDetachingFrom(Label label)
    {
        base.OnDetachingFrom(label);

        label.PropertyChanged -= OnLabelPropertyChanged;
        label.SizeChanged -= OnLabelSizeChanged;
        label.HandlerChanged -= OnHandlerChanged;
        _label = null;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        ScheduleAdjust();
    }

    private void OnLabelSizeChanged(object? sender, EventArgs e)
    {
        ScheduleAdjust();
    }

    private void OnLabelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Reajustar al cambiar texto, visibilidad, o MaxLines
        if (e.PropertyName is nameof(Label.Text) or nameof(Label.IsVisible) or nameof(Label.MaxLines)
            or nameof(Label.FormattedText))
        {
            ScheduleAdjust();
        }
    }

    private void ScheduleAdjust()
    {
        if (_label is null) return;
        // Despachar al hilo de UI para que el layout esté completo
        _label.Dispatcher.Dispatch(() => AdjustFontSize());
    }

    private void AdjustFontSize()
    {
        if (_isAdjusting || _label is null) return;

        var text = _label.Text;
        if (string.IsNullOrEmpty(text)) return;

        // Obtener ancho disponible recorriendo padres hasta encontrar uno con ancho positivo
        var availableWidth = GetAvailableWidth(_label);
        if (availableWidth <= 0) return;

        var maxLines = _label.MaxLines > 0 ? _label.MaxLines : 1;

        _isAdjusting = true;
        try
        {
            double lo = MinFontSize;
            double hi = MaxFontSize;

            // Búsqueda binaria para encontrar el mayor FontSize que cabe
            while (hi - lo > Precision)
            {
                var mid = (lo + hi) / 2.0;
                _label.FontSize = mid;

                var measured = _label.Measure(availableWidth, double.PositiveInfinity);

                // Estimar si cabe dentro de maxLines: la altura medida no debe superar
                // la altura de maxLines (cada línea ≈ fontSize * 1.3)
                var maxAllowedHeight = mid * 1.3 * maxLines;

                if (measured.Width <= availableWidth && measured.Height <= maxAllowedHeight)
                    lo = mid;
                else
                    hi = mid;
            }

            _label.FontSize = lo;
        }
        finally
        {
            _isAdjusting = false;
        }
    }

    /// <summary>
    /// Sube por el árbol visual hasta encontrar un elemento con un ancho positivo
    /// que represente el espacio disponible real.
    /// </summary>
    private static double GetAvailableWidth(VisualElement element)
    {
        var parent = element.Parent as VisualElement;
        while (parent is not null)
        {
            if (parent.Width > 0)
            {
                // Descontar padding si es un Layout con Padding
                var padding = parent switch
                {
                    Layout layout => layout.Padding.HorizontalThickness,
                    Border border => border.Padding.HorizontalThickness,
                    ContentView cv => cv.Padding.HorizontalThickness,
                    _ => 0.0
                };
                return parent.Width - padding;
            }
            parent = parent.Parent as VisualElement;
        }

        return 0;
    }
}
