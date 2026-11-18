using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Acerto.Windows;
using Acerto.Windows.UI;

// uso: AcertoUiShots <pasta-saida> [light|dark]
internal static class Shots
{
    [STAThread]
    private static void Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : "shots";
        Directory.CreateDirectory(outDir);

        var app = Program.CreateApplication();
        if (args.Length > 1) app.ThemeMode = args[1] == "dark" ? ThemeMode.Dark : ThemeMode.Light;
        var controller = new AppController(app, preview: true);

        bool started = false;
        controller.StateChanged += () =>
        {
            if (started || !controller.Ready) return;
            started = true;
            app.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    foreach (var page in new[] { "home", "shortcuts", "words", "apps", "support" })
                    {
                        var w = new MainWindow(controller) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
                        w.ShowPage(page);
                        w.Show();
                        await Task.Delay(500);
                        Save(w, Path.Combine(outDir, $"main-{page}.png"));
                        if (page == "words" && w.FindName("WordBox") is TextBox box) // a dica some quando há texto
                        {
                            box.Text = "Acertô";
                            await Task.Delay(200);
                            Save(w, Path.Combine(outDir, "main-words-com-texto.png"));
                        }
                        w.Close();
                    }
                    var f = new TrayFlyout(controller) { PlaceAtTray = false, Left = -30000, Top = 0, ShowActivated = false };
                    f.Show();
                    await Task.Delay(500);
                    Save(f, Path.Combine(outDir, "flyout.png"));
                    f.Close();
                    await Correction(outDir);
                    var setup = new SetupWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
                    setup.Show();
                    await Task.Delay(500);
                    Save(setup, Path.Combine(outDir, "instalador.png"));
                    setup.Close();
                }
                catch (Exception e)
                {
                    File.WriteAllText(Path.Combine(outDir, "erro.txt"), e.ToString());
                }
                app.Shutdown();
            });
        };
        app.Run();
    }

    /// <summary>
    /// Animação da palavra corrigida e barra de sugestões, as de verdade, sobre um texto de exemplo fora da tela.
    /// Salva quadros em vários instantes (correcao-0000ms.png...) para ver a entrada, o auge e a saída.
    /// </summary>
    private static async Task Correction(string outDir)
    {
        const string before = "Isso ", word = "é", after = " muito importante";
        var font = new FontFamily("Segoe UI");
        var text = new TextBlock { Text = before + word + after, FontFamily = font, FontSize = 22, Margin = new Thickness(24, 28, 0, 0), Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Top };
        var page = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = -30000, Top = 0, Width = 460, Height = 120,
            ShowActivated = false, ShowInTaskbar = false, Background = Brushes.White, Content = text,
        };
        page.Show();
        await Task.Delay(300);
        double scale = VisualTreeHelper.GetDpi(page).DpiScaleX;
        double Width(string s) => new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 22, Brushes.Black, scale).WidthIncludingTrailingWhitespace;
        var wordDip = new Rect(page.Left + 24 + Width(before), page.Top + 28, Width(word), text.ActualHeight);
        var wordPx = new Rect(wordDip.X * scale, wordDip.Y * scale, wordDip.Width * scale, wordDip.Height * scale);

        var overlay = new HighlightOverlay();
        var bar = new SuggestionBar();
        var start = DateTime.Now;
        overlay.Flash(wordPx);
        foreach (int ms in (int[])[0, 35, 70, 120, 200, 350, 500, 650, 900, 1150])
        {
            var wait = start.AddMilliseconds(ms) - DateTime.Now;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            int width = (int)page.ActualWidth, height = (int)page.ActualHeight;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(page), null, new Rect(0, 0, width, height));
                // a janela da animação fica 3 px à esquerda e 1 px acima da palavra (HighlightOverlay.Flash)
                var o = new Rect(wordDip.X - page.Left - 3 / scale, wordDip.Y - page.Top - 1 / scale, overlay.ActualWidth, overlay.ActualHeight);
                var content = (FrameworkElement)overlay.Content;
                o.Size = new Size(content.ActualWidth, content.ActualHeight);
                dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, o);
            }
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(outDir, $"correcao-{ms:0000}ms.png"));
            enc.Save(fs);
        }

        // barra logo depois da correção: o que foi digitado (Tab desfaz) e alternativas
        bar.ShowAt(new Rect(0, 0, 10, 10), ["e", "ele", "era"]);
        await Task.Delay(300);
        Save(bar, Path.Combine(outDir, "barra-sugestoes.png"));
        bar.Close();
        overlay.Close();
        page.Close();
    }

    /// <summary>Desenha a janela sobre a cor de fundo do tema (o Mica/acrílico de verdade só existe na tela).</summary>
    private static void Save(Window w, string path)
    {
        int width = (int)w.ActualWidth, height = (int)w.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = w.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            dc.DrawRectangle(bg, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(w) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
        }
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
