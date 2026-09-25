using System.Reflection;
using Microsoft.Identity.Client;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

sealed class AlarmContext : ApplicationContext
{
    Config cfg;
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem miUpcoming, miRefresh, miTestPopup, miChatPopup, miAutostart, miSettings, miExit;
    readonly NotifyIcon tray;
    readonly WinTimer checkTimer;
    readonly WinTimer reloadTimer = new() { Interval = 500 };   // debounce: editors often write several times
    readonly FileSystemWatcher watcher;
    readonly Dictionary<CalendarConfig, List<Meeting>> perCalendar = new();
    // Keyed by config object (not by name): two calendars may share a name.
    readonly Dictionary<object, ToolStripMenuItem> statusItems = new();
    readonly Dictionary<TeamsConfig, ToolStripMenuItem> signInItems = new();
    readonly HashSet<object> errorReported = new();
    readonly HashSet<string> shown = new();
    readonly HashSet<string> autoSignInTried = new();
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly CancellationTokenSource stop = new();

    readonly ChatState chatState = new();
    readonly ChatPopup chatPopup = new();
    List<ChatPoller> pollers = [];
    List<ChatRow> chatRows = [];
    bool chatPolling;

    public AlarmContext(Config cfg, bool test)
    {
        this.cfg = cfg;
        // Texts are set in ApplyConfig (the language can change live).
        miUpcoming = new ToolStripMenuItem("", null, (_, _) => ShowUpcoming());
        miRefresh = new ToolStripMenuItem("", null, async (_, _) => { await RefreshCalendars(); await PollChats(); });
        miTestPopup = new ToolStripMenuItem("", null, (_, _) => TestPopup());
        miChatPopup = new ToolStripMenuItem("", null, (_, _) => TestChatPopup());
        miAutostart = new ToolStripMenuItem("") { Checked = Autostart.Enabled, CheckOnClick = true };
        miAutostart.CheckedChanged += (_, _) => Autostart.Enabled = miAutostart.Checked;
        miSettings = new ToolStripMenuItem("", null, (_, _) => Config.OpenInNotepad());
        miExit = new ToolStripMenuItem("", null, (_, _) => ExitThread());
        menu.Items.AddRange([new ToolStripSeparator(), miUpcoming, miRefresh, miTestPopup, miChatPopup, miAutostart, miSettings,
            new ToolStripSeparator(), miExit]);

        tray = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "Meeting Alarm",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseUp += (_, e) =>
        {
            // Left-click opens the same menu as right-click. NotifyIcon's own (private) ShowContextMenu brings the menu
            // to the foreground properly, so it closes when clicking elsewhere; menu.Show() alone doesn't.
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(tray, null);
        };

        chatPopup.Seen += Seen;
        ApplyConfig();

        // FileSystemWatcher fires on a thread-pool thread; (re)start the debounce timer via the UI context.
        var ui = SynchronizationContext.Current!;
        reloadTimer.Tick += (_, _) => { reloadTimer.Stop(); Reload(); };
        watcher = new FileSystemWatcher(Path.GetDirectoryName(Config.FilePath)!, Path.GetFileName(Config.FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler changed = (_, _) => ui.Post(_ => { reloadTimer.Stop(); reloadTimer.Start(); }, null);
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Renamed += (s, e) => changed(s, e);   // editors that save via a temporary file
        watcher.EnableRaisingEvents = true;

        SystemEvents.DisplaySettingsChanged += DisplaysChanged;   // screen (un)plugged: popups move along

        checkTimer = new WinTimer { Interval = 10_000 };
        checkTimer.Tick += (_, _) => Check();
        checkTimer.Start();

        _ = CalendarLoop();
        _ = ChatLoop();
        if (test) TestPopup();
    }

    /// <summary>Everything that follows from the config and must change along live.</summary>
    void ApplyConfig()
    {
        UseLanguage(cfg.Language);
        miUpcoming.Text = T.MenuUpcoming;
        miRefresh.Text = T.MenuRefresh;
        miTestPopup.Text = T.MenuTestPopup;
        miChatPopup.Text = T.MenuChatPopup;
        miAutostart.Text = T.MenuAutostart;
        miSettings.Text = T.MenuSettings;
        miExit.Text = T.MenuExit;

        MeetingPopup.Position = cfg.Meetings.Position;
        MeetingPopup.DisplayNumber = cfg.Meetings.Screen;
        MeetingPopup.Reposition();
        chatPopup.Position = cfg.Chats.Position;
        chatPopup.DisplayNumber = cfg.Chats.Screen;
        chatPopup.FlashSeconds = cfg.Chats.FlashSeconds;
        chatPopup.Sound = cfg.Sound;

        var owner = chatPopup.Handle;   // parent for the WAM sign-in window (tray apps have no window of their own)
        pollers = cfg.ActiveTeams
            .Select(t => new ChatPoller(t, cfg.ClientIdFor(t), cfg.Chats.ChatTypes, chatState, () => owner))
            .ToList();
        BuildStatusItems();
    }

    void BuildStatusItems()
    {
        foreach (var item in statusItems.Values.Concat(signInItems.Values))
        {
            menu.Items.Remove(item);
            item.Dispose();
        }
        statusItems.Clear();
        signInItems.Clear();

        int i = 0;
        foreach (var c in cfg.ActiveCalendars)
            menu.Items.Insert(i++, statusItems[c] =
                new ToolStripMenuItem(F(T.StatusNotFetched, c.Name)) { Enabled = false });
        foreach (var p in pollers)
        {
            menu.Items.Insert(i++, statusItems[p.Job] =
                new ToolStripMenuItem(F(T.StatusNotFetched, TeamsName(p))) { Enabled = false });
            var signIn = new ToolStripMenuItem(F(T.MenuSignInTo, p.Job.Name), null, async (_, _) => await SignIn(p)) { Visible = false };
            menu.Items.Insert(i++, signInItems[p.Job] = signIn);
        }
    }

    static string TeamsName(ChatPoller p) => $"{p.Job.Name} Teams";

    void SetStatus(object key, string text)
    {
        if (statusItems.TryGetValue(key, out var item)) item.Text = text;
    }

    void Reload()
    {
        Config fresh;
        try { fresh = Config.Load(); }
        catch (IOException) when (File.Exists(Config.FilePath))
        {
            reloadTimer.Start();   // file still in use by the editor, try again shortly
            return;
        }
        catch (Exception e)
        {
            tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalloonConfigError, Shorten(e.Message, 150)), ToolTipIcon.Warning);
            return;
        }
        cfg = fresh;
        perCalendar.Clear();
        errorReported.Clear();
        ApplyConfig();
        tray.ShowBalloonTip(3000, "Meeting Alarm", T.BalloonReloaded, ToolTipIcon.Info);
        _ = RefreshCalendars();
        _ = PollChats();
    }

