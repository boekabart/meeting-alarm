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

public class MeetingLinksTests
{
    [Theory]
    [InlineData("Join: https://meet.google.com/abc-defg-hij now", "https://meet.google.com/abc-defg-hij")]
    [InlineData("https://acme.zoom.us/j/123456?pwd=x", "https://acme.zoom.us/j/123456?pwd=x")]
    [InlineData("<https://teams.microsoft.com/l/meetup-join/19%3a1>", "https://teams.microsoft.com/l/meetup-join/19%3a1")]
    [InlineData("see https://example.com/meet", null)]
    public void Finds_join_links(string text, string? expected) => Assert.Equal(expected, MeetingLinks.Find(null, text));

    [Theory]
    [InlineData("https://meet.google.com/abc", "Meet")]
    [InlineData("https://acme.zoom.us/j/1", "Zoom")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/x", null)]
    public void Provider(string url, string? expected) => Assert.Equal(expected, MeetingLinks.Provider(url));

    [Theory]
    [InlineData("Room 4.12 (Eindhoven)", "Room 4.12 (Eindhoven)")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    [InlineData("https://meet.google.com/abc", null)]
    [InlineData("Microsoft Teams Meeting", null)]
    [InlineData("Microsoft Teams-vergadering", null)]
    public void CleanLocation(string? location, string? expected) => Assert.Equal(expected, MeetingLinks.CleanLocation(location));
}

public class GraphCalendarLinkTests
{
    [Fact]
    public void Meet_link_from_the_invitation_text_and_room_from_location()
    {
        var m = TenantClient.ToMeeting(JsonDocument.Parse("""
            {
              "subject": "Review", "start": { "dateTime": "2026-09-26T08:00:00", "timeZone": "UTC" },
              "onlineMeeting": null,
              "location": { "displayName": "Room 4.12" },
              "body": { "contentType": "text", "content": "Join with Google Meet\nhttps://meet.google.com/abc-defg-hij\nOr dial in" }
            }
            """).RootElement, "HTI", "teal")!;

        Assert.Equal("https://meet.google.com/abc-defg-hij", m.Link);
        Assert.Equal("Room 4.12", m.Location);
    }
}
