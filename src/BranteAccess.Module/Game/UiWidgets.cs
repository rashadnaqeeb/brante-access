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

        /// <summary>A label that is safe against the game's one-beat localization race: a
        /// TextMeshProLocalization label resolves through its own I2 keys (the same
        /// composition its Start() applies - a freshly instantiated prefab still shows the
        /// serialized text of the authoring language until then). Falls back to
        /// <see cref="LabelText"/> for plain labels.</summary>
        public static string LocalizedLabel(GameObject go)
        {
            if (go == null) return null;
            var tl = go.GetComponentInChildren<_Scripts.Localization.TextMeshProLocalization>(false);
            if (tl != null)
            {
                var text = tl.ItsKeysCombination
                    ? I2.Loc.LocalizationManager.GetTranslation(tl.Keys[0]) + " "
                        + I2.Loc.LocalizationManager.GetTranslation(tl.Keys[1])
                    : I2.Loc.LocalizationManager.GetTranslation(tl.Key);
                if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0) return text;
            }
            return LabelText(go);
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

        /// <summary>Activate with the FULL pointer sequence a physical mouse press sends -
        /// down, up, click - mirroring StandaloneInputModule. The game hides state writes on
        /// IPointerDownHandler (KeyChapterParametersChanger seals the chapter 3 occupation
        /// choice in OnPointerDown, not onClick); sending only the click silently skips them
        /// and corrupts the playthrough. False when the object handles no click (caller
        /// speaks its "nothing to activate" feedback) or the game is blocking input to it
        /// (a mouse click could not reach it either). Afterwards any non-text-input EventSystem
        /// selection is cleared: Selectable.OnPointerDown selects the pressed button, and the
        /// game's StandaloneInputModule maps Submit to enter AND space (Unity default axes,
        /// verified in globalgamemanagers), so a lingering selection turns the tooltip key into
        /// a phantom click on the last activated control and lets arrows/Enter drive uGUI
        /// selection behind the mod's focus. Text inputs keep their selection - an input field
        /// receives typing through it (name entry).</summary>
        public static bool Click(GameObject go)
        {
            if (go == null || !RaycastsReachable(go)) return false;
            var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            if (handler == null) return false;
            var eventSystem = EventSystem.current;
            var data = new PointerEventData(eventSystem);
            var pressed = ExecuteEvents.ExecuteHierarchy(go, data, ExecuteEvents.pointerDownHandler);
            if (pressed == null) pressed = handler;
            data.pointerPress = pressed;
            ExecuteEvents.Execute(pressed, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerClickHandler);
            if (eventSystem != null)
            {
                var selected = eventSystem.currentSelectedGameObject;
                if (selected != null && selected.GetComponent<TMP_InputField>() == null
                    && selected.GetComponent<InputField>() == null)
                    eventSystem.SetSelectedGameObject(null);
            }
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
