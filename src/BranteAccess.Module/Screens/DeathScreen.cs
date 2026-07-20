using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using DeathWindow = _Scripts.AMVCC.Views.Windows.Death.DeathWindowBehaviour;
using DeathChoiceButton = _Scripts.AMVCC.Views.Windows.Death.DeathChoiceButtonBehaviour;
using SimplePageTurner = _Scripts.AMVCC.Views.Windows.Death.SimplePageTurner;
using UniversalPageTurner = _Scripts.AMVCC.Views.Windows.Death.UniversalPageTurner;
using ResolveBehaviour = _Scripts.AMVCC.Views.Windows.Death.ResolveBehaviour;
using TextBlock = _Scripts.AMVCC.Controllers.TextBlock;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The death window (a scene loaded on a death trigger), spoken with the transcript
    /// pattern like the event scene and interlude: delivered pages accumulate as rows, Enter
    /// on the newest turns the page through the game's own pager (bounds-guarded, sound), a
    /// new page re-homes silently with the delivery announcement as the speech. Past rows
    /// re-derive their text the way the game itself does when paging back (block key through
    /// its own InsertCharacterName), the newest row reads the live text (the fourth-death
    /// resolve rewrites it after Start). The setup pager ends in death-choice buttons (their
    /// descriptions are hover-only for sighted players - folded onto the button); a chosen
    /// consequence swaps in the resolve pager, delivered like any new content, ending in the
    /// game's Continue back to the story.
    /// </summary>
    public sealed class DeathScreen : Screen
    {
        public override string Key => "death";
        // The death prefab lands in the UIManager popup slot: a specific popup-slot surface
        // outranks the generic popup sweep (20), like the interlude (the slot holds one popup,
        // so the two never coexist).
        public override int Layer => 21;

        public override bool IsActive()
        {
            var w = Window();
            return w != null && w.gameObject.activeInHierarchy && !ForeignPopupOpen();
        }

        // A popup-slot occupant that is not the death window itself (the achievement popup
        // the fourth-death Continue opens) is modal OVER the death window - yield so the
        // generic popup screen wins; this screen re-pushes when the popup closes.
        private static bool ForeignPopupOpen()
        {
            var p = GameUi.OpenedPopup;
            return p != null && p.activeInHierarchy
                && p.GetComponentInChildren<DeathWindow>() == null;
        }

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                return w == null ? null : Message.MaybeRaw(Title(w).text);
            }
        }

        private static readonly FieldInfo TitleField = typeof(DeathWindow)
            .GetField("_titleTmp", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PageIndexField = typeof(UniversalPageTurner)
            .GetField("_pageIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo MainTextField = typeof(SimplePageTurner)
            .GetField("_mainText_TMP", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TextBlockField = typeof(SimplePageTurner)
            .GetField("_textBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo InsertNameMethod = typeof(SimplePageTurner)
            .GetMethod("InsertCharacterName", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ResolveContinueField = typeof(ResolveBehaviour)
            .GetField("_continueButton", BindingFlags.NonPublic | BindingFlags.Instance);

        // Live component references for delivery bookkeeping only; text re-reads at speech time.
        private SimplePageTurner _watched;
        private int _spokenPage;
        private string _spokenText;
        private string _spokenTitle;
        private string _pendingText;
        private float _pendingSince;
        private bool _pendingSeat;
        // A newly activated pager shows its prefab placeholder (raw Russian) for a few frames
        // before the game populates it (heard live in the fourth-death resolve, speech 470) -
        // deliveries for swapped/rewritten text wait until the text holds still. Page turns
        // set their text synchronously and stay immediate.
        private const float TextSettleSeconds = 0.45f;

        private static DeathWindow Window() => Object.FindObjectOfType<DeathWindow>();

        private static TMPro.TextMeshProUGUI Title(DeathWindow w)
            => (TMPro.TextMeshProUGUI)TitleField.GetValue(w);

        // The active pager. The window ROOT carries its own SimplePageTurner which stays
        // active for the window's whole life; a resolve pager activates as a CHILD alongside
        // it (seen live in the true-death judgment: the root pager sat exhausted on the
        // question while Resolve1 displayed), so an active child pager wins over the root.
        private static SimplePageTurner Turner()
        {
            SimplePageTurner root = null;
            foreach (var t in Object.FindObjectsOfType<SimplePageTurner>())
            {
                if (!t.gameObject.activeInHierarchy) continue;
                if (t.GetComponent<DeathWindow>() != null) { root = t; continue; }
                return t;
            }
            return root;
        }

        private static int PageIndex(SimplePageTurner t) => (int)PageIndexField.GetValue(t);

        private static string PageText(SimplePageTurner t)
            => ((TMPro.TextMeshProUGUI)MainTextField.GetValue(t)).text;

        // A past page's text the way the game itself re-renders it on a backward turn: the
        // block key through the pager's own name-substitution helper - a model read, not a
        // cache (the same call rebuilds the same words every time).
        private static string DerivedText(SimplePageTurner t, int page)
        {
            var blocks = (System.Collections.Generic.List<TextBlock>)TextBlockField.GetValue(t);
            return (string)InsertNameMethod.Invoke(t, new object[] { blocks[page].SpeechByKey });
        }

        // Structural and qualified by pager instance: the fourth-death trial reloads its
        // scene per judgment question synchronously (no pop frame) - a bare page index would
        // reconcile onto the new pager's identical key and swallow the new content's re-seat.
        private static ControlId PageId(SimplePageTurner t, int page) =>
            ControlId.Structural("death:" + t.GetInstanceID() + ":page:" + page);

        public override void OnPop()
        {
            _watched = null;
            _spokenTitle = null;
            _pendingText = null;
            _pendingSeat = false;
        }

        public override void OnUpdate()
        {
            var w = Window();
            if (w == null) return;
            // The game populates the window (title, pager text) a beat after showing the
            // prefab, so entry announcements catch placeholders: rewrites re-deliver so the
            // player hears the real content.
            string title = Title(w).text;
            if (_spokenTitle != null && title != _spokenTitle) Mod.Speech.Speak(title);
            _spokenTitle = title;
            var t = Turner();
            if (t == null) return;
            int page = PageIndex(t);
            string text = PageText(t);
            if (t != _watched)
            {
                // Real null means OnPop cleared it (screen entry: the navigator's seat
                // announcement reads the page). A DESTROYED pager means the death scene
                // swapped under an alive screen (the fourth-death trial reloads its scene
                // per judgment question, synchronously - no pop frame), which Unity's
                // overloaded == also reports as null: that is new content to deliver.
                bool entry = ReferenceEquals(_watched, null);
                _watched = t;
                _spokenPage = page;
                _pendingText = null;
                // Screen entry: the navigator's seat announcement reads the page. A mid-screen
                // swap (setup to resolve) is new content - delivered below once it settles,
                // with a silent re-seat onto its newest row.
                _spokenText = entry ? text : null;
                _pendingSeat = !entry;
                return;
            }
            if (page != _spokenPage)
            {
                _spokenPage = page;
                _spokenText = text;
                _pendingText = null;
                // The new page's row joined the transcript this frame: re-home silently, the
                // delivery announcement is the speech.
                Navigation.FocusNode(PageId(t, page), announce: false);
                Mod.Speech.Speak(text);
                return;
            }
            if (text == _spokenText) { _pendingText = null; return; }
            if (text != _pendingText)
            {
                _pendingText = text;
                _pendingSince = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - _pendingSince < TextSettleSeconds) return;
            _pendingText = null;
            _spokenText = text;
            if (_pendingSeat)
            {
                _pendingSeat = false;
                Navigation.FocusNode(PageId(t, page), announce: false);
            }
            Mod.Speech.Speak(text);
        }

        public override void Build(GraphBuilder b)
        {
            var t = Turner();
            if (t == null) return;

            int pages = PageIndex(t);
            b.PushContext("", role: null, positions: false);
            for (int i = 0; i <= pages; i++)
            {
                int page = i;
                bool newest = page == pages;
                b.AddItem(PageId(t, page), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    SilentRecovery = true,
                    Announcements = new[]
                    {
                        // The newest page reads the live text (the game rewrites it in the
                        // fourth-death resolve); settled rows re-derive from their block.
                        new NodeAnnouncement(
                            () => page == PageIndex(t) ? PageText(t) : DerivedText(t, page),
                            kind: AnnouncementKinds.Label),
                    },
                    // Enter turns forward through the game's own pager (bounds-guarded,
                    // sound); on the final page it is the guard's no-op - what follows
                    // (choices, Continue) sits below as rows.
                    OnActivate = !newest ? (System.Action)null : () => t.RightButton_Click(),
                });
            }
            b.SetStart(PageId(t, pages));
            b.PopContext();

            // Death choices (revealed by the game on the setup pager's last page). The
            // description only exists on hover for sighted players - folded onto the button.
            b.PushContext("", role: null);
            // FindObjectsOfType returns reverse-instantiation order, and the buttons span the
            // book's two page columns - the reading order is left column top-to-bottom, then
            // right (matches the game's own 1..6 numbering, verified live).
            var choices = new System.Collections.Generic.List<DeathChoiceButton>();
            foreach (var dc in Object.FindObjectsOfType<DeathChoiceButton>())
                if (UiWidgets.Visible(dc.gameObject)) choices.Add(dc);
            choices.Sort((a, c) =>
            {
                Vector3 pa = a.transform.position, pc = c.transform.position;
                if (Mathf.Abs(pa.x - pc.x) > 1f) return pa.x.CompareTo(pc.x);
                return pc.y.CompareTo(pa.y);
            });
            foreach (var dc in choices)
            {
                var choice = dc;
                b.AddItem(ControlId.Referenced(choice, "death:choice:" + choice.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => ChoiceText(choice),
                                kind: AnnouncementKinds.Label),
                        },
                        SearchText = () => ChoiceText(choice),
                        // Through the game's own Button: standard death windows wire
                        // OnButton_Click (select + show resolve) but the fourth-death
                        // trial wires SceneButton_Click (advance to the next trial
                        // scene) on the same prefab family - the wiring is the truth.
                        OnActivate = () => UiWidgets.Click(choice.gameObject),
                    });
            }
            b.PopContext();

            // The resolve pager's Continue (revealed on its last page) - back to the story.
            var resolve = Object.FindObjectOfType<ResolveBehaviour>();
            if (resolve != null)
            {
                var cont = (GameObject)ResolveContinueField.GetValue(resolve);
                if (cont != null && cont.activeInHierarchy)
                    b.AddItem(ControlId.Referenced(
                        cont.GetComponent<UnityEngine.UI.Button>(), "death:continue"),
                        new NodeVtable
                        {
                            ControlType = ControlTypes.Button,
                            Announcements = new[]
                            {
                                new NodeAnnouncement(() => UiWidgets.LabelText(cont),
                                    kind: AnnouncementKinds.Label),
                            },
                            OnActivate = () => UiWidgets.Click(cont),
                        });
            }
        }

        private static string ChoiceText(DeathChoiceButton choice)
        {
            var label = choice.GetComponentInChildren<TMPro.TextMeshProUGUI>().text;
            if (string.IsNullOrEmpty(choice.ButtonDesriptionKey)) return label;
            var desc = I2.Loc.LocalizationManager.GetTranslation(choice.ButtonDesriptionKey);
            return string.IsNullOrEmpty(desc) ? label : label + ". " + desc;
        }
    }
}
