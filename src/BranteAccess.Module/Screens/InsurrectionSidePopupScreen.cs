using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using GameLoc = I2.Loc.LocalizationManager;
using ConditionsByParameter = _Scripts.Helpers.ConditionsByParameter;
using ConditionByObjective = _Scripts.Helpers.ConditionByObjective;
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

        // The popup controller keeps its content serialized private - same reflection route
        // as the Character window's era fields. Missing fields throw loudly at first use.
        private static FieldInfo PopupField(string name)
            => typeof(InsurrectionSidePopupController).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);

        public override Message ScreenName
        {
            get
            {
                var p = Popup();
                return p == null ? null
                    : Message.MaybeRaw(GameLoc.GetTranslation(
                        (string)PopupField("_titleKey").GetValue(p)));
            }
        }

        public override void Build(GraphBuilder b)
        {
            var pop = Popup();
            if (pop == null) return;

            b.PushContext("", role: null, positions: false);

            bool empire = ((Objective)PopupField("_empireObjective").GetValue(pop)).Unlocked;
            bool rebel = ((Objective)PopupField("_rebelObjective").GetValue(pop)).Unlocked;
            if (empire) AddSide(b, pop, empireSide: true);
            else if (rebel) AddSide(b, pop, empireSide: false);
            else
            {
                AddSide(b, pop, empireSide: true);
                AddSide(b, pop, empireSide: false);
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

        // One side's victory conditions: the side's own subtitle, then the peaceful and
        // military groups headed by the popup's own column titles, rows in the game's
        // generation order (objectives, then parameters).
        private static void AddSide(GraphBuilder b, InsurrectionSidePopupController pop,
            bool empireSide)
        {
            var side = empireSide ? "empire" : "rebel";
            AddTextRow(b, "insside:cond:" + side,
                () => GameLoc.GetTranslation((string)PopupField(
                    empireSide ? "_subtitleEmpireKey" : "_subtitleRebelKey").GetValue(pop)));

            AddGroup(b, pop, side + ":peace", "_leftSideKey",
                empireSide ? "_peaceByObjective" : "_peaceRebelByObjective",
                empireSide ? "_peaceByParameter" : "_peaceRebelByParameter", empireSide);
            AddGroup(b, pop, side + ":war", "_rightSideKey",
                empireSide ? "_warByObjective" : "_warRebelByObjective",
                empireSide ? "_warByParameter" : "_warRebelByParameter", empireSide);
        }

        private static void AddGroup(GraphBuilder b, InsurrectionSidePopupController pop,
            string id, string headerKey, string objectivesField, string parametersField,
            bool empireSide)
        {
            AddTextRow(b, "insside:cond:" + id,
                () => GameLoc.GetTranslation((string)PopupField(headerKey).GetValue(pop)));

            var objectives =
                (List<ConditionByObjective>)PopupField(objectivesField).GetValue(pop);
            for (int i = 0; i < objectives.Count; i++)
            {
                var c = objectives[i];
                AddTextRow(b, "insside:cond:" + id + ":obj:" + i,
                    () => Readouts.InsurrectionObjectiveRow(c));
            }
            var parameters =
                (List<ConditionsByParameter>)PopupField(parametersField).GetValue(pop);
            for (int i = 0; i < parameters.Count; i++)
            {
                var c = parameters[i];
                AddTextRow(b, "insside:cond:" + id + ":param:" + i,
                    () => Readouts.InsurrectionParamRow(c, empireSide));
            }
        }

        private static void AddTextRow(GraphBuilder b, string id, System.Func<string> text)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(text, kind: AnnouncementKinds.Label),
                },
            });
        }
    }
}
