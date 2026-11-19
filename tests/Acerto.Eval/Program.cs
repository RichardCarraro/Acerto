// Mede o corretor com erros de digitação sintéticos nas palavras mais usadas.
// uso: AcertoEval <pasta data> [quantas palavras=3000] [amostras por categoria=12]
// Para cada palavra válida e frequente, gera erros realistas (letras vizinhas trocadas de lugar, letra faltando,
// tecla vizinha, letra dobrada) e vê o que o Corrector faz: corrige certo, não mexe ou corrige errado.
using System.Globalization;
using Acerto.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var dataDir = args.Length > 0 && !args[0].Contains('=') ? args[0] : Path.Combine(AppContext.BaseDirectory, "data");
var positional = args.Where(a => !a.Contains('=')).ToArray();
int topN = positional.Length > 1 && int.TryParse(positional[1], out var n0) ? n0 : 3000;
int samples = positional.Length > 2 && int.TryParse(positional[2], out var s0) ? s0 : 12;

var userDir = Path.Combine(Path.GetTempPath(), "acerto-eval-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(userDir);
File.WriteAllText(Path.Combine(userDir, "atalhos.txt"), ""); // sem atalhos: eles mascaram o resultado
var relogioCarga = System.Diagnostics.Stopwatch.StartNew(); // para o modo desempenho: é a carga que o app faz ao abrir
var data = CoreData.Load(dataDir, userDir);
var tempoLoad = relogioCarga.Elapsed;
// perfil=agressivo usa o perfil padrão do app; sem isso, o cuidadoso
var opt = args.Contains("perfil=agressivo") ? TypoOptions.Aggressive : TypoOptions.Careful;
string? prevArg = args.FirstOrDefault(a => a.StartsWith("ant="))?[4..]; // palavra anterior, para o modo explicar
string? nextArg = args.FirstOrDefault(a => a.StartsWith("prox="))?[5..]; // e a seguinte (acento pela seguinte)
foreach (var kv in args.Where(a => a.Contains('=') && !a.StartsWith("perfil=") && !a.StartsWith("ant=") && !a.StartsWith("prox=")).Select(a => a.Split('=')))
{
    var prop = typeof(TypoOptions).GetProperty(kv[0]) ?? throw new ArgumentException("opção desconhecida: " + kv[0]);
    object value = prop.PropertyType == typeof(long) ? long.Parse(kv[1])
        : prop.PropertyType == typeof(int) ? int.Parse(kv[1])
        : prop.PropertyType == typeof(bool) ? bool.Parse(kv[1])
        : (object)double.Parse(kv[1], CultureInfo.InvariantCulture);
    opt = (TypoOptions)opt.GetType().GetMethod("<Clone>$")!.Invoke(opt, null)!;
    prop.SetValue(opt, value);
}
relogioCarga.Restart();
var corrector = new Corrector(data, opt);
var tempoCorretor = relogioCarga.Elapsed;

// modo "desempenho": AcertoEval data desempenho <frases-reais.txt> <corpus.txt> [palavras=20000] perfil=agressivo
// Carga (CoreData.Load e Corrector), memória depois de carregar, e média/p50/p99 de Correct por palavra e de Suggest
// por tecla (cada prefixo da palavra), depois de uma volta de aquecimento. Rode 3 vezes e use a mediana.
// "assinatura" resume todas as respostas: otimização só vale se ela não mudar.
if (positional.Length > 1 && positional[1] == "desempenho")
{
    int quantas = positional.Length > 4 ? int.Parse(positional[4]) : 20000;
    var palavra = new System.Text.RegularExpressions.Regex(@"[\p{L}-]+");
    var itens = new List<(string? Prev, string Word)>();
    void Junta(string linha)
    {
        string? prev = BigramModel.SentenceStart;
        foreach (System.Text.RegularExpressions.Match m in palavra.Matches(linha))
        {
            if (itens.Count >= quantas) return;
            itens.Add((prev, m.Value));
            prev = m.Value.ToLowerInvariant();
        }
    }
    foreach (var l in File.ReadLines(positional[2]).Where(l => l.Length > 0 && !l.StartsWith('#'))) Junta(l.Split('\t')[0]);
    foreach (var l in File.ReadAllLines(positional[3]).TakeLast(20000)) { if (itens.Count >= quantas) break; Junta(l.Split('\t')[^1]); }

    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long gerenciada = GC.GetTotalMemory(true);
    using var proc = System.Diagnostics.Process.GetCurrentProcess();
    long privada = proc.PrivateMemorySize64, conjunto = proc.WorkingSet64;

    var assinatura = new System.Text.StringBuilder();
    double[] TempoCorrect()
    {
        var t = new double[itens.Count];
        for (int i = 0; i < itens.Count; i++)
        {
            long a = System.Diagnostics.Stopwatch.GetTimestamp();
            corrector.Correct(itens[i].Word, false, itens[i].Prev);
            t[i] = System.Diagnostics.Stopwatch.GetElapsedTime(a).TotalMicroseconds;
        }
        return t;
    }
    double[] TempoSuggest()
    {
        var t = new List<double>();
        foreach (var (prev, w) in itens)
            for (int k = 1; k <= w.Length; k++)
            {
                long a = System.Diagnostics.Stopwatch.GetTimestamp();
                corrector.Suggester.Suggest(prev, w[..k].ToLowerInvariant());
                t.Add(System.Diagnostics.Stopwatch.GetElapsedTime(a).TotalMicroseconds);
            }
        return t.ToArray();
    }
    TempoCorrect(); TempoSuggest(); // aquecimento (JIT e caches)
    var tc = TempoCorrect(); var ts = TempoSuggest();

    foreach (var (prev, w) in itens)
    {
        assinatura.Append(corrector.Correct(w, false, prev)?.Replacement).Append('|');
        assinatura.Append(string.Join(",", corrector.Suggester.Suggest(prev, w.ToLowerInvariant()[..Math.Min(3, w.Length)]))).Append('|');
    }
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(assinatura.ToString())))[..16];

    static string Resumo(double[] t)
    {
        var o = t.OrderBy(x => x).ToArray();
        double P(double q) => o[Math.Min(o.Length - 1, (int)(q * o.Length))];
        return string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"{o.Length} chamadas; média {t.Average():F1} µs, p50 {P(0.5):F1} µs, p99 {P(0.99):F1} µs, máx {o[^1]:F0} µs");
    }
    var ptd = CultureInfo.GetCultureInfo("pt-BR");
    Console.WriteLine(string.Create(ptd, $"Carga: CoreData.Load {tempoLoad.TotalMilliseconds:F0} ms; Corrector {tempoCorretor.TotalMilliseconds:F0} ms"));
    Console.WriteLine(string.Create(ptd, $"Memória depois de carregar: gerenciada {gerenciada / 1048576.0:F0} MB; privada {privada / 1048576.0:F0} MB; conjunto de trabalho {conjunto / 1048576.0:F0} MB"));
    Console.WriteLine($"Correct por palavra: {Resumo(tc)}");
    Console.WriteLine($"Suggest por tecla:   {Resumo(ts)}");
    Console.WriteLine($"Assinatura das respostas: {hash} ({itens.Count} palavras)");

    // Teclada: a pessoa esmaga o teclado numa risada ("ksljlkajcalkçcj..."). Tudo isso roda na thread que atende os
    // hooks de teclado e mouse do Windows: tecla lenta aqui trava o PC inteiro. Pior tecla = OnChar + Suggestions.
    var teclas = "asdfghjklçqwertyuiopzxcvbnm";
    var sorteio = new Random(42);
    var motor = new TextEngine(corrector, _ => { });
    foreach (int n in (int[])[8, 16, 30, 50, 100])
    {
        motor.Reset();
        motor.ConfirmContext(motor.Generation, "");
        double pior = 0, soma = 0;
        for (int i = 0; i < n; i++)
        {
            char ch = teclas[sorteio.Next(teclas.Length)];
            long a = System.Diagnostics.Stopwatch.GetTimestamp();
            motor.OnChar(ch);
            motor.Suggestions();
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(a).TotalMilliseconds;
            pior = Math.Max(pior, ms);
            soma += ms;
        }
        long e = System.Diagnostics.Stopwatch.GetTimestamp();
        motor.OnChar(' ');
        double espaco = System.Diagnostics.Stopwatch.GetElapsedTime(e).TotalMilliseconds;
        Console.WriteLine(string.Create(ptd, $"Teclada de {n,3} letras: pior tecla {pior:F2} ms; soma {soma:F1} ms; espaço {espaco:F2} ms"));
    }
    Directory.Delete(userDir, true);
    return;
}

