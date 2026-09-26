using System.Text.Json;

public class GraphCalendarTests
{
    static Meeting? Map(string json) => TenantClient.ToMeeting(JsonDocument.Parse(json).RootElement, "Pricer", "firebrick");

    const string Event = """
        {
          "id": "AAMk-1", "iCalUId": "040000008200E0", "subject": " Stand-up ",
          "start": { "dateTime": "2026-09-26T08:00:00.0000000", "timeZone": "UTC" },
          "isAllDay": false, "isCancelled": false,
          "responseStatus": { "response": "accepted" },
          "onlineMeeting": { "joinUrl": "https://teams.microsoft.com/l/meetup-join/abc" }
        }
        """;

    [Fact]
    public void Maps_title_utc_start_join_link_and_uid()
    {
        var m = Map(Event)!;

        Assert.Equal("Stand-up", m.Title);
        Assert.Equal(new DateTime(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc).ToLocalTime(), m.Start);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", m.Link);
        Assert.Equal("040000008200E0", m.Uid);
        Assert.Equal(("Pricer", "firebrick"), (m.CalendarName, m.Color));
    }

    [Theory]
    [InlineData("\"isCancelled\": false", "\"isCancelled\": true")]
    [InlineData("\"isAllDay\": false", "\"isAllDay\": true")]
    [InlineData("\"response\": \"accepted\"", "\"response\": \"declined\"")]
    public void Skips_cancelled_all_day_and_declined(string from, string to) => Assert.Null(Map(Event.Replace(from, to)));

    [Fact]
    public void Tentative_and_unanswered_still_count() =>
        Assert.NotNull(Map(Event.Replace("\"response\": \"accepted\"", "\"response\": \"notResponded\"")));

    [Fact]
    public void Without_online_meeting_there_is_no_link() =>
        Assert.Null(Map(Event.Replace("\"joinUrl\": \"https://teams.microsoft.com/l/meetup-join/abc\"", "\"joinUrl\": null"))!.Link);
}
