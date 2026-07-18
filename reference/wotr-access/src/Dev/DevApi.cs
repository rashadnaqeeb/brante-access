#if DEBUG
using System.Reflection;
using WrathAccess.Screens;

namespace WrathAccess.Dev
{
    /// <summary>
    /// A tiny PUBLIC surface for eval'd code to reach from. Mono.CSharp eval runs in its own dynamic
    /// assembly and sees only PUBLIC members of the mod, while almost all of WrathAccess is internal — so
    /// rather than mirror the codebase, this exposes a handle to reflect from (<see cref="Asm"/>) plus a
    /// couple of high-use probes. Reflection into internals is expected and fine (we reflect into the game
    /// throughout the mod anyway); this just removes the boilerplate from the common cases. DEBUG-only.
    /// </summary>
    public static class DevApi
    {
        /// <summary>The mod assembly — reflect into internals with
        /// <c>DevApi.Asm.GetType("WrathAccess.UI.Navigation")</c> etc.</summary>
        public static Assembly Asm => typeof(DevApi).Assembly;

        /// <summary>Speak a probe line through the real speech path (also lands in /speech).</summary>
        public static void Say(string text) => Tts.Speak(text, true);

        /// <summary>The active screen: "key | name", or "(none)".</summary>
        public static string Screen()
        {
            var s = ScreenManager.Current;
            return s == null ? "(none)" : s.Key + " | " + (s.ScreenName ?? "");
        }
    }
}
#endif