// modo diagnóstico: AcertoEval data explicar palavra1 palavra2 ...
if (positional.Length > 1 && positional[1] == "explicar")
{
    foreach (var w in positional.Skip(2))
    {
        var r = corrector.Correct(w, false, prevArg);
        Console.WriteLine($"{(prevArg is null ? "" : prevArg + " ")}{w} → {(r is null ? "(não mexe)" : r.Replacement + " [" + r.Kind + "]")}   corpus={data.Frequency.Count(w)} válida={data.Lexicon.IsValid(w)}");
        if (nextArg is not null)
            Console.WriteLine($"   com a seguinte \"{nextArg}\": {corrector.CorrectByNext(w, prevArg, nextArg)?.Replacement ?? "(não mexe)"}");
        foreach (var c in corrector.Typos.Candidates(w, prevArg).Take(5))
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"     {c.Word,-14} n={c.Count,-9} custo={c.Cost:F2} nota={c.Score:F2}"));
    }
    Directory.Delete(userDir, true);
    return;
}

// modo "bigramas": AcertoEval data bigramas <frases.txt> <saida.txt> [contagem mínima=3] [frases reservadas=20000]
// Conta pares de palavras vizinhas num corpus de frases (uma por linha; se houver TAB, o texto é o que vem depois)
// e grava a tabela usada pela correção por contexto. As últimas frases ficam de fora para servir de teste.
// Formato: "=palavra total" abre um bloco; cada linha seguinte é "próxima contagem". "<s>" é início de frase.
if (positional.Length > 1 && positional[1] == "bigramas")
{
    string input = positional[2], output = positional[3];
    int minCount = positional.Length > 4 ? int.Parse(positional[4]) : 3;
    int heldOut = positional.Length > 5 ? int.Parse(positional[5]) : 20000;
    var lines = File.ReadAllLines(input);
    var ids = new Dictionary<string, int>(StringComparer.Ordinal) { [BigramModel.SentenceStart] = 0 };
    var vocab = new List<string> { BigramModel.SentenceStart };
    var unigram = new List<long> { 0 };
    var pairs = new Dictionary<long, int>();
    int Id(string w)
    {
        if (!ids.TryGetValue(w, out int id)) { id = vocab.Count; ids[w] = id; vocab.Add(w); unigram.Add(0); }
        return id;
    }
    for (int li = 0; li < lines.Length - heldOut; li++)
    {
        int prev = 0; // início de frase
        unigram[0]++;
        foreach (var tok in BigramModel.Tokenize(lines[li]))
        {
            if (tok is null) { prev = -1; continue; }                       // pontuação/número: quebra a sequência
            if (tok == BigramModel.SentenceStart) { prev = 0; unigram[0]++; continue; }
            int id = Id(tok);
            unigram[id]++;
            if (prev >= 0)
            {
                long key = ((long)prev << 32) | (uint)id;
                pairs[key] = pairs.GetValueOrDefault(key) + 1;
            }
            prev = id;
        }
    }
    var valid = new Dictionary<int, bool>();
    bool IsValid(int id) { if (!valid.TryGetValue(id, out bool v)) valid[id] = v = data.Lexicon.IsValid(vocab[id]); return v; }
    var kept = pairs.Where(kv => kv.Value >= minCount && IsValid((int)(kv.Key & 0xFFFFFFFF)))
        .GroupBy(kv => (int)(kv.Key >> 32))
        .Where(g => g.Key == 0 || unigram[g.Key] >= 5)
        .OrderByDescending(g => unigram[g.Key]);
    long n = 0;
    using (var w = new StreamWriter(output, false, new System.Text.UTF8Encoding(false)))
    {
        w.NewLine = "\n";
        foreach (var g in kept)
        {
            w.WriteLine($"={vocab[g.Key]} {unigram[g.Key]}");
            foreach (var kv in g.OrderByDescending(kv => kv.Value)) { w.WriteLine($"{vocab[(int)(kv.Key & 0xFFFFFFFF)]} {kv.Value}"); n++; }
        }
    }
    Console.WriteLine($"{lines.Length - heldOut} frases, {unigram.Sum()} palavras, {pairs.Count} pares distintos, {n} gravados em {output} ({new FileInfo(output).Length / 1048576.0:F1} MB)");
    Directory.Delete(userDir, true);
    return;
}

