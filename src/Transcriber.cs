using System.Net.Http.Headers;
using System.Text.Json;

namespace SpeechToText;

/// <summary>Cliente del endpoint local /v1/audio/transcriptions (faster-whisper).</summary>
sealed class Transcriber : IDisposable
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(90) };

    // Frases típicas que Whisper alucina con audio casi silencioso.
    static readonly string[] Hallucinations =
    {
        "amara.org", "subtítulos realizados", "subtitulos realizados", "gracias por ver",
        "suscríbete", "thanks for watching", "subtitles by"
    };

    public async Task<string> TranscribeAsync(Settings s, byte[] wav, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        form.Add(new StringContent(s.ModelName), "model_name");
        if (!string.IsNullOrWhiteSpace(s.Language)) form.Add(new StringContent(s.Language), "language");
        form.Add(new StringContent("json"), "response_format");

        using var resp = await http.PostAsync(s.ServerUrl.TrimEnd('/') + "/v1/audio/transcriptions", form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        text = text.Trim();
        return Hallucinations.Any(h => text.Contains(h, StringComparison.OrdinalIgnoreCase)) ? "" : text;
    }

    public async Task<string> CheckAsync(string url)
    {
        using var resp = await http.GetAsync(url.TrimEnd('/') + "/v1/models");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var ids = doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()).Where(i => i != null);
        return string.Join(", ", ids);
    }

    public void Dispose() => http.Dispose();
}
