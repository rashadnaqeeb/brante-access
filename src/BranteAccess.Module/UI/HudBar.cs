using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using UnityEngine.UI;
using HudController = _Scripts.AMVCC.Views.HudController;
using HudButtonBehavior = _Scripts.AMVCC.Views.HudButtonBehavior;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.UI
{
    /// <summary>
    /// The HUD bar as a Tab-stop: a year/chapter status row, then the window buttons in one
    /// left-to-right row. Button labels are the game's own I2 terms keyed by the button
    /// object's name (the same lookup the game's hover tooltip does); a still-locked button
    /// announces the game's own HUD.WillOpen{chapter} reason. Activation goes through the
    /// game's pointer-click path, whose handlers run the game's own IsButtonsBlocked gate.
    /// Shared by every screen that shows the bar (the event scene now, windows later).
    /// </summary>
    public static class HudBar
    {
        public const string StopKey = "hud";

        // The game's own pressed-window marker (set by PressButton when a window opens, cleared
        // by NormalButton) - the bar's selected state and the open window's spoken name.
        private static readonly System.Reflection.FieldInfo ClickedField =
            typeof(HudButtonBehavior).GetField("_isButtonClicked",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static readonly System.Reflection.FieldInfo BackField =
            typeof(HudController).GetField("_backButton",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        public static void Build(GraphBuilder b)
        {
            var hud = Object.FindObjectOfType<HudController>();
            if (hud == null || !UiWidgets.Visible(hud.gameObject)) return;

            b.BeginStop(StopKey);
            b.PushContext(Loc.T("hud.region"), role: null, positions: false);

            b.AddLabel(ControlId.Structural("hud:info"), () => Info(hud));

            b.StartRow();
            foreach (var btn in Buttons(hud))
            {
                var go = btn.gameObject;
                b.AddItem(ControlId.Referenced(btn, "hud:btn:" + go.name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => Label(go), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => IsPressed(go)
                                    ? Loc.T("state.selected") : null,
                                kind: AnnouncementKinds.Selected),
                            new NodeAnnouncement(() => LockReason(go),
                                kind: AnnouncementKinds.Enabled),
                        },
                        SearchText = () => Label(go),
                        OnActivate = () =>
                        {
                            var reason = LockReason(go);
                            if (reason != null)
                            {
                                Mod.Speech.Speak(reason, interrupt: true);
                                return;
                            }
                            UiWidgets.Click(go);
                        },
                    });
            }
            b.EndRow();
            b.PopContext();
        }

        // Year + chapter, read from the bar's own live texts (the game localizes and updates
        // them; the prologue hides the year pair entirely).
        private static string Info(HudController hud)
        {
            var parts = new List<string>();
            if (hud.Year.gameObject.activeInHierarchy)
                parts.Add(Loc.T("hud.pair",
                    new { label = hud.Year.text, value = hud.YearValue.text }));
            parts.Add(Loc.T("hud.pair",
                new { label = hud.ChapterName.text, value = hud.ChapterValue.text }));
            return string.Join(", ", parts.ToArray());
        }

        // Every active button on the bar (window buttons, chapter/main button, settings, and
        // the back button while the game shows it), left to right. Re-queried per render.
        private static List<Button> Buttons(HudController hud)
        {
            var list = new List<Button>();
            foreach (var btn in hud.GetComponentsInChildren<Button>())
                if (UiWidgets.Visible(btn.gameObject)) list.Add(btn);
            list.Sort((a, c) => a.transform.position.x.CompareTo(c.transform.position.x));
            return list;
        }

        // The game's tooltip label lookup: the button object's name is its I2 term. A name
        // with no term falls back to any text on the button, then the raw name (audible, so
        // a gap is discoverable, never silent).
        private static string Label(GameObject go)
        {
            var term = GameLoc.GetTranslation(go.name);
            if (!string.IsNullOrEmpty(term)) return term;
            return UiWidgets.LabelText(go) ?? go.name;
        }

        // The game's own lock reason for a not-yet-unlocked window button, null when open.
        // HUD.WillOpen{n} is what the game's tooltip shows over a locked button.
        private static string LockReason(GameObject go)
        {
            var hb = go.GetComponent<HudButtonBehavior>();
            if (hb == null || hb.Unlocked) return null;
            var reason = GameLoc.GetTranslation("HUD.WillOpen" + hb.ChapterToUnlock);
            return string.IsNullOrEmpty(reason) ? Loc.T("state.unavailable") : reason;
        }

        private static bool IsPressed(GameObject go)
        {
            var hb = go.GetComponent<HudButtonBehavior>();
            return hb != null && (bool)ClickedField.GetValue(hb);
        }

        /// <summary>The game's localized name for the open window: the term of the HUD button
        /// the game marked pressed. Falls back to the window object's raw name (audible, so a
        /// gap is discoverable).</summary>
        public static string OpenWindowTitle()
        {
            var hud = Object.FindObjectOfType<HudController>();
            if (hud != null)
                foreach (var hb in hud.GetComponentsInChildren<HudButtonBehavior>())
                    if ((bool)ClickedField.GetValue(hb)) return Label(hb.gameObject);
            var w = Game.GameUi.OpenedWindow;
            return w == null ? null : w.name;
        }

        /// <summary>Run the game's own back button (return from the open window to the scene).
        /// False when the bar or its back button is not currently shown.</summary>
        public static bool ClickBack()
        {
            var hud = Object.FindObjectOfType<HudController>();
            if (hud == null) return false;
            var back = (GameObject)BackField.GetValue(hud);
            if (back == null || !back.activeInHierarchy)
            {
                Mod.Warn("[hud] back button null or inactive - Escape not delivered");
                return false;
            }
            SeedTooltipSlot();
            return UiWidgets.Click(back);
        }

        // The game's back handler (WindowMainButton_Click) calls OpenedTooltip.SetActive(false)
        // without a null guard, and the slot is only ever assigned by mouse-hover tooltip
        // handlers - so a keyboard-only session NREs on its first window close, which
        // deactivates the back button and strands the window open. An inactive stub in the
        // slot makes that SetActive a no-op; any real tooltip open replaces the stub.
        private static GameObject _tooltipStub;

        private static void SeedTooltipSlot()
        {
            var mgr = GameUi.Manager;
            if (mgr == null || mgr.OpenedTooltip != null) return;
            if (_tooltipStub == null)
            {
                _tooltipStub = new GameObject("BranteAccess.TooltipStub");
                _tooltipStub.SetActive(false);
                Object.DontDestroyOnLoad(_tooltipStub);
            }
            mgr.OpenedTooltip = _tooltipStub;
        }
    }
}
