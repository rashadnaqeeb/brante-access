using BranteAccess.Module.Game;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using GameDisclaimer = _Scripts.AMVCC.Views.Windows.Disclaimer.Disclaimer;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// A Disclaimer-component scene (the PreIntro pages after starting a new game): full-screen
    /// text pages advanced by clicking anywhere - the game wires an EventTrigger on the same
    /// object to ClickOnScreen, and has NO keyboard path of its own here. One node: the current
    /// page, live (the animation event that swaps the page text drives the announcement -
    /// delivery, not our input); Enter advances through the game's own click handler.
    /// </summary>
    public sealed class DisclaimerScreen : Screen
    {
        public override string Key => "disclaimer";
        public override int Layer => 0;
        public override bool IsActive()
            => UnityEngine.Object.FindObjectOfType<GameDisclaimer>() != null;

        public override void Build(GraphBuilder b)
        {
            var d = UnityEngine.Object.FindObjectOfType<GameDisclaimer>();
            if (d == null) return;
            b.AddItem(ControlId.Referenced(d, "disclaimer:page"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => d.Text.text, live: true,
                        kind: AnnouncementKinds.Label),
                },
                OnActivate = () => UiWidgets.Click(d.gameObject),
            });
        }
    }
}
