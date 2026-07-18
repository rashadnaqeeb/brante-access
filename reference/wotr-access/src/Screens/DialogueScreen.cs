using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UINotificationTexts (game-localized notification formats)
using Kingmaker.GameModes;
using Kingmaker.Settings;                // SettingsRoot (the game's per-category notification toggles)
using Kingmaker.UI.Common;               // UIUtility.SkillCheckText / alignment texts
using Kingmaker.UI.MVVM._VM.Dialog.Dialog;
using UniRx;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// An in-game conversation (the common <see cref="DialogVM"/>) as ONE graph stop that reads like a
    /// transcript: the scrollback (the game's own pre-formatted <c>DialogVM.History</c> lines — past
    /// cues and chosen answers — plus NOTIFICATION rows we inject: alignment shifts, items gained or
    /// lost, XP, revealed locations, mirroring DialogNotificationsView's formats and settings gates),
    /// then the CURRENT cue row, then the answers. A new cue re-keys the cue/answer nodes and focus is
    /// pointed at the cue SILENTLY — you hear the line via the delivery announcement, press Down for the
    /// answers/continue, or Up to re-read earlier lines. No Tab hop (user spec: dialogue should flow).
    ///
    /// We speak a line only once it's actually delivered on screen, driven by model state — so it fires
    /// whether the cue was advanced by our nav, a mouse click, or an auto-continue. The catch: the game
    /// sets <c>Cue.Value</c> seconds before delivery for cutscene-gated lines (it swaps the cue while the
    /// previous line is still shown, runs an intro cutscene, then delivers the line — voiceover and all —
    /// when control returns to Dialog mode). It marks those with <c>DialogVM.m_CutsceneScheduled</c> (the
    /// same flag it uses to defer the voiceover) and clears it in <c>OnGameModeStart(Dialog)</c> at
    /// delivery. So: announce when the cue is new, we're in Dialog mode, AND the cue isn't cutscene-
    /// scheduled. Notification lines arrive with the cue-show event (the VM clears its lists right after
    /// the command, so the subscriber snapshots synchronously) and are QUEUED ahead of the cue line as
    /// separate utterances — nothing in dialogue ever interrupts speech (user spec). Book events,
    /// interchapters, and kingdom/crusade notification categories are not handled here yet.
    /// </summary>
    public sealed class DialogueScreen : Screen
    {
        public override string Key => "ctx.dialogue";
        public override int Layer => 15; // over the in-game context + service windows

        // Popping here usually means HIDDEN (cutscene gap, pause menu over the window), not closed —
        // keep the nav state so resuming restores the transcript position (OnPop re-points at the cue).
        public override bool KeepStateOnPop => true;

        private static readonly FieldInfo CutsceneScheduledField = AccessTools.Field(typeof(DialogVM), "m_CutsceneScheduled");

        private DialogVM _subscribedVm;   // the conversation our notification subscription belongs to
        private IDisposable _notifSub;
        private CueVM _focusedCue;        // cue whose node focus was pointed at
        private CueVM _spokenCue;         // cue we've spoken

        private readonly List<string> _rows = new List<string>();         // the transcript: history + notifications
        private readonly List<string> _pendingNotifRows = new List<string>(); // notif lines awaiting ordered insertion
        private readonly List<string> _pendingSpeak = new List<string>();     // notif lines to speak before the next cue
        private int _historyConsumed;

        private static DialogVM Vm()
            // In-area OR world-map context (the global map carries its own DialogContextVM) — see DialogTranscript.Context.
            => DialogTranscript.Context()?.DialogVM?.Value;

        // True while this cue's delivery is gated behind a cutscene (its voiceover/text appear only when
        // Dialog mode resumes). If the field can't be read, treat as not-scheduled (announce on Dialog mode).
        private static bool CutsceneScheduled(DialogVM vm)
            => CutsceneScheduledField != null && CutsceneScheduledField.GetValue(vm) is bool b && b;

        private static bool DialogMode()
        {
            var g = Game.Instance;
            return g != null && g.CurrentMode == GameModeType.Dialog;
        }

        // Active only while the conversation exists AND its window is actually shown. When a cutscene
        // transition hides the window we POP off the stack so the in-game context beneath regains the
        // keyboard (Escape, sonar, etc. keep working) and there's no hidden dialogue to browse ahead in;
        // we re-push when the window returns.
        public override bool IsActive() => Vm() != null && DialogVisibility.Shown;

        // A hide is a pop-while-the-conversation-continues: keep the transcript + notification subscription
        // (so nothing is lost across the cutscene). Only fully reset when the conversation actually ended.
        // Popping DROPS the per-screen graph state (ScreenClosed), so clear the focus marker too: the next
        // OnUpdate re-points focus at the current line — a re-push (Esc menu closed, cutscene gap over)
        // lands on the most recent line, not the top of the transcript.
        public override void OnPop()
        {
            _focusedCue = null;
            if (Vm() == null) Reset();
        }

        // Escape opens the game's pause menu, exactly like the game's own Esc key during a conversation —
        // required for save/load/quit/settings mid-dialogue. Without this the dialogue screen swallows
        // Escape (its UI category claims ui.back) and nothing happens; the InGame Escape only kicks in
        // during the cutscene gaps when this screen pops. EscMenuScreen takes over while it's open.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => Kingmaker.PubSubSystem.EventBus.RaiseEvent(
                    delegate(Kingmaker.PubSubSystem.IEscMenuHandler h) { h.HandleOpen(); }));
        }

        private void Reset()
        {
            _focusedCue = null;
            _spokenCue = null;
            _rows.Clear();
            _pendingNotifRows.Clear();
            _pendingSpeak.Clear();
            _historyConsumed = 0;
            _notifSub?.Dispose();
            _notifSub = null;
            _subscribedVm = null;
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;

            // A new conversation = a fresh VM: reset the transcript and re-subscribe to its notifications.
            if (vm != _subscribedVm)
            {
                Reset();
                _subscribedVm = vm;
                // The VM CLEARS its notification lists right after firing the command, so this snapshot
                // must happen synchronously inside the subscription.
                _notifSub = vm.DialogNotifications.OnUpdateCommand.Subscribe(show =>
                {
                    if (!show) return;
                    var lines = ComposeNotifications(vm.DialogNotifications);
                    _pendingNotifRows.AddRange(lines);
                    _pendingSpeak.AddRange(lines);
                });
            }

            // Transcript order: history first (the previous cue + chosen answer are appended by the game
            // at answer-selection, BEFORE the cue-show event raised the notifications), then the
            // notification lines those events produced.
            var history = vm.History;
            for (; _historyConsumed < history.Count; _historyConsumed++)
                _rows.Add(TextUtil.StripRichText(history[_historyConsumed]));
            if (_pendingNotifRows.Count > 0)
            {
                _rows.AddRange(_pendingNotifRows);
                _pendingNotifRows.Clear();
            }

            var cue = vm.Cue.Value;
            if (cue == null) return;

            // A new cue: point focus at its (re-keyed) node SILENTLY — the delivery announcement below is
            // the speech; Down reaches the answers, Up scrolls back through the conversation so far.
            if (cue != _focusedCue)
            {
                _focusedCue = cue;
                Navigation.FocusNode(CueId(vm), announce: false);
            }

            // Speak once delivered: in Dialog mode and not waiting on a cutscene. Once per cue, QUEUED —
            // never interrupting (user spec) — the notification lines first (the results of the previous
            // answer), then the new line, each its own utterance.
            if (cue != _spokenCue && DialogMode() && !CutsceneScheduled(vm))
            {
                _spokenCue = cue;
                foreach (var line in _pendingSpeak) Tts.Speak(line, interrupt: false);
                _pendingSpeak.Clear();
                Tts.Speak(CueLine(vm), interrupt: false);
            }
        }


        private static ControlId CueId(DialogVM vm)
            => ControlId.Structural("dlg:" + vm.GetHashCode() + ":cue:"
                + (vm.Cue.Value != null ? vm.Cue.Value.GetHashCode().ToString() : "?"));

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "dlg:" + vm.GetHashCode() + ":";

            // One stop, no positions (a transcript, not a list — "37 of 40" per line is noise; the old
            // FlowSheet announced none). The empty-label context is a silent positions-off scope.
            b.PushContext("", role: null, positions: false);

            // The scrollback: absolute-index keys, stable as lines append.
            for (int i = 0; i < _rows.Count; i++)
            {
                var text = _rows[i];
                b.AddItem(ControlId.Structural(k + "row:" + i), GraphNodes.Text(() => text));
            }

            // The live line — focusing repeats it; Enter presses Continue when that's the only way
            // forward (never when real choices exist); Space resolves the skill-check result link (the
            // roll breakdown) with glossary links falling through. Keyed per cue (a new cue re-homes).
            var cue = vm.Cue.Value;
            if (cue != null)
            {
                bool hasRealAnswers = vm.Answers.Value != null && vm.Answers.Value.Count > 0;
                b.AddItem(CueId(vm), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[] { new NodeAnnouncement(() => CueLine(vm)) },
                    // Enter = the system Continue, only while the window is actually shown (during a
                    // cutscene transition the game hides it and the real button isn't clickable — Enter
                    // must not spam-advance). The game plays its own NextDialogLine sound.
                    OnActivate = () =>
                    {
                        if (hasRealAnswers) return;
                        var a = vm.SystemAnswer.Value;
                        if (a != null && a.Enable.Value && DialogVisibility.Shown) a.OnChooseAnswer();
                    },
                    OnTooltip = () => TooltipScreen.FollowLinks(CueLineRaw(vm),
                        (id, keys) => WrathAccess.UI.Proxies.DialogLinks.ResolveSkillCheck(
                            keys, vm.Cue.Value?.SkillChecks, null)),
                });

                // Real answers, else the system Continue when that's the only way forward.
                List<AnswerVM> answers = null;
                if (hasRealAnswers) answers = new List<AnswerVM>(vm.Answers.Value);
                else if (vm.SystemAnswer.Value != null) answers = new List<AnswerVM> { vm.SystemAnswer.Value };
                if (answers != null)
                {
                    int ai = 0;
                    foreach (var a in answers)
                    {
                        if (a != null)
                            b.AddItem(ControlId.Referenced(a, k + "ans:" + ai), DialogTranscript.AnswerNode(a));
                        ai++;
                    }
                }
            }
            b.PopContext();
        }

        // Mirrors DialogNotificationsView: the same per-category game settings gates and the same
        // game-localized UINotificationTexts formats; colors stripped for speech. Kingdom/crusade
        // categories (stats, events, free buildings, morale) are deferred with the kingdom screens.
        private static List<string> ComposeNotifications(DialogNotificationsVM n)
        {
            var lines = new List<string>();
            var t = UINotificationTexts.Instance;

            if (SettingsRoot.Game.Dialogs.ShowAlignmentShiftsNotifications)
            {
                foreach (var shift in n.AlignmentShifts)
                    lines.Add(TextUtil.StripRichText(string.Format(t.AlignmentShiftedFormat, "#FFFFFF",
                        UIUtility.GetAlignmentShiftDirectionText(shift.Direction), shift.Value)));
                var cueData = n.CueData;
                if (cueData != null && cueData.NewAlignment.HasValue)
                    lines.Add(TextUtil.StripRichText(string.Format(t.NewAlignmentAfterShiftedFormat, "#FFFFFF",
                        UIUtility.GetAlignmentName(cueData.NewAlignment.Value))));
            }

            if (SettingsRoot.Game.Dialogs.ShowLocationRevealedNotification && n.RevealedLocationNames.Count > 0)
                lines.Add(TextUtil.StripRichText(string.Format(
                    n.RevealedLocationNames.Count < 2 ? t.RevealedLocationFormat : t.RevealedLocationsFormat,
                    string.Join(", ", n.RevealedLocationNames.ToArray()))));

            if (SettingsRoot.Game.Dialogs.ShowItemsReceivedNotification)
            {
                var got = new List<string>();
                var lost = new List<string>();
                foreach (var kv in n.ItemsChanged)
                {
                    if (kv.Key == null || kv.Value == 0) continue;
                    int abs = Math.Abs(kv.Value);
                    var label = abs > 1 ? kv.Key.Name + " ×" + abs : kv.Key.Name;
                    (kv.Value > 0 ? got : lost).Add(label);
                }
                if (got.Count > 0)
                    lines.Add(TextUtil.StripRichText(string.Format(t.ItemsRecievedFormat, string.Join(", ", got.ToArray()))));
                if (lost.Count > 0)
                    lines.Add(TextUtil.StripRichText(string.Format(t.ItemsLostFormat, string.Join(", ", lost.ToArray()))));
            }

            if (SettingsRoot.Game.Dialogs.ShowXPGainedNotification && n.XpGains.Count > 0)
            {
                int sum = 0;
                foreach (var x in n.XpGains) sum += x;
                lines.Add(TextUtil.StripRichText(string.Format(t.XPGainedFormat, sum)));
            }

            foreach (var c in n.CustomNotifications)
                if (!string.IsNullOrEmpty(c)) lines.Add(TextUtil.StripRichText(c));

            return lines;
        }

        private static string CueLine(DialogVM vm) => TextUtil.StripRichText(CueLineRaw(vm));

        // Markup-intact (for the link extractor); Tts/announcements strip at speak time anyway.
        private static string CueLineRaw(DialogVM vm)
        {
            var cue = vm.Cue.Value;
            var text = cue != null ? cue.BaseText : null;

            // The check result ("[Failed an Athletics check]") is a runtime prefix the cue view composes
            // from the cue's SkillChecks — it's NOT part of BaseText — so prepend it the same way the game
            // does (UIUtility.SkillCheckText).
            if (cue != null && cue.SkillChecks != null && cue.SkillChecks.Count > 0)
            {
                var check = UIUtility.SkillCheckText(cue.SkillChecks);
                if (!string.IsNullOrEmpty(check)) text = string.IsNullOrEmpty(text) ? check : check + " " + text;
            }

            var speaker = vm.SpeakerName.Value;
            if (string.IsNullOrEmpty(text)) return speaker;
            return string.IsNullOrEmpty(speaker) ? text : speaker + ": " + text;
        }
    }
}
