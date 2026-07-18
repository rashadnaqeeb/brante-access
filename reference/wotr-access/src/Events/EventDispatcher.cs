using System;
using System.Collections.Generic;
using WrathAccess.Speech;

namespace WrathAccess.Events
{
    /// <summary>
    /// Collects <see cref="ModEvent"/>s and flushes them once per frame (so a burst is read in arrival
    /// order, and we never speak mid-game-frame). PROTOTYPE: no consolidation/dedup — every event reads
    /// individually; never interrupts (the mod's speech rule). Each event resolves its (per-source)
    /// settings via <see cref="EventRegistry"/> and reads through the chosen <see cref="SpeechConfig"/>.
    /// Positional playback isn't wired yet (next increment); everything reads non-positionally for now.
    /// </summary>
    internal static class EventDispatcher
    {
        private static readonly List<ModEvent> _pending = new List<ModEvent>();

        public static void Raise(ModEvent e)
        {
            if (e != null) _pending.Add(e);
        }

        public static void Tick()
        {
            if (_pending.Count == 0) return;
            // Snapshot count: an event's GetMessage/dispatch won't re-enter and grow this mid-loop, but
            // be defensive — only flush what's present now, keep anything raised during the flush.
            int n = _pending.Count;
            for (int i = 0; i < n; i++) Dispatch(_pending[i]);
            _pending.RemoveRange(0, n);
        }

        private static void Dispatch(ModEvent e)
        {
            try
            {
                if (!Main.Enabled || !e.Visible || !EventRegistry.Enabled(e)) return;
                var msg = e.GetMessage();
                if (msg == null || msg.IsEmpty) return;
                var text = msg.Resolve();
                var config = SpeechConfigRegistry.Get(EventRegistry.ConfigId(e));

                // Render-capable configs (SAPI) read through the mod's mixer so utterances OVERLAP (combat
                // bursts) instead of queuing — positioned at the unit when the event opts in. Screen-reader
                // configs (Prism) can't render: fall back to live speech (which queues).
                if (config.SupportsPositional)
                {
                    var audio = config.RenderToAudio(text);
                    if (audio != null)
                    {
                        bool positional = EventRegistry.Positional(e) && e.Source != EventSources.None;
                        WrathAccess.Audio.PositionalSpeech.Play(audio, positional ? e.Position : (UnityEngine.Vector3?)null);
                        return;
                    }
                }
                config.Output(text, interrupt: false);
            }
            catch (Exception ex) { Main.Log?.Error("[events] dispatch failed: " + ex.Message); }
        }
    }
}
