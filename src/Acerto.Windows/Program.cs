using System.Windows;

namespace Acerto.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Acerto.SingleInstance", out bool first);
        if (!first) return;

        // O laço de mensagens é do WPF; o WinForms (ícone da bandeja) não pode trocar o contexto de sincronização.
        System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;

        var app = CreateApplication();
        // Um erro numa janela não pode derrubar o corretor.
        app.DispatcherUnhandledException += (_, e) => e.Handled = true;
        _ = new AppController(app);
        app.Run();
    }

    /// <summary>Aplicação WPF com o tema do Windows 11 (claro/escuro conforme o sistema) e os estilos do Acertô.</summary>
    internal static Application CreateApplication()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.ThemeMode = ThemeMode.System;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Acerto;component/UI/Styles.xaml"),
        });
        return app;
    }
}
