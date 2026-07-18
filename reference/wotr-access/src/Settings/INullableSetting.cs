namespace WrathAccess.Settings
{
    /// <summary>
    /// Marker for the Nullable* override settings (per-element-type announcement overrides). Lets the
    /// resolution code check whether an override is explicitly set without knowing the value type.
    /// </summary>
    public interface INullableSetting
    {
        bool IsOverridden { get; }
    }
}
