using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI.Graph;
using UnityEngine;

namespace BranteAccess.Module.UI
{
    /// <summary>
    /// Generic panel reader for game-composed surfaces with no dedicated model adapter (the
    /// PopupsEnum popup family, generated stat panels): visible texts become rows in hierarchy
    /// order, visible buttons become activatable nodes. The rendered text IS the game's own
    /// localized output for these surfaces, so reading the live components is the model read.
    /// Sweeps TMP and legacy uGUI Text both (the game mixes them).
    /// </summary>
    public static class PanelSweep
    {
        public static void Build(GraphBuilder b, GameObject root, string idPrefix)
        {
            b.PushContext("", role: null, positions: false);
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>())
                AddText(b, tmp, tmp.text, idPrefix);
            foreach (var legacy in root.GetComponentsInChildren<UnityEngine.UI.Text>())
                AddText(b, legacy, legacy.text, idPrefix);
            b.PopContext();

            foreach (var btn in root.GetComponentsInChildren<UnityEngine.UI.Button>())
            {
                if (!UiWidgets.Visible(btn.gameObject)) continue;
                var bt = btn;
                b.AddItem(ControlId.Referenced(bt, idPrefix + ":btn:" + bt.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => UiWidgets.LabelText(bt.gameObject),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(
                                () => UiWidgets.Interactable(bt.gameObject)
                                    ? null : Loc.T("state.unavailable"),
                                kind: AnnouncementKinds.Enabled),
                        },
                        OnActivate = () =>
                        {
                            if (!UiWidgets.Interactable(bt.gameObject))
                            {
                                Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                                return;
                            }
                            UiWidgets.Click(bt.gameObject);
                        },
                    });
            }
        }

        // The panel's readable content as one string (rows in hierarchy order, button labels
        // excluded) - the delivery announcement for panels that appear or swap under the player.
        public static string JoinVisible(GameObject root)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>())
                if (IsRow(tmp, tmp.text)) parts.Add(tmp.text);
            foreach (var legacy in root.GetComponentsInChildren<UnityEngine.UI.Text>())
                if (IsRow(legacy, legacy.text)) parts.Add(legacy.text);
            return string.Join(", ", parts.ToArray());
        }

        // The id of the first row Build would create - for silently re-seating focus before a
        // JoinVisible delivery (ControlId equality is structural-key-only, so this matches the
        // built Referenced node).
        public static ControlId FirstTextId(GameObject root, string idPrefix)
        {
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>())
                if (IsRow(tmp, tmp.text))
                    return ControlId.Structural(idPrefix + ":text:" + tmp.GetInstanceID());
            foreach (var legacy in root.GetComponentsInChildren<UnityEngine.UI.Text>())
                if (IsRow(legacy, legacy.text))
                    return ControlId.Structural(idPrefix + ":text:" + legacy.GetInstanceID());
            return null;
        }

        // A button's label is spoken by the button node, not as a separate row.
        private static bool IsRow(Component text, string value)
            => UiWidgets.Visible(text.gameObject)
            && !string.IsNullOrEmpty(value) && value.Trim().Length != 0
            && text.GetComponentInParent<UnityEngine.UI.Button>() == null;

        private static void AddText(GraphBuilder b, Component text, string value, string idPrefix)
        {
            if (!IsRow(text, value)) return;
            var t = text;
            // Stat rows carry the game's ParameterGetSet - Space reads the same scale detail
            // the game's ParameterValueTooltip shows on hover, composed from the parameter
            // asset at speech time.
            var pgs = t.GetComponentInParent<_Scripts.AMVCC.Views.Windows.ParameterGetSet>();
            b.AddItem(ControlId.Referenced(t, idPrefix + ":text:" + t.GetInstanceID()),
                new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LabelText(t.gameObject),
                            kind: AnnouncementKinds.Label),
                    },
                    OnTooltip = pgs == null ? (System.Action)null
                        : () => Mod.Speech.Speak(Readouts.ParameterScales(pgs.Parameter)),
                });
        }
    }
}
