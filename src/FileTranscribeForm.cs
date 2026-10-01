using System.Text;

namespace SpeechToText;

/// <summary>Transcribe un fichero de audio (o vídeo) por trozos, con progreso, y permite copiar o guardar el texto.</summary>
sealed class FileTranscribeForm : Form
{
    readonly Settings s;
    readonly Dictation engine;
    readonly TextBox path = new() { Dock = DockStyle.Fill };
    readonly Button browse = new() { Text = "Examinar…", AutoSize = true };
    readonly Button go = new() { Text = "Transcribir", AutoSize = true };
    readonly CheckBox stamps = new() { Text = "Marcas de tiempo", AutoSize = true, Checked = true };
    readonly CheckBox llm = new() { Text = "Depurar con LLM", AutoSize = true };
    readonly ProgressBar bar = new() { Dock = DockStyle.Fill };
    readonly Label status = new() { AutoSize = true, Text = "Elige un fichero o arrástralo a esta ventana." };
    readonly TextBox result = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f) };
    readonly Button copy = new() { Text = "Copiar", AutoSize = true };
    readonly Button save = new() { Text = "Guardar .txt…", AutoSize = true };
    CancellationTokenSource? cts;

    public FileTranscribeForm(Settings settings, Dictation engine)
    {
        s = settings; this.engine = engine;
        Text = "Transcribir fichero de audio";
        Size = new Size(760, 600); MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        AllowDrop = true;
        llm.Checked = s.UseLlm;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(path, 0, 0); top.Controls.Add(browse, 1, 0); top.Controls.Add(go, 2, 0);
        var opts = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        opts.Controls.Add(stamps); opts.Controls.Add(llm);
        top.Controls.Add(opts, 0, 1); top.SetColumnSpan(opts, 3);
        top.Controls.Add(bar, 0, 2); top.SetColumnSpan(bar, 3);
        top.Controls.Add(status, 0, 3); top.SetColumnSpan(status, 3);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bottom.Controls.Add(save); bottom.Controls.Add(copy);

        Controls.Add(result); Controls.Add(bottom); Controls.Add(top);

        browse.Click += (_, _) =>
        {
            using var d = new OpenFileDialog
            {
                Filter = "Audio y vídeo|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.mp4;*.mkv;*.mov;*.wmv;*.avi|Todos|*.*"
            };
            if (d.ShowDialog(this) == DialogResult.OK) path.Text = d.FileName;
        };
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) { path.Text = f[0]; if (!busy) _ = RunAsync(); }
        };
        go.Click += async (_, _) => { if (busy) cts?.Cancel(); else await RunAsync(); };
        copy.Click += (_, _) => { if (result.TextLength > 0) Clipboard.SetText(result.Text); };
        save.Click += (_, _) =>
        {
            using var d = new SaveFileDialog
            {
                Filter = "Texto (*.txt)|*.txt",
                FileName = Path.GetFileNameWithoutExtension(path.Text) + ".txt"
            };
            if (d.ShowDialog(this) == DialogResult.OK) File.WriteAllText(d.FileName, result.Text, new UTF8Encoding(true));
        };
        FormClosing += (_, _) => cts?.Cancel();
    }

    bool busy;

    async Task RunAsync()
    {
        if (!File.Exists(path.Text)) { status.Text = "El fichero no existe."; return; }
        busy = true; go.Text = "Cancelar"; result.Clear(); bar.Value = 0;
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        try
        {
            status.Text = "Leyendo y decodificando el audio…";
            var chunks = await Task.Run(() =>
            {
                var pcm = AudioFile.Decode(path.Text);
                return AudioFile.Split(pcm, s.ThresholdDb - 8);
            }, ct);
            if (chunks.Count == 0) { status.Text = "No se ha detectado voz en el fichero."; return; }

            bar.Maximum = chunks.Count;
            bool refine = llm.Checked;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                status.Text = $"Transcribiendo trozo {i + 1} de {chunks.Count}…";
                var text = await engine.Transcriber.TranscribeAsync(s, chunks[i].Wav, ct);
                if (text.Length == 0) { bar.Value = i + 1; continue; }
                if (refine)
                {
                    try { text = await engine.Llm.CleanAsync(s, text, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { Settings.Log("LLM (fichero): " + ex.Message); refine = false; status.Text = "LLM no disponible; se continúa sin depurar."; }
                }
                var line = stamps.Checked ? $"[{TimeSpan.FromSeconds(chunks[i].StartSeconds):hh\\:mm\\:ss}] {text}" : text;
                result.AppendText(line + Environment.NewLine);
                bar.Value = i + 1;
            }
            status.Text = $"Terminado: {chunks.Count} trozos en {sw.Elapsed.TotalSeconds:0.0} s.";
        }
        catch (OperationCanceledException) { status.Text = "Cancelado."; }
        catch (Exception ex)
        {
            Settings.Log("Fichero: " + ex);
            status.Text = "Error: " + ex.Message + (ex is System.Runtime.InteropServices.COMException
                ? " (formato no compatible; conviértelo a mp3 o wav)" : "");
        }
        finally { busy = false; go.Text = "Transcribir"; }
    }
}
