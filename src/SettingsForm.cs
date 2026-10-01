namespace SpeechToText;

sealed class SettingsForm : Form
{
    readonly Settings s;
    readonly Dictation dictation;

    // General
    readonly TextBox url = new(), model = new(), hotkey = new();
    readonly ComboBox lang = new() { DropDownStyle = ComboBoxStyle.DropDown };
    readonly ComboBox mic = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown threshold = new() { Minimum = -70, Maximum = -10, DecimalPlaces = 0 };
    readonly NumericUpDown silence = new() { Minimum = 300, Maximum = 3000, Increment = 100 };
    readonly CheckBox autostart = new() { Text = "Iniciar con Windows", AutoSize = true };
    readonly Button test = new() { Text = "Probar servidor", AutoSize = true };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(480, 0) };

    // Destinos
    readonly CheckBox toFocus = new() { Text = "Escribir en la ventana con foco (donde está el cursor)", AutoSize = true };
    readonly CheckBox toFile = new() { Text = "Guardar en un fichero de texto", AutoSize = true };
    readonly CheckBox toCaptions = new() { Text = "Mostrar subtítulos en directo en una ventana (accesibilidad)", AutoSize = true };
    readonly TextBox file = new();
    readonly Button browse = new() { Text = "…", Width = 32 };
    readonly CheckBox stamp = new() { Text = "Añadir fecha y hora a cada frase", AutoSize = true };
    readonly Label preview = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(480, 0) };
    readonly NumericUpDown captionSize = new() { Minimum = 14, Maximum = 120 };
    readonly NumericUpDown captionSilence = new() { Minimum = 150, Maximum = 1000, Increment = 50 };
    readonly NumericUpDown sentencePause = new() { Minimum = 400, Maximum = 3000, Increment = 100 };

    // LLM
    readonly CheckBox useLlm = new() { Text = "Depurar cada fragmento con un LLM local (Ollama)", AutoSize = true };
    readonly TextBox llmUrl = new(), llmModel = new(), llmPrompt = new() { Multiline = true, Height = 130, ScrollBars = ScrollBars.Vertical };
    readonly CheckBox llmUnload = new() { Text = "Descargar el modelo de la GPU al parar el dictado (ahorra VRAM)", AutoSize = true };
    readonly Button llmTest = new() { Text = "Probar LLM", AutoSize = true };
    readonly Button llmReset = new() { Text = "Restaurar prompt", AutoSize = true };
    readonly Label llmStatus = new() { AutoSize = true, MaximumSize = new Size(480, 0) };

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

        var tabs = new TabControl { Dock = DockStyle.Fill, Width = 600, Height = 440 };
        var general = Page(tabs, "General");
        var dest = Page(tabs, "Destinos");
        var llmPage = Page(tabs, "Depuración (LLM)");

        // --- General
        Row(general, "Servidor Whisper", url, test);
        Row(general, "Modelo (vacío = por defecto)", model);
        lang.Items.AddRange(new object[] { "es", "en", "ca", "fr", "de", "it", "pt", "" });
        Row(general, "Idioma (vacío = autodetectar)", lang);
        mic.Items.Add("(Predeterminado de Windows)");
        foreach (var m in Dictation.Microphones()) mic.Items.Add(m);
        Row(general, "Micrófono", mic);
        hotkey.ReadOnly = true;
        hotkey.KeyDown += CaptureHotkey;
        Row(general, "Atajo iniciar/parar", hotkey);
        Row(general, "Umbral de voz (dBFS)", threshold);
        Row(general, "Silencio que corta frase (ms)", silence);
        Wide(general, autostart);
        Wide(general, status);

        // --- Destinos
        Wide(dest, toFocus); Wide(dest, toFile);
        Row(dest, "Fichero (plantilla)", file, browse);
        Wide(dest, preview);
        Wide(dest, stamp);
        Wide(dest, toCaptions);
        Row(dest, "Tamaño de letra (subtítulos)", captionSize);
        Row(dest, "Silencio que corta fragmento (ms)", captionSilence);
        Wide(dest, new Label
        {
            AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText,
            Text = "Con subtítulos se usa este silencio (más corto = el texto aparece antes y casi palabra a palabra, " +
                   "pero Whisper tiene menos contexto) en lugar del de la pestaña General; los fragmentos se acotan a 8 s. " +
                   "Los subtítulos nunca pasan por el LLM. En la ventana: Ctrl + / Ctrl - o Ctrl + rueda para la letra; " +
                   "clic derecho para tema y más."
        });

        // --- LLM
        Wide(llmPage, useLlm);
        Row(llmPage, "URL de Ollama", llmUrl, llmTest);
        Row(llmPage, "Modelo", llmModel);
        Row(llmPage, "Pausa que cierra una frase (ms)", sentencePause);
        Wide(llmPage, llmUnload);
        Wide(llmPage, new Label { Text = "Prompt de depuración:", AutoSize = true });
        Wide(llmPage, llmPrompt);
        Wide(llmPage, llmReset);
        Wide(llmPage, llmStatus);
        Wide(llmPage, new Label
        {
            AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText,
            Text = "Con LLM o subtítulos los fragmentos cortos se agrupan en frases completas (se cierran tras la pausa indicada) " +
                   "antes de enviarlos al LLM, a la ventana con foco o al fichero. " +
                   "Añade ~0,6 s por frase (con el modelo ya cargado). Se aplica a ventana con foco, fichero y transcripción " +
                   "de ficheros de audio. Si Ollama no responde, se escribe el texto sin depurar. Al iniciar el dictado se " +
                   "precarga el modelo (la primera carga puede tardar más de un minuto). Con esta opción desactivada " +
                   "la app no contacta con Ollama ni carga ningún modelo en la GPU."
        });

        var ok = new Button { Text = "Guardar", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true };
        buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(tabs); Controls.Add(buttons);

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
        llmTest.Click += async (_, _) =>
        {
            llmStatus.Text = "Conectando…";
            try { llmStatus.Text = await dictation.Llm.CheckAsync(llmUrl.Text, llmModel.Text.Trim()); }
            catch (Exception ex) { llmStatus.Text = "Error: " + ex.Message; }
        };
        llmReset.Click += (_, _) => llmPrompt.Text = Settings.DefaultLlmPrompt;
        toFile.CheckedChanged += (_, _) => UpdateEnabled();
        toCaptions.CheckedChanged += (_, _) => UpdateEnabled();
        useLlm.CheckedChanged += (_, _) => UpdateEnabled();
        llmUrl.PlaceholderText = "Ej.: http://127.0.0.1:11434";
        llmModel.PlaceholderText = "Ej.: gemma4-aula";
        file.TextChanged += (_, _) => UpdatePreview();
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK && !Apply()) e.Cancel = true; };

        Load_();
    }

    static TableLayoutPanel Page(TabControl tabs, string title)
    {
        var page = new TabPage(title) { Padding = new Padding(8), AutoScroll = true };
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        page.Controls.Add(t);
        tabs.TabPages.Add(page);
        return t;
    }

    static void Row(TableLayoutPanel t, string label, Control c, Control? extra = null)
    {
        int r = t.RowCount++;
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) }, 0, r);
        c.Dock = DockStyle.Fill; c.Margin = new Padding(3, 5, 3, 3);
        t.Controls.Add(c, 1, r);
        if (extra != null) t.Controls.Add(extra, 2, r);
    }

    static void Wide(TableLayoutPanel t, Control c)
    {
        int r = t.RowCount++;
        t.Controls.Add(c, 0, r); t.SetColumnSpan(c, 3); c.Margin = new Padding(3, 6, 3, 3);
        if (c is TextBox) c.Dock = DockStyle.Fill;
    }

    void Load_()
    {
        url.Text = s.ServerUrl; model.Text = s.ModelName; lang.Text = s.Language;
        int i = mic.Items.IndexOf(s.MicrophoneName);
        mic.SelectedIndex = i >= 0 ? i : 0;
        hotkey.Text = s.Hotkey;
        threshold.Value = (decimal)Math.Clamp(s.ThresholdDb, -70, -10);
        silence.Value = Math.Clamp(s.SilenceMs, 300, 3000);
        autostart.Checked = s.StartWithWindows;
        toFocus.Checked = s.OutputToWindow; toFile.Checked = s.OutputToFile; toCaptions.Checked = s.OutputToCaptions;
        file.Text = s.FilePath; stamp.Checked = s.TimestampInFile;
        captionSize.Value = Math.Clamp(s.CaptionFontSize, 14, 120);
        captionSilence.Value = Math.Clamp(s.CaptionSilenceMs, 150, 1000);
        sentencePause.Value = Math.Clamp(s.SentencePauseMs, 400, 3000);
        llmUnload.Checked = s.LlmUnloadOnStop;
        useLlm.Checked = s.UseLlm; llmUrl.Text = s.LlmUrl; llmModel.Text = s.LlmModel; llmPrompt.Text = s.LlmPrompt;
        UpdateEnabled();
    }

    void UpdateEnabled()
    {
        file.Enabled = browse.Enabled = stamp.Enabled = toFile.Checked;
        captionSize.Enabled = captionSilence.Enabled = toCaptions.Checked;
        llmUrl.Enabled = llmModel.Enabled = llmPrompt.Enabled = llmTest.Enabled = llmReset.Enabled =
            llmUnload.Enabled = useLlm.Checked;
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
        if (!toFocus.Checked && !toFile.Checked && !toCaptions.Checked)
        {
            MessageBox.Show(this, "Elige al menos un destino.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (toFile.Checked && string.IsNullOrWhiteSpace(file.Text))
        {
            MessageBox.Show(this, "Indica un fichero de salida.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (useLlm.Checked && (string.IsNullOrWhiteSpace(llmUrl.Text) || string.IsNullOrWhiteSpace(llmModel.Text) || string.IsNullOrWhiteSpace(llmPrompt.Text)))
        {
            MessageBox.Show(this, "Para usar el LLM indica la URL de Ollama, el modelo y el prompt.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        s.ServerUrl = url.Text.Trim(); s.ModelName = model.Text.Trim(); s.Language = lang.Text.Trim();
        s.MicrophoneName = mic.SelectedIndex > 0 ? (string)mic.SelectedItem! : "";
        s.Hotkey = hotkey.Text;
        s.ThresholdDb = (double)threshold.Value; s.SilenceMs = (int)silence.Value;
        s.StartWithWindows = autostart.Checked;
        s.OutputToWindow = toFocus.Checked; s.OutputToFile = toFile.Checked; s.OutputToCaptions = toCaptions.Checked;
        s.FilePath = file.Text.Trim(); s.TimestampInFile = stamp.Checked;
        s.CaptionFontSize = (int)captionSize.Value;
        s.CaptionSilenceMs = (int)captionSilence.Value; s.SentencePauseMs = (int)sentencePause.Value;
        s.LlmUnloadOnStop = llmUnload.Checked;
        s.UseLlm = useLlm.Checked; s.LlmUrl = llmUrl.Text.Trim(); s.LlmModel = llmModel.Text.Trim(); s.LlmPrompt = llmPrompt.Text.Trim();
        return true;
    }
}
