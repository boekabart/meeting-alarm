using System.Text.RegularExpressions;

static class MeetingLinks
{
    static readonly Regex LinkRe = new(
        @"https://(?:teams\.microsoft\.com/l/meetup-join|teams\.live\.com/meet|[\w.-]*zoom\.us/j|meet\.google\.com)/[^\s""<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>First Teams, Zoom or Google Meet join link in the given texts (location, description, …).</summary>
    public static string? Find(params string?[] texts) =>
        texts.Select(t => t is null ? null : LinkRe.Match(t)).FirstOrDefault(m => m is { Success: true })?.Value;

    /// <summary>Provider name for the join button; null for Teams (the button then just says "Join").</summary>
    public static string? Provider(string url) =>
        url.Contains("meet.google.com", StringComparison.OrdinalIgnoreCase) ? "Meet"
        : url.Contains("zoom.us", StringComparison.OrdinalIgnoreCase) ? "Zoom"
        : null;

    /// <summary>The location worth showing: not empty, not a bare link, not Outlook's automatic "Microsoft Teams Meeting".</summary>
    public static string? CleanLocation(string? location)
    {
        var l = location?.Trim();
        if (string.IsNullOrEmpty(l)) return null;
        if (l.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        // Outlook fills this in for every Teams meeting, in the organizer's language ("Microsoft Teams-vergadering", …).
        if (l.StartsWith("Microsoft Teams", StringComparison.OrdinalIgnoreCase)) return null;
        return l;
    }
}
