using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine.UI;
using NameWindow = _Scripts.AMVCC.Views.Windows.NameRequestWindow.NameRequestWindow;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The new-game name request popup (UIManager.ShowNameRequestPopup - NOT tracked in the
    /// popup slot, so presence is the live NameRequestWindow component). Content: subtitle line,
    /// the name field (Sir [name] Brante, the game's prefix/postfix text), two radio pairs
    /// (Chapter Restarts, Consequences - game toggle labels), and the Start button, which the
    /// game gates on a 2+ letter name. Enter on the name field starts a MODAL EDIT: the game's
    /// TMP_InputField takes keyboard focus, this screen captures raw input (InputManager stops
    /// dispatching), and typed characters echo from the field's own text changes. The field's
    /// built-in Enter (commit) / Escape (cancel) end the edit; the game's own unpatched
    /// NameRequestWindow.Update additionally closes the whole popup on Escape - vanilla behavior.
    /// </summary>
    public sealed class NameRequestScreen : Screen
    {
        public override string Key => "namerequest";
        public override int Layer => 20;
        public override bool IsActive() => Window() != null;

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                return w == null ? null : Message.MaybeRaw(w.MainTitleText.text);
            }
        }

        // Live reference to the field being edited (read at speech time), plus the edit differ's
        // spoken baseline - the last text already announced, never game state kept for later.
        private TMPro.TMP_InputField _editField;
        private bool _wasEditing;
        private string _lastText;

        public override bool CapturesRawInput => _editField != null && _editField.isFocused;

        private static NameWindow Window() => UnityEngine.Object.FindObjectOfType<NameWindow>();

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;
            var field = w.GetComponentInChildren<TMPro.TMP_InputField>(true);
            var start = w.GetComponentInChildren<Button>(true);

            var subtitleId = ControlId.Referenced(w, "namerequest:subtitle");
            b.AddItem(subtitleId, new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => w.Subtitle.text, kind: AnnouncementKinds.Label),
                },
            });
            // Explicit start: without it the selected-member landing would seat initial focus on a
            // checked radio in this mixed form-stop instead of the top.
            b.SetStart(subtitleId);

            b.AddItem(ControlId.Referenced(field, "namerequest:name"), new NodeVtable
            {
                ControlType = ControlTypes.Edit,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("namerequest.name_row", new
                    {
                        prefix = w.NamePrefixText.text,
                        value = field.text.Length > 0 ? field.text : Loc.T("edit.blank"),
                        postfix = w.NamePostfixText.text,
                    }), kind: AnnouncementKinds.Label),
                },
                OnActivate = () => BeginEdit(field),
            });

            AddTogglePair(b, w, "Loading", "restarts", w.IronManGameModeTitle.text);
            AddTogglePair(b, w, "Consequence", "consequences", w.ConsequenceGameModeTitle.text);

            b.AddItem(ControlId.Referenced(start, "namerequest:start"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiWidgets.LabelText(start.gameObject),
                        kind: AnnouncementKinds.Label),
                    // Gate on the MODEL (the game's 2-letter rule), not the button: the game also
                    // disables the button as its double-click guard after Start fires, and the live
                    // watch must not read that as "unavailable" mid-transition.
                    new NodeAnnouncement(
                        () => field.text.Length >= 2 ? null : Loc.T("namerequest.start_unavailable"),
                        live: true, kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () =>
                {
                    if (field.text.Length < 2)
                    {
                        Mod.Speech.Speak(Loc.T("namerequest.start_unavailable"), interrupt: true);
                        return;
                    }
                    UiWidgets.Click(start.gameObject); // honors the button's own interactable state
                },
            });
        }

        // One radio pair (a ToggleGroup): its two toggles as a left/right row under the game's
        // group title, region-jumpable. Clicking the selected one is skipped (the group would
        // just force it back on); the game's click path plays the toggle sound and sets the mode.
        private static void AddTogglePair(GraphBuilder b, NameWindow w, string parentName,
            string regionKey, string title)
        {
            var pair = new List<Toggle>();
            foreach (var t in w.GetComponentsInChildren<Toggle>(true))
                if (t.transform.parent.name == parentName)
                    pair.Add(t);
            pair.Sort((a, c) => a.transform.position.x.CompareTo(c.transform.position.x));
            if (pair.Count == 0) return;

            b.PushContext(title);
            b.SetRegion(regionKey);
            b.StartRow();
            foreach (var t in pair)
            {
                var toggle = t;
                b.AddItem(
                    ControlId.Referenced(toggle,
                        "namerequest:" + regionKey + ":" + toggle.gameObject.name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.RadioButton,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => UiWidgets.LabelText(toggle.gameObject),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(
                                () => toggle.isOn ? Loc.T("state.selected") : null,
                                live: true, kind: AnnouncementKinds.Selected),
                        },
                        OnActivate = () =>
                        {
                            if (!toggle.isOn) UiWidgets.Click(toggle.gameObject);
                        },
                        StateText = () => toggle.isOn ? Loc.T("state.selected") : null,
                        // The game explains each mode only in a hover tooltip
                        // (MainMenuUITooltip off the toggle's TooltipKeyHolder key).
                        OnTooltip = toggle.GetComponent<TooltipKeyHolder>() == null
                            ? (System.Action)null
                            : () => Mod.Speech.Speak(I2.Loc.LocalizationManager.GetTranslation(
                                toggle.GetComponent<TooltipKeyHolder>().LocalizationKey)),
                    });
            }
            b.EndRow();
            b.PopContext();
            b.SetRegion(null);
        }

        private void BeginEdit(TMPro.TMP_InputField field)
        {
            _editField = field;
            _lastText = field.text;
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
            Mod.Speech.Speak(Loc.T("edit.start"), interrupt: true);
        }

        // The edit differ: echo is driven by the FIELD's text changing (model state), not our
        // keystrokes - paste, game-side rewrites, and backspace all speak correctly. Interrupts
        // are the house exception for feedback under key repeat.
        public override void OnUpdate()
        {
            bool editing = CapturesRawInput;
            if (editing)
            {
                var text = _editField.text;
                if (_wasEditing && text != _lastText)
                {
                    if (text.Length == _lastText.Length + 1 && text.StartsWith(_lastText))
                    {
                        var added = text[text.Length - 1];
                        Mod.Speech.Speak(added == ' ' ? Loc.T("edit.space") : added.ToString(),
                            interrupt: true);
                    }
                    else
                    {
                        Mod.Speech.Speak(text.Length > 0 ? text : Loc.T("edit.blank"),
                            interrupt: true);
                    }
                }
                _lastText = text;
            }
            else if (_wasEditing)
            {
                // Edit ended (field's Enter commit / Escape cancel, or the popup closed): speak
                // the resulting name. A destroyed field means the popup is gone - stay silent,
                // the screen pop announces the return.
                if (_editField != null)
                    Mod.Speech.Speak(_editField.text.Length > 0
                        ? _editField.text : Loc.T("edit.blank"), interrupt: true);
            }
            _wasEditing = editing;
        }
    }
}
