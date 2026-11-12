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

            var selection = ((TextPattern)pattern).GetSelection();
            if (selection.Length != 1) return null;
            var range = selection[0].Clone();
            if (range.GetText(1).Length != 0) return null; // texto selecionado: digitar vai substituir, não arriscar

            range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -MaxChars);
            return range.GetText(MaxChars);
        }
        catch (Exception)
        {
            return null; // UIA falha de vários jeitos em apps diferentes; sem contexto, vale a regra de esperar o espaço
        }
    }
}
