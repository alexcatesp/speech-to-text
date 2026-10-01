using System.Drawing.Drawing2D;

namespace SpeechToText;

/// <summary>Aplicación residente en la bandeja del sistema.</summary>
sealed class TrayApp : ApplicationContext
{
    readonly Settings settings = Settings.Load();
    readonly NotifyIcon tray = new() { Visible = true };
    readonly Dictation dictation;
    readonly HotkeyWindow hotkey = new();
    readonly Icon idle = MakeIcon(Color.FromArgb(90, 90, 100)), active = MakeIcon(Color.FromArgb(220, 40, 40)),
                  talking = MakeIcon(Color.FromArgb(40, 180, 70));
    readonly ToolStripMenuItem toggleItem = new();
    readonly SynchronizationContext ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
    DateTime lastError = DateTime.MinValue;
    bool settingsOpen;
    CaptionsForm? captions;
    FileTranscribeForm? fileForm;

    public TrayApp()
    {
        dictation = new Dictation(settings);
        dictation.Error += msg => ui.Post(_ => ShowError(msg), null);
        dictation.Speaking += on => ui.Post(_ =>
        {
            if (dictation.IsRunning) tray.Icon = on ? talking : active;
            if (captions is { IsDisposed: false }) captions.SetListening(on);
        }, null);
        dictation.Caption += text => ui.Post(_ => ShowCaptions().AddLine(text), null);

        var menu = new ContextMenuStrip();
        toggleItem.Click += async (_, _) => await ToggleAsync();
        menu.Items.Add(toggleItem);
        menu.Items.Add("Subtítulos en directo", null, (_, _) => ShowCaptions());
        menu.Items.Add("Transcribir fichero de audio…", null, (_, _) => OpenFileTranscriber());
        menu.Items.Add("Configuración…", null, (_, _) => OpenSettings());
        menu.Items.Add("Abrir fichero de salida", null, (_, _) => OpenOutputFile());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, async (_, _) => await ExitAsync());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += async (_, _) => await ToggleAsync();

        hotkey.Pressed += async () => await ToggleAsync();
        RegisterHotkey();
        Refresh();
    }

    void RegisterHotkey()
    {
        if (!hotkey.Register(settings.Hotkey))
            tray.ShowBalloonTip(4000, "Dictado por voz", $"No se pudo registrar el atajo {settings.Hotkey}; cámbialo en Configuración.", ToolTipIcon.Warning);
    }

    async Task ToggleAsync()
    {
        try
        {
            if (dictation.IsRunning) await dictation.StopAsync();
            else
            {
                dictation.Start();
                if (settings.OutputToCaptions) ShowCaptions();
            }
        }
        catch (Exception ex) { Settings.Log("Toggle: " + ex); ShowError(ex.Message); }
        Refresh();
    }

    void Refresh()
    {
        bool on = dictation.IsRunning;
        tray.Icon = on ? active : idle;
        toggleItem.Text = on ? "Parar dictado" : "Iniciar dictado";
        var dest = string.Join("+", new[]
        {
            settings.OutputToWindow ? "ventana" : null, settings.OutputToFile ? "fichero" : null,
            settings.OutputToCaptions ? "subtítulos" : null, settings.UseLlm ? "LLM" : null
        }.Where(x => x != null));
        var text = (on ? "Dictado ACTIVO" : "Dictado parado") + $" · {settings.Hotkey} · {dest}";
        tray.Text = text.Length > 63 ? text[..63] : text; // límite de NotifyIcon.Text
    }

    CaptionsForm ShowCaptions()
    {
        if (captions == null || captions.IsDisposed) captions = new CaptionsForm(settings);
        if (!captions.Visible) captions.Show();
        return captions;
    }

    void OpenFileTranscriber()
    {
        if (fileForm is { IsDisposed: false }) { fileForm.Activate(); return; }
        fileForm = new FileTranscribeForm(settings, dictation);
        fileForm.Show();
    }

    void ShowError(string msg)
    {
        if ((DateTime.Now - lastError).TotalSeconds < 10) return;
        lastError = DateTime.Now;
        tray.ShowBalloonTip(5000, "Dictado por voz", msg, ToolTipIcon.Error);
    }

    void OpenSettings()
    {
        if (settingsOpen) return;
        settingsOpen = true;
        try
        {
            using var f = new SettingsForm(settings, dictation);
            if (f.ShowDialog() == DialogResult.OK)
            {
                settings.Save();
                dictation.UpdateSettings(settings);
                RegisterHotkey();
                if (dictation.IsRunning) { dictation.StopAsync().GetAwaiter().GetResult(); dictation.Start(); }
                Refresh();
            }
        }
        finally { settingsOpen = false; }
    }

    void OpenOutputFile()
    {
        var path = dictation.SessionFile ?? Settings.ResolvePath(settings.FilePath, DateTime.Now);
        if (File.Exists(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        else
            tray.ShowBalloonTip(3000, "Dictado por voz", "El fichero de salida todavía no existe.", ToolTipIcon.Info);
    }

    async Task ExitAsync()
    {
        await dictation.StopAsync();
        tray.Visible = false;
        hotkey.Dispose();
        dictation.Dispose();
        ExitThread();
    }

    static Icon MakeIcon(Color c)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(c);
            using var pen = new Pen(c, 3f);
            g.FillEllipse(b, 1, 1, 30, 30);
            using var w = new SolidBrush(Color.White);
            using var wp = new Pen(Color.White, 2.5f);
            g.FillRectangle(w, 12, 6, 8, 13);
            g.FillEllipse(w, 12, 3, 8, 8);
            g.FillEllipse(w, 12, 14, 8, 8);
            g.DrawArc(wp, 8, 10, 16, 14, 0, 180);
            g.DrawLine(wp, 16, 24, 16, 28);
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        NativeMethods.DestroyIcon(h);
        return icon;
    }
}
