using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using CaseForYearWindow = _Scripts.AMVCC.Views.Windows.CaseForYearWindow;
using CaseForYearEnabler = _Scripts.AMVCC.Views.Windows.CaseForYearEnabler;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The year-case selection (chapter 4 peace cases / chapter 5 war cases): the game deals
    /// this dialog over the scene, one button per case the current post can take, and the
    /// player must commit to one for the year. Each case is a row with the game's own title,
    /// grayed rows announce the failed requirement, and Space reads the case description,
    /// conditions with live met state, and the year's stat effects. Enter runs the game's own
    /// click path (confirm popup, then the detail popup - both covered by the popup screens).
    /// The game offers no way out of this dialog short of choosing, so there is no back action.
    /// </summary>
    public sealed class CaseForYearScreen : Screen
    {
        public override string Key => "caseforyear";
        public override int Layer => 12;

        public override bool IsActive()
            => Object.FindObjectOfType<CaseForYearWindow>() != null;

        // The dialog's own header text ("Case for the Year"), localized by the scene that
        // carries the case terms.
        public override Message ScreenName
        {
            get
            {
                var w = Object.FindObjectOfType<CaseForYearWindow>();
                if (w == null) return null;
                foreach (var t in w.GetComponentsInChildren<TMPro.TMP_Text>())
                    if (t.name == "Title") return Message.MaybeRaw(t.text);
                return null;
            }
        }

        public override void Build(GraphBuilder b)
        {
            var w = Object.FindObjectOfType<CaseForYearWindow>();
            if (w == null) return;
            // Hierarchy order is the visual order: the left page's cases, then the right's.
            b.PushContext("", role: null);
            foreach (var enabler in w.GetComponentsInChildren<CaseForYearEnabler>())
            {
                var e = enabler;
                b.AddItem(ControlId.Referenced(e, "case:" + e.CurrentCase.name), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => e.ButtonText.text,
                            kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(
                            () => UiWidgets.Interactable(e.gameObject)
                                ? null : Readouts.CaseUnavailableReason(e),
                            kind: AnnouncementKinds.Enabled),
                    },
                    SearchText = () => e.ButtonText.text,
                    OnTooltip = () => Mod.Speech.Speak(Readouts.CaseDetails(e)),
                    OnActivate = () =>
                    {
                        if (!UiWidgets.Interactable(e.gameObject))
                        {
                            Mod.Speech.Speak(Readouts.CaseUnavailableReason(e), interrupt: true);
                            return;
                        }
                        UiWidgets.Click(e.gameObject);
                    },
                });
            }
            b.PopContext();
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield break;
        }
    }
}
