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

        // ----- Box-drawing merging -----
        // Every line char is a set of arms pointing N/S/E/W. When an element draws over
        // existing line art, the arms are unioned and the union's junction char is emitted
        // (e.g. '│' under '─' becomes '┼'). Junctions always use light-style glyphs.

        [Flags]
        private enum Arms
        {
            None = 0,
            N = 1,
            S = 2,
            E = 4,
            W = 8,
            All = N | S | E | W,
        }

        private static Arms CharArms(char c) => c switch
        {
            '─' or '╌' or '┈' => Arms.E | Arms.W,
            '│' or '╎' or '┊' => Arms.N | Arms.S,
            '┌' => Arms.E | Arms.S,
            '┐' => Arms.W | Arms.S,
            '└' => Arms.N | Arms.E,
            '┘' => Arms.N | Arms.W,
            '├' => Arms.N | Arms.S | Arms.E,
            '┤' => Arms.N | Arms.S | Arms.W,
            '┬' => Arms.E | Arms.W | Arms.S,
            '┴' => Arms.E | Arms.W | Arms.N,
            '┼' => Arms.All,
            _ => Arms.None,
        };

        private static char ArmsChar(Arms a) => a switch
        {
            Arms.N or Arms.S or (Arms.N | Arms.S) => '│',
            Arms.E or Arms.W or (Arms.E | Arms.W) => '─',
            Arms.E | Arms.S => '┌',
            Arms.W | Arms.S => '┐',
            Arms.N | Arms.E => '└',
            Arms.N | Arms.W => '┘',
            Arms.N | Arms.S | Arms.E => '├',
            Arms.N | Arms.S | Arms.W => '┤',
            Arms.E | Arms.W | Arms.S => '┬',
            Arms.E | Arms.W | Arms.N => '┴',
            _ => '┼',
        };

        /// <summary>Draws a line char, merging with line art already in the cell.
        /// <paramref name="keep"/> restricts which existing arms survive — a solid
        /// rectangle keeps only arms pointing away from its interior.</summary>
        private void MergeSet(int x, int y, char c, Arms keep = Arms.All)
        {
            var arms = CharArms(c);
            if (arms == Arms.None)
            {
                Set(x, y, c);
                return;
            }
            var existing = CharArms(Get(x, y)) & keep;
            if (existing == Arms.None || (existing | arms) == arms)
                Set(x, y, c);
            else
                Set(x, y, ArmsChar(existing | arms));
        }

        private void DrawRect(RectElement r)
        {
            int x1 = r.X, y1 = r.Y;
            int x2 = r.X + r.Width - 1, y2 = r.Y + r.Height - 1;
            bool solid = r.FillStyle == FillStyle.Solid;
            bool hasBorder = r.LineStyle != LineStyle.None && r.Width >= 2 && r.Height >= 2;

            if (solid)
            {
                // With a border the fill only blanks the interior; the border cells
                // themselves merge with the outward arms of whatever lies beneath.
                int fx1 = hasBorder ? x1 + 1 : x1, fy1 = hasBorder ? y1 + 1 : y1;
                int fx2 = hasBorder ? x2 - 1 : x2, fy2 = hasBorder ? y2 - 1 : y2;
                for (int y = fy1; y <= fy2; y++)
                    for (int x = fx1; x <= fx2; x++)
                        Set(x, y, ' ');
            }

            if (r.LineStyle != LineStyle.None)
            {
                var (hc, vc) = StyleChars(r.LineStyle);
                if (r.Width == 1 && r.Height == 1)
                {
                    MergeSet(x1, y1, hc);
                }
                else if (r.Height == 1)
                {
                    for (int x = x1; x <= x2; x++) MergeSet(x, y1, hc);
                }
                else if (r.Width == 1)
                {
                    for (int y = y1; y <= y2; y++) MergeSet(x1, y, vc);
                }
                else
                {
                    Arms top = solid ? Arms.N : Arms.All;
                    Arms bottom = solid ? Arms.S : Arms.All;
                    Arms left = solid ? Arms.W : Arms.All;
                    Arms right = solid ? Arms.E : Arms.All;
                    for (int x = x1 + 1; x < x2; x++)
                    {
                        MergeSet(x, y1, hc, top);
                        MergeSet(x, y2, hc, bottom);
                    }
                    for (int y = y1 + 1; y < y2; y++)
                    {
                        MergeSet(x1, y, vc, left);
                        MergeSet(x2, y, vc, right);
                    }
                    MergeSet(x1, y1, '┌', solid ? Arms.N | Arms.W : Arms.All);
                    MergeSet(x2, y1, '┐', solid ? Arms.N | Arms.E : Arms.All);
                    MergeSet(x1, y2, '└', solid ? Arms.S | Arms.W : Arms.All);
                    MergeSet(x2, y2, '┘', solid ? Arms.S | Arms.E : Arms.All);
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
                MergeSet(cur.X, cur.Y, c);
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
