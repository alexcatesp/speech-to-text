using System.Threading.Channels;
using NAudio.Wave;

namespace SpeechToText;

/// <summary>
/// Captura el micrófono, segmenta la voz por silencios (VAD por energía) y transcribe cada frase.
/// Etapa 1: Whisper + subtítulos (mínima latencia). Etapa 2 (opcional): depuración con LLM y
/// salida a ventana con foco / fichero, para que el LLM nunca retrase los subtítulos.
/// </summary>
sealed class Dictation : IDisposable
{
    const int Rate = 16000;
    const int PreRollMs = 300;
    // Con subtítulos activos se acortan los fragmentos para que el texto aparezca antes.
    const int CaptionMaxSeconds = 8;

    public Transcriber Transcriber { get; } = new();
    public LlmCleaner Llm { get; } = new();
    readonly WaveFormat format = new(Rate, 16, 1);
    Settings settings;
    WaveInEvent? mic;
    Channel<byte[]>? queue;
    Channel<string>? textQueue;
    Task? worker, worker2;

    readonly Queue<byte[]> preRoll = new();
    int preRollMs;
    readonly List<byte[]> segment = new();
    readonly List<double> segDb = new();
    int segmentMs, silenceMs, voicedMs;
    bool speaking;
    DateTime llmDownUntil = DateTime.MinValue;

    public bool IsRunning => mic != null;
    /// <summary>Fichero de la sesión actual (o de la última), calculado al iniciar el dictado.</summary>
    public string? SessionFile { get; private set; }
    public event Action<string>? Error;
    public event Action<bool>? Speaking;
    /// <summary>Texto transcrito en bruto (sin LLM), para la ventana de subtítulos.</summary>
    public event Action<string>? Caption;

    public Dictation(Settings s) => settings = s;

    public void UpdateSettings(Settings s) => settings = s;

    public Task<string> CheckServerAsync(string url) => Transcriber.CheckAsync(url);

