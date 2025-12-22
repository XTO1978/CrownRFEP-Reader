using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CrownRFEP_Reader.Views.Controls;

public class SimplePieChart : GraphicsView
{
    // Colores predefinidos para las porciones del pie (pastel + buen contraste entre tonos)
    // Basado en una paleta tipo "Set3" (ColorBrewer), funciona bien en fondos oscuros.
    private static readonly Color[] DefaultColors = new[]
    {
        Color.FromArgb("#FF8DD3C7"), // Turquesa suave
        Color.FromArgb("#FFFFFFB3"), // Amarillo pastel
        Color.FromArgb("#FFBEBADA"), // Lila pastel
        Color.FromArgb("#FFFB8072"), // Salmón pastel
        Color.FromArgb("#FF80B1D3"), // Azul pastel
        Color.FromArgb("#FFFDB462"), // Naranja pastel
        Color.FromArgb("#FFB3DE69"), // Verde lima pastel
        Color.FromArgb("#FFFCCDE5"), // Rosa pastel
        Color.FromArgb("#FFD9D9D9"), // Gris claro
        Color.FromArgb("#FFBC80BD"), // Púrpura pastel
        Color.FromArgb("#FFCCEBC5"), // Verde menta pastel
        Color.FromArgb("#FFFFED6F"), // Amarillo dorado pastel
    };

    public static readonly BindableProperty ValuesProperty = BindableProperty.Create(
        nameof(Values),
        typeof(IList),
        typeof(SimplePieChart),
        defaultValue: null,
        propertyChanged: OnValuesChanged);

