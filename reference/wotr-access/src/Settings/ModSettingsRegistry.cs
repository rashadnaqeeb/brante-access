namespace WrathAccess.Settings
{
    /// <summary>
    /// Helper to create nested setting categories by dot-path, building any missing intermediate
    /// categories (ported from SayTheSpire2). Label segments are slash-separated and used as fallback
    /// display names per path segment; an optional slash-separated localizationKey gives each its loc key.
    /// Returns the leaf category. Used to lay out the per-announcement / per-element override tree.
    /// </summary>
    public static class ModSettingsRegistry
    {
        public static CategorySetting EnsureCategory(string path, string label, string localizationKey = "")
        {
            var pathParts = path.Split('.');
            var labelParts = label != null ? label.Split('/') : new string[0];
            var locParts = string.IsNullOrEmpty(localizationKey) ? new string[0] : localizationKey.Split('/');

            CategorySetting current = ModSettings.Root;
            for (int i = 0; i < pathParts.Length; i++)
            {
                var key = pathParts[i];
                if (current.GetByKey(key) is CategorySetting existing)
                {
                    current = existing;
                    continue;
                }
                var segLabel = i < labelParts.Length ? labelParts[i].Trim() : key;
                var segLoc = i < locParts.Length ? locParts[i].Trim() : string.Empty;
                var created = new CategorySetting(key, segLabel, localizationKey: segLoc);
                current.Add(created);
                current = created;
            }
            return current;
        }
    }
}
