namespace Acerto.Core;

/// <summary>Como o erro de digitação se parece. A regra de palavras "atestadas" do Corrector depende disso.</summary>
public enum TypoKind
{
    /// <summary>Duas letras vizinhas trocadas de lugar (apra → para).</summary>
    Transposition,
    /// <summary>Uma letra a mais, igual à vizinha (paara → para).</summary>
    DoubledLetter,
    /// <summary>Faltou uma letra (hje → hoje).</summary>
    MissingLetter,
    /// <summary>Letra ou dígrafo de som igual: enxente → enchente, paralização → paralisação, susseço → sucesso.</summary>
    Phonetic,
    Other,
}

/// <param name="Score">ln(contagem) − peso × custo do erro; serve para comparar com outras explicações.</param>
public readonly record struct TypoMatch(string Word, long Count, TypoKind Kind, double Score);

/// <summary>
/// Pesos do modelo de erro e limiares de decisão. Os valores padrão foram ajustados com tests/Acerto.Eval
/// (erros sintéticos nas palavras mais usadas) sob a condição de os testes de corpus continuarem passando.
/// </summary>
public sealed record TypoOptions
{
    /// <summary>Quanto um erro "custa" em relação à frequência: score = ln(contagem) − CostWeight × custo.</summary>
    public double CostWeight { get; init; } = 5.0;
    /// <summary>Folga mínima de score entre a melhor candidata e a segunda. Abaixo disso, desiste.</summary>
    public double Margin { get; init; } = 1.2;
    public long MinBestCount { get; init; } = 50;

    public double Transposition { get; init; } = 0.4;
    /// <summary>Letra a mais igual à vizinha.</summary>
    public double DoubledLetter { get; init; } = 0.5;
    /// <summary>Letra a mais que é tecla vizinha de uma letra ao lado (esbarrão).</summary>
    public double NeighborInsert { get; init; } = 0.75;
    /// <summary>Faltou uma das letras de um par dobrado (caro → carro).</summary>
    public double MissingDoubled { get; init; } = 0.6;
    public double NeighborSubstitution { get; init; } = 0.6;
    /// <summary>Troca por letra de som parecido (s/z, g/j, m/n...).</summary>
    public double PhoneticSubstitution { get; init; } = 0.7;

    /// <summary>Troca de erro de escrita (s/z, s/c, g/j, x/s, m/n, x/ch, ss/ç): o erro mais comum de quem escreve.</summary>
    public double SpellingSubstitution { get; init; } = 0.7;
    /// <summary>A primeira letra quase nunca sai errada: mexer nela custa a mais.</summary>
    public double FirstLetterPenalty { get; init; } = 0.4;
    /// <summary>Faltou uma letra qualquer.</summary>
    public double MissingLetter { get; init; } = 0.7;
    /// <summary>
    /// O digitado é o começo exato da candidata (corrig → corrigir): cada letra que falta no fim custa só isto.
    /// É o "completar palavra" do celular; bem mais provável do que dois erros no meio.
    /// </summary>
    public double PrefixCompletion { get; init; } = 0.4;
    /// <summary>Sobrou uma letra que não é dobrada nem tecla vizinha (raro ao digitar).</summary>
    public double ExtraLetter { get; init; } = 1.3;

    /// <summary>
    /// Quando falta só o acento (maos → mãos) essa é quase sempre a explicação certa. O erro de digitação só ganha
    /// do acento se o score dele for pelo menos isso maior que ln(contagem) da forma acentuada (vaos → vamos, não vãos).
    /// </summary>
    public double AccentYield { get; init; } = 4.0;

    /// <summary>
    /// Palavra que o dicionário aceita mas que nunca aparece no corpus (erado, de "erar") só é tratada como erro de
    /// digitação se a correção for pelo menos tão usada assim (errado). Formas raras de verbo continuam protegidas:
    /// as vizinhas delas não chegam nem perto dessa contagem.
    /// </summary>
    public long RareValidMinCandidate { get; init; } = 20000;

    /// <summary>
    /// Erros que muita gente comete também aparecem no corpus ("atestados"). 0 = nunca corrigir palavra atestada.
    /// Acima de 0: corrige quando o erro é um deslize (inversão, letra dobrada, letra faltando) e a correção é
    /// pelo menos isso vezes mais usada que a forma digitada.
    /// </summary>
    public long AttestedSlipRatio { get; init; }

