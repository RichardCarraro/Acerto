using System.Text;

namespace Acerto.Core;

/// <summary>
/// O que o host faz com uma tecla. Se <see cref="Swallow"/>, a tecla não chega ao app:
/// o host envia <see cref="Backspaces"/> × Backspace e depois <see cref="Insert"/> ('\n' = Enter, '\t' = Tab).
/// <see cref="WordLength"/> é o tamanho da palavra corrigida no começo de <see cref="Insert"/> (0 se não houve
/// correção de palavra); o host usa para animar a palavra.
/// </summary>
public readonly record struct EngineAction(bool Swallow, int Backspaces, string Insert, int WordLength = 0)
{
    public static readonly EngineAction None = new(false, 0, "");
}

/// <summary>
/// Acompanha o que a pessoa digita, caractere a caractere, e decide as correções.
/// Só corrige o que viu nascer: depois de um Reset, espera o próximo espaço/Enter/Tab
/// ou a confirmação do host sobre o que há antes do cursor.
/// </summary>
/// <param name="learn">Chamado com a palavra que o usuário mostrou querer: apagou a correção e digitou de novo.
/// O host decide quando gravar (o hook do Windows não pode fazer I/O).</param>
public sealed class TextEngine(Corrector corrector, Action<string> learn)
{
    private const string Prefixes = "(\"“['";
    private const string ClosingEnds = ".,;:!?)]}\"”»";
    private const string DeferredEnds = ".:"; // podem ser meio de e-mail, domínio ou arquivo

    private readonly record struct Deferred(string Word, string Result, char End);
    /// <summary>Correção que acabou de entrar (palavra + espaço), enquanto nada mais foi digitado.</summary>
    /// <param name="Suffix">Correção pela palavra seguinte: o que veio depois (" muito"); o Tab desfaz as duas.</param>
    /// <param name="Learns">Desfazer ensina a palavra ("nao" de propósito). Não para "e"/"é": as duas são normais.</param>
    private readonly record struct Undo(string Typed, string Result, string? Prev, string Suffix = "", bool Learns = true);
    /// <summary>Palavra que ficou como estava, seguida de espaço: a próxima pode mostrar que faltou acento ("e muito").</summary>
    private readonly record struct Pending(string Word, string? Prev);

    private readonly StringBuilder _word = new();
    private bool _tokenKnown;      // vimos o começo deste token?
    private bool _tokenPure = true; // só prefixos + letras/hífen até agora
    private bool _tokenHadEnd;     // já passou pontuação neste token ("www.site")
    private bool _capNext;
    private bool _lastWasDoubleSpace;
    private char _prev1, _prev2;   // dois últimos caracteres do texto ('\0' = desconhecido)
    private Deferred? _deferred;   // correção esperando ver o que vem depois de "." ou ":"
    private string? _prev;         // palavra anterior (minúsculas), início de frase, ou null se não se sabe
    private Undo? _undo;           // logo depois de uma correção, a barra oferece o que foi digitado e alternativas
    private Pending? _pending;     // palavra anterior, candidata a ganhar acento quando esta terminar
    private string? _lastByNext;   // última palavra que ganhou acento pela seguinte, como foi digitada
    private bool _byNextUndone;    // e a pessoa apagou depois: não insistir na mesma
    private readonly StringBuilder _sinceReset = new(); // digitado desde o Reset, enquanto o contexto é desconhecido

    // Última palavra corrigida, como foi digitada. Se o usuário apagar e digitar a mesma de novo, é de propósito.
    private string? _lastCorrectedWord;
    private bool _lastCorrectionLearns;
    private bool _backspacedSinceCorrection;

    /// <summary>Maiúscula automática no começo da frase.</summary>
    public bool AutoCapitalize { get; set; } = true;

    /// <summary>O campo em foco pede o perfil cuidadoso (barra de endereço). Ver <see cref="Corrector.CarefulField"/>.</summary>
    public bool CarefulField
    {
        get => corrector.CarefulField;
        set => corrector.CarefulField = value;
    }

    /// <summary>Dois espaços seguidos viram ". ".</summary>
    public bool DoubleSpacePeriod { get; set; } = true;

    /// <summary>Muda a cada Reset; o host usa para descartar respostas de contexto atrasadas.</summary>
    public long Generation { get; private set; }

