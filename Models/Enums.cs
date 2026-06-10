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
}
