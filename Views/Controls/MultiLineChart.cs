using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CrownRFEP_Reader.Views.Controls;

/// <summary>
/// Gráfico de líneas múltiples para mostrar evolución de varios valores
/// Soporta hasta 3 series con colores diferentes
/// </summary>
public class MultiLineChart : GraphicsView
{
    // Colores de la paleta del PieChart
    private static readonly Color[] SeriesColors = new[]
    {
        Color.FromArgb("#FF8DD3C7"), // Turquesa suave (Físico)
        Color.FromArgb("#FFFFED6F"), // Amarillo dorado (Mental)
        Color.FromArgb("#FFBEBADA"), // Lila pastel (Técnico)
    };

    public static readonly BindableProperty Values1Property = BindableProperty.Create(
        nameof(Values1),
        typeof(IList),
        typeof(MultiLineChart),
        defaultValue: null,
        propertyChanged: OnValuesChanged);

    public static readonly BindableProperty Values2Property = BindableProperty.Create(
        nameof(Values2),
        typeof(IList),
        typeof(MultiLineChart),
        defaultValue: null,
        propertyChanged: OnValuesChanged);

    public static readonly BindableProperty Values3Property = BindableProperty.Create(
        nameof(Values3),
        typeof(IList),
        typeof(MultiLineChart),
        defaultValue: null,
        propertyChanged: OnValuesChanged);