// modo "validas": AcertoEval data validas > data/validas.txt
// Palavras da tabela de frequência que o Hunspell aceita. O corretor monta os índices com elas ao abrir; sem o
// arquivo ele pergunta ao Hunspell uma por uma (~370 mil consultas). Gerar de novo ao trocar frequencia.txt ou o
// dicionário: DataTests confere que o arquivo bate com os dois.
if (positional.Length > 1 && positional[1] == "validas")
{
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    foreach (var w in Lexicon.ValidasDoCorpus(data.Frequency, new Lexicon(Path.Combine(dataDir, "pt_BR.aff"), Path.Combine(dataDir, "pt_BR.dic"))))
        Console.Out.Write(w + "\n");
    Directory.Delete(userDir, true);
    return;
}

// modo "protegidas": AcertoEval data protegidas > data/protegidas.txt
// Palavras do corpus que o dicionário pt-BR (VERO) não tem, mas que o corretor do próprio Windows aceita
// (estrangeirismos e nomes: mail, voice, office, Frazier). Nunca passam pela regra de digitação.
if (positional.Length > 1 && positional[1] == "protegidas")
{
    var windows = WindowsSpeller.TryCreate("pt-BR") ?? throw new InvalidOperationException("corretor pt-BR do Windows não disponível");
    foreach (var (w, n) in data.Frequency.Entries.OrderByDescending(e => e.Count))
    {
        if (n < Corrector.AttestedCount || w.Length < 3 || !w.All(char.IsLetter) || data.Lexicon.IsValid(w)) continue;
        if (windows.IsValid(w) || windows.IsValid(char.ToUpperInvariant(w[0]) + w[1..])) Console.WriteLine(w);
    }
    Directory.Delete(userDir, true);
    return;
}

