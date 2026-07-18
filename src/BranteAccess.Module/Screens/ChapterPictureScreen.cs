using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using StartPictureHelper = _Scripts.Helpers.StartPictureHelper;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The chapter title splash (StartPictureHelper - "ChildhoodPicture" and its siblings): a
    /// full-screen picture with the chapter title, dismissed by clicking anywhere. One node
    /// carrying the game's own localized title; Enter runs the game's click handler so the hide
    /// animation, sfx and the Bolt flow advance exactly as a mouse click would. The dismiss
    /// gesture is unusual (the whole screen is the control), so the hint is part of the readout.
    /// </summary>
    public sealed class ChapterPictureScreen : Screen
    {
        public override string Key => "chapterpicture";
        public override int Layer => 0;

        public override bool IsActive()
            => Object.FindObjectOfType<StartPictureHelper>() != null;

        private static StartPictureHelper Helper() => Object.FindObjectOfType<StartPictureHelper>();

        public override void Build(GraphBuilder b)
        {
            var helper = Helper();
            if (helper == null) return;
            b.AddItem(ControlId.Referenced(helper, "chapterpicture:title"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => helper.Title.text, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Loc.T("chapterpicture.hint")),
                },
                OnActivate = () => Helper().OnPointerClick(),
            });
        }
    }
}
