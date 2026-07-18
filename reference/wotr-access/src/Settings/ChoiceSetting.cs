using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WrathAccess.Settings
{
    /// <summary>One option of a <see cref="ChoiceSetting"/>: a stable <see cref="Id"/> (persisted) and a
    /// display label (localized via the "settings" table with a raw fallback).</summary>
    public sealed class Choice
    {
        public string Id { get; }
        private readonly string _labelFallback;
        private readonly string _localizationKey;

        public Choice(string id, string label, string localizationKey = "")
        {
            Id = id;
            _labelFallback = label;
            _localizationKey = localizationKey;
        }

        public string Label => string.IsNullOrEmpty(_localizationKey)
            ? _labelFallback
            : WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", _localizationKey, _labelFallback);
    }

    /// <summary>A pick-one-of-N setting (→ a dropdown in the menu). Persisted by the chosen option's Id.</summary>
    public sealed class ChoiceSetting : Setting
    {
        private readonly IReadOnlyList<Choice> _choices;
        private readonly Func<IReadOnlyList<Choice>> _choicesProvider;

        /// <summary>The options. With a provider (e.g. the live speech-config roster, which the user adds
        /// to / removes from at runtime) this re-evaluates on every read, so a freshly-built dropdown always
        /// reflects the current set; otherwise it's the fixed list passed at construction.</summary>
        public IReadOnlyList<Choice> Choices => _choicesProvider != null
            ? (_choicesProvider() ?? System.Array.Empty<Choice>())
            : _choices;

        public string Default { get; }
        public string ValueId { get; private set; }
        public event Action<string> Changed;

        public ChoiceSetting(string key, string label, IReadOnlyList<Choice> choices, string defaultId,
            string localizationKey = "") : base(key, label, localizationKey)
        {
            _choices = choices ?? new List<Choice>();
            Default = defaultId;
            ValueId = defaultId;
        }

        /// <summary>Live-options overload — the dropdown re-reads <paramref name="choicesProvider"/> on each
        /// access (for option sets that change at runtime, e.g. the speech-config roster).</summary>
        public ChoiceSetting(string key, string label, Func<IReadOnlyList<Choice>> choicesProvider,
            string defaultId, string localizationKey = "") : base(key, label, localizationKey)
        {
            _choicesProvider = choicesProvider;
            Default = defaultId;
            ValueId = defaultId;
        }

        public Choice Current => Choices.FirstOrDefault(c => c.Id == ValueId);

        public override void ResetToDefault() => Set(Default);

        public void Set(string id)
        {
            if (ValueId == id || Choices.All(c => c.Id != id)) return;
            ValueId = id;
            ModSettings.MarkDirty();
            Changed?.Invoke(id);
        }

        public override object BoxedValue => ValueId;

        public override void LoadValue(object value)
        {
            if (value is JToken t && t.Type == JTokenType.String)
            {
                var id = t.Value<string>();
                if (id != ValueId && Choices.Any(c => c.Id == id)) { ValueId = id; Changed?.Invoke(id); }
            }
        }
    }
}
