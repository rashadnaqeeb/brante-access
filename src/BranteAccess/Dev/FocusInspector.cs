#if DEBUG
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BranteAccess.Dev
{
    /// <summary>
    /// Reads the current uGUI EventSystem selection on demand for /focus, independent of speech and
    /// of the mod's own navigator (/nav). Host-side and self-contained, so focus stays inspectable
    /// even when the module failed to load - which is exactly when it's needed. Reads live state;
    /// caches nothing. Ported from Non-Visual Calculus (Brante has no NavigationManager; the
    /// EventSystem is the single game-side source).
    /// </summary>
    internal static class FocusInspector
    {
        public static string Describe()
        {
            var sb = new StringBuilder();

            EventSystem es = EventSystem.current;
            GameObject go = es != null ? es.currentSelectedGameObject : null;
            sb.Append("eventsystem: ")
              .Append(go != null ? go.name : (es == null ? "(no EventSystem)" : "none")).Append('\n');

            if (go == null)
            {
                sb.Append("text: (nothing selected)\n");
                return sb.ToString();
            }

            Selectable sel = go.GetComponent<Selectable>();
            sb.Append("path: ").Append(HierarchyPath(go)).Append('\n');
            sb.Append("interactable: ").Append(sel != null ? sel.interactable.ToString() : "n/a")
              .Append("  button: ").Append(go.GetComponent<Button>() != null).Append('\n');
            sb.Append("text: ").Append(LabelText(go)).Append('\n');
            return sb.ToString();
        }

        // All text under the object, '|'-joined so the per-label split stays visible for debugging.
        private static string LabelText(GameObject go)
        {
            var parts = new List<string>();
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                string text = GuiInspector.NodeText(t.gameObject);
                if (!string.IsNullOrEmpty(text)) parts.Add(text);
            }
            return parts.Count > 0 ? string.Join(" | ", parts) : "(no text)";
        }

        private static string HierarchyPath(GameObject go)
        {
            var names = new List<string>();
            Transform t = go.transform;
            while (t != null)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }
    }
}
#endif
