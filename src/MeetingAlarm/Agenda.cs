using System.Text.RegularExpressions;
using Ical.Net;
using Ical.Net.CalendarComponents;

sealed record Meeting(CalendarConfig Agenda, string Titel, DateTime Start, string Uid, string? Link)
{
    public string Sleutel => $"{Agenda.Name}|{Uid}|{Start:O}";
}

static class Agenda
{
    static readonly Regex LinkRe = new(
        @"https://(?:teams\.microsoft\.com/l/meetup-join|teams\.live\.com/meet|[\w.-]*zoom\.us/j|meet\.google\.com)/[^\s""<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<Meeting> Parse(CalendarConfig a, string ics)
    {
        var cal = Calendar.Load(ics);
        var van = DateTime.Now.AddHours(-1);
        var tot = DateTime.Now.AddDays(2);
        var res = new List<Meeting>();

        foreach (var occ in cal.GetOccurrences(van, tot))
        {
            if (occ.Source is not CalendarEvent ev || ev.IsAllDay) continue;
            if (string.Equals(ev.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;

            var titel = string.IsNullOrWhiteSpace(ev.Summary) ? "(geen titel)" : ev.Summary.Trim();
            if (titel.StartsWith("Geannuleerd", StringComparison.OrdinalIgnoreCase) ||
                titel.StartsWith("Canceled", StringComparison.OrdinalIgnoreCase) ||
                titel.StartsWith("Cancelled", StringComparison.OrdinalIgnoreCase)) continue;

            var teamsUrl = ev.Properties
                .FirstOrDefault(p => string.Equals(p.Name, "X-MICROSOFT-SKYPETEAMSMEETINGURL", StringComparison.OrdinalIgnoreCase))
                ?.Value?.ToString();
            var m = LinkRe.Match($"{teamsUrl} {ev.Location} {ev.Description}");

            res.Add(new Meeting(a, titel, occ.Period.StartTime.AsSystemLocal, ev.Uid ?? titel, m.Success ? m.Value : null));
        }
        return res.OrderBy(x => x.Start).ToList();
    }
}
