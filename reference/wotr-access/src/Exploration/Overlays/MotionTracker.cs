using UnityEngine;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Tracks whether a point (a cursor) has moved recently, with a short linger so it reads as "moving"
    /// smoothly across discrete steps / key-repeat gaps instead of flickering. Fed a position each frame;
    /// <see cref="MovingRecently"/> stays true for <see cref="LingerSec"/> after the last real move. Drives
    /// the systems' "when moving" play mode (in-area cursor on the Overlay; world-map cursor on the map sonar).
    /// </summary>
    internal sealed class MotionTracker
    {
        public const float LingerSec = 0.25f;

        private Vector3 _last;
        private bool _has;
        private float _linger;

        public bool MovingRecently { get; private set; }

        // intentMoving = the player is actively holding the movement keys even if the position can't change
        // (e.g. the cursor is against a wall) — counts as moving alongside a real position change.
        public void Update(Vector3 pos, float dt, bool intentMoving = false)
        {
            bool moved = intentMoving || (_has && (pos - _last).sqrMagnitude > 1e-6f);
            if (moved) _linger = LingerSec;
            else _linger -= dt;
            _last = pos;
            _has = true;
            MovingRecently = _linger > 0f;
        }

        public void Reset() { _has = false; _linger = 0f; MovingRecently = false; }
    }
}
