// Uso: AcertoBancada.exe [frases=12] [semente=N] [alvo=caixa|chrome|edge] [saida=bancada.txt] [rapido=1]
// Com o Acertô rodando: sorteia frases, estraga palavras (acento esquecido, letra trocada, faltando, invertida...),
// digita tecla por tecla como uma pessoa (teclas mortas para acento, ritmo variável) e compara:
//  - o texto que ficou no app com o motor do Acertô rodando aqui dentro (gabarito): diferença = problema no app
//    (parou de corrigir, perdeu tecla, injetou errado);
//  - o gabarito com a frase original: qualidade da correção (corrigiu, errou, deixou passar, estragou palavra certa).
// Usa as configurações e as palavras do usuário (%APPDATA%\Acerto) só para leitura.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Acerto.Bancada;
using Acerto.Core;

var opt = args.Select(a => a.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
int count = int.Parse(opt.GetValueOrDefault("frases", "12"));
int seed = opt.TryGetValue("semente", out var s) ? int.Parse(s) : Environment.TickCount & 0xFFFF;
string target = opt.GetValueOrDefault("alvo", "caixa");
string output = opt.GetValueOrDefault("saida", "bancada.txt");
bool fast = opt.GetValueOrDefault("rapido") == "1";
var rng = new Random(seed);

// ---- gabarito: o mesmo motor, com as mesmas opções e palavras do usuário ----
var userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Acerto");
var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(userDir, "config.json"))).RootElement;
bool Cfg(string name, bool def) => config.TryGetProperty(name, out var v) ? v.GetBoolean() : def;
var data = CoreData.Load(FindDataDir(), userDir);
var corrector = new Corrector(data, Cfg("CorrectMore", true) ? TypoOptions.Aggressive : TypoOptions.Careful)
{
    LearnFromTyping = Cfg("LearnFromTyping", true),
};
var oracle = new TextEngine(corrector, _ => { })
{
    AutoCapitalize = Cfg("AutoCapitalize", true),
    DoubleSpacePeriod = Cfg("DoubleSpacePeriod", true),
};

// ---- frases e erros ----
var originals = Frases.Todas.OrderBy(_ => rng.Next()).Take(count).ToList();
var injected = new List<List<(string Original, string Typed, string Kind)>>();
var typedLines = originals.Select(o =>
{
    var errs = new List<(string, string, string)>();
    injected.Add(errs);
    return Spoil(o, rng, errs);
}).ToList();

ApplicationConfiguration.Initialize();
var form = new Form { Text = "Acertô — bancada", Width = 900, Height = 420, TopMost = true, StartPosition = FormStartPosition.CenterScreen };
var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12), ScrollBars = ScrollBars.Vertical };
form.Controls.Add(box);
string real = "";
var oracleDoc = new StringBuilder();
var stamps = new List<DateTime>();
Process? browser = null;

form.Shown += async (_, _) =>
{
    if (target == "caixa")
    {
        form.Activate();
        box.Focus();
    }
    else
    {
        form.WindowState = FormWindowState.Minimized;
        var exe = target == "edge" ? @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
            : @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        var page = "data:text/html;charset=utf-8,<title>Acerto bancada</title><textarea autofocus style='width:95vw;height:90vh;font-size:18px'></textarea>";
        browser = Process.Start(exe, $"--new-window \"{page}\"");
        await Task.Delay(4000);
    }
    await Task.Delay(1200); // o Acertô confere o campo (senha? texto antes do cursor?) em segundo plano

    oracle.ConfirmContext(oracle.Generation, "");
    await Task.Run(() =>
    {
        var hkl = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
        for (int li = 0; li < typedLines.Count; li++)
        {
            stamps.Add(DateTime.Now);
            var line = typedLines[li] + (li < typedLines.Count - 1 ? "\n" : " ");
            foreach (var c in line)
            {
                TypeChar(c, hkl);
                var act = oracle.OnChar(c);
                if (act.Swallow)
                {
                    oracleDoc.Length -= act.Backspaces;
                    oracleDoc.Append(act.Insert);
                }
                else oracleDoc.Append(c);
                int wait = fast ? 15 : rng.Next(35, 120);
                if (c == ' ' && !fast && rng.NextDouble() < 0.12) wait += rng.Next(300, 900); // pensando
                Thread.Sleep(wait);
            }
        }
        Thread.Sleep(600);
    });

    try
    {
        if (target == "caixa") real = box.Text;
        else
        {
            Chord(VK_CONTROL, 'A');
            Thread.Sleep(150);
            Chord(VK_CONTROL, 'C');
            Thread.Sleep(300);
            var sta = new Thread(() => real = Clipboard.ContainsText() ? Clipboard.GetText() : ""); // área de transferência exige STA
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();
            Chord(VK_CONTROL, 'W');
        }
        Report();
    }
    catch (Exception e)
    {
        File.WriteAllText(output, $"semente={seed} FALHA NA BANCADA: {e}");
    }
    form.Close();
};
Application.Run(form);

