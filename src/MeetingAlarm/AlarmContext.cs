using Microsoft.Identity.Client;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

sealed class AlarmContext : ApplicationContext
{
    Config cfg;
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem miKomende, miVerversen, miTestPopup, miChatPopup, miAutostart, miInstellingen, miAfsluiten;
    readonly NotifyIcon tray;
    readonly WinTimer checkTimer;
    readonly WinTimer herlaadTimer = new() { Interval = 500 };   // debounce: editors schrijven vaak meerdere keren
    readonly FileSystemWatcher watcher;
    readonly Dictionary<CalendarConfig, List<Meeting>> perAgenda = new();
    // Per config-object (niet per naam): twee agenda's mogen dezelfde naam hebben.
    readonly Dictionary<object, ToolStripMenuItem> statusItems = new();
    readonly Dictionary<TeamsConfig, ToolStripMenuItem> loginItems = new();
    readonly HashSet<object> foutGemeld = new();
    readonly HashSet<string> getoond = new();
    readonly HashSet<string> autoInlogGeprobeerd = new();
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly CancellationTokenSource stop = new();

    readonly ChatState chatState = new();
    readonly ChatPopup chatPopup = new();
    List<ChatPoller> pollers = [];
    List<ChatRij> chatRijen = [];
    bool chatBezig;

    public AlarmContext(Config cfg, bool test)
    {
        this.cfg = cfg;
        // Teksten volgen in PasConfigToe (taal kan live wijzigen).
        miKomende = new ToolStripMenuItem("", null, (_, _) => ToonKomende());
        miVerversen = new ToolStripMenuItem("", null, async (_, _) => { await VerversAlles(); await PollChats(); });
        miTestPopup = new ToolStripMenuItem("", null, (_, _) => TestPopup());
        miChatPopup = new ToolStripMenuItem("", null, (_, _) => TestChatPopup());
        miAutostart = new ToolStripMenuItem("") { Checked = Autostart.Aan, CheckOnClick = true };
        miAutostart.CheckedChanged += (_, _) => Autostart.Aan = miAutostart.Checked;
        miInstellingen = new ToolStripMenuItem("", null, (_, _) => Config.OpenInKladblok());
        miAfsluiten = new ToolStripMenuItem("", null, (_, _) => ExitThread());
        menu.Items.AddRange([new ToolStripSeparator(), miKomende, miVerversen, miTestPopup, miChatPopup, miAutostart, miInstellingen,
            new ToolStripSeparator(), miAfsluiten]);

        tray = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "Meeting Alarm",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => ToonKomende();

        chatPopup.Gezien += Gezien;
        PasConfigToe();

        // FileSystemWatcher vuurt op een threadpool-thread; via de UI-context de debounce-timer (her)starten.
        var ui = SynchronizationContext.Current!;
        herlaadTimer.Tick += (_, _) => { herlaadTimer.Stop(); Herlaad(); };
        watcher = new FileSystemWatcher(Path.GetDirectoryName(Config.Pad)!, Path.GetFileName(Config.Pad))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler gewijzigd = (_, _) => ui.Post(_ => { herlaadTimer.Stop(); herlaadTimer.Start(); }, null);
        watcher.Changed += gewijzigd;
        watcher.Created += gewijzigd;
        watcher.Renamed += (s, e) => gewijzigd(s, e);   // editors die via een tijdelijk bestand opslaan
        watcher.EnableRaisingEvents = true;

        SystemEvents.DisplaySettingsChanged += SchermenGewijzigd;   // scherm (los)gekoppeld: popups verhuizen mee

        checkTimer = new WinTimer { Interval = 10_000 };
        checkTimer.Tick += (_, _) => Check();
        checkTimer.Start();

        _ = VerversLoop();
        _ = ChatLoop();
        if (test) TestPopup();
    }

    /// <summary>Alles wat uit de config volgt en live mee moet veranderen.</summary>
    void PasConfigToe()
    {
        Kies(cfg.Language);
        miKomende.Text = T.MenuKomende;
        miVerversen.Text = T.MenuVerversen;
        miTestPopup.Text = T.MenuTestPopup;
        miChatPopup.Text = T.MenuChatPopup;
        miAutostart.Text = T.MenuAutostart;
        miInstellingen.Text = T.MenuInstellingen;
        miAfsluiten.Text = T.MenuAfsluiten;

        MeetingPopup.Positie = cfg.Meetings.Position;
        MeetingPopup.Scherm = cfg.Meetings.Screen;
        MeetingPopup.Herplaats();
        chatPopup.Positie = cfg.Chats.Position;
        chatPopup.Scherm = cfg.Chats.Screen;
        chatPopup.KnipperSeconden = cfg.Chats.FlashSeconds;
        chatPopup.Geluid = cfg.Sound;

        var eigenaar = chatPopup.Handle;   // ouder voor het WAM-inlogvenster (tray-apps hebben geen eigen venster)
        pollers = cfg.ActieveTeams
            .Select(t => new ChatPoller(t, cfg.ClientIdVoor(t), cfg.Chats.ChatTypes, chatState, () => eigenaar))
            .ToList();
        BouwStatusItems();
    }

