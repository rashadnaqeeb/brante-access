using UnityEngine;

namespace WrathAccess.Input
{
    /// <summary>
    /// A keyboard combo polled against Unity Input. Phase-agnostic: it reports
    /// JustPressed/Held/Released so the dispatcher can route each phase into the
    /// navigator (OnInputJustPressed/Held/Released).
    /// </summary>
    public class KeyboardBinding : InputBinding
    {
        public KeyCode Key { get; }
        public bool Ctrl { get; }
        public bool Shift { get; }
        public bool Alt { get; }

        public KeyboardBinding(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
        }

        private static bool CtrlHeld =>
            UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
        private static bool ShiftHeld =>
            UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
        private static bool AltHeld =>
            UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);

        // Modifiers must match exactly so Ctrl+A doesn't also fire a bare-A binding.
        private bool ModifiersMatch() => Ctrl == CtrlHeld && Shift == ShiftHeld && Alt == AltHeld;

        public override bool JustPressed() => ModifiersMatch() && UnityEngine.Input.GetKeyDown(Key);
        public override bool Held() => ModifiersMatch() && UnityEngine.Input.GetKey(Key);
        public override bool Released() => ModifiersMatch() && UnityEngine.Input.GetKeyUp(Key);

        public override string DisplayName
        {
            get
            {
                var s = "";
                if (Ctrl) s += "Ctrl+";
                if (Shift) s += "Shift+";
                if (Alt) s += "Alt+";
                return s + Key;
            }
        }

        public override string Type => "keyboard";

        // "A|ctrl,shift" — the KeyCode, then a comma-list of held modifiers (omitted if none).
        public override string Serialize()
        {
            var mods = new System.Collections.Generic.List<string>();
            if (Ctrl) mods.Add("ctrl");
            if (Shift) mods.Add("shift");
            if (Alt) mods.Add("alt");
            return mods.Count == 0 ? Key.ToString() : Key + "|" + string.Join(",", mods);
        }

        public static KeyboardBinding Deserialize(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            var parts = data.Split('|');
            if (!System.Enum.TryParse(parts[0], out KeyCode key)) return null;
            bool ctrl = false, shift = false, alt = false;
            if (parts.Length > 1)
                foreach (var m in parts[1].Split(','))
                    switch (m) { case "ctrl": ctrl = true; break; case "shift": shift = true; break; case "alt": alt = true; break; }
            return new KeyboardBinding(key, ctrl, shift, alt);
        }
    }
}
