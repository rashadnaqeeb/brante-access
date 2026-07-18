using System;
using Newtonsoft.Json.Linq;

namespace WrathAccess.Settings
{
    /// <summary>
    /// A bool override that inherits from a global <see cref="Fallback"/> setting (ported from
    /// SayTheSpire2). Stores <c>bool?</c>: null = inherit, true/false = explicit override. The menu renders
    /// it as a checkbox showing the RESOLVED value (the user never sees "inherit"); toggling writes an
    /// explicit value, and the secondary action <see cref="Reset"/>s it back to inherit. Only an explicit
    /// value is persisted (null isn't), so the file holds just real overrides. Used for per-element-type
    /// announcement overrides.
    /// </summary>
    public sealed class NullableBoolSetting : Setting, INullableSetting
    {
        public BoolSetting Fallback { get; }
        public bool? LocalValue { get; private set; }

        public NullableBoolSetting(string key, string label, BoolSetting fallback, string localizationKey = "")
            : base(key, label, localizationKey)
        {
            Fallback = fallback;
        }

        public bool IsOverridden => LocalValue.HasValue;

        /// <summary>The value that takes effect: explicit override if set, else the global fallback.</summary>
        public bool Resolved => LocalValue ?? (Fallback != null && Fallback.Get());

        public void SetExplicit(bool value)
        {
            if (LocalValue == value) return;
            LocalValue = value;
            ModSettings.MarkDirty();
        }

        /// <summary>Toggle the resolved value (writing an explicit override).</summary>
        public void ToggleExplicit() => SetExplicit(!Resolved);

        public override void ResetToDefault() => Reset(); // default = no override (follow the global)

        /// <summary>Clear the override so it follows the global fallback again.</summary>
        public void Reset()
        {
            if (!LocalValue.HasValue) return;
            LocalValue = null;
            ModSettings.MarkDirty();
        }

        public override object BoxedValue => LocalValue.HasValue ? (object)LocalValue.Value : null; // null → not saved

        public override void LoadValue(object value)
        {
            if (value is JToken t)
            {
                if (t.Type == JTokenType.Boolean) LocalValue = t.Value<bool>();
                else if (t.Type == JTokenType.Null) LocalValue = null;
            }
        }
    }
}