    void BouwStatusItems()
    {
        foreach (var item in statusItems.Values.Concat(loginItems.Values))
        {
            menu.Items.Remove(item);
            item.Dispose();
        }
        statusItems.Clear();
        loginItems.Clear();

        int i = 0;
        foreach (var a in cfg.ActieveAgendas)
            menu.Items.Insert(i++, statusItems[a] =
                new ToolStripMenuItem(F(T.StatusNogNiet, a.Name)) { Enabled = false });
        foreach (var p in pollers)
        {
            menu.Items.Insert(i++, statusItems[p.Job] =
                new ToolStripMenuItem(F(T.StatusNogNiet, TeamsNaam(p))) { Enabled = false });
            var login = new ToolStripMenuItem(F(T.MenuInloggenBij, p.Job.Name), null, async (_, _) => await Inloggen(p)) { Visible = false };
            menu.Items.Insert(i++, loginItems[p.Job] = login);
        }
    }

    static string TeamsNaam(ChatPoller p) => $"{p.Job.Name} Teams";

    void ZetStatus(object sleutel, string tekst)
    {
        if (statusItems.TryGetValue(sleutel, out var item)) item.Text = tekst;
    }

    void Herlaad()
    {
        Config nieuw;
        try { nieuw = Config.Laad(); }
        catch (IOException) when (File.Exists(Config.Pad))
        {
            herlaadTimer.Start();   // bestand nog in gebruik door de editor, zo nog eens proberen
            return;
        }
        catch (Exception e)
        {
            tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalConfigFout, Kort(e.Message, 150)), ToolTipIcon.Warning);
            return;
        }
        cfg = nieuw;
        perAgenda.Clear();
        foutGemeld.Clear();
        PasConfigToe();
        tray.ShowBalloonTip(3000, "Meeting Alarm", T.BalHerladen, ToolTipIcon.Info);
        _ = VerversAlles();
        _ = PollChats();
    }

    // ------------------------------------------------------------ Agenda's

    async Task VerversLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            await VerversAlles();
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, cfg.Meetings.RefreshSeconds)), stop.Token); }
            catch (TaskCanceledException) { break; }
        }
    }

    async Task VerversAlles()
    {
        foreach (var a in cfg.ActieveAgendas.ToList())
        {
            var sleutel = a;
            if (!Uri.TryCreate(a.Url, UriKind.Absolute, out _))
            {
                ZetStatus(sleutel, F(T.StatusGeenIcs, a.Name));
                continue;
            }
            try
            {
                var ics = await http.GetStringAsync(a.Url, stop.Token);
                var lijst = await Task.Run(() => Agenda.Parse(a, ics));
                if (!cfg.Calendars.Contains(a)) continue;   // config is intussen herladen
                perAgenda[a] = lijst;   // alleen vervangen bij succes
                ZetStatus(sleutel, F(T.StatusAgendaOk, a.Name, DateTime.Now, lijst.Count));
                foutGemeld.Remove(sleutel);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                if (!cfg.Calendars.Contains(a)) continue;
                ZetStatus(sleutel, F(T.StatusFout, a.Name, DateTime.Now, Kort(e.Message, 70)));
                if (foutGemeld.Add(sleutel))
                    tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalAgendaMislukt, a.Name, Kort(e.Message, 150)), ToolTipIcon.Warning);
            }
        }
        Check();
    }

    static string Kort(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    void Check()
    {
        var nu = DateTime.Now;
        foreach (var m in perAgenda.Values.SelectMany(x => x))
        {
            var sec = (m.Start - nu).TotalSeconds;
            if (sec <= cfg.Meetings.MinutesBefore * 60 && sec > -120 && getoond.Add(m.Sleutel))
                new MeetingPopup(m, cfg).Toon();
        }
    }

    void ToonKomende()
    {
        var regels = perAgenda.Values.SelectMany(x => x)
            .Where(m => m.Start > DateTime.Now.AddMinutes(-5))
            .OrderBy(m => m.Start).Take(20)
            .Select(m => F("{0:ddd} {0:t}   [{1}]   {2}", m.Start, m.Agenda.Name, m.Titel))
            .ToList();
        MessageBox.Show(regels.Count > 0 ? string.Join("\n", regels) : T.GeenMeetings, T.KomendeTitel);
    }

    void TestPopup()
    {
        var agenda = cfg.Calendars.FirstOrDefault() ?? new CalendarConfig { Name = "Test" };
        new MeetingPopup(new Meeting(agenda, T.TestMeeting, DateTime.Now.AddSeconds(75),
            "test-" + Guid.NewGuid(), "https://teams.microsoft.com/l/meetup-join/test"), cfg).Toon();
    }

    // ------------------------------------------------------------ Teams-chats

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
        if (chatBezig) return;
        chatBezig = true;
        try
        {
            var alle = new List<ChatRij>();
            foreach (var p in pollers.ToList())
            {
                var sleutel = p.Job;
                if (p.InloggenNodig)
                {
                    alle.AddRange(p.Vorige);
                    continue;
                }
                try
                {
                    alle.AddRange(await p.Poll(stop.Token));
                    ZetStatus(sleutel, F(T.StatusOk, TeamsNaam(p), DateTime.Now));
                    foutGemeld.Remove(sleutel);
                }
                catch (MsalUiRequiredException)
                {
                    alle.AddRange(p.Vorige);
                    MoetInloggen(p);
                }
                catch (Exception e) when (!stop.IsCancellationRequested)
                {
                    alle.AddRange(p.Vorige);
                    ZetStatus(sleutel, F(T.StatusFout, TeamsNaam(p), DateTime.Now, Kort(e.Message, 70)));
                    if (foutGemeld.Add(sleutel))
                        tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalChatsMislukt, p.Job.Name, Kort(e.Message, 150)), ToolTipIcon.Warning);
                }
            }
            chatRijen = alle;
            chatPopup.Werk(chatRijen);
        }
        finally
        {
            chatBezig = false;
        }
    }

    void MoetInloggen(ChatPoller p)
    {
        p.InloggenNodig = true;
        ZetStatus(p.Job, F(T.StatusInloggenVereist, TeamsNaam(p)));
        if (loginItems.TryGetValue(p.Job, out var item)) item.Visible = true;

        // De allereerste keer meteen het inlogvenster; daarna nooit ongevraagd, alleen via het menu.
        if (p.NooitIngelogd && autoInlogGeprobeerd.Add($"{p.Job.Tenant}|{p.Job.LoginHint}"))
            _ = Inloggen(p);
        else if (foutGemeld.Add(p.Job))
            tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalOpnieuwInloggen, p.Job.Name), ToolTipIcon.Warning);
    }

    async Task Inloggen(ChatPoller p)
    {
        try
        {
            await p.Inloggen();
            if (loginItems.TryGetValue(p.Job, out var item)) item.Visible = false;
            foutGemeld.Remove(p.Job);
            ZetStatus(p.Job, F(T.StatusIngelogd, TeamsNaam(p)));
            await PollChats();
        }
        catch (Exception e)
        {
            tray.ShowBalloonTip(5000, "Meeting Alarm", F(T.BalInloggenMislukt, p.Job.Name, Kort(e.Message, 150)), ToolTipIcon.Warning);
        }
    }

    void Gezien(IReadOnlyList<ChatRij> rijen)
    {
        foreach (var r in rijen.Where(r => !r.Sleutel.StartsWith("test|")))
            chatState.Ack(r.Sleutel, r.Nieuwste);
        var weg = rijen.Select(r => r.Sleutel).ToHashSet();
        foreach (var p in pollers) p.Vergeet(weg);
        chatRijen = chatRijen.Where(r => !weg.Contains(r.Sleutel)).ToList();
        chatPopup.Werk(chatRijen);
    }

    void TestChatPopup()
    {
        // Voorbeeldrij (wordt niet bewaard); verdwijnt bij de volgende poll of met ✓.
        var job = cfg.Teams.FirstOrDefault() ?? new TeamsConfig { Name = "Test" };
        chatRijen = [.. chatRijen.Where(r => !r.Sleutel.StartsWith("test|")),
            new ChatRij(job, "test|voorbeeld", T.VoorbeeldCollega, 3, DateTimeOffset.Now, null)];
        chatPopup.Werk(chatRijen);
    }

    void SchermenGewijzigd(object? sender, EventArgs e)
    {
        MeetingPopup.Herplaats();
        chatPopup.Herplaats();
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.DisplaySettingsChanged -= SchermenGewijzigd;
        stop.Cancel();
        watcher.Dispose();
        herlaadTimer.Stop();
        checkTimer.Stop();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}