    public static readonly BindableProperty LabelsProperty = BindableProperty.Create(
        nameof(Labels),
        typeof(IList),
        typeof(SimplePieChart),
        defaultValue: null,
        propertyChanged: (b, _, _) =>
        {
            var chart = (SimplePieChart)b;
            chart.UpdateHeightRequestFromWidth();
            chart.Invalidate();
        });

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(SimplePieChart),
        Colors.White,
        propertyChanged: (b, _, _) => ((SimplePieChart)b).Invalidate());

    public static readonly BindableProperty ShowLegendProperty = BindableProperty.Create(
        nameof(ShowLegend),
        typeof(bool),
        typeof(SimplePieChart),
        true,
        propertyChanged: (b, _, _) =>
        {
            var chart = (SimplePieChart)b;
            chart.UpdateHeightRequestFromWidth();
            chart.Invalidate();
        });

    public IList? Values
    {
        get => (IList?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IList? Labels
    {
        get => (IList?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public bool ShowLegend
    {
        get => (bool)GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    private INotifyCollectionChanged? _valuesObservable;
    private bool _isUpdatingHeight;

    public SimplePieChart()
    {
        Drawable = new SimplePieChartDrawable(this);
        // Auto-ajuste de altura: en MAUI, dentro de ScrollView/StackLayout la altura tiende a colapsar.
        // Ajustamos HeightRequest en función del ancho para que el pie crezca al ensanchar la columna.
        SizeChanged += (_, _) => UpdateHeightRequestFromWidth();
        UpdateHeightRequestFromWidth();
    }

    private void UpdateHeightRequestFromWidth()
    {
        if (_isUpdatingHeight)
            return;

        if (Width <= 0)
            return;

        var hasLabels = Labels != null && Labels.Count > 0;
        var showLegend = ShowLegend && hasLabels;
        var legendWidth = showLegend ? Math.Min(140.0, Width * 0.35) : 0.0;
        var pieAreaWidth = Math.Max(0.0, Width - legendWidth);

        // Pedimos una altura proporcional al área del pie.
        // Factor 0.6 => un poco más pequeño, pero sigue creciendo con el ancho.
        var minimum = Math.Max(140.0, MinimumHeightRequest);
        var desired = Math.Max(minimum, pieAreaWidth * 0.6);

        if (double.IsNaN(desired) || double.IsInfinity(desired))
            return;

        if (Math.Abs(HeightRequest - desired) < 1)
            return;

        try
        {
            _isUpdatingHeight = true;
            HeightRequest = desired;
        }
        finally
        {
            _isUpdatingHeight = false;
        }
    }

    private static void OnValuesChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var chart = (SimplePieChart)bindable;

        if (oldValue is INotifyCollectionChanged oldObs)
            oldObs.CollectionChanged -= chart.OnValuesCollectionChanged;

        if (newValue is INotifyCollectionChanged newObs)
        {
            chart._valuesObservable = newObs;
            newObs.CollectionChanged += chart.OnValuesCollectionChanged;
        }
        else
        {
            chart._valuesObservable = null;
        }

        chart.Invalidate();
    }

    private void OnValuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();

    internal static Color GetColorForIndex(int index) => DefaultColors[index % DefaultColors.Length];

    private sealed class SimplePieChartDrawable : IDrawable
    {
        private readonly SimplePieChart _chart;

        public SimplePieChartDrawable(SimplePieChart chart)
        {
            _chart = chart;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var values = _chart.Values;
            if (values == null || values.Count == 0)
            {
                return;
            }

            var labels = _chart.Labels;

            // Convertir valores a números
            var numericValues = new double[values.Count];
            var total = 0.0;
            for (var i = 0; i < values.Count; i++)
            {
                var v = values[i];
                var dv = 0.0;
                if (v is double d) dv = d;
                else if (v is float f) dv = f;
                else if (v is int n) dv = n;
                else if (v is long l) dv = l;
                else if (v is decimal m) dv = (double)m;
                else if (v != null && double.TryParse(v.ToString(), out var parsed)) dv = parsed;

                if (dv < 0) dv = 0;
                numericValues[i] = dv;
                total += dv;
            }

            if (total <= 0)
                return;

            // Calcular dimensiones
            const float padding = 12;
            var showLegend = _chart.ShowLegend && labels != null && labels.Count > 0;
            
            // La leyenda ocupa un porcentaje del ancho disponible
            var legendWidth = showLegend ? Math.Min(140f, dirtyRect.Width * 0.35f) : 0f;
            
            var availableWidth = dirtyRect.Width - 2 * padding - legendWidth;
            var availableHeight = dirtyRect.Height - 2 * padding;
            var diameter = Math.Min(availableWidth, availableHeight);
            
            if (diameter < 30) return;

            // Centrar el gráfico en el espacio disponible para el pie
            var pieAreaWidth = dirtyRect.Width - legendWidth;
            var centerX = dirtyRect.Left + pieAreaWidth / 2;
            var centerY = dirtyRect.Top + dirtyRect.Height / 2;
            var radius = diameter / 2 - 4;

            // Dibujar porciones del pie
            var startAngle = -90f; // Empezar desde arriba
            for (var i = 0; i < numericValues.Length; i++)
            {
                var sweepAngle = (float)(numericValues[i] / total * 360);
                if (sweepAngle < 0.5f) continue; // Ignorar segmentos muy pequeños

                canvas.FillColor = GetColorForIndex(i);

                var path = new PathF();
                path.MoveTo(centerX, centerY);
                path.AddArc(
                    centerX - radius, centerY - radius,
                    centerX + radius, centerY + radius,
                    startAngle, startAngle + sweepAngle,
                    clockwise: false);
                path.Close();

                canvas.FillPath(path);

                startAngle += sweepAngle;
            }

            // Dibujar círculo interno (efecto donut)
            var innerRadius = radius * 0.5f;
            canvas.FillColor = Color.FromArgb("#FF121212"); // Color de fondo
            canvas.FillCircle(centerX, centerY, innerRadius);

            // Dibujar leyenda
            if (showLegend && labels != null)
            {
                canvas.FontColor = _chart.TextColor;
                canvas.FontSize = 11;

                // Leyenda a la derecha del gráfico (reusar pieAreaWidth)
                var legendX = dirtyRect.Left + pieAreaWidth + 8;
                var legendItemHeight = Math.Min(20f, dirtyRect.Height / 8);
                var maxItems = Math.Min(labels.Count, Math.Min(numericValues.Length, 8));
                var totalLegendHeight = maxItems * legendItemHeight;
                var legendY = dirtyRect.Top + (dirtyRect.Height - totalLegendHeight) / 2;
                const float colorBoxSize = 10f;

                for (var i = 0; i < maxItems; i++)
                {
                    var y = legendY + i * legendItemHeight;
                    
                    // Cuadrado de color
                    canvas.FillColor = GetColorForIndex(i);
                    canvas.FillRoundedRectangle(legendX, y + 2, colorBoxSize, colorBoxSize, 2);

                    // Texto
                    var label = labels[i]?.ToString() ?? "";
                    var value = (int)numericValues[i];
                    var text = $"{label} ({value})";
                    
                    canvas.FontColor = _chart.TextColor;
                    canvas.DrawString(text, legendX + colorBoxSize + 6, y, legendWidth - colorBoxSize - 14, legendItemHeight, 
                        HorizontalAlignment.Left, VerticalAlignment.Center, TextFlow.ClipBounds);
                }
            }
        }
    }
}
