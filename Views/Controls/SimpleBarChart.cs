using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace CrownRFEP_Reader.Views.Controls;

public class SimpleBarChart : GraphicsView
{
    public static readonly BindableProperty ValuesProperty = BindableProperty.Create(
        nameof(Values),
        typeof(IList),
        typeof(SimpleBarChart),
        defaultValue: null,
        propertyChanged: OnValuesChanged);

    public static readonly BindableProperty LabelsProperty = BindableProperty.Create(
        nameof(Labels),
        typeof(IList),
        typeof(SimpleBarChart),
        defaultValue: null,
        propertyChanged: (b, _, _) => ((SimpleBarChart)b).Invalidate());

    public static readonly BindableProperty BarColorProperty = BindableProperty.Create(
        nameof(BarColor),
        typeof(Color),
        typeof(SimpleBarChart),
        Colors.DodgerBlue,
        propertyChanged: (b, _, _) => ((SimpleBarChart)b).Invalidate());

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(SimpleBarChart),
        Colors.White,
        propertyChanged: (b, _, _) => ((SimpleBarChart)b).Invalidate());

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

    public Color BarColor
    {
        get => (Color)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    private INotifyCollectionChanged? _valuesObservable;

    public SimpleBarChart()
    {
        Drawable = new SimpleBarChartDrawable(this);
        HeightRequest = 140;
    }

    private static void OnValuesChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var chart = (SimpleBarChart)bindable;

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

    private sealed class SimpleBarChartDrawable : IDrawable
    {
        private readonly SimpleBarChart _chart;

        public SimpleBarChartDrawable(SimpleBarChart chart)
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

            var max = 0.0;
            var numericValues = new double[values.Count];
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
                if (dv > max) max = dv;
            }

            if (max <= 0)
                max = 1;

            const float padding = 8;
            const float labelHeight = 16;
            const float gap = 6;

            var plotTop = dirtyRect.Top + padding;
            var plotBottom = dirtyRect.Bottom - padding - labelHeight;
            var plotHeight = Math.Max(0, plotBottom - plotTop);

            var availableWidth = dirtyRect.Width - 2 * padding;
            var barWidth = (availableWidth - (values.Count - 1) * gap) / values.Count;
            if (barWidth < 2) barWidth = 2;

            canvas.FontColor = _chart.TextColor;
            canvas.FontSize = 11;

            for (var i = 0; i < numericValues.Length; i++)
            {
                var x = dirtyRect.Left + padding + i * (barWidth + gap);
                var h = (float)(numericValues[i] / max) * plotHeight;
                var y = plotBottom - h;

                canvas.FillColor = _chart.BarColor;
                canvas.FillRoundedRectangle(x, y, barWidth, h, 4);

                if (labels != null && i < labels.Count)
                {
                    var label = labels[i]?.ToString() ?? "";
                    canvas.DrawString(label, x, dirtyRect.Bottom - padding - labelHeight + 2, barWidth, labelHeight, HorizontalAlignment.Center, VerticalAlignment.Top, TextFlow.ClipBounds);
                }
            }
        }
    }
}
