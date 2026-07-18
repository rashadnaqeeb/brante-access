using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using UnityEngine.UI;
using WindowPause = _Scripts.AMVCC.Views.Windows.WindowPause;
using Tooltip = _Scripts.AMVCC.Views.TooltipWithTitleBehavior;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The in-game pause window (UIManager.PauseWindow, toggled by the game's Escape read that
    /// focus mode suppresses - our Back action and the scene screen's Escape run the same
    /// ShowPauseMenu handler). Rows in the game's visual order off WindowPause's serialized
    /// references: Music/Sound sliders (immediate volume, same percent style as Settings), the
    /// Hidden Consequences and Animated Illustrations toggles, then Quit to Main Menu (the
    /// game's exit-confirm popup - its own screen) and Resume. The language row and the
    /// save/load buttons are left to the game: it ships them inactive in this build.
    /// </summary>
    public sealed class PauseScreen : Screen
    {
        public override string Key => "pause";
        public override int Layer => 24;
        public override bool IsActive() => GameUi.PauseOpen;

        private static WindowPause Window() => Object.FindObjectOfType<WindowPause>();

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                // The window's own title (the game titles its pause window "Settings"), read
                // through the localization key where one exists - on the session's FIRST open
                // the rendered text is still the prefab's serialized editor Russian for a
                // frame, and announcements fire before the game's localize pass.
                return w == null ? null
                    : Message.MaybeRaw(UiWidgets.LocalizedLabel(
                        w.transform.Find("Buttons/Panel/Title").gameObject));
            }
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // The game's own Escape path while paused: ShowPauseMenu toggles the window off.
            yield return new ElementAction(ActionIds.Back, null,
                _ => _Scripts.Managers.UIManager.Initiate.ShowPauseMenu());
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;

            var rows = w.transform.Find("Buttons/Panel/Objects");
            for (int i = 0; i < rows.childCount; i++)
            {
                var row = rows.GetChild(i);
                if (!row.gameObject.activeSelf) continue;
                var caption = row.GetComponentInChildren<TMPro.TMP_Text>(true);

                if (w.Music.transform.IsChildOf(row)) AddSlider(b, caption, w.Music, "music");
                else if (w.Sound.transform.IsChildOf(row)) AddSlider(b, caption, w.Sound, "sound");
                else if (w.NarrativeMode.transform.IsChildOf(row)) AddToggle(b, caption, w.NarrativeMode, "consequences");
                else if (w.PictureAnimation.transform.IsChildOf(row)) AddToggle(b, caption, w.PictureAnimation, "animations");
                else if (w.LanguageText.transform.IsChildOf(row)) AddSpinner(b, caption, row, w.LanguageText);
            }

            foreach (var pair in new[] { new { label = w.ExitButtonText, id = "exit" },
                                         new { label = w.BackButtonText, id = "resume" } })
            {
                var button = pair.label.GetComponentInParent<Button>().gameObject;
                b.AddItem(ControlId.Referenced(button.GetComponent<Button>(), "pause:" + pair.id),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => UiWidgets.LabelText(button),
                                kind: AnnouncementKinds.Label),
                        },
                        OnActivate = () => UiWidgets.Click(button),
                    });
            }
        }

        private static void AddSlider(GraphBuilder b, TMPro.TMP_Text caption, Slider slider, string id)
        {
            b.AddItem(ControlId.Referenced(slider, "pause:" + id), new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiWidgets.LocalizedLabel(caption.gameObject),
                        kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Percent(slider), live: true, kind: AnnouncementKinds.Value),
                },
                OnAdjust = (sign, large) =>
                {
                    float step = (slider.maxValue - slider.minValue) * (large ? 0.25f : 0.1f);
                    slider.value = Mathf.Clamp(slider.value + sign * step, slider.minValue, slider.maxValue);
                },
                StateText = () => Percent(slider),
            });
        }

        private static string Percent(Slider slider)
        {
            int pct = Mathf.RoundToInt((slider.value - slider.minValue)
                / (slider.maxValue - slider.minValue) * 100f);
            return Loc.T("settings.percent", new { value = pct });
        }

        private static void AddToggle(GraphBuilder b, TMPro.TMP_Text caption, Toggle toggle, string id)
        {
            // The toggle's Background child carries the hover tooltip with the game's
            // what-this-setting-does description (ConsequenceToggle.Description /
            // IsPictureAnimatedToggle.Desription - game typo).
            var tip = toggle.GetComponentInChildren<Tooltip>(true);
            b.AddItem(ControlId.Referenced(toggle, "pause:" + id), new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiWidgets.LocalizedLabel(caption.gameObject),
                        kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Loc.T(toggle.isOn ? "state.on" : "state.off"),
                        live: true, kind: AnnouncementKinds.Value),
                },
                OnActivate = () => UiWidgets.Click(toggle.gameObject),
                StateText = () => Loc.T(toggle.isOn ? "state.on" : "state.off"),
                OnTooltip = tip == null || string.IsNullOrEmpty(tip.TitleMainText)
                    ? (System.Action)null
                    : () => Mod.Speech.Speak(GameLoc.GetTranslation(tip.TitleMainText)),
            });
        }

        // The language cycler (game ships it inactive in-game; built only if a patch enables it).
        private static void AddSpinner(GraphBuilder b, TMPro.TMP_Text caption, Transform row,
            TMPro.TextMeshProUGUI valueText)
        {
            Button left = null, right = null;
            foreach (var btn in row.GetComponentsInChildren<Button>(true))
            {
                if (btn.gameObject.name == "LeftAr") left = btn;
                if (btn.gameObject.name == "RightAr") right = btn;
            }
            b.AddItem(ControlId.Referenced(valueText, "pause:language"), new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiWidgets.LocalizedLabel(caption.gameObject),
                        kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => valueText.text, live: true, kind: AnnouncementKinds.Value),
                },
                OnAdjust = (sign, large) => UiWidgets.Click((sign > 0 ? right : left).gameObject),
                StateText = () => valueText.text,
            });
        }
    }
}
