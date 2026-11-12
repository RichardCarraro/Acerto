using System.Text.Json;

namespace Acerto.Windows;

/// <summary>Configurações do usuário em %APPDATA%\Acerto\config.json.</summary>
internal sealed class AppSettings
{
    public static readonly string UserDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Acerto");

    private static string FilePath => Path.Combine(UserDir, "config.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public bool Paused { get; set; }
    public bool StartWithWindows { get; set; }
    public bool WelcomeShown { get; set; }

    /// <summary>Editores de código e terminais vêm desligados por padrão.</summary>
    public List<string> ExcludedApps { get; set; } =
    [
        "Code.exe", "devenv.exe", "Cursor.exe", "idea64.exe", "rider64.exe", "pycharm64.exe", "phpstorm64.exe",
        "webstorm64.exe", "studio64.exe", "sublime_text.exe", "notepad++.exe", "WindowsTerminal.exe", "wt.exe",
        "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe", "mintty.exe", "OpenConsole.exe", "powershell_ise.exe",
        "putty.exe", "goland64.exe", "clion64.exe", "datagrip64.exe", "rubymine64.exe", "mstsc.exe",
    ];

    public bool AutoCapitalize { get; set; } = true;
    public bool DoubleSpacePeriod { get; set; } = true;
    public bool CorrectionAnimation { get; set; } = true;

    /// <summary>Quantas palavras o Acertô já corrigiu (só o número, nunca o texto).</summary>
    public long CorrectionCount { get; set; }

    public bool IsExcluded(string processName) =>
        ExcludedApps.Any(a => string.Equals(a, processName, StringComparison.OrdinalIgnoreCase));

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings(); // arquivo corrompido ou travado: usa o padrão sem derrubar o programa
        }
        var s = new AppSettings();
        try { s.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(UserDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
