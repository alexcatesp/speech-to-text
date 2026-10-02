using System.Net.Http.Json;
using System.Text.Json;

namespace SpeechToText;

/// <summary>Depura cada fragmento transcrito con un modelo local de Ollama (/api/chat).</summary>
sealed class LlmCleaner : IDisposable
{
    readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    // Último modelo que se ha cargado/usado, para poder descargarlo aunque la configuración haya cambiado.
    string? loadedUrl, loadedModel;

    void Remember(Settings s) { loadedUrl = s.LlmUrl; loadedModel = s.LlmModel; }

    /// <summary>Descarga de la GPU el modelo usado (keep_alive 0). No hace nada si nunca se ha usado el LLM.</summary>
    public async Task UnloadAsync()
    {
        var (url, model) = (loadedUrl, loadedModel);
        if (url == null || model == null) return;
        loadedUrl = loadedModel = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var resp = await http.PostAsJsonAsync(url.TrimEnd('/') + "/api/generate",
                new { model, keep_alive = 0 }, cts.Token);
        }
        catch (Exception ex) { Settings.Log("Descarga LLM: " + ex.Message); }
    }

    /// <summary>Devuelve el texto depurado. Lanza excepción si Ollama no responde; el llamante usa el texto original.</summary>
    public async Task<string> CleanAsync(Settings s, string text, CancellationToken ct = default)
    {
        Remember(s);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var body = new
        {
            model = s.LlmModel,
            stream = false,
            think = false,
            keep_alive = s.LlmKeepAlive,
            messages = new object[]
            {
                new { role = "system", content = s.LlmPrompt },
                new { role = "user", content = text }
            },
            options = new { temperature = 0.1, num_predict = Math.Max(128, text.Length) }
        };
        using var resp = await http.PostAsJsonAsync(s.LlmUrl.TrimEnd('/') + "/api/chat", body, cts.Token);
        var json = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Ollama HTTP {(int)resp.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        var clean = doc.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
        // Salvaguardas: una depuración solo quita palabras. Si el modelo devuelve vacío, mucho más largo o
        // con palabras que no estaban en la entrada (reescribe o inventa), se descarta y se usa el texto original.
        if (clean.Length == 0 || clean.Length > text.Length * 1.3 + 20) return text;
        var input = Words(text).ToHashSet();
        var output = Words(clean).ToList();
        int known = output.Count(input.Contains);
        return output.Count > 0 && known < output.Count * 0.85 ? text : clean;
    }

    static IEnumerable<string> Words(string t) =>
        System.Text.RegularExpressions.Regex.Matches(t.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value);

    /// <summary>Carga el modelo en memoria (la primera carga puede tardar más de un minuto) y lo mantiene cargado.</summary>
    public async Task WarmUpAsync(Settings s)
    {
        Remember(s);
        WarmingUp = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var resp = await http.PostAsJsonAsync(s.LlmUrl.TrimEnd('/') + "/api/generate",
                new { model = s.LlmModel, keep_alive = s.LlmKeepAlive }, cts.Token);
        }
        catch (Exception ex) { Settings.Log("Precarga LLM: " + ex.Message); }
        finally { WarmingUp = false; }
    }

    /// <summary>True mientras el modelo se carga en frío; entonces el texto sale sin depurar en vez de esperar.</summary>
    public volatile bool WarmingUp;

    public async Task<string> CheckAsync(string url, string model)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var json = await http.GetStringAsync(url.TrimEnd('/') + "/api/tags", cts.Token);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.GetProperty("models").EnumerateArray()
            .Select(m => m.GetProperty("name").GetString() ?? "").ToList();
        bool found = names.Any(n => n == model || n == model + ":latest");
        return found ? $"Conectado. Modelo «{model}» disponible."
                     : $"Conectado, pero no existe «{model}». Modelos: {string.Join(", ", names)}";
    }

    public void Dispose() => http.Dispose();
}
