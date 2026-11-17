// Uso: AcertoBancada.exe [frases=12] [semente=N] [alvo=caixa|chrome|edge|firefox|endereco] [rico=1] [saida=bancada.txt] [rapido=1]
//      [foto=tela.png] [texto="frase fixa"] [quadros=pasta]
// Navegadores abrem num processo separado com perfil temporário (nunca na sessão de quem usa o PC) e são fechados no fim.
// alvo=endereco: barra de endereço do Chrome (uma frase só, sem Enter, que navegaria). foto: tira uma foto da tela
// antes de ler o resultado, para ver a barra de sugestões.
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
string? photo = opt.GetValueOrDefault("foto");
string? fixedText = opt.GetValueOrDefault("texto"); // digita só este texto, como está (para a demonstração do README)
string? framesDir = opt.GetValueOrDefault("quadros"); // pasta: grava a janela de teste a ~15 quadros/s (GIF do README, análise da animação)
bool rich = opt.GetValueOrDefault("rico") == "1";
const string TestTitle = "Acerto bancada";
string TempProfile = Path.Combine(Path.GetTempPath(), $"acerto-bancada-{target}");
nint testWindow = 0;
if (target == "endereco") count = 1;
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
    CarefulField = target == "endereco", // o Acertô usa o perfil cuidadoso na barra de endereço
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
if (fixedText is not null)
{
    originals = [fixedText];
    typedLines = [fixedText];
    injected = [[]];
}

ApplicationConfiguration.Initialize();
var form = new Form { Text = "Acertô — bancada", Width = 900, Height = 420, TopMost = true, StartPosition = FormStartPosition.CenterScreen };
var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12), ScrollBars = ScrollBars.Vertical };
if (framesDir is not null)
{
    // limpo para a gravação: margem branca, sem borda nem barra de rolagem, letra maior
    form.Height = 260;
    form.Padding = new Padding(28, 24, 28, 0);
    form.BackColor = Color.White;
    (box.BorderStyle, box.ScrollBars, box.Font) = (BorderStyle.None, ScrollBars.None, new Font("Segoe UI", 16));
}
form.Controls.Add(box);
bool typingDone = false;
string real = "";
var oracleDoc = new StringBuilder();
var stamps = new List<DateTime>();
Process? browser = null;

form.Shown += async (_, _) =>
{
    if (target == "caixa")
    {
        form.Activate();
        BringToFront(form.Handle);
        // se ainda assim não vier para a frente, espera a pessoa clicar nela (até 30 s)
        box.Text = "Clique aqui para começar";
        for (int i = 0; i < 300 && GetForegroundWindow() != form.Handle; i++) await Task.Delay(100);
        box.Text = "";
        box.Focus();
    }
    else
    {
        form.WindowState = FormWindowState.Minimized;
        var exe = target switch
        {
            "edge" => @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            "firefox" => @"C:\Program Files\Mozilla Firefox\firefox.exe",
            _ => @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        };
        // rico=1: campo editável como o do WhatsApp Web e do Gmail, em vez de textarea
        var field = rich
            ? "<div id=c contenteditable style='width:90vw;height:80vh;font:18px Segoe UI;outline:none'></div><script>c.focus()</script>"
            : "<textarea autofocus style='width:95vw;height:90vh;font-size:18px'></textarea>";
        var page = $"data:text/html;charset=utf-8,<title>{TestTitle}</title>{field}";
        // Navegador separado, com perfil temporário: nunca abre aba nem mexe na sessão de quem usa o PC.
        // Só a caixa e navegadores: app que guarda sessão (Bloco de Notas com abas) já perdeu abas não salvas num teste.
        Directory.CreateDirectory(TempProfile);
        browser = Process.Start(exe, target == "firefox"
            ? $"-no-remote -profile \"{TempProfile}\" -new-window \"{page}\""
            : $"--user-data-dir=\"{TempProfile}\" --no-first-run --no-default-browser-check --new-window \"{page}\"");
        // a janela de teste tem título único; sem achar e trazer ela para a frente, não digita nada
        for (int i = 0; i < 100 && testWindow == 0; i++)
        {
            await Task.Delay(100);
            testWindow = FindWindowByTitle(TestTitle);
        }
        if (testWindow != 0)
        {
            await Task.Delay(1500); // a página termina de carregar e põe o foco no campo
            BringToFront(testWindow);
            for (int i = 0; i < 30 && GetForegroundWindow() != testWindow; i++) await Task.Delay(100);
        }
        if (target == "endereco" && GetForegroundWindow() == testWindow) Chord(VK_CONTROL, 'L');
    }
    await Task.Delay(1200); // o Acertô confere o campo (senha? texto antes do cursor?) em segundo plano

    oracle.ConfirmContext(oracle.Generation, "");
    Task recording = Task.CompletedTask;
    if (framesDir is not null)
    {
        Directory.CreateDirectory(framesDir);
        var area = form.RectangleToScreen(form.ClientRectangle);
        if (testWindow != 0 && GetWindowRect(testWindow, out var wr))
            area = Rectangle.Intersect(Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom), SystemInformation.VirtualScreen);
        if (area.Width < 10 || area.Height < 10) area = Screen.FromHandle(testWindow).Bounds; // minimizada na hora de medir: grava a tela
        File.WriteAllText(Path.Combine(framesDir, "area.txt"), $"{area.X},{area.Y},{area.Width},{area.Height}");
        recording = Task.Run(() => Record(framesDir, area, () => typingDone));
    }
    nint formHandle = form.Handle;
    bool aborted = false;
    await Task.Run(() =>
    {
        // Segurança: as teclas só vão para a janela que estava na frente no começo (na caixa, tem de ser ela).
        // Se a pessoa trocar de janela ou a caixa não ganhar o foco, para na hora em vez de digitar em outro programa.
        nint expected = GetForegroundWindow();
        if (expected != (target == "caixa" ? formHandle : testWindow)) { aborted = true; return; }
        var hkl = GetKeyboardLayout(GetWindowThreadProcessId(expected, out _));
        for (int li = 0; li < typedLines.Count && !aborted; li++)
        {
            stamps.Add(DateTime.Now);
            var line = typedLines[li] + (li < typedLines.Count - 1 ? "\n" : " ");
            foreach (var c in line)
            {
                if (GetForegroundWindow() != expected) { aborted = true; break; }
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
        Thread.Sleep(framesDir is null ? 600 : 1800); // na gravação, deixa o resultado parado um pouco no fim
    });
    typingDone = true;
    await recording;
    if (aborted)
    {
        File.WriteAllText(output, "PARADA: a janela da frente não era a do teste (foco em outro programa). Nada foi digitado fora dela.");
        CloseBrowser();
        form.Close();
        return;
    }
    if (photo is not null) TakePhoto(photo);

    try
    {
        if (target == "caixa") real = box.Text;
        else if (GetForegroundWindow() != testWindow) real = "(não li: a janela de teste saiu da frente)";
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
        }
        Report();
    }
    catch (Exception e)
    {
        File.WriteAllText(output, $"semente={seed} FALHA NA BANCADA: {e}");
    }
    CloseBrowser();
    form.Close();
};
Application.Run(form);