    /// <summary>
    /// O host diz o que havia antes do cursor (ex.: via UI Automation). Se o que foi digitado desde o Reset
    /// bate com o final desse texto e antes disso há início de campo, espaço ou prefixo, a primeira palavra
    /// passa a poder ser corrigida (e ganha maiúscula se o campo estava vazio ou depois de . ! ?).
    /// </summary>
    /// <returns>true se o contexto foi aceito.</returns>
    public bool ConfirmContext(long generation, string textBeforeCaret)
    {
        if (generation != Generation || _tokenKnown) return false;
        var typed = _sinceReset.ToString();
        if (!textBeforeCaret.EndsWith(typed, StringComparison.Ordinal)) return false;
        var context = textBeforeCaret[..^typed.Length];
        char prev = context.Length == 0 ? '\0' : context[^1];
        if (!(prev == '\0' || char.IsWhiteSpace(prev) || Prefixes.Contains(prev))) return false;

        _tokenKnown = true;
        var trimmed = context.TrimEnd(' ', '\t');
        _capNext = trimmed.Length == 0 || trimmed[^1] is '.' or '!' or '?' or '\n' or '\r';
        _prev = _capNext ? BigramModel.SentenceStart
            : trimmed.Length < context.Length ? LastWord(trimmed)   // há espaço antes do cursor: a palavra anterior é conhecida
            : null;
        if (typed.Length == 0)
        {
            _prev1 = prev;
            _prev2 = context.Length > 1 ? context[^2] : '\0';
        }
        return true;
    }

    /// <summary>O motor sabe onde está (começo de campo, depois de espaço...). Falso depois de um Reset, até confirmar.</summary>
    public bool ContextKnown => _tokenKnown;

    /// <summary>Tamanho da palavra em andamento (o host usa para achar onde ela está na tela).</summary>
    public int CurrentWordLength => _word.Length;

    /// <summary>
    /// Sugestões para o ponto atual: completar a palavra em andamento ou, logo depois de um espaço, a próxima palavra.
    /// Vazio quando o contexto não é conhecido ou a palavra já está completa e é comum.
    /// </summary>
    public IReadOnlyList<string> Suggestions(int max = 3)
    {
        if (_undo is { } u) return Alternatives(u, max);
        if (!_tokenKnown || !_tokenPure || _tokenHadEnd || _deferred is not null) return [];
        var typed = _word.ToString();
        if (typed.Length == 0 && _prev1 != ' ') return [];              // próxima palavra só logo depois de um espaço
        var list = corrector.Suggester.Suggest(_prev, typed, max);
        bool upper = typed.Length > 0 ? char.IsUpper(typed[0]) : AutoCapitalize && _capNext;
        return upper ? list.Select(s => char.ToUpper(s[0], TextUtil.PtBr) + s[1..]).ToList() : list;
    }

    /// <summary>
    /// Logo depois de uma correção: primeiro o que a pessoa digitou (Tab desfaz), depois outras palavras que o
    /// digitado poderia ser. Com a maiúscula que a correção usou.
    /// </summary>
    private IReadOnlyList<string> Alternatives(Undo u, int max)
    {
        var typed = u.Typed.ToLower(TextUtil.PtBr);
        var result = u.Result.ToLower(TextUtil.PtBr);
        var list = new List<string> { typed };
        if (u.Suffix.Length == 0) // pela seguinte só desfaz: outra palavra no lugar de "e" raramente faz sentido
            foreach (var c in corrector.Typos.Candidates(typed, u.Prev))
                if (list.Count < max && c.Word != result && !list.Contains(c.Word)) list.Add(c.Word);
        bool upper = char.IsUpper(u.Result[0]);
        return list.Select(w => w == typed ? u.Typed : upper ? char.ToUpper(w[0], TextUtil.PtBr) + w[1..] : w).ToList();
    }

    /// <summary>
    /// A pessoa aceitou uma sugestão: troca o que foi digitado da palavra pela sugestão seguida de espaço,
    /// e o motor segue como se ela tivesse sido digitada inteira. Logo depois de uma correção, troca a palavra
    /// corrigida; se a escolhida for a que foi digitada, aprende (como apagar e digitar de novo).
    /// </summary>
    public EngineAction AcceptSuggestion(string suggestion)
    {
        int typed = _word.Length;
        string suffix = "";
        if (_undo is { } u)
        {
            suffix = u.Suffix;
            typed = u.Result.Length + suffix.Length + 1; // a palavra corrigida (e a seguinte, se foi por ela) e o espaço
            if (u.Learns && string.Equals(suggestion, u.Typed, StringComparison.OrdinalIgnoreCase)) learn(u.Typed);
            _undo = null;
        }
        else corrector.Record(_prev, suggestion);
        var text = suggestion + suffix;
        _prev = LastWord(text);
        _capNext = false;
        _prev2 = text[^1];
        _prev1 = ' ';
        _lastWasDoubleSpace = false;
        _deferred = null;
        _pending = null;
        _lastCorrectedWord = null;
        EndToken();
        return new EngineAction(true, typed, text + " ");
    }

