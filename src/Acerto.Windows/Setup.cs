using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace Acerto.Windows;

/// <summary>
/// O Acertô é o próprio instalador. Publicado como arquivo único (installer/gerar.ps1), o exe leva os dados
/// embutidos; aberto fora de uma pasta com data\ ao lado (ex.: Acerto-Setup-1.3.9.exe em Downloads) mostra a
/// janela de instalação, e instalado roda normal. Configurações &gt; Apps chama o exe com <see cref="UninstallArg"/>.
/// Instala por usuário, sem administrador. Os dados do usuário (%APPDATA%\Acerto) nunca são apagados.
/// </summary>
internal static class Setup
{
    public const string UninstallArg = "--desinstalar";
    private const string Payload = "payload/"; // nome lógico dos recursos embutidos (ver Acerto.Windows.csproj)
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Acerto";
    private const string InnoKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F0B2B7E-3C55-4E0B-9C0A-ACE27D0A0001}_is1"; // até a 1.3.9
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Acerto"; // o mesmo do interruptor "Iniciar com o Windows" (AppController)

    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Acerto");
    private static string InstalledExe => Path.Combine(InstallDir, "Acerto.exe");
    private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Acertô.lnk");
    private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Acertô.lnk");

    public static string Version => typeof(Setup).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>Este exe leva os dados embutidos e não está numa pasta de programa (com data\ ao lado).</summary>
    public static bool IsInstaller =>
        !Directory.Exists(Path.Combine(AppContext.BaseDirectory, "data"))
        && PayloadNames().Any(n => n.StartsWith(Payload + "data/", StringComparison.Ordinal));

    public static bool IsInstalled => File.Exists(InstalledExe);
    public static bool HasDesktopShortcut => File.Exists(DesktopLink);

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
    }

    private static IEnumerable<string> PayloadNames() =>
        typeof(Setup).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(Payload, StringComparison.Ordinal));

    /// <summary>Instala ou atualiza. Roda fora da thread da interface; <paramref name="progress"/> vai de 0 a 1.</summary>
    public static void Install(bool startWithWindows, bool desktopShortcut, IProgress<double> progress)
    {
        CloseRunning();
        Directory.CreateDirectory(InstallDir);
        // A versão anterior (Inno Setup) deixava ~270 arquivos soltos; agora é um exe e os dados. Começa limpo.
        foreach (var file in Directory.EnumerateFiles(InstallDir)) File.Delete(file);
        foreach (var dir in Directory.EnumerateDirectories(InstallDir)) Directory.Delete(dir, recursive: true);

        var asm = typeof(Setup).Assembly;
        var names = PayloadNames().ToList();
        int done = 0, total = names.Count + 1;
        File.Copy(Environment.ProcessPath!, InstalledExe, overwrite: true);
        progress.Report(++done / (double)total);
        foreach (var name in names)
        {
            var target = Path.Combine(InstallDir, name[Payload.Length..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var src = asm.GetManifestResourceStream(name)!)
            using (var dst = File.Create(target))
                src.CopyTo(dst);
            progress.Report(++done / (double)total);
        }

        CreateShortcut(StartMenuLink);
        if (desktopShortcut) CreateShortcut(DesktopLink);
        else File.Delete(DesktopLink);

        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (startWithWindows) run.SetValue(RunValue, $"\"{InstalledExe}\"");
            else run.DeleteValue(RunValue, throwOnMissingValue: false);
        }

        long sizeKb = new DirectoryInfo(InstallDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Acertô");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Acertô");
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" {UninstallArg}");
            key.SetValue("EstimatedSize", (int)Math.Min(sizeKb, int.MaxValue), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        Registry.CurrentUser.DeleteSubKeyTree(InnoKey, throwOnMissingSubKey: false);
    }

    /// <summary>Abre o Acertô instalado (fica na bandeja, perto do relógio).</summary>
    public static void Launch() => Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });

    /// <summary>Chamado por Configurações &gt; Apps &gt; Desinstalar.</summary>
    public static void Uninstall()
    {
        const string title = "Acertô";
        if (MessageBox.Show("Remover o Acertô deste computador?\n\nSuas palavras, atalhos e configurações ficam guardados para uma próxima instalação.",
                title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        CloseRunning();
        File.Delete(StartMenuLink);
        File.Delete(DesktopLink);
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            run?.DeleteValue(RunValue, throwOnMissingValue: false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(InnoKey, throwOnMissingSubKey: false);

        // O exe em uso não pode se apagar: um cmd escondido espera ele fechar e remove a pasta.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        MessageBox.Show("O Acertô foi removido.", title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Fecha o Acertô que estiver rodando (fica na bandeja), menos este processo.</summary>
    private static void CloseRunning()
    {
        foreach (var p in Process.GetProcessesByName("Acerto"))
        {
            using (p)
            {
                if (p.Id == Environment.ProcessId) continue;
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (InvalidOperationException) { } // já tinha fechado
            }
        }
    }

    private static void CreateShortcut(string path)
    {
        // WScript.Shell: o jeito mais curto de criar .lnk sem dependência
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = ((dynamic)shell).CreateShortcut(path);
        link.TargetPath = InstalledExe;
        link.WorkingDirectory = InstallDir;
        link.IconLocation = InstalledExe + ",0";
        link.Description = "Corretor automático em português";
        link.Save();
    }
}
