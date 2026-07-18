using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WrathAccess.Settings
{
    /// <summary>An integer setting with a range + step (→ a slider in the menu).</summary>
    public sealed class IntSetting : Setting
    {
        public int Default { get; }
        public int Min { get; }
        public int Max { get; }
        public int Step { get; }
        public int Value { get; private set; }
        public event Action<int> Changed;

        public IntSetting(string key, string label, int defaultValue, int min, int max, int step = 1,
            string localizationKey = "") : base(key, label, localizationKey)
        {
            Min = min;
            Max = max;
            Step = Mathf.Max(1, step);
            Default = Mathf.Clamp(defaultValue, min, max);
            Value = Default;
        }

        public int Get() => Value;

        public override void ResetToDefault() => Set(Default);

        public void Set(int value)
        {
            value = Mathf.Clamp(value, Min, Max);
            if (Value == value) return;
            Value = value;
            ModSettings.MarkDirty();
            Changed?.Invoke(value);
        }

        public override object BoxedValue => Value;

        public override void LoadValue(object value)
        {
            if (value is JToken t && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float))
            {
                var v = Mathf.Clamp(t.Value<int>(), Min, Max);
                if (v != Value) { Value = v; Changed?.Invoke(v); }
            }
        }
    }
}
