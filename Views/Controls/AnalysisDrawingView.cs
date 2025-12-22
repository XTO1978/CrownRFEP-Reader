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

    private IDrawingElement? _selectedElement;

    private bool _isDraggingSelection;
    private PointF _lastDragPoint;

    public event EventHandler<TextRequestedEventArgs>? TextRequested;

    public bool HasSelection => _selectedElement != null;

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
        _selectedElement = null;
        _drawable.SelectedElement = null;
        _isDrawing = false;
        Invalidate();
    }

    public void ClearSelection()
    {
        if (_selectedElement == null)
            return;

        _selectedElement = null;
        _drawable.SelectedElement = null;
        Invalidate();
    }

    public bool DeleteSelected()
    {
        if (_selectedElement == null)
            return false;

        var removed = _drawable.Elements.Remove(_selectedElement);
        _selectedElement = null;
        _drawable.SelectedElement = null;
        Invalidate();
        return removed;
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (InputTransparent)
            return;

        var p = e.GetPosition(this);
        if (p == null)
            return;

        var point = new PointF((float)p.Value.X, (float)p.Value.Y);

        // Prioridad: selección de elementos existentes.
        // Si hay hit-test, solo seleccionamos (no empezamos un trazo/figura).
        var hit = HitTest(point);
        if (hit != null)
        {
            _selectedElement = hit;
            _drawable.SelectedElement = hit;
            _isDraggingSelection = true;
            _lastDragPoint = point;
            _isDrawing = false;
            _activeStroke = null;
            _activeShape = null;
            Invalidate();
            return;
        }

        // Click en vacío: limpiar selección (si la hubiera) y continuar con la herramienta.
        if (_selectedElement != null)
        {
            _selectedElement = null;
            _drawable.SelectedElement = null;
            _isDraggingSelection = false;
            Invalidate();
        }

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

    private RectF GetCanvasBounds()
        => new(0, 0, (float)Math.Max(1, Width), (float)Math.Max(1, Height));

    private IDrawingElement? HitTest(PointF point)
    {
        if (_drawable.Elements.Count == 0)
            return null;

        var tolerance = Math.Max(8f, InkThickness * 2f);
        var bounds = GetCanvasBounds();

        for (var i = _drawable.Elements.Count - 1; i >= 0; i--)
        {
            var element = _drawable.Elements[i];
            if (element.HitTest(point, tolerance, bounds))
                return element;
        }

        return null;
    }

    private static float DistanceSquared(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static float DistancePointToSegment(PointF p, PointF a, PointF b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var apx = p.X - a.X;
        var apy = p.Y - a.Y;

        var abLenSq = abx * abx + aby * aby;
        if (abLenSq < 0.0001f)
            return (float)Math.Sqrt(DistanceSquared(p, a));

        var t = (apx * abx + apy * aby) / abLenSq;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;

        var cx = a.X + abx * t;
        var cy = a.Y + aby * t;
        var dx = p.X - cx;
        var dy = p.Y - cy;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (InputTransparent)
            return;

        var p = e.GetPosition(this);
        if (p == null)
            return;

        var point = new PointF((float)p.Value.X, (float)p.Value.Y);

        if (_isDraggingSelection && _selectedElement != null)
        {
            var dx = point.X - _lastDragPoint.X;
            var dy = point.Y - _lastDragPoint.Y;
            if (Math.Abs(dx) > 0.001f || Math.Abs(dy) > 0.001f)
            {
                _selectedElement.Translate(dx, dy);
                _lastDragPoint = point;
                Invalidate();
            }
            return;
        }

        if (!_isDrawing)
            return;

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

        _isDraggingSelection = false;
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

        public IDrawingElement? SelectedElement { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.SaveState();

            foreach (var element in Elements)
                element.Draw(canvas, dirtyRect);

            if (SelectedElement != null)
            {
                var b = SelectedElement.GetBounds(dirtyRect);
                // Un pequeño margen para que se vea el highlight.
                b = new RectF(b.X - 4, b.Y - 4, b.Width + 8, b.Height + 8);

                canvas.StrokeColor = Colors.White;
                canvas.StrokeSize = 2f;
                canvas.DrawRectangle(b);
            }

            canvas.RestoreState();
        }
    }

    public interface IDrawingElement
    {
        void Draw(ICanvas canvas, RectF bounds);

        bool HitTest(PointF point, float tolerance, RectF canvasBounds);

        RectF GetBounds(RectF canvasBounds);

        void Translate(float dx, float dy);
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

        public bool HitTest(PointF point, float tolerance, RectF canvasBounds)
        {
            if (Points.Count == 0)
                return false;

            var tol = tolerance + StrokeSize * 0.5f;
            var tolSq = tol * tol;

            if (Points.Count == 1)
                return DistanceSquared(point, Points[0]) <= tolSq;

            for (var i = 1; i < Points.Count; i++)
            {
                var a = Points[i - 1];
                var b = Points[i];
                var d = DistancePointToSegment(point, a, b);
                if (d <= tol)
                    return true;
            }

            return false;
        }

        public RectF GetBounds(RectF canvasBounds)
        {
            if (Points.Count == 0)
                return new RectF(0, 0, 0, 0);

            var minX = Points[0].X;
            var minY = Points[0].Y;
            var maxX = Points[0].X;
            var maxY = Points[0].Y;

            for (var i = 1; i < Points.Count; i++)
            {
                var p = Points[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            var pad = StrokeSize * 0.5f;
            return new RectF(minX - pad, minY - pad, (maxX - minX) + pad * 2f, (maxY - minY) + pad * 2f);
        }

        public void Translate(float dx, float dy)
        {
            if (Points.Count == 0)
                return;

            for (var i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                Points[i] = new PointF(p.X + dx, p.Y + dy);
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

        public bool HitTest(PointF point, float tolerance, RectF canvasBounds)
        {
            var r = new RectF(Rect.X - tolerance, Rect.Y - tolerance, Rect.Width + tolerance * 2f, Rect.Height + tolerance * 2f);
            return r.Contains(point);
        }

        public RectF GetBounds(RectF canvasBounds) => Rect;

        public void Translate(float dx, float dy)
        {
            Rect = new RectF(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);
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

        public bool HitTest(PointF point, float tolerance, RectF canvasBounds)
        {
            // Hit-test aproximado a la elipse (con tolerancia).
            var cx = Rect.Center.X;
            var cy = Rect.Center.Y;
            var rx = Math.Max(1f, Rect.Width * 0.5f + tolerance);
            var ry = Math.Max(1f, Rect.Height * 0.5f + tolerance);

            var dx = point.X - cx;
            var dy = point.Y - cy;
            var v = (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry);
            return v <= 1f;
        }

        public RectF GetBounds(RectF canvasBounds) => Rect;

        public void Translate(float dx, float dy)
        {
            Rect = new RectF(Rect.X + dx, Rect.Y + dy, Rect.Width, Rect.Height);
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

        public bool HitTest(PointF point, float tolerance, RectF canvasBounds)
        {
            var tol = tolerance + StrokeSize * 0.5f;
            return DistancePointToSegment(point, Start, End) <= tol;
        }

        public RectF GetBounds(RectF canvasBounds)
        {
            var left = Math.Min(Start.X, End.X);
            var top = Math.Min(Start.Y, End.Y);
            var right = Math.Max(Start.X, End.X);
            var bottom = Math.Max(Start.Y, End.Y);
            var pad = StrokeSize * 0.5f;
            return new RectF(left - pad, top - pad, (right - left) + pad * 2f, (bottom - top) + pad * 2f);
        }

        public void Translate(float dx, float dy)
        {
            Start = new PointF(Start.X + dx, Start.Y + dy);
            End = new PointF(End.X + dx, End.Y + dy);
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

        public bool HitTest(PointF point, float tolerance, RectF canvasBounds)
        {
            var tol = tolerance + StrokeSize * 0.5f;
            return DistancePointToSegment(point, Start, End) <= tol;
        }

        public RectF GetBounds(RectF canvasBounds)
        {
            var left = Math.Min(Start.X, End.X);
            var top = Math.Min(Start.Y, End.Y);
            var right = Math.Max(Start.X, End.X);
            var bottom = Math.Max(Start.Y, End.Y);
            var pad = StrokeSize * 0.5f + 14f; // incluye cabeza de flecha
            return new RectF(left - pad, top - pad, (right - left) + pad * 2f, (bottom - top) + pad * 2f);
        }

        public void Translate(float dx, float dy)
        {
            Start = new PointF(Start.X + dx, Start.Y + dy);
            End = new PointF(End.X + dx, End.Y + dy);
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

        public bool HitTest(PointF point, float tolerance, RectF canvasBounds)
        {
            var b = GetBounds(canvasBounds);
            b = new RectF(b.X - tolerance, b.Y - tolerance, b.Width + tolerance * 2f, b.Height + tolerance * 2f);
            return b.Contains(point);
        }

        public RectF GetBounds(RectF canvasBounds)
        {
            if (string.IsNullOrWhiteSpace(Text))
                return new RectF(Position.X, Position.Y, 0, 0);

            // Aproximación simple para selección/hit-test (sin medir con la fuente real).
            var width = Math.Max(10f, Text.Length * FontSize * 0.6f);
            var height = Math.Max(10f, FontSize * 1.3f);
            return new RectF(Position.X, Position.Y, width, height);
        }

        public void Translate(float dx, float dy)
        {
            Position = new PointF(Position.X + dx, Position.Y + dy);
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
