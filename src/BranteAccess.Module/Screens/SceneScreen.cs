using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using TextController = _Scripts.AMVCC.Controllers.TextController;
using SceneController = _Scripts.AMVCC.Controllers.SceneController;

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

        // Live component reference for delivery bookkeeping only; all text re-reads at speech time.
        private TextController _watched;
        private int _spokenPage;

        private static TextController Pager()
        {
            foreach (var tc in Object.FindObjectsOfType<TextController>())
                if (!tc.ItsConsequence) return tc;
            return null;
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

        public override void OnPop()
        {
            _watched = null;
        }

        public override void OnUpdate()
        {
            var tc = Pager();
            if (tc == null) return;
            int page = PageIndex(tc);

            // First sight of a pager (screen entry or a new scene): the navigator's own focus-seat
            // announcement reads the current row - delivery must not repeat it.
            if (tc != _watched)
            {
                _watched = tc;
                _spokenPage = page;
                return;
            }
            if (page == _spokenPage) return;

            // New page delivered: silent re-home, the queued delivery announcement is the speech.
            _spokenPage = page;
            Navigation.FocusNode(PageId(tc, page), announce: false);
            Mod.Speech.Speak(PageText(tc, page));
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
        }
    }
}
