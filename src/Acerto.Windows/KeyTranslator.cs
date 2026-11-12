using static Acerto.Windows.Native;

namespace Acerto.Windows;

/// <summary>Tecla física → caractere, no layout do app em foreground, sem mexer no estado de tecla morta do Windows.</summary>
internal static class KeyTranslator
{
    private const uint DontChangeKeyboardState = 0x4; // Windows 10 1607+

    /// <param name="dead">true se a tecla é morta (´ ~ ^ ` ¨); o texto traz o caractere dela.</param>
    /// <returns>Texto produzido, ou "" se a tecla não produz caractere.</returns>
    public static string Translate(uint vk, uint scan, out bool dead)
    {
        dead = false;
        var state = new byte[256];
        if (IsDown(VK_SHIFT)) state[VK_SHIFT] = 0x80;
        if (IsDown(VK_CONTROL)) state[VK_CONTROL] = 0x80;
        if (IsDown(VK_MENU)) state[VK_MENU] = 0x80;
        if ((GetKeyState(VK_CAPITAL) & 1) != 0) state[VK_CAPITAL] = 0x01;

        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var hkl = GetKeyboardLayout(thread);
        var buf = new char[8];
        int n = ToUnicodeEx(vk, scan, state, buf, buf.Length, DontChangeKeyboardState, hkl);
        if (n < 0)
        {
            dead = true;
            return buf[0].ToString();
        }
        if (n == 0) return "";
        var text = new string(buf, 0, n);
        return text.Any(char.IsControl) ? "" : text;
    }
}
