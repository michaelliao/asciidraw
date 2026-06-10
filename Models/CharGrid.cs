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

        // ----- Weighted box-drawing merging -----
        // A cell's line art is four arms (N/S/E/W), each with a weight:
        // 0 none, 1 light, 2 heavy (Bold), 3 double. A key packs the four
        // weights into a byte (N | S<<2 | E<<4 | W<<6). Drawing over existing
        // line art unions the arms per direction (taking the heavier weight)
        // and emits the glyph for the union, so '│' under '━' becomes '┿'.
        // Unicode has no heavy×double junction glyphs, so those fall back to
        // the nearest match, preferring double ('╬' for a full crossing).

        private const int KeepAll = 0xFF;
        private const int KeepN = 0b0000_0011;
        private const int KeepS = 0b0000_1100;
        private const int KeepE = 0b0011_0000;
        private const int KeepW = 0b1100_0000;

        private static int Key(int n, int s, int e, int w) => n | (s << 2) | (e << 4) | (w << 6);

        private static readonly (char C, int N, int S, int E, int W)[] CharTable =
        {
            // straights and half-lines (light/heavy)
            ('─', 0, 0, 1, 1), ('━', 0, 0, 2, 2), ('│', 1, 1, 0, 0), ('┃', 2, 2, 0, 0),
            ('╴', 0, 0, 0, 1), ('╵', 1, 0, 0, 0), ('╶', 0, 0, 1, 0), ('╷', 0, 1, 0, 0),
            ('╸', 0, 0, 0, 2), ('╹', 2, 0, 0, 0), ('╺', 0, 0, 2, 0), ('╻', 0, 2, 0, 0),
            ('╼', 0, 0, 2, 1), ('╽', 1, 2, 0, 0), ('╾', 0, 0, 1, 2), ('╿', 2, 1, 0, 0),
            // corners (light/heavy)
            ('┌', 0, 1, 1, 0), ('┍', 0, 1, 2, 0), ('┎', 0, 2, 1, 0), ('┏', 0, 2, 2, 0),
            ('┐', 0, 1, 0, 1), ('┑', 0, 1, 0, 2), ('┒', 0, 2, 0, 1), ('┓', 0, 2, 0, 2),
            ('└', 1, 0, 1, 0), ('┕', 1, 0, 2, 0), ('┖', 2, 0, 1, 0), ('┗', 2, 0, 2, 0),
            ('┘', 1, 0, 0, 1), ('┙', 1, 0, 0, 2), ('┚', 2, 0, 0, 1), ('┛', 2, 0, 0, 2),
            // tees (light/heavy)
            ('├', 1, 1, 1, 0), ('┝', 1, 1, 2, 0), ('┞', 2, 1, 1, 0), ('┟', 1, 2, 1, 0),
            ('┠', 2, 2, 1, 0), ('┡', 2, 1, 2, 0), ('┢', 1, 2, 2, 0), ('┣', 2, 2, 2, 0),
            ('┤', 1, 1, 0, 1), ('┥', 1, 1, 0, 2), ('┦', 2, 1, 0, 1), ('┧', 1, 2, 0, 1),
            ('┨', 2, 2, 0, 1), ('┩', 2, 1, 0, 2), ('┪', 1, 2, 0, 2), ('┫', 2, 2, 0, 2),
            ('┬', 0, 1, 1, 1), ('┭', 0, 1, 1, 2), ('┮', 0, 1, 2, 1), ('┯', 0, 1, 2, 2),
            ('┰', 0, 2, 1, 1), ('┱', 0, 2, 1, 2), ('┲', 0, 2, 2, 1), ('┳', 0, 2, 2, 2),
            ('┴', 1, 0, 1, 1), ('┵', 1, 0, 1, 2), ('┶', 1, 0, 2, 1), ('┷', 1, 0, 2, 2),
            ('┸', 2, 0, 1, 1), ('┹', 2, 0, 1, 2), ('┺', 2, 0, 2, 1), ('┻', 2, 0, 2, 2),
            // crosses (light/heavy)
            ('┼', 1, 1, 1, 1), ('┽', 1, 1, 1, 2), ('┾', 1, 1, 2, 1), ('┿', 1, 1, 2, 2),
            ('╀', 2, 1, 1, 1), ('╁', 1, 2, 1, 1), ('╂', 2, 2, 1, 1), ('╃', 2, 1, 1, 2),
            ('╄', 2, 1, 2, 1), ('╅', 1, 2, 1, 2), ('╆', 1, 2, 2, 1), ('╇', 2, 1, 2, 2),
            ('╈', 1, 2, 2, 2), ('╉', 2, 2, 1, 2), ('╊', 2, 2, 2, 1), ('╋', 2, 2, 2, 2),
            // double
            ('═', 0, 0, 3, 3), ('║', 3, 3, 0, 0),
            ('╔', 0, 3, 3, 0), ('╗', 0, 3, 0, 3), ('╚', 3, 0, 3, 0), ('╝', 3, 0, 0, 3),
            ('╠', 3, 3, 3, 0), ('╣', 3, 3, 0, 3), ('╦', 0, 3, 3, 3), ('╩', 3, 0, 3, 3),
            ('╬', 3, 3, 3, 3),
            // double/light mixes
            ('╒', 0, 1, 3, 0), ('╓', 0, 3, 1, 0), ('╕', 0, 1, 0, 3), ('╖', 0, 3, 0, 1),
            ('╘', 1, 0, 3, 0), ('╙', 3, 0, 1, 0), ('╛', 1, 0, 0, 3), ('╜', 3, 0, 0, 1),
            ('╞', 1, 1, 3, 0), ('╟', 3, 3, 1, 0), ('╡', 1, 1, 0, 3), ('╢', 3, 3, 0, 1),
            ('╤', 0, 1, 3, 3), ('╥', 0, 3, 1, 1), ('╧', 1, 0, 3, 3), ('╨', 3, 0, 1, 1),
            ('╪', 1, 1, 3, 3), ('╫', 3, 3, 1, 1),
        };

        private static readonly Dictionary<char, int> KeyByChar =
            CharTable.ToDictionary(t => t.C, t => Key(t.N, t.S, t.E, t.W));

        private static readonly Dictionary<int, char> CharByKey =
            CharTable.ToDictionary(t => Key(t.N, t.S, t.E, t.W), t => t.C);

        private static int ArmsOf(char c) => KeyByChar.TryGetValue(c, out var k) ? k : 0;

        /// <summary>Glyph for an arm key; combinations without an exact glyph (heavy
        /// mixed with double) pick the nearest glyph with the same arms present,
        /// preferring double then heavy — a full heavy×double crossing yields '╬'.</summary>
        private static char CharFor(int key)
        {
            if (CharByKey.TryGetValue(key, out var exact))
                return exact;
            int n = key & 3, s = (key >> 2) & 3, e = (key >> 4) & 3, w = (key >> 6) & 3;
            char best = '┼';
            int bestScore = int.MaxValue, bestDoubles = -1, bestHeavies = -1;
            foreach (var t in CharTable)
            {
                if ((t.N > 0) != (n > 0) || (t.S > 0) != (s > 0) ||
                    (t.E > 0) != (e > 0) || (t.W > 0) != (w > 0))
                    continue;
                int score = Math.Abs(t.N - n) + Math.Abs(t.S - s) + Math.Abs(t.E - e) + Math.Abs(t.W - w);
                int doubles = (t.N == 3 ? 1 : 0) + (t.S == 3 ? 1 : 0) + (t.E == 3 ? 1 : 0) + (t.W == 3 ? 1 : 0);
                int heavies = (t.N == 2 ? 1 : 0) + (t.S == 2 ? 1 : 0) + (t.E == 2 ? 1 : 0) + (t.W == 2 ? 1 : 0);
                bool better = score < bestScore ||
                    (score == bestScore && (doubles > bestDoubles ||
                        (doubles == bestDoubles && heavies > bestHeavies)));
                if (better)
                {
                    best = t.C;
                    bestScore = score;
                    bestDoubles = doubles;
                    bestHeavies = heavies;
                }
            }
            return best;
        }

        private static int MergeKeys(int a, int b)
        {
            int r = 0;
            for (int shift = 0; shift < 8; shift += 2)
                r |= Math.Max((a >> shift) & 3, (b >> shift) & 3) << shift;
            return r;
        }

        /// <summary>Draws line arms into a cell, merging with arms already present.
        /// <paramref name="keep"/> restricts which existing arms survive — a solid
        /// rectangle keeps only arms pointing away from its interior.</summary>
        private void MergeSet(int x, int y, int ownKey, int keep = KeepAll)
        {
            if (ownKey == 0)
                return;
            int existing = ArmsOf(Get(x, y)) & keep;
            Set(x, y, CharFor(MergeKeys(existing, ownKey)));
        }

        private static int Weight(LineStyle s) => s switch
        {
            LineStyle.Bold => 2,
            LineStyle.Double => 3,
            _ => 1,
        };

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
                int wt = Weight(r.LineStyle);
                int hKey = Key(0, 0, wt, wt);
                int vKey = Key(wt, wt, 0, 0);
                if (r.Width == 1 && r.Height == 1)
                {
                    MergeSet(x1, y1, hKey);
                }
                else if (r.Height == 1)
                {
                    for (int x = x1; x <= x2; x++) MergeSet(x, y1, hKey);
                }
                else if (r.Width == 1)
                {
                    for (int y = y1; y <= y2; y++) MergeSet(x1, y, vKey);
                }
                else
                {
                    for (int x = x1 + 1; x < x2; x++)
                    {
                        MergeSet(x, y1, hKey, solid ? KeepN : KeepAll);
                        MergeSet(x, y2, hKey, solid ? KeepS : KeepAll);
                    }
                    for (int y = y1 + 1; y < y2; y++)
                    {
                        MergeSet(x1, y, vKey, solid ? KeepW : KeepAll);
                        MergeSet(x2, y, vKey, solid ? KeepE : KeepAll);
                    }
                    MergeSet(x1, y1, Key(0, wt, wt, 0), solid ? KeepN | KeepW : KeepAll);
                    MergeSet(x2, y1, Key(0, wt, 0, wt), solid ? KeepN | KeepE : KeepAll);
                    MergeSet(x1, y2, Key(wt, 0, wt, 0), solid ? KeepS | KeepW : KeepAll);
                    MergeSet(x2, y2, Key(wt, 0, 0, wt), solid ? KeepS | KeepE : KeepAll);
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
                    int top = iy + r.VerticalAlign switch
                    {
                        VAlign.Top => 0,
                        VAlign.Bottom => ih - lines.Count,
                        _ => (ih - lines.Count) / 2,
                    };
                    for (int i = 0; i < lines.Count; i++)
                    {
                        string ln = lines[i];
                        int left = ix + Math.Max(0, r.HorizontalAlign switch
                        {
                            HAlign.Left => 0,
                            HAlign.Right => iw - ln.Length,
                            _ => (iw - ln.Length) / 2,
                        });
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
            int wt = Weight(l.LineStyle);
            int count = cells.Count;

            for (int i = 0; i < count; i++)
            {
                var cur = cells[i];
                int n = 0, s = 0, e = 0, w = 0;

                void ArmToward((int X, int Y) other)
                {
                    if (other.Y < cur.Y) n = wt;
                    else if (other.Y > cur.Y) s = wt;
                    else if (other.X > cur.X) e = wt;
                    else if (other.X < cur.X) w = wt;
                }

                if (count == 1)
                {
                    e = w = wt;
                }
                else if (i == 0)
                {
                    // End cells render as full straights along their segment axis.
                    if (cells[1].Y == cur.Y) e = w = wt;
                    else n = s = wt;
                }
                else if (i == count - 1)
                {
                    if (cells[i - 1].Y == cur.Y) e = w = wt;
                    else n = s = wt;
                }
                else
                {
                    ArmToward(cells[i - 1]);
                    ArmToward(cells[i + 1]);
                }
                MergeSet(cur.X, cur.Y, Key(n, s, e, w));
            }

            if (count > 1)
            {
                if (l.StartArrow == ArrowStyle.Triangle)
                    Set(cells[0].X, cells[0].Y, Arrow(cells[1], cells[0]));
                if (l.EndArrow == ArrowStyle.Triangle)
                    Set(cells[count - 1].X, cells[count - 1].Y, Arrow(cells[count - 2], cells[count - 1]));
            }
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
