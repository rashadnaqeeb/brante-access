using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem; // CombatLogChannel
using Kingmaker.UI.MVVM._VM.CombatLog; // CombatLogVM
using WrathAccess.Localization;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The mod's log review screen, opened from the in-game HUD's Log button. Mirrors the game's on-screen
    /// combat log: a tabs list of its CHANNELS (All / Events / Combat / Dialogue) + the selected channel's
    /// messages, chronological (newest at the bottom). Reads the game's own history —
    /// <c>CombatLogChannel.Messages</c> (public) off <c>CombatLogVM.m_Channels</c> (reflected) — so
    /// switching our tab doesn't disturb the game's panel. Graph-native and immediate-mode, so the list is
    /// LIVE: new messages appear at the bottom as they arrive (message keys are absolute indices, so focus
    /// stays put while they do). Mod-pushed; engages focus mode so nav works, restores on close. Escape closes.
    /// </summary>
    public sealed class ModLogScreen : Screen
    {
        private static bool s_open;
        public static void Open() => s_open = true;
        public static void Close() => s_open = false;

        public override string Key => "overlay.modlog";
        public override string ScreenName => L("log.title", "Log");

        // Resuming your reading position across open/close is the point of a log review screen.
        public override bool KeepStateOnPop => true;
        public override int Layer => 22; // above exploration / dialogue / loot / saveload; below settings (25)
        public override bool IsActive() => s_open;

        private const int MaxLines = 200; // most-recent N per channel, for nav sanity
        private static readonly FieldInfo ChannelsField = AccessTools.Field(typeof(CombatLogVM), "m_Channels");

        private bool _priorFocus;
        private int _active; // the selected channel tab (our view state, not tree state)

        public override void OnPush() { _priorFocus = FocusMode.Active; FocusMode.Set(true); _active = 0; }
        public override void OnPop() { FocusMode.Set(_priorFocus); }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        private static List<CombatLogChannel> Channels()
        {
            var clog = Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.CombatLogVM;
            return clog != null ? ChannelsField?.GetValue(clog) as List<CombatLogChannel> : null;
        }

        private static string L(string key, string fallback) => LocalizationManager.GetOrDefault("ui", key, fallback);

        // The game localizes the channel labels only inside the combat-log prefab (each toggle's
        // LocalizedUIText → SharedStringAsset → LocalizedString); the strings aren't in any blueprint/VM, and
        // CombatLogChannel.ChannelName is just a hardcoded English id. So we resolve the prefab's
        // localization keys (recovered once via tools/combatlog_channels.py; stable — the game is
        // patch-frozen) through the game's OWN LocalizationManager, giving the exact in-game wording in the
        // current language. Falls back to the channel's English id if a key ever fails to resolve.
        private static readonly Dictionary<string, string> ChannelKeys = new Dictionary<string, string>
        {
            { "All",      "eb6c03cd-fd2a-456f-8c3a-5ddbcf921aab" },
            { "Events",   "4087542d-0070-432b-a446-8f868629ece1" },
            { "Combat",   "031c6795-5982-4b46-ad79-5b5c9c58a8c7" },
            { "Dialogue", "84d2b99d-7443-4adf-af41-df4f8bc27acf" },
        };

        private static string ChannelLabel(CombatLogChannel ch)
        {
            if (ch == null) return L("log.title", "Log");
            if (ChannelKeys.TryGetValue(ch.ChannelName, out var key))
            {
                var pack = Kingmaker.Localization.LocalizationManager.CurrentPack;
                var text = pack != null ? pack.GetText(key, false) : null;
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return ch.ChannelName; // fallback to the game's English id
        }


        public override void Build(GraphBuilder b)
        {
            var channels = Channels();

            // The channel tabs.
            b.BeginStop("tabs").PushContext(L("log.channels", "Channels"), "list");
            if (channels != null)
                for (int i = 0; i < channels.Count; i++)
                {
                    int idx = i;
                    b.AddItem(ControlId.Structural("modlog:tab:" + i),
                        GraphNodes.Tab(TabLabel(channels[i]), () => _active == idx, () => _active = idx));
                }
            b.PopContext();

            // The selected channel's messages — keys carry the channel, so a tab switch re-keys the
            // content only (tab focus survives); message keys are absolute indices, stable as new lines
            // append. No positions: "37 of 200" per line is noise.
            var ch = channels != null && _active >= 0 && _active < channels.Count ? channels[_active] : null;
            b.BeginStop("content").PushContext(ChannelLabel(ch), role: null, positions: false);
            var msgs = ch?.Messages;
            if (msgs != null && msgs.Count > 0)
            {
                int start = msgs.Count > MaxLines ? msgs.Count - MaxLines : 0;
                for (int i = start; i < msgs.Count; i++) // chronological: oldest first, newest at the bottom
                {
                    var msg = msgs[i]; // capture for the live read
                    b.AddItem(ControlId.Structural("modlog:ch" + _active + ":msg" + i),
                        GraphNodes.Text(() => msg.Message));
                }
            }
            else
            {
                b.AddItem(ControlId.Structural("modlog:ch" + _active + ":empty"),
                    GraphNodes.Text(() => L("log.empty", "No messages.")));
            }
            b.PopContext();
        }

        // (Named to avoid hiding Container.Label while Screen still derives from it during migration.)
        private static System.Func<string> TabLabel(CombatLogChannel ch) => () => ChannelLabel(ch);
    }
}
