using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CrownRFEP_Reader.Views.Controls;

/// <summary>
/// Gráfico de líneas simple para mostrar evolución de valores
/// </summary>
public class SimpleLineChart : GraphicsView
{
    public static readonly BindableProperty ValuesProperty = BindableProperty.Create(
        nameof(Values),
        typeof(IList),
        typeof(SimpleLineChart),
        defaultValue: null,
        propertyChanged: OnValuesChanged);

    public static readonly BindableProperty LabelsProperty = BindableProperty.Create(
        nameof(Labels),
        typeof(IList),
        typeof(SimpleLineChart),
        defaultValue: null,
        propertyChanged: (b, _, _) => ((SimpleLineChart)b).Invalidate());

    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(
        nameof(LineColor),
        typeof(Color),
        typeof(SimpleLineChart),
        Colors.DodgerBlue,
        propertyChanged: (b, _, _) => ((SimpleLineChart)b).Invalidate());

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(SimpleLineChart),
        Colors.White,
        propertyChanged: (b, _, _) => ((SimpleLineChart)b).Invalidate());

    public static readonly BindableProperty ShowPointsProperty = BindableProperty.Create(
        nameof(ShowPoints),
        typeof(bool),
        typeof(SimpleLineChart),
        true,
        propertyChanged: (b, _, _) => ((SimpleLineChart)b).Invalidate());

    public static readonly BindableProperty ShowFillProperty = BindableProperty.Create(
        nameof(ShowFill),
        typeof(bool),
        typeof(SimpleLineChart),
        true,
        propertyChanged: (b, _, _) => ((SimpleLineChart)b).Invalidate());

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

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
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

    public bool ShowFill
    {
        get => (bool)GetValue(ShowFillProperty);
        set => SetValue(ShowFillProperty, value);
    }

    private INotifyCollectionChanged? _valuesObservable;

    public SimpleLineChart()
    {
        Drawable = new SimpleLineChartDrawable(this);
        HeightRequest = 80;
    }

    private static void OnValuesChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var chart = (SimpleLineChart)bindable;

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

    private sealed class SimpleLineChartDrawable : IDrawable
    {
        private readonly SimpleLineChart _chart;

        public SimpleLineChartDrawable(SimpleLineChart chart)
        {
            _chart = chart;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var values = _chart.Values;
            if (values == null || values.Count == 0)
            {
                // Mostrar mensaje vacío
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 10;
                canvas.DrawString("Sin datos", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            var labels = _chart.Labels;

            // Convertir valores a double
            var numericValues = new double[values.Count];
            var min = double.MaxValue;
            var max = double.MinValue;

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

                numericValues[i] = dv;
                if (dv < min) min = dv;
                if (dv > max) max = dv;
            }

            // Ajustar rango para evitar línea plana
            if (Math.Abs(max - min) < 0.001)
            {
                min = max - 1;
                if (min < 0) min = 0;
            }
            var range = max - min;
            if (range <= 0) range = 1;

            const float padding = 8;
            const float pointRadius = 4;

            var plotLeft = dirtyRect.Left + padding;
            var plotRight = dirtyRect.Right - padding;
            var plotTop = dirtyRect.Top + padding;
            var plotBottom = dirtyRect.Bottom - padding;
            var plotWidth = plotRight - plotLeft;
            var plotHeight = plotBottom - plotTop;

            if (plotWidth <= 0 || plotHeight <= 0) return;

            // Calcular puntos
            var points = new PointF[numericValues.Length];
            for (var i = 0; i < numericValues.Length; i++)
            {
                var x = plotLeft + (i * plotWidth / (numericValues.Length - 1));
                if (numericValues.Length == 1) x = plotLeft + plotWidth / 2;
                
                var normalizedY = (numericValues[i] - min) / range;
                var y = plotBottom - (float)(normalizedY * plotHeight);
                points[i] = new PointF(x, y);
            }

            // Dibujar relleno bajo la línea
            if (_chart.ShowFill && points.Length >= 2)
            {
                var fillColor = _chart.LineColor.WithAlpha(0.2f);
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
                canvas.StrokeColor = _chart.LineColor;
                canvas.StrokeSize = 2;
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
                canvas.FillColor = _chart.LineColor;
                foreach (var p in points)
                {
                    canvas.FillCircle(p.X, p.Y, pointRadius);
                }

                // Punto central blanco
                canvas.FillColor = Colors.White;
                foreach (var p in points)
                {
                    canvas.FillCircle(p.X, p.Y, pointRadius * 0.5f);
                }
            }

            // Dibujar último valor
            if (numericValues.Length > 0)
            {
                var lastValue = numericValues[^1];
                var lastPoint = points[^1];
                
                canvas.FontColor = _chart.LineColor;
                canvas.FontSize = 11;
                canvas.DrawString(
                    lastValue.ToString("0"),
                    lastPoint.X - 20,
                    lastPoint.Y - 18,
                    40,
                    18,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Bottom);
            }
        }
    }
}
