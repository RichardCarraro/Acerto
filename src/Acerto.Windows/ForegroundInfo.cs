using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using static Acerto.Windows.Native;

namespace Acerto.Windows;

/// <summary>Quem está em foreground: nome do processo, se roda como admin e se o foco é campo de senha.</summary>
internal sealed class ForegroundInfo
{
    private uint _pid;
    private int _version;

    public string ProcessName { get; private set; } = "";
    public bool IsElevated { get; private set; }
    public bool IsOwnProcess { get; private set; }

    private volatile bool _focusIsPassword = true;
    private volatile bool _focusResolved;
    public bool FocusIsPassword => _focusIsPassword;
    public bool FocusResolved => _focusResolved;

    /// <summary>Chamado na thread da UI quando o foreground ou o foco mudam.</summary>
    public void Refresh()
    {
        var hwnd = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != _pid)
        {
            _pid = pid;
            IsOwnProcess = pid == (uint)Environment.ProcessId;
            ProcessName = NameOf(pid);
            IsElevated = Elevated(pid);
        }

        _focusResolved = false;
        _focusIsPassword = true;
        int version = Interlocked.Increment(ref _version);
        Task.Run(() =>
        {
            bool pwd = IsPasswordFocus(thread);
            if (version != Volatile.Read(ref _version)) return; // o foco já mudou de novo
            _focusIsPassword = pwd;
            _focusResolved = true;
        });
    }

    private static string NameOf(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName + ".exe"; }
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
    }

    /// <summary>Se não der para abrir o processo, trata como elevado: injetar nele perderia teclas.</summary>
    private static bool Elevated(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return true;
        try
        {
            if (!OpenProcessToken(h, TOKEN_QUERY, out var token)) return true;
            try { return !GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) || elevated != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(h); }
    }

    /// <summary>UI Automation primeiro; o estilo ES_PASSWORD do Win32 como reserva. Erro conta como senha.</summary>
    private static bool IsPasswordFocus(uint thread)
    {
        try
        {
            var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(thread, ref info) && info.hwndFocus != 0)
            {
                var cls = new StringBuilder(64);
                GetClassNameW(info.hwndFocus, cls, cls.Capacity);
                if (cls.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase)
                    && (GetWindowLongW(info.hwndFocus, GWL_STYLE) & ES_PASSWORD) != 0)
                    return true;
            }
            return AutomationElement.FocusedElement?.Current.IsPassword ?? false;
        }
        catch (Exception)
        {
            return true; // UIA falha de vários jeitos em apps diferentes; na dúvida, não corrige
        }
    }
}
