using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using InsurrectionWindow = _Scripts.AMVCC.Views.Windows.InsurrectionWindowController;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;
using ParametersList = _Scripts.AMVCC.Models.Static.ParametersList;
using GameLoc = I2.Loc.LocalizationManager;
using ConditionsByParameter = _Scripts.Helpers.ConditionsByParameter;
using ConditionByObjective = _Scripts.Helpers.ConditionByObjective;
using Objective = _Scripts.AMVCC.Views.Windows.Destiny.Objective;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Insurrection window (chapter V): title from the window's own I2 key, the chosen-side
    /// panel (or the game's no-side text), the insurrection parameter rows, then the victory
    /// conditions the game shows only behind hover tooltips - a peaceful and a military group
    /// per side, composed from the side popup's serialized condition model with live met state.
    /// With no side chosen yet both sides read, matching the window's two hover targets.
    /// </summary>
    public sealed class InsurrectionWindowScreen : Screen
    {
        public override string Key => "window:insurrection";
        public override int Layer => 10;

        private static InsurrectionWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<InsurrectionWindow>();
        }

        public override bool IsActive() => Window() != null;

        // The controllers keep their content serialized private - same reflection route as the
        // Character window's era fields. Missing fields throw loudly at first use.
        private static FieldInfo WindowField(string name)
            => typeof(InsurrectionWindow).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);

        internal static FieldInfo PopupField(string name)
            => typeof(InsurrectionSidePopupController).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                return w == null ? null
                    : Message.MaybeRaw(GameLoc.GetTranslation(
                        (string)WindowField("_windowTitleKey").GetValue(w)));
            }
        }

        // The condition model rides the persistent tooltip instance of the side popup (the
        // window's hover targets configure and show that same instance).
        private static InsurrectionSidePopupController ConditionSource()
        {
            foreach (var p in Resources.FindObjectsOfTypeAll<InsurrectionSidePopupController>())
                if ((bool)PopupField("_itsTooltip").GetValue(p))
                    return p;
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var iw = Window();
            if (iw == null) return;
            // Populate gate (Work-window pattern): the game fills the prefab in Start(), a
            // beat after ShowWindow - no graph until the first parameter row reads localized.
            var first = iw.GetComponentInChildren<ParameterComponent>();
            if (first == null) return;
            var firstName = System.Enum.GetName(typeof(ParametersList),
                first.GetComponent<ParameterGetSet>().Parameter.ParameterName);
            if (first.Name.text != GameLoc.GetTranslation(firstName)) return;

            b.PushContext("", role: null, positions: false);

            // The side panel the game shows: side name when chosen, its no-side text otherwise.
            var have = (GameObject)WindowField("_haveSidePanel").GetValue(iw);
            var none = (GameObject)WindowField("_noSidePanel").GetValue(iw);
            var panel = have != null && have.activeSelf ? have
                : none != null && none.activeSelf ? none : null;
            if (panel != null)
            {
                var p = panel;
                b.AddItem(ControlId.Referenced(p.transform, "insurrection:side"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LabelText(p),
                            kind: AnnouncementKinds.Label),
                    },
                });
            }

            ParameterRows.Add(b, iw, "insurrection:parameter:");

            var pop = ConditionSource();
            if (pop != null)
            {
                bool empire = ((Objective)PopupField("_empireObjective").GetValue(pop)).Unlocked;
                bool rebel = ((Objective)PopupField("_rebelObjective").GetValue(pop)).Unlocked;
                if (empire) AddSide(b, pop, empireSide: true);
                else if (rebel) AddSide(b, pop, empireSide: false);
                else
                {
                    AddSide(b, pop, empireSide: true);
                    AddSide(b, pop, empireSide: false);
                }
            }

            b.PopContext();
            HudBar.Build(b);
        }

        // One side's victory conditions: the side's own subtitle, then the peaceful and
        // military groups headed by the popup's own column titles, rows in the game's
        // generation order (objectives, then parameters).
        internal static void AddSide(GraphBuilder b, InsurrectionSidePopupController pop,
            bool empireSide)
        {
            var side = empireSide ? "empire" : "rebel";
            AddTextRow(b, "insurrection:cond:" + side,
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
            AddTextRow(b, "insurrection:cond:" + id,
                () => GameLoc.GetTranslation((string)PopupField(headerKey).GetValue(pop)));

            var objectives =
                (List<ConditionByObjective>)PopupField(objectivesField).GetValue(pop);
            for (int i = 0; i < objectives.Count; i++)
            {
                var c = objectives[i];
                AddTextRow(b, "insurrection:cond:" + id + ":obj:" + i,
                    () => Readouts.InsurrectionObjectiveRow(c));
            }
            var parameters =
                (List<ConditionsByParameter>)PopupField(parametersField).GetValue(pop);
            for (int i = 0; i < parameters.Count; i++)
            {
                var c = parameters[i];
                AddTextRow(b, "insurrection:cond:" + id + ":param:" + i,
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

        public override string HelpText() => GameUi.WindowHelp();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