    /// <summary>O mesmo para os outros tipos de erro (troca de letra, letra sobrando), onde há mais coincidência
    /// com palavras estrangeiras. 0 = nunca.</summary>
    public long AttestedOtherRatio { get; init; }

    /// <summary>
    /// O mesmo para a troca fonética (<see cref="TypoKind.Phonetic"/>): "paralização" aparece no corpus porque muita
    /// gente erra assim, não porque exista. 0 = nunca.
    /// </summary>
    public long AttestedPhoneticRatio { get; init; }

    /// <summary>
    /// Peso do contexto (0 a 1) na frequência de cada candidata: contagem efetiva =
    /// (1 − peso) × frequência geral + peso × N × P(palavra | palavra anterior). Par nunca visto perde um pouco,
    /// par comum ganha muito ("minha cas" → casa; "esse cas" → caso).
    /// </summary>
    public double ContextWeight { get; init; } = 0.75;

    /// <summary>
    /// Com contexto, troca só de acento entre duas palavras que existem ("ele esta" → "ele está") quando a forma
    /// acentuada é pelo menos isso vezes mais provável depois da palavra anterior. 0 = nunca.
    /// </summary>
    public double ContextAccentRatio { get; init; }

    /// <summary>O par (anterior, acentuada) tem que aparecer pelo menos isso no corpus para valer como evidência.</summary>
    public int ContextMinPairCount { get; init; } = 5;

    /// <summary>
    /// Outra porta para a troca de acento por contexto, olhando só os pares: depois da palavra anterior, a acentuada
    /// aparece pelo menos isso vezes mais que a digitada ("essa é": 515 × "essa e": 27). A regra acima mistura a
    /// frequência geral, e palavras comuníssimas como "e" escondem o contexto. 0 = desligada.
    /// Simulado nos 758 pares de acento do corpus: +21,8 mil acertos e +500 trocas indevidas (15×, mínimo 20).
    /// </summary>
    public int ContextPairRatio { get; init; }

    /// <summary>O par (anterior, acentuada) tem que aparecer pelo menos isso para a porta acima valer.</summary>
    public int ContextPairMinCount { get; init; } = 20;

    /// <summary>
    /// Troca de acento olhando também a palavra seguinte ("isso e muito" → "é"): a acentuada tem que ser pelo menos
    /// isso vezes mais provável que a digitada, contando a anterior e a seguinte. 0 = desligada.
    /// </summary>
    public double NextAccentRatio { get; init; }

    /// <summary>O par (acentuada, seguinte) tem que aparecer pelo menos isso no corpus para a regra acima valer.</summary>
    public int NextMinPairCount { get; init; } = 5;

    /// <summary>
    /// A lista embutida perde para a vizinha que a palavra anterior pede ("bom dai" → dia, não daí) quando o par
    /// (anterior, vizinha) aparece pelo menos isso no corpus e (anterior, da lista) nunca. 0 = a lista sempre ganha.
    /// </summary>
    public int FixedContextMinPair { get; init; }

    /// <summary>Palavras de 3 letras também são corrigidas quando há inversão (cmo → com).</summary>
    public bool ShortWordTransposition { get; init; }

    /// <summary>
    /// Perfil padrão do app: tenta sempre, como o teclado do celular. Escolhe a melhor candidata mesmo quando a
    /// segunda está perto e corrige erros comuns do corpus. Às vezes troca um nome ou uma palavra estrangeira em
    /// minúsculo; a pessoa apaga, digita de novo e o Acertô aprende.
    /// </summary>
    // Limites de "atestados" escolhidos pelo usuário em 2026-11-12 ("melhor tentar e errar do que não corrigir"),
    // sabendo o preço medido: com 30/50 o acerto vai a 94,2% e 1636 das 60 mil palavras frequentes do corpus passam
    // a ser trocadas, várias em inglês digitadas em minúsculo (put→puta, one→onde, list→lista, save→sabe).
    // Com 300/3000 eram 227. Uma lista de palavras em inglês/espanhol em data/protegidas.txt resolveria sem perder acerto.
    public static TypoOptions Aggressive { get; } = new()
    {
        Margin = 0,
        MinBestCount = 20,
        AttestedSlipRatio = 30,
        AttestedOtherRatio = 50,
        ShortWordMinCount = 5000,
        ShortWordTransposition = true,
        RareValidMinCandidate = 5000,
        AccentYield = 3.0,
        ContextAccentRatio = 10,
        ContextPairRatio = 15,
        // medido em 30 mil frases de notícias fora da amostra (AcertoEval direita): volta 34% dos acentos que a
        // regra da palavra anterior deixava passar (e→é, esta→está, ha→há), estragando 0,054% das palavras certas.
        // 10 → 7: +294 acertos, +58 estragos; 5 já troca "maior e melhor" → é
        NextAccentRatio = 7,
        // "bom dai" → dia. Com 200 não muda nada nas 30 mil frases de notícias (com 50, B cai 0,1 ponto)
        FixedContextMinPair = 200,
        // erro de escrita (enxente, xinelo, paralização): razão 10 troca 21 palavras frequentes a mais do corpus, metade
        // correções boas (garçon, marron, obsecado); custo 0,4 leva os erros fonéticos gerados de 84,9% para 87,3% de
        // acerto (errado 5,2% → 2,7%) sem mudar as trocas
        AttestedPhoneticRatio = 10,
        SpellingSubstitution = 0.4,
    };

