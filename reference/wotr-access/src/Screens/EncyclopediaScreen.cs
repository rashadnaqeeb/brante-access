using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Encyclopedia; // IPage, UnitInfoPage
using Kingmaker.PubSubSystem; // EventBus, IEncyclopediaHandler
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Encyclopedia; // EncyclopediaVM, EncyclopediaNavigationElementVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Encyclopedia.Blocks; // block VMs
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The encyclopedia service window (<see cref="EncyclopediaVM"/>), graph-native. The game has no
    /// search; its navigation is a fully-expandable hierarchy tree (chapters → pages → subpages), so we
    /// mirror that: the whole hierarchy as nested GROUPS (children materialize lazily — a collapsed
    /// chapter's child VMs are never built, gated on <see cref="GraphBuilder.IsExpanded"/>) plus the
    /// current page — title, text, and links to its child pages, keyed per page so navigating re-keys it
    /// while the tree keeps its expansion and position. Enter on any node loads its page and lands focus
    /// on the page top (announced). Navigation goes through the IEncyclopediaHandler EventBus (like the
    /// game's own clicks) so the on-screen visuals stay in sync. We keep our own history so Escape goes
    /// back from the page (from the tree — a jump-anywhere navigator — it closes). Creature pages render
    /// their unit-inspect bricks; class-progression / image blocks are noted but not yet rendered.
    /// </summary>
    public sealed class EncyclopediaScreen : Screen
    {
        public override string Key => "service.Encyclopedia";
        public override string ScreenName => Loc.T("screen.encyclopedia");
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Encyclopedia;

        private bool _navigated;      // an Enter/back happened: land on the page once its VM swaps
        private string _lastPage;     // the page identity last seen (detects the swap)
        private readonly Stack<IPage> _history = new Stack<IPage>();

        public override void OnPush() { _navigated = false; _lastPage = null; _history.Clear(); }
        public override void OnPop() { _history.Clear(); }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            // After a navigation, wait for the VM's page to actually swap, then land on the page top
            // (announced via the differ). Landing immediately would hit the OLD page's nodes.
            var page = vm.Page?.Value?.Page;
            var id = page != null ? PageLabel(page) : "";
            if (id != _lastPage)
            {
                _lastPage = id;
                if (_navigated) { _navigated = false; Navigation.FocusStop("page"); }
            }
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            // History-back is a page-view notion: only when focus is in the page and we've drilled via a
            // link. From the tree (a jump-anywhere navigator), or with nothing to return to, Escape closes.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                if (!Equals(Navigation.FocusedStopKey, "tree") && _history.Count > 0) Back();
                else ServiceWindows()?.HandleCloseAll();
            });
        }

        private static EncyclopediaVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.EncyclopediaVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;

        // An entry whose title resolves EMPTY at runtime renders as a blank, affordance-free row in
        // the game's own view — effectively invisible to sighted players (the shipped case: the
        // VoiceoverChapter chapter, Platforms=Common so it passes the game's PC filter, title never
        // localized). Mirroring the effective experience: we skip such entries entirely.
        internal static bool HasVisibleTitle(IPage page)
            => !string.IsNullOrWhiteSpace(page?.GetNavigationTitle());

        // Label fallback for anything that still slips through with no title (defensive — visible
        // entries all have real titles once the HasVisibleTitle filter is applied): the blueprint's
        // dev name, prettified ("Combat_Maneuvers" → "Combat Maneuvers").
        internal static string PageLabel(IPage page)
        {
            var t = page?.GetNavigationTitle();
            if (!string.IsNullOrWhiteSpace(t)) return TextUtil.StripRichText(t);
            if (page is Kingmaker.Blueprints.BlueprintScriptableObject bp && !string.IsNullOrEmpty(bp.name))
                return bp.name.Replace('_', ' ');
            return Loc.T("ency.untitled");
        }

        // Jumping via the tree resets history — it's a "go anywhere" navigator, not a drill-down, so there's
        // nothing to go "back" to afterward. Drilling via an in-page link pushes history so Escape can return.
        private void NavigateJump(IPage node) { _history.Clear(); Go(node); }
        private void NavigateDrill(IPage node)
        {
            var cur = Vm()?.Page?.Value?.Page;
            if (cur != null) _history.Push(cur);
            Go(node);
        }

        // Route through the EventBus like the game's SelectPage, not vm.HandleEncyclopediaPage directly — the
        // VM, the navigation tree (highlight/expand) and breadcrumbs all subscribe as IEncyclopediaHandler, so
        // this keeps the on-screen visuals in sync, not just our reading.
        private void Go(IPage node)
        {
            if (Vm() == null || node == null) return;
            _navigated = true;
            EventBus.RaiseEvent<IEncyclopediaHandler>(x => x.HandleEncyclopediaPage(node, scrollToCenter: false));
        }

        private void Back()
        {
            if (_history.Count == 0) return;
            _navigated = true;
            var prev = _history.Pop();
            EventBus.RaiseEvent<IEncyclopediaHandler>(x => x.HandleEncyclopediaPage(prev, scrollToCenter: false));
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;

            // The hierarchy tree: nested groups keyed by page label (stable), children materialized
            // only while their group is expanded (the child VMs are created lazily by the game).
            b.BeginStop("tree").PushContext(Loc.T("screen.encyclopedia"), role: null, positions: false);
            var chapters = vm.NavigationVM?.GetChapters();
            if (chapters != null)
                foreach (var ch in chapters)
                    if (ch != null && HasVisibleTitle(ch.Page)) EmitNavNode(b, ch, "ency:");
            b.PopContext();

            BuildPage(b, vm);
        }

        // One tree node: expandable (a group whose children build lazily on expand) or a leaf; Enter
        // loads its page either way. Labels avoid the raw VM title (empty for the titleless chapter).
        private void EmitNavNode(GraphBuilder b, EncyclopediaNavigationElementVM vm, string prefix)
        {
            var page = vm.Page;
            string key = prefix + PageLabel(page);
            var id = ControlId.Structural(key);
            var vt = new NodeVtable
            {
                ControlType = ControlTypes.Text, // no role word: label (+ expanded/collapsed on groups)
                Announcements = new[] { GraphNodes.LabelPart(() => PageLabel(page)) },
                SearchText = () => PageLabel(page),
                OnActivate = () =>
                {
                    UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                    NavigateJump(page);
                },
            };

            if (vm.IsCanCollapse) // has children (even before they're built)
            {
                b.BeginGroup(id, vt);
                if (b.IsExpanded(id)) // materialize child VMs only while open
                    foreach (var child in vm.GetOrCreateChildsVM())
                        if (child != null && HasVisibleTitle(child.Page))
                            EmitNavNode(b, child, key + "/");
                b.EndGroup();
            }
            else
            {
                b.AddItem(id, vt);
            }
        }

        // The current page: title, text, links to its child pages, and (creature pages) the unit-inspect
        // bricks. Keyed per page, so navigating re-keys it while the tree keeps expansion and position.
        private void BuildPage(GraphBuilder b, EncyclopediaVM vm)
        {
            b.BeginStop("page").PushContext(Loc.T("ency.page"), role: null, positions: false);
            var page = vm.Page?.Value;
            if (page == null)
            {
                b.AddItem(ControlId.Structural("ency:noselect"), GraphNodes.Text(() => Loc.T("ency.select_topic")));
                b.PopContext();
                return;
            }
            string pk = "ency:page:" + PageLabel(page.Page) + ":";

            var heading = !string.IsNullOrWhiteSpace(page.Title) ? page.Title : PageLabel(page.Page);
            b.AddItem(ControlId.Structural(pk + "title"), GraphNodes.Text(() => TextUtil.StripRichText(heading)));

            int bi = 0;
            foreach (var block in page.BlockVMs)
            {
                switch (block)
                {
                    case EncyclopediaPageBlockTextVM t when !string.IsNullOrWhiteSpace(t.Text):
                        var raw = t.Text;
                        b.AddItem(ControlId.Structural(pk + "block:" + bi), new NodeVtable
                        {
                            ControlType = ControlTypes.Text,
                            Announcements = new[] { new NodeAnnouncement(() => TextUtil.StripRichText(raw)) },
                            // Glossary links inside the text follow on Space.
                            OnTooltip = () => TooltipScreen.FollowLinks(raw, null),
                        });
                        break;
                    case EncyclopediaPageBlockClassProgressionVM _:
                        b.AddItem(ControlId.Structural(pk + "block:" + bi),
                            GraphNodes.Text(() => Loc.T("ency.progression_not_shown")));
                        break;
                    case EncyclopediaPageBlockUnitVM _:
                        b.AddItem(ControlId.Structural(pk + "block:" + bi),
                            GraphNodes.Text(() => Loc.T("ency.stats_not_shown")));
                        break;
                    // Child pages are listed below; images are skipped.
                }
                bi++;
            }

            var childs = page.Page?.GetChilds();
            if (childs != null && childs.Count > 0)
            {
                b.PushContext(Message.Localized("ui", "encyclopedia.topics").Resolve());
                int ti = 0;
                foreach (var child in childs)
                {
                    if (child == null || !HasVisibleTitle(child)) { ti++; continue; }
                    var c = child;
                    b.AddItem(ControlId.Structural(pk + "topic:" + ti),
                        GraphNodes.Button(() => PageLabel(c), () => NavigateDrill(c)));
                    ti++;
                }
                b.PopContext();
            }
            b.PopContext();

            // Creature pages (the Creatures group: inspected monsters) carry no blocks — the game's
            // view renders them through the tooltip-brick system instead (EncyclopediaPagePCView binds
            // a TooltipTemplateUnitInspect when IsBricks). Mirror that: the same template through our
            // brick renderer, as its own Tab-stop after the page. What it reveals follows the game's
            // inspect rules (more details as your knowledge checks succeed).
            if (page.Page is UnitInfoPage uip && uip.UnitInfo != null)
            {
                b.BeginStop("bricks");
                WrathAccess.UI.Tooltips.TooltipFlowBuilder.Emit(b, pk + "bricks:",
                    new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateUnitInspect(uip.UnitInfo.Blueprint),
                    includeEmptyNotice: false);
            }
        }
    }
}
