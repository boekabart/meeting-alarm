static class Plaatsing
{
    public const int Marge = 10;

    /// <summary>
    /// Posities voor een stapel vensters binnen het werkgebied. Index 0 ligt het dichtst bij de ankerrand;
    /// "Onder" stapelt omhoog, "Boven" omlaag, "Midden" gecentreerd rond het midden.
    /// </summary>
    public static List<Point> Bereken(Positie pos, Rectangle wa, IReadOnlyList<Size> maten)
    {
        int X(int w) => pos switch
        {
            Positie.LinksBoven or Positie.LinksMidden or Positie.LinksOnder => wa.Left + Marge,
            Positie.RechtsBoven or Positie.RechtsMidden or Positie.RechtsOnder => wa.Right - w - Marge,
            _ => wa.Left + (wa.Width - w) / 2,
        };

        var res = new List<Point>(maten.Count);
        switch (pos)
        {
            case Positie.LinksOnder or Positie.Onder or Positie.RechtsOnder:
                int yOnder = wa.Bottom;
                foreach (var m in maten)
                {
                    yOnder -= m.Height + Marge;
                    res.Add(new Point(X(m.Width), yOnder));
                }
                break;
            case Positie.LinksBoven or Positie.Boven or Positie.RechtsBoven:
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
