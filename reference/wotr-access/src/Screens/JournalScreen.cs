using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsType, ServiceWindowsVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Journal; // JournalVM, JournalQuestVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The journal service window (<see cref="JournalVM"/>), graph-native: the grouped quest list on top
    /// (one region per quest group — Ctrl+arrows jump groups; each quest a radio reading its state and
    /// "updated" attention flag; Enter selects) and the selected quest's detail below — title, description,
    /// objectives (with their state) and addendums, plus completion text for finished quests. Everything
    /// renders live; detail keys carry the selected quest, so selecting re-keys the detail only (quest-list
    /// focus stays put — the old signature/capture/restore machinery is deleted). Escape closes.
    /// </summary>
    public sealed class JournalScreen : Screen
    {
        public override string Key => "service.Journal";
        public override string ScreenName => Loc.T("screen.journal");
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Journal;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        private static JournalVM Jv()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.JournalVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;


        public override void Build(GraphBuilder b)
        {
            var jv = Jv();
            if (jv == null) return;
            string k = "journal:" + jv.GetHashCode() + ":";

            BuildQuestList(b, jv, k);
            BuildDetail(b, jv, k);
        }

        // The grouped quest list: one stop; a region + context level per quest group (Ctrl+arrows jump
        // between groups, entering one announces its title). Quests key by VM.
        private static void BuildQuestList(GraphBuilder b, JournalVM jv, string k)
        {
            b.BeginStop("quests").PushContext(Loc.T("journal.quests"), "list", positions: false);
            var groups = jv.Navigation?.NavigationGroups;
            bool any = false;
            int gi = 0;
            if (groups != null)
                foreach (var g in groups)
                {
                    if (g?.Quests == null || g.Quests.Count == 0) { gi++; continue; }
                    b.SetRegion(k + "group:" + gi);
                    b.PushContext(g.Title);
                    int qi = 0;
                    foreach (var q in g.Quests)
                    {
                        if (q == null) { qi++; continue; }
                        b.AddItem(ControlId.Referenced(q, k + "q:" + gi + ":" + qi), QuestNode(q));
                        any = true;
                        qi++;
                    }
                    b.PopContext();
                    gi++;
                }
            if (!any)
                b.AddItem(ControlId.Structural(k + "noquests"), GraphNodes.Text(() => Loc.T("journal.no_quests")));
            b.PopContext();
        }

        // One quest: a radio reading "selected" for the shown quest plus its state (active / completed /
        // failed, and "updated" when it needs attention); Enter selects it (the game's SelectQuest).
        private static NodeVtable QuestNode(JournalQuestVM q)
        {
            return new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => q.Title),
                    GraphNodes.SelectedPart(() => q.IsSelected.Value),
                    new NodeAnnouncement(() => QuestState(q), live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => q.Title,
                StateText = () => q.IsSelected.Value ? Loc.T("state.selected") : null,
                OnActivate = () => q.SelectQuest(),
            };
        }

        private static string QuestState(JournalQuestVM q)
        {
            var s = StateWord(q.IsCompleted, q.IsFailed);
            if (q.IsAttention) s += ", " + Loc.T("journal.updated");
            return s;
        }

        // The selected quest's detail: title + description (+ completion text), then its objectives and
        // their addendums, each with its state. Keys carry the quest, so a selection re-keys the detail.
        private static void BuildDetail(GraphBuilder b, JournalVM jv, string k)
        {
            var q = jv.Quest?.Value;
            b.BeginStop("detail");
            if (q == null)
            {
                b.AddItem(ControlId.Structural(k + "noselect"), GraphNodes.Text(() => Loc.T("journal.select_quest")));
                return;
            }
            string dk = k + "d:" + (jv.SelectedQuest?.Value?.Blueprint?.name ?? q.Title) + ":";

            b.PushContext(Loc.T("journal.quest"), role: null, positions: false);
            b.AddItem(ControlId.Structural(dk + "title"), GraphNodes.Text(() => q.Title));
            if (!string.IsNullOrWhiteSpace(q.Description))
                b.AddItem(ControlId.Structural(dk + "desc"), GraphNodes.Text(() => q.Description));
            if (q.IsCompleted && !string.IsNullOrWhiteSpace(q.CompletionText))
                b.AddItem(ControlId.Structural(dk + "completion"), GraphNodes.Text(() => q.CompletionText));

            if (q.Objectives != null && q.Objectives.Count > 0)
            {
                b.SetRegion(dk + "objectives");
                b.PushContext(Loc.T("journal.objectives"));
                int oi = 0;
                foreach (var o in q.Objectives)
                {
                    if (o == null) { oi++; continue; }
                    var ob = o;
                    b.AddItem(ControlId.Structural(dk + "obj:" + oi), GraphNodes.Text(
                        () => (string.IsNullOrWhiteSpace(ob.Description) ? ob.Title : ob.Description)
                            + " (" + StateWord(ob.IsCompleted, ob.IsFailed) + ")"));
                    if (o.Addendums != null)
                    {
                        int ai = 0;
                        foreach (var a in o.Addendums)
                        {
                            if (a == null) { ai++; continue; }
                            var ad = a;
                            b.AddItem(ControlId.Structural(dk + "obj:" + oi + ":add:" + ai), GraphNodes.Text(
                                () => ad.Description + " (" + StateWord(ad.IsCompleted, ad.IsFailed) + ")"));
                            ai++;
                        }
                    }
                    oi++;
                }
                b.PopContext();
            }
            b.PopContext();
        }

        private static string StateWord(bool completed, bool failed)
            => Loc.T(completed ? "journal.completed" : failed ? "journal.failed" : "journal.active");
    }
}
