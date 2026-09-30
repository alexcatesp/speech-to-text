using static SpeechToText.NativeMethods;

namespace SpeechToText;

/// <summary>Ventana invisible que recibe WM_HOTKEY del atajo global.</summary>
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int Id = 0x5354;
    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public static bool TryParse(string text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": mods |= MOD_WIN; break;
                default: return false;
            }
        }
        if (!Enum.TryParse<Keys>(parts[^1], true, out var key)) return false;
        vk = (uint)key;
        return mods != 0 || (key >= Keys.F1 && key <= Keys.F24);
    }

    public bool Register(string hotkey)
    {
        UnregisterHotKey(Handle, Id);
        return TryParse(hotkey, out var mods, out var vk) && RegisterHotKey(Handle, Id, mods | MOD_NOREPEAT, vk);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }
}
