using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector;
using Kingmaker.UI.MVVM._VM.Other; // RecommendationType
using Kingmaker.UnitLogic.Class.LevelUp; // FeatureSelectionViewState.SelectState
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The generic feature-selector phase (Background, deity, heritage, and other feature picks). The
    /// game's model is "selecting a feature reveals its sub-choices" — so we reflect exactly that with
    /// ZERO view state: each selectable feature is a radio item, and a SELECTED feature that opens
    /// sub-choices reveals them inline as items beneath it (under the feature's name as context), read by
    /// arrowing down. Selecting a sibling collapses the previous (the game's radio), which just falls out
    /// of the live render. Each item reads its name + selected/disabled state (with reason), activates
    /// via the game's SetSelectedFromView, and Space opens its full write-up. The phase name (from the
    /// wizard shell) supplies the header. Built lazily by the game — renders once it materializes.
    ///
    /// The game's tag filter is VIEW state — a single <c>SearchRequest</c> string on the live
    /// <c>CharGenFeatureSearchPCView</c> that BOTH its tag dropdown and its text field write, and the
    /// selector's <c>IsVisible</c> keeps entities where <c>HasText(SearchRequest)</c> (all when empty).
    /// We reflect that live property: a tag dropdown reads/writes it (driving the game's real filter),
    /// and our list mirrors IsVisible. (Type-ahead covers free-text name search.)
    /// </summary>
    public sealed class FeatureSelectorPhaseContent : CharGenPhaseContent<CharGenFeatureSelectorPhaseVM>
    {
        // CharGenView.s_Instance → SelectedDetailView → (feature) m_CharGenFeatureSearchView → SearchRequest.
        private static readonly System.Type CharGenViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.CharGenView");
        private static readonly System.Reflection.FieldInfo InstanceField =
            CharGenViewType != null ? AccessTools.Field(CharGenViewType, "s_Instance") : null;
        private static readonly System.Reflection.FieldInfo SelectedDetailField =
            CharGenViewType != null ? AccessTools.Field(CharGenViewType, "SelectedDetailView") : null;
        private static readonly System.Type DetailViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.Phases.FeatureSelector.CharGenFeatureSelectorPhaseDetailedPCView");
        private static readonly System.Reflection.FieldInfo SearchViewField =
            DetailViewType != null ? AccessTools.Field(DetailViewType, "m_CharGenFeatureSearchView") : null;
        private static readonly System.Type SearchViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.Phases.FeatureSelector.CharGenFeatureSearchPCView");
        private static readonly System.Reflection.FieldInfo SearchRequestField =
            SearchViewType != null ? AccessTools.Field(SearchViewType, "SearchRequest") : null;
        // The selector list view: its VisibleCollection is the EXACT on-screen order (the game sorts
        // per nesting level at bind/expand/search time, then splices — it does NOT re-sort on live
        // reactive flips, so recomputing the comparer per frame drifts from the screen).
        private static readonly System.Reflection.FieldInfo SelectorViewField =
            DetailViewType != null ? AccessTools.Field(DetailViewType, "m_Selector") : null;
        private static readonly System.Type SelectorViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.Phases.FeatureSelector.CharGenFeatureSelectorPCView");
        private static readonly System.Reflection.FieldInfo VisibleCollectionField =
            SelectorViewType != null ? AccessTools.Field(SelectorViewType, "VisibleCollection") : null;

        public FeatureSelectorPhaseContent(CharGenFeatureSelectorPhaseVM phase) : base(phase) { }

        // The live SearchRequest reactive object, or null when the search view isn't present.
        private static object SearchRequestReactive()
        {
            var cgv = InstanceField?.GetValue(null);
            if (cgv == null) return null;
            var view = SelectedDetailField?.GetValue(cgv);
            if (view == null || DetailViewType == null || !DetailViewType.IsInstanceOfType(view)) return null;
            var search = SearchViewField?.GetValue(view);
            return search != null ? SearchRequestField?.GetValue(search) : null;
        }

        // The on-screen order as a rank map (VM → index in the game view's VisibleCollection), or null
        // when the selector view isn't reachable (fall back to the comparer). Read live each render.
        private static Dictionary<object, int> ScreenOrder()
        {
            var cgv = InstanceField?.GetValue(null);
            if (cgv == null) return null;
            var view = SelectedDetailField?.GetValue(cgv);
            if (view == null || DetailViewType == null || !DetailViewType.IsInstanceOfType(view)) return null;
            var selector = SelectorViewField?.GetValue(view);
            var visible = selector != null ? VisibleCollectionField?.GetValue(selector) : null;
            if (!(visible is System.Collections.IEnumerable seq)) return null;
            var rank = new Dictionary<object, int>();
            int i = 0;
            foreach (var vm in seq) { if (vm != null && !rank.ContainsKey(vm)) rank.Add(vm, i); i++; }
            return rank.Count > 0 ? rank : null;
        }

        // Order a nesting level the way the SCREEN shows it: by VisibleCollection rank when the view is
        // up (exact, including the game's sort-at-bind timing), else the game's comparer as fallback.
        private static void SortLevel(List<CharGenFeatureSelectorItemVM> items, Dictionary<object, int> rank)
        {
            if (rank != null)
                items.Sort((a, b) =>
                {
                    bool ha = rank.TryGetValue(a, out int ra), hb = rank.TryGetValue(b, out int rb);
                    if (ha && hb) return ra.CompareTo(rb);
                    if (ha != hb) return ha ? -1 : 1; // off-screen (shouldn't happen) sinks to the end
                    return CompareItems(a, b);
                });
            else
                items.Sort(CompareItems);
        }

        private static string SearchRequest()
        {
            var rp = SearchRequestReactive();
            return rp?.GetType().GetProperty("Value")?.GetValue(rp) as string;
        }

        private static void SetSearchRequest(string value)
        {
            var rp = SearchRequestReactive();
            rp?.GetType().GetProperty("Value")?.SetValue(rp, value ?? ""); // drives the game's filter too
        }

        public override void Build(GraphBuilder b, string k)
        {
            // Source + description: the source of this pick — the granting class / race / progression —
            // then the selection's own description ("what this choice is"). The source isn't shown
            // anywhere in-game, but it's too useful to omit (deliberate exception to surface-only-visible).
            var source = SourceLabel();
            if (!string.IsNullOrWhiteSpace(source))
                b.AddItem(ControlId.Structural(k + "source"),
                    GraphNodes.Text(() => Loc.T("chargen.source", new { value = source })));
            var overview = Phase.FeatureSelectorStateVM?.Feature?.Description;
            if (!string.IsNullOrWhiteSpace(overview))
                b.AddItem(ControlId.Structural(k + "overview"), GraphNodes.Text(() => overview));

            if (Phase.SelectionIsProhibited != null && Phase.SelectionIsProhibited.Value)
                b.AddItem(ControlId.Structural(k + "prohibited"),
                    GraphNodes.Text(() => Loc.T("chargen.nothing_to_select")));

            // The tag filter, reflecting the live SearchRequest — ONLY on selections whose items carry
            // feature tags (Phase.HasFeatureTags — feats yes, backgrounds/deities/heritages no; the
            // game gates its search widget the same way).
            var tags = Phase.CharGenFeatureSearchVM?.LocalizedValues;
            string search = SearchRequest();
            if (Phase.HasFeatureTags && search != null && tags != null && tags.Count > 0)
            {
                var options = new List<string> { Loc.T("filter.all") };
                options.AddRange(tags);
                b.BeginStop("filter").AddItem(ControlId.Structural(k + "filter"),
                    ModSettingNodes.ChoiceDropdown(Loc.T("chargen.filter_by_tag"), options,
                        () => { int idx = tags.IndexOf(SearchRequest() ?? ""); return idx >= 0 ? idx + 1 : 0; },
                        i => SetSearchRequest(i <= 0 || i - 1 >= tags.Count ? "" : tags[i - 1])));
            }

            var top = TopEntities();
            if (top.Count == 0) return; // lazy — renders once the selector materializes

            // Mirror CharGenFeatureSelectorPCView.IsVisible: all when SearchRequest empty, else HasText.
            var req = SearchRequest();
            if (!string.IsNullOrEmpty(req))
                top.RemoveAll(it => !it.HasText(req));
            var rank = ScreenOrder();
            SortLevel(top, rank); // the SCREEN's order (VisibleCollection), comparer fallback

            b.BeginStop("features");
            foreach (var it in top) EmitFeature(b, it, k + "f:", rank);
            // (the wizard shell's phase-name context announces "Background" / "Feat")
        }

        // One feature as a radio item. When it's selected AND opens sub-choices, its children are
        // revealed beneath it under its name as context (the game's "select = reveal"); selecting a
        // sibling deselects this one, so its children simply vanish from the next render.
        private void EmitFeature(GraphBuilder b, CharGenFeatureSelectorItemVM vm, string prefix,
            Dictionary<object, int> rank)
        {
            Func<bool> canSelect = () => vm.SelectState == FeatureSelectionViewState.SelectState.CanSelect;
            Func<bool> isSelected = () => vm.IsSelected.Value;
            string key = prefix + vm.GetHashCode();

            b.AddItem(ControlId.Referenced(vm, key), new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => vm.FeatureName ?? ""),
                    GraphNodes.SelectedPart(isSelected),
                    // The unavailable reason, when it can't be picked (live).
                    new NodeAnnouncement(() => !canSelect() && !isSelected()
                        ? vm.NotAvailableLabel?.Value : null, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(() => canSelect() || isSelected()),
                },
                SearchText = () => vm.FeatureName ?? "",
                StateText = () => isSelected() ? Loc.T("state.selected") : null,
                OnActivate = () =>
                {
                    if (!canSelect() && !isSelected()) return;
                    UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                    vm.SetSelectedFromView(true); // selecting reveals children next render; siblings drop
                },
                OnTooltip = () =>
                {
                    var tpl = vm.TooltipTemplate();
                    if (tpl != null) TooltipScreen.Open(tpl);
                },
            });

            // Selected + has sub-choices: reveal them inline under this feature's name.
            if (vm.HasNesting && isSelected())
            {
                var kids = Children(vm);
                if (kids.Count > 0)
                {
                    SortLevel(kids, rank);
                    b.PushContext(vm.FeatureName ?? "");
                    foreach (var kid in kids) EmitFeature(b, kid, key + "/", rank);
                    b.PopContext();
                }
            }
        }

        private List<CharGenFeatureSelectorItemVM> Children(CharGenFeatureSelectorItemVM vm)
        {
            var result = new List<CharGenFeatureSelectorItemVM>();
            var sel = Phase.SelectorVM;
            if (sel != null && sel.NestedEntityCollections.TryGetValue(vm, out var kids) && kids != null)
                foreach (var kk in kids)
                    if (kk is CharGenFeatureSelectorItemVM it) result.Add(it);
            return result;
        }

        // The game's feature-list order (CharGenFeatureSelectorPCView.EntityComparer): selectable
        // (selected or can-select) first, then by recommendation (Recommended > Neutral >
        // NotRecommended), then alphabetical. The game's "already has" tier is a no-op (it compares an
        // item to itself — a copy-paste bug), so we skip it to match the observable order.
        private static int CompareItems(CharGenFeatureSelectorItemVM a, CharGenFeatureSelectorItemVM b)
        {
            int s = Pickable(b).CompareTo(Pickable(a));
            if (s != 0) return s;
            int r = Recommend(b).CompareTo(Recommend(a));
            if (r != 0) return r;
            return string.Compare(a.FeatureName, b.FeatureName, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool Pickable(CharGenFeatureSelectorItemVM x)
            => x.IsSelected.Value || x.SelectState == FeatureSelectionViewState.SelectState.CanSelect;

        private static RecommendationType Recommend(CharGenFeatureSelectorItemVM x)
            => x.FeatureRecommendation.Value?.Recommendation.Value ?? RecommendationType.Neutral;

        // The selection's source blueprint reads as a class / race / progression name (the level-up
        // origin of this pick), or null if unknown.
        private string SourceLabel()
        {
            var st = Phase.FeatureSelectorStateVM?.SelectionState;
            if (st == null) return null;
            var bp = st.Source.Blueprint;
            if (bp is BlueprintCharacterClass c) return c.Name;
            if (bp is BlueprintRace r) return r.Name;
            if (bp is BlueprintFeatureBase f) return f.Name; // progression / feature (e.g. bonus-feat source)
            return null;
        }

        // The top-level feature entities, from the game's own nested-selection collection (keyed by the
        // phase, the root source) so we share the instances the game tracks.
        private List<CharGenFeatureSelectorItemVM> TopEntities()
        {
            var result = new List<CharGenFeatureSelectorItemVM>();
            var sel = Phase.SelectorVM;
            if (sel != null && sel.NestedEntityCollections.TryGetValue(Phase, out var top) && top != null)
                foreach (var e in top)
                    if (e is CharGenFeatureSelectorItemVM it) result.Add(it);
            return result;
        }
    }
}
