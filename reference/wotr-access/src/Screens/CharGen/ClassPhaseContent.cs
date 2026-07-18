using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateGlossary
using Owlcat.Runtime.UI.Tooltips;
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Class phase: the class list, the selected class's archetypes (nested sub-options), and a live
    /// Details panel that mirrors the game's two detail modes (toggled by a "Detailed description"
    /// switch, matching its in-UI button):
    ///   • Short — renders the selection's own template (what the game's InfoSectionView shows for
    ///     the selected class/archetype), at <see cref="TooltipTemplateType.Info"/>.
    ///   • Mechanic — the plain bindings: name + full description + saves/BAB/HP grades + caster
    ///     stats + class skills, plus the per-level progression grid (ProgressionGrid.Emit).
    /// Classes/archetypes come from the canonical (selectable) instances so SetSelectedFromView works.
    /// Archetype/detail keys carry the selection (+ mode), so they re-key on change while list focus
    /// stays put.
    /// </summary>
    public sealed class ClassPhaseContent : CharGenPhaseContent<CharGenClassPhaseVM>
    {
        // Private list of class items — reflected (no public accessor; reflection is fine here).
        private static readonly System.Reflection.FieldInfo ClassesField =
            AccessTools.Field(typeof(CharGenClassPhaseVM), "m_ClassesVMs");

        // The Short/Mechanic toggle is VIEW state (CharGenClassPhaseDetailedPCView.m_ViewMode), not on
        // the VM — so to make the rendered screen track our toggle, we reach the live view and call its
        // private SwitchMode(). We get there via CharGenView's static self-ref → SelectedDetailView
        // (the active detailed phase view) — a deterministic chain, no FindObjectOfType scene scan; a
        // no-op when the view is in Level-up mode (no switch) or isn't present.
        private static readonly System.Type DetailViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.Phases.Class.CharGenClassPhaseDetailedPCView");
        private static readonly System.Reflection.FieldInfo ViewModeField =
            DetailViewType != null ? AccessTools.Field(DetailViewType, "m_ViewMode") : null;
        private static readonly System.Reflection.MethodInfo SwitchModeMethod =
            DetailViewType != null ? AccessTools.Method(DetailViewType, "SwitchMode") : null;
        private static readonly System.Type ViewModeEnum = ResolveModeEnum();

        // CharGenView.s_Instance (static self-ref) → SelectedDetailView (current detailed phase view).
        private static readonly System.Type CharGenViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.CharGenView");
        private static readonly System.Reflection.FieldInfo InstanceField =
            CharGenViewType != null ? AccessTools.Field(CharGenViewType, "s_Instance") : null;
        private static readonly System.Reflection.FieldInfo SelectedDetailField =
            CharGenViewType != null ? AccessTools.Field(CharGenViewType, "SelectedDetailView") : null;

        // The live class detail view via the static chain — only when SelectedDetailView is actually
        // the class detail view (i.e. we're on the class phase), else null.
        private static object ActiveClassDetailView()
        {
            var cgv = InstanceField?.GetValue(null);
            if (cgv == null) return null;
            var view = SelectedDetailField?.GetValue(cgv);
            return DetailViewType != null && view != null && DetailViewType.IsInstanceOfType(view) ? view : null;
        }

        private static System.Type ResolveModeEnum()
        {
            var t = ViewModeField?.FieldType; // ReactiveProperty<ClassDetailedViewMode?>
            if (t == null || !t.IsGenericType) return null;
            var arg = t.GetGenericArguments()[0];
            return System.Nullable.GetUnderlyingType(arg) ?? arg;
        }

        public ClassPhaseContent(CharGenClassPhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            SyncGamePanel();
            int ci = 0;
            foreach (var item in Classes())
            {
                b.AddItem(ControlId.Referenced(item, k + "class:" + ci), CharGenNodes.ClassItem(item));
                ci++;
            }

            // The selected class's archetypes — keyed per class, so a class change re-keys the list.
            var cls = Phase.SelectedClassVM.Value;
            var archetypes = Archetypes().ToList();
            if (archetypes.Count > 0)
            {
                string ak = k + "arch:" + (cls?.GetHashCode() ?? 0) + ":";
                b.BeginStop("archetypes").PushContext(Loc.T("chargen.archetypes"), "list");
                // "No archetype" (base class) — selected when none is chosen; the game has no VM for
                // this, so we synthesize a clear, discoverable control alongside the game's gestures.
                b.AddItem(ControlId.Structural(ak + "none"), new NodeVtable
                {
                    ControlType = ControlTypes.RadioButton,
                    Announcements = new List<NodeAnnouncement>
                    {
                        GraphNodes.LabelPart(() => Loc.T("chargen.no_archetype")),
                        GraphNodes.SelectedPart(() => Phase.SelectedArchetypeVM.Value == null),
                    },
                    SearchText = () => Loc.T("chargen.no_archetype"),
                    StateText = () => Phase.SelectedArchetypeVM.Value == null ? Loc.T("state.selected") : null,
                    OnActivate = () =>
                    {
                        UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                        var c = Phase.SelectedClassVM.Value;
                        c?.WarnLevelupPlansWillDropBeforeAction(() => c.TryUnselectArchetypes());
                    },
                });
                int ai = 0;
                foreach (var a in archetypes)
                {
                    b.AddItem(ControlId.Referenced(a, ak + ai), CharGenNodes.ClassItem(a));
                    ai++;
                }
                b.PopContext();
            }

            // Mode switch — mirrors the game's "Detailed description" button. Reads the GAME view's
            // live mode (reflected below) and flips it via the game's own SwitchMode — no shadow state.
            bool detailed = IsDetailed();
            b.BeginStop("mode").AddItem(ControlId.Structural(k + "mode"), GraphNodes.Toggle(
                () => (string)UIStrings.Instance.CharGen.DetailedDescription,
                IsDetailed,
                ToggleDetailed));

            // The details, keyed per class + archetype + mode so any change re-keys them.
            string dk = k + "detail:" + (cls?.GetHashCode() ?? 0) + ":"
                + (Phase.SelectedArchetypeVM.Value?.GetHashCode() ?? 0) + ":" + (detailed ? "M" : "S") + ":";
            b.BeginStop("details").PushContext(Loc.T("chargen.details"), role: null, positions: false);
            if (cls != null)
            {
                if (detailed) EmitMechanic(b, dk);
                else EmitShort(b, dk);
            }
            b.PopContext();

            // The progression grid + auto-levelup, in Mechanic mode only — their OWN Tab-stops after
            // the details (the grid is a big table; burying it at the bottom of the details vertical
            // makes it undiscoverable by Tab).
            if (cls != null && detailed) EmitProgression(b, dk);
        }

        // The game's on-screen info panel (InfoSectionView ← ReactiveTooltipTemplate) is HOVER-fed:
        // rows scrolling under the parked mouse write their template into it, so the panel can show a
        // class nobody selected (a sighted observer sees Oracle while Sorcerer is selected — the mod's
        // own reads/announcements are unaffected). While our UI drives, re-assert the SELECTION's
        // template whenever the panel diverges — the same UpdateTooltipTemplate(hover: false) the
        // game's selection path calls. Cheap: two reflected field reads per render, write only on
        // divergence.
        private static readonly Dictionary<System.Type, System.Reflection.FieldInfo> TplClassFields =
            new Dictionary<System.Type, System.Reflection.FieldInfo>();
        private static readonly Dictionary<System.Type, System.Reflection.FieldInfo> TplArchFields =
            new Dictionary<System.Type, System.Reflection.FieldInfo>();

        private static object TplField(Dictionary<System.Type, System.Reflection.FieldInfo> cache,
            object tpl, string name)
        {
            var t = tpl.GetType();
            if (!cache.TryGetValue(t, out var f)) { f = AccessTools.Field(t, name); cache[t] = f; }
            return f?.GetValue(tpl);
        }

        private void SyncGamePanel()
        {
            var cls = Phase.SelectedClassVM.Value?.Class;
            if (cls == null) return;
            var tpl = Phase.ReactiveTooltipTemplate.Value;
            if (tpl == null) return;
            var tplClass = TplField(TplClassFields, tpl, "Class");
            if (tplClass == null) return; // not a class template (glossary page etc.) — leave it be
            var arch = Phase.SelectedArchetypeVM.Value?.Archetype;
            var tplArch = TplField(TplArchFields, tpl, "Archetype");
            if (!ReferenceEquals(tplClass, cls) || !ReferenceEquals(tplArch, arch))
                Phase.UpdateTooltipTemplate(hover: false);
        }

        // The current detail mode, READ from the game's own class-detail view (its m_ViewMode reactive —
        // the source of truth, not a shadow). False (Short) when the view isn't present or is bound to
        // Level-up (which has no Short/Mechanic split).
        private bool IsDetailed()
        {
            if (ViewModeField == null || ViewModeEnum == null) return false;
            var view = ActiveClassDetailView();
            if (view == null) return false;
            var rp = ViewModeField.GetValue(view);
            var cur = rp?.GetType().GetProperty("Value")?.GetValue(rp);
            return cur != null && cur.Equals(System.Enum.Parse(ViewModeEnum, "MechanicDescription"));
        }

        // Flip the game's own Short↔Mechanic view mode (the same SwitchMode its button calls); a no-op
        // when the view isn't present or is in Level-up mode. Next render's IsDetailed() reflects it.
        private void ToggleDetailed()
        {
            if (SwitchModeMethod == null || ViewModeEnum == null) return;
            var view = ActiveClassDetailView();
            if (view == null) return;
            var rp = ViewModeField.GetValue(view);
            var cur = rp?.GetType().GetProperty("Value")?.GetValue(rp);
            if (cur != null && cur.Equals(System.Enum.Parse(ViewModeEnum, "Levelup"))) return; // no switch in level-up
            SwitchModeMethod.Invoke(view, null);
        }

        // Short mode mirrors the game's InfoSectionView content, computed LIVE from the SELECTION —
        // never from the phase's shared ReactiveTooltipTemplate (the game also writes HOVER templates
        // into that reactive, so it tracks the mouse, not the selection). Same precedence as the VM's
        // own TooltipTemplate(hover): the selected archetype's template, else the selected class's.
        private void EmitShort(GraphBuilder b, string dk)
        {
            var item = Phase.SelectedArchetypeVM.Value ?? Phase.SelectedClassVM.Value;
            var tpl = item?.TooltipTemplate(hover: false);
            if (tpl == null) return;
            TooltipFlowBuilder.Emit(b, dk, tpl, TooltipTemplateType.Info, includeEmptyNotice: false);
        }

        // Mechanic mode reconstructs the game's Detailed PANEL (plain VM bindings, NOT a tooltip):
        // name, full description, then Martial / Caster / Class-skill groups, the progression grid,
        // and — only when actually present — the auto-levelup button.
        private void EmitMechanic(GraphBuilder b, string dk)
        {
            var cs = UIStrings.Instance.CharacterSheet;
            var cg = UIStrings.Instance.CharGen;

            var name = Phase.ClassDisplayName.Value;
            if (!string.IsNullOrEmpty(name))
                b.AddItem(ControlId.Structural(dk + "name"), GraphNodes.Text(() => Phase.ClassDisplayName.Value));
            var desc = Phase.ClassDescription.Value;
            if (!string.IsNullOrEmpty(desc))
                b.AddItem(ControlId.Structural(dk + "desc"), GraphNodes.Text(() => Phase.ClassDescription.Value));

            // Saves/BAB are progression GRADES here (the panel's representation), not numbers.
            var m = Phase.MartialStatsVM.Value;
            if (m != null)
            {
                b.PushContext(Loc.T("chargen.martial_stats"));
                StatRow(b, dk + "bab", (string)cs.BAB, () => m.BAB.Value, "BaseAttackBonus");
                StatRow(b, dk + "fort", (string)cs.FORTITUDE, () => m.Fortitude.Value, "SaveFortitude");
                StatRow(b, dk + "reflex", (string)cs.REFLEX, () => m.Reflex.Value, "SaveReflex");
                StatRow(b, dk + "will", (string)cs.WILL, () => m.Will.Value, "SaveWill");
                StatRow(b, dk + "hp", (string)cs.HP, () => m.HitPointsFirstLevel.Value, "HP");
                StatRow(b, dk + "hpl", (string)cg.HPPerLevel, () => m.HitPointsPerLevel.Value, "HPPerLevel");
                b.PopContext();
            }

            var c = Phase.ClassCasterStatsVM.Value;
            if (c != null && c.CanCast.Value)
            {
                b.PushContext(Loc.T("chargen.caster_stats"));
                StatRow(b, dk + "msl", (string)cg.MaxSpellsLevel, () => c.MaxSpellsLevel.Value, "MaxSpellsLevel");
                StatRow(b, dk + "cas", (string)cg.CasterAbilityScore, () => c.CasterAbilityScore.Value, "CasterAbilityScore");
                StatRow(b, dk + "ct", (string)cg.CasterType, () => c.CasterMindType.Value, "CasterType");
                StatRow(b, dk + "sut", (string)cg.SpellbookUseType, () => c.SpellbookUseType.Value, "CasterMemoryType");
                b.PopContext();
            }

            var s = Phase.ClassSkillsVM.Value;
            if (s != null && s.ClassSkills != null && s.ClassSkills.Count > 0)
            {
                b.PushContext((string)cs.ClassSkills);
                int i = 0;
                foreach (var entry in s.ClassSkills)
                {
                    if (entry == null) { i++; continue; }
                    var e = entry;
                    b.AddItem(ControlId.Structural(dk + "skill:" + i), GraphNodes.Text(
                        () => e.DisplayName, () => e.TooltipTemplate));
                    i++;
                }
                b.PopContext();
            }

        }

        // The progression grid and the auto-levelup button — own Tab-stops (Mechanic mode only).
        private void EmitProgression(GraphBuilder b, string dk)
        {
            // Progression grid: levels = columns, feature lines = rows (banded by class / Shared). The
            // chargen class-Mechanic UnitProgressionView prefab uniquely leaves m_FeatProgressionView
            // unwired, suppressing the Feats band here — mirror that (feats/race show up on the
            // feature-selector phases via the global edge-window, not on the class screen).
            b.BeginStop("progression");
            ProgressionGrid.Emit(b, dk + "prog:", Phase.ProgressionVM, Phase.SelectedClassVM.Value?.Class,
                new ProgressionGrid.Options { IncludeFeats = false });

            // Auto-levelup button: present only when the game shows it active (first level + a default
            // build plan). Label/enabled mirror the view; activate opens its confirm dialog.
            var al = Phase.AutoLevelupButtonVM;
            if (al != null && al.ButtonIsActiveProperty.Value)
            {
                b.BeginStop("autolevel").AddItem(ControlId.Structural(dk + "autolevel"), GraphNodes.Button(
                    () => al.AutoLevelupIsAccessible.Value
                        ? (string)UIStrings.Instance.CharGen.LoadDefaultClassButton
                        : (string)UIStrings.Instance.CharGen.NoDefaultBuildForArchetype,
                    () => al.RequestActivateAutoLevelup(),
                    () => al.ButtonIsActiveProperty.Value && al.AutoLevelupIsAccessible.Value && !al.AutoLevelupIsOnProperty.Value));
            }
        }

        // One mechanic stat row: "Label: grade", with the stat's game glossary tooltip on Space.
        private static void StatRow(GraphBuilder b, string key, string label, System.Func<string> value, string glossaryKey)
        {
            b.AddItem(ControlId.Structural(key), GraphNodes.Text(
                () => { var v = value(); return string.IsNullOrEmpty(v) ? label : label + ": " + v; },
                () => new TooltipTemplateGlossary(glossaryKey)));
        }

        private IEnumerable<CharGenClassSelectorItemVM> Classes()
        {
            var list = ClassesField?.GetValue(Phase) as IEnumerable<CharGenClassSelectorItemVM>;
            if (list == null) yield break;
            foreach (var c in list)
                if (c != null) yield return c;
        }

        // The selected class's archetypes — cached in the nested group once the class is selected
        // (expanded), so these are the same instances SetSelectedFromView acts on.
        private IEnumerable<CharGenClassSelectorItemVM> Archetypes()
        {
            var cls = Phase.SelectedClassVM.Value;
            if (cls == null || Phase.ClassSelector == null) yield break;
            if (Phase.ClassSelector.NestedEntityCollections.TryGetValue(cls, out var list) && list != null)
                foreach (var e in list)
                    if (e is CharGenClassSelectorItemVM a) yield return a;
        }
    }
}
