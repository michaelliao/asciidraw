using System;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AsciiDraw.Models
{
    public static class Exporter
    {
        private const string FontFallbacks = "Cascadia Mono,Consolas,Courier New";

        public static string ToText(DrawDocument doc)
        {
            var grid = CharGrid.Render(doc);
            var bounds = grid.UsedBounds();
            if (bounds == null)
                return "";
            var (minX, minY, maxX, maxY) = bounds.Value;
            var sb = new StringBuilder();
            for (int y = minY; y <= maxY; y++)
            {
                sb.AppendLine(grid.RowString(y).Substring(minX, maxX - minX + 1).TrimEnd());
            }
            return sb.ToString();
        }

        public static string ToSvg(DrawDocument doc)
        {
            var grid = CharGrid.Render(doc);
            var bounds = grid.UsedBounds() ?? (0, 0, 0, 0);
            var (minX, minY, maxX, maxY) = bounds;
            int cols = maxX - minX + 1, rows = maxY - minY + 1;

            const double fontSize = 16;
            double cw = fontSize * 0.6;
            double ch = fontSize * 1.25;
            double width = (cols + 2) * cw;
            double height = (rows + 2) * ch;

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(ci,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0:0.##}\" height=\"{1:0.##}\" viewBox=\"0 0 {0:0.##} {1:0.##}\">",
                width, height));
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
            sb.AppendLine(string.Format(ci,
                "<g font-family=\"{0},monospace\" font-size=\"{1}\" fill=\"black\" xml:space=\"preserve\" style=\"white-space:pre\">",
                FontFallbacks, fontSize));
            for (int y = minY; y <= maxY; y++)
            {
                string line = grid.RowString(y).Substring(minX, cols).TrimEnd();
                if (line.Length == 0)
                    continue;
                double tx = cw;
                double ty = (y - minY + 1) * ch + fontSize * 0.8;
                sb.AppendLine(string.Format(ci,
                    "<text x=\"{0:0.##}\" y=\"{1:0.##}\" textLength=\"{2:0.##}\">{3}</text>",
                    tx, ty, line.Length * cw, EscapeXml(line)));
            }
            sb.AppendLine("</g>");
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static void ToPng(DrawDocument doc, Stream stream)
        {
            var grid = CharGrid.Render(doc);
            var bounds = grid.UsedBounds() ?? (0, 0, 0, 0);
            var (minX, minY, maxX, maxY) = bounds;
            int cols = maxX - minX + 1, rows = maxY - minY + 1;

            const double fontSize = 16;
            var typeface = new Typeface(FontFallbacks);
            var probe = new FormattedText("0", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);
            double cw = probe.Width;
            double ch = probe.Height;

            var size = new PixelSize(
                Math.Max(1, (int)Math.Ceiling((cols + 2) * cw)),
                Math.Max(1, (int)Math.Ceiling((rows + 2) * ch)));

            using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            using (var ctx = bitmap.CreateDrawingContext())
            {
                ctx.FillRectangle(Brushes.White, new Rect(0, 0, size.Width, size.Height));
                for (int y = minY; y <= maxY; y++)
                {
                    string line = grid.RowString(y).Substring(minX, cols).TrimEnd();
                    if (line.Length == 0)
                        continue;
                    var ft = new FormattedText(line, CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);
                    ctx.DrawText(ft, new Point(cw, (y - minY + 1) * ch));
                }
            }
            bitmap.Save(stream);
        }

        private static string EscapeXml(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
