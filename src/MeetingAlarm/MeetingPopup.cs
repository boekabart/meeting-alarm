using System.Diagnostics;
using System.Media;
using WinTimer = System.Windows.Forms.Timer;

sealed class MeetingPopup : PopupBasis
{
    static readonly List<MeetingPopup> Open = new();

    /// <summary>Anker voor de stapel meeting-popups; na wijzigen <see cref="Herplaats"/> aanroepen.</summary>
    public static Position Positie { get; set; } = Position.BottomRight;
    public static int Scherm { get; set; }

    readonly Meeting m;
    readonly Config cfg;
    readonly Color kleur;
    readonly FlowLayoutPanel binnen;
    readonly Label lblTijd;
    readonly WinTimer tick = new() { Interval = 1000 };
    bool knipperAan;

    public MeetingPopup(Meeting m, Config cfg)
    {
        this.m = m;
        this.cfg = cfg;
        kleur = Kleur.Parse(m.Agenda.Color);
        int tekstBreedte = S(400);

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

        binnen.Controls.Add(Tekst(m.Agenda.Name.ToUpperInvariant(), 10, tekstBreedte));
        binnen.Controls.Add(Tekst(m.Titel, 15, tekstBreedte));
        lblTijd = Tekst("", 20, tekstBreedte);
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
            knoppen.Controls.Add(Knop(T.Deelnemen, () =>
            {
                Process.Start(new ProcessStartInfo(m.Link) { UseShellExecute = true });
                Close();
            }));
        knoppen.Controls.Add(Knop(T.Straks, Snooze));
        knoppen.Controls.Add(Knop(T.Sluiten, Close));
        binnen.Controls.Add(knoppen);

        Controls.Add(binnen);
        tick.Tick += (_, _) => Tik();
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
        if (cfg.Sound) SystemSounds.Exclamation.Play();
    }

    public static void Herplaats()
    {
        var punten = Plaatsing.Bereken(Positie, Plaatsing.Werkgebied(Scherm), Open.Select(p => p.Size).ToList());
        for (int i = 0; i < Open.Count; i++)
            Open[i].Location = punten[i];
    }

    void Tik()
    {
        var sec = (int)Math.Ceiling((m.Start - DateTime.Now).TotalSeconds);
        bool knipperen;
        if (sec > 0)
        {
            lblTijd.Text = F(T.StartOver, sec / 60, sec % 60, m.Start);
            knipperen = sec <= 60;   // laatste minuut knipperen
        }
        else
        {
            int min = -sec / 60;
            if (cfg.Meetings.AutoCloseAfterMinutes > 0 && min >= cfg.Meetings.AutoCloseAfterMinutes) { Close(); return; }
            lblTijd.Text = min == 0 ? T.NuBegonnen : F(T.BegonnenGeleden, min);
            knipperen = true;
        }
        knipperAan = knipperen && !knipperAan;
        binnen.BackColor = knipperAan ? KnipperKleur : kleur;
        HouBovenop();
    }

    void Snooze()
    {
        tick.Stop();
        Open.Remove(this);
        Hide();
        Herplaats();

        var wacht = new WinTimer { Interval = Math.Max(5, cfg.Meetings.SnoozeSeconds) * 1000 };
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