    /// <summary>O host perdeu o fio (clique, troca de janela, seta, atalho): esquece tudo.</summary>
    public void Reset()
    {
        _lastCorrectedWord = null;
        _lastByNext = null;
        ResetState();
    }

    private void ResetState()
    {
        Generation++;
        _undo = null;
        _sinceReset.Clear();
        _word.Clear();
        _tokenKnown = false;
        _tokenPure = true;
        _tokenHadEnd = false;
        _capNext = false;
        _lastWasDoubleSpace = false;
        _prev1 = _prev2 = '\0';
        _deferred = null;
        _pending = null;
        _prev = null;
    }

    public EngineAction OnChar(char c)
    {
        _undo = null;
        if (!_tokenKnown) _sinceReset.Append(c);
        bool whitespace = c is ' ' or '\n' or '\t';

        if (_deferred is { } d)
        {
            _deferred = null;
            if (c is ' ' or '\n')
            {
                OnWordEnd(c, whitespace: true); // atualiza estado; a palavra já foi consumida
                return new EngineAction(true, d.Word.Length + 1, d.Result + d.End + c, d.Result.Length);
            }
        }

        if (whitespace || (ClosingEnds.Contains(c) && !(c is '"' && _word.Length == 0 && !_tokenHadEnd)))
            return OnWordEnd(c, whitespace);

        if (char.IsLetter(c) || (c == '-' && _word.Length > 0))
        {
            if (_tokenHadEnd) _tokenPure = false;
            _word.Append(c);
        }
        else if (!(Prefixes.Contains(c) && _word.Length == 0 && !_tokenHadEnd))
        {
            _tokenPure = false; // dígito, @, /, \, _ ... ou prefixo no meio
            _word.Clear();
        }
        Push(c);
        _lastWasDoubleSpace = false;
        return EngineAction.None;
    }

    /// <summary>Backspace nunca é engolido: só apaga, como em qualquer texto.</summary>
    public EngineAction OnBackspace()
    {
        _deferred = null;
        _undo = null;
        _pending = null;
        if (_lastCorrectedWord is not null) _backspacedSinceCorrection = true;
        if (_lastByNext is not null) _byNextUndone = true;
        if (_word.Length == 0)
        {
            ResetState(); // apagou para dentro de algo que não estamos vendo
            return EngineAction.None;
        }
        _word.Length--;
        if (_sinceReset.Length > 0) _sinceReset.Length--;
        _prev1 = _word.Length > 0 ? _word[^1] : '\0';
        _prev2 = '\0';
        return EngineAction.None;
    }

