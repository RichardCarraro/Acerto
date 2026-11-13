using System.Runtime.InteropServices;
using System.Windows.Forms;
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

    // Para a animação achar a palavra corrigida mesmo se a pessoa continuou digitando:
    // quantos caracteres entraram depois da última correção (negativo = não dá mais para saber).
    private int _charsSinceCorrection = -1;
    private int _fedChars; // caracteres que a tecla atual produziu
    public int CharsSinceCorrection => _charsSinceCorrection;
    /// <summary>Aumenta a cada palavra corrigida; a animação usa para saber se ainda é a correção mais recente.</summary>
    public int CorrectionSequence { get; private set; }
    private System.Windows.Forms.Timer? _watchdog;
    private uint _lastHookTick; // mesmo relógio do GetLastInputInfo

    // Depois de um reset (clique, foco, seta), pergunta ao app o que há antes do cursor.
    // Espera um pouco para o app terminar de mover o cursor.
    private readonly System.Windows.Forms.Timer _probeTimer = new() { Interval = 150 };
    private int _probeAttempts;
    private const int MaxProbeAttempts = 3;

    public event Action? PauseToggled;

    /// <summary>Uma palavra foi corrigida. Disparado de dentro do hook: quem ouve tem que ser rápido.</summary>
    public event Action<EngineAction>? Corrected;

    /// <summary>O fio da digitação foi perdido (clique, troca de janela, seta, Enter): animações em curso não valem mais.</summary>
    public event Action? ContextLost;

    /// <summary>As sugestões para o ponto atual mudaram (lista vazia = esconder). O número é o tamanho da palavra em andamento.</summary>
    public event Action<IReadOnlyList<string>, int>? SuggestionsChanged;

    /// <summary>
    /// O que a barra de sugestões está mostrando agora. Quem mostra a barra preenche; o Tab só aceita a primeira
    /// sugestão quando a pessoa de fato está vendo a barra.
    /// </summary>
    public IReadOnlyList<string> VisibleSuggestions { get; set; } = [];
    private bool _suggestionsShown; // já avisamos alguém de uma lista não vazia

    private SynchronizationContext _ui = null!; // capturado no Install, na thread da UI

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
        Task.Run(() =>
        {
            var text = CaretContext.TextBeforeCaret(); // UIA é lenta: fora da thread do hook
            _ui.Post(_ =>
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
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Install precisa rodar na thread da UI.");
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
                ResetAll("vigia");
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
        ResetAll("pausa");
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
                ResetAll("inativo");
                return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
            }

            long generation = _engine.Generation;
            _fedChars = 0;
            var action = Handle(k);
            _ui.Post(_ => PublishSuggestions(), null); // depois que o callback voltar: aqui dentro tem que ser rápido
            if (generation != _engine.Generation) // o motor resetou por conta própria (ex.: Backspace)
            {
                if (Diag.On) Diag.Log("RESET motor (backspace em contexto vazio)");
                ScheduleProbe();
            }
            if (Diag.On) Diag.Log($"KEY ativo swallow={action.Swallow} bs={action.Backspaces} ins={action.Insert.Length} proc={_fg.ProcessName}");
            if (action.Swallow)
            {
                // Com Ctrl/Alt/Win apertado, um Backspace injetado vira atalho (Ctrl+Backspace apaga palavra).
                bool modifiers = IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN);
                // Janela trocou e o evento de foco ainda não chegou: os Backspaces iriam para texto que não vimos.
                bool sameWindow = GetForegroundWindow() == _fg.Hwnd;
                if (Diag.On && (modifiers || !sameWindow)) Diag.Log($"SWALLOW cancelado modifiers={modifiers} sameWindow={sameWindow}");
                if (!modifiers && sameWindow && InputSender.Send(action.Backspaces, action.Insert))
                {
                    if (action.WordLength > 0)
                    {
                        _charsSinceCorrection = 0;
                        CorrectionSequence++;
                        Corrected?.Invoke(action);
                    }
                    else if (_charsSinceCorrection >= 0)
                    {
                        _charsSinceCorrection += action.Insert.Length - action.Backspaces; // espaço duplo, só maiúscula
                    }
                    return 1;
                }
                ResetAll("envio-falhou"); // não deu para corrigir: deixa a tecla original passar
            }
            else if (_charsSinceCorrection >= 0)
            {
                int vk = (int)k.vkCode;
                if (vk == VK_BACK) _charsSinceCorrection--;            // negativo = apagou para antes da correção
                else if (vk is VK_RETURN or VK_TAB) LoseContext();     // mudou de linha/campo: a posição não vale mais
                else _charsSinceCorrection += _fedChars;
            }
        }
        catch (Exception)
        {
            // exceção saindo de callback nativo derruba o processo; na dúvida, deixa a tecla passar
            ResetAll("excecao");
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
            return ResetAll("atalho"); // atalhos: Ctrl+C, Alt+Tab, Ctrl+Backspace...

        // Tecla morta pendente no app + Backspace/Enter/Tab: o app vai combinar a morta com o que injetarmos.
        if (_pendingDead is not null && vk is VK_BACK or VK_RETURN or VK_TAB)
            return ResetAll("morta+enter/back/tab");

        switch (vk)
        {
            case VK_BACK: return _engine.OnBackspace();
            case VK_RETURN: return _engine.OnChar('\n');
            case VK_TAB:
                if (IsDown(VK_SHIFT)) return ResetAll("shift+tab");
                if (VisibleSuggestions.Count > 0) // Tab aceita a primeira sugestão que está na tela
                {
                    var first = VisibleSuggestions[0];
                    HideSuggestions();
                    return _engine.AcceptSuggestion(first);
                }
                return _engine.OnChar('\t');
            case VK_ESCAPE or VK_INSERT or VK_DELETE or VK_APPS or (>= VK_PRIOR and <= VK_DOWN):
                return ResetAll("navegacao");
            case VK_PACKET:
                return Feed(((char)k.scanCode).ToString());
        }

        var text = KeyTranslator.Translate(k.vkCode, k.scanCode, out bool dead);
        if (dead)
        {
            if (_pendingDead is not null) return ResetAll("duas-mortas"); // duas mortas seguidas: não tentar adivinhar
            _pendingDead = text[0];
            return EngineAction.None;
        }
        if (text.Length == 0) return ResetAll("tecla-sem-caractere");

        if (_pendingDead is { } d)
        {
            _pendingDead = null;
            text = DeadKeys.Compose(d, text[0]) + text[1..];
            if (text.Length != 1) return ResetAll("morta-composta"); // "~x": o app compõe sozinho; não arriscar engolir
        }
        return Feed(text);
    }

    private EngineAction Feed(string text)
    {
        var action = EngineAction.None;
        foreach (var ch in text) action = _engine.OnChar(ch);
        _fedChars = text.Length;
        if (text.Length > 1 && action.Swallow) return ResetAll("varios-caracteres"); // vários caracteres numa tecla só: não engolir
        return action;
    }

    private void LoseContext()
    {
        if (_charsSinceCorrection == -1) return;
        _charsSinceCorrection = -1;
        ContextLost?.Invoke();
    }

    /// <param name="why">Motivo, só para o diagnóstico (nunca inclui o que foi digitado).</param>
    /// <summary>Calcula as sugestões para o ponto atual e avisa quem desenha a barra.</summary>
    private void PublishSuggestions()
    {
        IReadOnlyList<string> list = _settings.ShowSuggestions && Active ? _engine.Suggestions() : [];
        if (list.Count == 0)
        {
            HideSuggestions();
            return;
        }
        _suggestionsShown = true;
        SuggestionsChanged?.Invoke(list, _engine.CurrentWordLength);
    }

    private void HideSuggestions()
    {
        VisibleSuggestions = [];
        if (!_suggestionsShown) return;
        _suggestionsShown = false;
        SuggestionsChanged?.Invoke([], 0);
    }

    private EngineAction ResetAll(string why)
    {
        if (Diag.On && why != "inativo") Diag.Log("RESET " + why);
        HideSuggestions();
        _engine.Reset();
        _pendingDead = null;
        LoseContext();
        ScheduleProbe();
        return EngineAction.None;
    }

    private nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        _lastHookTick = (uint)Environment.TickCount;
        try
        {
            if (nCode >= 0 && wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
                ResetAll("mouse");
        }
        catch (Exception) { /* nunca deixar exceção sair de callback nativo */ }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            ResetAll("foco");
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
