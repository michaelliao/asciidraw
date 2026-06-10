namespace AsciiDraw.Models
{
    public enum LineStyle
    {
        Normal,
        Dashed,
        Dotted,
        None,
    }

    public enum FillStyle
    {
        Transparent,
        Solid,
    }

    public enum ArrowStyle
    {
        None,
        Triangle,
    }

    public enum Tool
    {
        Select,
        Rect,
        Text,
        Line,
    }

    public enum VAlign
    {
        Top,
        Center,
        Bottom,
    }

    public enum HAlign
    {
        Left,
        Center,
        Right,
    }

    /// <summary>The eight connection points of a rectangle.</summary>
    public enum Anchor
    {
        TopLeft,
        Top,
        TopRight,
        Left,
        Right,
        BottomLeft,
        Bottom,
        BottomRight,
    }
}