    private EngineAction OnWordEnd(char c, bool whitespace)
    {
        var action = EngineAction.None;

        if (DoubleSpacePeriod && c == ' ' && _prev1 == ' ' && char.IsLetterOrDigit(_prev2) && !_lastWasDoubleSpace)
        {
            _capNext = true;
            _prev2 = '.';
            _prev1 = ' ';
            _lastWasDoubleSpace = true;
            _prev = BigramModel.SentenceStart;
            EndToken();
            return new EngineAction(true, 1, ". ");
        }

        bool hadWord = _word.Length > 0;
        var pending = _pending;
        _pending = null;
        string? completed = _tokenKnown && _tokenPure && hadWord ? _word.ToString() : null; // palavra que acabou de fechar

        // Tab não corrige: em muitos apps ele aceita uma sugestão do próprio app
        if (completed is not null && c != '\t')
        {
            var word = completed;
            var corr = corrector.Correct(word, atSentenceStart: _capNext, prev: _prev);

            if (corr is not null && _backspacedSinceCorrection
                && string.Equals(word, _lastCorrectedWord, StringComparison.OrdinalIgnoreCase))
            {
                // Apagou a correção e digitou a mesma palavra: é de propósito. Fica como está e aprende.
                if (_lastCorrectionLearns) learn(word);
                _lastCorrectedWord = null;
                corr = null;
            }
            else if (corr is not null)
            {
                _lastCorrectedWord = word;
                _lastCorrectionLearns = corr.Kind is not CorrectionKind.Shortcut;
                _backspacedSinceCorrection = false;
            }
            else
            {
                _lastCorrectedWord = null;
            }

            var result = corr?.Replacement ?? word;
            if (AutoCapitalize && _capNext && char.IsLower(word[0]) && result.Length > 0 && !TextUtil.IsMixedCase(word)) // "iPhone" fica
                result = char.ToUpper(result[0], TextUtil.PtBr) + result[1..];

            if (result != word)
            {
                int animated = corr is null ? 0 : result.Length; // só maiúscula automática não anima
                if (DeferredEnds.Contains(c))
                {
                    _deferred = new Deferred(word, result, c);
                }
                else
                {
                    action = new EngineAction(true, word.Length, result + c, animated);
                    _prev1 = result[^1];
                    if (corr is not null && c == ' ' && _lastCorrectionLearns) _undo = new Undo(word, result, _prev); // atalho não tem alternativa
                }
            }
            // a palavra anterior pode ter ficado sem acento: "isso e muito" → "é" (não depois de "." ou ":", que esperam)
            string? pendingFinal = pending?.Word;
            if (pending is { } pw && _deferred is null
                && corrector.CorrectByNext(pw.Word, pw.Prev, result.ToLower(TextUtil.PtBr)) is { } byNext)
            {
                if (_byNextUndone && string.Equals(pw.Word, _lastByNext, StringComparison.OrdinalIgnoreCase))
                    _lastByNext = null; // apagou a troca e digitou de novo: fica como quis
                else
                {
                    var rest = " " + result + c;
                    action = new EngineAction(true, pw.Word.Length + 1 + word.Length, byNext.Replacement + rest, byNext.Replacement.Length);
                    _undo = c == ' ' ? new Undo(pw.Word, byNext.Replacement, pw.Prev, " " + result, Learns: false) : null;
                    _lastByNext = pw.Word;
                    _byNextUndone = false;
                    pendingFinal = byNext.Replacement;
                    _prev = LastWord(pendingFinal);
                }
            }
            // Conta no histórico o que ficou no texto. A palavra que espera a seguinte só entra com a forma final:
            // contada antes, o "esta" recém-digitado já pesaria contra "está". "." e ":" ainda podem virar e-mail.
            // shortcut: a que espera e é abandonada (Backspace, Tab, número em seguida) não é contada; raro, aceito.
            if (pending is { } done) corrector.Record(done.Prev, pendingFinal!);
            if (corr is null && c == ' ') _pending = new Pending(result, _prev);
            else if (_deferred is null) corrector.Record(_prev, result);
            completed = result;
            _capNext = false;
        }

        // palavra anterior para a próxima correção: muda de frase em . ! ? e Enter; outra pontuação quebra o contexto
        _prev = c switch
        {
            '\n' or '.' or '!' or '?' => BigramModel.SentenceStart,
            ' ' or '\t' => completed is not null ? LastWord(completed) : hadWord ? null : _prev,
            _ => null,
        };

        bool sentenceEnd = _prev1 is '.' or '!' or '?';
        Push(c);
        _lastWasDoubleSpace = false;

        if (c == '\n' || (c == ' ' && sentenceEnd && _tokenKnown)) _capNext = true;

        if (whitespace) EndToken();
        else
        {
            _word.Clear();
            _tokenHadEnd = true;
        }
        return action;
    }

    /// <summary>Última palavra de um texto, em minúsculas; null se o final não for uma palavra só de letras.</summary>
    private static string? LastWord(string text)
    {
        int start = text.Length;
        while (start > 0 && (char.IsLetter(text[start - 1]) || text[start - 1] is '-' or '\'')) start--;
        if (start == text.Length || (start > 0 && !char.IsWhiteSpace(text[start - 1]))) return null;
        return text[start..].ToLower(TextUtil.PtBr);
    }

    private void EndToken()
    {
        _word.Clear();
        _tokenKnown = true;
        _tokenPure = true;
        _tokenHadEnd = false;
    }

    private void Push(char c)
    {
        _prev2 = _prev1;
        _prev1 = c;
    }
}
