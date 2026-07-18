using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using WrathAccess.Input;

namespace WrathAccess.Settings
{
    /// <summary>
    /// Wraps an <see cref="InputAction"/> so its key bindings persist like any other setting (ported from
    /// SayTheSpire2). Serializes the action's bindings as [{type, data}] and reloads them by clearing then
    /// re-adding. Auto-saves whenever the action's bindings change (a rebind), guarded so loading saved
    /// bindings doesn't re-trigger a save. The default bindings registered in code stand until a saved
    /// config overrides them. Label defaults to the action's label; the value shows the current combo.
    /// </summary>
    public sealed class BindingSetting : Setting
    {
        private readonly InputAction _action;
        private bool _loading;

        public InputAction Action => _action;

        // The default combos, captured at construction — BuildSettings runs after registration but
        // BEFORE the saved config loads, so this snapshot is the registration-time default set.
        private readonly List<(string type, string data)> _defaults;

        public BindingSetting(InputAction action) : base(action.Key, action.Label, "bind." + action.Key)
        {
            _action = action;
            _defaults = action.Bindings.Select(b => (b.Type, b.Serialize())).ToList();
            _action.BindingsChanged += () => { if (!_loading) ModSettings.MarkDirty(); };
        }

        public override void ResetToDefault()
        {
            _loading = true; // one save at the end, not per AddBinding
            try
            {
                _action.ClearBindings();
                foreach (var (type, data) in _defaults)
                {
                    var parsed = InputBinding.Deserialize(type, data);
                    if (parsed != null) _action.AddBinding(parsed);
                }
            }
            finally { _loading = false; }
            ModSettings.MarkDirty();
        }

        public override object BoxedValue =>
            _action.Bindings.Select(b => new Dictionary<string, string>
            {
                { "type", b.Type },
                { "binding", b.Serialize() },
            }).ToList();

        public override void LoadValue(object value)
        {
            if (!(value is JToken token) || token.Type != JTokenType.Array) return;
            _loading = true;
            try
            {
                _action.ClearBindings();
                foreach (var item in (JArray)token)
                {
                    if (item.Type != JTokenType.Object) continue;
                    var type = item["type"]?.ToString();
                    var data = item["binding"]?.ToString();
                    if (type == null || data == null) continue;
                    var parsed = InputBinding.Deserialize(type, data);
                    if (parsed != null) _action.AddBinding(parsed);
                }
            }
            finally { _loading = false; }
        }
    }
}
