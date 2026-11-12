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
public sealed class TextEngine(Corrector corrector, PersonalDictionary personal)
{
    private const string Prefixes = "(\"“['";
    private const string ClosingEnds = ".,;:!?)]}\"”»";

    private readonly record struct Undo(int Backspaces, string Insert, string? Learn);

    private readonly StringBuilder _word = new();
    private bool _tokenKnown;      // vimos o começo deste token?
    private bool _tokenPure = true; // só prefixos + letras/hífen até agora
    private bool _tokenHadEnd;     // já passou pontuação neste token ("www.site")
    private bool _capNext;
    private bool _lastWasDoubleSpace;
    private char _prev1, _prev2;   // dois últimos caracteres do texto ('\0' = desconhecido)
    private Undo? _undo;

    public void Reset()
    {
        _word.Clear();
        _tokenKnown = false;
        _tokenPure = true;
        _tokenHadEnd = false;
        _capNext = false;
        _lastWasDoubleSpace = false;
        _prev1 = _prev2 = '\0';
        _undo = null;
    }

    public EngineAction OnChar(char c)
    {
        _undo = null;
        bool whitespace = c is ' ' or '\n' or '\t';

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
        if (_undo is { } u)
        {
            _undo = null;
            if (u.Learn is not null) personal.Add(u.Learn);
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

        if (_tokenKnown && _tokenPure && _word.Length > 0)
        {
            var word = _word.ToString();
            var corr = corrector.Correct(word, _capNext);
            var result = corr?.Replacement ?? word;
            if (_capNext && char.IsLower(word[0]) && result.Length > 0)
                result = char.ToUpper(result[0], TextUtil.PtBr) + result[1..];

            if (result != word)
            {
                var insert = result + c;
                action = new EngineAction(true, word.Length, insert);
                if (c is not ('\n' or '\t')) // Enter/Tab podem ter enviado a mensagem: desfazer não é seguro
                {
                    bool learns = corr?.Kind is CorrectionKind.Fixed or CorrectionKind.Accent or CorrectionKind.Typo;
                    _undo = new Undo(insert.Length, word + c, learns ? word : null);
                }
                _prev1 = result[^1];
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
