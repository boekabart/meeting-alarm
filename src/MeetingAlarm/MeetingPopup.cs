using System.Diagnostics;
using System.Media;
using WinTimer = System.Windows.Forms.Timer;

sealed class MeetingPopup : PopupBasis
{
    static readonly List<MeetingPopup> Open = new();

    /// <summary>Anker voor de stapel meeting-popups; na wijzigen <see cref="Herplaats"/> aanroepen.</summary>
    public static Positie Positie { get; set; } = Positie.RechtsOnder;

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
        kleur = ParseKleur(m.Agenda.Kleur);
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

        binnen.Controls.Add(Tekst(m.Agenda.Naam.ToUpperInvariant(), 10, tekstBreedte));
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

    public static void Herplaats()
    {
        var punten = Plaatsing.Bereken(Positie, Werkgebied, Open.Select(p => p.Size).ToList());
        for (int i = 0; i < Open.Count; i++)
            Open[i].Location = punten[i];
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
        HouBovenop();
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