    /// <summary>Perfil cuidadoso: na dúvida, não troca. É o que os testes de corpus exigem com zero trocas indevidas.</summary>
    public static TypoOptions Careful { get; } = new();

    /// <summary>A forma acentuada precisa ser pelo menos tão usada assim para a regra de acento valer (icar não vira "içar").</summary>
    public long AccentMinCount { get; init; } = 200;

    /// <summary>
    /// Palavras digitadas com 3 letras só são corrigidas quando falta uma letra e a correção é pelo menos tão usada
    /// assim (hje → hoje). Inversão fica de fora (cmo: "com" ou "como"?), assim como siglas (cpf, pdf, sql).
    /// </summary>
    public long ShortWordMinCount { get; init; } = 20000;
}

/// <summary>
/// Acha a palavra que a pessoa quis digitar quando errou letras (troca, falta, sobra, inversão).
/// Só responde quando uma candidata ganha com folga; na dúvida devolve null.
/// </summary>
public sealed class TypoRanker
{
    private const int MinIndexCount = 5;
    private const double MaxCost = 2.0;

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz";
    private static readonly string[] Abnt2Rows = ["qwertyuiop", "asdfghjklç", "zxcvbnm"];
    private static readonly Dictionary<char, (int Row, int Col)> KeyPos = BuildKeyPos();
    private static readonly HashSet<(char, char)> Phonetic = BuildPhonetic("sz", "sc", "gj", "mn", "ei", "ou", "lu", "xs", "xz", "cq", "ck");
    // só os pares de erro de escrita contam como TypoKind.Phonetic: k/c e vogais trocadas são mais nome estrangeiro
    // (klara, lukas, morino) do que erro, e ficam com a razão das outras trocas
    private static readonly HashSet<(char, char)> Spelling = BuildPhonetic("sz", "sc", "gj", "mn", "xs", "xz");

    private TypoOptions _o;
    /// <summary>Pode ser trocado em uso (interruptor "corrigir mais" nas configurações).</summary>
    public TypoOptions Options { get => _o; set => _o = value; }
    // forma sem acento → palavras reais (com acento) e contagem
    private readonly Dictionary<string, List<(string Word, long Count)>> _byStripped = new(StringComparer.Ordinal);
    // (1ª letra, tamanho) → formas sem acento; usado na busca de distância 2
    private readonly Dictionary<(char, int), List<string>> _buckets = [];

    private readonly BigramModel _bigrams;
    private readonly UserHistory _history;
    private readonly long _corpusSize;

    /// <summary>
    /// Peso do jeito do usuário na mistura com o corpus: a probabilidade da palavra no que ele escreve, vezes isto,
    /// soma-se à do corpus. Uma palavra que ele usa (em proporção) 5× mais que o corpus ganha dele o mesmo tanto
    /// que já tinha do corpus.
    /// shortcut: valores escolhidos sem medição; medir com histórico simulado se incomodar.
    /// </summary>
    public const double UserWeight = 0.2;
    /// <summary>Palavras "imaginárias" somadas ao total do usuário: nos primeiros dias poucas palavras não mandam em tudo.</summary>
    public const double UserSmoothing = 500;
    /// <summary>O mesmo para os pares: um único uso depois de uma palavra não vira certeza.</summary>
    public const double UserPairSmoothing = 5;