// modo "trocas": AcertoEval data trocas [quantas=60000] [perfil=agressivo]
// Palavras frequentes do corpus (válidas ou não) que o corretor mudaria. No perfil cuidadoso tem que dar zero
// para a regra de digitação; no agressivo mostra o preço: nomes e estrangeirismos trocados.
if (positional.Length > 1 && positional[1] == "trocas")
{
    int n = positional.Length > 2 ? int.Parse(positional[2]) : 60000;
    var changed = data.Frequency.Entries.Where(e => e.Word.All(char.IsLetter))
        .OrderByDescending(e => e.Count).Take(n)
        .Select(e => (e.Word, e.Count, R: corrector.Correct(e.Word)))
        .Where(x => x.R is { Kind: CorrectionKind.Typo })
        .ToList();
    Console.Error.WriteLine($"{changed.Count} de {n} palavras frequentes seriam trocadas pela regra de digitação");
    foreach (var c in changed) Console.WriteLine($"{c.Word}>{c.R!.Replacement}\t{c.Count}");
    Directory.Delete(userDir, true);
    return;
}

// modo "caixa": AcertoEval data caixa [quantas=60000] — palavras frequentes do corpus que ganhariam maiúscula de nome
if (positional.Length > 1 && positional[1] == "caixa")
{
    int n = positional.Length > 2 ? int.Parse(positional[2]) : 60000;
    var changed = data.Frequency.Entries.Where(e => e.Word.All(char.IsLetter)).OrderByDescending(e => e.Count).Take(n)
        .Select(e => (e.Word, e.Count, R: corrector.Correct(e.Word))).Where(x => x.R is { Kind: CorrectionKind.Case }).ToList();
    Console.Error.WriteLine($"{changed.Count} de {n} ganhariam maiúscula");
    foreach (var c in changed) Console.WriteLine($"{c.Word}>{c.R!.Replacement}	{c.Count}");
    Directory.Delete(userDir, true);
    return;
}

// modo mineração: AcertoEval data minerar [contagem mínima=10] [razão mínima=200]
// Lista palavras do corpus que parecem erro de digitação de uma palavra muito mais usada, para revisão manual.
if (positional.Length > 1 && positional[1] == "minerar")
{
    long minCount = positional.Length > 2 ? long.Parse(positional[2]) : 10;
    long minRatio = positional.Length > 3 ? long.Parse(positional[3]) : 200;
    var windows = WindowsSpeller.TryCreate("pt-BR");
    int skippedBySpeller = 0;
    var found = new List<(string Typed, long Count, string Target, long Ratio)>();
    foreach (var (w, n) in data.Frequency.Entries)
    {
        if (n < minCount || w.Length < 4 || !w.All(char.IsLetter) || data.Lexicon.IsValid(w) || data.Fixed.TryGet(w, out _)) continue;
        if (corrector.Typos.Best(w) is not { } m || m.Count < minRatio * n) continue;
        // nomes e estrangeirismos que o corretor do Windows conhece (voice, mail, Frazier) ficam de fora
        if (windows is not null && (windows.IsValid(w) || windows.IsValid(char.ToUpperInvariant(w[0]) + w[1..]))) { skippedBySpeller++; continue; }
        found.Add((w, n, m.Word, m.Count / n));
    }
    Console.Error.WriteLine($"{found.Count} candidatas; {skippedBySpeller} descartadas pelo corretor do Windows");
    foreach (var f in found.OrderByDescending(f => f.Count))
        Console.WriteLine($"{f.Typed}	{f.Target}	{f.Count}");
    Directory.Delete(userDir, true);
    return;
}

string[] rows = ["qwertyuiop", "asdfghjklç", "zxcvbnm"];
var neighbors = new Dictionary<char, List<char>>();
for (int r = 0; r < rows.Length; r++)
    for (int c = 0; c < rows[r].Length; c++)
    {
        var list = neighbors[rows[r][c]] = [];
        if (c > 0) list.Add(rows[r][c - 1]);
        if (c + 1 < rows[r].Length) list.Add(rows[r][c + 1]);
    }

// modo "erros das mais usadas": AcertoEval data comuns [quantas palavras=2000]
// Para as palavras mais usadas, lista os erros típicos que de fato aparecem no corpus (contagem >= 10) e que o
// corretor hoje não corrige por estarem "atestados". Saída para revisão manual: tipo, erro, palavra, contagem.
if (positional.Length > 1 && positional[1] == "comuns")
{
    int n = positional.Length > 2 ? int.Parse(positional[2]) : 2000;
    var windows = WindowsSpeller.TryCreate("pt-BR");
    var seen = new HashSet<string>();
    var top = data.Frequency.Entries
        .Where(e => e.Word.Length >= 4 && e.Word.All(char.IsLetter) && data.Lexicon.IsValid(e.Word))
        .OrderByDescending(e => e.Count).Take(n).ToList();
    foreach (var (word, count) in top)
        foreach (var (kind, typo) in Typos(word))
        {
            long tc = data.Frequency.Count(typo);
            if (tc < Corrector.AttestedCount || typo.Length < 4 || data.Lexicon.IsValid(typo) || data.Fixed.TryGet(typo, out _)) continue;
            if (corrector.Correct(typo) is not null) continue;                       // já corrige (acento, etc.)
            if (count < 300 * tc) continue;                                           // a palavra tem que ser muito mais usada
            if (corrector.Typos.Candidates(typo) is not [var best, ..] || best.Word != word) continue; // e ser a melhor explicação
            if (windows is not null && (windows.IsValid(typo) || windows.IsValid(char.ToUpperInvariant(typo[0]) + typo[1..]))) continue;
            if (seen.Add(typo)) Console.WriteLine($"{kind}	{typo}	{word}	{tc}");
        }
    Directory.Delete(userDir, true);
    return;
}

