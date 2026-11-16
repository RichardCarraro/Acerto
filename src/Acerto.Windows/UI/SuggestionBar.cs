using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Acerto.Windows.UI;

/// <summary>
/// Barrinha de sugestões perto do cursor, como a do teclado do celular: até três palavras, a primeira em destaque
/// com a dica "Tab". Não recebe clique nem foco; quem aceita é a tecla Tab, tratada no hook.
/// </summary>
internal sealed class SuggestionBar : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = -1;

    private readonly Border _root;
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal };
    private nint _hwnd;

    /// <summary>A barra está visível para a pessoa (só então o Tab aceita a sugestão).</summary>
    public bool IsShowing { get; private set; }

    public SuggestionBar()
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
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;

        _root = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 6), BorderThickness = new Thickness(1), Child = _items, Opacity = 0 };
        _root.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        _root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        Content = _root;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // clique atravessa, nunca ganha foco, não aparece no Alt+Tab
        SetWindowLongW(_hwnd, GWL_EXSTYLE, GetWindowLongW(_hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public void SetItems(IReadOnlyList<string> suggestions)
    {
        _items.Children.Clear();
        for (int i = 0; i < suggestions.Count; i++)
        {
            var text = new TextBlock { Text = suggestions[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(i == 0 ? 0 : 14, 0, 0, 0) };
            if (i == 0)
            {
                text.FontWeight = FontWeights.SemiBold;
                text.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            }
            else
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            }
            _items.Children.Add(text);

            if (i == 0)
            {
                var hint = new TextBlock { Text = "Tab", FontSize = 10, Margin = new Thickness(5, 1, 5, 1) };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                var key = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = hint };
                key.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                _items.Children.Add(key);
            }
        }
    }

    /// <param name="anchor">Retângulo (pixels) da palavra em andamento, ou do cursor; a barra aparece logo abaixo.</param>
    public void ShowAt(Rect anchor, IReadOnlyList<string> suggestions)
    {
        SetItems(suggestions);
        if (!IsVisible) Show(); // ShowActivated=false: não rouba o foco
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.Left, (int)anchor.Bottom)).WorkingArea;
        int x = Math.Max(area.Left + 4, Math.Min((int)anchor.Left, area.Right - width - 4));
        int y = (int)anchor.Bottom + 6;
        if (y + height > area.Bottom) y = (int)anchor.Top - height - 6; // sem espaço embaixo: mostra em cima
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        // surge em 90 ms quando aparece; trocando de sugestão a cada tecla fica parada (senão piscaria)
        if (!IsShowing && SystemParameters.ClientAreaAnimation)
            _root.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90)));
        else
        {
            _root.BeginAnimation(OpacityProperty, null);
            _root.Opacity = 1;
        }
        IsShowing = true;
    }

    /// <summary>Some da tela. A janela continua existindo (esconder e mostrar de novo faria ela piscar).</summary>
    public void Conceal()
    {
        _root.BeginAnimation(OpacityProperty, null);
        _root.Opacity = 0;
        IsShowing = false;
    }

    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
