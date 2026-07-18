using System;
using System.Collections.Generic;
using System.Reflection;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized rest labels)
using Kingmaker.Controllers.Rest;        // RestController, CheckStatus, RestIterationStatus
using Kingmaker.Controllers.Rest.State;  // CampingRoleType
using Kingmaker.PubSubSystem;            // EventBus, IRestRequestEvents, IRestRoleUIStageEvents
using Kingmaker.UI.MVVM._PCView.Rest;    // CraftStage (the stage-event payload)
using Kingmaker.UI.Common;               // UIUtility.AddSign
using Kingmaker.UI.MVVM._VM.Rest;        // RestVM family, UIRestPhase
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The camping window (<c>RestContextVM.RestVM</c>, the same reactive pattern as loot/dialogue),
    /// covering all three phases. Management is one Tab-stop accordion: each role — guards' two watches,
    /// camouflage, divine service, alchemist, scroll scribe — is a GROUP that reads its live assignment
    /// collapsed ("Guards, first watch: Seelah"); expanding builds Primary/Assistant member DROPDOWNS
    /// (live options via the shared chooser; selecting drives the game's own radio contract →
    /// AddUnitToRole) and raises the game's stage-open event, so the REAL role panel appears on screen in
    /// step — and because the open role is the screen's single view-state field, expansion is exclusive:
    /// opening one role closes the previous (raising its stage-close), exactly like the sighted card
    /// click. Camp-wide settings (number-of-rests dropdown over the game's iteration radios; autotune +
    /// healing toggles written to CampingState exactly like the PC view's toggle handlers) and the action
    /// buttons are leaf nodes in the same stop. Start rest is ONE VM call — RestVM.StartRest fires
    /// StartRestCommand and the game's bound view runs its own per-phase flow (fade + StartCamp /
    /// SkipPhase / FinishRest). Results reads RestController.Status, mirroring RestPCView.ShowResults'
    /// iteration picks (current iteration for camp checks, last-rolled for craft checks); phase keys
    /// carry the phase, so a transition re-homes and the phase's context label announces it. Escape
    /// mirrors CloseRest (refused while InProcess). Craft recipes (potions/scrolls) are a follow-up.
    /// </summary>
    public sealed class RestScreen : Screen
    {
        public override string Key => "ctx.rest";
        public override string ScreenName => Loc.T("screen.rest");
        public override int Layer => 15; // over the in-game context, alongside dialogue/loot

        // The open role panel (null = all collapsed) — VIEW state mirroring which game panel is up,
        // driven through the stage events below; exclusivity is by construction.
        private CampingRoleType? _openRole;

        public override void OnPush() { _openRole = null; }
        public override void OnPop() { _openRole = null; }

        private static RestVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            if (rc == null) return null;
            // In-area rest goes through RestContextVM; the WORLD-MAP fatigue rest (travel → fatigue popup →
            // accept → RestController.Start) lives on GlobalMapVM.RestVM instead. Same RestVM type, so the
            // same accordion screen drives it — we just have to look in both places.
            return rc.InGameVM?.StaticPartVM?.RestContextVM?.RestVM?.Value
                ?? rc.GlobalMapVM?.RestVM?.Value;
        }

        public override bool IsActive()
        {
            var vm = Vm();
            return vm != null && vm.CurrentPhase.Value != UIRestPhase.None;
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var phase = vm.CurrentPhase.Value;
            // Keys carry the VM and the phase: a phase transition (management → in-process → results)
            // re-homes onto the new phase's first node, whose context label announces the phase.
            string k = "rest:" + vm.GetHashCode() + ":" + phase + ":";

            switch (phase)
            {
                case UIRestPhase.Management: BuildManagement(b, vm, k); break;
                case UIRestPhase.InProcess: BuildInProcess(b, vm, k); break;
                case UIRestPhase.Results: BuildResults(b, vm, k); break;
            }
        }

        // ---- management (camp setup): one stop, roles as exclusive groups ----

        private void BuildManagement(GraphBuilder b, RestVM vm, string k)
        {
            b.BeginStop(k + "tree");

            b.AddItem(ControlId.Structural(k + "time"), GraphNodes.Text(
                () => Loc.T("rest.time", new { time = TimeText(vm.RestTime.Value) }),
                () => vm.RestingTimeTooltip));

            // The shift VMs are fetched INSIDE the expanded branch only: the game's panel views dispose
            // their VM whenever a panel hides, and the RestVM lazy properties recreate it on access —
            // touching them for collapsed roles every render would fight that dispose cycle.
            EmitRole(b, k, Loc.T("rest.role.guard_first"), CampingRoleType.GuardFirstWatch,
                () => new[] { vm.GuardVM.FirstPrimaryShiftVM, vm.GuardVM.FirstSecondaryShiftVM }, () => vm.GuardRolesVM);
            EmitRole(b, k, Loc.T("rest.role.guard_second"), CampingRoleType.GuardSecondWatch,
                () => new[] { vm.GuardVM.SecondPrimaryShiftVM, vm.GuardVM.SecondSecondaryShiftVM }, () => vm.GuardRolesVM);
            EmitRole(b, k, Loc.T("rest.role.camouflage"), CampingRoleType.Camouflage,
                () => new[] { vm.CamouflageVM.PrimaryShiftVM, vm.CamouflageVM.SecondaryShiftVM }, () => vm.CamouflageRolesVM);
            EmitRole(b, k, Loc.T("rest.role.divine"), CampingRoleType.DivineService,
                () => new[] { vm.DivineServiceVM.PrimaryShiftVM, vm.DivineServiceVM.SecondaryShiftVM }, () => vm.DivineRolesVM);
            EmitRole(b, k, Loc.T("rest.role.alchemy"), CampingRoleType.Alchemist,
                () => new[] { vm.AlchemyCraftVM.PrimaryShiftVM, vm.AlchemyCraftVM.SecondaryShiftVM }, () => vm.AlchemyRolesVM);
            EmitRole(b, k, Loc.T("rest.role.scribe"), CampingRoleType.ScrollScribe,
                () => new[] { vm.ScribesCraftVM.PrimaryShiftVM, vm.ScribesCraftVM.SecondaryShiftVM }, () => vm.ScribeRolesVM);

            // Number of rests: a dropdown over the game's iteration radios — selecting one drives the
            // game's selector, whose bound view writes RestIterationsCount. The value part is live, so
            // the autotune toggle changing the count under focus announces itself.
            b.AddItem(ControlId.Structural(k + "iterations"), GraphNodes.Dropdown(
                () => Loc.T("rest.iterations"),
                () => Game.Instance.Player.Camping.RestIterationsCount.ToString(),
                () => ChoiceSubmenuScreen.Open(Loc.T("rest.iterations"), new List<string> { "1", "2", "3" },
                    Game.Instance.Player.Camping.RestIterationsCount - 1,
                    idx =>
                    {
                        foreach (var btn in IterationButtons(vm))
                            if (btn.IterationNumber == idx + 1) btn.SetSelectedFromView(true);
                    })));

            // The autotune/healing toggles are written by the game's VIEW straight into CampingState
            // (SetAutotuneIterationsState / SetHealingState) — mirror those one-line writes. The
            // iteration radios update themselves off the autotune event.
            b.AddItem(ControlId.Structural(k + "autotune"), GraphNodes.Toggle(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.RecommendedIterationsNumber),
                () => Game.Instance.Player.Camping.AutotuneRestIterations,
                () => { var c = Game.Instance.Player.Camping; c.AutotuneRestIterations = !c.AutotuneRestIterations; }));
            b.AddItem(ControlId.Structural(k + "healing"), GraphNodes.Toggle(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.HealingUseSpells),
                () => Game.Instance.Player.Camping.UseSpells,
                () => { var c = Game.Instance.Player.Camping; c.UseSpells = !c.UseSpells; }));

            b.AddItem(ControlId.Structural(k + "autogroup"), GraphNodes.Button(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.AutoGroupTooltipHeader), vm.AutoGroup));
            b.AddItem(ControlId.Structural(k + "start"), GraphNodes.Button(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.StartButton),
                vm.StartRest)); // -> the game's view: fade out + RestController.StartCamp
        }

        // A role as an expandable group: collapsed it reads the live assignment; expanding sets it as THE
        // open role (closing the previous — accordion) and raises the game's stage-open event so the real
        // role panel shows on screen; collapsing raises stage-close — the sighted panel swap, mirrored.
        private void EmitRole(GraphBuilder b, string k, string label, CampingRoleType type,
            Func<RestShiftVM[]> shifts, Func<RestRolesVM> roles)
        {
            bool open = _openRole == type;
            var vt = GraphNodes.Group(() =>
            {
                var unit = Game.Instance?.Player?.Camping?.GetCharacterByRoleType(type, true);
                return label + ": " + (unit != null ? unit.CharacterName : Loc.T("rest.nobody"));
            });
            vt.OnExpand = () => OpenRole(type);
            vt.OnCollapse = () => CloseRole(type);
            b.BeginGroup(ControlId.Structural(k + "role:" + type), vt, expanded: open);
            if (open)
            {
                var rolesVm = roles();
                if (rolesVm != null)
                    b.AddItem(ControlId.Structural(k + "role:" + type + ":dc"), GraphNodes.Text(
                        () => Loc.T("rest.dc", new { value = rolesVm.CalculateDCValue() }),
                        () => rolesVm.DCTooltipTemplate));
                var shiftVms = shifts();
                b.AddItem(ControlId.Structural(k + "role:" + type + ":primary"),
                    SlotDropdown(Loc.T("rest.primary"), shiftVms[0]));
                b.AddItem(ControlId.Structural(k + "role:" + type + ":assistant"),
                    SlotDropdown(Loc.T("rest.assistant"), shiftVms[1]));
            }
            b.EndGroup();
        }

        private void OpenRole(CampingRoleType type)
        {
            if (_openRole.HasValue && _openRole.Value != type) CloseRole(_openRole.Value); // accordion
            _openRole = type;
            EventBus.RaiseEvent(delegate(IRestRoleUIStageEvents h) { h.RestStageOpened(type, CraftStage.UnitSelection); });
        }

        private void CloseRole(CampingRoleType type)
        {
            if (_openRole == type) _openRole = null;
            EventBus.RaiseEvent(delegate(IRestRoleUIStageEvents h) { h.RestStageClosed(type, CraftStage.UnitSelection, fromCloseButton: true); });
        }

        // A camp-slot dropdown ("Primary, combo box, Seelah, +12"). Enter opens the shared chooser
        // with LIVE options — availability shifts as other roles claim people — "None" first, the
        // assigned person always listed. Selecting drives the game's own radio contract
        // (SetSelectedFromView -> AddUnitToRole / RemoveUnitFromRole, with the game's select sound).
        // The value part is live, so an assignment settling under focus announces itself.
        private static NodeVtable SlotDropdown(string label, RestShiftVM shift)
        {
            Func<RestShiftUnitVM> selected = () => shift != null ? shift.SelectedUnit.Value : null;
            return GraphNodes.Dropdown(
                () => label,
                () => { var sel = selected(); return sel != null ? UnitLabel(sel) : Loc.T("rest.nobody"); },
                () => OpenChooser(label, shift, selected()));
        }

        private static void OpenChooser(string label, RestShiftVM shift, RestShiftUnitVM sel)
        {
            if (shift == null) return;
            // The VM's public Units collection is PAGED (6 portraits per prefab row); list everyone.
            var all = AllUnitsField?.GetValue(shift) as List<RestShiftUnitVM>;
            if (all == null) all = new List<RestShiftUnitVM>(shift.Units);
            var options = new List<string> { Loc.T("rest.nobody") };
            var units = new List<RestShiftUnitVM> { null };
            foreach (var u in all)
            {
                if (u == null) continue;
                if (u != sel && !u.IsAvailable.Value) continue; // dead / primary elsewhere — the game greys them
                options.Add(UnitLabel(u));
                units.Add(u);
            }
            int current = sel != null ? units.IndexOf(sel) : 0;
            ChoiceSubmenuScreen.Open(label, options, current, idx =>
            {
                if (idx <= 0) sel?.SetSelectedFromView(false); // None -> unassign
                else if (idx < units.Count) units[idx].SetSelectedFromView(true);
            });
        }

        private static readonly FieldInfo AllUnitsField =
            typeof(RestShiftVM).GetField("m_AllUnits", BindingFlags.NonPublic | BindingFlags.Instance);

        private static string UnitLabel(RestShiftUnitVM u)
        {
            var name = u.UnitData != null ? u.UnitData.CharacterName : "";
            return name + ", " + UIUtility.AddSign(u.SkillValue.Value);
        }

        private static readonly FieldInfo IterButtonsField =
            typeof(RestVM).GetField("m_IterationsButtons", BindingFlags.NonPublic | BindingFlags.Instance);

        private static IEnumerable<RestIterationRadioButtonVM> IterationButtons(RestVM vm)
            => IterButtonsField?.GetValue(vm) as List<RestIterationRadioButtonVM>
               ?? (IEnumerable<RestIterationRadioButtonVM>)new RestIterationRadioButtonVM[0];

        // ---- in process (the night plays out) ----

        private static void BuildInProcess(GraphBuilder b, RestVM vm, string k)
        {
            b.BeginStop(k + "list").PushContext(Loc.T("rest.in_process"));
            b.AddItem(ControlId.Structural(k + "continue"), GraphNodes.Button(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.ContinueButton),
                vm.StartRest)); // → the game's view: RestController.SkipPhase
            b.PopContext();
        }

        // ---- results ----

        private static void BuildResults(GraphBuilder b, RestVM vm, string k)
        {
            b.BeginStop(k + "list").PushContext(Loc.T("rest.results"));
            var status = RestController.Instance != null ? RestController.Instance.Status : null;
            if (status != null)
            {
                b.AddItem(ControlId.Structural(k + "time"), GraphNodes.Text(
                    () => Loc.T("rest.total_time", new { time = TimeText(status.TotalTime) })));
                if (status.WasNightRandomEncounter)
                {
                    var watch = Loc.T(status.WakeUpGuardsSlot == 1 ? "rest.role.guard_second" : "rest.role.guard_first");
                    b.AddItem(ControlId.Structural(k + "encounter"), GraphNodes.Text(
                        // NightEncounter is a format TEMPLATE ("After {0} hours of rest…") — fill the
                        // hours the way RestController's own notification does.
                        () => string.Format(TextUtil.StripRichText(UIStrings.Instance.Rest.NightEncounter),
                                status.TimePassedBeforeEncounter.Hours) + ", " + watch));
                }
                var iters = status.Iterations;
                if (iters != null && iters.Count > 0)
                {
                    // Mirror RestPCView.ShowResults: camp checks from the current iteration, craft
                    // checks from whichever iteration last rolled them.
                    var cur = iters[Math.Min(status.IterationNumber, iters.Count - 1)];
                    EmitCheck(b, k, Loc.T("rest.role.divine"), cur.DivineService);
                    EmitCheck(b, k, Loc.T("rest.role.camouflage"), cur.Camouflage);
                    EmitCheck(b, k, Loc.T("rest.role.guard_first"), cur.GuardFirst);
                    EmitCheck(b, k, Loc.T("rest.role.guard_second"), cur.GuardSecond);
                    EmitCheck(b, k, Loc.T("rest.role.alchemy"), LastCheck(iters, s => s.AlchemyPotions));
                    EmitCheck(b, k, Loc.T("rest.role.cooking"), LastCheck(iters, s => s.AlchemyCooking));
                    EmitCheck(b, k, Loc.T("rest.role.scribe"), LastCheck(iters, s => s.ScrollScribing));
                }
            }
            b.AddItem(ControlId.Structural(k + "continue"), GraphNodes.Button(
                () => TextUtil.StripRichText(UIStrings.Instance.Rest.ContinueButton),
                vm.StartRest)); // → the game's view: RestController.FinishRest
            b.PopContext();
        }

        private static CheckStatus LastCheck(List<RestIterationStatus> iters, Func<RestIterationStatus, CheckStatus> get)
        {
            for (int i = iters.Count - 1; i >= 0; i--)
            {
                var c = get(iters[i]);
                if (c != null && c.Check != null) return c;
            }
            return null;
        }

        private static void EmitCheck(GraphBuilder b, string k, string role, CheckStatus check)
        {
            if (check == null || check.Check == null) return;
            var c = check;
            var r = role;
            b.AddItem(ControlId.Structural(k + "check:" + role), GraphNodes.Text(() => Loc.T("rest.check", new
            {
                role = r,
                unit = c.Check.Initiator != null ? c.Check.Initiator.CharacterName : "",
                roll = c.Check.RollResult,
                dc = c.Check.DC,
                result = Loc.T(c.Success ? "rest.success" : "rest.failure"),
            })));
        }

        // ---- shared ----

        private static string TimeText(TimeSpan t)
            => string.Format(UIStrings.Instance.TimeTexts.TimeDay, t.Days) + " "
             + string.Format(UIStrings.Instance.TimeTexts.TimeHour, t.Hours);

        // Escape mirrors the view's CloseRest: refused mid-rest, otherwise the close-request event
        // (the same one its X button raises) tears the window down and stops Rest mode.
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null && vm.CurrentPhase.Value != UIRestPhase.InProcess)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => EventBus.RaiseEvent(delegate(IRestRequestEvents h) { h.HandleRestCloseRequest(); }));
        }
    }
}
