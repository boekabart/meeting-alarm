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
    // Meetings per source: a CalendarConfig (ICS) or a TenantConfig (Graph).
    readonly Dictionary<object, List<Meeting>> meetingsBySource = new();
    // Keyed by config object (not by name): two calendars may share a name. Tenants use (tenant, "chats"/"calendar").
    readonly Dictionary<object, ToolStripMenuItem> statusItems = new();
    readonly Dictionary<TenantConfig, ToolStripMenuItem> signInItems = new();
    readonly HashSet<object> errorReported = new();
    readonly HashSet<string> shown = new();
    readonly HashSet<string> autoSignInTried = new();
    // Features that got "interaction required" from the silent token call: not signed in, or a permission not granted yet.
    readonly HashSet<(TenantConfig, Feature)> accessMissing = new();
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly CancellationTokenSource stop = new();

    readonly ChatState chatState = new();
    readonly ChatPopup chatPopup = new();
    List<TenantClient> clients = [];
    List<ChatRow> chatRows = [];
    bool chatPolling, graphCalendarPolling;

    public AlarmContext(Config cfg, bool test)
    {
        this.cfg = cfg;
        // Texts are set in ApplyConfig (the language can change live).
        miUpcoming = new ToolStripMenuItem("", null, (_, _) => ShowUpcoming());
        miRefresh = new ToolStripMenuItem("", null, async (_, _) => { await RefreshIcsCalendars(); await RefreshGraphCalendars(); await PollChats(); });
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

        _ = Loop(RefreshIcsCalendars, () => Math.Max(30, cfg.Meetings.RefreshSeconds));
        _ = Loop(RefreshGraphCalendars, () => Math.Max(15, cfg.Meetings.GraphRefreshSeconds));
        _ = Loop(PollChats, () => Math.Max(10, cfg.Chats.PollSeconds));
        if (test) TestPopup();
    }

    async Task Loop(Func<Task> work, Func<int> intervalSeconds)
    {
        while (!stop.IsCancellationRequested)
        {
            await work();
            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds()), stop.Token); }
            catch (TaskCanceledException) { break; }
        }
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
        clients = cfg.ActiveTenants
            .Select(t => new TenantClient(t, cfg.ClientIdFor(t), cfg.Chats.ChatTypes, chatState, () => owner))
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
        void AddStatus(object key, string name) =>
            menu.Items.Insert(i++, statusItems[key] = new ToolStripMenuItem(F(T.StatusNotFetched, name)) { Enabled = false });

        foreach (var c in cfg.ActiveCalendars)
            AddStatus(c, c.Name);
        foreach (var client in clients)
        {
            var t = client.Tenant;
            if (t.Calendar) AddStatus((t, "calendar"), OutlookName(t));
            if (t.Chats) AddStatus((t, "chats"), TeamsName(t));
            var signIn = new ToolStripMenuItem(F(T.MenuSignInTo, t.Name), null, async (_, _) => await SignIn(client)) { Visible = false };
            menu.Items.Insert(i++, signInItems[t] = signIn);
        }
    }

    static string TeamsName(TenantConfig t) => $"{t.Name} Teams";
    static string OutlookName(TenantConfig t) => $"{t.Name} Outlook";

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
        meetingsBySource.Clear();
        errorReported.Clear();
        ApplyConfig();
        tray.ShowBalloonTip(3000, "Meeting Alarm", T.BalloonReloaded, ToolTipIcon.Info);
        _ = RefreshIcsCalendars();
        _ = RefreshGraphCalendars();
        _ = PollChats();
    }

    static string Shorten(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // ------------------------------------------------------------ Meetings

    async Task RefreshIcsCalendars()
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
                meetingsBySource[c] = meetings;   // only replace on success
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

    async Task RefreshGraphCalendars()
    {
        if (graphCalendarPolling) return;
        graphCalendarPolling = true;
        try
        {
            foreach (var client in clients.Where(c => c.Tenant.Calendar).ToList())
            {
                var t = client.Tenant;
                var key = (t, "calendar");
                try
                {
                    var meetings = await client.GetMeetings(stop.Token);
                    if (!clients.Contains(client)) continue;   // config was reloaded in the meantime
                    meetingsBySource[t] = meetings;
                    SetStatus(key, F(T.StatusCalendarOk, OutlookName(t), DateTime.Now, meetings.Count));
                    errorReported.Remove(key);
                    AccessOk(client, Feature.Calendar);
                }
                catch (MsalUiRequiredException)
                {
                    // Only the calendar waits for a sign-in or permission; chats keep running.
                    AccessMissing(client, Feature.Calendar, key, OutlookName(t));
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    SetStatus(key, F(T.StatusError, OutlookName(t), DateTime.Now, Shorten(e.Message, 70)));
                    if (errorReported.Add(key))
                        tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalloonCalendarFailed, OutlookName(t), Shorten(e.Message, 150)), ToolTipIcon.Warning);
                }
            }
            Check();
        }
        finally
        {
            graphCalendarPolling = false;
        }
    }

    void Check()
    {
        var now = DateTime.Now;
        foreach (var m in meetingsBySource.Values.SelectMany(x => x))
        {
            var sec = (m.Start - now).TotalSeconds;
            if (sec <= cfg.Meetings.MinutesBefore * 60 && sec > -120 && shown.Add(m.Key))
                new MeetingPopup(m, cfg).ShowPopup();
        }
    }

    void ShowUpcoming()
    {
        var lines = meetingsBySource.Values.SelectMany(x => x)
            .Where(m => m.Start > DateTime.Now.AddMinutes(-5))
            .DistinctBy(m => m.Key)
            .OrderBy(m => m.Start).Take(20)
            .Select(m => F("{0:ddd} {0:t}   [{1}]   {2}", m.Start, m.CalendarName, m.Title))
            .ToList();
        MessageBox.Show(lines.Count > 0 ? string.Join("\n", lines) : T.NoMeetings, T.UpcomingTitle);
    }

    void TestPopup()
    {
        var (name, color) = cfg.ActiveTenants.FirstOrDefault(t => t.Calendar) is { } t ? (t.Name, t.Color)
            : cfg.Calendars.FirstOrDefault() is { } c ? (c.Name, c.Color)
            : ("Test", ColorParser.Default.Name);
        new MeetingPopup(new Meeting(name, color, T.ExampleMeeting, DateTime.Now.AddSeconds(75),
            "test-" + Guid.NewGuid(), "https://teams.microsoft.com/l/meetup-join/test"), cfg).ShowPopup();
    }

    // ------------------------------------------------------------ Teams chats

    async Task PollChats()
    {
        if (chatPolling) return;
        chatPolling = true;
        try
        {
            var all = new List<ChatRow>();
            foreach (var client in clients.Where(c => c.Tenant.Chats).ToList())
            {
                var key = (client.Tenant, "chats");
                try
                {
                    all.AddRange(await client.Poll(stop.Token));
                    SetStatus(key, F(T.StatusOk, TeamsName(client.Tenant), DateTime.Now));
                    errorReported.Remove(key);
                    AccessOk(client, Feature.Chats);
                }
                catch (MsalUiRequiredException)
                {
                    // Keep trying silently each round: once the permission is granted (e.g. admin consent in the portal) it just works.
                    all.AddRange(client.Previous);
                    AccessMissing(client, Feature.Chats, key, TeamsName(client.Tenant));
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    all.AddRange(client.Previous);
                    SetStatus(key, F(T.StatusError, TeamsName(client.Tenant), DateTime.Now, Shorten(e.Message, 70)));
                    if (errorReported.Add(key))
                        tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalloonChatsFailed, client.Tenant.Name, Shorten(e.Message, 150)), ToolTipIcon.Warning);
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

    // ------------------------------------------------------------ Sign-in

    static string FeatureText(Feature f) =>
        $"{(f == Feature.Chats ? T.FeatureChats : T.FeatureCalendar)} ({TenantClient.ScopeOf(f)})";

    string MissingText(TenantConfig t) =>
        string.Join(", ", Enum.GetValues<Feature>().Where(f => accessMissing.Contains((t, f))).Select(FeatureText));

    /// <summary>The tray item is "Sign in to X…" before the first sign-in, and names the missing permissions after that.</summary>
    void UpdateSignInItem(TenantClient client)
    {
        var t = client.Tenant;
        if (!signInItems.TryGetValue(t, out var item)) return;
        item.Visible = Enum.GetValues<Feature>().Any(f => accessMissing.Contains((t, f)));
        item.Text = client.NeverSignedIn ? F(T.MenuSignInTo, t.Name) : F(T.MenuGrantAccess, t.Name, MissingText(t));
    }

    void AccessMissing(TenantClient client, Feature feature, object statusKey, string statusName)
    {
        var t = client.Tenant;
        accessMissing.Add((t, feature));
        SetStatus(statusKey, client.NeverSignedIn
            ? F(T.StatusSignInRequired, statusName)
            : F(T.StatusAccessNeeded, statusName, TenantClient.ScopeOf(feature)));
        UpdateSignInItem(client);

        // The very first time, open the sign-in window right away; after that never unasked, only via the menu.
        if (client.NeverSignedIn && autoSignInTried.Add($"{t.Tenant}|{t.LoginHint}"))
            _ = SignIn(client);
        else if (errorReported.Add((t, feature, "access")))
            tray.ShowBalloonTip(5000, "Meeting Alarm", client.NeverSignedIn
                ? F(T.BalloonSignInAgain, t.Name)
                : F(T.BalloonAccessNeeded, t.Name, FeatureText(feature)), ToolTipIcon.Warning);
    }

    void AccessOk(TenantClient client, Feature feature)
    {
        if (!accessMissing.Remove((client.Tenant, feature))) return;
        errorReported.Remove((client.Tenant, feature, "access"));
        UpdateSignInItem(client);
    }

    async Task SignIn(TenantClient client)
    {
        var t = client.Tenant;
        try
        {
            await client.SignIn();
            foreach (var f in Enum.GetValues<Feature>()) AccessOk(client, f);
            if (t.Chats) SetStatus((t, "chats"), F(T.StatusSignedIn, TeamsName(t)));
            if (t.Calendar) SetStatus((t, "calendar"), F(T.StatusSignedIn, OutlookName(t)));
            await RefreshGraphCalendars();
            await PollChats();
        }
        catch (Exception e)
        {
            tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalloonSignInFailed, t.Name, Shorten(e.Message, 150)), ToolTipIcon.Warning);
        }
    }

    // ------------------------------------------------------------ Chat popup

    void Seen(IReadOnlyList<ChatRow> rows)
    {
        foreach (var r in rows.Where(r => !r.Key.StartsWith("test|")))
            chatState.Ack(r.Key, r.Newest);
        var gone = rows.Select(r => r.Key).ToHashSet();
        foreach (var c in clients) c.Forget(gone);
        chatRows = chatRows.Where(r => !gone.Contains(r.Key)).ToList();
        chatPopup.ShowRows(chatRows);
    }

    void TestChatPopup()
    {
        // Example row (not persisted); disappears at the next poll or with ✓.
        var tenant = cfg.Tenants.FirstOrDefault() ?? new TenantConfig { Name = "Test" };
        chatRows = [.. chatRows.Where(r => !r.Key.StartsWith("test|")),
            new ChatRow(tenant, "test|example", T.ExampleColleague, 3, DateTimeOffset.Now, null)];
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
