using Avalonia.Media;

namespace AsciiDraw.Models
{
    /// <summary>The embedded monospace font used for all ASCII rendering.</summary>
    public static class AppFonts
    {
        // The embedded TTF's family is "Maple Mono Normal NL NF CN", but Avalonia's
        // Typeface.Normalize strips the style word "Normal" from queried names, so a
        // full-name lookup always misses. Querying the prefix "Maple Mono" resolves
        // via the font collection's prefix matching instead.
        public const string FamilyName = "Maple Mono";

        /// <summary>Family name as stored in the TTF — used where the real name matters (e.g. SVG).</summary>
        public const string FullFamilyName = "Maple Mono Normal NL NF CN";

        // The "fonts:asciidraw" collection is registered in App.OnFrameworkInitializationCompleted.
        public const string Uri = "fonts:asciidraw#" + FamilyName;

        public static readonly FontFamily Mono = new(Uri);
    }
}
