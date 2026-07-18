using System;
using System.Reflection;
using Kingmaker;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.View;                   // CameraRig

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Camera pan / rotate / follow for low-vision players, replicating the game's own camera controls.
    /// Pan and rotate call the <see cref="CameraRig"/>'s own keyboard scroll/rotate methods (private, so
    /// reflected — see the reflection-acceptable rule) so they use the game's speed settings and feel; follow
    /// engages the game's <c>CameraController.Follower</c> on the selected character (the same follower the
    /// game releases when you scroll manually). Registered in the Exploration input category, so — exactly
    /// like the game — they work in an area while you have control and go dead in cutscenes, dialogue, menus,
    /// and on the world map (which has its own camera).
    /// </summary>
    internal static class CameraControls
    {
        private static CameraRig Rig => Game.Instance?.UI?.GetCameraRig();

        // The CameraRig's own private keyboard-scroll / rotate methods — the ones it binds to the game's
        // camera keys (CameraLeft/Right/Up/Down, CameraRotateLeft/Right). Resolved once.
        private static readonly MethodInfo PanUpM = Method("AddUp");
        private static readonly MethodInfo PanDownM = Method("AddDown");
        private static readonly MethodInfo PanLeftM = Method("AddLeft");
        private static readonly MethodInfo PanRightM = Method("AddRight");
        private static readonly MethodInfo RotLeftM = Method("RotateLeft");
        private static readonly MethodInfo RotRightM = Method("RotateRight");

        private static MethodInfo Method(string name)
            => typeof(CameraRig).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

        public static void PanUp() => Invoke(PanUpM);
        public static void PanDown() => Invoke(PanDownM);
        public static void PanLeft() => Invoke(PanLeftM);
        public static void PanRight() => Invoke(PanRightM);
        public static void RotateLeft() => Invoke(RotLeftM);
        public static void RotateRight() => Invoke(RotRightM);

        private static void Invoke(MethodInfo m)
        {
            var rig = Rig;
            if (rig == null || m == null) return;
            try { m.Invoke(rig, null); }
            catch (Exception e) { Main.Log?.Error("[camera] " + e.Message); }
        }

        // Follow the selected character with the game's own camera follower (auto-released when you pan/rotate
        // or scroll manually — the rig calls Follower.Release() then). CameraController is a decompile-skipped
        // type, so reach the Follower reflectively off the live object.
        public static void Follow()
        {
            var unit = SelectedUnit();
            var cc = Game.Instance?.CameraController;
            if (unit == null || cc == null) return;
            try
            {
                var follower = cc.GetType().GetProperty("Follower")?.GetValue(cc)
                            ?? cc.GetType().GetField("Follower")?.GetValue(cc);
                follower?.GetType().GetMethod("Follow", new[] { typeof(UnitEntityData) })
                        ?.Invoke(follower, new object[] { unit });
            }
            catch (Exception e) { Main.Log?.Error("[camera follow] " + e.Message); }
        }

        // The character to follow: the current single selection (Ctrl+1..6), else the main character.
        private static UnitEntityData SelectedUnit()
        {
            var sel = Game.Instance?.SelectionCharacter?.SelectedUnits;
            if (sel != null)
                foreach (var u in sel)
                    if (u != null) return u;
            return Game.Instance?.Player?.MainCharacter.Value;
        }
    }
}
