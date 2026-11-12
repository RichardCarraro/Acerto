using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Acerto.Windows.UI;

/// <summary>Efeitos de janela do Windows 11: fundo translúcido (Mica/acrílico), cantos arredondados, modo escuro.</summary>
internal static class Dwm
{
    public enum Backdrop { Mica = 2, Acrylic = 3 }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;

    /// <summary>Windows 11 22H2 em diante tem os fundos translúcidos do sistema.</summary>
    public static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22621;

    public static bool IsDarkTheme
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
    }

    /// <summary>
    /// Deixa o fundo da janela por conta do Windows. Chamar depois que a janela tem handle (SourceInitialized).
    /// Em sistemas sem suporte, a janela fica com a cor sólida do tema.
    /// </summary>
    public static void ApplyBackdrop(Window window, Backdrop backdrop)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;

        int dark = IsDarkTheme ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        if (!SupportsBackdrop)
        {
            window.SetResourceReference(Window.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
            return;
        }

        window.Background = Brushes.Transparent;
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int type = (int)backdrop;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);
}
