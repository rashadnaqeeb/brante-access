using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using LoadWindow = _Scripts.AMVCC.Views.Windows.SaveLoad.LoadWindow;
using SaveSlot = _Scripts.AMVCC.Views.Windows.SaveLoad.SaveSlot;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The load window ("Continue Game" - the main menu's Continue opens it as an ADDITIVE
    /// scene). One stop: each save slot is a single row folding the game's own slot text (hero,
    /// chapter, year, scene) with its date; Enter loads that save through the slot's own
    /// pointer-click path, Backspace opens the game's delete confirmation (its own popup screen).
    /// Escape runs the game's back button (the window's Escape read is focus-suppressed), which
    /// also re-enables the menu's Continue via the game's UpdateButtonsState broadcast.
    /// </summary>
    public sealed class LoadWindowScreen : Screen
    {
        public override string Key => "loadwindow";
        public override int Layer => 10;
        public override bool IsActive() => GameUi.IsSceneLoaded("LoadWindow");

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                return w == null ? null
                    : Message.MaybeRaw(w.transform.Find("TextMeshPro Text").GetComponent<TMPro.TMP_Text>().text);
            }
        }

        private static LoadWindow Window() => Object.FindObjectOfType<LoadWindow>();

        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ =>
            {
                var w = Window();
                if (w != null) UiWidgets.Click(w.transform.Find("MainPanel/BackButton").gameObject);
            });
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;

            // Slots in the game's own list order (container children; each clone holds one
            // SaveSlot on its Body). Rebuilt live - the game's UpdateLoadWindow refresh after a
            // delete just shows up on the next render.
            for (int i = 0; i < w.SavesContainer.childCount; i++)
            {
                var slot = w.SavesContainer.GetChild(i).GetComponentInChildren<SaveSlot>();
                if (slot == null) continue;
                var s = slot;
                b.AddItem(ControlId.Referenced(s, "loadwindow:slot:" + s.SlotId), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(
                            () => (s.SaveName.text + ", " + s.Date.text).Replace("\r", " ").Replace("\n", " "),
                            kind: AnnouncementKinds.Label),
                    },
                    OnActivate = () => UiWidgets.Click(s.gameObject),
                    OnSecondary = () => UiWidgets.Click(s.transform.Find("Delete").gameObject),
                });
            }

            var back = w.transform.Find("MainPanel/BackButton").gameObject;
            b.AddItem(ControlId.Referenced(back.GetComponent<UnityEngine.UI.Button>(), "loadwindow:back"),
                new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LabelText(back), kind: AnnouncementKinds.Label),
                    },
                    OnActivate = () => UiWidgets.Click(back),
                });
        }
    }

    /// <summary>
    /// The save-slot delete confirmation (UIManager.ShowSaveDeleteConfirmPopup - a real popup
    /// slot occupant). Title is the screen name, description is the start node, then the game's
    /// Delete/Cancel buttons. Escape cancels through the game's cancel handler (the popup's own
    /// Escape read is focus-suppressed).
    /// </summary>
    public sealed class DeleteConfirmScreen : Screen
    {
        public override string Key => "deleteconfirm";
        public override int Layer => 22;
        public override bool IsActive() => Popup() != null;

        public override Message ScreenName
        {
            get
            {
                var p = Popup();
                return p == null ? null
                    : Message.MaybeRaw(p.transform.Find("Body/Container/Title").GetComponent<TMPro.TMP_Text>().text);
            }
        }

        private static _Scripts.AMVCC.Views.Windows.DeletePopupConfirmation.DeletePopupConfirmation Popup()
            => Object.FindObjectOfType<_Scripts.AMVCC.Views.Windows.DeletePopupConfirmation.DeletePopupConfirmation>();

        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ =>
            {
                var p = Popup();
                if (p != null) UiWidgets.Click(p.transform.Find("Body/Container/Buttons/Cancel").gameObject);
            });
        }

        public override void Build(GraphBuilder b)
        {
            var p = Popup();
            if (p == null) return;
            var container = p.transform.Find("Body/Container");

            var description = container.Find("Description").GetComponent<TMPro.TMP_Text>();
            var descriptionId = ControlId.Referenced(description, "deleteconfirm:description");
            b.AddItem(descriptionId, new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => description.text, kind: AnnouncementKinds.Label),
                },
            });
            b.SetStart(descriptionId);

            foreach (var name in new[] { "Confirm", "Cancel" })
            {
                var button = container.Find("Buttons/" + name).gameObject;
                b.AddItem(ControlId.Referenced(button.GetComponent<UnityEngine.UI.Button>(),
                    "deleteconfirm:" + name), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LabelText(button), kind: AnnouncementKinds.Label),
                    },
                    OnActivate = () => UiWidgets.Click(button),
                });
            }
        }
    }
}
