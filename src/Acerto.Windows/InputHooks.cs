using System.Runtime.InteropServices;
using Acerto.Core;
using static Acerto.Windows.Native;

namespace Acerto.Windows;

/// <summary>
/// Escuta teclado e mouse do sistema inteiro e conversa com o <see cref="TextEngine"/>.
/// Tudo roda na thread da UI; o callback do teclado precisa voltar rápido.
/// </summary>
internal sealed class InputHooks : IDisposable
{
    private const int PauseHotkeyId = 1;

    private readonly TextEngine _engine;
    private readonly AppSettings _settings;
    private readonly ForegroundInfo _fg;

    // delegates guardados em campos: se o GC coletar, o Windows chama memória solta
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private readonly WinEventProc _winEventProc;
    private nint _keyboardHook, _mouseHook, _foregroundHook, _focusHook;
    private HotkeyWindow? _hotkeyWindow;
    private char? _pendingDead;
    private System.Windows.Forms.Timer? _watchdog;
    private uint _lastHookTick; // mesmo relógio do GetLastInputInfo

    // Depois de um reset (clique, foco, seta), pergunta ao app o que há antes do cursor.
    // Espera um pouco para o app terminar de mover o cursor.
    private readonly System.Windows.Forms.Timer _probeTimer = new() { Interval = 150 };
    private int _probeAttempts;
    private const int MaxProbeAttempts = 3;

    public event Action? PauseToggled;

    public InputHooks(TextEngine engine, AppSettings settings, ForegroundInfo fg)
    {
        _engine = engine;
        _settings = settings;
        _fg = fg;
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
        _winEventProc = WinEventProc;
        _probeTimer.Tick += (_, _) => ProbeContext();
    }

    private void ScheduleProbe()
    {
        _probeAttempts = 0;
        _probeTimer.Stop();
        _probeTimer.Start();
    }

    private void ProbeContext()
    {
        _probeTimer.Stop();
        if (++_probeAttempts > MaxProbeAttempts) return;
        if (!Active)
        {
            if (!_fg.FocusResolved) _probeTimer.Start(); // a checagem de senha ainda não terminou
            return;
        }
        long generation = _engine.Generation;
        var ui = SynchronizationContext.Current!;
        Task.Run(() =>
        {
            var text = CaretContext.TextBeforeCaret(); // UIA é lenta: fora da thread do hook
            ui.Post(_ =>
            {
                if (generation != _engine.Generation) return; // houve outro reset; ele agendou a própria consulta
                bool ok = text is not null && _engine.ConfirmContext(generation, text);
                if (Diag.On) Diag.Log($"CONTEXTO ok={ok} lido={text?.Length ?? -1} tentativa={_probeAttempts}");
                if (!ok && text is not null) _probeTimer.Start(); // digitou no meio da consulta: tenta de novo
            }, null);
        });
    }

    public void Install()
    {
        InstallLowLevelHooks();

        // O Windows remove hook lento sem avisar. Se houve entrada que o hook não viu, reinstala.
        _watchdog = new System.Windows.Forms.Timer { Interval = 5000 };
        _watchdog.Tick += (_, _) =>
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (GetLastInputInfo(ref info) && unchecked((int)(info.dwTime - _lastHookTick)) > 2000)
            {
                Diag.Log($"WATCHDOG reinstala (lastInput={info.dwTime} lastHook={_lastHookTick})");
                UnhookWindowsHookEx(_keyboardHook);
                UnhookWindowsHookEx(_mouseHook);
                InstallLowLevelHooks();
                ResetAll();
            }
        };
        _watchdog.Start();

        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        _focusHook = SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, 0, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        _hotkeyWindow = new HotkeyWindow(TogglePause);
        RegisterHotKey(_hotkeyWindow.Handle, PauseHotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F12);

