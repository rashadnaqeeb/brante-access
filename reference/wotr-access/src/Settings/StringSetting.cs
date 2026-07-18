using System;
using Newtonsoft.Json.Linq;

namespace WrathAccess.Settings
{
    /// <summary>A free-text setting (persisted as a JSON string). Used for things like the overlay-id
    /// list and per-overlay display names. Often <see cref="Setting.Hidden"/> (edited indirectly).</summary>
    public sealed class StringSetting : Setting
    {
        public string Default { get; }
        public string Value { get; private set; }
        public event Action<string> Changed;

        public StringSetting(string key, string label, string defaultValue = "", string localizationKey = "")
            : base(key, label, localizationKey)
        {
            Default = defaultValue ?? "";
            Value = Default;
        }

        public string Get() => Value;

        public override void ResetToDefault() => Set(Default);

        public void Set(string value)
        {
            value = value ?? "";
            if (Value == value) return;
            Value = value;
            ModSettings.MarkDirty();
            Changed?.Invoke(value);
        }

        public override object BoxedValue => Value;

        public override void LoadValue(object value)
        {
            if (value is JToken t && t.Type == JTokenType.String)
            {
                var s = t.Value<string>() ?? "";
                if (s != Value) { Value = s; Changed?.Invoke(s); }
            }
        }
    }
}
