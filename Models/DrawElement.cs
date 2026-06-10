using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AsciiDraw.Models
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(RectElement), "rect")]
    [JsonDerivedType(typeof(LineElement), "line")]
    public abstract class DrawElement
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public Guid? GroupId { get; set; }

        public abstract void Translate(int dx, int dy);

        [JsonIgnore]
        public abstract (int X, int Y, int W, int H) Bounds { get; }
    }

    public class RectElement : DrawElement
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;
        public LineStyle LineStyle { get; set; } = LineStyle.Normal;
        public FillStyle FillStyle { get; set; } = FillStyle.Transparent;
        public string Text { get; set; } = "";

        [JsonIgnore]
        public bool IsTextBox => LineStyle == LineStyle.None;

        public override void Translate(int dx, int dy)
        {
            X += dx;
            Y += dy;
        }

        public override (int X, int Y, int W, int H) Bounds => (X, Y, Width, Height);

        public bool Contains(int cx, int cy) => cx >= X && cx < X + Width && cy >= Y && cy < Y + Height;

        /// <summary>Cell position of one of the eight connection points.</summary>
        public (int X, int Y) AnchorCell(Anchor a)
        {
            int xr = X + Width - 1, yb = Y + Height - 1;
            int xm = X + Width / 2, ym = Y + Height / 2;
            return a switch
            {
                Anchor.TopLeft => (X, Y),
                Anchor.Top => (xm, Y),
                Anchor.TopRight => (xr, Y),
                Anchor.Left => (X, ym),
                Anchor.Right => (xr, ym),
                Anchor.BottomLeft => (X, yb),
                Anchor.Bottom => (xm, yb),
                _ => (xr, yb),
            };
        }
    }

    public class LineElement : DrawElement
    {
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public LineStyle LineStyle { get; set; } = LineStyle.Normal;
        public ArrowStyle StartArrow { get; set; } = ArrowStyle.None;
        public ArrowStyle EndArrow { get; set; } = ArrowStyle.Triangle;

        // Optional links to rectangle connection points. When the linked rectangle
        // moves or resizes, the endpoint follows its anchor.
        public Guid? StartLink { get; set; }
        public Anchor StartAnchor { get; set; }
        public Guid? EndLink { get; set; }
        public Anchor EndAnchor { get; set; }

        public override void Translate(int dx, int dy)
        {
            X1 += dx;
            Y1 += dy;
            X2 += dx;
            Y2 += dy;
        }

        public override (int X, int Y, int W, int H) Bounds =>
            (Math.Min(X1, X2), Math.Min(Y1, Y2), Math.Abs(X2 - X1) + 1, Math.Abs(Y2 - Y1) + 1);

        /// <summary>Outward exit direction of a connection point (corners exit horizontally).</summary>
        public static (int X, int Y) AnchorDir(Anchor a) => a switch
        {
            Anchor.Top => (0, -1),
            Anchor.Bottom => (0, 1),
            Anchor.Left or Anchor.TopLeft or Anchor.BottomLeft => (-1, 0),
            _ => (1, 0),
        };

        private const int StubLength = 2;

        /// <summary>Orthogonal waypoints of the step route from start to end.
        /// A linked endpoint first leaves its rectangle through a short stub in the
        /// anchor's outward direction before the route bends toward the other end.</summary>
        public List<(int X, int Y)> RoutePoints()
        {
            bool linked1 = StartLink != null, linked2 = EndLink != null;

            if (!linked1 && !linked2)
            {
                if (X1 == X2 || Y1 == Y2)
                    return new List<(int, int)> { (X1, Y1), (X2, Y2) };

                if (Math.Abs(X2 - X1) >= Math.Abs(Y2 - Y1))
                {
                    int mid = (X1 + X2) / 2;
                    return new List<(int, int)> { (X1, Y1), (mid, Y1), (mid, Y2), (X2, Y2) };
                }
                else
                {
                    int mid = (Y1 + Y2) / 2;
                    return new List<(int, int)> { (X1, Y1), (X1, mid), (X2, mid), (X2, Y2) };
                }
            }

            var d1 = linked1 ? AnchorDir(StartAnchor) : default;
            var d2 = linked2 ? AnchorDir(EndAnchor) : default;
            var s1 = linked1 ? (X: X1 + d1.X * StubLength, Y: Y1 + d1.Y * StubLength) : (X: X1, Y: Y1);
            var s2 = linked2 ? (X: X2 + d2.X * StubLength, Y: Y2 + d2.Y * StubLength) : (X: X2, Y: Y2);

            var pts = new List<(int X, int Y)> { (X1, Y1) };
            if (linked1)
                pts.Add(s1);
            // After a horizontal stub the route turns vertical first (and vice versa);
            // with only the end linked, bend so the last leg meets the stub at a right angle.
            bool verticalFirst = linked1 ? d1.Y == 0 : d2.Y != 0;
            pts.Add(verticalFirst ? (s1.X, s2.Y) : (s2.X, s1.Y));
            pts.Add(s2);
            if (linked2)
                pts.Add((X2, Y2));
            return pts;
        }

        /// <summary>Every grid cell the line passes through, in order from start to end.</summary>
        public List<(int X, int Y)> RouteCells()
        {
            var pts = RoutePoints();
            var cells = new List<(int X, int Y)>();
            var cur = pts[0];
            cells.Add(cur);
            for (int k = 1; k < pts.Count; k++)
            {
                var target = pts[k];
                while (cur != target)
                {
                    if (cur.X != target.X)
                        cur = (cur.X + Math.Sign(target.X - cur.X), cur.Y);
                    else
                        cur = (cur.X, cur.Y + Math.Sign(target.Y - cur.Y));
                    cells.Add(cur);
                }
            }
            return cells;
        }
    }

    public class GroupInfo
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
    }
}
