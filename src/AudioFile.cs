using NAudio.Wave;

namespace SpeechToText;

/// <summary>Decodifica un fichero de audio/vídeo a PCM 16 kHz mono y lo trocea por silencios.</summary>
static class AudioFile
{
    public const int Rate = 16000;
    const int FrameBytes = Rate * 2 * 30 / 1000;   // 30 ms
    const int FramesPerSecond = 1000 / 30;

    public sealed record Chunk(double StartSeconds, byte[] Wav);

    /// <summary>Usa Media Foundation de Windows (wav, mp3, m4a/aac, wma, flac, mp4…).</summary>
    public static byte[] Decode(string path)
    {
        using var reader = new MediaFoundationReader(path);
        using var resampler = new MediaFoundationResampler(reader, new WaveFormat(Rate, 16, 1)) { ResamplerQuality = 60 };
        using var ms = new MemoryStream();
        var buf = new byte[Rate * 2];
        int n;
        while ((n = resampler.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
        return ms.ToArray();
    }

    /// <summary>
    /// Trozos de como máximo <paramref name="maxSeconds"/>, cortando en el punto más silencioso de los
    /// últimos segundos para no partir palabras. Los trozos sin voz se descartan.
    /// </summary>
    public static List<Chunk> Split(byte[] pcm, double thresholdDb, int maxSeconds = 28)
    {
        int frames = pcm.Length / FrameBytes;
        var db = new double[frames];
        for (int i = 0; i < frames; i++) db[i] = Db(pcm, i * FrameBytes, FrameBytes);

        var chunks = new List<Chunk>();
        int maxFrames = maxSeconds * FramesPerSecond, lookback = 6 * FramesPerSecond, pos = 0;
        while (pos < frames)
        {
            int end = Math.Min(pos + maxFrames, frames);
            if (end < frames)
            {
                int from = Math.Max(pos + FramesPerSecond, end - lookback), best = end - 1;
                for (int i = from; i < end; i++) if (db[i] < db[best]) best = i;
                end = best + 1;
            }
            int voiced = 0;
            for (int i = pos; i < end; i++) if (db[i] > thresholdDb) voiced++;
            if (voiced * 30 >= 250)
                chunks.Add(new Chunk(pos * 0.03, ToWav(pcm, pos * FrameBytes, (end - pos) * FrameBytes)));
            pos = end;
        }
        return chunks;
    }

    static double Db(byte[] pcm, int offset, int count)
    {
        double sum = 0;
        int n = count / 2;
        for (int i = 0; i < n; i++)
        {
            double v = BitConverter.ToInt16(pcm, offset + i * 2) / 32768.0;
            sum += v * v;
        }
        return 20 * Math.Log10(Math.Sqrt(sum / n) + 1e-9);
    }

    static byte[] ToWav(byte[] pcm, int offset, int len)
    {
        using var ms = new MemoryStream(44 + len);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + len); w.Write("WAVEfmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(len);
        w.Write(pcm, offset, len);
        w.Flush();
        return ms.ToArray();
    }
}
