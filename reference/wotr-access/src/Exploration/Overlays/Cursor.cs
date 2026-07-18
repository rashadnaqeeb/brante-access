using System.Collections.Generic;
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The overlay's point of attention — a world position plus the <see cref="MovementMode"/>s that move
    /// it. Movement lives here (not in systems) so multiple modes on different input slots can drive one
    /// cursor; systems only describe wherever it lands.
    ///
    /// <see cref="Position"/> is backed by the shared <see cref="WrathAccess.Exploration.Cursor"/> (the one
    /// point the scanner plants and move-to-cursor walks to), so browsing tiles and then walking to the
    /// current spot keeps working, and a jump made elsewhere (the scanner's Home) is honoured automatically.
    /// </summary>
    internal sealed class Cursor
    {
        private readonly List<MovementMode> _modes = new List<MovementMode>();

        // The single shared world point. Falls back to the player when nothing's set it yet.
        public Vector3 Position
        {
            get => WrathAccess.Exploration.Cursor.Has ? WrathAccess.Exploration.Cursor.Position.Value : PlayerPosition;
            set => WrathAccess.Exploration.Cursor.Set(value);
        }

        public IReadOnlyList<MovementMode> Modes => _modes;
        public void AddMode(MovementMode mode) { if (mode != null) _modes.Add(mode); }

        // Movement is driven by each slot's "mode" choice setting; the cursor (re)builds its modes from
        // them, so a mode change in the menu takes effect live (the registry also wires the choice's
        // Changed event to ResolveModes).
        private CategorySetting _primarySlot, _secondarySlot;

        /// <summary>The slot's settings category (mode/speed + the world-map mode/speed). Exposed so the
        /// world-map cursor can read THIS overlay's slots when it's the engaged one.</summary>
        public CategorySetting Slot(MovementSlot slot) => slot == MovementSlot.Primary ? _primarySlot : _secondarySlot;

        public void SetSlots(CategorySetting primary, CategorySetting secondary)
        {
            _primarySlot = primary;
            _secondarySlot = secondary;
            ResolveModes();
        }

        public void ResolveModes()
        {
            _modes.Clear();
            AddResolved(MovementSlot.Primary, _primarySlot);
            AddResolved(MovementSlot.Secondary, _secondarySlot);
        }

        private void AddResolved(MovementSlot slot, CategorySetting slotCat)
        {
            var id = slotCat?.Get<ChoiceSetting>("mode")?.Current?.Id ?? "none";
            if (id == "continuous") _modes.Add(new ContinuousGlide(slot, slotCat));
            else if (id == "tiled") _modes.Add(new TileStep(slot));
        }

        /// <summary>The movement mode bound to a slot, or null. (One mode per slot in practice.)</summary>
        public MovementMode ModeFor(MovementSlot slot)
        {
            foreach (var m in _modes) if (m.Slot == slot) return m;
            return null;
        }

        /// <summary>Whether the player is holding this cursor's movement keys for any ACTIVE slot — "trying
        /// to move" even when blocked (against a wall). Goes through the input-action held state
        /// (CursorKeys → InputManager.Held), so it respects bindings + category liveness. Drives the
        /// WhenMoving mode alongside actual position change.</summary>
        public bool MovementKeysHeld()
        {
            foreach (var m in _modes)
            {
                CursorKeys.HeldVector(m.Slot, out int dx, out int dz);
                if (dx != 0 || dz != 0) return true;
            }
            return false;
        }

        public void Recenter() => Position = PlayerPosition;

        public void Tick(float dt, Overlay overlay) { foreach (var m in _modes) m.Tick(dt, overlay); }
        public void OnEnter(Overlay overlay) { foreach (var m in _modes) m.OnEnter(overlay); }
        public void OnExit(Overlay overlay) { foreach (var m in _modes) m.OnExit(overlay); }

        /// <summary>The reference unit's live position — the origin for relative readouts and recenter. In
        /// turn-based that's the acting unit (so "c" lands on whoever's turn it is); otherwise the main
        /// character.</summary>
        public static Vector3 PlayerPosition
        {
            get
            {
                var p = Kingmaker.Game.Instance?.Player;
                var u = WrathAccess.Exploration.CombatMode.ReferenceUnit
                    ?? (p != null ? p.MainCharacter.Value : null);
                return WrathAccess.Exploration.Geo.Live(u);
            }
        }
    }
}
