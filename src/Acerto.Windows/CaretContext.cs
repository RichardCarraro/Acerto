using System.Windows.Automation;
using System.Windows.Automation.Text;

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
    public static System.Windows.Rect? CaretAnchor(int wordLength)
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
    public static System.Windows.Rect? WordRect(string word, int trailing)
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

            int back = word.Length + trailing;
            if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -back) != -back) return null;
            if (trailing > 0 && range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, -trailing) != -trailing) return null;
            if (!string.Equals(range.GetText(word.Length + 1), word, StringComparison.Ordinal)) return null;

            var rects = range.GetBoundingRectangles();
            if (rects.Length == 0) return null;
            var r = rects[^1]; // palavra quebrada em duas linhas: anima o pedaço final
            // retângulo absurdo (linha inteira, altura enorme): alguns apps informam errado
            if (r.Height < 4 || r.Height > 120 || r.Width < 4 || r.Width > word.Length * r.Height * 1.6 + 8) return null;
            return r;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
