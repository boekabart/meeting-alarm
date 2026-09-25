using Microsoft.Identity.Client;
using WinTimer = System.Windows.Forms.Timer;

sealed class AlarmContext : ApplicationContext
{
    Config cfg;
    readonly ContextMenuStrip menu = new();
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
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Komende meetings…", null, (_, _) => ToonKomende());
        menu.Items.Add("Nu verversen", null, async (_, _) => { await VerversAlles(); await PollChats(); });
        menu.Items.Add("Test-popup tonen", null, (_, _) => TestPopup());
        menu.Items.Add("Chat-popup tonen", null, (_, _) => TestChatPopup());
        var autostart = new ToolStripMenuItem("Start met Windows") { Checked = Autostart.Aan, CheckOnClick = true };
        autostart.CheckedChanged += (_, _) => Autostart.Aan = autostart.Checked;
        menu.Items.Add(autostart);
        menu.Items.Add("Instellingen openen", null, (_, _) => Config.OpenInKladblok());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => ExitThread());

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
        MeetingPopup.Positie = cfg.Meetings.Position;
        MeetingPopup.Herplaats();
        chatPopup.Positie = cfg.Chats.Position;
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
                new ToolStripMenuItem($"{a.Name}: nog niet opgehaald") { Enabled = false });
        foreach (var p in pollers)
        {
            menu.Items.Insert(i++, statusItems[p.Job] =
                new ToolStripMenuItem($"{p.Job.Name} Teams: nog niet opgehaald") { Enabled = false });
            var login = new ToolStripMenuItem($"Inloggen bij {p.Job.Name}…", null, async (_, _) => await Inloggen(p)) { Visible = false };
            menu.Items.Insert(i++, loginItems[p.Job] = login);
        }
    }

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
            tray.ShowBalloonTip(5000, "Meeting Alarm", $"Fout in config.json, oude instellingen blijven actief:\n{Kort(e.Message, 150)}", ToolTipIcon.Warning);
            return;
        }
        cfg = nieuw;
        perAgenda.Clear();
        foutGemeld.Clear();
        PasConfigToe();
        tray.ShowBalloonTip(3000, "Meeting Alarm", "Instellingen opnieuw geladen.", ToolTipIcon.Info);
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
                ZetStatus(sleutel, $"{a.Name}: nog geen ICS-link ingevuld");
                continue;
            }
            try
            {
                var ics = await http.GetStringAsync(a.Url, stop.Token);
                var lijst = await Task.Run(() => Agenda.Parse(a, ics));
                if (!cfg.Calendars.Contains(a)) continue;   // config is intussen herladen
                perAgenda[a] = lijst;   // alleen vervangen bij succes
                ZetStatus(sleutel, $"{a.Name}: OK om {DateTime.Now:HH:mm}, {lijst.Count} komende meeting(s)");
                foutGemeld.Remove(sleutel);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                if (!cfg.Calendars.Contains(a)) continue;
                ZetStatus(sleutel, $"{a.Name}: FOUT om {DateTime.Now:HH:mm} - {Kort(e.Message, 70)}");
                if (foutGemeld.Add(sleutel))
                    tray.ShowBalloonTip(5000, "Meeting Alarm", $"Agenda '{a.Name}' ophalen mislukt:\n{Kort(e.Message, 150)}", ToolTipIcon.Warning);
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
            .Select(m => $"{m.Start:ddd HH:mm}   [{m.Agenda.Name}]   {m.Titel}")
            .ToList();
        MessageBox.Show(regels.Count > 0 ? string.Join("\n", regels) : "Geen meetings gevonden in de komende 2 dagen.",
            "Komende meetings");
    }

    void TestPopup()
    {
        var agenda = cfg.Calendars.FirstOrDefault() ?? new CalendarConfig { Name = "Test" };
        new MeetingPopup(new Meeting(agenda, "Testmeeting: weekstart met het team", DateTime.Now.AddSeconds(75),
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
                    ZetStatus(sleutel, $"{p.Job.Name} Teams: OK om {DateTime.Now:HH:mm}");
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
                    ZetStatus(sleutel, $"{p.Job.Name} Teams: FOUT om {DateTime.Now:HH:mm} - {Kort(e.Message, 70)}");
                    if (foutGemeld.Add(sleutel))
                        tray.ShowBalloonTip(5000, "Meeting Alarm", $"Teams-chats '{p.Job.Name}' ophalen mislukt:\n{Kort(e.Message, 150)}", ToolTipIcon.Warning);
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
        ZetStatus(p.Job, $"{p.Job.Name} Teams: inloggen vereist");
        if (loginItems.TryGetValue(p.Job, out var item)) item.Visible = true;

        // De allereerste keer meteen het inlogvenster; daarna nooit ongevraagd, alleen via het menu.
        if (p.NooitIngelogd && autoInlogGeprobeerd.Add($"{p.Job.Tenant}|{p.Job.LoginHint}"))
            _ = Inloggen(p);
        else if (foutGemeld.Add(p.Job))
            tray.ShowBalloonTip(5000, "Meeting Alarm", $"Teams '{p.Job.Name}': opnieuw inloggen vereist (rechtsklik op het icoon).", ToolTipIcon.Warning);
    }

    async Task Inloggen(ChatPoller p)
    {
        try
        {
            await p.Inloggen();
            if (loginItems.TryGetValue(p.Job, out var item)) item.Visible = false;
            foutGemeld.Remove(p.Job);
            ZetStatus(p.Job, $"{p.Job.Name} Teams: ingelogd, ophalen…");
            await PollChats();
        }
        catch (Exception e)
        {
            tray.ShowBalloonTip(5000, "Meeting Alarm", $"Inloggen bij '{p.Job.Name}' mislukt:\n{Kort(e.Message, 150)}", ToolTipIcon.Warning);
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
            new ChatRij(job, "test|voorbeeld", "Voorbeeld Collega", 3, DateTimeOffset.Now, null)];
        chatPopup.Werk(chatRijen);
    }

    protected override void ExitThreadCore()
    {
        stop.Cancel();
        watcher.Dispose();
        herlaadTimer.Stop();
        checkTimer.Stop();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}
