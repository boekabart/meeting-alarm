using System.Drawing;

public class KleurTests
{
    [Theory]
    [InlineData("#c62828", 0xc6, 0x28, 0x28)]
    [InlineData("#0f0", 0x00, 0xff, 0x00)]
    [InlineData("teal", 0x00, 0x80, 0x80)]
    [InlineData("RebeccaPurple", 0x66, 0x33, 0x99)]
    [InlineData("slategrey", 0x70, 0x80, 0x90)]
    [InlineData(" firebrick ", 0xb2, 0x22, 0x22)]
    public void Hex_en_css_namen(string invoer, int r, int g, int b)
    {
        var c = Kleur.Parse(invoer);

        Assert.Equal((r, g, b), (c.R, c.G, c.B));
    }

    [Theory]
    [InlineData("geenkleur")]
    [InlineData("#12")]
    [InlineData("")]
    [InlineData(null)]
    public void Onzin_wordt_de_standaardkleur(string? invoer) => Assert.Equal(Kleur.Standaard, Kleur.Parse(invoer));
}

public class MigratieTests
{
    const string V1 = """
        {
          "MinutenVooraf": 7,
          "Geluid": false,
          "MeetingPositie": "LinksOnder",
          "ChatPositie": "RechtsMidden",
          "ChatPollSeconden": 45,
          // commentaar mag
          "Agendas": [
            { "Naam": "Pricer", "Url": "https://a/cal.ics", "Kleur": "#c62828", "Tenant": "pricer.com", "LoginHint": "bart@pricer.com" },
            { "Naam": "Alleen ICS", "Url": "https://b/cal.ics", "Kleur": "#1565c0" },
            { "Naam": "Alleen Teams", "Url": "", "Kleur": "teal", "Tenant": "x.nl", "LoginHint": "b@x.nl", "ClientId": "eigen-id" },
          ]
        }
        """;

    [Fact]
    public void Herkent_v1_en_niet_v2()
    {
        Assert.True(Config.Migratie.IsV1(V1));
        Assert.False(Config.Migratie.IsV1("""{ "Calendars": [], "Teams": [] }"""));
    }

    [Fact]
    public void Splitst_jobs_in_agendas_en_teams()
    {
        var c = Config.Migratie.VanV1(V1);

        Assert.Equal(["Pricer", "Alleen ICS"], c.Calendars.Select(a => a.Name));
        Assert.Equal("https://b/cal.ics", c.Calendars[1].Url);
        Assert.Equal(["Pricer", "Alleen Teams"], c.Teams.Select(t => t.Name));
        Assert.Equal(("pricer.com", "bart@pricer.com", "#c62828"), (c.Teams[0].Tenant, c.Teams[0].LoginHint, c.Teams[0].Color));
        Assert.Equal("eigen-id", c.Teams[1].ClientId);
    }

    [Fact]
    public void Zet_instellingen_om_en_vult_de_rest_met_standaarden()
    {
        var c = Config.Migratie.VanV1(V1);

        Assert.False(c.Sound);
        Assert.Equal(7, c.Meetings.MinutesBefore);
        Assert.Equal(60, c.Meetings.SnoozeSeconds);   // niet in v1-bestand
        Assert.Equal(Position.BottomLeft, c.Meetings.Position);
        Assert.Equal(Position.MiddleRight, c.Chats.Position);
        Assert.Equal(45, c.Chats.PollSeconds);
        Assert.Equal(["oneOnOne", "group"], c.Chats.ChatTypes);
    }
}
