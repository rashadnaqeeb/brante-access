using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BranteAccess.Module.Game
{
    /// <summary>
    /// uGUI primitives every screen adapter builds on: read a control's label, test real
    /// visibility/interactability, and activate through the game's own event path. All methods
    /// re-read live components at call time - nothing is cached (CLAUDE.md). Ported from the
    /// wotr-access adapter layer, adapted to uGUI + TMP.
    /// </summary>
    public static class UiWidgets
    {
        /// <summary>The first non-empty text under this object - TMP first, then legacy
        /// UnityEngine.UI.Text (CLAUDE.md gotcha: Brante mixes both; sweep both per screen).
        /// RAW text, markup included - stripping happens at the speech boundary. Null when the
        /// control carries no text (an image-only button).</summary>
        public static string LabelText(GameObject go)
        {
            if (go == null) return null;
            foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(false))
            {
                var t = tmp.text;
                if (!string.IsNullOrEmpty(t) && t.Trim().Length > 0) return t;
            }
            foreach (var legacy in go.GetComponentsInChildren<Text>(false))
            {
                var t = legacy.text;
                if (!string.IsNullOrEmpty(t) && t.Trim().Length > 0) return t;
            }
            return null;
        }

        /// <summary>Is this object actually shown - active in hierarchy AND not hidden by any
        /// ancestor CanvasGroup faded to zero (the game hides windows by alpha during
        /// transitions; hidden is not closed - never activate through it).</summary>
        public static bool Visible(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            for (var t = go.transform; t != null; t = t.parent)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group == null) continue;
                if (group.alpha <= 0.01f) return false;
                if (group.ignoreParentGroups) break; // this group cuts the inheritance chain
            }
            return true;
        }

        /// <summary>Interactable through the whole CanvasGroup chain - Selectable.IsInteractable
        /// folds in every parent group's interactable flag; the raycast chain adds the game's
        /// animation-time input block (see <see cref="RaycastsReachable"/>).</summary>
        public static bool Interactable(GameObject go)
        {
            if (go == null) return false;
            var selectable = go.GetComponent<Selectable>();
            return selectable != null && selectable.IsInteractable() && RaycastsReachable(go);
        }

        /// <summary>Activate through the game's own pointer-click path (ExecuteEvents), so
        /// Button.OnPointerClick runs its interactable check, transitions, sounds, and onClick
        /// exactly as a mouse click would. False when the object handles no click (caller
        /// speaks its "nothing to activate" feedback) or the game is blocking input to it
        /// (a mouse click could not reach it either).</summary>
        public static bool Click(GameObject go)
        {
            if (go == null || !RaycastsReachable(go)) return false;
            var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            if (handler == null) return false;
            ExecuteEvents.Execute(handler, new PointerEventData(EventSystem.current),
                ExecuteEvents.pointerClickHandler);
            return true;
        }

        /// <summary>Could a mouse click reach this control right now? The game blocks input
        /// during page-turn/show/hide animations by switching ancestor CanvasGroups'
        /// blocksRaycasts off (SceneAnimationController) - keyboard activation goes around the
        /// raycaster, so it must honor the same block or Enter double-fires mid-animation.</summary>
        private static bool RaycastsReachable(GameObject go)
        {
            for (var t = go.transform; t != null; t = t.parent)
            {
                var group = t.GetComponent<CanvasGroup>();
                if (group == null) continue;
                if (!group.blocksRaycasts) return false;
                if (group.ignoreParentGroups) break;
            }
            return true;
        }
    }
}