// modo "contexto": AcertoEval data contexto <frases.txt> [frases reservadas=20000] [perfil=agressivo]
// Mede em frases reais (as reservadas, que não entraram na tabela de pares) o que o contexto traz:
//  A) erro de digitação numa palavra, corrigido com e sem a palavra anterior;
//  B) acento retirado de uma palavra acentuada (está → esta), para ver se volta;
//  C) palavras certas sem acento que têm par acentuado (esta, e, pais): quantas o corretor estraga.
if (positional.Length > 1 && positional[1] == "contexto")
{
    var sentences = File.ReadAllLines(positional[2]);
    int held = positional.Length > 3 ? int.Parse(positional[3]) : 20000;
    int typos = 0, okCtx = 0, okNo = 0, wrongCtx = 0, wrongNo = 0;
    int accents = 0, accCtx = 0, accNo = 0;
    int plain = 0, spoiled = 0;
    var spoiledSamples = new List<string>();
    var fixedSamples = new List<string>();
    for (int li = sentences.Length - held; li < sentences.Length; li++)
    {
        string? prev = BigramModel.SentenceStart;
        int pos = 0;
        foreach (var tok in BigramModel.Tokenize(sentences[li]))
        {
            if (tok is null) { prev = null; continue; }
            if (tok == BigramModel.SentenceStart) { prev = tok; continue; }
            pos++;
            bool valid = tok.All(char.IsLetter) && data.Lexicon.IsValid(tok);
            if (valid && tok.Length >= 4)
            {
                // A) um erro por palavra, escolhido de forma determinística
                var options = Typos(tok).Select(t => t.Item2).Where(t => t != tok && !data.Lexicon.IsValid(t)).ToList();
                if (options.Count > 0)
                {
                    var typo = options[(li * 31 + pos * 7) % options.Count];
                    typos++;
                    var a = corrector.Correct(typo, false, prev)?.Replacement;
                    var b = corrector.Correct(typo, false, null)?.Replacement;
                    if (a == tok) okCtx++; else if (a is not null) wrongCtx++;
                    if (b == tok) okNo++; else if (b is not null) wrongNo++;
                    if (a == tok && b != tok && fixedSamples.Count < 14) fixedSamples.Add($"{prev} {typo}→{tok} (sem contexto: {b ?? "nada"})");
                }
            }
            if (valid)
            {
                var stripped = TextUtil.StripAccents(tok);
                if (stripped != tok)
                {
                    // B) tira o acento e vê se volta
                    accents++;
                    if (corrector.Correct(stripped, false, prev)?.Replacement == tok) accCtx++;
                    if (corrector.Correct(stripped, false, null)?.Replacement == tok) accNo++;
                }
                else
                {
                    // C) palavra certa sem acento: não pode mudar
                    plain++;
                    var r = corrector.Correct(tok, false, prev);
                    // maiúscula de nome não conta (Kind Case, ou locução de cidade: "porto alegre" → Alegre): o corpus é todo minúsculo
                    if (r is { Kind: not CorrectionKind.Case } && !string.Equals(r.Replacement, tok, StringComparison.OrdinalIgnoreCase)) { spoiled++; if (spoiledSamples.Count < 24) spoiledSamples.Add($"{prev} {tok}→{r.Replacement}"); }
                }
            }
            prev = tok;
        }
    }
    var c2 = CultureInfo.GetCultureInfo("pt-BR");
    Console.WriteLine(string.Create(c2, $"A) erros de digitação: {typos}"));
    Console.WriteLine(string.Create(c2, $"   com contexto: certo {100.0 * okCtx / typos:F1}%, errado {100.0 * wrongCtx / typos:F1}%"));
    Console.WriteLine(string.Create(c2, $"   sem contexto: certo {100.0 * okNo / typos:F1}%, errado {100.0 * wrongNo / typos:F1}%"));
    Console.WriteLine(string.Create(c2, $"B) acento retirado: {accents}; volta com contexto {100.0 * accCtx / accents:F1}%, sem contexto {100.0 * accNo / accents:F1}%"));
    Console.WriteLine(string.Create(c2, $"C) palavras certas sem acento: {plain}; estragadas {spoiled} ({100.0 * spoiled / plain:F2}%)"));
    Console.WriteLine("   estragadas: " + string.Join("  |  ", spoiledSamples));
    Console.WriteLine("   só o contexto acertou: " + string.Join("  |  ", fixedSamples));
    Directory.Delete(userDir, true);
    return;
}

