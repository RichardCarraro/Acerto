using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Accessibility;
using static Acerto.Windows.Native;

namespace Acerto.Windows;

/// <summary>
/// Pergunta ao app em foco (UI Automation) o que há logo antes do cursor de texto.
/// Serve só para saber se o cursor está em início de campo ou depois de um espaço;
/// o texto fica em memória por instantes e nunca é gravado.
/// </summary>
internal static class CaretContext
{
    private const int MaxChars = 64;

    /// <returns>Até 64 caracteres antes do cursor; null se o app não informa, há seleção ou é campo de senha.</returns>
    public static string? TextBeforeCaret()
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el is null || el.Current.IsPassword) return null;
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;

            var text = (TextPattern)pattern;
            var selection = text.GetSelection();
            if (selection.Length != 1) return null;
            var range = selection[0].Clone();
            if (range.GetText(1).Length != 0) return null; // texto selecionado: digitar vai substituir, não arriscar

            range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -MaxChars);
            // Em apps Chromium/Electron (Claude, Chrome, Teams) o intervalo atravessa a caixa de texto e lê o resto
            // da página. Limita ao começo do próprio campo: caixa vazia tem que dar texto vazio.
            var field = text.DocumentRange;
            if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, field, TextPatternRangeEndpoint.Start) < 0)
                range.MoveEndpointByRange(TextPatternRangeEndpoint.Start, field, TextPatternRangeEndpoint.Start);
            return range.GetText(MaxChars);
        }
        catch (Exception)
        {
            return null; // UIA falha de vários jeitos em apps diferentes; sem contexto, vale a regra de esperar o espaço
        }
    }

    /// <summary>
    /// Onde ancorar a barra de sugestões: o retângulo (pixels) da palavra em andamento, ou do cursor quando ainda não
    /// há palavra (<paramref name="wordLength"/> = 0). null se o app não informa a posição.
    /// </summary>
    /// <param name="preferCaret">Campo que informa a posição errada pela UI Automation (barra de endereço do Chrome
    /// devolve o começo do campo): usa primeiro o cursor da acessibilidade antiga.</param>
    public static System.Windows.Rect? CaretAnchor(int wordLength, bool preferCaret = false)
    {
        if (!preferCaret && UiaAnchor(wordLength) is { } uia) return uia;
        if (MsaaCaret() is { } caret)
        {
            Calibrate(caret, wordLength);
            return EstimateWord(caret, wordLength, 0);
        }
        return preferCaret ? UiaAnchor(wordLength) : null;
    }

    private static System.Windows.Rect? UiaAnchor(int wordLength)
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el is null || el.Current.IsPassword) return null;
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;

            var selection = ((TextPattern)pattern).GetSelection();
            if (selection.Length != 1) return null;
            var range = selection[0].Clone();
            if (range.GetText(1).Length != 0) return null;

            int back = Math.Max(wordLength, 1); // sem palavra: usa o caractere antes do cursor (o espaço)
            if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -back) != -back) return null;
            var rects = range.GetBoundingRectangles();
            if (rects.Length == 0) return null;
            var r = rects[^1];
            if (r.Height < 4 || r.Height > 120) return null;
            return wordLength > 0 ? r : new System.Windows.Rect(r.Right, r.Top, 1, r.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Retângulo na tela (pixels) da palavra corrigida, que termina <paramref name="trailing"/> caracteres antes do
    /// cursor. Só devolve se o texto nessa posição for exatamente <paramref name="word"/>: se o app ainda não recebeu
    /// a correção ou o cursor já está em outro lugar, é melhor não animar do que animar no lugar errado.
    /// </summary>
    /// <param name="lastTry">Última tentativa: se o app lê o texto mas ainda não mostra a palavra, estima pelo cursor.
    /// Antes disso espera, porque no meio da troca o cursor está noutro lugar (primeira palavra do campo: a correção
    /// apaga tudo e o cursor volta ao começo, e o realce caía no botão ao lado).</param>
    public static System.Windows.Rect? WordRect(string word, int trailing, bool preferCaret = false, bool lastTry = true)
    {
        if (preferCaret) return MsaaCaret() is { } p ? EstimateWord(p, word.Length, trailing) : UiaWordRect(word, trailing, out _);
        if (UiaWordRect(word, trailing, out bool readsText) is { } r) return r;
        if (readsText && !lastTry) return null;
        return MsaaCaret() is { } c ? EstimateWord(c, word.Length, trailing) : null;
    }

    /// <param name="readsText">O app informa o texto e o cursor pela UI Automation (o null é "ainda não", não "não sei").</param>
    private static System.Windows.Rect? UiaWordRect(string word, int trailing, out bool readsText)
    {
        readsText = false;
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el is null || el.Current.IsPassword) return null;
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;

            var selection = ((TextPattern)pattern).GetSelection();
            if (selection.Length != 1) return null;
            var range = selection[0].Clone();
            if (range.GetText(1).Length != 0) return null;
            readsText = true;

            int back = word.Length + trailing;
            if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -back) != -back) return null;
            if (trailing > 0 && range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, -trailing) != -trailing) return null;
            if (!string.Equals(range.GetText(word.Length + 1), word, StringComparison.Ordinal)) return null;

            var rects = range.GetBoundingRectangles();
            if (rects.Length == 0) return null;
            var r = rects[^1]; // palavra quebrada em duas linhas: anima o pedaço final
            // retângulo absurdo (linha inteira, altura enorme): alguns apps informam errado
            if (r.Height < 4 || r.Height > 120 || r.Width < 4 || r.Width > word.Length * r.Height * 1.6 + 8) return null;
            if (Diag.On) Diag.Log($"REALCE uia letras={word.Length} ret={(int)r.X},{(int)r.Y},{(int)r.Width},{(int)r.Height} partes={rects.Length}");
            return r;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Só o cursor, pela acessibilidade antiga (MSAA). Apps Electron como o Evernote não informam o texto pela UI
    /// Automation, mas informam o cursor por aqui.
    /// </summary>
    private static System.Windows.Rect? MsaaCaret()
    {
        try
        {
            var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (!GetGUIThreadInfo(thread, ref info) || info.hwndFocus == 0) return null;
            var iid = IAccessibleId;
            if (AccessibleObjectFromWindow(info.hwndFocus, OBJID_CARET, ref iid, out var acc) != 0 || acc is null) return null;
            acc.accLocation(out int x, out int y, out int w, out int h, 0);
            if (h < 4 || h > 120 || (x == 0 && y == 0)) return null;
            return new System.Windows.Rect(x, y, Math.Max(w, 1), h);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Largura média de letra medida no campo atual: enquanto a pessoa digita uma palavra, o cursor anda uma letra por
    // tecla. 0 = ainda não mediu (usa meia altura da linha). As leituras vêm de tarefas em segundo plano: trava simples.
    private static readonly Lock Gate = new();
    private static double _letter, _letterHeight, _lastX, _lastY;
    private static int _lastLength;

    private static void Calibrate(System.Windows.Rect caret, int wordLength)
    {
        lock (Gate)
        {
            if (Math.Abs(caret.Height - _letterHeight) > 2) // outro campo, outra fonte: mede de novo
            {
                _letter = 0;
                _letterHeight = caret.Height;
            }
            if (_lastLength > 0 && wordLength > _lastLength && Math.Abs(caret.Top - _lastY) < 2)
            {
                double step = (caret.Left - _lastX) / (wordLength - _lastLength);
                if (step > caret.Height * 0.15 && step < caret.Height * 0.9) // fora disso: pulou de linha, leitura atrasada
                    _letter = _letter == 0 ? step : _letter * 0.7 + step * 0.3;
            }
            (_lastX, _lastY, _lastLength) = (caret.Left, caret.Top, wordLength);
        }
    }

    /// <summary>
    /// Sem o texto, só o cursor: estima onde fica a palavra que termina <paramref name="trailing"/> caracteres antes dele,
    /// com a largura de letra medida no campo (ou meia altura da linha, antes de medir).
    /// </summary>
    private static System.Windows.Rect EstimateWord(System.Windows.Rect caret, int length, int trailing)
    {
        double letter;
        lock (Gate) letter = _letter > 0 && Math.Abs(caret.Height - _letterHeight) <= 2 ? _letter : caret.Height * 0.5;
        double right = caret.Left - trailing * letter;
        double width = Math.Max(length, 1) * letter;
        if (Diag.On) Diag.Log($"REALCE estimado letras={length} depois={trailing} letra={letter:0.0} cursor={(int)caret.X},{(int)caret.Y},{(int)caret.Height}");
        return length == 0 ? new System.Windows.Rect(caret.Left, caret.Top, 1, caret.Height)
            : new System.Windows.Rect(right - width, caret.Top, width, caret.Height);
    }

    private const int OBJID_CARET = -8;
    private static readonly Guid IAccessibleId = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint hwnd, int id, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? acc);
}
