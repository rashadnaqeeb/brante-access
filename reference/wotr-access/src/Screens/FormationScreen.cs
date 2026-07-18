using System.Collections.Generic;
using Kingmaker; // Game
using Kingmaker.Blueprints.Root.Strings; // UIStrings.FormationTexts (game-localized labels)
using Kingmaker.UI.MVVM._VM.Formation; // FormationVM
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateGlossary ("Hold the line")
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The party-formation window (<see cref="FormationVM"/> on <c>InGameStaticPartVM.FormationVM</c>),
    /// opened from the HUD menu's Formation button — graph-native. Tab stops, in order: the
    /// <b>formations list</b> (a radio of the 6 — Optimal Auto first, then the editable named ones), the
    /// WASD editing <b>field</b>, <b>Restore to default</b>, the <b>Hold the line</b> preserve-formation
    /// toggle, and <b>Close</b>. The list drives the game's own SelectionGroupRadioVM; Restore/Hold only
    /// apply to an editable (Custom) formation, so they grey out on the Auto one. The field's 2-D cursor
    /// is OUR editor state (like the exploration cursor), held on <see cref="FormationField"/> — reset
    /// each open, never mirroring game state. Layer 16, Exclusive (owns the keyboard while open).
    /// Back / Escape closes via FormationVM.Close.
    /// </summary>
    public sealed class FormationScreen : Screen
    {
        public FormationScreen() { Wrap = true; }

        public override string Key => "overlay.formation";
        public override string ScreenName => Loc.T("screen.formation");
        public override int Layer => 16;
        public override bool Exclusive => true;
        public override bool AllowsTypeahead => false; // WASD drive the editor field; no name-search needed

        // While the WASD editor field is focused, claim the Formation category so WASD move the cursor; on
        // the other tab stops only UI is live (so WASD stay free). Mirrors the game's focus-driven flip.
        private static readonly WrathAccess.Input.InputCategory[] FieldCats =
            { WrathAccess.Input.InputCategory.Formation, WrathAccess.Input.InputCategory.UI };
        private static readonly WrathAccess.Input.InputCategory[] BaseCats =
            { WrathAccess.Input.InputCategory.UI };
        public override IReadOnlyList<WrathAccess.Input.InputCategory> InputCategories
            => IsFieldFocused ? FieldCats : BaseCats;

        public override bool IsActive() => Vm() != null;

        internal static FormationVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.FormationVM?.Value;

        // The editor's 2-D cursor + held member — mod-owned editor state, fresh per open.
        private FormationField _field = new FormationField();

        private bool IsFieldFocused
            => ReferenceEquals(ScreenManager.Current, this) && Equals(Navigation.FocusedStopKey, "field");

        /// <summary>The editor field of the open, field-focused formation screen — the target the
        /// Formation-category input actions (WASD/Comma/Slash/C/Ctrl+N) route to; null otherwise.</summary>
        internal static FormationField FocusedField
            => ScreenManager.Current is FormationScreen fs && fs.IsFieldFocused ? fs._field : null;

        public override void OnPush() { _field = new FormationField(); }

        // The glide tick (Shift+WASD held) runs only while the field is focused — the same scope the
        // old per-focused-element OnUpdate had.
        public override void OnUpdate() { if (IsFieldFocused) _field.Tick(); }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // The formations, in the game's order (Optimal Auto, then the five editable named shapes).
            // Names come from the formation blueprints (game-localized, passed through).
            var names = FormationNames();
            b.PushContext(Loc.T("formation.list"), "list");
            int i = 0;
            foreach (var item in vm.FormationSelector.EntitiesCollection)
            {
                if (item == null) { i++; continue; }
                var it = item; // capture for the live closure
                b.AddItem(ControlId.Referenced(it, "form:" + i), GraphNodes.SelectionItem(it,
                    () => it.FormationIndex >= 0 && it.FormationIndex < names.Count ? names[it.FormationIndex] : ""));
                i++;
            }
            b.PopContext();

            // The WASD editing field: one stop holding the 2-D cursor. Enter picks up / drops; the value
            // reads what the cursor is over (moves speak themselves, so the part isn't live).
            b.BeginStop("field").AddItem(ControlId.Structural("field"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => Loc.T("formation.field")),
                    new NodeAnnouncement(() => _field.CellReadout(), kind: AnnouncementKinds.Value),
                },
                SearchText = () => Loc.T("formation.field"),
                OnActivate = () => _field.PickOrDrop(), // pick-up/placement speaks itself (no generic click)
            });

            // Footer. Restore + Hold the line act on the Custom formation only (the game greys them on
            // Auto); their labels are the game's own localized strings.
            var t = UIStrings.Instance.FormationTexts;
            b.BeginStop("restore").AddItem(ControlId.Structural("restore"), GraphNodes.Button(
                () => (string)t.RestoreToDefault,
                () => Vm()?.ResetCurrentFormation(),
                () => Vm()?.IsCustomFormation ?? false));
            var hold = GraphNodes.Toggle(
                () => (string)t.HoldTheLine,
                () => Vm()?.IsPreserveFormation.Value ?? false,
                () => Vm()?.SwitchPreserveFormation(),
                () => Vm()?.IsCustomFormation ?? false);
            hold.OnTooltip = () => TooltipScreen.Open(new TooltipTemplateGlossary("HoldTheLine")); // the game's glossary entry
            b.BeginStop("hold").AddItem(ControlId.Structural("hold"), hold);
            b.BeginStop("close").AddItem(ControlId.Structural("close"), GraphNodes.Button(
                () => Loc.T("action.close"), () => Vm()?.Close()));
        }

        // The predefined formations' display names, in order (parallel to the selector items by index).
        private static List<string> FormationNames()
        {
            var list = new List<string>();
            var formations = Game.Instance?.BlueprintRoot?.Formations?.PredefinedFormations;
            if (formations != null)
                foreach (var f in formations) list.Add(f != null ? (string)f.Name : "");
            return list;
        }
    }
}
