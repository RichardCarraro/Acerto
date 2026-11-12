using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Acerto.Windows.UI;

/// <summary>Painel que abre ao clicar no ícone da bandeja. Fecha sozinho quando perde o foco.</summary>
internal partial class TrayFlyout : Window
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x63, 0xD6));
    private static readonly Brush PausedBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private readonly AppController _app;
    private bool _closing;

    /// <summary>Falso só na geração de imagens das telas, para a janela não aparecer na tela do usuário.</summary>
    internal bool PlaceAtTray { get; init; } = true;

    public TrayFlyout(AppController app)
    {
        _app = app;
        InitializeComponent();

        SourceInitialized += (_, _) => Dwm.ApplyBackdrop(this, Dwm.Backdrop.Acrylic);
        Loaded += (_, _) => { if (PlaceAtTray) PlaceNearTray(); };
        Deactivated += (_, _) => { if (PlaceAtTray) CloseOnce(); };
        _app.StateChanged += Refresh;
        Closed += (_, _) => _app.StateChanged -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        bool ready = _app.Ready, active = ready && !_app.Settings.Paused;
        StatusDot.Background = active ? ActiveBrush : PausedBrush;
        StatusTitle.Text = !ready ? "Carregando…" : active ? "Acertô ativo" : "Acertô pausado";
        StatusHint.Text = !ready ? "Abrindo o dicionário" : $"{_app.Settings.CorrectionCount:N0} palavras corrigidas";
        ActiveSwitch.IsEnabled = ready;
        ActiveSwitch.IsChecked = active;
    }

    /// <summary>
    /// Abre logo acima da barra de tarefas, centralizado no ícone clicado (sem sair da tela),
    /// subindo e aparecendo como os painéis do Windows 11.
    /// </summary>
    private void PlaceNearTray()
    {
        var cursor = System.Windows.Forms.Cursor.Position;                       // pixels
        var area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;    // pixels, sem a barra de tarefas
        var dpi = VisualTreeHelper.GetDpi(this);
        const double margin = 12;
        double left = cursor.X / dpi.DpiScaleX - ActualWidth / 2;
        double minLeft = area.Left / dpi.DpiScaleX + margin, maxLeft = area.Right / dpi.DpiScaleX - ActualWidth - margin;
        Left = Math.Max(minLeft, Math.Min(left, maxLeft));
        double top = area.Bottom / dpi.DpiScaleY - ActualHeight - margin;
        Top = top;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(TopProperty, new DoubleAnimation(top + 28, top, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void ActiveSwitch_Click(object sender, RoutedEventArgs e) => _app.SetPaused(ActiveSwitch.IsChecked != true);

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var page = (sender as FrameworkElement)?.Tag as string ?? "home";
        CloseOnce();
        _app.OpenMainWindow(page);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        CloseOnce();
        _app.Exit();
    }
}
