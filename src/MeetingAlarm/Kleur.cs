static class Kleur
{
    public static readonly Color Standaard = Color.Firebrick;

    /// <summary>"#rrggbb", "#rgb" of een CSS-kleurnaam ("teal", "rebeccapurple", "slategrey"); anders <see cref="Standaard"/>.</summary>
    public static Color Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Standaard;
        // CSS kent naast "gray" ook "grey"-spellingen (darkgrey, slategrey, …); .NET alleen "gray".
        var naam = s.Trim().Replace("grey", "gray", StringComparison.OrdinalIgnoreCase);
        try
        {
            var c = ColorTranslator.FromHtml(naam);
            return c.IsEmpty || (c.A == 0 && !c.IsKnownColor) ? Standaard : c;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return Standaard;
        }
    }
}