// modo "direita": AcertoEval data direita <frases.txt> [quantas=30000] perfil=agressivo [NextAccentRatio=N]
// Acento pela palavra seguinte (Corrector.CorrectByNext), só onde a correção normal não mexeu, como no app:
//  B) acento retirado de uma palavra (é → e): quantas voltam;  C) palavra certa sem acento: quantas são estragadas.
if (positional.Length > 1 && positional[1] == "direita")
{
    var sentences = File.ReadAllLines(positional[2]);
    int take = positional.Length > 3 ? int.Parse(positional[3]) : 30000;
    int accents = 0, back = 0, wrongBack = 0, plain = 0, spoiled = 0;
    var spoiledBy = new Dictionary<string, int>();
    var backBy = new Dictionary<string, int>();
    foreach (var line in sentences.TakeLast(take))
    {
        var toks = BigramModel.Tokenize(line).ToList();
        for (int i = 0; i < toks.Count - 1; i++)
        {
            var tok = toks[i];
            var next = toks[i + 1];
            if (tok is null || tok == BigramModel.SentenceStart || next is null || next == BigramModel.SentenceStart) continue;
            if (!tok.All(char.IsLetter) || !data.Lexicon.IsValid(tok)) continue;
            string? prev = i > 0 ? toks[i - 1] : BigramModel.SentenceStart; // começo da linha é começo de frase, como no app
            var stripped = TextUtil.StripAccents(tok);
            bool isAccented = stripped != tok;
            if (corrector.Correct(stripped, false, prev) is not null) continue; // a correção normal já decidiu
            var r = corrector.CorrectByNext(stripped, prev, next);
            if (isAccented)
            {
                if (!data.Lexicon.IsValid(stripped)) continue; // "informacao": é a correção normal
                accents++;
                if (r?.Replacement == tok) { back++; backBy[$"{stripped}→{tok}"] = backBy.GetValueOrDefault($"{stripped}→{tok}") + 1; }
                else if (r is not null) wrongBack++;
            }
            else
            {
                plain++;
                if (r is not null && !string.Equals(r.Replacement, tok, StringComparison.OrdinalIgnoreCase)) { spoiled++; var k = $"{prev} {tok}→{r.Replacement} {next}"; spoiledBy[k] = spoiledBy.GetValueOrDefault(k) + 1; }
            }
        }
    }
    var c3 = CultureInfo.GetCultureInfo("pt-BR");
    Console.WriteLine(string.Create(c3, $"B) acento retirado (forma sem acento também existe, correção normal não mexeu): {accents}; voltou {back} ({100.0 * back / Math.Max(accents, 1):F1}%), outra acentuada {wrongBack}"));
    Console.WriteLine(string.Create(c3, $"C) palavras certas sem acento: {plain}; estragadas {spoiled} ({100.0 * spoiled / Math.Max(plain, 1):F3}%)"));
    Console.WriteLine("   voltaram: " + string.Join("  ", backBy.OrderByDescending(kv => kv.Value).Take(15).Select(kv => $"{kv.Key} {kv.Value}")));
    Console.WriteLine("   estragadas: " + string.Join("  |  ", spoiledBy.OrderByDescending(kv => kv.Value).Take(40).Select(kv => $"{kv.Key} {kv.Value}")));
    Directory.Delete(userDir, true);
    return;
}

// modo "sugestoes": AcertoEval data sugestoes <frases.txt> [quantas=2000] perfil=agressivo
// Em frases fora da amostra, como a barra se sai: (1) logo depois do espaço, a próxima palavra está entre as 3?
// (2) digitando sem acento, com quantas letras a palavra vira a 1ª sugestão (Tab completa) e quanto isso economiza.
if (positional.Length > 1 && positional[1] == "sugestoes")
{
    var sentences = File.ReadAllLines(positional[2]);
    int take = positional.Length > 3 ? int.Parse(positional[3]) : 2000;
    int nextTotal = 0, next1 = 0, next3 = 0, words4 = 0, completed = 0;
    long letters = 0, saved = 0;
    foreach (var line in sentences.TakeLast(take))
    {
        var toks = BigramModel.Tokenize(line).ToList();
        for (int i = 0; i < toks.Count; i++)
        {
            var tok = toks[i];
            if (tok is null || tok == BigramModel.SentenceStart || !tok.All(char.IsLetter) || !data.Lexicon.IsValid(tok)) continue;
            string? prev = i > 0 ? toks[i - 1] : BigramModel.SentenceStart;
            if (prev is not null && prev != BigramModel.SentenceStart)
            {
                nextTotal++;
                var nx = corrector.Suggester.Suggest(prev, "");
                if (nx.Count > 0 && nx[0] == tok) next1++;
                if (nx.Contains(tok)) next3++;
            }
            if (tok.Length < 4) continue;
            words4++;
            letters += tok.Length;
            var plainTok = TextUtil.StripAccents(tok);
            for (int k = 2; k < tok.Length - 1; k++)
                if (corrector.Suggester.Suggest(prev, plainTok[..k]) is [var first, ..] && first == tok)
                {
                    completed++;
                    saved += tok.Length - k - 1; // menos o Tab
                    break;
                }
        }
    }
    var c4 = CultureInfo.GetCultureInfo("pt-BR");
    Console.WriteLine(string.Create(c4, $"Próxima palavra: {nextTotal} posições; 1ª sugestão certa {100.0 * next1 / nextTotal:F1}%, entre as 3 {100.0 * next3 / nextTotal:F1}%"));
    Console.WriteLine(string.Create(c4, $"Completar (palavras de 4+ letras): {words4}; viraram 1ª sugestão antes do fim {100.0 * completed / words4:F1}%; teclas economizadas com Tab {100.0 * saved / letters:F1}% das letras"));
    Directory.Delete(userDir, true);
    return;
}

