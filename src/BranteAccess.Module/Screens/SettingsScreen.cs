using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using UnityEngine.UI;
using SettingsWindow = _Scripts.AMVCC.Views.Windows.Settings.SettingsWindow;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Settings scene (loaded ADDITIVELY from the main menu and the pause menu, never a
    /// UIManager window slot). One form stop, rows in the game's visual order, each row keyed off
    /// the SettingsWindow's own serialized control references and captioned by the row's game
    /// -localized label: Language spinner, two toggles, Music/Sound sliders, Screen format
    /// spinner, Resolution dropdown (interactable only in windowed mode - the game's rule), VSync
    /// toggle, Back button. Left/Right adjust sliders/spinners/dropdown through the game's own
    /// handlers (volume applies immediately - sliding Sound and Music to zero IS the keyboard
    /// audio mute); Escape runs the game's back handler (its own Escape read is focus-suppressed).
    /// </summary>
    public sealed class SettingsScreen : Screen
    {
        public override string Key => "settings";
        public override int Layer => 10;
        public override bool IsActive() => GameUi.IsSceneLoaded("Settings");

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                // The game's own localized window title ("Settings"); null while the additive
                // scene is still activating - the focus announcement then waits for content.
                // Resolved through the label's own I2 term: on the session's first open the
                // rendered text is still the prefab's serialized Russian for a frame.
                return w == null ? null
                    : Message.MaybeRaw(UiWidgets.LocalizedLabel(w.transform.Find("Title").gameObject));
            }
        }

        private static SettingsWindow Window() => Object.FindObjectOfType<SettingsWindow>();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ =>
            {
                var w = Window();
                if (w != null) UiWidgets.Click(w.transform.Find("BackButton").gameObject);
            });
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;
            var container = w.transform.Find("MainPanel/Container");

            // Rows in the game's own visual order (container child order). Each Add* helper
            // captions the row with its game-localized label text.
            for (int i = 0; i < container.childCount; i++)
            {
                var row = container.GetChild(i);
                if (!row.gameObject.activeSelf) continue;
                var caption = row.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (caption == null) continue; // spacer object

                if (w.Sound.transform.IsChildOf(row)) AddSlider(b, caption, w.Sound, "sound");
                else if (w.Music.transform.IsChildOf(row)) AddSlider(b, caption, w.Music, "music");
                else if (w.PictureAnimation.transform.IsChildOf(row)) AddToggle(b, caption, w.PictureAnimation, "animations");
                else if (w.VSync.transform.IsChildOf(row)) AddToggle(b, caption, w.VSync, "vsync");
                else if (w.Subtitles.transform.IsChildOf(row)) AddToggle(b, caption, w.Subtitles, "subtitles");
                else if (w.Resolutions.transform.IsChildOf(row)) AddDropdown(b, caption, w.Resolutions);
                else if (w.LanguageText.transform.IsChildOf(row)) AddSpinner(b, caption, row, w.LanguageText, "language");
                else if (w.WindowModeText.transform.IsChildOf(row)) AddSpinner(b, caption, row, w.WindowModeText, "windowmode");
            }

            var back = w.transform.Find("BackButton").gameObject;
            b.AddItem(ControlId.Referenced(back.GetComponent<Button>(), "settings:back"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiWidgets.LabelText(back), kind: AnnouncementKinds.Label),
                },
                OnActivate = () => UiWidgets.Click(back),
            });
        }

        // A volume slider (0..1): value spoken as percent, Left/Right steps through the slider's
        // own onValueChanged so the game applies volume and plays its feedback SFX live.
        private static void AddSlider(GraphBuilder b, TMPro.TMP_Text caption, Slider slider, string id)
        {
            b.AddItem(ControlId.Referenced(slider, "settings:" + id), new NodeVtable
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
            int pct = Mathf.RoundToInt((slider.value - slider.minValue) / (slider.maxValue - slider.minValue) * 100f);
            return Loc.T("settings.percent", new { value = pct });
        }

        private static void AddToggle(GraphBuilder b, TMPro.TMP_Text caption, Toggle toggle, string id)
        {
            b.AddItem(ControlId.Referenced(toggle, "settings:" + id), new NodeVtable
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
            });
        }

        // A left/right cycler row (Language, Screen format): the value is the game's own text
        // between the arrow buttons; Left/Right run the game's arrow-button handlers (which set
        // the language / screen mode and persist it).
        private static void AddSpinner(GraphBuilder b, TMPro.TMP_Text caption, Transform row,
            TMPro.TextMeshProUGUI valueText, string id)
        {
            var left = FindButton(row, "LeftAr");
            var right = FindButton(row, "RightAr");
            b.AddItem(ControlId.Referenced(valueText, "settings:" + id), new NodeVtable
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

        private static Button FindButton(Transform row, string name)
        {
            foreach (var btn in row.GetComponentsInChildren<Button>(true))
                if (btn.gameObject.name == name) return btn;
            throw new System.InvalidOperationException(
                "settings spinner row " + row.name + " has no button named " + name);
        }

        // The resolution dropdown: value from the model (the selected option), adjustable only in
        // windowed screen format - the game's own rule, announced with its reason since the game
        // only dims the control.
        private static void AddDropdown(GraphBuilder b, TMPro.TMP_Text caption, TMPro.TMP_Dropdown dropdown)
        {
            b.AddItem(ControlId.Referenced(dropdown, "settings:resolution"), new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiWidgets.LocalizedLabel(caption.gameObject),
                        kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => dropdown.options[dropdown.value].text,
                        live: true, kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(
                        () => dropdown.interactable ? null : Loc.T("settings.resolution_unavailable"),
                        live: true, kind: AnnouncementKinds.Enabled),
                },
                OnAdjust = (sign, large) =>
                {
                    if (!dropdown.interactable)
                    {
                        Mod.Speech.Speak(Loc.T("settings.resolution_unavailable"), interrupt: true);
                        return;
                    }
                    dropdown.value = Mathf.Clamp(dropdown.value + sign, 0, dropdown.options.Count - 1);
                },
                StateText = () => dropdown.options[dropdown.value].text,
            });
        }
    }
}
