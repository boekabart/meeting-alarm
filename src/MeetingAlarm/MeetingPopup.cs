using System.Diagnostics;
using System.Media;
using WinTimer = System.Windows.Forms.Timer;

sealed class MeetingPopup : PopupBase
{
    static readonly List<MeetingPopup> Open = new();

    /// <summary>Anchor for the stack of meeting popups; call <see cref="Reposition"/> after changing it.</summary>
    public static Position Position { get; set; } = Position.BottomRight;
    public static int DisplayNumber { get; set; }

    readonly Meeting meeting;
    readonly Config cfg;
    readonly Color color;
    readonly FlowLayoutPanel inner;
    readonly Label timeLabel;
    readonly WinTimer tick = new() { Interval = 1000 };
    bool flashOn;

    public MeetingPopup(Meeting meeting, Config cfg)
    {
        this.meeting = meeting;
        this.cfg = cfg;
        color = ColorParser.Parse(meeting.Color);
        int textWidth = S(400);

        inner = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = color,
            Padding = new Padding(S(14), S(10), S(14), S(10)),
            Margin = Padding.Empty,
        };

        inner.Controls.Add(MakeLabel(meeting.CalendarName.ToUpperInvariant(), 10, textWidth));
        inner.Controls.Add(MakeLabel(meeting.Title, 15, textWidth));
        timeLabel = MakeLabel("", 20, textWidth);
        timeLabel.Margin = new Padding(3, S(4), 3, S(8));
        inner.Controls.Add(timeLabel);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        if (meeting.Link is not null)
            buttons.Controls.Add(MakeButton(T.Join, () =>
            {
                Process.Start(new ProcessStartInfo(meeting.Link) { UseShellExecute = true });
                Close();
            }));
        buttons.Controls.Add(MakeButton(T.Later, Snooze));
        buttons.Controls.Add(MakeButton(T.CloseButton, Close));
        inner.Controls.Add(buttons);

        Controls.Add(inner);
        tick.Tick += (_, _) => UpdateCountdown();
    }

    public void ShowPopup()
    {
        _ = Handle;          // force layout so the height is right before positioning
        PerformLayout();
        Open.Add(this);
        Reposition();
        Show();
        PlaySound();
        UpdateCountdown();
        tick.Start();
    }

    void PlaySound()
    {
        if (cfg.Sound) SystemSounds.Exclamation.Play();
    }

    public static void Reposition()
    {
        var points = Placement.Compute(Position, Placement.WorkingArea(DisplayNumber), Open.Select(p => p.Size).ToList());
        for (int i = 0; i < Open.Count; i++)
            Open[i].Location = points[i];
    }

    void UpdateCountdown()
    {
        var sec = (int)Math.Ceiling((meeting.Start - DateTime.Now).TotalSeconds);
        bool flash;
        if (sec > 0)
        {
            timeLabel.Text = F(T.StartsIn, sec / 60, sec % 60, meeting.Start);
            flash = sec <= 60;   // flash during the last minute
        }
        else
        {
            int min = -sec / 60;
            if (cfg.Meetings.AutoCloseAfterMinutes > 0 && min >= cfg.Meetings.AutoCloseAfterMinutes) { Close(); return; }
            timeLabel.Text = min == 0 ? T.StartedNow : F(T.StartedAgo, min);
            flash = true;
        }
        flashOn = flash && !flashOn;
        inner.BackColor = flashOn ? FlashColor : color;
        KeepOnTop();
    }

    void Snooze()
    {
        tick.Stop();
        Open.Remove(this);
        Hide();
        Reposition();

        var wait = new WinTimer { Interval = Math.Max(5, cfg.Meetings.SnoozeSeconds) * 1000 };
        wait.Tick += (_, _) =>
        {
            wait.Dispose();
            if (IsDisposed) return;
            Open.Add(this);
            Reposition();
            Show();
            PlaySound();
            UpdateCountdown();
            tick.Start();
        };
        wait.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tick.Dispose();
        Open.Remove(this);
        Reposition();
        base.OnFormClosed(e);
    }
}
