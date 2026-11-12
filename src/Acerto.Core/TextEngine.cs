using System.Text;

namespace Acerto.Core;

/// <summary>
/// O que o host faz com uma tecla. Se <see cref="Swallow"/>, a tecla não chega ao app:
/// o host envia <see cref="Backspaces"/> × Backspace e depois <see cref="Insert"/> ('\n' = Enter, '\t' = Tab).
/// </summary>
public readonly record struct EngineAction(bool Swallow, int Backspaces, string Insert)
{
    public static readonly EngineAction None = new(false, 0, "");
}

/// <summary>
/// Acompanha o que a pessoa digita, caractere a caractere, e decide as correções.
/// Só corrige o que viu nascer: depois de um Reset, espera o próximo espaço/Enter/Tab.
/// </summary>
/// <param name="learn">Chamado com a palavra que o usuário ensinou desfazendo uma correção.
/// O host decide quando gravar (o hook do Windows não pode fazer I/O).</param>
public sealed class TextEngine(Corrector corrector, Action<string> learn)
{
    private const string Prefixes = "(\"“['";
    private const string ClosingEnds = ".,;:!?)]}\"”»";
    private const string DeferredEnds = ".:"; // podem ser meio de e-mail, domínio ou arquivo

    private readonly record struct Undo(int Backspaces, string Insert, string? Learn);
    private readonly record struct Deferred(string Word, string Result, char End, string? Learn);

    private readonly StringBuilder _word = new();
    private bool _tokenKnown;      // vimos o começo deste token?
    private bool _tokenPure = true; // só prefixos + letras/hífen até agora
    private bool _tokenHadEnd;     // já passou pontuação neste token ("www.site")
    private bool _capNext;
    private bool _lastWasDoubleSpace;
    private char _prev1, _prev2;   // dois últimos caracteres do texto ('\0' = desconhecido)
    private Undo? _undo;
    private Deferred? _deferred;   // correção esperando ver o que vem depois de "." ou ":"
    private string? _pendingLearn; // desfeita, mas só aprende se a próxima tecla não for outro Backspace

    public void Reset()
    {
        CommitLearn();
        _word.Clear();
        _tokenKnown = false;
        _tokenPure = true;
        _tokenHadEnd = false;
        _capNext = false;
        _lastWasDoubleSpace = false;
        _prev1 = _prev2 = '\0';
        _undo = null;
        _deferred = null;
    }

    public EngineAction OnChar(char c)
    {
        _undo = null;
        CommitLearn();
        bool whitespace = c is ' ' or '\n' or '\t';

        if (_deferred is { } d)
        {
            _deferred = null;
            if (c is ' ' or '\n')
            {
                var insert = d.Result + d.End + c;
                OnWordEnd(c, whitespace: true); // atualiza estado; a palavra já foi consumida
                if (c != '\n') _undo = new Undo(insert.Length, d.Word + d.End + c, d.Learn);
                return new EngineAction(true, d.Word.Length + 1, insert);
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

    public EngineAction OnBackspace()
    {
        _pendingLearn = null; // Backspace seguido de Backspace: está apagando, não ensinando
        _deferred = null;
        if (_undo is { } u)
        {
            _undo = null;
            _pendingLearn = u.Learn;
            _prev1 = u.Insert[^1];
            _prev2 = u.Insert.Length > 1 ? u.Insert[^2] : '\0';
            _lastWasDoubleSpace = true; // não refazer espaço duplo em cima do que foi desfeito
            return new EngineAction(true, u.Backspaces, u.Insert);
        }
        if (_word.Length == 0)
        {
            Reset(); // apagou para dentro de algo que não estamos vendo
            return EngineAction.None;
        }
        _word.Length--;
        _prev1 = _word.Length > 0 ? _word[^1] : '\0';
        _prev2 = '\0';
        return EngineAction.None;
    }

    private EngineAction OnWordEnd(char c, bool whitespace)
    {
        var action = EngineAction.None;

        if (c == ' ' && _prev1 == ' ' && char.IsLetterOrDigit(_prev2) && !_lastWasDoubleSpace)
        {
            action = new EngineAction(true, 1, ". ");
            _undo = new Undo(2, "  ", null);
            _capNext = true;
            _prev2 = '.';
            _prev1 = ' ';
            _lastWasDoubleSpace = true;
            EndToken();
            return action;
        }

        // Tab não corrige: em muitos apps ele aceita uma sugestão do próprio app
        if (_tokenKnown && _tokenPure && _word.Length > 0 && c != '\t')
        {
            var word = _word.ToString();
            var corr = corrector.Correct(word);
            var result = corr?.Replacement ?? word;
            if (_capNext && char.IsLower(word[0]) && result.Length > 0)
                result = char.ToUpper(result[0], TextUtil.PtBr) + result[1..];

            if (result != word)
            {
                bool learns = corr?.Kind is CorrectionKind.Fixed or CorrectionKind.Accent or CorrectionKind.Typo;
                if (DeferredEnds.Contains(c))
                {
                    _deferred = new Deferred(word, result, c, learns ? word : null);
                }
                else
                {
                    var insert = result + c;
                    action = new EngineAction(true, word.Length, insert);
                    if (c != '\n') // Enter pode ter enviado a mensagem: desfazer não é seguro
                        _undo = new Undo(insert.Length, word + c, learns ? word : null);
                    _prev1 = result[^1];
                }
            }
            _capNext = false;
        }

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

    private void CommitLearn()
    {
        if (_pendingLearn is { } w) learn(w);
        _pendingLearn = null;
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
