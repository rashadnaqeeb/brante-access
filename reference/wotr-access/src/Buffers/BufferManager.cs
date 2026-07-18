using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using System.Linq;

namespace WrathAccess.Buffers
{
    /// <summary>
    /// The ring of <see cref="Buffer"/>s and the current position within it. Alt+Left/Right cycle between
    /// enabled buffers (skipping disabled ones, wrapping); Alt+Up/Down move within the current buffer.
    /// Ported from SayTheSpire2. v1 ships two unit buffers — the selected unit and the review-cursor unit —
    /// both always enabled; enable/disable-per-screen and more buffer types (events, focus detail) come later.
    /// </summary>
    internal sealed class BufferManager
    {
        public static BufferManager Instance { get; } = new BufferManager();

        private readonly List<Buffer> _buffers = new List<Buffer>();
        private int _position = -1;

        public void Add(Buffer buffer) => _buffers.Add(buffer);

        public Buffer GetBuffer(string key)
        {
            foreach (var b in _buffers) if (b.Key == key) return b;
            return null;
        }

        public Buffer CurrentBuffer
        {
            get
            {
                if (_position < 0 || _position >= _buffers.Count) return null;
                var b = _buffers[_position];
                return b.Enabled ? b : null;
            }
        }

        public void EnableBuffer(string key, bool enabled)
        {
            var b = GetBuffer(key);
            if (b == null) return;
            b.Enabled = enabled;
            if (!enabled && b == CurrentBuffer) MoveToPrevious();
            else if (enabled && _position == -1) { _position = _buffers.IndexOf(b); b.Update(); }
        }

        public void SetCurrentBuffer(string key)
        {
            for (int i = 0; i < _buffers.Count; i++)
                if (_buffers[i].Key == key)
                {
                    _buffers[i].Update();
                    if (_buffers[i].FollowLatest && _buffers[i].Count > 0)
                        _buffers[i].MoveToPosition(_buffers[i].Count - 1);
                    _position = i;
                    return;
                }
        }

        public bool MoveToNext() => Step(+1);
        public bool MoveToPrevious() => Step(-1);

        // Walk the ring in the given direction to the next enabled buffer; refresh it and (if it follows the
        // latest) jump to its last line. Returns false when no enabled buffer exists.
        private bool Step(int dir)
        {
            if (_buffers.Count == 0) return false;
            int start = _position < 0 ? (dir > 0 ? _buffers.Count - 1 : 0) : _position;
            int i = start;
            do
            {
                i += dir;
                if (i >= _buffers.Count) i = 0;
                if (i < 0) i = _buffers.Count - 1;
                if (_buffers[i].Enabled)
                {
                    _position = i;
                    _buffers[i].Update();
                    if (_buffers[i].FollowLatest && _buffers[i].Count > 0)
                        _buffers[i].MoveToPosition(_buffers[i].Count - 1);
                    return true;
                }
            } while (i != start);
            return false;
        }

        /// <summary>Build the standard buffer set (once, at boot). The two unit buffers read their live unit
        /// from the game each refresh: the selected unit (the game's real single selection — Ctrl+1..6) and
        /// the review-cursor unit (the scanner's review target, when it's on a unit).</summary>
        public void RegisterDefaults()
        {
            if (_buffers.Count > 0) return;
            Add(new UnitBuffer("selected_unit", SelectedUnit));
            Add(new UnitBuffer("review_unit", ReviewUnit));
            foreach (var b in _buffers) b.Enabled = true;
            // Leave _position at -1: the first Alt+Left/Right ENTERS a buffer and reads its first line (the
            // name), then Alt+Up/Down advance from there (the SayTheSpire buffer convention).
        }

        // The game's current single selection (the unit Ctrl+1..6 selects); the first controllable when
        // several are selected. Null when nothing's selected or out of game.
        private static UnitEntityData SelectedUnit()
        {
            var sel = Game.Instance?.SelectionCharacter?.SelectedUnits;
            if (sel == null) return null;
            return sel.FirstOrDefault(u => u != null && u.IsDirectlyControllable) ?? sel.FirstOrDefault();
        }

        // The scanner's review-cursor target, when it's on a unit.
        private static UnitEntityData ReviewUnit()
        {
            var item = WrathAccess.Exploration.Scanner.Reviewed;
            return item != null && item.IsUnit ? item.TargetUnit : null;
        }
    }
}
