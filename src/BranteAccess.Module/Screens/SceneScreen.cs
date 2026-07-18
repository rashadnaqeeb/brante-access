using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using TextController = _Scripts.AMVCC.Controllers.TextController;
using SceneController = _Scripts.AMVCC.Controllers.SceneController;
using ParameterButtonChanger = _Scripts.AMVCC.Views.ParameterButtonChanger;
using SceneConsequenceGenerator = _Scripts.AMVCC.Views.Windows.SceneConsequenceGenerator;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The event scene - the passage pager as a TRANSCRIPT (house pattern from wotr-access's
    /// DialogueScreen): every delivered page is a text row in one silent-positions stop, arrows
    /// re-read earlier pages without touching game state (better than the game's own re-paging),
    /// Enter on the newest row advances through the game's NextPage (sound, animation, death
    /// triggers all run). A new page re-homes focus SILENTLY; the queued delivery announcement is
    /// the speech, spoken once per page off model state (the pager's own page index), no matter
    /// what advanced it. Escape opens the game's pause window - the game's own Escape read lives
    /// in UIManager.Update, which focus mode suppresses while a screen is active. Rows read the
    /// pager's serialized I2 keys through the game's own substitution helper at speech time
    /// (hero name, el-honorific), with the block's character name prefixed where the game shows
    /// a portrait.
    /// </summary>
    public sealed class SceneScreen : Screen
    {
        public override string Key => "scene";
        public override int Layer => 0;

        // The cutscene surface owns its keys (CutsceneScreen claims no input so the game's own
        // skip works); this screen must not sit under it claiming UI keys.
        public override bool IsActive()
            => GameUi.State == GameState.RUNNING
            && Object.FindObjectOfType<SceneController>() != null
            && Object.FindObjectOfType<CutsceneIntro>() == null
            && Object.FindObjectOfType<ChapterCutscene>() == null;

        public override Message ScreenName
        {
            get
            {
                var sc = Object.FindObjectOfType<SceneController>();
                return sc == null ? null
                    : Message.MaybeRaw(sc.Title.GetComponent<TMPro.TextMeshProUGUI>().text);
            }
        }

        private static readonly FieldInfo PageIndexField = typeof(TextController)
            .GetField("_pageIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo InsertNameMethod = typeof(TextController)
            .GetMethod("InsertCharacterName", BindingFlags.NonPublic | BindingFlags.Instance);

        // Live component references for delivery bookkeeping only; all text re-reads at speech time.
        private TextController _watched;
        private int _spokenPage;
        private bool _entryPending;
        private string _spokenStatsSig;

        // The consequence pager only activates after a choice resolves - while it is active it IS
        // the story surface (the game hides the setup pager), so it wins.
        private static TextController Pager()
        {
            TextController main = null;
            foreach (var tc in Object.FindObjectsOfType<TextController>())
            {
                if (tc.ItsConsequence) return tc;
                main = main ?? tc;
            }
            return main;
        }

        private static int PageIndex(TextController tc) => (int)PageIndexField.GetValue(tc);

        // Structural, not Referenced: every row would share the pager component as its reference,
        // and reference-tier focus reconciliation would snap focus back to the first row sharing
        // it on every rebuild (seen live: End bounced straight back to page 0).
        private static ControlId PageId(TextController tc, int page)
            => ControlId.Structural("scene:page:" + page);

        // The game's substitution helper resolves the page key (hero name, el-honorific); the
        // block's character name is prefixed exactly where the game shows the portrait + name.
        private static string PageText(TextController tc, int page)
        {
            var block = tc.TextBlock[page];
            var text = (string)InsertNameMethod.Invoke(tc, new object[] { block.SpeechByKey });
            if (block.Character != null)
                text = _Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate
                    .GetCharacterTrueName(block.Character.Name) + ": " + text;
            return text;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null,
                _ => _Scripts.Managers.UIManager.Initiate.ShowPauseMenu());
        }

        public override void OnPush()
        {
            _entryPending = true;
        }

        public override void OnPop()
        {
            _watched = null;
            _spokenStatsSig = null;
        }

        public override void OnUpdate()
        {
            DeliverStatPanels();
            var tc = Pager();
            if (tc == null) return;
            int page = PageIndex(tc);

            // A pager swap. On screen entry the navigator's own focus-seat announcement reads the
            // current row - delivery must not repeat it. Mid-screen (choice resolved into the
            // consequence pager, or a new scene loaded) there is no seat announcement: the swap
            // IS new content, delivered like any new page.
            if (tc != _watched)
            {
                bool entry = _entryPending;
                _entryPending = false;
                _watched = tc;
                _spokenPage = page;
                if (!entry)
                {
                    Navigation.FocusNode(PageId(tc, page), announce: false);
                    Mod.Speech.Speak(PageText(tc, page));
                }
                return;
            }
            if (page == _spokenPage) return;

            // New page delivered: silent re-home, the queued delivery announcement is the speech.
            _spokenPage = page;
            Navigation.FocusNode(PageId(tc, page), announce: false);
            Mod.Speech.Speak(PageText(tc, page));
        }

        // The post-choice stat panels (SceneConsequenceGenerator: relation/status panels per
        // character, then parameter rows by category, chained by the game's own Continue). Each
        // panel's rendered rows ARE the game's localized composition ("+1 (Become 3)", segment
        // names) - a new or swapped panel is delivered whole, once, off the rendered content.
        // Focus needs no re-seat: it sits on the Continue button, which survives the rebuild.
        private void DeliverStatPanels()
        {
            var gen = Object.FindObjectOfType<SceneConsequenceGenerator>();
            if (gen == null) { _spokenStatsSig = null; return; }
            var sig = PanelSweep.JoinVisible(gen.gameObject);
            if (sig.Length == 0) { _spokenStatsSig = null; return; }
            if (sig == _spokenStatsSig) return;
            _spokenStatsSig = sig;
            Mod.Speech.Speak(sig);
        }

        public override void Build(GraphBuilder b)
        {
            var tc = Pager();
            if (tc == null) return;
            int pages = PageIndex(tc);

            b.PushContext("", role: null, positions: false);
            for (int i = 0; i <= pages; i++)
            {
                int page = i;
                bool newest = page == pages;
                b.AddItem(PageId(tc, page), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => PageText(tc, page), kind: AnnouncementKinds.Label),
                    },
                    // Enter on the newest page turns to the next through the game's own pager,
                    // gated on the game's own next-button state (page-turn animations and death
                    // windows disable it - Enter must not advance through a blocked panel).
                    OnActivate = !newest ? (System.Action)null : () =>
                    {
                        if (tc.NextButton.GetComponent<UnityEngine.UI.Button>().interactable)
                            tc.NextPage();
                    },
                });
            }
            b.SetStart(PageId(tc, pages));
            b.PopContext();

            // The post-choice stat panels: rows + the chaining button as nodes, so the player can
            // re-read what the delivery spoke. Swept before the Continue node - if the game's
            // Continue lives inside the generator, the sweep's button node covers it.
            var gen = Object.FindObjectOfType<SceneConsequenceGenerator>();
            if (gen != null) PanelSweep.Build(b, gen.gameObject, "scene:stats");

            // The consequence's Continue button (the game shows its control panel after the last
            // consequence page) - the way onward to the next scene.
            if (tc.ItsConsequence && tc.ConsequenceControlPanel != null
                && tc.ConsequenceControlPanel.activeInHierarchy)
            {
                var cont = tc.ConsequenceControlPanel.GetComponentInChildren<UnityEngine.UI.Button>();
                if (cont != null && (gen == null || !cont.transform.IsChildOf(gen.transform)))
                    b.AddItem(ControlId.Referenced(cont, "scene:continue"), new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => UiWidgets.LabelText(cont.gameObject),
                                kind: AnnouncementKinds.Label),
                        },
                        OnActivate = () => UiWidgets.Click(cont.gameObject),
                    });
            }

            // Choices follow the transcript in the same flow, positioned ("1 of 4") - they are a
            // list, unlike the transcript lines. Only the buttons the game is showing exist here
            // (the panel activates on the last setup page and hides during the consequence).
            b.PushContext("", role: null);
            foreach (var pbc in Choices())
            {
                var choice = pbc;
                b.AddItem(ControlId.Referenced(choice, "scene:choice:" + choice.ButtonIndex),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => ChoiceText(choice),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(
                                () => choice.IsButtonInteractable ? null : UnavailableReason(choice),
                                kind: AnnouncementKinds.Enabled),
                        },
                        SearchText = () => ChoiceText(choice),
                        OnActivate = () =>
                        {
                            if (!choice.IsButtonInteractable)
                            {
                                Mod.Speech.Speak(UnavailableReason(choice), interrupt: true);
                                return;
                            }
                            // The game's own resolving gate: a clicked choice sets it until the
                            // consequence flow takes over - Enter must not choose twice.
                            if (Object.FindObjectOfType<SceneController>().GetButtonClickedState())
                                return;
                            UiWidgets.Click(choice.GetComponentInChildren<UnityEngine.UI.Button>()
                                .gameObject);
                        },
                    });
            }
            b.PopContext();
        }

        private static List<ParameterButtonChanger> Choices()
        {
            var list = new List<ParameterButtonChanger>(
                Object.FindObjectsOfType<ParameterButtonChanger>());
            list.Sort((a, c) => a.ButtonIndex.CompareTo(c.ButtonIndex));
            return list;
        }

        // The choice line with the game's post-click elaboration folded on (house rule: detail
        // rides the item itself; it is also what makes choices type-ahead searchable by meaning).
        private static string ChoiceText(ParameterButtonChanger choice)
        {
            var label = choice.ButtonText.text;
            var desc = I2.Loc.LocalizationManager.GetTranslation(
                GameUi.SceneName + "_" + choice.gameObject.name);
            return string.IsNullOrEmpty(desc) ? label : label + ". " + desc;
        }

        // Composed from the per-check data the game serialized on the button, using the game's
        // own localized names; only the operations are mod words (the game's terms are bare
        // symbols - readers voice those unreliably). An OR-tree (DifficultConditions) failure
        // with no failed simple check falls back to the plain unavailable word.
        private static string UnavailableReason(ParameterButtonChanger c)
        {
            var reqs = new List<string>();
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var names = _Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate;

            for (int i = 0; i < c.Condition.Count && i < c.Checks.Count; i++)
                if (!c.Checks[i])
                    reqs.Add(Loc.T("choice.req.param", new
                    {
                        name = I2.Loc.LocalizationManager.GetTranslation(
                            c.Condition[i].ParamName.ParameterName.ToString()),
                        op = OpWord(c.Condition[i].Operations.ToString()),
                        value = c.Condition[i].SecondValue,
                    }));

            for (int i = 0; i < c.ConditionByRelations.Length && i < c.ChecksByRelation.Count; i++)
                if (!c.ChecksByRelation[i])
                    reqs.Add(Loc.T("choice.req.relation", new
                    {
                        name = names.GetCharacterTrueName(c.ConditionByRelations[i].Character.Name),
                        op = OpWord(c.ConditionByRelations[i].Operation.ToString()),
                        value = pm.CheckParameterValue(c.ConditionByRelations[i].Value),
                    }));

            for (int i = 0; i < c.ConditionsByStatus.Length && i < c.ChecksByStatus.Count; i++)
                if (!c.ChecksByStatus[i])
                {
                    var cond = c.ConditionsByStatus[i];
                    var req = Loc.T("choice.req.status", new
                    {
                        name = names.GetCharacterTrueName(cond.Character.Name),
                        status = I2.Loc.LocalizationManager.GetTranslation(
                            System.Enum.GetName(typeof(CharacterStatus), cond.Status)),
                    });
                    reqs.Add(cond.Not ? Loc.T("choice.req.not", new { req }) : req);
                }

            for (int i = 0; i < c.ConditionByObjectives.Length && i < c.ChecksByObjective.Count; i++)
                if (!c.ChecksByObjective[i])
                {
                    var cond = c.ConditionByObjectives[i];
                    var req = I2.Loc.LocalizationManager.GetTranslation(
                        cond.Objective.Name.ToString());
                    reqs.Add(cond.Not ? Loc.T("choice.req.not", new { req }) : req);
                }

            if (reqs.Count == 0) return Loc.T("state.unavailable");
            return Loc.T("choice.unavailable", new { req = string.Join(", ", reqs.ToArray()) });
        }

        private static string OpWord(string operation)
            => Loc.T("choice.op." + operation.ToLower());
    }
}