// ---------------------------------------------------------------------------------------------------------------

void Report()
{
    // gabarito: o que o motor faria com as mesmas teclas (texto resultante reconstruído)
    var realLines = real.Replace("\r\n", "\n").Split('\n');
    var oracleLines = oracleDoc.ToString().Split('\n');
    var sb = new StringBuilder();
    int hostDiff = 0, fixedOk = 0, wrong = 0, missed = 0, spoiled = 0, errors = 0;
    var detail = new StringBuilder();
    for (int i = 0; i < originals.Count; i++)
    {
        string orig = originals[i], typed = typedLines[i];
        string want = i < oracleLines.Length ? oracleLines[i].TrimEnd() : "";
        string got = i < realLines.Length ? realLines[i].TrimEnd() : "(faltou)";
        bool same = want == got;
        if (!same) hostDiff++;
        detail.AppendLine($"[{stamps[i]:HH:mm:ss.fff}] {(same ? "app=gabarito" : "APP DIFERENTE DO GABARITO")}");
        detail.AppendLine($"   original: {orig}");
        detail.AppendLine($"   digitado: {typed}");
        detail.AppendLine($"   gabarito: {want}");
        if (!same) detail.AppendLine($"   app:      {got}");

        var o = orig.Split(' '); var t = typed.Split(' '); var g = want.Split(' ');
        if (o.Length != g.Length || o.Length != t.Length) { detail.AppendLine("   (contagem de palavras mudou; qualidade não medida)"); continue; }
        for (int w = 0; w < o.Length; w++)
        {
            if (t[w] != o[w])
            {
                errors++;
                if (g[w] == o[w]) fixedOk++;
                else if (g[w] == t[w]) { missed++; detail.AppendLine($"   deixou passar: {t[w]} (era {o[w]}) [{Kind(i, o[w])}]"); }
                else { wrong++; detail.AppendLine($"   CORRIGIU ERRADO: {t[w]} → {g[w]} (era {o[w]}) [{Kind(i, o[w])}]"); }
            }
            else if (g[w] != o[w]) { spoiled++; detail.AppendLine($"   ESTRAGOU: {o[w]} → {g[w]}"); }
        }
    }
    sb.AppendLine($"Bancada {DateTime.Now:yyyy-MM-dd HH:mm} alvo={target} semente={seed} frases={originals.Count}");
    sb.AppendLine($"App diferente do gabarito: {hostDiff} de {originals.Count} linhas");
    sb.AppendLine($"Palavras com erro: {errors} | corrigidas certo: {fixedOk} | corrigidas errado: {wrong} | deixou passar: {missed} | palavras certas estragadas: {spoiled}");
    sb.AppendLine();
    sb.Append(detail);
    File.WriteAllText(output, sb.ToString());
}

string Kind(int line, string original) => injected[line].FirstOrDefault(e => e.Original == original).Kind ?? "?";

// ---- erros sorteados ----

static string Spoil(string sentence, Random r, List<(string, string, string)> log)
{
    var words = sentence.Split(' ');
    for (int i = 0; i < words.Length; i++)
    {
        var w = words[i];
        int a = 0, b = w.Length;
        while (a < b && !char.IsLetter(w[a])) a++;
        while (b > a && !char.IsLetter(w[b - 1])) b--;
        var core = w[a..b];
        if (core.Length == 0 || core.Contains('-')) continue;
        string? spoiled = null, kind = null;
        var plain = TextUtil.StripAccents(core);
        if (plain != core && r.NextDouble() < 0.6) (spoiled, kind) = (plain, "acento");
        else if (core.Length >= 4 && r.NextDouble() < 0.22) (spoiled, kind) = Typo(plain != core && r.NextDouble() < 0.5 ? plain : core, r);
        if (spoiled is null || spoiled == core) continue;
        words[i] = w[..a] + spoiled + w[b..];
        log.Add((w, words[i], kind!));
    }
    return string.Join(' ', words);
}

