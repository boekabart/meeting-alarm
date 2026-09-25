using System.Diagnostics;
using System.Media;
using WinTimer = System.Windows.Forms.Timer;

/// <summary>Eén venster met alle chats die ongelezen én niet-geackte berichten hebben. Alleen zichtbaar als er iets is.</summary>
sealed class ChatPopup : PopupBasis
{
    const int MaxRijen = 10;
    static readonly Color Achtergrond = Color.FromArgb(38, 50, 56);

    readonly FlowLayoutPanel binnen;
    readonly WinTimer knipper = new() { Interval = 500 };
    readonly ToolTip tip = new();
    Dictionary<string, int> vorige = new();
    string handtekening = "";
    int knipperTikken;
    Position positie = Position.MiddleRight;
    int scherm;

    /// <summary>De gebruiker heeft deze rijen gezien (✓ of "Alles gezien").</summary>
    public event Action<IReadOnlyList<ChatRij>>? Gezien;

    public int KnipperSeconden { get; set; } = 3;
    public bool Geluid { get; set; } = true;

    public Position Positie
    {
        get => positie;
        set { positie = value; Herplaats(); }
    }

    public int Scherm
    {
        get => scherm;
        set { scherm = value; Herplaats(); }
    }

    public ChatPopup()
    {
        binnen = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Achtergrond,
            Padding = new Padding(S(12), S(8), S(12), S(8)),
            Margin = Padding.Empty,
        };
        Controls.Add(binnen);

        knipper.Tick += (_, _) =>
        {
            knipperTikken--;
            binnen.BackColor = knipperTikken > 0 && knipperTikken % 2 == 1 ? KnipperKleur : Achtergrond;
            if (knipperTikken <= 0) knipper.Stop();
            HouBovenop();
        };
    }

    /// <summary>Toon de actuele stand. Knippert als er een chat bij kwam of een teller omhoog ging.</summary>
    public void Werk(IReadOnlyList<ChatRij> rijen)
    {
        bool meer = rijen.Any(r => !vorige.TryGetValue(r.Sleutel, out var oud) || r.Aantal > oud);
        vorige = rijen.ToDictionary(r => r.Sleutel, r => r.Aantal);

        var nieuw = Cultuur.Name + "\n" + string.Join("\n", rijen.Select(r => $"{r.Sleutel}|{r.Aantal}|{r.Naam}"));   // taalwissel = opnieuw opbouwen
        if (nieuw != handtekening)
        {
            handtekening = nieuw;
            Bouw(rijen);
        }

        if (rijen.Count == 0)
        {
            Hide();
            return;
        }
        if (!Visible)
        {
            Herplaats();
            Show();
        }
        HouBovenop();
        if (meer) Knipper();
    }

    void Knipper()
    {
        knipperTikken = Math.Max(1, KnipperSeconden) * 2;
        knipper.Start();
        if (Geluid) SystemSounds.Exclamation.Play();
    }

    void Bouw(IReadOnlyList<ChatRij> rijen)
    {
        SuspendLayout();
        foreach (var c in binnen.Controls.Cast<Control>().ToList()) c.Dispose();

        int breedte = S(380);
        binnen.Controls.Add(Tekst(F(T.ChatKop, rijen.Sum(r => r.Aantal)), 11, breedte));
        foreach (var r in rijen.OrderByDescending(r => r.Nieuwste).Take(MaxRijen))
            binnen.Controls.Add(Rij(r));
        if (rijen.Count > MaxRijen)
            binnen.Controls.Add(Tekst(F(T.AndereChats, rijen.Count - MaxRijen), 9, breedte));
        binnen.Controls.Add(Knop(T.AllesGezien, () => Gezien?.Invoke(rijen)));

        ResumeLayout();
        PerformLayout();
    }

    Control Rij(ChatRij r)
    {
        var rij = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Kleur.Parse(r.Job.Color),
            Margin = new Padding(0, S(3), 0, S(3)),
            Padding = new Padding(S(8), S(4), S(4), S(4)),
        };

        var tekst = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        tekst.Controls.Add(Tekst(r.Job.Name.ToUpperInvariant(), 8, S(250)));
        var naam = Tekst(r.Naam, 12, S(250));
        naam.AutoSize = false;
        naam.AutoEllipsis = true;
        naam.Size = new Size(S(250), S(26));
        if (r.WebUrl is { } url)
        {
            naam.Cursor = Cursors.Hand;
            naam.Font = new Font(naam.Font, FontStyle.Bold | FontStyle.Underline);
            naam.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });   // openen = niet geackt
        }
        tekst.Controls.Add(naam);

        var aantal = Tekst(r.Aantal >= ChatTeller.MaxBerichten ? $"{ChatTeller.MaxBerichten}+" : r.Aantal.ToString(), 18, S(60));
        aantal.TextAlign = ContentAlignment.MiddleRight;

        var ok = Knop("✓", () => Gezien?.Invoke([r]));
        tip.SetToolTip(ok, T.GezienTip);

        rij.Controls.Add(tekst, 0, 0);
        rij.Controls.Add(aantal, 1, 0);
        rij.Controls.Add(ok, 2, 0);
        return rij;
    }

    public void Herplaats() => Location = Plaatsing.Bereken(positie, Plaatsing.Werkgebied(scherm), [Size])[0];

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Herplaats();   // groeit/krimpt met het aantal rijen; blijft aan zijn anker hangen
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 e.d.: niet weggooien; alleen "gezien" laat hem verdwijnen.
        if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
