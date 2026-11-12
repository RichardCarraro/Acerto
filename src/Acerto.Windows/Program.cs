using Acerto.Core;

namespace Acerto.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Acerto.SingleInstance", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();

        var settings = AppSettings.Load();
        var data = CoreData.Load(Path.Combine(AppContext.BaseDirectory, "data"), AppSettings.UserDir);
        var engine = new TextEngine(new Corrector(data), data.Personal);
        var fg = new ForegroundInfo();
        using var hooks = new InputHooks(engine, settings, fg);
        hooks.Install();

        Application.Run(new ApplicationContext());
    }
}
