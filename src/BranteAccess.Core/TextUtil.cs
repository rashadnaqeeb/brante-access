using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BranteAccess.Core
{
    /// <summary>
    /// Cleans game-sourced strings for speech. Brante UI text is TMP rich text (color/size/sprite
    /// tags), stripped at the speech boundary; raw text keeps its markup upstream where it matters.
    /// Ported from wotr-access.
    /// </summary>
    public static class TextUtil
    {
        // Sub/superscript content is decorative noise in speech - drop tag AND text.
        private static readonly Regex SubSup =
            new Regex("<(sub|sup)>.*?</(sub|sup)>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex RichTextTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = SubSup.Replace(s, "");
            // Tags strip to nothing, not to a space: tags are usually inline (a drop-cap
            // "<size=200%>N</size>ew Game") and a space would split words.
            s = RichTextTag.Replace(s, "");
            s = Whitespace.Replace(s, " ");
            return s.Trim();
        }

        /// <summary>Fold accents away for typeahead matching ("Séance" matches "seance");
        /// ligatures expand. Ported from OniAccess via wotr-access, with permission.</summary>
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char c = decomposed[i];
                switch (c)
                {
                    case 'œ': case 'Œ': sb.Append("oe"); break;
                    case 'æ': case 'Æ': sb.Append("ae"); break;
                    default:
                        if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
