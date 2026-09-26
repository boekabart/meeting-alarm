using System.Text.Json;

public class ConfigTests
{
    const string Outdated = """
        {
          // a comment
          "sound": false,
          "Meetings": { "Position": "TopRight", "Screen": 2, "_note": "mine" },
          "Tenants": [ { "Name": "HTI", "Tenant": "hti.nl", "LoginHint": "b@hti.nl", "Color": "teal", "Chats": true } ],
          "_DisabledCalendars": [ { "Name": "Old", "Url": "https://x/cal.ics" } ],
          "Calendars": [],
        }
        """;

    [Fact]
    public void Outdated_file_is_rewritten_with_new_settings_and_canonical_names()
    {
        var (cfg, rewritten) = Config.Parse(Outdated);

        Assert.False(cfg.Sound);
        Assert.NotNull(rewritten);
        Assert.Contains("\"Sound\": false", rewritten);
        Assert.Contains("\"GraphRefreshSeconds\": 30", rewritten);   // wasn't in the file
        Assert.Contains("\"Calendar\": false", rewritten);           // new per-tenant switch shows up
    }

    [Fact]
    public void Unknown_sections_are_kept_and_moved_to_the_end()
    {
        var rewritten = Config.Parse(Outdated).Rewritten!;
        var root = JsonDocument.Parse(rewritten).RootElement;

        Assert.Equal("_DisabledCalendars", root.EnumerateObject().Last().Name);
        Assert.Equal("https://x/cal.ics", root.GetProperty("_DisabledCalendars")[0].GetProperty("Url").GetString());
        Assert.Equal("mine", root.GetProperty("Meetings").GetProperty("_note").GetString());   // nested too
        Assert.Equal("_note", root.GetProperty("Meetings").EnumerateObject().Last().Name);
    }

    [Fact]
    public void Current_file_is_left_alone()
    {
        var current = Config.Parse(Outdated).Rewritten!;

        Assert.Null(Config.Parse(current).Rewritten);
        // Comments alone don't trigger a rewrite either.
        var withComment = "{ // keep me" + current[1..];
        Assert.Contains("keep me", withComment);
        Assert.Null(Config.Parse(withComment).Rewritten);
    }
}
