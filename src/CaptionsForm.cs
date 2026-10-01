using System.Runtime.InteropServices;

namespace SpeechToText;

/// <summary>Ventana de subtítulos en directo: dimensionable, letra grande y desplazamiento automático.</summary>
sealed class CaptionsForm : Form
{
    readonly Settings s;
    readonly RichTextBox box = new()
    {
        ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
        ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = false, DetectUrls = false
    };
    readonly Label hint = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };

    public CaptionsForm(Settings settings)
    {
        s = settings;
        Text = "Subtítulos en directo";
        Icon = SystemIcons.Information;
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen!.WorkingArea;
        Size = s.CaptionW > 200 && s.CaptionH > 120 ? new Size(s.CaptionW, s.CaptionH) : new Size(Math.Min(1000, area.Width), 420);
        var pos = new Point(s.CaptionX, s.CaptionY);
        Location = s.CaptionW > 0 && Screen.AllScreens.Any(sc => sc.WorkingArea.Contains(pos))
            ? pos : new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 20);
        TopMost = s.CaptionTopMost;
        KeyPreview = true;

        Controls.Add(box);
        Controls.Add(hint);
        ApplyStyle();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Letra más grande (Ctrl +)", null, (_, _) => Zoom(+4));
        menu.Items.Add("Letra más pequeña (Ctrl -)", null, (_, _) => Zoom(-4));
        var dark = new ToolStripMenuItem("Fondo oscuro") { Checked = s.CaptionDark, CheckOnClick = true };
        dark.Click += (_, _) => { s.CaptionDark = dark.Checked; ApplyStyle(); };
        var top = new ToolStripMenuItem("Siempre visible") { Checked = s.CaptionTopMost, CheckOnClick = true };
        top.Click += (_, _) => { s.CaptionTopMost = top.Checked; TopMost = top.Checked; };
        menu.Items.Add(dark); menu.Items.Add(top);
        menu.Items.Add("Limpiar", null, (_, _) => box.Clear());
        box.ContextMenuStrip = menu; ContextMenuStrip = menu;

        box.MouseWheel += (_, e) => { if ((ModifierKeys & Keys.Control) != 0) Zoom(e.Delta > 0 ? 4 : -4); };
        KeyDown += (_, e) =>
        {
            if (!e.Control) return;
            if (e.KeyCode is Keys.Oemplus or Keys.Add) { Zoom(+4); e.Handled = true; }
            else if (e.KeyCode is Keys.OemMinus or Keys.Subtract) { Zoom(-4); e.Handled = true; }
        };
        FormClosing += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal)
            { s.CaptionX = Left; s.CaptionY = Top; s.CaptionW = Width; s.CaptionH = Height; }
            try { s.Save(); } catch { }
        };
    }

    void Zoom(int delta)
    {
        s.CaptionFontSize = Math.Clamp(s.CaptionFontSize + delta, 14, 120);
        ApplyStyle();
        ScrollToEnd();
    }

    void ApplyStyle()
    {
        var bg = s.CaptionDark ? Color.Black : Color.White;
        var fg = s.CaptionDark ? Color.White : Color.Black;
        box.BackColor = hint.BackColor = bg;
        box.ForeColor = fg;
        hint.ForeColor = s.CaptionDark ? Color.FromArgb(120, 220, 120) : Color.FromArgb(20, 120, 20);
        box.Font = new Font("Segoe UI", s.CaptionFontSize, FontStyle.Regular, GraphicsUnit.Point);
        hint.Font = new Font("Segoe UI", 11f);
    }

    /// <summary>Añade una frase; solo desplaza al final si el lector no se ha ido hacia arriba a releer.</summary>
    public void AddLine(string text)
    {
        bool follow = AtBottom();
        if (box.TextLength > 60000)
        {
            box.Select(0, box.TextLength / 2);
            box.SelectedText = "";
        }
        box.AppendText(text + "\n\n");
        if (follow) ScrollToEnd();
    }

    public void SetListening(bool on) => hint.Text = on ? "● hablando…" : "";

    void ScrollToEnd()
    {
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
    }

    bool AtBottom()
    {
        var si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = 0x17 };
        return !GetScrollInfo(box.Handle, 1, ref si) || si.nPos + si.nPage >= si.nMax - 2;
    }

    [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr hWnd, int bar, ref SCROLLINFO si);

    [StructLayout(LayoutKind.Sequential)]
    struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }
}
