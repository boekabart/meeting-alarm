using System.Text.RegularExpressions;
using Ical.Net;
using Ical.Net.CalendarComponents;

sealed record Meeting(string CalendarName, string Color, string Title, DateTime Start, string Uid, string? Link)
{
    /// <summary>Without the calendar name: the same meeting via ICS and via Graph gives one popup.</summary>
    public string Key => $"{Uid}|{Start:O}";
}

static class IcsCalendar
{
    static readonly Regex LinkRe = new(
        @"https://(?:teams\.microsoft\.com/l/meetup-join|teams\.live\.com/meet|[\w.-]*zoom\.us/j|meet\.google\.com)/[^\s""<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Outlook puts this in front of a cancelled meeting's title, in the organizer's language.
    static readonly string[] CancelledPrefixes = ["Geannuleerd", "Canceled", "Cancelled", "Inställt", "Avbokat"];

    public static List<Meeting> Parse(CalendarConfig calendar, string ics)
    {
        var cal = Calendar.Load(ics);
        var from = DateTime.Now.AddHours(-1);
        var until = DateTime.Now.AddDays(2);
        var result = new List<Meeting>();

        foreach (var occ in cal.GetOccurrences(from, until))
        {
            if (occ.Source is not CalendarEvent ev || ev.IsAllDay) continue;
            if (string.Equals(ev.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;

            var title = string.IsNullOrWhiteSpace(ev.Summary) ? T.NoTitle : ev.Summary.Trim();
            if (CancelledPrefixes.Any(p => title.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

            var teamsUrl = ev.Properties
                .FirstOrDefault(p => string.Equals(p.Name, "X-MICROSOFT-SKYPETEAMSMEETINGURL", StringComparison.OrdinalIgnoreCase))
                ?.Value?.ToString();
            var m = LinkRe.Match($"{teamsUrl} {ev.Location} {ev.Description}");

            result.Add(new Meeting(calendar.Name, calendar.Color, title, occ.Period.StartTime.AsSystemLocal, ev.Uid ?? title, m.Success ? m.Value : null));
        }
        return result.OrderBy(x => x.Start).ToList();
    }
}
