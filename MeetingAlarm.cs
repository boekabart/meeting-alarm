#:sdk Microsoft.NET.Sdk
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false
#:property PublishSingleFile=true
#:property SelfContained=false
#:property RuntimeIdentifier=win-x64
#:package Ical.Net@4.3.1

// Meeting Alarm - grote, blijvende popup rechtsonder voordat een meeting begint.
// Leest meerdere Outlook-agenda's via hun gepubliceerde ICS-link.
//
// Draaien:   dotnet run MeetingAlarm.cs
// Testen:    dotnet run MeetingAlarm.cs -- --test
// Exe maken: dotnet publish MeetingAlarm.cs -o publish
// Instellingen: %APPDATA%\MeetingAlarm\config.json (wordt bij eerste start aangemaakt,
//               wijzigingen worden automatisch geladen)

using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "MeetingAlarm_SingleInstance", out bool eerste);
        if (!eerste)
        {
            MessageBox.Show("Meeting Alarm draait al (zie systeemvak).", "Meeting Alarm");
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var config = Config.LaadOfMaak();
        if (config is null) return;
        Application.Run(new AlarmContext(config, args.Contains("--test")));
    }
}

// ------------------------------------------------------------------ Config
sealed class AgendaConfig
{
    public string Naam { get; set; } = "";
    public string Url { get; set; } = "";
    public string Kleur { get; set; } = "#c62828";
}

sealed class Config
{
    public int MinutenVooraf { get; set; } = 5;
    public int SnoozeSeconden { get; set; } = 60;
    public int AutoSluitenNaMinuten { get; set; } = 15;
    public int VerversSeconden { get; set; } = 180;
    public bool Geluid { get; set; } = true;
    public List<AgendaConfig> Agendas { get; set; } = new();

    public static string Pad => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingAlarm", "config.json");

    static readonly JsonSerializerOptions Opties = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static void OpenInKladblok() =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Pad}\"") { UseShellExecute = true });

    public static Config Laad() =>
        JsonSerializer.Deserialize<Config>(File.ReadAllText(Pad), Opties) ?? new Config();

    public static Config? LaadOfMaak()
    {
        if (!File.Exists(Pad))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pad)!);
            var voorbeeld = new Config
            {
                Agendas =
                {
                    new AgendaConfig { Naam = "Job 1", Url = "PLAK_HIER_ICS_LINK_JOB_1", Kleur = "#c62828" },
                    new AgendaConfig { Naam = "Job 2", Url = "PLAK_HIER_ICS_LINK_JOB_2", Kleur = "#1565c0" },
                }
            };
            File.WriteAllText(Pad, JsonSerializer.Serialize(voorbeeld, Opties));
            if (MessageBox.Show("Meeting Alarm automatisch starten met Windows?", "Meeting Alarm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Autostart.Aan = true;
            MessageBox.Show($"Instellingenbestand aangemaakt:\n{Pad}\n\nVul je ICS-links in en sla op; wijzigingen worden automatisch geladen.",
                "Meeting Alarm");
            OpenInKladblok();
        }
        try
        {
            return Laad();
        }
        catch (Exception e)
        {
            MessageBox.Show($"Fout in {Pad}:\n\n{e.Message}", "Meeting Alarm");
            return null;
        }
    }
}

sealed record Meeting(AgendaConfig Agenda, string Titel, DateTime Start, string Uid, string? Link)
{
    public string Sleutel => $"{Agenda.Naam}|{Uid}|{Start:O}";
}

