using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Acerto.Core;
using Acerto.Windows.UI;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Acerto.Windows;

/// <summary>
/// O "dono" do app: ícone na bandeja, carga do dicionário em segundo plano, liga o corretor aos hooks
/// e expõe para as janelas tudo que elas podem ver e mudar.
/// </summary>
internal sealed class AppController
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Acerto";
    private static readonly Drawing.Color ActiveColor = Drawing.ColorTranslator.FromHtml("#1E63D6");
    private static readonly Drawing.Color PausedColor = Drawing.ColorTranslator.FromHtml("#8A8A8A");

    private readonly Application _app;
    private readonly Dispatcher _ui;
    private readonly WinForms.NotifyIcon _tray;
    private readonly DispatcherTimer _saveTimer;
    private Drawing.Icon? _icon;
    private TextEngine? _engine;
    private Corrector? _corrector;
    private InputHooks? _hooks;
    private MainWindow? _window;
    private TrayFlyout? _flyout;
    private HighlightOverlay? _overlay;
    private SuggestionBar? _bar;
    private int _suggestionSequence;
    private bool _settingsDirty;
    private readonly bool _preview;

    public AppSettings Settings { get; }
    public CoreData? Data { get; private set; }
    public bool Ready => _engine is not null;

    /// <summary>Algo que as janelas mostram mudou (carregou, pausou, contador, listas).</summary>
    public event Action? StateChanged;

    /// <param name="preview">Só para gerar imagens das telas: carrega os dados, mas não mostra ícone nem instala hooks.</param>
    public AppController(Application app, bool preview = false)
    {
        _preview = preview;
        _app = app;
        _ui = app.Dispatcher;
        Settings = AppSettings.Load();

        _tray = new WinForms.NotifyIcon { Visible = !preview, Text = "Acertô — carregando o dicionário…" };
        _tray.MouseUp += (_, _) => ShowFlyout();
        SetIcon(PausedColor);

        // o contador muda a cada correção; gravar em disco a cada palavra seria exagero
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (_, _) => SaveIfDirty();
        _saveTimer.Start();

        Task.Run(() =>
        {
            var data = CoreData.Load(Path.Combine(AppContext.BaseDirectory, "data"), AppSettings.UserDir);
            return (data, corrector: new Corrector(data)); // os índices são pesados: fora da thread da UI
        }).ContinueWith(t => _ui.BeginInvoke(() => OnLoaded(t)));
    }

    private void OnLoaded(Task<(CoreData data, Corrector corrector)> load)
    {
        if (load.Exception is { } ex)
        {
            MessageBox.Show($"O Acertô não conseguiu carregar o dicionário e vai fechar.\n\n{ex.InnerException?.Message}",
                "Acertô", MessageBoxButton.OK, MessageBoxImage.Error);
            Exit();
            return;
        }

        Data = load.Result.data;
        // Aprender grava arquivo: nunca dentro do hook. Vai para a fila da UI e roda depois que o callback volta.
        _corrector = load.Result.corrector;
        _engine = new TextEngine(_corrector, word => _ui.BeginInvoke(() => AddWord(word)));
        ApplyEngineOptions();
        if (_preview)
        {
            StateChanged?.Invoke();
            return;
        }

        var hooks = new InputHooks(_engine, Settings, new ForegroundInfo());
        hooks.PauseToggled += OnStateChanged;
        hooks.Corrected += OnCorrected;
        hooks.ContextLost += () => _overlay?.Cancel();
        hooks.SuggestionsChanged += OnSuggestions;
        try
        {
            hooks.Install();
        }
        catch (InvalidOperationException e)
        {
            MessageBox.Show(e.Message + "\n\nO Acertô vai fechar.", "Acertô", MessageBoxButton.OK, MessageBoxImage.Error);
            Exit();
            return;
        }
        _hooks = hooks;

        // devolve ao sistema a memória usada só durante a carga
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();

        OnStateChanged();
        if (!Settings.WelcomeShown)
        {
            _tray.ShowBalloonTip(8000, "Acertô está ativo", "Corrigindo em todos os programas. Clique no ícone para abrir.", WinForms.ToolTipIcon.Info);
            Settings.WelcomeShown = true;
            _settingsDirty = true;
        }
    }

    // ---- o que as janelas podem fazer ----

    public void SetPaused(bool paused)
    {
        if (_hooks is null || Settings.Paused == paused) return;
        _hooks.TogglePause(); // salva e dispara PauseToggled
    }

    /// <summary>Aplica e salva as opções alteradas em <see cref="Settings"/>.</summary>
    public void ApplyOptions()
    {
        ApplyEngineOptions();
        Save();
        StateChanged?.Invoke();
    }

    public bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
            Settings.StartWithWindows = value;
            Save();
            StateChanged?.Invoke();
        }
    }

    public void SaveShortcuts(IDictionary<string, string> shortcuts) =>
        TryIo(() => Data?.Shortcuts.Save(shortcuts));

    public void AddWord(string word) => TryIo(() => Data?.Personal.Add(word));

    public void RemoveWord(string word) => TryIo(() => Data?.Personal.Remove(word));

    public void ClearHistory()
    {
        Data?.History.Clear();
        TryIo(() => Data?.History.Save());
    }

    public void OpenMainWindow(string page = "home")
    {
        if (_window is null)
        {
            _window = new MainWindow(this);
            _window.Closed += (_, _) => _window = null;
        }
        _window.ShowPage(page);
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Exit()
    {
        SaveIfDirty();
        _hooks?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _icon?.Dispose();
        _app.Shutdown();
    }

    // ---- interno ----

    private void ShowFlyout()
    {
        if (_flyout is not null) return;
        _flyout = new TrayFlyout(this);
        _flyout.Closed += (_, _) => _flyout = null;
        _flyout.Show();
        _flyout.Activate();
    }

    /// <summary>Chamado de dentro do hook de teclado: só o mínimo aqui, o resto vai para a fila da UI.</summary>
    private void OnCorrected(EngineAction action)
    {
        Settings.CorrectionCount++;
        _settingsDirty = true;
        var hooks = _hooks;
        bool animate = Settings.CorrectionAnimation && hooks is not null && !action.Insert.EndsWith('\n'); // Enter: o texto já foi embora
        string word = action.Insert[..action.WordLength];
        int trailing = action.Insert.Length - action.WordLength;
        int sequence = hooks?.CorrectionSequence ?? 0;
        _ui.BeginInvoke(async () =>
        {
            if (_window is not null || _flyout is not null) StateChanged?.Invoke(); // contador ao vivo
            if (!animate) return;
            // O app leva alguns milissegundos para receber o texto corrigido; tenta três vezes e desiste.
            foreach (int wait in (int[])[50, 80, 140])
            {
                await Task.Delay(wait);
                int extra = hooks!.CharsSinceCorrection;                        // o que a pessoa digitou depois da correção
                if (hooks.CorrectionSequence != sequence || extra < 0) return;  // já houve outra correção, ou perdeu o fio
                int back = trailing + extra;
                bool preferCaret = hooks.FocusInlineAutocomplete;
                var rect = await OneAtATime(() => CaretContext.WordRect(word, back, preferCaret),
                    () => hooks.CorrectionSequence == sequence && hooks.CharsSinceCorrection >= 0);
                if (hooks.CorrectionSequence != sequence || hooks.CharsSinceCorrection < 0) return;
                if (rect is { } r)
                {
                    (_overlay ??= new HighlightOverlay()).Flash(r);
                    return;
                }
            }
        });
    }

    /// <summary>Mostra (ou esconde) a barra de sugestões perto do cursor.</summary>
    private void OnSuggestions(IReadOnlyList<string> suggestions, int wordLength)
    {
        int sequence = ++_suggestionSequence;
        var hooks = _hooks;
        if (hooks is null) return;
        if (suggestions.Count == 0)
        {
            _bar?.Conceal();
            return;
        }
        if (_bar is { IsShowing: true })
        {
            // mesma palavra, mais uma letra: troca o conteúdo já, e reposiciona quando a posição chegar
            _bar.SetItems(suggestions);
            hooks.VisibleSuggestions = suggestions;
        }
        _ui.BeginInvoke(async () =>
        {
            await Task.Delay(30); // o app precisa receber a tecla antes de informar onde o texto está
            if (sequence != _suggestionSequence) return;
            bool preferCaret = hooks.FocusInlineAutocomplete;
            var anchor = await OneAtATime(() => CaretContext.CaretAnchor(wordLength, preferCaret), () => sequence == _suggestionSequence);
            if (sequence != _suggestionSequence) return;
            if (Diag.On) Diag.Log($"BARRA posicao={(anchor is { } p ? $"{(int)p.Left},{(int)p.Top}" : "nenhuma")}");
            if (anchor is { } a)
            {
                (_bar ??= new SuggestionBar()).ShowAt(a, suggestions);
                hooks.VisibleSuggestions = suggestions;
            }
            else
            {
                // o app não informa a posição: sem barra na tela, o Tab continua sendo Tab
                _bar?.Conceal();
                hooks.VisibleSuggestions = [];
            }
        });
    }

    private Task _uia = Task.CompletedTask;

    /// <summary>
    /// Consulta de posição (UI Automation) numa tarefa por vez: app ocupado demora para responder e, na teclada, as
    /// consultas se empilhavam no pool. Roda na thread da UI; quem esperou confere a própria sequência na volta.
    /// </summary>
    /// <param name="wanted">Conferido depois da espera: a consulta que ficou velha na fila nem começa (devolve null).</param>
    private async Task<System.Windows.Rect?> OneAtATime(Func<System.Windows.Rect?> query, Func<bool> wanted)
    {
        while (!_uia.IsCompleted) await _uia; // as consultas do CaretContext não lançam exceção
        if (!wanted()) return null;
        var task = Task.Run(query);
        _uia = task;
        return await task;
    }

    private void OnStateChanged()
    {
        bool active = Ready && !Settings.Paused;
        _tray.Text = !Ready ? "Acertô — carregando o dicionário…" : active ? "Acertô — ativo" : "Acertô — pausado (Ctrl+Shift+F12)";
        SetIcon(active ? ActiveColor : PausedColor);
        StateChanged?.Invoke();
    }

    private void ApplyEngineOptions()
    {
        if (_engine is null) return;
        _engine.AutoCapitalize = Settings.AutoCapitalize;
        _engine.DoubleSpacePeriod = Settings.DoubleSpacePeriod;
        if (_corrector is not null)
        {
            _corrector.Typos.Options = Settings.CorrectMore ? TypoOptions.Aggressive : TypoOptions.Careful;
            _corrector.LearnFromTyping = Settings.LearnFromTyping;
        }
    }

    private void TryIo(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // sem gravar desta vez (arquivo travado, disco cheio); o app segue funcionando
        }
        StateChanged?.Invoke();
    }

    private void Save()
    {
        _settingsDirty = false;
        try { Settings.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void SaveIfDirty()
    {
        if (_settingsDirty) Save();
        // o histórico muda a cada palavra; vai junto com o contador, no timer (fora do hook) e ao sair
        if (Data?.History is { Dirty: true } history)
        {
            try { history.Save(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Círculo colorido com um "ô" branco, desenhado em tempo de execução.</summary>
    private void SetIcon(Drawing.Color color)
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var brush = new Drawing.SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Drawing.Font("Segoe UI", 17, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            var size = g.MeasureString("ô", font);
            g.DrawString("ô", font, Drawing.Brushes.White, (32 - size.Width) / 2, (32 - size.Height) / 2 - 1);
        }
        var hIcon = bmp.GetHicon();
        var old = _icon;
        _icon = (Drawing.Icon)Drawing.Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        _tray.Icon = _icon;
        old?.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint hIcon);
}
