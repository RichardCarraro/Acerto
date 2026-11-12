namespace Acerto.Windows;

/// <summary>
/// Diagnóstico opcional (variável de ambiente ACERTO_DIAG = caminho do arquivo).
/// Grava só estados e contagens — NUNCA o texto digitado.
/// </summary>
internal static class Diag
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("ACERTO_DIAG");
    public static bool On => Path is not null;

    public static void Log(string state)
    {
        if (Path is null) return;
        try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {state}\n"); }
        catch (IOException) { }
    }
}
