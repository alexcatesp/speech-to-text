namespace SpeechToText;

sealed class SettingsForm : Form
{
    readonly Settings s;
    readonly Dictation dictation;

    readonly TextBox url = new(), model = new(), file = new(), hotkey = new();
    readonly ComboBox lang = new() { DropDownStyle = ComboBoxStyle.DropDown };
    readonly ComboBox mic = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox toFocus = new() { Text = "Escribir en la ventana con foco (donde está el cursor)", AutoSize = true };
    readonly CheckBox toFile = new() { Text = "Guardar en un fichero de texto", AutoSize = true };
    readonly Label preview = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(440, 0) };
    readonly CheckBox stamp = new() { Text = "Añadir fecha y hora a cada frase", AutoSize = true };
    readonly CheckBox autostart = new() { Text = "Iniciar con Windows", AutoSize = true };
    readonly NumericUpDown threshold = new() { Minimum = -70, Maximum = -10, DecimalPlaces = 0 };
    readonly NumericUpDown silence = new() { Minimum = 300, Maximum = 3000, Increment = 100 };
    readonly Button browse = new() { Text = "…", Width = 32 };
    readonly Button test = new() { Text = "Probar servidor", AutoSize = true };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(440, 0) };

    public SettingsForm(Settings settings, Dictation dictation)
    {
        s = settings; this.dictation = dictation;
        Text = "Dictado por voz · Configuración";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(12);

        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Row(string label, Control c, Control? extra = null)
        {
            int r = t.RowCount++;
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) }, 0, r);
            c.Dock = DockStyle.Fill; c.Margin = new Padding(3, 5, 3, 3);
            t.Controls.Add(c, 1, r);
            if (extra != null) t.Controls.Add(extra, 2, r);
        }
        void Wide(Control c)
        {
            int r = t.RowCount++;
            t.Controls.Add(c, 0, r); t.SetColumnSpan(c, 3); c.Margin = new Padding(3, 6, 3, 3);
        }

        Row("Servidor Whisper", url, test);
        Row("Modelo (vacío = por defecto)", model);
        lang.Items.AddRange(new object[] { "es", "en", "ca", "fr", "de", "it", "pt", "" });
        Row("Idioma (vacío = autodetectar)", lang);
        mic.Items.Add("(Predeterminado de Windows)");
        foreach (var m in Dictation.Microphones()) mic.Items.Add(m);
        Row("Micrófono", mic);
        hotkey.ReadOnly = true;
        hotkey.KeyDown += CaptureHotkey;
        Row("Atajo iniciar/parar", hotkey);
        Wide(toFocus); Wide(toFile);
        Row("Fichero (plantilla)", file, browse);
        Wide(preview);
        Wide(stamp);
        Row("Umbral de voz (dBFS)", threshold);
        Row("Silencio que corta frase (ms)", silence);
        Wide(autostart);
        Wide(status);

        var ok = new Button { Text = "Guardar", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        Wide(buttons);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(t);

        browse.Click += (_, _) =>
        {
            using var d = new SaveFileDialog { Filter = "Texto (*.txt)|*.txt|Todos|*.*", FileName = file.Text, OverwritePrompt = false };
            if (d.ShowDialog(this) == DialogResult.OK) file.Text = d.FileName;
        };
        test.Click += async (_, _) =>
        {
            status.Text = "Conectando…";
            try { status.Text = "Conectado. Modelos: " + await dictation.CheckServerAsync(url.Text); }
            catch (Exception ex) { status.Text = "Error: " + ex.Message; }
        };
        toFile.CheckedChanged += (_, _) => UpdateEnabled();
        file.TextChanged += (_, _) => UpdatePreview();
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && !Apply()) e.Cancel = true; };

        Load_();
    }

    void Load_()
    {
        url.Text = s.ServerUrl; model.Text = s.ModelName; lang.Text = s.Language;
        int i = mic.Items.IndexOf(s.MicrophoneName);
        mic.SelectedIndex = i >= 0 ? i : 0;
        hotkey.Text = s.Hotkey;
        toFocus.Checked = s.OutputToWindow; toFile.Checked = s.OutputToFile;
        file.Text = s.FilePath; stamp.Checked = s.TimestampInFile;
        threshold.Value = (decimal)Math.Clamp(s.ThresholdDb, -70, -10);
        silence.Value = Math.Clamp(s.SilenceMs, 300, 3000);
        autostart.Checked = s.StartWithWindows;
        UpdateEnabled();
    }

    void UpdateEnabled()
    {
        file.Enabled = browse.Enabled = stamp.Enabled = toFile.Checked;
        UpdatePreview();
    }

    void UpdatePreview() => preview.Text = toFile.Checked
        ? "Marcadores de fecha entre llaves, p. ej. {yyyy-MM-dd} {HH-mm}. Ahora: " + Settings.ResolvePath(file.Text, DateTime.Now)
        : "";

    void CaptureHotkey(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true; e.Handled = true;
        var k = e.KeyCode;
        if (k is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin) return;
        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Alt) parts.Add("Alt");
        if (e.Shift) parts.Add("Shift");
        parts.Add(k.ToString());
        var text = string.Join("+", parts);
        if (HotkeyWindow.TryParse(text, out _, out _)) hotkey.Text = text;
        else status.Text = "El atajo necesita Ctrl, Alt o Shift (o una tecla F).";
    }

    bool Apply()
    {
        if (!toFocus.Checked && !toFile.Checked)
        {
            MessageBox.Show(this, "Elige al menos un destino.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (toFile.Checked && string.IsNullOrWhiteSpace(file.Text))
        {
            MessageBox.Show(this, "Indica un fichero de salida.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        s.ServerUrl = url.Text.Trim(); s.ModelName = model.Text.Trim(); s.Language = lang.Text.Trim();
        s.MicrophoneName = mic.SelectedIndex > 0 ? (string)mic.SelectedItem! : "";
        s.Hotkey = hotkey.Text;
        s.OutputToWindow = toFocus.Checked; s.OutputToFile = toFile.Checked;
        s.FilePath = file.Text.Trim(); s.TimestampInFile = stamp.Checked;
        s.ThresholdDb = (double)threshold.Value; s.SilenceMs = (int)silence.Value;
        s.StartWithWindows = autostart.Checked;
        return true;
    }
}
