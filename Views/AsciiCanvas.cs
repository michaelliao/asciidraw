using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using AsciiDraw.Models;
using AsciiDraw.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AsciiDraw.Views
{
    /// <summary>
    /// The Monodraw-style edit area: a character grid with light grid lines,
    /// rendered with a monospace font, plus selection handles and tool interaction.
    /// </summary>
    public class AsciiCanvas : Control
    {
        private MainWindowViewModel? _vm;

        private readonly Typeface _typeface = new("Cascadia Mono,Consolas,Courier New");
        private double _fontSize = 14;
        public double CellWidth { get; private set; } = 8;
        public double CellHeight { get; private set; } = 17;
        public event Action? MetricsChanged;

        // Indexed by handle: 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L.
        private static readonly Cursor[] HandleCursors =
        {
            new(StandardCursorType.TopLeftCorner),
            new(StandardCursorType.SizeNorthSouth),
            new(StandardCursorType.TopRightCorner),
            new(StandardCursorType.SizeWestEast),
            new(StandardCursorType.BottomRightCorner),
            new(StandardCursorType.SizeNorthSouth),
            new(StandardCursorType.BottomLeftCorner),
            new(StandardCursorType.SizeWestEast),
        };
        private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
        private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

        private static readonly Color SelectionColor = Color.FromRgb(0x2F, 0x7D, 0xF6);
        private static readonly IBrush SelectionBrush = new SolidColorBrush(SelectionColor);
        private static readonly IBrush SelectionFillBrush = new SolidColorBrush(SelectionColor, 0.12);
        private static readonly IBrush GridBrushMinor = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
        private static readonly IBrush GridBrushMajor = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));

        private enum DragMode { None, Move, Resize, LineEnd, Create, Rubber }

        private DragMode _drag = DragMode.None;
        private (int X, int Y) _pressCell, _lastCell;
        private Point _pressPoint, _currentPoint;
        private bool _movedSinceUndo;
        private DrawElement? _createElement;
        private RectElement? _resizeRect;
        private int _resizeHandle = -1;
        private (int X, int Y, int W, int H) _resizeOrig;
        private LineElement? _endLine;
        private bool _endIsStart;
        private RectElement? _snapRect;
        private Anchor? _snapAnchor;

        public AsciiCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_vm != null)
            {
                _vm.DocumentChanged -= OnVmInvalidate;
                _vm.SelectionChanged -= OnVmInvalidate;
                _vm.PropertyChanged -= OnVmPropertyChanged;
            }
            _vm = DataContext as MainWindowViewModel;
            if (_vm != null)
            {
                _vm.DocumentChanged += OnVmInvalidate;
                _vm.SelectionChanged += OnVmInvalidate;
                _vm.PropertyChanged += OnVmPropertyChanged;
                UpdateMetrics();
            }
        }

        private void OnVmInvalidate()
        {
            InvalidateMeasure();
            InvalidateVisual();
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ZoomText))
                UpdateMetrics();
            else if (e.PropertyName == nameof(MainWindowViewModel.CurrentTool))
                UpdateCursor();
        }

        private void UpdateMetrics()
        {
            _fontSize = 14.0 * (_vm?.ZoomFactor ?? 1.0);
            var probe = new FormattedText("0", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black);
            CellWidth = probe.Width;
            CellHeight = probe.Height;
            InvalidateMeasure();
            InvalidateVisual();
            MetricsChanged?.Invoke();
        }

        private void UpdateCursor()
        {
            Cursor = _vm?.CurrentTool is Tool.Rect or Tool.Text or Tool.Line
                ? CrossCursor
                : Cursor.Default;
        }

        private void UpdateHoverCursor(Point pt)
        {
            var vm = _vm!;
            if (vm.CurrentTool != Tool.Select)
            {
                Cursor = CrossCursor;
                return;
            }
            if (vm.Selection.Count == 1)
            {
                switch (vm.SingleSelected)
                {
                    case RectElement r:
                        int hi = HitHandle(r, pt);
                        if (hi >= 0)
                        {
                            Cursor = HandleCursors[hi];
                            return;
                        }
                        break;
                    case LineElement l:
                        if (HitLineEnd(l, pt) > 0)
                        {
                            Cursor = MoveCursor;
                            return;
                        }
                        break;
                }
            }
            Cursor = Cursor.Default;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_vm == null)
                return new Size(100, 100);
            return new Size(_vm.Document.Columns * CellWidth + 1, _vm.Document.Rows * CellHeight + 1);
        }

        public override void Render(DrawingContext ctx)
        {
            var vm = _vm;
            if (vm == null)
                return;
            var doc = vm.Document;
            int cols = doc.Columns, rows = doc.Rows;
            double w = cols * CellWidth, h = rows * CellHeight;

            ctx.FillRectangle(Brushes.White, new Rect(0, 0, w, h));

            var minorPen = new Pen(GridBrushMinor, 1);
            var majorPen = new Pen(GridBrushMajor, 1);
            for (int c = 0; c <= cols; c++)
            {
                double x = Math.Round(c * CellWidth) + 0.5;
                ctx.DrawLine(c % 10 == 0 ? majorPen : minorPen, new Point(x, 0), new Point(x, h));
            }
            for (int r = 0; r <= rows; r++)
            {
                double y = Math.Round(r * CellHeight) + 0.5;
                ctx.DrawLine(r % 10 == 0 ? majorPen : minorPen, new Point(0, y), new Point(w, y));
            }

            var grid = CharGrid.Render(doc);
            for (int r = 0; r < rows; r++)
            {
                string s = grid.RowString(r);
                if (string.IsNullOrWhiteSpace(s))
                    continue;
                var ft = new FormattedText(s, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black);
                ctx.DrawText(ft, new Point(0, r * CellHeight));
            }

            // Selection overlays.
            var selPen = new Pen(SelectionBrush, 1.5);
            foreach (var el in doc.Elements)
            {
                if (!vm.Selection.Contains(el.Id))
                    continue;
                switch (el)
                {
                    case RectElement r:
                        ctx.DrawRectangle(null, selPen, PixelRect(r));
                        break;
                    case LineElement l:
                        foreach (var cell in l.RouteCells())
                            ctx.FillRectangle(SelectionFillBrush, CellRect(cell));
                        break;
                }
            }

            if (vm.Selection.Count == 1)
            {
                switch (vm.SingleSelected)
                {
                    case RectElement r:
                        foreach (var (pt, _) in RectHandles(r))
                            DrawHandle(ctx, pt);
                        break;
                    case LineElement l:
                        DrawHandle(ctx, CellCenter((l.X1, l.Y1)));
                        DrawHandle(ctx, CellCenter((l.X2, l.Y2)));
                        break;
                }
            }

            // Connection points of the rectangle a dragged line endpoint may snap to.
            if (_snapRect != null && doc.Elements.Contains(_snapRect))
            {
                var portPen = new Pen(SelectionBrush, 1.5);
                foreach (Anchor a in Enum.GetValues<Anchor>())
                {
                    var center = CellCenter(_snapRect.AnchorCell(a));
                    bool active = a == _snapAnchor;
                    double radius = active ? 4.5 : 3.5;
                    ctx.DrawEllipse(active ? SelectionBrush : Brushes.White, portPen,
                        center, radius, radius);
                }
            }

            if (_drag == DragMode.Rubber)
            {
                var rubber = new Rect(_pressPoint, _currentPoint).Normalize();
                ctx.DrawRectangle(SelectionFillBrush,
                    new Pen(SelectionBrush, 1, DashStyle.Dash), rubber);
            }
        }

        private void DrawHandle(DrawingContext ctx, Point center)
        {
            var rect = new Rect(center.X - 3.5, center.Y - 3.5, 7, 7);
            ctx.DrawRectangle(Brushes.White, new Pen(SelectionBrush, 1.5), rect);
        }

        private Rect PixelRect(RectElement r) =>
            new(r.X * CellWidth, r.Y * CellHeight, r.Width * CellWidth, r.Height * CellHeight);

        private Rect CellRect((int X, int Y) cell) =>
            new(cell.X * CellWidth, cell.Y * CellHeight, CellWidth, CellHeight);

        private Point CellCenter((int X, int Y) cell) =>
            new((cell.X + 0.5) * CellWidth, (cell.Y + 0.5) * CellHeight);

        // Handle indices: 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L.
        private IEnumerable<(Point Pt, int Idx)> RectHandles(RectElement r)
        {
            var rc = PixelRect(r);
            double cx = rc.X + rc.Width / 2, cy = rc.Y + rc.Height / 2;
            yield return (new Point(rc.X, rc.Y), 0);
            yield return (new Point(cx, rc.Y), 1);
            yield return (new Point(rc.Right, rc.Y), 2);
            yield return (new Point(rc.Right, cy), 3);
            yield return (new Point(rc.Right, rc.Bottom), 4);
            yield return (new Point(cx, rc.Bottom), 5);
            yield return (new Point(rc.X, rc.Bottom), 6);
            yield return (new Point(rc.X, cy), 7);
        }

        private int HitHandle(RectElement r, Point pt)
        {
            foreach (var (hp, idx) in RectHandles(r))
                if (Math.Abs(pt.X - hp.X) <= 5 && Math.Abs(pt.Y - hp.Y) <= 5)
                    return idx;
            return -1;
        }

        /// <summary>0 = none, 1 = start endpoint, 2 = end endpoint.</summary>
        private int HitLineEnd(LineElement l, Point pt)
        {
            var s = CellCenter((l.X1, l.Y1));
            var e = CellCenter((l.X2, l.Y2));
            if (Math.Abs(pt.X - s.X) <= 6 && Math.Abs(pt.Y - s.Y) <= 6) return 1;
            if (Math.Abs(pt.X - e.X) <= 6 && Math.Abs(pt.Y - e.Y) <= 6) return 2;
            return 0;
        }

        private (int X, int Y) CellAt(Point pt)
        {
            var vm = _vm!;
            int x = (int)Math.Floor(pt.X / CellWidth);
            int y = (int)Math.Floor(pt.Y / CellHeight);
            return (Math.Clamp(x, 0, vm.Document.Columns - 1), Math.Clamp(y, 0, vm.Document.Rows - 1));
        }

        // ----- Pointer interaction -----

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var vm = _vm;
            if (vm == null)
                return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            Focus();

            var pt = e.GetPosition(this);
            var cell = CellAt(pt);
            _pressCell = _lastCell = cell;
            _pressPoint = _currentPoint = pt;
            _movedSinceUndo = false;
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            switch (vm.CurrentTool)
            {
                case Tool.Rect:
                case Tool.Text:
                {
                    vm.PushUndo();
                    var r = new RectElement { X = cell.X, Y = cell.Y, Width = 1, Height = 1 };
                    if (vm.CurrentTool == Tool.Text)
                        r.LineStyle = LineStyle.None;
                    vm.AddElement(r, isTextBox: vm.CurrentTool == Tool.Text);
                    _createElement = r;
                    _drag = DragMode.Create;
                    break;
                }
                case Tool.Line:
                {
                    vm.PushUndo();
                    var l = new LineElement { X1 = cell.X, Y1 = cell.Y, X2 = cell.X, Y2 = cell.Y };
                    vm.AddElement(l);
                    var snap = vm.SnapLineEndpoint(l, start: true, cell.X, cell.Y);
                    l.X2 = l.X1;
                    l.Y2 = l.Y1;
                    _snapRect = snap?.Rect;
                    _snapAnchor = snap?.Anchor;
                    _createElement = l;
                    _drag = DragMode.Create;
                    break;
                }
                case Tool.Select:
                {
                    if (vm.Selection.Count == 1)
                    {
                        switch (vm.SingleSelected)
                        {
                            case RectElement rr when HitHandle(rr, pt) is int hi and >= 0:
                                vm.PushUndo();
                                _drag = DragMode.Resize;
                                _resizeRect = rr;
                                _resizeHandle = hi;
                                _resizeOrig = (rr.X, rr.Y, rr.Width, rr.Height);
                                goto captured;
                            case LineElement ll when HitLineEnd(ll, pt) is int ei and > 0:
                                vm.PushUndo();
                                _drag = DragMode.LineEnd;
                                _endLine = ll;
                                _endIsStart = ei == 1;
                                goto captured;
                        }
                    }

                    var hit = vm.HitTest(cell.X, cell.Y);
                    if (hit != null)
                    {
                        var ids = vm.ExpandToGroup(hit);
                        if (shift)
                            vm.ToggleSelection(ids);
                        else if (!vm.Selection.Contains(hit.Id))
                            vm.SetSelection(ids);
                        _drag = DragMode.Move;
                    }
                    else
                    {
                        if (!shift)
                            vm.ClearSelection();
                        _drag = DragMode.Rubber;
                    }
                    break;
                }
            }

        captured:
            e.Pointer.Capture(this);
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var vm = _vm;
            if (vm == null)
                return;
            var pt = e.GetPosition(this);
            var cell = CellAt(pt);
            _currentPoint = pt;
            vm.CursorStatus = $"{cell.X}, {cell.Y}";

            if (_drag == DragMode.None)
                UpdateHoverCursor(pt);

            switch (_drag)
            {
                case DragMode.Create:
                    if (_createElement is RectElement r)
                    {
                        r.X = Math.Min(_pressCell.X, cell.X);
                        r.Y = Math.Min(_pressCell.Y, cell.Y);
                        r.Width = Math.Abs(cell.X - _pressCell.X) + 1;
                        r.Height = Math.Abs(cell.Y - _pressCell.Y) + 1;
                        vm.NotifyDocumentChanged();
                    }
                    else if (_createElement is LineElement l)
                    {
                        var snap = vm.SnapLineEndpoint(l, start: false, cell.X, cell.Y);
                        _snapRect = snap?.Rect;
                        _snapAnchor = snap?.Anchor;
                    }
                    break;

                case DragMode.Move:
                {
                    int dx = cell.X - _lastCell.X, dy = cell.Y - _lastCell.Y;
                    if (dx != 0 || dy != 0)
                    {
                        if (!_movedSinceUndo)
                        {
                            vm.PushUndo();
                            _movedSinceUndo = true;
                        }
                        vm.TranslateSelected(dx, dy);
                        _lastCell = cell;
                    }
                    break;
                }

                case DragMode.Resize:
                    ApplyResize(cell);
                    vm.GeometryChanged();
                    break;

                case DragMode.LineEnd:
                    if (_endLine != null)
                    {
                        var snap = vm.SnapLineEndpoint(_endLine, _endIsStart, cell.X, cell.Y);
                        _snapRect = snap?.Rect;
                        _snapAnchor = snap?.Anchor;
                    }
                    break;

                case DragMode.Rubber:
                    InvalidateVisual();
                    break;
            }
        }

        private void ApplyResize((int X, int Y) cell)
        {
            if (_resizeRect == null)
                return;
            int dx = cell.X - _pressCell.X, dy = cell.Y - _pressCell.Y;
            var (ox, oy, ow, oh) = _resizeOrig;
            int nx = ox, ny = oy, nw = ow, nh = oh;

            bool left = _resizeHandle is 0 or 6 or 7;
            bool right = _resizeHandle is 2 or 3 or 4;
            bool top = _resizeHandle is 0 or 1 or 2;
            bool bottom = _resizeHandle is 4 or 5 or 6;

            if (left) { nx = ox + dx; nw = ow - dx; }
            if (right) { nw = ow + dx; }
            if (top) { ny = oy + dy; nh = oh - dy; }
            if (bottom) { nh = oh + dy; }

            if (nw < 1) { nw = 1; if (left) nx = ox + ow - 1; }
            if (nh < 1) { nh = 1; if (top) ny = oy + oh - 1; }

            _resizeRect.X = nx;
            _resizeRect.Y = ny;
            _resizeRect.Width = nw;
            _resizeRect.Height = nh;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            var vm = _vm;
            if (vm == null)
                return;

            switch (_drag)
            {
                case DragMode.Create:
                    if (_createElement is RectElement r)
                    {
                        if (!r.IsTextBox)
                        {
                            r.Width = Math.Max(r.Width, 2);
                            r.Height = Math.Max(r.Height, 2);
                        }
                        else
                        {
                            r.Width = Math.Max(r.Width, 4);
                            if (string.IsNullOrEmpty(r.Text))
                                r.Text = "Text";
                        }
                    }
                    if (_createElement != null)
                        vm.SetSelection(new[] { _createElement.Id });
                    vm.CurrentTool = Tool.Select;
                    vm.NotifyStructureChanged();
                    break;

                case DragMode.Move:
                    vm.SyncLinkedLines();
                    vm.NotifyDocumentChanged();
                    break;

                case DragMode.Resize:
                    vm.GeometryChanged();
                    break;

                case DragMode.LineEnd:
                    vm.GeometryChanged();
                    break;

                case DragMode.Rubber:
                {
                    var cell = CellAt(e.GetPosition(this));
                    int x1 = Math.Min(_pressCell.X, cell.X), x2 = Math.Max(_pressCell.X, cell.X);
                    int y1 = Math.Min(_pressCell.Y, cell.Y), y2 = Math.Max(_pressCell.Y, cell.Y);
                    if (x2 > x1 || y2 > y1)
                    {
                        var ids = new HashSet<Guid>(
                            e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? vm.Selection : Enumerable.Empty<Guid>());
                        foreach (var el in vm.Document.Elements)
                        {
                            var (bx, by, bw, bh) = el.Bounds;
                            if (bx <= x2 && bx + bw - 1 >= x1 && by <= y2 && by + bh - 1 >= y1)
                                foreach (var id in vm.ExpandToGroup(el))
                                    ids.Add(id);
                        }
                        vm.SetSelection(ids);
                    }
                    break;
                }
            }

            _drag = DragMode.None;
            _createElement = null;
            _resizeRect = null;
            _endLine = null;
            _snapRect = null;
            _snapAnchor = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }

        // ----- Keyboard -----

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var vm = _vm;
            if (vm == null)
                return;

            switch (e.Key)
            {
                case Key.Delete:
                case Key.Back:
                    vm.DeleteSelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.ClearSelection();
                    e.Handled = true;
                    break;
                case Key.Left:
                    Nudge(-1, 0);
                    e.Handled = true;
                    break;
                case Key.Right:
                    Nudge(1, 0);
                    e.Handled = true;
                    break;
                case Key.Up:
                    Nudge(0, -1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    Nudge(0, 1);
                    e.Handled = true;
                    break;
            }
        }

        private void Nudge(int dx, int dy)
        {
            var vm = _vm;
            if (vm == null || vm.Selection.Count == 0)
                return;
            vm.PushUndo("nudge");
            vm.TranslateSelected(dx, dy);
        }
    }
}
