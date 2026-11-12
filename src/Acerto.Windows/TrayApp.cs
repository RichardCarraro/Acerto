using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Acerto.Core;
using Acerto.Windows.Forms;
using Microsoft.Win32;

namespace Acerto.Windows;

/// <summary>Ícone na bandeja + menu. Carrega o dicionário em segundo plano e só então liga o corretor.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Acerto";
    private static readonly Color Active = ColorTranslator.FromHtml("#1E63D6");
    private static readonly Color Paused = ColorTranslator.FromHtml("#8A8A8A");

    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pauseItem, _startupItem;
    private readonly List<ToolStripItem> _needsData = [];
    private CoreData? _data;
    private InputHooks? _hooks;
    private Icon? _icon;

    public TrayApp(AppSettings settings)
    {
        _settings = settings;

        _pauseItem = new ToolStripMenuItem("Pausar", null, (_, _) => _hooks?.TogglePause()) { ShortcutKeyDisplayString = "Ctrl+Shift+F12" };
        var words = new ToolStripMenuItem("Palavras aprendidas…", null, (_, _) => ShowWords());
        var shortcuts = new ToolStripMenuItem("Atalhos…", null, (_, _) => Show(new ShortcutsForm(_data!.Shortcuts)));
        var apps = new ToolStripMenuItem("Apps excluídos…", null, (_, _) => ShowApps());
        _startupItem = new ToolStripMenuItem("Iniciar com o Windows", null, (_, _) => ToggleStartup()) { Checked = IsStartupEnabled() };
        var support = new ToolStripMenuItem("Apoie o projeto ♥", null, (_, _) => Show(new SupportForm()));
        var exit = new ToolStripMenuItem("Sair", null, (_, _) => ExitThread());
        _needsData.AddRange([_pauseItem, words, shortcuts]);
        foreach (var item in _needsData) item.Enabled = false;

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_pauseItem, new ToolStripSeparator(), words, shortcuts, apps, new ToolStripSeparator(),
            _startupItem, support, new ToolStripSeparator(), exit]);

        _tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Acertô — carregando o dicionário…" };
        SetIcon(Paused);

        var ui = SynchronizationContext.Current!;
        Task.Run(() => CoreData.Load(Path.Combine(AppContext.BaseDirectory, "data"), AppSettings.UserDir))
            .ContinueWith(t => ui.Post(_ => OnLoaded(t), null));
    }

    private void OnLoaded(Task<CoreData> load)
    {
        if (load.Exception is { } ex)
        {
            MessageBox.Show($"O Acertô não conseguiu carregar o dicionário e vai fechar.\n\n{ex.InnerException?.Message}",
                "Acertô", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitThread();
            return;
        }
        _data = load.Result;
        var engine = new TextEngine(new Corrector(_data), _data.Personal);
        _hooks = new InputHooks(engine, _settings, new ForegroundInfo());
        _hooks.PauseToggled += UpdateState;
        _hooks.Install();
        foreach (var item in _needsData) item.Enabled = true;
        UpdateState();

        if (!_settings.WelcomeShown)
        {
            _tray.ShowBalloonTip(8000, "Acertô está ativo", "Corrigindo em todos os programas. Ctrl+Shift+F12 pausa.", ToolTipIcon.Info);
            _settings.WelcomeShown = true;
            _settings.Save();
        }
    }

    private void UpdateState()
    {
        _pauseItem.Text = _settings.Paused ? "Retomar" : "Pausar";
        _tray.Text = _settings.Paused ? "Acertô — pausado (Ctrl+Shift+F12)" : "Acertô — ativo";
        SetIcon(_settings.Paused ? Paused : Active);
    }

    private void ShowWords() => Show(new ListEditorForm(
        "Acertô — Palavras aprendidas",
        "Palavras que o Acertô não corrige mais. Remova uma para ele voltar a corrigir.",
        _data!.Personal.Words, allowAdd: true, normalize: w => w.ToLowerInvariant(),
        items =>
        {
            var keep = new HashSet<string>(items, StringComparer.Ordinal);
            foreach (var w in _data.Personal.Words.Where(w => !keep.Contains(w)).ToList()) _data.Personal.Remove(w);
            foreach (var w in keep) _data.Personal.Add(w);
        }));

    private void ShowApps() => Show(new ListEditorForm(
        "Acertô — Apps excluídos",
        "Programas onde o Acertô não corrige (nome do processo, ex.: Code.exe).",
        _settings.ExcludedApps, allowAdd: true, normalize: a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a : a + ".exe",
        items =>
        {
            _settings.ExcludedApps = [.. items];
            _settings.Save();
        }));

    private static void Show(Form form)
    {
        form.ShowInTaskbar = true;
        form.Show();
        form.Activate();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    private void ToggleStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (IsStartupEnabled()) key.DeleteValue(RunValue, throwOnMissingValue: false);
        else key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        _startupItem.Checked = IsStartupEnabled();
        _settings.StartWithWindows = _startupItem.Checked;
        _settings.Save();
    }

    /// <summary>Círculo colorido com um "ô" branco, desenhado em tempo de execução.</summary>
    private void SetIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString("ô", font);
            g.DrawString("ô", font, Brushes.White, (32 - size.Width) / 2, (32 - size.Height) / 2 - 1);
        }
        var hIcon = bmp.GetHicon();
        var old = _icon;
        _icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        _tray.Icon = _icon;
        old?.Dispose();
    }

    protected override void ExitThreadCore()
    {
        _hooks?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _icon?.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint hIcon);
}