// modo "frases": AcertoEval data frases <arquivo> [perfil=agressivo]
// Cada linha "digitado<TAB>esperado" (sem TAB: não pode mudar nada; # comenta) passa pelo motor inteiro, como no app
// (contexto, maiúscula, ponto), letra por letra com espaço no fim. Lista o que saiu diferente e o placar.
if (positional.Length > 1 && positional[1] == "frases")
{
    int ok = 0, total = 0, broke = 0;
    foreach (var raw in File.ReadAllLines(positional[2]))
    {
        if (raw.Length == 0 || raw.StartsWith('#')) continue;
        var parts = raw.Split('\t');
        string typed = parts[0], want = parts.Length > 1 ? parts[1] : parts[0];
        var engine = new TextEngine(corrector, _ => { });
        engine.ConfirmContext(engine.Generation, "");
        var doc = new System.Text.StringBuilder();
        foreach (var ch in typed + " ")
        {
            var act = engine.OnChar(ch);
            if (act.Swallow) { doc.Length -= act.Backspaces; doc.Append(act.Insert); }
            else doc.Append(ch);
        }
        var got = doc.ToString().TrimEnd();
        total++;
        if (got == want) { ok++; continue; }
        if (parts.Length == 1) broke++;
        Console.WriteLine($"{(parts.Length == 1 ? "ESTRAGOU" : "falhou  ")}  {typed}\n          quer: {want}\n          deu:  {got}");
    }
    Console.WriteLine($"\n{ok}/{total} certas ({100.0 * ok / total:F1}%); {broke} frases que não deviam mudar foram mexidas");
    Directory.Delete(userDir, true);
    return;
}

var words = data.Frequency.Entries
    .Where(e => e.Word.Length >= 4 && e.Word.All(char.IsLetter) && data.Lexicon.IsValid(e.Word))
    .OrderByDescending(e => e.Count).Take(topN).ToList();

var stats = new Dictionary<string, Stat>();
var fails = new Dictionary<string, List<string>>();

foreach (var (word, count) in words)
{
    foreach (var (kind, typo) in Typos(word))
    {
        if (typo == word || data.Lexicon.IsValid(typo)) { Add(kind, "virou outra palavra", count, word, typo, null); continue; }
        var result = corrector.Correct(typo)?.Replacement;
        string outcome = result is null
            ? (data.Frequency.Count(typo) >= Corrector.AttestedCount ? "não corrigiu (erro comum no corpus)" : "não corrigiu")
            : result == word || TextUtil.StripAccents(result) == TextUtil.StripAccents(word) ? "certo" : "ERRADO"; // esta/está conta como certo
        Add(kind, outcome, count, word, typo, result);
    }
}

var pt = CultureInfo.GetCultureInfo("pt-BR");
Console.WriteLine($"{words.Count} palavras, {stats.Values.Sum(s => s.Total)} erros gerados\n");
Console.WriteLine($"{"categoria",-22}{"total",8}{"certo",9}{"não corr.",11}{"ERRADO",9}{"outra pal.",11}   (ponderado pela frequência: certo / ERRADO)");
foreach (var (kind, s) in stats.OrderBy(kv => kv.Key))
    Console.WriteLine(string.Create(pt, $"{kind,-22}{s.Total,8}{s.Pct("certo"),8:F1}%{s.Pct("não corrigiu") + s.Pct("não corrigiu (erro comum no corpus)"),10:F1}%{s.Pct("ERRADO"),8:F1}%{s.Pct("virou outra palavra"),10:F1}%   {s.WPct("certo"),6:F1}% / {s.WPct("ERRADO"),4:F1}%"));
var all = new Stat();
foreach (var s in stats.Values) all.Merge(s);
Console.WriteLine(string.Create(pt, $"{"TOTAL",-22}{all.Total,8}{all.Pct("certo"),8:F1}%{all.Pct("não corrigiu") + all.Pct("não corrigiu (erro comum no corpus)"),10:F1}%{all.Pct("ERRADO"),8:F1}%{all.Pct("virou outra palavra"),10:F1}%   {all.WPct("certo"),6:F1}% / {all.WPct("ERRADO"),4:F1}%"));
Console.WriteLine(string.Create(pt, $"\nDos corrigíveis (sem 'virou outra palavra'): certo {all.PctOfFixable("certo"):F1}%, ERRADO {all.PctOfFixable("ERRADO"):F1}%, erro comum no corpus {all.PctOfFixable("não corrigiu (erro comum no corpus)"):F1}%, desistiu {all.PctOfFixable("não corrigiu"):F1}%"));