        _fg.Refresh();
    }

    private void InstallLowLevelHooks()
    {
        var module = GetModuleHandleW(null);
        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook == 0 || _mouseHook == 0)
            throw new InvalidOperationException($"Não foi possível instalar o hook de teclado/mouse (erro {Marshal.GetLastWin32Error()}).");
        _lastHookTick = (uint)Environment.TickCount;
    }

    public void TogglePause()
    {
        _settings.Paused = !_settings.Paused;
        _settings.Save();
        ResetAll();
        PauseToggled?.Invoke();
    }

    private bool Active =>
        !_settings.Paused && !_fg.IsOwnProcess && !_fg.IsElevated && !_fg.IsFullScreenApp
        && _fg.FocusResolved && !_fg.FocusIsPassword && !_settings.IsExcluded(_fg.ProcessName);

    private nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        _lastHookTick = (uint)Environment.TickCount;
        if (nCode < 0 || (wParam != WM_KEYDOWN && wParam != WM_SYSKEYDOWN))
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        try
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((k.flags & LLKHF_INJECTED) != 0 && k.dwExtraInfo == InputSender.Marker)
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

            if (!Active)
            {
                if (Diag.On) Diag.Log($"KEY inativo paused={_settings.Paused} own={_fg.IsOwnProcess} elev={_fg.IsElevated} full={_fg.IsFullScreenApp} resolved={_fg.FocusResolved} pwd={_fg.FocusIsPassword} excl={_settings.IsExcluded(_fg.ProcessName)} proc={_fg.ProcessName}");
                ResetAll();
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            long generation = _engine.Generation;
            var action = Handle(k);
            if (generation != _engine.Generation) ScheduleProbe(); // o motor resetou por conta própria (ex.: Backspace)
            if (Diag.On) Diag.Log($"KEY ativo swallow={action.Swallow} bs={action.Backspaces} ins={action.Insert.Length} proc={_fg.ProcessName}");
            if (action.Swallow)
            {
                // Com Ctrl/Alt/Win apertado, um Backspace injetado vira atalho (Ctrl+Backspace apaga palavra).
                bool modifiers = IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN);
                // Janela trocou e o evento de foco ainda não chegou: os Backspaces iriam para texto que não vimos.
                bool sameWindow = GetForegroundWindow() == _fg.Hwnd;
                if (Diag.On && (modifiers || !sameWindow)) Diag.Log($"SWALLOW cancelado modifiers={modifiers} sameWindow={sameWindow}");
                if (!modifiers && sameWindow && InputSender.Send(action.Backspaces, action.Insert))
                    return 1;
                ResetAll(); // não deu para corrigir: deixa a tecla original passar
            }
        }
        catch (Exception)
        {
            // exceção saindo de callback nativo derruba o processo; na dúvida, deixa a tecla passar
            ResetAll();
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private EngineAction Handle(KBDLLHOOKSTRUCT k)
    {
        int vk = (int)k.vkCode;

        if (vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_CAPITAL or VK_NUMLOCK or VK_SCROLL or VK_PAUSE
            or VK_LWIN or VK_RWIN or (>= VK_LSHIFT and <= VK_RMENU) or (>= VK_F1 and <= VK_F24))
            return EngineAction.None;

        bool ctrl = IsDown(VK_CONTROL), alt = IsDown(VK_MENU);
        bool altGr = ctrl && IsDown(VK_RMENU);
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN) || ((ctrl || alt) && !altGr))
            return ResetAll(); // atalhos: Ctrl+C, Alt+Tab, Ctrl+Backspace...

        // Tecla morta pendente no app + Backspace/Enter/Tab: o app vai combinar a morta com o que injetarmos.
        if (_pendingDead is not null && vk is VK_BACK or VK_RETURN or VK_TAB)
            return ResetAll();

        switch (vk)
        {
            case VK_BACK: return _engine.OnBackspace();
            case VK_RETURN: return _engine.OnChar('\n');
            case VK_TAB: return IsDown(VK_SHIFT) ? ResetAll() : _engine.OnChar('\t');
            case VK_ESCAPE or VK_INSERT or VK_DELETE or VK_APPS or (>= VK_PRIOR and <= VK_DOWN):
                return ResetAll();
            case VK_PACKET:
                return Feed(((char)k.scanCode).ToString());
        }

        var text = KeyTranslator.Translate(k.vkCode, k.scanCode, out bool dead);
        if (dead)
        {
            if (_pendingDead is not null) return ResetAll(); // duas mortas seguidas: não tentar adivinhar
            _pendingDead = text[0];
            return EngineAction.None;
        }
        if (text.Length == 0) return ResetAll();

        if (_pendingDead is { } d)
        {
            _pendingDead = null;
            text = DeadKeys.Compose(d, text[0]) + text[1..];
            if (text.Length != 1) return ResetAll(); // "~x": o app compõe sozinho; não arriscar engolir
        }
        return Feed(text);
    }

    private EngineAction Feed(string text)
    {
        var action = EngineAction.None;
        foreach (var ch in text) action = _engine.OnChar(ch);
        if (text.Length > 1 && action.Swallow) return ResetAll(); // vários caracteres numa tecla só: não engolir
        return action;
    }

    private EngineAction ResetAll()
    {
        _engine.Reset();
        _pendingDead = null;
        ScheduleProbe();
        return EngineAction.None;
    }

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        _lastHookTick = (uint)Environment.TickCount;
        try
        {
            if (nCode >= 0 && wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
                ResetAll();
        }
        catch (Exception) { /* nunca deixar exceção sair de callback nativo */ }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            ResetAll();
            _fg.Refresh();
            if (Diag.On) Diag.Log($"FOCO ev={(ev == EVENT_SYSTEM_FOREGROUND ? "foreground" : "focus")} proc={_fg.ProcessName}");
        }
        catch (Exception) { /* nunca deixar exceção sair de callback nativo */ }
    }

    public void Dispose()
    {
        _watchdog?.Dispose();
        _probeTimer.Dispose();
        if (_hotkeyWindow is not null)
        {
            UnregisterHotKey(_hotkeyWindow.Handle, PauseHotkeyId);
            _hotkeyWindow.DestroyHandle();
        }
        if (_keyboardHook != 0) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        if (_foregroundHook != 0) UnhookWinEvent(_foregroundHook);
        if (_focusHook != 0) UnhookWinEvent(_focusHook);
    }

    /// <summary>Janela invisível só para receber o atalho global de pausa.</summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action _onHotkey;

        public HotkeyWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) _onHotkey();
            base.WndProc(ref m);
        }
    }
}
