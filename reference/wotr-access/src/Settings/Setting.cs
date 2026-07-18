using System;

namespace WrathAccess.Settings
{
    /// <summary>
    /// One mod setting (or category) in the persisted tree. Ported from SayTheSpire2. The leaf types
    /// (<see cref="BoolSetting"/>, <see cref="IntSetting"/>, <see cref="ChoiceSetting"/>,
    /// <see cref="BindingSetting"/>) carry a typed value; <see cref="CategorySetting"/> nests children.
    /// <see cref="FullPath"/> (the dot-path down the tree) is the serialization key, so reorganizing the
    /// menu doesn't break saved settings as long as keys are stable. Labels resolve from the "settings"
    /// locale table (<see cref="LocalizationKey"/>) with a raw fallback, or a live <see cref="LabelProvider"/>.
    /// </summary>
    public abstract class Setting
    {
        public string Key { get; }
        public CategorySetting Parent { get; internal set; }

        private readonly string _labelFallback;
        public string LocalizationKey { get; }

        /// <summary>Restore this setting to its registration-time default (the Reset buttons in the
        /// mod menu). Value types Set() their stored Default (firing Changed so live consumers apply);
        /// categories recurse; bindings restore the default combos.</summary>
        public virtual void ResetToDefault() { }

        /// <summary>Live label override (e.g. a binding showing its current key); wins over the loc key.</summary>
        public Func<string> LabelProvider { get; set; }

        public string Label => LabelProvider != null
            ? LabelProvider()
            : !string.IsNullOrEmpty(LocalizationKey)
                ? WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", LocalizationKey, _labelFallback)
                : _labelFallback;

        /// <summary>Whether this node's key is part of its serialized path. UI-only grouping categories
        /// can opt out so reorganizing the menu doesn't move saved keys.</summary>
        public virtual bool IncludeInPath => true;

        /// <summary>Lower sorts first; ties broken alphabetically by label.</summary>
        public int SortPriority { get; set; }

        /// <summary>When true the settings menu skips this node (state edited indirectly).</summary>
        public bool Hidden { get; set; }

        protected Setting(string key, string label, string localizationKey = "")
        {
            Key = key;
            _labelFallback = label;
            LocalizationKey = localizationKey;
        }

        public string FullPath
        {
            get
            {
                var parentPath = (Parent == null || Parent.IsRoot) ? string.Empty : Parent.FullPath;
                if (!IncludeInPath) return parentPath;
                if (string.IsNullOrEmpty(parentPath)) return Key;
                return parentPath + "." + Key;
            }
        }

        public virtual bool IsRoot => false;

        /// <summary>The value to serialize (null for categories — they're not persisted directly).</summary>
        public abstract object BoxedValue { get; }

        /// <summary>Load a value from deserialized JSON (a Newtonsoft JToken).</summary>
        public abstract void LoadValue(object value);
    }
}
