#if DEBUG
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace BranteAccess.Dev
{
    /// <summary>
    /// Raw structural dump of the active uGUI hierarchy for /gui: every active GameObject under
    /// each root canvas, indented by depth, with component types, any TMP/legacy text, and
    /// CanvasGroup alpha. Deliberately NOT the mod's cleaned focus view - it surfaces structure the
    /// mod hides, so an unfamiliar screen can be reverse-engineered in one shot and then driven
    /// with /eval. Reads live state; caches nothing. Ported from Non-Visual Calculus.
    ///
    /// TMP text is read via reflection (no compile-time TMPro reference in the host): the module
    /// owns the TMP dependency; the host stays lean.
    /// </summary>
    internal static class GuiInspector
    {
        public static string Describe()
        {
            var sb = new StringBuilder();
            int roots = 0;
            foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas || !canvas.gameObject.activeInHierarchy) continue;
                roots++;
                sb.Append("=== canvas: ").Append(canvas.name)
                  .Append("  sort=").Append(canvas.sortingOrder).Append(" ===\n");
                Walk(canvas.transform, 0, sb);
            }
            if (roots == 0) sb.Append("(no active root canvas)\n");
            return sb.ToString();
        }

        private static void Walk(Transform t, int depth, StringBuilder sb)
        {
            GameObject go = t.gameObject;
            if (!go.activeInHierarchy) return;

            sb.Append(' ', depth * 2).Append(go.name);
            AppendComponents(go, sb);

            var cg = go.GetComponent<CanvasGroup>();
            if (cg != null) sb.Append("  alpha=").Append(cg.alpha);

            string text = NodeText(go);
            if (text != null) sb.Append("  \"").Append(text).Append('"');

            sb.Append('\n');

            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), depth + 1, sb);
        }

        // Component type names on this node, skipping the ones present on nearly every UI node.
        private static void AppendComponents(GameObject go, StringBuilder sb)
        {
            bool any = false;
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) continue; // a missing-script slot
                string name = c.GetType().Name;
                if (name == "Transform" || name == "RectTransform" || name == "CanvasRenderer") continue;
                sb.Append(any ? ", " : "  [").Append(name);
                any = true;
            }
            if (any) sb.Append(']');
        }

        /// <summary>Text on this exact node only (children get their own lines). Reads both TMP
        /// (reflectively) and legacy UnityEngine.UI.Text - Brante screens must be checked for both.</summary>
        internal static string NodeText(GameObject go)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var type = c.GetType();
                // TMP_Text is the base of TextMeshProUGUI; walk up so both match.
                for (var t = type; t != null; t = t.BaseType)
                {
                    if (t.Name != "TMP_Text") continue;
                    var text = (string)t.GetProperty("text")?.GetValue(c, null);
                    if (!string.IsNullOrEmpty(text)) return text;
                    break;
                }
            }
            var legacy = go.GetComponent<Text>();
            if (legacy != null && !string.IsNullOrEmpty(legacy.text)) return legacy.text;
            return null;
        }
    }
}
#endif
