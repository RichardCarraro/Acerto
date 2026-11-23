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

        // palavra em pedaços na mesma linha ("q" + "uanto"): o realce cobre tudo; quebrada em duas linhas: só a última
        var joined = CaretContext.LastLine([new Rect(100, 10, 8, 20), new Rect(108, 10, 40, 20)]);
        if (joined != new Rect(100, 10, 48, 20)) throw new InvalidOperationException($"realce sem o começo da palavra: {joined}");
        var wrapped = CaretContext.LastLine([new Rect(300, 10, 30, 20), new Rect(10, 34, 20, 20)]);
        if (wrapped != new Rect(10, 34, 20, 20)) throw new InvalidOperationException($"realce juntou duas linhas: {wrapped}");

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
                        if (page == "home") // moldura de foco (Tab) no menu lateral e no 1º interruptor, sem tirar o foco de ninguém
                            Save(w, Path.Combine(outDir, "main-home-foco.png"), First<RadioButton>(w), First<CheckBox>(w));
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
        // no tema escuro, a frase num app escuro (fundo #202020, letra branca): é onde o realce translúcido some mais
        bool dark = Application.Current.ThemeMode == ThemeMode.Dark;
        var ink = dark ? Brushes.White : Brushes.Black;
        var text = new TextBlock { Text = before + word + after, FontFamily = font, FontSize = 22, Margin = new Thickness(24, 28, 0, 0), Foreground = ink, VerticalAlignment = VerticalAlignment.Top };
        var page = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = -30000, Top = 0, Width = 460, Height = 120,
            ShowActivated = false, ShowInTaskbar = false, Background = dark ? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) : Brushes.White, Content = text,
        };
        page.Show();
        await Task.Delay(300);
        double scale = VisualTreeHelper.GetDpi(page).DpiScaleX;
        double Width(string s) => new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 22, ink, scale).WidthIncludingTrailingWhitespace;
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
        // mesma palavra, mais uma letra e sugestões mais curtas: a barra não pode encolher (pulava a cada letra)
        bar.ShowAt(new Rect(0, 0, 10, 10), ["obrigado", "obrigada", "obrigação"]);
        await Task.Delay(150);
        double wide = bar.ActualWidth;
        bar.ShowAt(new Rect(0, 0, 10, 10), ["obrigado"], sameWord: true);
        await Task.Delay(150);
        Save(bar, Path.Combine(outDir, "barra-mesma-palavra.png"));
        if (bar.ActualWidth < wide - 0.5) throw new InvalidOperationException($"a barra encolheu na mesma palavra: {wide:F0} → {bar.ActualWidth:F0}");
        // saída: no meio dos 80 ms ainda aparece, um pouco apagada
        bar.Conceal();
        await Task.Delay(40);
        Save(bar, Path.Combine(outDir, "barra-saindo.png"));
        bar.Close();
        overlay.Close();
        page.Close();
    }

    private static T? First<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            if (First<T>(c) is { } found) return found;
        }
        return null;
    }

    /// <summary>Desenha a janela sobre a cor de fundo do tema (o Mica/acrílico de verdade só existe na tela).</summary>
    /// <param name="focused">Controles que ganham a moldura de foco desenhada por cima (o FocusVisualStyle deles), como no Tab.</param>
    private static void Save(Window w, string path, params FrameworkElement?[] focused)
    {
        int width = (int)w.ActualWidth, height = (int)w.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = w.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            dc.DrawRectangle(bg, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(w) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
            foreach (var el in focused)
            {
                if (el is null) continue;
                var style = el.FocusVisualStyle ?? throw new InvalidOperationException($"{el.GetType().Name} sem moldura de foco");
                const double pad = 6; // a moldura passa um pouco da borda do controle
                var box = new Grid { Width = el.ActualWidth + 2 * pad, Height = el.ActualHeight + 2 * pad };
                box.Children.Add(new Control { Style = style, Margin = new Thickness(pad) });
                box.Measure(new Size(box.Width, box.Height));
                box.Arrange(new Rect(0, 0, box.Width, box.Height));
                box.UpdateLayout();
                var at = el.TranslatePoint(new Point(-pad, -pad), w);
                dc.DrawRectangle(new VisualBrush(box) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, box.Width, box.Height) },
                    null, new Rect(at, new Size(box.Width, box.Height)));
            }
        }
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
