using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WrathAccess
{
    /// <summary>
    /// Cleans game-sourced strings for speech. WotR UI text is TMP rich text —
    /// labels come pre-wrapped in tags (color/size/sprite/style, e.g. the main
    /// menu's "saber book" formatting), so we strip tags before speaking.
    /// </summary>
    public static class TextUtil
    {
        // Sub/superscripts are decorative (e.g. the per-level BAB shows iterative-attack indices as
        // "<sub><size=125%> 1 </size></sub>"); their content is noise in speech, so drop tag AND text.
        private static readonly Regex SubSup =
            new Regex("<(sub|sup)>.*?</(sub|sup)>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex RichTextTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = SubSup.Replace(s, "");   // remove sub/superscript blocks entirely (content included)
            // Strip remaining tags to nothing: real spaces in the text are preserved, and tags
            // are usually inline (e.g. a drop-cap "<size=200%>N</size>ew Game"), so a
            // space here would wrongly split words into "N ew Game".
            s = RichTextTag.Replace(s, "");
            s = Whitespace.Replace(s, " ");
            return s.Trim();
        }

        /// <summary>Fold accents away for matching ("Séance" matches "seance"); ligatures œ/æ expand.
        /// Ported from OniAccess (VisionNotIncluded) with permission.</summary>
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