    public TypoRanker(Lexicon lex, FrequencyTable freq, TypoOptions? options = null, BigramModel? bigrams = null,
        UserHistory? history = null)
    {
        _o = options ?? new TypoOptions();
        _bigrams = bigrams ?? BigramModel.Empty;
        _history = history ?? UserHistory.Empty;
        _corpusSize = Math.Max(freq.Total, 1);
        foreach (var (word, count) in freq.Entries)
        {
            if (count < MinIndexCount || !word.All(char.IsLetter) || !lex.IsValid(word)) continue;
            var stripped = TextUtil.StripAccents(word);
            if (!_byStripped.TryGetValue(stripped, out var list))
            {
                _byStripped[stripped] = list = [];
                var key = (stripped[0], stripped.Length);
                if (!_buckets.TryGetValue(key, out var b)) _buckets[key] = b = [];
                b.Add(stripped);
            }
            list.Add((word, count));
        }
    }

    /// <summary>Todas as palavras válidas conhecidas, com a forma sem acento e a contagem (para as sugestões).</summary>
    public IEnumerable<(string Stripped, string Word, long Count)> IndexedWords =>
        _byStripped.SelectMany(kv => kv.Value.Select(w => (kv.Key, w.Word, w.Count)));

    /// <summary>
    /// Frequência da palavra levando em conta a palavra anterior e o que o usuário costuma escrever. Sem contexto
    /// (ou contexto desconhecido) e sem histórico é a contagem geral.
    /// </summary>
    public double EffectiveCount(string word, long count, string? prev)
    {
        double user = 0;
        if (!_history.IsEmpty)
        {
            double alone = _history.Count(word) / (_history.Total + UserSmoothing);
            int context = prev is null ? 0 : _history.ContextTotal(prev);
            double p = context == 0 ? alone
                : (1 - _o.ContextWeight) * alone + _o.ContextWeight * _history.PairCount(prev!, word) / (context + UserPairSmoothing);
            user = UserWeight * _corpusSize * p;
        }
        if (prev is null) return count + user;
        long total = _bigrams.ContextTotal(prev);
        if (total == 0) return count + user;
        double afterPrev = (double)_bigrams.Count(prev, word) / total;
        return (1 - _o.ContextWeight) * count + _o.ContextWeight * _corpusSize * afterPrev + user;
    }

    /// <summary>
    /// Começos de palavra a um erro de <paramref name="stripped"/> (sem acento), com o custo do erro: para sugerir
    /// "obrigado" quando já se digitou "obrgi".
    /// </summary>
    public IEnumerable<(string Prefix, double Cost)> NearPrefixes(string stripped) =>
        stripped.Length == 0 || !stripped.All(c => c is >= 'a' and <= 'z') ? []
            : Edits1(stripped).Where(e => e.Length > 0 && e != stripped).Distinct().Select(e => (e, Cost(stripped, e)));

    /// <param name="prev">Palavra anterior em minúsculas, <see cref="BigramModel.SentenceStart"/> ou null se desconhecida.</param>
    /// <param name="options">Limiares desta consulta; null = <see cref="Options"/>.</param>
    public TypoMatch? Best(string lowerWord, string? prev = null, TypoOptions? options = null)
    {
        var o = options ?? _o;
        var ranked = Candidates(lowerWord, prev);
        if (ranked.Count == 0) return null;
        var best = ranked[0];
        if (best.Count < o.MinBestCount) return null;
        if (ranked.Count > 1 && best.Score - ranked[1].Score < o.Margin) return null;
        return new TypoMatch(best.Word, best.Count, Classify(TextUtil.StripAccents(lowerWord), TextUtil.StripAccents(best.Word)), best.Score);
    }

