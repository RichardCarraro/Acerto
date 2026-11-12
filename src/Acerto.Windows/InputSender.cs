using System.Runtime.InteropServices;
using static Acerto.Windows.Native;

namespace Acerto.Windows;

/// <summary>Digita no app da vez. Tudo vai marcado com ACRT para o nosso hook ignorar.</summary>
internal static class InputSender
{
    public const nuint Marker = 0x41435254; // "ACRT"

    /// <returns>true se o Windows aceitou todos os eventos.</returns>
    public static bool Send(int backspaces, string text)
    {
        var inputs = new List<INPUT>(backspaces * 2 + text.Length * 2);
        for (int i = 0; i < backspaces; i++) AddVk(inputs, VK_BACK);
        foreach (var ch in text)
        {
            if (ch == '\n') AddVk(inputs, VK_RETURN);
            else if (ch == '\t') AddVk(inputs, VK_TAB);
            else
            {
                inputs.Add(Key(0, ch, KEYEVENTF_UNICODE));
                inputs.Add(Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }
        }
        var arr = inputs.ToArray();
        return SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>()) == arr.Length;
    }

    private static void AddVk(List<INPUT> list, int vk)
    {
        list.Add(Key((ushort)vk, 0, 0));
        list.Add(Key((ushort)vk, 0, KEYEVENTF_KEYUP));
    }

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, dwExtraInfo = Marker } },
    };
}