// ---------------------------------------------------------------------------------------------------------------

// O navegador é só nosso (perfil temporário): fecha pelo processo, sem atalho de teclado, e apaga o perfil
void CloseBrowser()
{
    if (browser is null) return;
    try { browser.Kill(entireProcessTree: true); browser.WaitForExit(5000); } catch (Exception) { }
    Thread.Sleep(500);
    try { Directory.Delete(TempProfile, recursive: true); } catch (Exception) { }
}

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

static void TakePhoto(string path)
{
    var b = SystemInformation.VirtualScreen; // todos os monitores
    using var bmp = new Bitmap(b.Width, b.Height);
    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
}

static nint FindWindowByTitle(string part)
{
    nint found = 0;
    EnumWindows((h, _) =>
    {
        if (!IsWindowVisible(h)) return true;
        var sb = new StringBuilder(512);
        GetWindowTextW(h, sb, sb.Capacity);
        if (sb.ToString().Contains(part, StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
        return true;
    }, 0);
    return found;
}

// O Windows só deixa trazer janela para a frente quem tem o foco: liga a fila de entrada à da janela da frente por um instante
static void BringToFront(nint hwnd)
{
    uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out _), me = GetCurrentThreadId();
    AttachThreadInput(me, fg, true);
    ShowWindow(hwnd, 9); // SW_RESTORE
    BringWindowToTop(hwnd);
    SetForegroundWindow(hwnd);
    AttachThreadInput(me, fg, false);
    // e clica no meio dela, como uma pessoa: assim o foco chega pelo caminho normal (os apps e o Acertô recebem os
    // eventos de foco; só o SetForegroundWindow deixou o Acertô achando que a frente ainda era a janela anterior)
    Thread.Sleep(300);
    if (GetForegroundWindow() != hwnd || !GetWindowRect(hwnd, out var r)) return;
    GetCursorPos(out var back);
    SetCursorPos((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
    mouse_event(0x0002, 0, 0, 0, 0); // botão esquerdo desce
    mouse_event(0x0004, 0, 0, 0, 0); // sobe
    Thread.Sleep(100);
    SetCursorPos(back.X, back.Y);
}

// CaptureBlt: sem ele a animação e a barra de sugestões (janelas em camadas) não saem na imagem
static void Record(string dir, Rectangle area, Func<bool> done)
{
    var clock = Stopwatch.StartNew();
    for (int i = 0; !done(); i++)
    {
        using var bmp = new Bitmap(area.Width, area.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            // Graphics.CopyFromScreen recusa SourceCopy | CaptureBlt; o BitBlt aceita
            nint screen = GetDC(0), target = g.GetHdc();
            BitBlt(target, 0, 0, area.Width, area.Height, screen, area.X, area.Y, 0x00CC0020 | 0x40000000);
            g.ReleaseHdc(target);
            ReleaseDC(0, screen);
        }
        bmp.Save(Path.Combine(dir, $"{i:0000}-{clock.ElapsedMilliseconds:000000}.png"), System.Drawing.Imaging.ImageFormat.Png);
        var next = (i + 1) * 66 - clock.ElapsedMilliseconds;
        if (next > 0) Thread.Sleep((int)next);
    }
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
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    delegate bool EnumProc(nint hwnd, nint param);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, nint param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nint extra);
}