    public static readonly BindableProperty LabelsProperty = BindableProperty.Create(
        nameof(Labels),
        typeof(IList),
        typeof(MultiLineChart),
        defaultValue: null,
        propertyChanged: (b, _, _) => ((MultiLineChart)b).Invalidate());

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(MultiLineChart),
        Colors.White,
        propertyChanged: (b, _, _) => ((MultiLineChart)b).Invalidate());

    public static readonly BindableProperty ShowPointsProperty = BindableProperty.Create(
        nameof(ShowPoints),
        typeof(bool),
        typeof(MultiLineChart),
        true,
        propertyChanged: (b, _, _) => ((MultiLineChart)b).Invalidate());

    public static readonly BindableProperty MinValueProperty = BindableProperty.Create(
        nameof(MinValue),
        typeof(double),
        typeof(MultiLineChart),
        1.0,
        propertyChanged: (b, _, _) => ((MultiLineChart)b).Invalidate());

    public static readonly BindableProperty MaxValueProperty = BindableProperty.Create(
        nameof(MaxValue),
        typeof(double),
        typeof(MultiLineChart),
        5.0,
        propertyChanged: (b, _, _) => ((MultiLineChart)b).Invalidate());

    public IList? Values1
    {
        get => (IList?)GetValue(Values1Property);
        set => SetValue(Values1Property, value);
    }

    public IList? Values2
    {
        get => (IList?)GetValue(Values2Property);
        set => SetValue(Values2Property, value);
    }

    public IList? Values3
    {
        get => (IList?)GetValue(Values3Property);
        set => SetValue(Values3Property, value);
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

    public bool ShowPoints
    {
        get => (bool)GetValue(ShowPointsProperty);
        set => SetValue(ShowPointsProperty, value);
    }

    public double MinValue
    {
        get => (double)GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public MultiLineChart()
    {
        Drawable = new MultiLineChartDrawable(this);
        HeightRequest = 150;
    }

    private static void OnValuesChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var chart = (MultiLineChart)bindable;
        chart.Invalidate();
    }

    private sealed class MultiLineChartDrawable : IDrawable
    {
        private readonly MultiLineChart _chart;

        public MultiLineChartDrawable(MultiLineChart chart)
        {
            _chart = chart;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var hasData = (_chart.Values1?.Count ?? 0) > 0 ||
                          (_chart.Values2?.Count ?? 0) > 0 ||
                          (_chart.Values3?.Count ?? 0) > 0;

            if (!hasData)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 12;
                canvas.DrawString("Sin datos de evolución", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            const float paddingLeft = 30;
            const float paddingRight = 15;
            const float paddingTop = 15;
            const float paddingBottom = 25;

            var plotLeft = dirtyRect.Left + paddingLeft;
            var plotRight = dirtyRect.Right - paddingRight;
            var plotTop = dirtyRect.Top + paddingTop;
            var plotBottom = dirtyRect.Bottom - paddingBottom;
            var plotWidth = plotRight - plotLeft;
            var plotHeight = plotBottom - plotTop;

            if (plotWidth <= 0 || plotHeight <= 0) return;

            var min = _chart.MinValue;
            var max = _chart.MaxValue;
            var range = max - min;
            if (range <= 0) range = 1;

            // Determinar cantidad máxima de puntos
            var count = Math.Max(
                Math.Max(_chart.Values1?.Count ?? 0, _chart.Values2?.Count ?? 0),
                _chart.Values3?.Count ?? 0);

            if (count == 0) return;

            // Dibujar líneas de referencia horizontales (1-5)
            canvas.StrokeColor = Color.FromArgb("#FF303030");
            canvas.StrokeSize = 1;
            canvas.FontColor = Color.FromArgb("#FF606060");
            canvas.FontSize = 10;

            for (int v = (int)min; v <= (int)max; v++)
            {
                var normalizedY = (v - min) / range;
                var y = plotBottom - (float)(normalizedY * plotHeight);
                
                canvas.DrawLine(plotLeft, y, plotRight, y);
                canvas.DrawString(v.ToString(), plotLeft - 20, y - 6, 18, 12, HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            // Dibujar cada serie
            var series = new[] { _chart.Values1, _chart.Values2, _chart.Values3 };
            
            for (int s = 0; s < series.Length; s++)
            {
                var values = series[s];
                if (values == null || values.Count == 0) continue;

                var color = SeriesColors[s];
                var points = CalculatePoints(values, count, plotLeft, plotWidth, plotTop, plotBottom, plotHeight, min, range);

                // Dibujar área de relleno
                if (points.Length >= 2)
                {
                    var fillColor = color.WithAlpha(0.15f);
                    canvas.FillColor = fillColor;

                    var path = new PathF();
                    path.MoveTo(points[0].X, plotBottom);
                    foreach (var p in points)
                    {
                        path.LineTo(p.X, p.Y);
                    }
                    path.LineTo(points[^1].X, plotBottom);
                    path.Close();
                    canvas.FillPath(path);
                }

                // Dibujar línea
                if (points.Length >= 2)
                {
                    canvas.StrokeColor = color;
                    canvas.StrokeSize = 2.5f;
                    canvas.StrokeLineCap = LineCap.Round;
                    canvas.StrokeLineJoin = LineJoin.Round;

                    var path = new PathF();
                    path.MoveTo(points[0].X, points[0].Y);
                    for (var i = 1; i < points.Length; i++)
                    {
                        path.LineTo(points[i].X, points[i].Y);
                    }
                    canvas.DrawPath(path);
                }

                // Dibujar puntos
                if (_chart.ShowPoints)
                {
                    canvas.FillColor = color;
                    foreach (var p in points)
                    {
                        canvas.FillCircle(p.X, p.Y, 4);
                    }

                    canvas.FillColor = Color.FromArgb("#FF1E1E1E");
                    foreach (var p in points)
                    {
                        canvas.FillCircle(p.X, p.Y, 2);
                    }
                }
            }

            // Dibujar etiquetas de fechas si existen
            var labels = _chart.Labels;
            if (labels != null && labels.Count > 0)
            {
                canvas.FontColor = Color.FromArgb("#FF808080");
                canvas.FontSize = 9;

                var labelCount = Math.Min(labels.Count, count);
                var step = labelCount > 6 ? labelCount / 5 : 1; // Mostrar máximo 5-6 etiquetas

                for (int i = 0; i < labelCount; i += step)
                {
                    var x = count == 1 
                        ? plotLeft + plotWidth / 2 
                        : plotLeft + (i * plotWidth / (count - 1));
                    
                    var label = labels[i]?.ToString() ?? "";
                    canvas.DrawString(label, x - 25, plotBottom + 5, 50, 20, HorizontalAlignment.Center, VerticalAlignment.Top);
                }
            }
        }

        private PointF[] CalculatePoints(IList values, int totalCount, float plotLeft, float plotWidth, 
            float plotTop, float plotBottom, float plotHeight, double min, double range)
        {
            var points = new PointF[values.Count];
            
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

                var x = totalCount == 1 
                    ? plotLeft + plotWidth / 2 
                    : plotLeft + (i * plotWidth / (totalCount - 1));
                
                var normalizedY = (dv - min) / range;
                normalizedY = Math.Clamp(normalizedY, 0, 1);
                var y = plotBottom - (float)(normalizedY * plotHeight);
                
                points[i] = new PointF(x, y);
            }

            return points;
        }
    }
}
