using System.Runtime.InteropServices;

/// <summary>Borderless, always on top, doesn't steal focus, not in Alt-Tab; scales with DPI.</summary>
abstract class PopupBase : Form
{
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    protected static readonly Color FlashColor = Color.FromArgb(255, 111, 0);
    protected static readonly Color BorderColor = Color.FromArgb(255, 214, 0);

    protected override bool ShowWithoutActivation => true;   // don't steal focus while typing

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x8;   // WS_EX_TOOLWINDOW (not in Alt-Tab) | WS_EX_TOPMOST
            return cp;
        }
    }

    protected PopupBase()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = BorderColor;   // yellow border
        Padding = new Padding(S(5));
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    /// <summary>Scale a 96-DPI pixel value to the current DPI.</summary>
    protected int S(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    protected void KeepOnTop()
    {
        if (Visible)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected static Label MakeLabel(string text, float pt, int width) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(width, 0),
        MaximumSize = new Size(width, 0),
        ForeColor = Color.White,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", pt, FontStyle.Bold),
        UseMnemonic = false,
    };

    protected static Button MakeButton(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), UseVisualStyleBackColor = true };
        b.Click += (_, _) => action();
        return b;
    }
}
