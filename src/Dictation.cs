using System.Threading.Channels;
using NAudio.Wave;

namespace SpeechToText;

/// <summary>
/// Captura el micrófono, segmenta la voz por silencios (VAD por energía) y transcribe
/// cada frase en orden enviando el resultado al destino configurado.
/// </summary>
sealed class Dictation : IDisposable
{
    const int Rate = 16000;
    const int PreRollMs = 300;
    const int MinVoicedMs = 250;

    readonly Transcriber transcriber = new();
    readonly WaveFormat format = new(Rate, 16, 1);
    Settings settings;
    WaveInEvent? mic;
    Channel<byte[]>? queue;
    Task? worker;

    readonly Queue<byte[]> preRoll = new();
    int preRollMs;
    readonly List<byte[]> segment = new();
    int segmentMs, silenceMs, voicedMs;
    bool speaking;

    public bool IsRunning => mic != null;
    /// <summary>Fichero de la sesión actual (o de la última), calculado al iniciar el dictado.</summary>
    public string? SessionFile { get; private set; }
    public event Action<string>? Error;
    public event Action<bool>? Speaking;

    public Dictation(Settings s) => settings = s;

    public void UpdateSettings(Settings s) => settings = s;

    public Task<string> CheckServerAsync(string url) => transcriber.CheckAsync(url);

    public static List<string> Microphones()
    {
        var list = new List<string>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++) list.Add(WaveInEvent.GetCapabilities(i).ProductName);
        return list;
    }

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
        var reader = queue.Reader;
        worker = Task.Run(() => ProcessQueue(reader));
        var m = new WaveInEvent { DeviceNumber = device, WaveFormat = format, BufferMilliseconds = 30 };
        m.DataAvailable += OnData;
        m.RecordingStopped += (_, e) => { if (e.Exception != null) Error?.Invoke("Micrófono: " + e.Exception.Message); };
        m.StartRecording();
        mic = m;
    }

    public async Task StopAsync()
    {
        var m = mic;
        if (m == null) return;
        mic = null;
        m.DataAvailable -= OnData;
        m.StopRecording();
        m.Dispose();
        FlushSegment();
        queue?.Writer.Complete();
        if (worker != null) await worker;
        Speaking?.Invoke(false);
    }

    void ResetSegmenter()
    {
        preRoll.Clear(); preRollMs = 0; segment.Clear();
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
            segmentMs = preRollMs; voicedMs = ms; silenceMs = 0;
            preRoll.Clear(); preRollMs = 0;
            Speaking?.Invoke(true);
            return;
        }

        segment.Add(buf); segmentMs += ms;
        if (voiced) { silenceMs = 0; voicedMs += ms; } else silenceMs += ms;
        if (silenceMs >= settings.SilenceMs || segmentMs >= settings.MaxSegmentSeconds * 1000) FlushSegment();
    }

    void FlushSegment()
    {
        if (segment.Count > 0 && voicedMs >= MinVoicedMs)
            queue?.Writer.TryWrite(ToWav(segment));
        segment.Clear();
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

    async Task ProcessQueue(ChannelReader<byte[]> reader)
    {
        await foreach (var wav in reader.ReadAllAsync())
        {
            try
            {
                var s = settings;
                var text = await transcriber.TranscribeAsync(s, wav);
                if (text.Length == 0) continue;
                Output(s, SessionFile, text);
            }
            catch (Exception ex)
            {
                Settings.Log("Transcripción: " + ex);
                Error?.Invoke("Transcripción: " + ex.Message);
            }
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
        transcriber.Dispose();
    }
}
