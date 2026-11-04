using System.Globalization;
using System.Text;

namespace Wubi86.Core;

public static class PinyinNormalizer
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var decomposed = text.Trim().ToLowerInvariant().Replace("ü", "v").Replace("ǖ", "v").Replace("ǘ", "v").Replace("ǚ", "v").Replace("ǜ", "v").Replace("u:", "v").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (char ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC)
            .Replace("ü", "v", StringComparison.Ordinal);
    }
}