    /// <summary>Todas as candidatas, da melhor para a pior. Público para diagnóstico (tests/Acerto.Eval).</summary>
    public IReadOnlyList<(string Word, long Count, double Cost, double Score)> Candidates(string lowerWord, string? prev = null)
    {
        var typed = TextUtil.StripAccents(lowerWord);
        if (typed.Length == 0 || !typed.All(c => c is >= 'a' and <= 'z')) return [];

        var cands = new Dictionary<string, double>(StringComparer.Ordinal); // forma sem acento → custo
        foreach (var e in Edits1(typed))
            if (e != typed && _byStripped.ContainsKey(e)) cands.TryAdd(e, Cost(typed, e));

        // dígrafos de som igual (x/ch, ss/ç): um erro de escrita, não dois de digitação (enxente → enchente)
        foreach (var e in PhoneticRewrites(typed))
            if (_byStripped.ContainsKey(e))
                cands[e] = Math.Min(cands.GetValueOrDefault(e, double.MaxValue), _o.SpellingSubstitution + (e[0] != typed[0] ? _o.FirstLetterPenalty : 0));

        if (cands.Count == 0 && typed.Length >= 6)
        {
            // distância 2: só palavras com a mesma 1ª letra e tamanho parecido
            for (int len = typed.Length - 2; len <= typed.Length + 2; len++)
                if (_buckets.TryGetValue((typed[0], len), out var bucket))
                    foreach (var s in bucket)
                    {
                        var c = Cost(typed, s);
                        if (c <= MaxCost) cands.TryAdd(s, c);
                    }
        }

        // "Completar palavra" do celular usado como correção: o digitado é o começo exato de uma palavra duas letras
        // maior (corrig → corrigir). Com uma letra a mais ela já entrou acima como "letra faltando".
        if (typed.Length >= 4 && _buckets.TryGetValue((typed[0], typed.Length + 2), out var longer))
            foreach (var s in longer)
                if (s.StartsWith(typed, StringComparison.Ordinal)) cands.TryAdd(s, Cost(typed, s));

        var ranked = new List<(string Word, long Count, double Cost, double Score)>();
        foreach (var (stripped, cost) in cands)
            foreach (var (word, count) in _byStripped[stripped])
                ranked.Add((word, count, cost, Math.Log(Math.Max(EffectiveCount(word, count, prev), 0.01)) - _o.CostWeight * cost));
        ranked.Sort((x, y) => y.Score.CompareTo(x.Score));
        return ranked;
    }

    private static TypoKind Classify(string typed, string cand)
    {
        if (typed.Length == cand.Length)
        {
            int i = 0;
            while (i < typed.Length && typed[i] == cand[i]) i++;
            if (i < typed.Length - 1 && typed[i] == cand[i + 1] && typed[i + 1] == cand[i]
                && typed.AsSpan(i + 2).SequenceEqual(cand.AsSpan(i + 2)))
                return TypoKind.Transposition;
        }
        else if (typed.Length == cand.Length + 1)
        {
            int i = 0;
            while (i < cand.Length && typed[i] == cand[i]) i++;
            // typed[i] é a letra a mais; tem que ser igual a uma vizinha e o resto bater
            if (typed.AsSpan(i + 1).SequenceEqual(cand.AsSpan(i))
                && ((i > 0 && typed[i] == typed[i - 1]) || (i + 1 < typed.Length && typed[i] == typed[i + 1])))
                return TypoKind.DoubledLetter;
        }
        else if (typed.Length == cand.Length - 1)
        {
            int i = 0;
            while (i < typed.Length && typed[i] == cand[i]) i++;
            if (typed.AsSpan(i).SequenceEqual(cand.AsSpan(i + 1))) return TypoKind.MissingLetter;
        }
        if (typed.Length == cand.Length)
        {
            int diff = -1;
            for (int i = 0; i < typed.Length; i++)
                if (typed[i] != cand[i]) { if (diff >= 0) { diff = -2; break; } diff = i; }
            if (diff >= 0 && Spelling.Contains((typed[diff], cand[diff]))) return TypoKind.Phonetic;
        }
        foreach (var r in PhoneticRewrites(typed))
            if (r == cand) return TypoKind.Phonetic;
        return TypoKind.Other;
    }

    // em formas sem acento: ç já virou c
    private static readonly (string From, string To)[] Digraphs = [("x", "ch"), ("ch", "x"), ("c", "ss"), ("ss", "c")];

    /// <summary>A palavra com uma ocorrência de um dígrafo trocada pelo de som igual.</summary>
    private static IEnumerable<string> PhoneticRewrites(string w)
    {
        foreach (var (from, to) in Digraphs)
            for (int i = w.IndexOf(from, StringComparison.Ordinal); i >= 0; i = w.IndexOf(from, i + 1, StringComparison.Ordinal))
                yield return string.Concat(w.AsSpan(0, i), to, w.AsSpan(i + from.Length));
    }