foreach (var (key, list) in fails.OrderBy(kv => kv.Key))
    Console.WriteLine($"\n[{key}] " + string.Join("  ", list));

Directory.Delete(userDir, true);

void Add(string kind, string outcome, long weight, string word, string typo, string? result)
{
    if (!stats.TryGetValue(kind, out var s)) stats[kind] = s = new Stat();
    s.Add(outcome, weight);
    if (outcome is "certo" or "virou outra palavra") return;
    var key = $"{kind} / {outcome}";
    if (!fails.TryGetValue(key, out var list)) fails[key] = list = [];
    if (list.Count < samples) list.Add(result is null ? $"{typo}({word})" : $"{typo}→{result}({word})");
}

IEnumerable<(string, string)> Typos(string w)
{
    for (int i = 0; i < w.Length - 1; i++)
        if (w[i] != w[i + 1]) yield return ("inversão", w[..i] + w[i + 1] + w[i] + w[(i + 2)..]);
    for (int i = 0; i < w.Length; i++)
        yield return ("letra faltando", w.Remove(i, 1));
    for (int i = 0; i < w.Length; i++)
        yield return ("letra dobrada", w.Insert(i, w[i].ToString()));
    for (int i = 0; i < w.Length; i++)
        if (neighbors.TryGetValue(TextUtil.StripAccents(w[i].ToString())[0], out var ns))
            foreach (var n in ns)
                yield return ("tecla vizinha", w[..i] + n + w[(i + 1)..]);
    for (int i = 0; i <= w.Length; i += 2) // esbarrou numa tecla ao lado (amostra: posições pares)
    {
        char near = w[Math.Min(i, w.Length - 1)];
        if (neighbors.TryGetValue(TextUtil.StripAccents(near.ToString())[0], out var ns))
            yield return ("tecla a mais", w.Insert(i, ns[0].ToString()));
    }
    var stripped = TextUtil.StripAccents(w);
    if (stripped != w) yield return ("sem acento", stripped);
    foreach (var (from, to) in ((string, string)[])[("ch", "x"), ("x", "ch"), ("ç", "ss"), ("ss", "ç"), ("z", "s"), ("ge", "je"), ("gi", "ji"), ("j", "g")])
        if (w.Contains(from, StringComparison.Ordinal))
        {
            var t = w.Replace(from, to, StringComparison.Ordinal);
            if (t != w) yield return ("fonético", t);
        }
    for (int i = 1; i < w.Length - 1; i++)
        if (w[i] == 's' && "aeiou".Contains(w[i - 1]) && "aeiou".Contains(w[i + 1])) { yield return ("fonético", w[..i] + 'z' + w[(i + 1)..]); break; }
}

sealed class Stat
{
    private readonly Dictionary<string, (long N, long W)> _by = [];
    public long Total { get; private set; }
    public long Weight { get; private set; }

    public void Add(string outcome, long weight)
    {
        var (n, w) = _by.GetValueOrDefault(outcome);
        _by[outcome] = (n + 1, w + weight);
        Total++;
        Weight += weight;
    }

    public void Merge(Stat other)
    {
        foreach (var (k, (n, w)) in other._by)
        {
            var (n0, w0) = _by.GetValueOrDefault(k);
            _by[k] = (n0 + n, w0 + w);
        }
        Total += other.Total;
        Weight += other.Weight;
    }

    public double Pct(string outcome) => Total == 0 ? 0 : 100.0 * _by.GetValueOrDefault(outcome).N / Total;
    public double WPct(string outcome) => Weight == 0 ? 0 : 100.0 * _by.GetValueOrDefault(outcome).W / Weight;

    public double PctOfFixable(string outcome)
    {
        long fixable = Total - _by.GetValueOrDefault("virou outra palavra").N;
        return fixable == 0 ? 0 : 100.0 * _by.GetValueOrDefault(outcome).N / fixable;
    }
}

/// <summary>Corretor ortográfico do próprio Windows (ISpellChecker), só para filtrar candidatas na mineração.</summary>
sealed class WindowsSpeller
{
    private readonly ISpellChecker _checker;
    private WindowsSpeller(ISpellChecker checker) => _checker = checker;

    public static WindowsSpeller? TryCreate(string language)
    {
        try
        {
            var factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass();
            factory.IsSupported(language, out int supported);
            return supported == 0 ? null : new WindowsSpeller(factory.CreateSpellChecker(language));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsValid(string word) => _checker.Check(word).Next(out _) != 0; // S_OK (0) = achou erro

    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    private class SpellCheckerFactoryClass { }

    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Interface)] object SupportedLanguages { get; }
        [System.Runtime.InteropServices.PreserveSig] int IsSupported([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string languageTag, out int supported);
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Interface)] ISpellChecker CreateSpellChecker([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string languageTag);
    }

    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        string LanguageTag { [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] get; }
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Interface)] IEnumSpellingError Check([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string text);
    }

    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("803E3BD4-2828-4410-8290-418D1D73C762"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [System.Runtime.InteropServices.PreserveSig] int Next([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Interface)] out object? error);
    }
}
