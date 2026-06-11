using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Platform;

namespace AsciiDraw.Views
{
    /// <summary>
    /// Renders an embedded SVG icon (Assets/icons/{Source}.svg) by extracting its
    /// path data and filling it with the inherited Foreground ("currentColor"),
    /// so icons follow the button's text color in all visual states.
    /// </summary>
    public class SvgIcon : Control
    {
        public static readonly StyledProperty<string?> SourceProperty =
            AvaloniaProperty.Register<SvgIcon, string?>(nameof(Source));

        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner<SvgIcon>();

        public string? Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public IBrush? Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        private static readonly Dictionary<string, (Geometry? Geometry, Rect ViewBox)> Cache = new();

        private static (Geometry? Geometry, Rect ViewBox) Load(string name)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;
            (Geometry?, Rect) result;
            try
            {
                using var stream = AssetLoader.Open(new Uri($"avares://AsciiDraw/Assets/icons/{name}.svg"));
                var doc = XDocument.Load(stream);
                XNamespace ns = "http://www.w3.org/2000/svg";

                var viewBox = new Rect(0, 0, 24, 24);
                var vb = doc.Root?.Attribute("viewBox")?.Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (vb is { Length: 4 })
                {
                    viewBox = new Rect(
                        double.Parse(vb[0], System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(vb[1], System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(vb[2], System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(vb[3], System.Globalization.CultureInfo.InvariantCulture));
                }

                // "F1" = nonzero fill rule, the SVG default.
                var geometries = doc.Descendants(ns + "path")
                    .Select(p => p.Attribute("d")?.Value)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Select(d => Geometry.Parse("F1 " + d))
                    .ToList();

                Geometry? geometry = geometries.Count switch
                {
                    0 => null,
                    1 => geometries[0],
                    _ => Group(geometries),
                };
                result = (geometry, viewBox);
            }
            catch (Exception)
            {
                result = (null, new Rect(0, 0, 24, 24));
            }
            Cache[name] = result;
            return result;
        }

        private static GeometryGroup Group(List<Geometry> geometries)
        {
            var group = new GeometryGroup { FillRule = FillRule.NonZero };
            foreach (var g in geometries)
                group.Children.Add(g);
            return group;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SourceProperty || change.Property == ForegroundProperty)
                InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var source = Source;
            var brush = Foreground;
            if (source == null || brush == null || Bounds.Width <= 0 || Bounds.Height <= 0)
                return;
            var (geometry, viewBox) = Load(source);
            if (geometry == null || viewBox.Width <= 0 || viewBox.Height <= 0)
                return;
            var transform =
                Matrix.CreateTranslation(-viewBox.X, -viewBox.Y) *
                Matrix.CreateScale(Bounds.Width / viewBox.Width, Bounds.Height / viewBox.Height);
            using (ctx.PushTransform(transform))
                ctx.DrawGeometry(brush, null, geometry);
        }
    }
}
