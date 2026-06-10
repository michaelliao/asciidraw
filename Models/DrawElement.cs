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

        // Optional links to rectangle elements. When the linked rectangle moves,
        // the endpoint follows, keeping the stored offset relative to the rect origin.
        public Guid? StartLink { get; set; }
        public int StartOffsetX { get; set; }
        public int StartOffsetY { get; set; }
        public Guid? EndLink { get; set; }
        public int EndOffsetX { get; set; }
        public int EndOffsetY { get; set; }

        public override void Translate(int dx, int dy)
        {
            X1 += dx;
            Y1 += dy;
            X2 += dx;
            Y2 += dy;
        }

        public override (int X, int Y, int W, int H) Bounds =>
            (Math.Min(X1, X2), Math.Min(Y1, Y2), Math.Abs(X2 - X1) + 1, Math.Abs(Y2 - Y1) + 1);

        /// <summary>Orthogonal waypoints of the step route from start to end.</summary>
        public List<(int X, int Y)> RoutePoints()
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
