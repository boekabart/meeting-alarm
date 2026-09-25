using System.Runtime.InteropServices;

/// <summary>Randloos, altijd bovenop, steelt geen focus, niet in Alt-Tab; schaalt met de DPI.</summary>
abstract class PopupBasis : Form
{
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    protected static readonly Color KnipperKleur = Color.FromArgb(255, 111, 0);
    protected static readonly Color RandKleur = Color.FromArgb(255, 214, 0);

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

    protected PopupBasis()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = RandKleur;   // gele rand
        Padding = new Padding(S(5));
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    protected static Rectangle Werkgebied => Screen.PrimaryScreen!.WorkingArea;   // houdt rekening met de taakbalk

    protected int S(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    protected void HouBovenop()
    {
        if (Visible)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected static Label Tekst(string t, float pt, int breedte) => new()
    {
        Text = t,
        AutoSize = true,
        MinimumSize = new Size(breedte, 0),
        MaximumSize = new Size(breedte, 0),
        ForeColor = Color.White,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", pt, FontStyle.Bold),
        UseMnemonic = false,
    };

    protected static Button Knop(string t, Action actie)
    {
        var b = new Button { Text = t, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), UseVisualStyleBackColor = true };
        b.Click += (_, _) => actie();
        return b;
    }
}
