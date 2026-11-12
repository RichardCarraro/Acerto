using System.Windows;
using System.Windows.Media;

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

    /// <summary>Canto inferior direito da área de trabalho do monitor onde está o mouse (onde fica a bandeja).</summary>
    private void PlaceNearTray()
    {
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea; // pixels
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = area.Right / dpi.DpiScaleX - ActualWidth - 12;
        Top = area.Bottom / dpi.DpiScaleY - ActualHeight - 12;
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