    // ------------------------------------------------------------ Calendars

    async Task CalendarLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            await RefreshCalendars();
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, cfg.Meetings.RefreshSeconds)), stop.Token); }
            catch (TaskCanceledException) { break; }
        }
    }

    async Task RefreshCalendars()
    {
        foreach (var c in cfg.ActiveCalendars.ToList())
        {
            if (!Uri.TryCreate(c.Url, UriKind.Absolute, out _))
            {
                SetStatus(c, F(T.StatusNoIcsLink, c.Name));
                continue;
            }
            try
            {
                var ics = await http.GetStringAsync(c.Url, stop.Token);
                var meetings = await Task.Run(() => IcsCalendar.Parse(c, ics));
                if (!cfg.Calendars.Contains(c)) continue;   // config was reloaded in the meantime
                perCalendar[c] = meetings;   // only replace on success
                SetStatus(c, F(T.StatusCalendarOk, c.Name, DateTime.Now, meetings.Count));
                errorReported.Remove(c);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                if (!cfg.Calendars.Contains(c)) continue;
                SetStatus(c, F(T.StatusError, c.Name, DateTime.Now, Shorten(e.Message, 70)));
                if (errorReported.Add(c))
                    tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalloonCalendarFailed, c.Name, Shorten(e.Message, 150)), ToolTipIcon.Warning);
            }
        }
        Check();
    }

    static string Shorten(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    void Check()
    {
        var now = DateTime.Now;
        foreach (var m in perCalendar.Values.SelectMany(x => x))
        {
            var sec = (m.Start - now).TotalSeconds;
            if (sec <= cfg.Meetings.MinutesBefore * 60 && sec > -120 && shown.Add(m.Key))
                new MeetingPopup(m, cfg).ShowPopup();
        }
    }

    void ShowUpcoming()
    {
        var lines = perCalendar.Values.SelectMany(x => x)
            .Where(m => m.Start > DateTime.Now.AddMinutes(-5))
            .OrderBy(m => m.Start).Take(20)
            .Select(m => F("{0:ddd} {0:t}   [{1}]   {2}", m.Start, m.Calendar.Name, m.Title))
            .ToList();
        MessageBox.Show(lines.Count > 0 ? string.Join("\n", lines) : T.NoMeetings, T.UpcomingTitle);
    }

    void TestPopup()
    {
        var calendar = cfg.Calendars.FirstOrDefault() ?? new CalendarConfig { Name = "Test" };
        new MeetingPopup(new Meeting(calendar, T.ExampleMeeting, DateTime.Now.AddSeconds(75),
            "test-" + Guid.NewGuid(), "https://teams.microsoft.com/l/meetup-join/test"), cfg).ShowPopup();
    }

    // ------------------------------------------------------------ Teams chats

    async Task ChatLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            await PollChats();
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, cfg.Chats.PollSeconds)), stop.Token); }
            catch (TaskCanceledException) { break; }
        }
    }

    async Task PollChats()
    {
        if (chatPolling) return;
        chatPolling = true;
        try
        {
            var all = new List<ChatRow>();
            foreach (var p in pollers.ToList())
            {
                if (p.NeedsSignIn)
                {
                    all.AddRange(p.Previous);
                    continue;
                }
                try
                {
                    all.AddRange(await p.Poll(stop.Token));
                    SetStatus(p.Job, F(T.StatusOk, TeamsName(p), DateTime.Now));
                    errorReported.Remove(p.Job);
                }
                catch (MsalUiRequiredException)
                {
                    all.AddRange(p.Previous);
                    MustSignIn(p);
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    all.AddRange(p.Previous);
                    SetStatus(p.Job, F(T.StatusError, TeamsName(p), DateTime.Now, Shorten(e.Message, 70)));
                    if (errorReported.Add(p.Job))
                        tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalloonChatsFailed, p.Job.Name, Shorten(e.Message, 150)), ToolTipIcon.Warning);
                }
            }
            chatRows = all;
            chatPopup.ShowRows(chatRows);
        }
        finally
        {
            chatPolling = false;
        }
    }

    void MustSignIn(ChatPoller p)
    {
        p.NeedsSignIn = true;
        SetStatus(p.Job, F(T.StatusSignInRequired, TeamsName(p)));
        if (signInItems.TryGetValue(p.Job, out var item)) item.Visible = true;

        // The very first time, open the sign-in window right away; after that never unasked, only via the menu.
        if (p.NeverSignedIn && autoSignInTried.Add($"{p.Job.Tenant}|{p.Job.LoginHint}"))
            _ = SignIn(p);
        else if (errorReported.Add(p.Job))
            tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalloonSignInAgain, p.Job.Name), ToolTipIcon.Warning);
    }

    async Task SignIn(ChatPoller p)
    {
        try
        {
            await p.SignIn();
            if (signInItems.TryGetValue(p.Job, out var item)) item.Visible = false;
            errorReported.Remove(p.Job);
            SetStatus(p.Job, F(T.StatusSignedIn, TeamsName(p)));
            await PollChats();
        }
        catch (Exception e)
        {
            tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalloonSignInFailed, p.Job.Name, Shorten(e.Message, 150)), ToolTipIcon.Warning);
        }
    }

    void Seen(IReadOnlyList<ChatRow> rows)
    {
        foreach (var r in rows.Where(r => !r.Key.StartsWith("test|")))
            chatState.Ack(r.Key, r.Newest);
        var gone = rows.Select(r => r.Key).ToHashSet();
        foreach (var p in pollers) p.Forget(gone);
        chatRows = chatRows.Where(r => !gone.Contains(r.Key)).ToList();
        chatPopup.ShowRows(chatRows);
    }

    void TestChatPopup()
    {
        // Example row (not persisted); disappears at the next poll or with ✓.
        var job = cfg.Teams.FirstOrDefault() ?? new TeamsConfig { Name = "Test" };
        chatRows = [.. chatRows.Where(r => !r.Key.StartsWith("test|")),
            new ChatRow(job, "test|example", T.ExampleColleague, 3, DateTimeOffset.Now, null)];
        chatPopup.ShowRows(chatRows);
    }

    void DisplaysChanged(object? sender, EventArgs e)
    {
        MeetingPopup.Reposition();
        chatPopup.Reposition();
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        stop.Cancel();
        watcher.Dispose();
        reloadTimer.Stop();
        checkTimer.Stop();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}