    public static List<string> Microphones()
    {
        var list = new List<string>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++) list.Add(WaveInEvent.GetCapabilities(i).ProductName);
        return list;
    }

    int SilenceLimit => settings.OutputToCaptions ? settings.CaptionSilenceMs : settings.SilenceMs;
    int MinVoicedMs => settings.OutputToCaptions ? 150 : 250;
    int MaxMs => (settings.OutputToCaptions ? Math.Min(settings.MaxSegmentSeconds, CaptionMaxSeconds) : settings.MaxSegmentSeconds) * 1000;

    public void Start()
    {
        if (IsRunning) return;
        int device = -1; // micrófono predeterminado de Windows
        if (!string.IsNullOrEmpty(settings.MicrophoneName))
        {
            int idx = Microphones().IndexOf(settings.MicrophoneName);
            if (idx >= 0) device = idx;
        }
        ResetSegmenter();
        SessionFile = Settings.ResolvePath(settings.FilePath, DateTime.Now);
        queue = Channel.CreateUnbounded<byte[]>();
        textQueue = Channel.CreateUnbounded<string>();
        var reader = queue.Reader;
        var reader2 = textQueue.Reader;
        worker = Task.Run(() => TranscribeLoop(reader));
        worker2 = Task.Run(() => OutputLoop(reader2));
        llmDownUntil = DateTime.MinValue;
        if (settings.LlmEnabled && (settings.OutputToWindow || settings.OutputToFile))
            _ = Llm.WarmUpAsync(settings);
        var m = new WaveInEvent { DeviceNumber = device, WaveFormat = format, BufferMilliseconds = 30 };
        m.DataAvailable += OnData;
        m.RecordingStopped += (_, e) => { if (e.Exception != null) Error?.Invoke("Micrófono: " + e.Exception.Message); };
        m.StartRecording();
        mic = m;
    }

    /// <param name="releaseLlm">Descarga el modelo LLM de la GPU si está configurado (false al reiniciar por un cambio de ajustes).</param>
    public async Task StopAsync(bool releaseLlm = true)
    {
        var m = mic;
        if (m == null) return;
        mic = null;
        m.DataAvailable -= OnData;
        m.StopRecording();
        m.Dispose();
        FlushSegment();
        queue?.Writer.Complete();
        // ConfigureAwait(false): los llamantes pueden bloquear el hilo de UI esperando esta tarea.
        if (worker != null) await worker.ConfigureAwait(false);
        textQueue?.Writer.Complete();
        if (worker2 != null) await worker2.ConfigureAwait(false);
        if (releaseLlm && settings.LlmUnloadOnStop) await Llm.UnloadAsync().ConfigureAwait(false);
        Speaking?.Invoke(false);
    }

    void ResetSegmenter()
    {
        preRoll.Clear(); preRollMs = 0; segment.Clear(); segDb.Clear();
        segmentMs = silenceMs = voicedMs = 0; speaking = false;
    }

    static int Ms(byte[] b) => b.Length * 1000 / (Rate * 2);

    void OnData(object? sender, WaveInEventArgs e)
    {
        var buf = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, buf, 0, e.BytesRecorded);
        int ms = Ms(buf);
        bool voiced = Db(buf) > settings.ThresholdDb;

        if (!speaking)
        {
            preRoll.Enqueue(buf); preRollMs += ms;
            while (preRollMs > PreRollMs && preRoll.Count > 1) preRollMs -= Ms(preRoll.Dequeue());
            if (!voiced) return;
            speaking = true;
            segment.AddRange(preRoll);
            segDb.AddRange(preRoll.Select(Db));
            segmentMs = preRollMs; voicedMs = ms; silenceMs = 0;
            preRoll.Clear(); preRollMs = 0;
            Speaking?.Invoke(true);
            return;
        }

        segment.Add(buf); segDb.Add(Db(buf)); segmentMs += ms;
        if (voiced) { silenceMs = 0; voicedMs += ms; } else silenceMs += ms;
        if (silenceMs >= SilenceLimit) FlushSegment();
        else if (segmentMs >= MaxMs) SplitAtQuietPoint();
    }

    /// <summary>
    /// Al llegar al máximo de duración corta en el punto más silencioso de los últimos 2 s
    /// (para no partir palabras) y el resto sigue como inicio del siguiente fragmento.
    /// </summary>
    void SplitAtQuietPoint()
    {
        int n = segment.Count, from = n - 1, acc = 0;
        while (from > 0 && acc < 2000) { acc += Ms(segment[from]); from--; }
        from = Math.Max(from, 1);
        int best = from;
        for (int i = from; i < n - 1; i++) if (segDb[i] < segDb[best]) best = i;
        if (best >= n - 1) { FlushSegment(); return; }

        var head = segment.GetRange(0, best + 1);
        var tail = segment.GetRange(best + 1, n - best - 1);
        var tailDb = segDb.GetRange(best + 1, n - best - 1);
        queue?.Writer.TryWrite(ToWav(head));
        segment.Clear(); segDb.Clear();
        segment.AddRange(tail); segDb.AddRange(tailDb);
        segmentMs = tail.Sum(Ms);
        voicedMs = 0; silenceMs = 0;
        for (int i = 0; i < tail.Count; i++)
        {
            if (tailDb[i] > settings.ThresholdDb) { voicedMs += Ms(tail[i]); silenceMs = 0; }
            else silenceMs += Ms(tail[i]);
        }
    }

    void FlushSegment()
    {
        if (segment.Count > 0 && voicedMs >= MinVoicedMs)
            queue?.Writer.TryWrite(ToWav(segment));
        segment.Clear(); segDb.Clear();
        segmentMs = silenceMs = voicedMs = 0;
        if (speaking) { speaking = false; Speaking?.Invoke(false); }
    }

    static double Db(byte[] pcm)
    {
        int n = pcm.Length / 2;
        if (n == 0) return -120;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double v = BitConverter.ToInt16(pcm, i * 2) / 32768.0;
            sum += v * v;
        }
        return 20 * Math.Log10(Math.Sqrt(sum / n) + 1e-9);
    }

    static byte[] ToWav(List<byte[]> chunks)
    {
        int len = chunks.Sum(c => c.Length);
        using var ms = new MemoryStream(44 + len);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + len); w.Write("WAVEfmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(len);
        foreach (var c in chunks) w.Write(c);
        w.Flush();
        return ms.ToArray();
    }

    // Etapa 1: Whisper → subtítulos → cola de la etapa 2.
    async Task TranscribeLoop(ChannelReader<byte[]> reader)
    {
        await foreach (var wav in reader.ReadAllAsync())
        {
            try
            {
                var s = settings;
                var text = TrailingEllipsis.Replace(await Transcriber.TranscribeAsync(s, wav), "").Trim();
                if (text.Length == 0) continue;
                if (s.OutputToCaptions) Caption?.Invoke(text);
                if (s.OutputToWindow || s.OutputToFile) textQueue?.Writer.TryWrite(text);
            }
            catch (Exception ex)
            {
                Settings.Log("Transcripción: " + ex);
                Error?.Invoke("Transcripción: " + ex.Message);
            }
        }
    }

    // Whisper añade "..." al final de los fragmentos cortados a mitad de frase.
    static readonly System.Text.RegularExpressions.Regex TrailingEllipsis = new(@"(\.\.\.|…)\s*$");

    // Etapa 2: agrupa fragmentos en frases completas (si hay LLM o subtítulos, que generan fragmentos cortos),
    // depura con LLM si procede y escribe en ventana/fichero.
    async Task OutputLoop(ChannelReader<string> reader)
    {
        var pending = new List<string>();

        async Task FlushAsync()
        {
            if (pending.Count == 0) return;
            var text = string.Join(" ", pending);
            pending.Clear();
            await EmitAsync(settings, text);
        }

        while (true)
        {
            if (pending.Count == 0)
            {
                if (!await reader.WaitToReadAsync()) break;
            }
            else
            {
                using var cts = new CancellationTokenSource(settings.SentencePauseMs);
                try { if (!await reader.WaitToReadAsync(cts.Token)) break; }
                catch (OperationCanceledException) { await FlushAsync(); continue; }
            }
            while (reader.TryRead(out var raw))
            {
                var s = settings;
                if (s.LlmEnabled || s.OutputToCaptions) pending.Add(raw);
                else await EmitAsync(s, raw);
            }
            if (pending.Sum(p => p.Length) > 600) await FlushAsync();
        }
        await FlushAsync();
    }

    async Task EmitAsync(Settings s, string raw)
    {
        var text = raw;
        // Si el endpoint falla, se usa el texto sin depurar y no se reintenta durante 60 s
        // (evita esperas de 30 s por frase en un equipo donde Ollama no está levantado).
        if (s.LlmEnabled && !Llm.WarmingUp && DateTime.Now >= llmDownUntil)
        {
            try { text = await Llm.CleanAsync(s, raw); }
            catch (Exception ex)
            {
                llmDownUntil = DateTime.Now.AddSeconds(60);
                Settings.Log("LLM: " + ex.Message);
                Error?.Invoke("LLM no disponible, se usa el texto sin depurar: " + ex.Message);
            }
        }
        try { Output(s, SessionFile, text); }
        catch (Exception ex)
        {
            Settings.Log("Salida: " + ex);
            Error?.Invoke("Salida: " + ex.Message);
        }
    }

    static void Output(Settings s, string? file, string text)
    {
        if (s.OutputToWindow) TextInjector.Type(text + " ");
        if (s.OutputToFile && !string.IsNullOrWhiteSpace(file))
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var line = s.TimestampInFile ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}" : text;
            File.AppendAllText(file, line + Environment.NewLine);
        }
    }

    public void Dispose()
    {
        mic?.Dispose();
        Transcriber.Dispose();
        Llm.Dispose();
    }
}
