using System.Runtime.InteropServices;
using static SpeechToText.NativeMethods;

namespace SpeechToText;

/// <summary>Escribe texto en la ventana con foco simulando pulsaciones Unicode (no usa el portapapeles).</summary>
static class TextInjector
{
    public static void Type(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            if (c == '\r') continue;
            ushort vk = 0, scan = c;
            uint flags = KEYEVENTF_UNICODE;
            if (c == '\n') { vk = 0x0D; scan = 0; flags = 0; }
            inputs.Add(Key(vk, scan, flags));
            inputs.Add(Key(vk, scan, flags | KEYEVENTF_KEYUP));
        }
        var arr = inputs.ToArray();
        // En bloques, para no saturar la cola de entrada de la ventana destino.
        for (int i = 0; i < arr.Length; i += 200)
        {
            var chunk = arr.Skip(i).Take(200).ToArray();
            SendInput((uint)chunk.Length, chunk, Marshal.SizeOf<INPUT>());
        }
    }

    static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
    };
}