    private static IEnumerable<string> Edits1(string w)
    {
        for (int i = 0; i <= w.Length; i++)
        {
            if (i < w.Length) yield return w.Remove(i, 1);                                          // sobra
            if (i < w.Length - 1) yield return string.Concat(w.AsSpan(0, i), [w[i + 1], w[i]], w.AsSpan(i + 2)); // inversão
            foreach (var c in Alphabet)
            {
                if (i < w.Length && c != w[i]) yield return string.Concat(w.AsSpan(0, i), [c], w.AsSpan(i + 1)); // troca
                yield return w.Insert(i, c.ToString());                                            // falta
            }
        }
    }

    /// <summary>
    /// Distância Damerau (OSA) com pesos do modelo de erro: <paramref name="a"/> é o digitado, <paramref name="b"/> a
    /// candidata. Para cedo acima de MaxCost.
    /// </summary>
    private double Cost(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return double.MaxValue;
        if (b.Length > a.Length && b.StartsWith(a, StringComparison.Ordinal)) return (b.Length - a.Length) * _o.PrefixCompletion;
        var d = new double[a.Length + 1, b.Length + 1];
        for (int i = 1; i <= a.Length; i++) d[i, 0] = d[i - 1, 0] + ExtraCost(a, i - 1);
        for (int j = 1; j <= b.Length; j++) d[0, j] = d[0, j - 1] + MissingCost(b, j - 1);
        for (int i = 1; i <= a.Length; i++)
        {
            double rowMin = double.MaxValue;
            for (int j = 1; j <= b.Length; j++)
            {
                char x = a[i - 1], y = b[j - 1];
                double sub = x == y ? 0 : Adjacent(x, y) ? _o.NeighborSubstitution
                    : Spelling.Contains((x, y)) ? _o.SpellingSubstitution : Phonetic.Contains((x, y)) ? _o.PhoneticSubstitution : 1.0;
                double v = Math.Min(Math.Min(d[i - 1, j] + ExtraCost(a, i - 1), d[i, j - 1] + MissingCost(b, j - 1)), d[i - 1, j - 1] + sub);
                if (i > 1 && j > 1 && x == b[j - 2] && a[i - 2] == y && x != y)
                    v = Math.Min(v, d[i - 2, j - 2] + _o.Transposition);
                d[i, j] = v;
                rowMin = Math.Min(rowMin, v);
            }
            if (rowMin > MaxCost) return double.MaxValue;
        }
        double cost = d[a.Length, b.Length];
        // 1ª letra diferente pesa (a não ser que sejam só as duas primeiras invertidas: "apra")
        if (a[0] != b[0] && !(a.Length > 1 && b.Length > 1 && a[0] == b[1] && a[1] == b[0]))
            cost += _o.FirstLetterPenalty;
        return cost;
    }

    /// <summary>Custo de uma letra a mais no digitado (posição <paramref name="i"/>).</summary>
    private double ExtraCost(string a, int i)
    {
        char c = a[i];
        bool left = i > 0, right = i + 1 < a.Length;
        if ((left && a[i - 1] == c) || (right && a[i + 1] == c)) return _o.DoubledLetter;
        if ((left && Adjacent(a[i - 1], c)) || (right && Adjacent(a[i + 1], c))) return _o.NeighborInsert;
        return _o.ExtraLetter;
    }

    /// <summary>Custo de uma letra da candidata que faltou no digitado (posição <paramref name="j"/> da candidata).</summary>
    private double MissingCost(string b, int j) =>
        (j > 0 && b[j - 1] == b[j]) || (j + 1 < b.Length && b[j + 1] == b[j]) ? _o.MissingDoubled : _o.MissingLetter;

    private static bool Adjacent(char x, char y)
    {
        if (!KeyPos.TryGetValue(x, out var p) || !KeyPos.TryGetValue(y, out var q)) return false;
        if (p.Row == q.Row) return Math.Abs(p.Col - q.Col) == 1;
        return Math.Abs(p.Row - q.Row) == 1 && Math.Abs(p.Col - q.Col) <= 1;
    }

    private static Dictionary<char, (int, int)> BuildKeyPos()
    {
        var map = new Dictionary<char, (int, int)>();
        for (int r = 0; r < Abnt2Rows.Length; r++)
            for (int c = 0; c < Abnt2Rows[r].Length; c++)
                map[Abnt2Rows[r][c]] = (r, c);
        return map;
    }

    private static HashSet<(char, char)> BuildPhonetic(params string[] pairs)
    {
        var set = new HashSet<(char, char)>();
        foreach (var p in pairs)
        {
            set.Add((p[0], p[1]));
            set.Add((p[1], p[0]));
        }
        return set;
    }
}
