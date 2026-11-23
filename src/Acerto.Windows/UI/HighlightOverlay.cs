using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Acerto.Windows.UI;

/// <summary>
/// Animação da palavra corrigida, como no teclado do celular: um fundo translúcido aparece sobre a palavra
/// e some em meio segundo; o sublinhado fica um pouco mais. É uma janela transparente por cima do texto,
/// que não recebe clique nem foco.
/// </summary>
internal sealed class HighlightOverlay : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = -1;

    private readonly Border _fill, _line;
    private readonly ScaleTransform _pop = new();
    private nint _hwnd;

    public HighlightOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = 1;

        // Cor de destaque do tema (Fluent): tom mais escuro no tema claro e mais claro no escuro, onde o tom puro some no
        // fundo; acompanha sozinha a troca de cor ou de tema com o app aberto. O fundo fica translúcido pela opacidade
        // fixa do Grid em volta; a animação mexe só na opacidade do próprio realce.
        _fill = new Border { CornerRadius = new CornerRadius(4) };
        _fill.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        _line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1) };
        _line.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        _fill.Opacity = 0;
        _line.Opacity = 0;
        _fill.RenderTransformOrigin = new Point(0.5, 0.5);
        _fill.RenderTransform = _pop;
        Content = new Grid { Children = { new Grid { Opacity = 0.35, Children = { _fill } }, _line } };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // clique atravessa a janela, ela nunca ganha foco e não aparece no Alt+Tab
        SetWindowLongW(_hwnd, GWL_EXSTYLE, GetWindowLongW(_hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    /// <param name="word">Retângulo da palavra na tela, em pixels.</param>
    public void Flash(Rect word)
    {
        if (word.Width < 2 || word.Height < 2) return;
        // palavra de uma letra (é, à, a): realce estreito some no texto; dá uma largura mínima, centrada
        double minWidth = word.Height * 0.5;
        if (word.Width < minWidth) word = new Rect(word.X - (minWidth - word.Width) / 2, word.Y, minWidth, word.Height);
        // A janela nunca é escondida (esconder e mostrar de novo faz ela piscar na posição antiga):
        // parada, ela fica 100% transparente e deixa o clique passar.
        if (!IsVisible) Show(); // ShowActivated=false: não rouba o foco de quem está digitando
        SetWindowPos(_hwnd, HWND_TOPMOST, (int)word.X - 3, (int)word.Y - 1, (int)word.Width + 6, (int)word.Height + 3, SWP_NOACTIVATE);

        // "Efeitos de animação" desligado no Windows: só acende e apaga, sem crescer
        bool motion = SystemParameters.ClientAreaAnimation;
        var fadeIn = TimeSpan.FromMilliseconds(motion ? 70 : 0);

        // fundo: acende em 70 ms crescendo um pouco (como no celular), segura e some em 400 ms
        _fill.BeginAnimation(OpacityProperty, Fade(fadeIn, 220, 620));
        var grow = new DoubleAnimation(motion ? 0.94 : 1, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        _pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        // sublinhado: fica 900 ms e some
        _line.BeginAnimation(OpacityProperty, Fade(fadeIn, 900, 1250));
    }

    private static DoubleAnimationUsingKeyFrames Fade(TimeSpan fadeIn, int holdUntilMs, int goneAtMs)
    {
        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(fadeIn), new CubicEase { EasingMode = EasingMode.EaseOut }));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(holdUntilMs))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(goneAtMs)), new QuadraticEase()));
        return a;
    }

    /// <summary>Apaga na hora (a pessoa clicou em outro lugar, mudou de linha ou de janela: o texto não está mais ali).</summary>
    public void Cancel()
    {
        _fill.BeginAnimation(OpacityProperty, null);
        _line.BeginAnimation(OpacityProperty, null);
        _fill.Opacity = 0;
        _line.Opacity = 0;
    }

    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
