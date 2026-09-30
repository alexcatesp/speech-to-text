using System.Text.Json;
using Microsoft.Win32;

namespace SpeechToText;

public sealed class Settings
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:8000";
    public string ModelName { get; set; } = "";
    public string Language { get; set; } = "es";
    public string MicrophoneName { get; set; } = "";
    public bool OutputToWindow { get; set; } = true;
    public bool OutputToFile { get; set; }
    public string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "transcripcion_{yyyy-MM-dd_HH-mm}.txt");
    public bool TimestampInFile { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public double ThresholdDb { get; set; } = -38;
    public int SilenceMs { get; set; } = 700;
    public int MaxSegmentSeconds { get; set; } = 25;
    public bool StartWithWindows { get; set; } = !IsPortable;

    /// <summary>Modo portable: si existe portable.txt junto al exe, los datos se guardan en .\data.</summary>
    public static readonly bool IsPortable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    static readonly string Dir = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpeechToText");
    public static string SettingsPath => Path.Combine(Dir, "settings.json");
    public static string LogPath => Path.Combine(Dir, "log.txt");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), Json) ?? new();
        }
        catch (Exception ex) { Log("No se pudo leer la configuración: " + ex.Message); }
        var s = new Settings();
        s.Save();
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, Json));
        ApplyAutostart();
    }

    void ApplyAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (StartWithWindows && Environment.ProcessPath is { } exe)
                key.SetValue("SpeechToText", $"\"{exe}\"");
            else
                key.DeleteValue("SpeechToText", false);
        }
        catch (Exception ex) { Log("Autoarranque: " + ex.Message); }
    }

    /// <summary>Sustituye marcadores como {yyyy-MM-dd} por la fecha indicada (formatos de fecha de .NET).</summary>
    public static string ResolvePath(string template, DateTime now) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{([^{}]+)\}", m =>
        {
            try { return now.ToString(m.Groups[1].Value); }
            catch (FormatException) { return m.Value; }
        });

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
