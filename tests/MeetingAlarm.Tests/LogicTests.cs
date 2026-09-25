using System.Drawing;

public class ChatCounterTests
{
    static readonly DateTimeOffset T0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    static ChatMessage M(int minute, string sender = "other", bool isMessage = true) => new(T0.AddMinutes(minute), sender, isMessage);

    [Fact]
    public void Baseline_is_the_later_of_ack_and_teams_read()
    {
        Assert.Equal(T0.AddMinutes(5), ChatCounter.Baseline(T0.AddMinutes(5), T0));
        Assert.Equal(T0.AddMinutes(5), ChatCounter.Baseline(T0, T0.AddMinutes(5)));
        Assert.Equal(T0, ChatCounter.Baseline(null, T0));
        Assert.Equal(T0, ChatCounter.Baseline(T0, null));
        Assert.Null(ChatCounter.Baseline(null, null));
    }

    [Fact]
    public void Counts_only_real_messages_from_others_after_the_baseline()
    {
        var c = ChatCounter.Count([M(5), M(4, isMessage: false), M(3), M(2), M(1)], T0.AddMinutes(2), "me");

        Assert.Equal(2, c.Count);
        Assert.Equal(T0.AddMinutes(5), c.Newest);
        Assert.Null(c.OwnMessage);
    }

    [Fact]
    public void Own_message_stops_the_count_and_gives_an_auto_ack()
    {
        var c = ChatCounter.Count([M(5), M(4, "me"), M(3)], T0, "me");

        Assert.Equal(1, c.Count);
        Assert.Equal(T0.AddMinutes(4), c.OwnMessage);
    }

    [Fact]
    public void Nothing_new_gives_zero()
    {
        var c = ChatCounter.Count([M(1)], T0.AddMinutes(1), "me");

        Assert.Equal(0, c.Count);
        Assert.Null(c.Newest);
    }
}

public class PlacementTests
{
    static readonly Rectangle Area = new(0, 0, 1000, 800);

    [Fact]
    public void BottomRight_stacks_upward()
    {
        var p = Placement.Compute(Position.BottomRight, Area, [new Size(200, 100), new Size(200, 50)]);

        Assert.Equal([new Point(790, 690), new Point(790, 630)], p);
    }

    [Fact]
    public void MiddleRight_centers_the_stack_vertically()
    {
        var p = Placement.Compute(Position.MiddleRight, Area, [new Size(300, 100), new Size(300, 100)]);

        Assert.Equal([new Point(690, 295), new Point(690, 405)], p);
    }

    [Fact]
    public void TopLeft_stacks_downward()
    {
        var p = Placement.Compute(Position.TopLeft, Area, [new Size(200, 100), new Size(200, 100)]);

        Assert.Equal([new Point(10, 10), new Point(10, 120)], p);
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY1", 1)]
    [InlineData(@"\\.\DISPLAY12", 12)]
    [InlineData(@"\\.\display3", 3)]
    [InlineData("something else", null)]
    public void DisplayNumber_from_DeviceName(string deviceName, int? expected) =>
        Assert.Equal(expected, Placement.DisplayNumber(deviceName));
}

public class WinFormsTests
{
    // Left-click on the tray icon relies on this private method; if this test fails after a .NET update, that stops working.
    [Fact]
    public void NotifyIcon_still_has_ShowContextMenu() =>
        Assert.NotNull(typeof(NotifyIcon).GetMethod("ShowContextMenu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
}

public class TeamsLinkTests
{
    [Fact]
    public void Built_link_has_the_shape_of_graph_webUrl() =>
        Assert.Equal("https://teams.microsoft.com/l/chat/19%3Aabc%40thread.v2/0?tenantId=t-1",
            TeamsLink.ForChat("t-1", "19:abc@thread.v2"));

    [Theory]
    [InlineData("https://teams.microsoft.com/l/chat/19%3Aabc%40thread.v2/0?tenantId=t-1", "msteams:/l/chat/19%3Aabc%40thread.v2/0?tenantId=t-1")]
    [InlineData("https://teams.cloud.microsoft/l/chat/19%3Aabc/0?tenantId=t", "msteams:/l/chat/19%3Aabc/0?tenantId=t")]
    [InlineData("https://example.com/l/chat/x", null)]
    [InlineData("not a url", null)]
    public void ToApp(string webUrl, string? expected) => Assert.Equal(expected, TeamsLink.ToApp(webUrl));
}
