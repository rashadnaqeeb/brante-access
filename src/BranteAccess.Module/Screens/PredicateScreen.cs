using System;
using BranteAccess.Module.Speech;

namespace BranteAccess.Module.Screens
{
    /// <summary>A screen fully described by its constructor arguments - for simple surfaces whose
    /// activity is one live-state predicate, and for dev-driven stack tests. Ported from wotr-access.</summary>
    public class PredicateScreen : Screen
    {
        private readonly string _key;
        private readonly Message _name;
        private readonly int _layer;
        private readonly Func<bool> _isActive;

        public PredicateScreen(string key, Message name, int layer, Func<bool> isActive)
        {
            _key = key;
            _name = name;
            _layer = layer;
            _isActive = isActive;
        }

        public override string Key => _key;
        public override Message ScreenName => _name;
        public override int Layer => _layer;
        public override bool IsActive() => _isActive();
    }
}
