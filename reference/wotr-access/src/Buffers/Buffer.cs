using System;
using System.Collections.Generic;

namespace WrathAccess.Buffers
{
    /// <summary>
    /// One review "buffer": a named, ordered list of text lines the user scrubs through with the buffer
    /// keys (Alt+Up/Down within a buffer, Alt+Left/Right between buffers — see <see cref="BufferControls"/>).
    /// A buffer is orthogonal to focus navigation — it's a parallel review channel onto a live thing (a
    /// unit, the event log, …). Ported from SayTheSpire2's buffers (sibling project): subclasses bind a
    /// live game object and override <see cref="Update"/> to repopulate from it, so a buffer always reads
    /// the current state when navigated.
    /// </summary>
    internal class Buffer
    {
        private readonly List<string> _contents = new List<string>();

        public string Key { get; }

        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; if (!value) ClearBinding(); }
        }

        public int Position { get; private set; }

        /// <summary>When true, switching to this buffer jumps to the latest (last) line — for an append-only
        /// stream like an event log.</summary>
        public bool FollowLatest { get; set; }

        public Buffer(string key) { Key = key; }

        /// <summary>The spoken buffer name (localized <c>buffers.&lt;key&gt;</c>); falls back to the key.</summary>
        public string Label
        {
            get { var s = Loc.T("buffers." + Key); return string.IsNullOrEmpty(s) ? Key : s; }
        }

        public void Add(string item) { if (!string.IsNullOrEmpty(item)) _contents.Add(item); }
        public void Clear() { _contents.Clear(); Position = 0; }
        public bool IsEmpty => _contents.Count == 0;
        public int Count => _contents.Count;

        public string CurrentItem
        {
            get
            {
                if (_contents.Count == 0) return null;
                if (Position >= _contents.Count) Position = 0;
                return _contents[Position];
            }
        }

        public bool MoveToNext()
        {
            Update();
            if (Position + 1 >= _contents.Count) return false;
            Position++;
            return true;
        }

        public bool MoveToPrevious()
        {
            Update();
            if (Position - 1 < 0) return false;
            Position--;
            return true;
        }

        public bool MoveToPosition(int position)
        {
            if (position < 0 || position >= _contents.Count) return false;
            Position = position;
            return true;
        }

        /// <summary>Called when the buffer becomes active or is navigated — subclasses refresh their lines
        /// from the bound live object here.</summary>
        public virtual void Update() { }

        /// <summary>Called when the buffer is disabled — subclasses clear their bound object so stale data
        /// isn't shown if it's re-enabled.</summary>
        protected virtual void ClearBinding() { }

        /// <summary>Clear and repopulate while preserving the cursor position (so a live refresh during
        /// item navigation doesn't snap the user back to the top).</summary>
        protected void Repopulate(Action populate)
        {
            var saved = Position;
            Clear();
            populate();
            if (saved > 0 && saved < Count) MoveToPosition(saved);
        }
    }
}
