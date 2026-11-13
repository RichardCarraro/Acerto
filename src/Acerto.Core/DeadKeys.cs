using System.Text;

namespace Acerto.Core;

/// <summary>Composição de teclas mortas do ABNT2 (´ ` ^ ~ ¨), do mesmo jeito que o Windows faz.</summary>
public static class DeadKeys
{
    public static string Compose(char dead, char baseChar)
    {
        if (baseChar == ' ') return dead.ToString();
        char? mark = dead switch
        {
            '´' => '́',
            '`' => '̀',
            '^' => '̂',
            '~' => '̃',
            '¨' => '̈',
            _ => null,
        };
        if (mark is null) return $"{dead}{baseChar}";
        // Às vezes o app (Chrome) já aplicou a tecla morta antes de o hook traduzir a letra: ela chega pronta ("ã").
        if (baseChar.ToString().Normalize(NormalizationForm.FormD) is { Length: 2 } d && d[1] == mark) return baseChar.ToString();
        var composed = $"{baseChar}{mark}".Normalize(NormalizationForm.FormC);
        return composed.Length == 1 ? composed : $"{dead}{baseChar}";
    }
}
