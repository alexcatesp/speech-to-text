using SpeechToText;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\SpeechToText.SingleInstance", out bool first);
        if (!first) return;

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Settings.Log("UI: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Settings.Log("Fatal: " + e.ExceptionObject);
        Application.Run(new TrayApp());
    }
}
