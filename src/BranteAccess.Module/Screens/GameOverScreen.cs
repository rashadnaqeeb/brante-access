using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The standalone GameOver scene: a "Game over" title over an endless credits scroll
    /// (GameOverWindow moves its Text object upward forever). No code path loads this scene -
    /// the shipping game-over ending is Credits_GameOver via WindowCreditsController - but the
    /// death flow's Bolt graphs are opaque, and if a player ever lands here the scene has no
    /// exit and would otherwise be perfectly silent. Two text rows, read live; the scene
    /// offers no interaction to mirror.
    /// </summary>
    public sealed class GameOverScreen : Screen
    {
        public override string Key => "gameover";
        public override int Layer => 10;

        private static GameOverWindow Window() => Object.FindObjectOfType<GameOverWindow>();

        public override bool IsActive()
        {
            var w = Window();
            return w != null && w.gameObject.activeInHierarchy;
        }

        // The scrolling Text object carries the "Game over" title on itself and the credits
        // block as its child - both TMPs ride the scroll together.
        public override Message ScreenName
        {
            get
            {
                var w = Window();
                if (w == null) return null;
                var t = w.Text.GetComponent<TMPro.TMP_Text>();
                return t == null ? null : Message.MaybeRaw(t.text);
            }
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;
            int i = 0;
            foreach (var t in w.GetComponentsInChildren<TMPro.TMP_Text>())
            {
                var tmp = t;
                b.AddItem(ControlId.Referenced(tmp, "gameover:text:" + i++), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => tmp.text, kind: AnnouncementKinds.Label),
                    },
                });
            }
        }
    }
}
