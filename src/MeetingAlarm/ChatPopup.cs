using System.Media;
using WinTimer = System.Windows.Forms.Timer;

/// <summary>One window with every chat that has unread and un-acked messages. Only visible when there is something.</summary>
sealed class ChatPopup : PopupBase
{
    const int MaxRows = 10;
    static readonly Color Background = Color.FromArgb(38, 50, 56);

    readonly FlowLayoutPanel inner;
    readonly WinTimer flashTimer = new() { Interval = 500 };
    readonly ToolTip tip = new();
    Dictionary<string, int> previousCounts = new();
    string signature = "";
    int flashTicks;
    Position position = Position.MiddleRight;
    int displayNumber;

    /// <summary>The user has seen these rows (✓ or "Mark all as seen").</summary>
    public event Action<IReadOnlyList<ChatRow>>? Seen;

    public int FlashSeconds { get; set; } = 3;
    public bool Sound { get; set; } = true;

    public Position Position
    {
        get => position;
        set { position = value; Reposition(); }
    }

    public int DisplayNumber
    {
        get => displayNumber;
        set { displayNumber = value; Reposition(); }
    }

    public ChatPopup()
    {
        inner = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Background,
            Padding = new Padding(S(12), S(8), S(12), S(8)),
            Margin = Padding.Empty,
        };
        Controls.Add(inner);

        flashTimer.Tick += (_, _) =>
        {
            flashTicks--;
            inner.BackColor = flashTicks > 0 && flashTicks % 2 == 1 ? FlashColor : Background;
            if (flashTicks <= 0) flashTimer.Stop();
            KeepOnTop();
        };
    }

    /// <summary>Show the current state. Flashes when a chat was added or a count went up.</summary>
    public void ShowRows(IReadOnlyList<ChatRow> rows)
    {
        bool more = rows.Any(r => !previousCounts.TryGetValue(r.Key, out var old) || r.Count > old);
        previousCounts = rows.ToDictionary(r => r.Key, r => r.Count);

        var current = Culture.Name + "\n" + string.Join("\n", rows.Select(r => $"{r.Key}|{r.Count}|{r.Name}"));   // language switch = rebuild
        if (current != signature)
        {
            signature = current;
            Build(rows);
        }

        if (rows.Count == 0)
        {
            Hide();
            return;
        }
        if (!Visible)
        {
            Reposition();
            Show();
        }
        KeepOnTop();
        if (more) Flash();
    }

    void Flash()
    {
        flashTicks = Math.Max(1, FlashSeconds) * 2;
        flashTimer.Start();
        if (Sound) SystemSounds.Exclamation.Play();
    }

    void Build(IReadOnlyList<ChatRow> rows)
    {
        SuspendLayout();
        foreach (var c in inner.Controls.Cast<Control>().ToList()) c.Dispose();

        int width = S(380);
        inner.Controls.Add(MakeLabel(F(T.ChatHeader, rows.Sum(r => r.Count)), 11, width));
        foreach (var r in rows.OrderByDescending(r => r.Newest).Take(MaxRows))
            inner.Controls.Add(MakeRow(r));
        if (rows.Count > MaxRows)
            inner.Controls.Add(MakeLabel(F(T.MoreChats, rows.Count - MaxRows), 9, width));
        inner.Controls.Add(MakeButton(T.MarkAllSeen, () => Seen?.Invoke(rows)));

        ResumeLayout();
        PerformLayout();
    }

    Control MakeRow(ChatRow r)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = ColorParser.Parse(r.Job.Color),
            Margin = new Padding(0, S(3), 0, S(3)),
            Padding = new Padding(S(8), S(4), S(4), S(4)),
        };

        var text = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        text.Controls.Add(MakeLabel(r.Job.Name.ToUpperInvariant(), 8, S(250)));
        var name = MakeLabel(r.Name, 12, S(250));
        name.AutoSize = false;
        name.AutoEllipsis = true;
        name.Size = new Size(S(250), S(26));
        if (r.WebUrl is { } url)
        {
            name.Cursor = Cursors.Hand;
            name.Font = new Font(name.Font, FontStyle.Bold | FontStyle.Underline);
            name.Click += (_, _) => TeamsLink.Open(url);   // opening does not ack
        }
        text.Controls.Add(name);

        var count = MakeLabel(r.Count >= ChatCounter.MaxMessages ? $"{ChatCounter.MaxMessages}+" : r.Count.ToString(), 18, S(60));
        count.TextAlign = ContentAlignment.MiddleRight;

        var ok = MakeButton("✓", () => Seen?.Invoke([r]));
        tip.SetToolTip(ok, T.SeenTooltip);

        row.Controls.Add(text, 0, 0);
        row.Controls.Add(count, 1, 0);
        row.Controls.Add(ok, 2, 0);
        return row;
    }

    public void Reposition() => Location = Placement.Compute(position, Placement.WorkingArea(displayNumber), [Size])[0];

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Reposition();   // grows/shrinks with the number of rows; stays attached to its anchor
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 and such: don't throw it away; only "seen" makes it go away.
        if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