// ------------------------------------------------------------------ Tray-app
sealed class AlarmContext : ApplicationContext
{
    static readonly Regex LinkRe = new(
        @"https://(?:teams\.microsoft\.com/l/meetup-join|teams\.live\.com/meet|[\w.-]*zoom\.us/j|meet\.google\.com)/[^\s""<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    Config cfg;
    readonly ContextMenuStrip menu = new();
    readonly NotifyIcon tray;
    readonly WinTimer checkTimer;
    readonly WinTimer herlaadTimer = new() { Interval = 500 };   // debounce: editors schrijven vaak meerdere keren
    readonly FileSystemWatcher watcher;
    readonly Dictionary<string, List<Meeting>> perAgenda = new();
    readonly Dictionary<string, ToolStripMenuItem> statusItems = new();
    readonly HashSet<string> foutGemeld = new();
    readonly HashSet<string> getoond = new();
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly CancellationTokenSource stop = new();

    public AlarmContext(Config cfg, bool test)
    {
        this.cfg = cfg;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Komende meetings…", null, (_, _) => ToonKomende());
        menu.Items.Add("Nu verversen", null, async (_, _) => await VerversAlles());
        menu.Items.Add("Test-popup tonen", null, (_, _) => TestPopup());
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
        BouwStatusItems();

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
        if (test) TestPopup();
    }

    void BouwStatusItems()
    {
        foreach (var item in statusItems.Values)
        {
            menu.Items.Remove(item);
            item.Dispose();
        }
        statusItems.Clear();
        int i = 0;
        foreach (var a in cfg.Agendas)
        {
            var item = new ToolStripMenuItem($"{a.Naam}: nog niet opgehaald") { Enabled = false };
            statusItems[a.Naam] = item;
            menu.Items.Insert(i++, item);
        }
    }

    void ZetStatus(AgendaConfig a, string tekst)
    {
        if (statusItems.TryGetValue(a.Naam, out var item)) item.Text = tekst;
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
        BouwStatusItems();
        tray.ShowBalloonTip(3000, "Meeting Alarm", "Instellingen opnieuw geladen.", ToolTipIcon.Info);
        _ = VerversAlles();
    }

    async Task VerversLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            await VerversAlles();
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, cfg.VerversSeconden)), stop.Token); }
            catch (TaskCanceledException) { break; }
        }
    }

    async Task VerversAlles()
    {
        foreach (var a in cfg.Agendas)
        {
            if (!Uri.TryCreate(a.Url, UriKind.Absolute, out _))
            {
                ZetStatus(a, $"{a.Naam}: nog geen ICS-link ingevuld");
                continue;
            }
            try
            {
                var ics = await http.GetStringAsync(a.Url, stop.Token);
                var lijst = await Task.Run(() => Parse(a, ics));
                if (!cfg.Agendas.Contains(a)) continue;   // config is intussen herladen
                perAgenda[a.Naam] = lijst;   // alleen vervangen bij succes
                ZetStatus(a, $"{a.Naam}: OK om {DateTime.Now:HH:mm}, {lijst.Count} komende meeting(s)");
                foutGemeld.Remove(a.Naam);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                if (!cfg.Agendas.Contains(a)) continue;
                ZetStatus(a, $"{a.Naam}: FOUT om {DateTime.Now:HH:mm} - {Kort(e.Message, 70)}");
                if (foutGemeld.Add(a.Naam))
                    tray.ShowBalloonTip(5000, "Meeting Alarm", $"Agenda '{a.Naam}' ophalen mislukt:\n{Kort(e.Message, 150)}", ToolTipIcon.Warning);
            }
        }
        Check();
    }

    static string Kort(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    static List<Meeting> Parse(AgendaConfig a, string ics)
    {
        var cal = Calendar.Load(ics);
        var van = DateTime.Now.AddHours(-1);
        var tot = DateTime.Now.AddDays(2);
        var res = new List<Meeting>();

        foreach (var occ in cal.GetOccurrences(van, tot))
        {
            if (occ.Source is not CalendarEvent ev || ev.IsAllDay) continue;
            if (string.Equals(ev.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;

            var titel = string.IsNullOrWhiteSpace(ev.Summary) ? "(geen titel)" : ev.Summary.Trim();
            if (titel.StartsWith("Geannuleerd", StringComparison.OrdinalIgnoreCase) ||
                titel.StartsWith("Canceled", StringComparison.OrdinalIgnoreCase) ||
                titel.StartsWith("Cancelled", StringComparison.OrdinalIgnoreCase)) continue;

            var teamsUrl = ev.Properties
                .FirstOrDefault(p => string.Equals(p.Name, "X-MICROSOFT-SKYPETEAMSMEETINGURL", StringComparison.OrdinalIgnoreCase))
                ?.Value?.ToString();
            var m = LinkRe.Match($"{teamsUrl} {ev.Location} {ev.Description}");

            res.Add(new Meeting(a, titel, occ.Period.StartTime.AsSystemLocal, ev.Uid ?? titel, m.Success ? m.Value : null));
        }
        return res.OrderBy(x => x.Start).ToList();
    }

    void Check()
    {
        var nu = DateTime.Now;
        foreach (var m in perAgenda.Values.SelectMany(x => x))
        {
            var sec = (m.Start - nu).TotalSeconds;
            if (sec <= cfg.MinutenVooraf * 60 && sec > -120 && getoond.Add(m.Sleutel))
                new PopupForm(m, cfg).Toon();
        }
    }

    void ToonKomende()
    {
        var regels = perAgenda.Values.SelectMany(x => x)
            .Where(m => m.Start > DateTime.Now.AddMinutes(-5))
            .OrderBy(m => m.Start).Take(20)
            .Select(m => $"{m.Start:ddd HH:mm}   [{m.Agenda.Naam}]   {m.Titel}")
            .ToList();
        MessageBox.Show(regels.Count > 0 ? string.Join("\n", regels) : "Geen meetings gevonden in de komende 2 dagen.",
            "Komende meetings");
    }

    void TestPopup()
    {
        var agenda = cfg.Agendas.FirstOrDefault() ?? new AgendaConfig { Naam = "Test" };
        new PopupForm(new Meeting(agenda, "Testmeeting: weekstart met het team", DateTime.Now.AddSeconds(75),
            "test-" + Guid.NewGuid(), "https://teams.microsoft.com/l/meetup-join/test"), cfg).Toon();
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

// ------------------------------------------------------------------ Popup
sealed class PopupForm : Form
{
    static readonly List<PopupForm> Open = new();
    static readonly Color KnipperKleur = Color.FromArgb(255, 111, 0);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    readonly Meeting m;
    readonly Config cfg;
    readonly Color kleur;
    readonly FlowLayoutPanel binnen;
    readonly Label lblTijd;
    readonly WinTimer tick = new() { Interval = 1000 };
    bool knipperAan;

    protected override bool ShowWithoutActivation => true;   // geen focus stelen tijdens typen

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x8;   // WS_EX_TOOLWINDOW (niet in Alt-Tab) | WS_EX_TOPMOST
            return cp;
        }
    }

    public PopupForm(Meeting m, Config cfg)
    {
        this.m = m;
        this.cfg = cfg;
        kleur = ParseKleur(m.Agenda.Kleur);

        float s = DeviceDpi / 96f;
        int S(int px) => (int)Math.Round(px * s);
        int tekstBreedte = S(400);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(255, 214, 0);   // gele rand
        Padding = new Padding(S(5));
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        binnen = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = kleur,
            Padding = new Padding(S(14), S(10), S(14), S(10)),
            Margin = Padding.Empty,
        };

        Label Tekst(string t, float pt) => new()
        {
            Text = t,
            AutoSize = true,
            MinimumSize = new Size(tekstBreedte, 0),
            MaximumSize = new Size(tekstBreedte, 0),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", pt, FontStyle.Bold),
            UseMnemonic = false,
        };

        Button Knop(string t, Action actie)
        {
            var b = new Button { Text = t, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), UseVisualStyleBackColor = true };
            b.Click += (_, _) => actie();
            return b;
        }

        binnen.Controls.Add(Tekst(m.Agenda.Naam.ToUpperInvariant(), 10));
        binnen.Controls.Add(Tekst(m.Titel, 15));
        lblTijd = Tekst("", 20);
        lblTijd.Margin = new Padding(3, S(4), 3, S(8));
        binnen.Controls.Add(lblTijd);

        var knoppen = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        if (m.Link is not null)
            knoppen.Controls.Add(Knop("Deelnemen", () =>
            {
                Process.Start(new ProcessStartInfo(m.Link) { UseShellExecute = true });
                Close();
            }));
        knoppen.Controls.Add(Knop("Straks", Snooze));
        knoppen.Controls.Add(Knop("Sluiten", Close));
        binnen.Controls.Add(knoppen);

        Controls.Add(binnen);
        tick.Tick += (_, _) => Tik();
    }

    static Color ParseKleur(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return Color.Firebrick; }
    }

    public void Toon()
    {
        _ = Handle;          // forceer layout zodat de hoogte klopt vóór positioneren
        PerformLayout();
        Open.Add(this);
        Herplaats();
        Show();
        Speel();
        Tik();
        tick.Start();
    }

    void Speel()
    {
        if (cfg.Geluid) SystemSounds.Exclamation.Play();
    }

    static void Herplaats()
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;   // houdt rekening met de taakbalk
        int y = wa.Bottom;
        foreach (var p in Open)
        {
            y -= p.Height + 10;
            p.Location = new Point(wa.Right - p.Width - 10, y);
        }
    }

    void Tik()
    {
        var sec = (int)Math.Ceiling((m.Start - DateTime.Now).TotalSeconds);
        bool knipperen;
        if (sec > 0)
        {
            lblTijd.Text = $"Start over {sec / 60}:{sec % 60:00}   ({m.Start:HH:mm})";
            knipperen = sec <= 60;   // laatste minuut knipperen
        }
        else
        {
            int min = -sec / 60;
            if (min >= cfg.AutoSluitenNaMinuten) { Close(); return; }
            lblTijd.Text = min == 0 ? "NU BEGONNEN!" : $"Begonnen, {min} min geleden";
            knipperen = true;
        }
        knipperAan = knipperen && !knipperAan;
        binnen.BackColor = knipperAan ? KnipperKleur : kleur;

        if (Visible)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    void Snooze()
    {
        tick.Stop();
        Open.Remove(this);
        Hide();
        Herplaats();

        var wacht = new WinTimer { Interval = Math.Max(5, cfg.SnoozeSeconden) * 1000 };
        wacht.Tick += (_, _) =>
        {
            wacht.Dispose();
            if (IsDisposed) return;
            Open.Add(this);
            Herplaats();
            Show();
            Speel();
            Tik();
            tick.Start();
        };
        wacht.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tick.Dispose();
        Open.Remove(this);
        Herplaats();
        base.OnFormClosed(e);
    }
}

// ------------------------------------------------------------------ Autostart
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Naam = "MeetingAlarm";

    public static bool Aan
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Naam) is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(Naam, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Naam, false);
        }
    }
}
