using System;
using Newtonsoft.Json.Linq;

namespace WrathAccess.Settings
{
    /// <summary>A boolean setting (→ a checkbox in the menu).</summary>
    public sealed class BoolSetting : Setting
    {
        public bool Default { get; }
        public bool Value { get; private set; }
        public event Action<bool> Changed;

        public BoolSetting(string key, string label, bool defaultValue = true, string localizationKey = "")
            : base(key, label, localizationKey)
        {
            Default = defaultValue;
            Value = defaultValue;
        }

        public bool Get() => Value;

        public override void ResetToDefault() => Set(Default);

        public void Set(bool value)
        {
            if (Value == value) return;
            Value = value;
            ModSettings.MarkDirty();
            Changed?.Invoke(value);
        }

        public override object BoxedValue => Value;

        public override void LoadValue(object value)
        {
            if (value is JToken t && t.Type == JTokenType.Boolean)
            {
                var b = t.Value<bool>();
                if (b != Value) { Value = b; Changed?.Invoke(b); }
            }
        }
    }
}
