using System.Text.RegularExpressions;

static class Plaatsing
{
    public const int Marge = 10;

    /// <summary>Werkgebied (zonder taakbalk) van Windows-beeldscherm <paramref name="nummer"/>; 0 of niet aanwezig = hoofdscherm.</summary>
    public static Rectangle Werkgebied(int nummer) =>
        (Screen.AllScreens.FirstOrDefault(s => nummer > 0 && SchermNummer(s.DeviceName) == nummer) ?? Screen.PrimaryScreen!).WorkingArea;

    /// <summary>"\\.\DISPLAY2" → 2: hetzelfde nummer als bij Instellingen → Beeldscherm → Identificeren.</summary>
    public static int? SchermNummer(string deviceName) =>
        Regex.Match(deviceName, @"DISPLAY(\d+)$", RegexOptions.IgnoreCase) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    /// <summary>
    /// Posities voor een stapel vensters binnen het werkgebied. Index 0 ligt het dichtst bij de ankerrand;
    /// "Bottom" stapelt omhoog, "Top" omlaag, "Middle" gecentreerd rond het midden.
    /// </summary>
    public static List<Point> Bereken(Position pos, Rectangle wa, IReadOnlyList<Size> maten)
    {
        int X(int w) => pos switch
        {
            Position.TopLeft or Position.MiddleLeft or Position.BottomLeft => wa.Left + Marge,
            Position.TopRight or Position.MiddleRight or Position.BottomRight => wa.Right - w - Marge,
            _ => wa.Left + (wa.Width - w) / 2,
        };

        var res = new List<Point>(maten.Count);
        switch (pos)
        {
            case Position.BottomLeft or Position.Bottom or Position.BottomRight:
                int yOnder = wa.Bottom;
                foreach (var m in maten)
                {
                    yOnder -= m.Height + Marge;
                    res.Add(new Point(X(m.Width), yOnder));
                }
                break;
            case Position.TopLeft or Position.Top or Position.TopRight:
                int yBoven = wa.Top + Marge;
                foreach (var m in maten)
                {
                    res.Add(new Point(X(m.Width), yBoven));
                    yBoven += m.Height + Marge;
                }
                break;
            default:
                int totaal = maten.Sum(m => m.Height) + Marge * Math.Max(0, maten.Count - 1);
                int y = wa.Top + (wa.Height - totaal) / 2;
                foreach (var m in maten)
                {
                    res.Add(new Point(X(m.Width), y));
                    y += m.Height + Marge;
                }
                break;
        }
        return res;
    }
}
