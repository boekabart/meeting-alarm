public class IcsCalendarTests
{
    // Outlook writes Windows zone names as TZID, with a VTIMEZONE block.
    [Theory]
    [InlineData("Taipei Standard Time", 8)]
    [InlineData("Asia/Taipei", 8)]
    [InlineData("Pacific Standard Time", -7)]
    public void Start_is_converted_from_the_organizers_time_zone(string tzid, int offset)
    {
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);
        var organizerLocal = tomorrow.AddHours(12);
        var sign = offset < 0 ? "-" : "+";
        var ics = $"""
            BEGIN:VCALENDAR
            VERSION:2.0
            BEGIN:VTIMEZONE
            TZID:{tzid}
            BEGIN:STANDARD
            DTSTART:16010101T000000
            TZOFFSETFROM:{sign}{Math.Abs(offset):00}00
            TZOFFSETTO:{sign}{Math.Abs(offset):00}00
            END:STANDARD
            END:VTIMEZONE
            BEGIN:VEVENT
            UID:taipei-1
            SUMMARY:Sync
            DTSTART;TZID="{tzid}":{organizerLocal:yyyyMMdd'T'HHmmss}
            DTEND;TZID="{tzid}":{organizerLocal.AddHours(1):yyyyMMdd'T'HHmmss}
            END:VEVENT
            END:VCALENDAR
            """;

        var m = Assert.Single(IcsCalendar.Parse(new CalendarConfig { Name = "x" }, ics));

        Assert.Equal(DateTime.SpecifyKind(organizerLocal.AddHours(-offset), DateTimeKind.Utc).ToLocalTime(), m.Start);
    }
}