static (string, string) Typo(string w, Random r)
{
    int p = r.Next(1, w.Length); // a 1ª letra quase nunca sai errada
    switch (r.Next(5))
    {
        case 0 when p < w.Length - 1 && w[p] != w[p + 1]:
            return (w[..p] + w[p + 1] + w[p] + w[(p + 2)..], "inversão");
        case 1 when Neighbors(w[p]) is { Length: > 0 } n:
            return (w[..p] + n[r.Next(n.Length)] + w[(p + 1)..], "tecla vizinha");
        case 2:
            return (w[..p] + w[(p + 1)..], "faltou letra");
        case 3:
            return (w[..p] + w[p] + w[p..], "letra dobrada");
        default:
            return Neighbors(w[p]) is { Length: > 0 } m ? (w[..p] + m[r.Next(m.Length)] + w[p..], "tecla a mais") : (w[..p] + w[(p + 1)..], "faltou letra");
    }
}

static string Neighbors(char c)
{
    string[] rows = ["qwertyuiop", "asdfghjklç", "zxcvbnm"];
    var lower = char.ToLowerInvariant(c);
    for (int r = 0; r < rows.Length; r++)
    {
        int i = rows[r].IndexOf(lower);
        if (i < 0) continue;
        var sb = new StringBuilder();
        if (i > 0) sb.Append(rows[r][i - 1]);
        if (i < rows[r].Length - 1) sb.Append(rows[r][i + 1]);
        return char.IsUpper(c) ? sb.ToString().ToUpperInvariant() : sb.ToString();
    }
    return "";
}

static string FindDataDir()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "data", "pt_BR.dic"))) return Path.Combine(d.FullName, "data");
    throw new DirectoryNotFoundException("pasta data/ do repositório não encontrada");
}

// ---- teclado de verdade (ABNT2): tecla morta para acento, Shift para maiúscula ----

static void TypeChar(char c, nint hkl)
{
    if (c == '\n') { Tap(VK_RETURN, false, hkl); return; }
    if (c == '?') { Tap(0xC1, true, hkl); return; } // ABNT2: Shift + tecla do "/"
    const string accented = "áàâãéêíóôõúçÁÀÂÃÉÊÍÓÔÕÚ";
    const string deadKeys = "´`^~´^´´^~´\0´`^~´^´´^~´";
    int k = accented.IndexOf(c);
    if (k >= 0 && deadKeys[k] != '\0')
    {
        TypeKey(deadKeys[k], hkl);
        TypeKey(TextUtil.StripAccents(c.ToString())[0], hkl);
        return;
    }
    TypeKey(c, hkl);
}

static void TypeKey(char c, nint hkl)
{
    short r = VkKeyScanExW(c, hkl);
    if (r == -1 || ((r >> 8) & 6) != 0) throw new InvalidOperationException($"sem tecla para '{c}'");
    Tap(r & 0xFF, ((r >> 8) & 1) != 0, hkl);
}

static void Tap(int vk, bool shift, nint hkl)
{
    var list = new List<INPUT>();
    if (shift) list.Add(Key(VK_SHIFT, false, hkl));
    list.Add(Key(vk, false, hkl));
    list.Add(Key(vk, true, hkl));
    if (shift) list.Add(Key(VK_SHIFT, true, hkl));
    var arr = list.ToArray();
    SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
}

static void Chord(int mod, char key)
{
    var hkl = GetKeyboardLayout(0);
    var arr = new[] { Key(mod, false, hkl), Key(key, false, hkl), Key(key, true, hkl), Key(mod, true, hkl) };
    SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
}

static INPUT Key(int vk, bool up, nint hkl) => new()
{
    type = 1,
    ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKeyExW((uint)vk, 0, hkl), dwFlags = up ? 2u : 0u },
};

partial class Program
{
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_RETURN = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }

    // INPUT tem o tamanho da maior união (MOUSEINPUT); o preenchimento garante o cbSize certo
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public KEYBDINPUT ki; public long pad1; }

    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short VkKeyScanExW(char c, nint hkl);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyExW(uint code, uint type, nint hkl);
    [DllImport("user32.dll")] static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
