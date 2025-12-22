using Microsoft.Maui.Graphics;

namespace CrownRFEP_Reader.Views.Controls;

public enum AnalysisDrawingTool
{
    Stroke,
    Text,
    Shape
}

public enum AnalysisShapeType
{
    Rectangle,
    Circle,
    Line,
    Arrow
}

public class AnalysisDrawingView : GraphicsView
{
    private readonly DrawingDrawable _drawable;
    private AnalysisDrawingTool _tool = AnalysisDrawingTool.Stroke;
    private AnalysisShapeType _shapeType = AnalysisShapeType.Rectangle;

    private Color _inkColor = Color.FromArgb("#FFFF7043");
    private float _inkThickness = 3f;
    private float _textSize = 16f;

    private bool _isDrawing;
    private StrokeElement? _activeStroke;
    private IDrawingElement? _activeShape;
    private PointF _dragStart;

    public event EventHandler<TextRequestedEventArgs>? TextRequested;

    public AnalysisDrawingTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Invalidate();
        }
    }

    public AnalysisShapeType ShapeType
    {
        get => _shapeType;
        set
        {
            _shapeType = value;
            Invalidate();
        }
    }

    public Color InkColor
    {
        get => _inkColor;
        set
        {
            _inkColor = value;
            Invalidate();
        }
    }

    public float InkThickness
    {
        get => _inkThickness;
        set
        {
            _inkThickness = Math.Max(1f, value);
            Invalidate();
        }
    }

    public float TextSize
    {
        get => _textSize;
        set
        {
            _textSize = Math.Max(6f, value);
            Invalidate();
        }
    }

    public AnalysisDrawingView()
    {
        _drawable = new DrawingDrawable();
        Drawable = _drawable;

        InputTransparent = true;

        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += OnPointerPressed;
        pointer.PointerMoved += OnPointerMoved;
        pointer.PointerReleased += OnPointerReleased;
        GestureRecognizers.Add(pointer);
    }

    public void AddText(PointF position, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _drawable.Elements.Add(new TextElement
        {
            Position = position,
            Text = text.Trim(),
            Color = InkColor,
            FontSize = TextSize
        });

        Invalidate();
    }

    public void ClearAll()
    {
        _drawable.Elements.Clear();
        _activeStroke = null;
        _activeShape = null;
        _isDrawing = false;
        Invalidate();
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (InputTransparent)
            return;

        var p = e.GetPosition(this);
        if (p == null)
            return;

        var point = new PointF((float)p.Value.X, (float)p.Value.Y);

        switch (Tool)
        {
            case AnalysisDrawingTool.Stroke:
                _isDrawing = true;
                _activeStroke = new StrokeElement { Color = InkColor, StrokeSize = InkThickness };
                _activeStroke.Points.Add(point);
                _drawable.Elements.Add(_activeStroke);
                Invalidate();
                break;

            case AnalysisDrawingTool.Shape:
                _isDrawing = true;
                _dragStart = point;
                _activeShape = CreateShapeElement(_shapeType, point, InkColor, InkThickness);
                _drawable.Elements.Add(_activeShape);
                Invalidate();
                break;

            case AnalysisDrawingTool.Text:
                TextRequested?.Invoke(this, new TextRequestedEventArgs(point));
                break;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (InputTransparent)
            return;

        if (!_isDrawing)
            return;

        var p = e.GetPosition(this);
        if (p == null)
            return;

        var point = new PointF((float)p.Value.X, (float)p.Value.Y);

        if (Tool == AnalysisDrawingTool.Stroke && _activeStroke != null)
        {
            _activeStroke.Points.Add(point);
            Invalidate();
            return;
        }

        if (Tool == AnalysisDrawingTool.Shape && _activeShape != null)
        {
            UpdateShapeElement(_activeShape, _shapeType, _dragStart, point);
            Invalidate();
        }
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        if (InputTransparent)
            return;

        _isDrawing = false;
        _activeStroke = null;
        _activeShape = null;
    }

    private static IDrawingElement CreateShapeElement(AnalysisShapeType shapeType, PointF start, Color color, float strokeSize)
    {
        return shapeType switch
        {
            AnalysisShapeType.Rectangle => new RectangleElement { Rect = new RectF(start.X, start.Y, 1, 1), Color = color, StrokeSize = strokeSize },
            AnalysisShapeType.Circle => new EllipseElement { Rect = new RectF(start.X, start.Y, 1, 1), ForceCircle = true, Color = color, StrokeSize = strokeSize },
            AnalysisShapeType.Line => new LineElement { Start = start, End = start, Color = color, StrokeSize = strokeSize },
            AnalysisShapeType.Arrow => new ArrowElement { Start = start, End = start, Color = color, StrokeSize = strokeSize },
            _ => new RectangleElement { Rect = new RectF(start.X, start.Y, 1, 1), Color = color, StrokeSize = strokeSize }
        };
    }

    private static void UpdateShapeElement(IDrawingElement element, AnalysisShapeType shapeType, PointF start, PointF current)
    {
        switch (shapeType)
        {
            case AnalysisShapeType.Rectangle:
            {
                if (element is not RectangleElement r) return;
                var left = Math.Min(start.X, current.X);
                var top = Math.Min(start.Y, current.Y);
                var right = Math.Max(start.X, current.X);
                var bottom = Math.Max(start.Y, current.Y);
                r.Rect = new RectF(left, top, right - left, bottom - top);
                return;
            }

            case AnalysisShapeType.Circle:
            {
                if (element is not EllipseElement c) return;
                var dx = current.X - start.X;
                var dy = current.Y - start.Y;
                var size = Math.Max(Math.Abs(dx), Math.Abs(dy));
                var left = dx >= 0 ? start.X : start.X - (float)size;
                var top = dy >= 0 ? start.Y : start.Y - (float)size;
                c.Rect = new RectF(left, top, (float)size, (float)size);
                return;
            }

            case AnalysisShapeType.Line:
            {
                if (element is not LineElement l) return;
                l.Start = start;
                l.End = current;
                return;
            }

            case AnalysisShapeType.Arrow:
            {
                if (element is not ArrowElement a) return;
                a.Start = start;
                a.End = current;
                return;
            }
        }
    }

    private sealed class DrawingDrawable : IDrawable
    {
        public List<IDrawingElement> Elements { get; } = new();

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.SaveState();

            foreach (var element in Elements)
                element.Draw(canvas, dirtyRect);

            canvas.RestoreState();
        }
    }

    public interface IDrawingElement
    {
        void Draw(ICanvas canvas, RectF bounds);
    }

    private sealed class StrokeElement : IDrawingElement
    {
        public List<PointF> Points { get; } = new();
        public Color Color { get; set; } = Color.FromArgb("#FFFF7043");
        public float StrokeSize { get; set; } = 3f;

        public void Draw(ICanvas canvas, RectF bounds)
        {
            if (Points.Count < 2)
                return;

            canvas.StrokeColor = Color;
            canvas.StrokeSize = StrokeSize;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            var p0 = Points[0];
            for (var i = 1; i < Points.Count; i++)
            {
                var p1 = Points[i];
                canvas.DrawLine(p0.X, p0.Y, p1.X, p1.Y);
                p0 = p1;
            }
        }
    }

    private sealed class RectangleElement : IDrawingElement
    {
        public RectF Rect { get; set; }
        public Color Color { get; set; } = Color.FromArgb("#FFFF7043");
        public float StrokeSize { get; set; } = 3f;

        public void Draw(ICanvas canvas, RectF bounds)
        {
            canvas.StrokeColor = Color;
            canvas.StrokeSize = StrokeSize;
            canvas.DrawRectangle(Rect);
        }
    }

    private sealed class EllipseElement : IDrawingElement
    {
        public RectF Rect { get; set; }
        public bool ForceCircle { get; set; }
        public Color Color { get; set; } = Color.FromArgb("#FFFF7043");
        public float StrokeSize { get; set; } = 3f;

        public void Draw(ICanvas canvas, RectF bounds)
        {
            canvas.StrokeColor = Color;
            canvas.StrokeSize = StrokeSize;
            canvas.DrawEllipse(Rect);
        }
    }

    private sealed class LineElement : IDrawingElement
    {
        public PointF Start { get; set; }
        public PointF End { get; set; }
        public Color Color { get; set; } = Color.FromArgb("#FFFF7043");
        public float StrokeSize { get; set; } = 3f;

        public void Draw(ICanvas canvas, RectF bounds)
        {
            canvas.StrokeColor = Color;
            canvas.StrokeSize = StrokeSize;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine(Start.X, Start.Y, End.X, End.Y);
        }
    }

    private sealed class ArrowElement : IDrawingElement
    {
        public PointF Start { get; set; }
        public PointF End { get; set; }
        public Color Color { get; set; } = Color.FromArgb("#FFFF7043");
        public float StrokeSize { get; set; } = 3f;

        public void Draw(ICanvas canvas, RectF bounds)
        {
            canvas.StrokeColor = Color;
            canvas.StrokeSize = StrokeSize;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            canvas.DrawLine(Start.X, Start.Y, End.X, End.Y);

            // Cabeza de flecha
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1)
                return;

            var ux = dx / len;
            var uy = dy / len;

            const float headLength = 14f;
            const float headAngleDeg = 28f;
            var headAngle = (float)(Math.PI * headAngleDeg / 180.0);

            // Rotaciones del vector unitario para generar las alas
            var sin = (float)Math.Sin(headAngle);
            var cos = (float)Math.Cos(headAngle);

            // Vector hacia atrás
            var bx = (float)(-ux);
            var by = (float)(-uy);

            // Ala izquierda
            var lx = bx * cos - by * sin;
            var ly = bx * sin + by * cos;

            // Ala derecha
            var rx = bx * cos + by * sin;
            var ry = -bx * sin + by * cos;

            var leftPoint = new PointF(End.X + lx * headLength, End.Y + ly * headLength);
            var rightPoint = new PointF(End.X + rx * headLength, End.Y + ry * headLength);

            canvas.DrawLine(End.X, End.Y, leftPoint.X, leftPoint.Y);
            canvas.DrawLine(End.X, End.Y, rightPoint.X, rightPoint.Y);
        }
    }

    private sealed class TextElement : IDrawingElement
    {
        public PointF Position { get; set; }
        public string Text { get; set; } = string.Empty;
        public Color Color { get; set; } = Color.FromArgb("#FFFF7043");
        public float FontSize { get; set; } = 16f;

        public void Draw(ICanvas canvas, RectF bounds)
        {
            if (string.IsNullOrWhiteSpace(Text))
                return;

            canvas.FontColor = Color;
            canvas.FontSize = FontSize;

            // En MacCatalyst, el overload simple puede no renderizar como esperamos.
            // Usamos un rectángulo (como en SimpleBarChart) para asegurar layout.
            var width = Math.Max(1, bounds.Right - Position.X);
            var height = Math.Max(1, bounds.Bottom - Position.Y);
            canvas.DrawString(
                Text,
                Position.X,
                Position.Y,
                width,
                height,
                HorizontalAlignment.Left,
                VerticalAlignment.Top,
                TextFlow.ClipBounds);
        }
    }
}

public sealed class TextRequestedEventArgs : EventArgs
{
    public PointF Position { get; }

    public TextRequestedEventArgs(PointF position)
    {
        Position = position;
    }
}
