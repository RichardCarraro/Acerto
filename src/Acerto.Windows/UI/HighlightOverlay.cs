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
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private static readonly nint HWND_TOPMOST = -1;

    private readonly Border _fill, _line;
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

        var accent = SystemColors.AccentColor;
        _fill = new Border { CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0x59, accent.R, accent.G, accent.B)) };
        _line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(1) };
        Content = new Grid { Children = { _fill, _line } };
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
        if (!IsVisible) Show(); // ShowActivated=false: não rouba o foco de quem está digitando
        SetWindowPos(_hwnd, HWND_TOPMOST, (int)word.X - 3, (int)word.Y - 1, (int)word.Width + 6, (int)word.Height + 3,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        // fundo: aparece na hora, segura 200 ms e some em 450 ms
        var fill = new DoubleAnimationUsingKeyFrames();
        fill.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fill.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        fill.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650)), new QuadraticEase()));
        _fill.BeginAnimation(OpacityProperty, fill);

        // sublinhado: fica 1,2 s e some
        var line = new DoubleAnimationUsingKeyFrames();
        line.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        line.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));
        line.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600)), new QuadraticEase()));
        line.Completed += (_, _) => { if (_line.Opacity == 0) Hide(); };
        _line.BeginAnimation(OpacityProperty, line);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
