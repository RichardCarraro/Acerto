using System.Globalization;
using System.Text;

namespace Acerto.Core;

public static class TextUtil
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Remove acentos e cedilha mantendo a caixa: "Ção" → "Cao".</summary>
    public static string StripAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Copia o padrão de caixa do que foi digitado para a substituição.</summary>
    public static string ApplyCase(string typed, string replacement)
    {
        if (replacement.Length == 0) return replacement;
        if (IsAllCaps(typed)) return replacement.ToUpper(PtBr);
        if (typed.Length > 0 && char.IsUpper(typed[0]))
            return char.ToUpper(replacement[0], PtBr) + replacement[1..];
        return replacement;
    }

    /// <summary>Pelo menos 2 letras e todas maiúsculas.</summary>
    public static bool IsAllCaps(string s)
    {
        int letters = 0;
        foreach (var ch in s)
        {
            if (!char.IsLetter(ch)) continue;
            if (!char.IsUpper(ch)) return false;
            letters++;
        }
        return letters >= 2;
    }

    /// <summary>Tem maiúscula depois da primeira letra e não é tudo maiúsculo ("iPhone").</summary>
    public static bool IsMixedCase(string s)
    {
        if (IsAllCaps(s)) return false;
        for (int i = 1; i < s.Length; i++)
            if (char.IsUpper(s[i])) return true;
        return false;
    }
}
