using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem; // LogThreadBase, CombatLogMessage

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The live feed of game-log lines, tagged with the thread type that produced them. Every one of the
    /// game's ~50 log threads funnels its lines through <c>LogThreadBase.AddMessage</c>, so one Harmony
    /// postfix captures everything — no per-thread UniRx subscriptions, and nothing to resubscribe when the
    /// per-session <c>LogThreadService</c> is rebuilt on load. Messages buffer here (newest-capped) and the
    /// active overlay's <see cref="LogSystem"/> drains them each tick, speaking the types its toggles allow.
    /// </summary>
    internal static class LogFeed
    {
        private static readonly Queue<KeyValuePair<string, string>> _q = new Queue<KeyValuePair<string, string>>();
        private const int Cap = 64; // a backstop while nothing drains — drop oldest, never grow unbounded

        public static void Push(string threadType, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _q.Enqueue(new KeyValuePair<string, string>(threadType, text));
            while (_q.Count > Cap) _q.Dequeue();
        }

        public static bool TryDequeue(out string threadType, out string text)
        {
            if (_q.Count == 0) { threadType = null; text = null; return false; }
            var kv = _q.Dequeue();
            threadType = kv.Key;
            text = kv.Value;
            return true;
        }

        public static void Clear() => _q.Clear();
    }

    [HarmonyPatch(typeof(LogThreadBase), "AddMessage")]
    internal static class LogFeedPatch
    {
        private static void Postfix(LogThreadBase __instance, CombatLogMessage newMessage)
            => LogFeed.Push(__instance.GetType().Name, newMessage?.Message);
    }
}
