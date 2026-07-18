using System;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A screen whose activity is a simple predicate against game state. Lets us
    /// register the many simple screens (contexts, service windows, overlays)
    /// without a subclass each. Screens that need real lifecycle behaviour
    /// (nav tree, focus memory) get their own Screen subclass instead.
    /// </summary>
    public sealed class PredicateScreen : Screen
    {
        private readonly Func<bool> _isActive;

        public override string Key { get; }
        public override string ScreenName { get; }
        public override int Layer { get; }

        public PredicateScreen(string key, string screenName, int layer, Func<bool> isActive)
        {
            Key = key;
            ScreenName = screenName;
            Layer = layer;
            _isActive = isActive;
        }

        public override bool IsActive() => _isActive();
    }
}
