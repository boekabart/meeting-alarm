using System.Text.RegularExpressions;

static class Placement
{
    public const int Margin = 10;

    /// <summary>Working area (excluding the taskbar) of Windows display <paramref name="number"/>; 0 or not present = main screen.</summary>
    public static Rectangle WorkingArea(int number) =>
        (Screen.AllScreens.FirstOrDefault(s => number > 0 && DisplayNumber(s.DeviceName) == number) ?? Screen.PrimaryScreen!).WorkingArea;

    /// <summary>"\\.\DISPLAY2" → 2: the same number as Settings → Display → Identify.</summary>
    public static int? DisplayNumber(string deviceName) =>
        Regex.Match(deviceName, @"DISPLAY(\d+)$", RegexOptions.IgnoreCase) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    /// <summary>
    /// Positions for a stack of windows inside the working area. Index 0 is closest to the anchor edge;
    /// "Bottom" stacks upward, "Top" downward, "Middle" centered around the middle.
    /// </summary>
    public static List<Point> Compute(Position pos, Rectangle area, IReadOnlyList<Size> sizes)
    {
        int X(int w) => pos switch
        {
            Position.TopLeft or Position.MiddleLeft or Position.BottomLeft => area.Left + Margin,
            Position.TopRight or Position.MiddleRight or Position.BottomRight => area.Right - w - Margin,
            _ => area.Left + (area.Width - w) / 2,
        };

        var result = new List<Point>(sizes.Count);
        switch (pos)
        {
            case Position.BottomLeft or Position.Bottom or Position.BottomRight:
                int yBottom = area.Bottom;
                foreach (var s in sizes)
                {
                    yBottom -= s.Height + Margin;
                    result.Add(new Point(X(s.Width), yBottom));
                }
                break;
            case Position.TopLeft or Position.Top or Position.TopRight:
                int yTop = area.Top + Margin;
                foreach (var s in sizes)
                {
                    result.Add(new Point(X(s.Width), yTop));
                    yTop += s.Height + Margin;
                }
                break;
            default:
                int total = sizes.Sum(s => s.Height) + Margin * Math.Max(0, sizes.Count - 1);
                int y = area.Top + (area.Height - total) / 2;
                foreach (var s in sizes)
                {
                    result.Add(new Point(X(s.Width), y));
                    y += s.Height + Margin;
                }
                break;
        }
        return result;
    }
}
