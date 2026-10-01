using System.Net.Http.Json;
using System.Text.Json;

namespace SpeechToText;

/// <summary>Depura cada fragmento transcrito con un modelo local de Ollama (/api/chat).</summary>
sealed class LlmCleaner : IDisposable
{
    readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Devuelve el texto depurado. Lanza excepción si Ollama no responde; el llamante usa el texto original.</summary>
    public async Task<string> CleanAsync(Settings s, string text, CancellationToken ct = default)
    {
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
        // Salvaguarda: si el modelo devuelve vacío o mucho más largo que la entrada, no es una depuración.
        return clean.Length == 0 || clean.Length > text.Length * 1.3 + 20 ? text : clean;
    }

    /// <summary>Carga el modelo en memoria (la primera carga puede tardar más de un minuto) y lo mantiene cargado.</summary>
    public async Task WarmUpAsync(Settings s)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var resp = await http.PostAsJsonAsync(s.LlmUrl.TrimEnd('/') + "/api/generate",
                new { model = s.LlmModel, keep_alive = s.LlmKeepAlive }, cts.Token);
        }
        catch (Exception ex) { Settings.Log("Precarga LLM: " + ex.Message); }
    }

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
