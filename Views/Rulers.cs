using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AsciiDraw.Views
{
    public abstract class RulerBase : Control
    {
        public AsciiCanvas? Canvas { get; set; }

        private double _offset;
        public double Offset
        {
            get => _offset;
            set
            {
                if (Math.Abs(_offset - value) < 0.01)
                    return;
                _offset = value;
                InvalidateVisual();
            }
        }

        protected static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        protected static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
        protected static readonly Pen TickPen = new(new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)), 1);
        protected static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), 1);
        protected static readonly Typeface LabelTypeface = new("Segoe UI");

        protected FormattedText Label(string text) => new(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelTypeface, 9, LabelBrush);
    }

    /// <summary>Horizontal ruler above the canvas showing column numbers.</summary>
    public class ColumnRuler : RulerBase
    {
        public override void Render(DrawingContext ctx)
        {
            var b = Bounds;
            ctx.FillRectangle(Background, new Rect(0, 0, b.Width, b.Height));
            ctx.DrawLine(BorderPen, new Point(0, b.Height - 0.5), new Point(b.Width, b.Height - 0.5));
            if (Canvas == null)
                return;
            double cw = Canvas.CellWidth;
            if (cw <= 0)
                return;
            int first = Math.Max(0, (int)(Offset / cw));
            for (int c = first; c * cw - Offset <= b.Width; c++)
            {
                double x = Math.Round(c * cw - Offset) + 0.5;
                bool ten = c % 10 == 0;
                ctx.DrawLine(TickPen, new Point(x, b.Height - (ten ? 8 : 4)), new Point(x, b.Height - 1));
                if (ten && c > 0)
                {
                    var ft = Label(c.ToString(CultureInfo.InvariantCulture));
                    ctx.DrawText(ft, new Point(x - ft.Width / 2, 1));
                }
            }
        }
    }

    /// <summary>Vertical ruler to the left of the canvas showing row numbers.</summary>
    public class RowRuler : RulerBase
    {
        public override void Render(DrawingContext ctx)
        {
            var b = Bounds;
            ctx.FillRectangle(Background, new Rect(0, 0, b.Width, b.Height));
            ctx.DrawLine(BorderPen, new Point(b.Width - 0.5, 0), new Point(b.Width - 0.5, b.Height));
            if (Canvas == null)
                return;
            double ch = Canvas.CellHeight;
            if (ch <= 0)
                return;
            int first = Math.Max(0, (int)(Offset / ch));
            for (int r = first; r * ch - Offset <= b.Height; r++)
            {
                double y = r * ch - Offset;
                if (r > 0)
                {
                    var ft = Label(r.ToString(CultureInfo.InvariantCulture));
                    ctx.DrawText(ft, new Point(b.Width - ft.Width - 5, y + (ch - ft.Height) / 2));
                }
                ctx.DrawLine(TickPen, new Point(b.Width - 4, Math.Round(y) + 0.5),
                    new Point(b.Width - 1, Math.Round(y) + 0.5));
            }
        }
    }
}
