using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Acerto.Windows.UI;

/// <summary>Janela de configurações. Só lê e altera coisas através do <see cref="AppController"/>.</summary>
internal partial class MainWindow : Window
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x63, 0xD6));
    private static readonly Brush PausedBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppController _app;

    public MainWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        VersionText.Text = "Versão " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0");

        _app.StateChanged += Refresh;
        Closed += (_, _) => _app.StateChanged -= Refresh;
        Refresh();
    }

    public void ShowPage(string page)
    {
        var nav = page switch
        {
            "shortcuts" => NavShortcuts,
            "words" => NavWords,
            "apps" => NavApps,
            "support" => NavSupport,
            _ => NavHome,
        };
        nav.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageHome is null) return; // ainda carregando o XAML
        PageHome.Visibility = Vis(NavHome);
        PageShortcuts.Visibility = Vis(NavShortcuts);
        PageWords.Visibility = Vis(NavWords);
        PageApps.Visibility = Vis(NavApps);
        PageSupport.Visibility = Vis(NavSupport);

        static Visibility Vis(RadioButton nav) => nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Atualiza tudo a partir do estado atual do app (carregado, pausado, contador, listas).</summary>
    private void Refresh()
    {
        var s = _app.Settings;
        bool ready = _app.Ready, active = ready && !s.Paused;

        StatusDot.Background = active ? ActiveBrush : PausedBrush;
        StatusTitle.Text = !ready ? "Carregando o dicionário…" : active ? "O Acertô está ativo" : "O Acertô está pausado";
        StatusHint.Text = !ready ? "Só um instante."
            : active ? "Corrigindo em todos os programas."
            : "Nada é corrigido até você retomar.";
        NavStatus.Text = !ready ? "Carregando…" : active ? "Ativo" : "Pausado";
        ActiveSwitch.IsEnabled = ready;
        ActiveSwitch.IsChecked = active;

        var count = s.CorrectionCount.ToString("N0", PtBr);
        CountText.Text = count;
        SupportCount.Text = s.CorrectionCount > 0 ? $"O Acertô já corrigiu {count} palavras para você." : "";

        MoreSwitch.IsChecked = s.CorrectMore;
        SuggestSwitch.IsChecked = s.ShowSuggestions;
        CapSwitch.IsChecked = s.AutoCapitalize;
        PeriodSwitch.IsChecked = s.DoubleSpacePeriod;
        AnimationSwitch.IsChecked = s.CorrectionAnimation;
        StartupSwitch.IsChecked = _app.StartWithWindows;

        AppList.ItemsSource = s.ExcludedApps.OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase).ToList();

        bool hasData = _app.Data is not null;
        ShortcutTrigger.IsEnabled = ShortcutText.IsEnabled = WordBox.IsEnabled = hasData;
        if (_app.Data is { } data)
        {
            ShortcutList.ItemsSource = data.Shortcuts.All.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
            var words = data.Personal.Words.OrderBy(w => w, StringComparer.CurrentCultureIgnoreCase).ToList();
            WordList.ItemsSource = words;
            WordsEmpty.Visibility = words.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---- Início ----

    private void ActiveSwitch_Click(object sender, RoutedEventArgs e) => _app.SetPaused(ActiveSwitch.IsChecked != true);

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        var s = _app.Settings;
        s.CorrectMore = MoreSwitch.IsChecked == true;
        s.ShowSuggestions = SuggestSwitch.IsChecked == true;
        s.AutoCapitalize = CapSwitch.IsChecked == true;
        s.DoubleSpacePeriod = PeriodSwitch.IsChecked == true;
        s.CorrectionAnimation = AnimationSwitch.IsChecked == true;
        _app.ApplyOptions();
    }

    private void Startup_Click(object sender, RoutedEventArgs e) => _app.StartWithWindows = StartupSwitch.IsChecked == true;

    // ---- Atalhos ----

    private void ShortcutBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddShortcut_Click(sender, e);
    }

    private void AddShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Data is not { } data) return;
        var trigger = ShortcutTrigger.Text.Trim().ToLowerInvariant();
        var text = ShortcutText.Text;
        string? error =
            trigger.Length == 0 ? "Escreva o atalho."
            : trigger.Any(char.IsWhiteSpace) ? "O atalho não pode ter espaço."
            : !trigger.All(char.IsLetter) ? "O atalho só pode ter letras."
            : text.Trim().Length == 0 ? "Escreva o texto que o atalho vira."
            : null;
        ShortcutError.Text = error ?? "";
        ShortcutError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is not null) return;

        var all = new Dictionary<string, string>(data.Shortcuts.All) { [trigger] = text };
        _app.SaveShortcuts(all);
        ShortcutTrigger.Clear();
        ShortcutText.Clear();
        ShortcutTrigger.Focus();
    }

    private void RemoveShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Data is not { } data || (sender as FrameworkElement)?.Tag is not string trigger) return;
        var all = new Dictionary<string, string>(data.Shortcuts.All);
        all.Remove(trigger);
        _app.SaveShortcuts(all);
    }

    // ---- Minhas palavras ----

    private void WordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddWord_Click(sender, e);
    }

    private void AddWord_Click(object sender, RoutedEventArgs e)
    {
        var word = WordBox.Text.Trim();
        if (word.Length == 0 || word.Any(char.IsWhiteSpace)) return;
        _app.AddWord(word);
        WordBox.Clear();
        WordBox.Focus();
    }

    private void RemoveWord_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string word) _app.RemoveWord(word);
    }

    // ---- Apps excluídos ----

    private void AppBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddApp_Click(sender, e);
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var name = AppBox.Text.Trim();
        if (name.Length == 0) return;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        var apps = _app.Settings.ExcludedApps;
        if (!apps.Contains(name, StringComparer.OrdinalIgnoreCase)) apps.Add(name);
        _app.ApplyOptions();
        AppBox.Clear();
        AppBox.Focus();
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string name) return;
        _app.Settings.ExcludedApps.RemoveAll(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        _app.ApplyOptions();
    }
}
