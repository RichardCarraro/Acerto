using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Acerto.Windows.UI;

/// <summary>Menu que abre ao clicar no ícone da bandeja. Fecha sozinho quando perde o foco.</summary>
internal partial class TrayFlyout : Window
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x63, 0xD6));
    private static readonly Brush PausedBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private readonly AppController _app;
    private readonly System.Drawing.Point _cursor;
    private bool _closing;

    /// <summary>Falso só na geração de imagens das telas, para a janela não aparecer na tela do usuário.</summary>
    internal bool PlaceAtTray { get; init; } = true;

    public TrayFlyout(AppController app)
    {
        _app = app;
        _cursor = System.Windows.Forms.Cursor.Position; // posição do clique, antes de esta janela existir
        InitializeComponent();

        SourceInitialized += (_, _) => Dwm.ApplyBackdrop(this, Dwm.Backdrop.Acrylic);
        Loaded += (_, _) => { if (PlaceAtTray) PlaceAtCursor(); };
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
    /// Abre como um menu de contexto do Windows: o canto de baixo à esquerda fica no ponto do clique,
    /// subindo e indo para a direita; se não couber, vira para o outro lado. Nunca sai da área de trabalho.
    /// </summary>
    private void PlaceAtCursor()
    {
        var area = System.Windows.Forms.Screen.FromPoint(_cursor).WorkingArea; // pixels, sem a barra de tarefas
        var dpi = VisualTreeHelper.GetDpi(this);
        double x = _cursor.X / dpi.DpiScaleX, y = _cursor.Y / dpi.DpiScaleY;
        double areaLeft = area.Left / dpi.DpiScaleX, areaRight = area.Right / dpi.DpiScaleX;
        double areaTop = area.Top / dpi.DpiScaleY, areaBottom = area.Bottom / dpi.DpiScaleY;
        const double gap = 2;

        double left = x + ActualWidth <= areaRight ? x : x - ActualWidth;      // direita do clique, ou esquerda se não couber
        Left = Math.Max(areaLeft, Math.Min(left, areaRight - ActualWidth));
        double top = Math.Min(y, areaBottom) - ActualHeight - gap;              // acima do clique (e da barra de tarefas)
        if (top < areaTop) top = Math.Min(y + gap, areaBottom - ActualHeight);  // não coube acima: abre abaixo
        Top = top;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(TopProperty, new DoubleAnimation(top + 10, top, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
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
