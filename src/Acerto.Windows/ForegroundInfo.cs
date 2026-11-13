using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using static Acerto.Windows.Native;

namespace Acerto.Windows;

/// <summary>Quem está em foreground: nome do processo, se roda como admin e se o foco é campo de senha.</summary>
internal sealed class ForegroundInfo
{
    // Estado do foco publicado de uma vez só: a checagem em segundo plano só grava se ainda for a versão atual.
    private sealed record FocusState(int Version, bool Resolved, bool IsPassword);

    private uint _pid;
    private int _version;
    private FocusState _focus = new(0, false, true);

    public nint Hwnd { get; private set; }
    public string ProcessName { get; private set; } = "";
    public bool IsElevated { get; private set; }
    public bool IsOwnProcess { get; private set; }
    public bool IsFullScreenApp { get; private set; }

    public bool FocusIsPassword => Volatile.Read(ref _focus).IsPassword;
    public bool FocusResolved => Volatile.Read(ref _focus).Resolved;

    /// <summary>Chamado na thread da UI quando o foreground ou o foco mudam.</summary>
    public void Refresh()
    {
        var hwnd = GetForegroundWindow();
        Hwnd = hwnd;
        var thread = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != _pid)
        {
            _pid = pid;
            IsOwnProcess = pid == (uint)Environment.ProcessId;
            ProcessName = NameOf(pid);
            IsElevated = Elevated(pid);
        }
        // jogo em tela cheia / apresentação: espaço é pulo, WASD não é texto
        IsFullScreenApp = SHQueryUserNotificationState(out int state) == 0
            && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;

        int version = Interlocked.Increment(ref _version);
        var pending = new FocusState(version, false, true);
        Volatile.Write(ref _focus, pending);
        Task.Run(() =>
        {
            bool pwd = IsPasswordFocus(thread);
            Interlocked.CompareExchange(ref _focus, new FocusState(version, true, pwd), pending); // perde se o foco mudou
        });
    }

    private static string NameOf(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName + ".exe";
        }
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

    /// <summary>Campo onde não se corrige: senha ou barra de endereço do navegador. UI Automation primeiro; o estilo
    /// ES_PASSWORD do Win32 como reserva. Erro conta como "não corrigir".</summary>
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
            var el = AutomationElement.FocusedElement;
            if (el is null) return false;
            var c = el.Current;
            // Barra de endereço do navegador: ela completa o que você digita deixando o resto selecionado
            // ("chat" + "gpt.com"); o primeiro Backspace da correção apagaria a seleção e estragaria o texto ("cChat").
            // Endereço também não é texto em português. Tratada como campo onde não se corrige.
            if (c.ClassName is "OmniboxViewViews" || c.AutomationId is "urlbar-input" or "urlbar") return true;
            return c.IsPassword;
        }
        catch (Exception)
        {
            return true; // UIA falha de vários jeitos em apps diferentes; na dúvida, não corrige
        }
    }
}
