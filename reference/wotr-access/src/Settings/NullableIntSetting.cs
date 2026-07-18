using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WrathAccess.Settings
{
    /// <summary>
    /// An integer override that inherits from a <see cref="Fallback"/> setting — the int counterpart of
    /// <see cref="NullableBoolSetting"/>. Stores <c>int?</c>: null = inherit, a value = explicit override.
    /// The menu renders it as a slider over the RESOLVED value (the user never sees a raw "inherit" slot);
    /// stepping writes an explicit value, the secondary action <see cref="Reset"/>s back to inherit, and
    /// only an explicit value is persisted (null isn't). Used for per-speech-config handler params (SAPI
    /// rate/volume/boost) that follow the default config until deliberately overridden.
    /// </summary>
    public sealed class NullableIntSetting : Setting, INullableSetting
    {
        public IntSetting Fallback { get; }
        public int? LocalValue { get; private set; }
        public int Min { get; }
        public int Max { get; }
        public int Step { get; }

        public NullableIntSetting(string key, string label, IntSetting fallback, int min, int max,
            int step = 1, string localizationKey = "") : base(key, label, localizationKey)
        {
            Fallback = fallback;
            Min = min;
            Max = max;
            Step = Mathf.Max(1, step);
        }

        public bool IsOverridden => LocalValue.HasValue;

        /// <summary>The value that takes effect: explicit override if set, else the fallback's value.</summary>
        public int Resolved => Mathf.Clamp(LocalValue ?? (Fallback?.Get() ?? Min), Min, Max);

        public void SetExplicit(int value)
        {
            value = Mathf.Clamp(value, Min, Max);
            if (LocalValue == value) return;
            LocalValue = value;
            ModSettings.MarkDirty();
        }

        public override void ResetToDefault() => Reset(); // default = no override (follow the fallback)

        /// <summary>Clear the override so it follows the fallback again.</summary>
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
                if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
                    LocalValue = Mathf.Clamp(t.Value<int>(), Min, Max);
                else if (t.Type == JTokenType.Null) LocalValue = null;
            }
        }
    }
}
