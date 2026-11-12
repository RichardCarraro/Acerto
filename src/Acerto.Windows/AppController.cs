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
    private InputHooks? _hooks;
    private MainWindow? _window;
    private TrayFlyout? _flyout;
    private HighlightOverlay? _overlay;
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
        _engine = new TextEngine(load.Result.corrector, word => _ui.BeginInvoke(() => AddWord(word)));
        ApplyEngineOptions();
        if (_preview)
        {
            StateChanged?.Invoke();
            return;
        }

        var hooks = new InputHooks(_engine, Settings, new ForegroundInfo());
        hooks.PauseToggled += OnStateChanged;
        hooks.Corrected += OnCorrected;
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
        int wordLength = action.WordLength, trailing = action.Insert.Length - action.WordLength;
        bool animate = Settings.CorrectionAnimation;
        _ui.BeginInvoke(async () =>
        {
            if (_window is not null || _flyout is not null) StateChanged?.Invoke(); // contador ao vivo
            if (!animate) return;
            await Task.Delay(60); // o app precisa terminar de receber o texto corrigido
            var rect = await Task.Run(() => CaretContext.WordRect(wordLength, trailing));
            if (rect is { } r) (_overlay ??= new HighlightOverlay()).Flash(r);
        });
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
