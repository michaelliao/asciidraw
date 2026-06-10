using System;
using System.Collections.Generic;
using System.Linq;

namespace AsciiDraw.Models
{
    /// <summary>
    /// Rasterizes a document into a fixed-size grid of characters using
    /// Unicode box-drawing glyphs. Elements are drawn in list order, so later
    /// elements overwrite earlier ones (z-order bottom to top).
    /// </summary>
    public sealed class CharGrid
    {
        public int Cols { get; }
        public int Rows { get; }
        private readonly char[] _cells;

        public CharGrid(int cols, int rows)
        {
            Cols = cols;
            Rows = rows;
            _cells = new char[cols * rows];
            Array.Fill(_cells, ' ');
        }

        public void Set(int x, int y, char c)
        {
            if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                _cells[y * Cols + x] = c;
        }

        public char Get(int x, int y) =>
            x >= 0 && x < Cols && y >= 0 && y < Rows ? _cells[y * Cols + x] : ' ';

        public string RowString(int y) => new string(_cells, y * Cols, Cols);

        public static CharGrid Render(DrawDocument doc)
        {
            var grid = new CharGrid(doc.Columns, doc.Rows);
            foreach (var el in doc.Elements)
            {
                switch (el)
                {
                    case RectElement r:
                        grid.DrawRect(r);
                        break;
                    case LineElement l:
                        grid.DrawLine(l);
                        break;
                }
            }
            return grid;
        }

        private static (char H, char V) StyleChars(LineStyle s) => s switch
        {
            LineStyle.Dashed => ('╌', '╎'),
            LineStyle.Dotted => ('┈', '┊'),
            _ => ('─', '│'),
        };

        private void DrawRect(RectElement r)
        {
            int x1 = r.X, y1 = r.Y;
            int x2 = r.X + r.Width - 1, y2 = r.Y + r.Height - 1;

            if (r.FillStyle == FillStyle.Solid)
            {
                for (int y = y1; y <= y2; y++)
                    for (int x = x1; x <= x2; x++)
                        Set(x, y, ' ');
            }

            if (r.LineStyle != LineStyle.None)
            {
                var (hc, vc) = StyleChars(r.LineStyle);
                if (r.Width == 1 && r.Height == 1)
                {
                    Set(x1, y1, hc);
                }
                else if (r.Height == 1)
                {
                    for (int x = x1; x <= x2; x++) Set(x, y1, hc);
                }
                else if (r.Width == 1)
                {
                    for (int y = y1; y <= y2; y++) Set(x1, y, vc);
                }
                else
                {
                    for (int x = x1 + 1; x < x2; x++)
                    {
                        Set(x, y1, hc);
                        Set(x, y2, hc);
                    }
                    for (int y = y1 + 1; y < y2; y++)
                    {
                        Set(x1, y, vc);
                        Set(x2, y, vc);
                    }
                    Set(x1, y1, '┌');
                    Set(x2, y1, '┐');
                    Set(x1, y2, '└');
                    Set(x2, y2, '┘');
                }
            }

            if (!string.IsNullOrEmpty(r.Text))
            {
                bool border = r.LineStyle != LineStyle.None && r.Width > 2 && r.Height > 2;
                int inset = border ? 1 : 0;
                int ix = r.X + inset, iy = r.Y + inset;
                int iw = r.Width - inset * 2, ih = r.Height - inset * 2;
                if (iw > 0 && ih > 0)
                {
                    var lines = Wrap(r.Text, iw);
                    if (lines.Count > ih)
                        lines = lines.Take(ih).ToList();
                    int top = iy + (ih - lines.Count) / 2;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        string ln = lines[i];
                        int left = ix + Math.Max(0, (iw - ln.Length) / 2);
                        for (int j = 0; j < ln.Length && j < iw; j++)
                            Set(left + j, top + i, ln[j]);
                    }
                }
            }
        }

        public static List<string> Wrap(string text, int width)
        {
            var result = new List<string>();
            if (width <= 0)
                return result;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Length <= width)
                {
                    result.Add(raw);
                    continue;
                }
                string cur = "";
                foreach (var word in raw.Split(' '))
                {
                    var w = word;
                    // Hard-break words longer than the available width.
                    while (w.Length > width)
                    {
                        if (cur.Length > 0)
                        {
                            result.Add(cur);
                            cur = "";
                        }
                        result.Add(w[..width]);
                        w = w[width..];
                    }
                    if (cur.Length == 0)
                        cur = w;
                    else if (cur.Length + 1 + w.Length <= width)
                        cur += " " + w;
                    else
                    {
                        result.Add(cur);
                        cur = w;
                    }
                }
                result.Add(cur);
            }
            return result;
        }

        private void DrawLine(LineElement l)
        {
            var cells = l.RouteCells();
            var (hc, vc) = StyleChars(l.LineStyle);
            int n = cells.Count;

            for (int i = 0; i < n; i++)
            {
                var cur = cells[i];
                char c;
                if (n == 1)
                {
                    c = hc;
                }
                else if (i == 0)
                {
                    var next = cells[1];
                    c = next.Y == cur.Y ? hc : vc;
                }
                else if (i == n - 1)
                {
                    var prev = cells[i - 1];
                    c = prev.Y == cur.Y ? hc : vc;
                }
                else
                {
                    var prev = cells[i - 1];
                    var next = cells[i + 1];
                    if (prev.X == next.X)
                        c = vc;
                    else if (prev.Y == next.Y)
                        c = hc;
                    else
                        c = Corner(prev, cur, next);
                }
                Set(cur.X, cur.Y, c);
            }

            if (n > 1)
            {
                if (l.StartArrow == ArrowStyle.Triangle)
                    Set(cells[0].X, cells[0].Y, Arrow(cells[1], cells[0]));
                if (l.EndArrow == ArrowStyle.Triangle)
                    Set(cells[n - 1].X, cells[n - 1].Y, Arrow(cells[n - 2], cells[n - 1]));
            }
        }

        // Corner glyph at `cur` joining the neighbor cells `prev` and `next`.
        private static char Corner((int X, int Y) prev, (int X, int Y) cur, (int X, int Y) next)
        {
            bool east = prev.X > cur.X || next.X > cur.X;
            bool west = prev.X < cur.X || next.X < cur.X;
            bool north = prev.Y < cur.Y || next.Y < cur.Y;
            bool south = prev.Y > cur.Y || next.Y > cur.Y;
            if (east && south) return '┌';
            if (west && south) return '┐';
            if (east && north) return '└';
            if (west && north) return '┘';
            return '─';
        }

        // Arrow glyph pointing from `from` toward `tip`.
        private static char Arrow((int X, int Y) from, (int X, int Y) tip)
        {
            if (tip.X < from.X) return '◀';
            if (tip.X > from.X) return '▶';
            if (tip.Y < from.Y) return '▲';
            return '▼';
        }

        /// <summary>Bounding box of all non-space cells, or null when empty.</summary>
        public (int MinX, int MinY, int MaxX, int MaxY)? UsedBounds()
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                    if (_cells[y * Cols + x] != ' ')
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            return maxX < 0 ? null : (minX, minY, maxX, maxY);
        }
    }
}
