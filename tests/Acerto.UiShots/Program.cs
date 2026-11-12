using System.Windows;
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
                        w.Close();
                    }
                    var f = new TrayFlyout(controller) { PlaceAtTray = false, Left = -30000, Top = 0, ShowActivated = false };
                    f.Show();
                    await Task.Delay(500);
                    Save(f, Path.Combine(outDir, "flyout.png"));
                    f.Close();
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
