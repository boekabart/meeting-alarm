static class ColorParser
{
    public static readonly Color Default = Color.Firebrick;

    /// <summary>"#rrggbb", "#rgb" or a CSS color name ("teal", "rebeccapurple", "slategrey"); otherwise <see cref="Default"/>.</summary>
    public static Color Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Default;
        // CSS has "grey" spellings next to "gray" (darkgrey, slategrey, …); .NET only knows "gray".
        var name = s.Trim().Replace("grey", "gray", StringComparison.OrdinalIgnoreCase);
        try
        {
            var c = ColorTranslator.FromHtml(name);
            return c.IsEmpty || (c.A == 0 && !c.IsKnownColor) ? Default : c;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return Default;
        }
    }
}
