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
        "cmd.exe", "powershell.exe", "pwsh.exe", "conhost.exe", "mintty.exe",
    ];

    public bool IsExcluded(string processName) =>
        ExcludedApps.Any(a => string.Equals(a, processName, StringComparison.OrdinalIgnoreCase));

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // arquivo corrompido: volta ao padrão sem derrubar o programa
        }
        var s = new AppSettings();
        s.Save();
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(UserDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
