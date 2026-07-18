using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WrathAccess.Settings
{
    /// <summary>
    /// A pick-one-of-N override that inherits until explicitly set — the choice counterpart of
    /// <see cref="NullableIntSetting"/>/<see cref="NullableBoolSetting"/>, replacing the old pattern of a
    /// magic "inherit" sentinel Choice inside the option list with externally-wired display resolution.
    /// Stores a nullable id: null = inheriting; the setting OWNS its resolution
    /// (<see cref="ResolveInherited"/> returns the effective id it falls back to — the parent taxonomy
    /// pick, the default speech config's same setting, the per-source global). The menu offers
    /// <see cref="InheritOption"/> as the chooser's leading option (picking it calls <see cref="Reset"/>),
    /// and Backspace resets too, exactly like the nullable sliders/toggles.
    ///
    /// Persistence keeps the OLD on-disk format: inheriting saves as the literal string "inherit", so
    /// existing settings.json files load unchanged.
    /// </summary>
    public sealed class NullableChoiceSetting : Setting, INullableSetting
    {
        private const string InheritToken = "inherit"; // the legacy sentinel, kept as the disk format

        private readonly IReadOnlyList<Choice> _choices;
        private readonly Func<IReadOnlyList<Choice>> _choicesProvider;

        /// <summary>The REAL options (no inherit sentinel — the chooser adds <see cref="InheritOption"/>
        /// itself). With a provider (e.g. the live speech-config roster) this re-evaluates per read.</summary>
        public IReadOnlyList<Choice> Choices => _choicesProvider != null
            ? (_choicesProvider() ?? Array.Empty<Choice>())
            : _choices;

        /// <summary>The chooser's leading "inherit" option (site-specific wording: "Inherit default",
        /// "Inherit").</summary>
        public Choice InheritOption { get; }

        /// <summary>The explicit default id, or null = inherits by default.</summary>
        public string DefaultId { get; }

        /// <summary>The explicit override id, or null = inheriting.</summary>
        public string LocalValue { get; private set; }

        /// <summary>The id this setting falls back to while inheriting (the parent pick, the default
        /// config's same setting, the per-source global). Wired by the schema builder that knows the
        /// source. Null/unset = no resolution available (the chooser still works; the spoken value falls
        /// back to the inherit option's label).</summary>
        public Func<string> ResolveInherited;

        public event Action<string> Changed; // the new explicit id, or null on reset-to-inherit

        public NullableChoiceSetting(string key, string label, IReadOnlyList<Choice> choices,
            string defaultId = null, Choice inheritOption = null, string localizationKey = "")
            : base(key, label, localizationKey)
        {
            _choices = choices ?? new List<Choice>();
            DefaultId = defaultId;
            LocalValue = defaultId;
            InheritOption = inheritOption ?? new Choice(InheritToken, "Inherit default", "choice.inherit_default");
        }

        /// <summary>Live-options overload (option sets that change at runtime).</summary>
        public NullableChoiceSetting(string key, string label, Func<IReadOnlyList<Choice>> choicesProvider,
            string defaultId = null, Choice inheritOption = null, string localizationKey = "")
            : base(key, label, localizationKey)
        {
            _choicesProvider = choicesProvider;
            DefaultId = defaultId;
            LocalValue = defaultId;
            InheritOption = inheritOption ?? new Choice(InheritToken, "Inherit default", "choice.inherit_default");
        }

        public bool IsOverridden => LocalValue != null;

        /// <summary>The id that takes effect: the explicit override, else the inherited resolution.</summary>
        public string EffectiveId => LocalValue ?? ResolveInherited?.Invoke();

        /// <summary>The effective choice, when the effective id is one of <see cref="Choices"/>.</summary>
        public Choice Current
        {
            get
            {
                var id = EffectiveId;
                return id == null ? null : Choices.FirstOrDefault(c => c.Id == id);
            }
        }

        /// <summary>The effective value's display: the matching choice's label, else the raw effective id
        /// (an inherited id that isn't in this setting's own list), else null (no resolution).</summary>
        public string ResolvedLabel => Current?.Label ?? EffectiveId;

        public void SetExplicit(string id)
        {
            if (LocalValue == id || Choices.All(c => c.Id != id)) return;
            LocalValue = id;
            ModSettings.MarkDirty();
            Changed?.Invoke(id);
        }

        /// <summary>Clear the override so it inherits again.</summary>
        public void Reset()
        {
            if (LocalValue == null) return;
            LocalValue = null;
            ModSettings.MarkDirty();
            Changed?.Invoke(null);
        }

        // Default = the declared default id (concrete for e.g. taxonomy sounds), or inherit when none.
        public override void ResetToDefault()
        {
            if (DefaultId == null) Reset();
            else SetExplicit(DefaultId);
        }

        public override object BoxedValue => LocalValue ?? InheritToken; // legacy disk format

        public override void LoadValue(object value)
        {
            if (!(value is JToken t) || t.Type != JTokenType.String) return;
            var id = t.Value<string>();
            if (id == InheritToken)
            {
                if (LocalValue != null) { LocalValue = null; Changed?.Invoke(null); }
                return;
            }
            if (id != LocalValue && Choices.Any(c => c.Id == id)) { LocalValue = id; Changed?.Invoke(id); }
        }
    }
}
