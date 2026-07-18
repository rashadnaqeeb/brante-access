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

        private static void AddText(GraphBuilder b, Component text, string value, string idPrefix)
        {
            if (!UiWidgets.Visible(text.gameObject)) return;
            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0) return;
            // A button's label is spoken by the button node, not as a separate row.
            if (text.GetComponentInParent<UnityEngine.UI.Button>() != null) return;
            var t = text;
            b.AddItem(ControlId.Referenced(t, idPrefix + ":text:" + t.GetInstanceID()),
                new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LabelText(t.gameObject),
                            kind: AnnouncementKinds.Label),
                    },
                });
        }
    }
}
