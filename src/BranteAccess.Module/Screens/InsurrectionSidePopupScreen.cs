using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using GameLoc = I2.Loc.LocalizationManager;
using Objective = _Scripts.AMVCC.Views.Windows.Destiny.Objective;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The side-chosen popup (chapter V, once per side selection): the victory-conditions
    /// briefing for the player's side. Rows come from the same serialized condition model the
    /// Insurrection window reads - the popup's own labels are code-set a beat after it is
    /// instantiated (its first frame still renders serialized editor text) and its generated
    /// condition views split each row into separate name and value texts, so the model rows
    /// are both race-free and whole.
    /// </summary>
    public sealed class InsurrectionSidePopupScreen : Screen
    {
        public override string Key => "popup:insside";
        public override int Layer => 21;

        private static InsurrectionSidePopupController Popup()
        {
            var p = GameUi.OpenedPopup;
            return p == null || !p.activeInHierarchy ? null
                : p.GetComponent<InsurrectionSidePopupController>();
        }

        public override bool IsActive() => Popup() != null;

        public override Message ScreenName
        {
            get
            {
                var p = Popup();
                return p == null ? null
                    : Message.MaybeRaw(GameLoc.GetTranslation((string)InsurrectionWindowScreen
                        .PopupField("_titleKey").GetValue(p)));
            }
        }

        public override void Build(GraphBuilder b)
        {
            var pop = Popup();
            if (pop == null) return;

            b.PushContext("", role: null, positions: false);

            bool empire = ((Objective)InsurrectionWindowScreen.PopupField("_empireObjective")
                .GetValue(pop)).Unlocked;
            bool rebel = ((Objective)InsurrectionWindowScreen.PopupField("_rebelObjective")
                .GetValue(pop)).Unlocked;
            if (empire) InsurrectionWindowScreen.AddSide(b, pop, empireSide: true);
            else if (rebel) InsurrectionWindowScreen.AddSide(b, pop, empireSide: false);
            else
            {
                InsurrectionWindowScreen.AddSide(b, pop, empireSide: true);
                InsurrectionWindowScreen.AddSide(b, pop, empireSide: false);
            }

            foreach (var btn in pop.GetComponentsInChildren<UnityEngine.UI.Button>())
            {
                if (!UiWidgets.Visible(btn.gameObject)) continue;
                var bt = btn;
                b.AddItem(ControlId.Referenced(bt, "insside:btn:" + bt.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(
                                () => UiWidgets.LocalizedLabel(bt.gameObject),
                                kind: AnnouncementKinds.Label),
                        },
                        OnActivate = () => UiWidgets.Click(bt.gameObject),
                    });
            }

            b.PopContext();
        }
    }
}
