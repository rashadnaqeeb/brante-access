using System;
using System.Collections.Generic;
using System.IO;
using WrathAccess.Exploration.Overlays;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The user's sonar SOUND assignment over the <see cref="ScanTaxonomy"/> tree — the global "sounds"
    /// settings (like the shared volumes; sound identity isn't per-overlay), one dropdown per taxonomy
    /// node over the wav files in assets/audio/interactables, with child nodes additionally offering
    /// Inherit (use the parent category's pick). Was <c>SonarTaxonomy</c>, which duplicated the node
    /// definitions; it now reads the one tree. Each <see cref="ScanItem.Primary"/> is the single
    /// state-aware node that sounds; <see cref="Resolve"/> turns it into a wav stem (or null = silent).
    /// </summary>
    internal static class ScanSounds
    {
        /// <summary>The wav stems available for assignment (assets/audio/interactables/*.wav).</summary>
        private static List<string> AvailableSounds()
        {
            try
            {
                var dir = Path.Combine(OverlayAudio.Dir, "interactables");
                var stems = new List<string>();
                foreach (var f in Directory.GetFiles(dir, "*.wav"))
                    stems.Add(Path.GetFileNameWithoutExtension(f));
                stems.Sort(StringComparer.OrdinalIgnoreCase);
                return stems;
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[sounds] couldn't list interactable sounds: " + e.Message);
                return new List<string>();
            }
        }

        /// <summary>Build the global "sounds" settings tree (called from Main.BuildSettings): one dropdown
        /// per taxonomy node; child nodes additionally offer Inherit. Branch categories (Units, Containers,
        /// Doors) render as a collapsible group whose own pick is "all". Rendered on the Sonar tab.</summary>
        public static void RegisterSettings()
        {
            var stems = AvailableSounds();
            var root = new CategorySetting("sounds", "Sounds", localizationKey: "sounds.title");

            foreach (var cat in ScanTaxonomy.Categories)
            {
                if (cat.IsBranch)
                {
                    var catCat = new CategorySetting(cat.Key, cat.Label, localizationKey: cat.LocKey);
                    // The branch's own pick ("All <category>"), then each child — a nullable choice
                    // inheriting the branch pick (concrete taxonomy defaults stay explicit overrides).
                    var all = new ChoiceSetting("all", cat.Label, Choices(stems), Def(cat, stems),
                        "taxonomy." + cat.Key + ".all");
                    catCat.Add(all);
                    foreach (var child in cat.Children)
                    {
                        var nc = new NullableChoiceSetting(LeafKey(child.Key), child.Label, Choices(stems),
                            defaultId: Def(child, stems),
                            inheritOption: new Choice("inherit", "Inherit", "choice.inherit"),
                            localizationKey: child.LocKey);
                        nc.ResolveInherited = () => all.ValueId;
                        catCat.Add(nc);
                    }
                    root.Add(catCat);
                }
                else
                {
                    root.Add(new ChoiceSetting(cat.Key, cat.Label, Choices(stems), Def(cat, stems), cat.LocKey));
                }
            }

            // World-map entity sounds — separate types (GlobalMapTaxonomy), same tree + dropdowns. Flat per
            // type (no Inherit): one pick each for Locations / Junctions; junctions default Silent.
            foreach (var cat in GlobalMapTaxonomy.Categories)
            {
                var catCat = new CategorySetting(cat.Key, cat.Label, localizationKey: cat.LocKey);
                foreach (var child in cat.Children)
                    catCat.Add(new ChoiceSetting(LeafKey(child.Key), child.Label, Choices(stems), Def(child, stems), child.LocKey));
                root.Add(catCat);
            }

            ModSettings.Root.Add(root);
        }

        // The REAL options (inheritance is the nullable child settings' own state, not a list entry).
        private static List<Choice> Choices(List<string> stems)
        {
            var choices = new List<Choice> { new Choice(ScanTaxonomy.Silent, "Silent", "choice.silent") };
            foreach (var s in stems) choices.Add(new Choice(s, s, "sound." + s));
            return choices;
        }

        // A default that vanished from disk (user removed a wav) falls back to silent.
        private static string Def(ScanTaxonomy.Node n, List<string> stems)
            => n.DefaultSound == ScanTaxonomy.Silent || stems.Contains(n.DefaultSound)
                ? n.DefaultSound : ScanTaxonomy.Silent;

        private static string LeafKey(string key)
        {
            int dot = key.LastIndexOf('.');
            return dot < 0 ? key : key.Substring(dot + 1);
        }

        // Settings path for a node's dropdown: top-level leaf "sounds.<key>"; a branch's own pick
        // "sounds.<key>.all"; a child "sounds.<parent>.<leaf>".
        private static string PathFor(string key)
        {
            if (key.IndexOf('.') >= 0) return "sounds." + key; // child (e.g. "sounds.units.party")
            var node = ScanTaxonomy.Get(key);
            return node != null && node.IsBranch ? "sounds." + key + ".all" : "sounds." + key;
        }

        /// <summary>The sound dropdown setting for a taxonomy node (for the Scanner-tab Entities tree):
        /// a plain <see cref="ChoiceSetting"/> for roots/branch "all" picks, a
        /// <see cref="NullableChoiceSetting"/> for inherit-capable children.</summary>
        public static Setting SoundSetting(string nodeKey)
            => (Setting)ModSettings.GetSetting<ChoiceSetting>(PathFor(nodeKey))
               ?? ModSettings.GetSetting<NullableChoiceSetting>(PathFor(nodeKey));

        /// <summary>The wav stem the given taxonomy node should play, or null for silent/unknown nodes.
        /// Inheritance resolves inside the nullable settings themselves (child → the branch's "all").
        /// Read live (settings dictionary lookups — cheap enough per ping).</summary>
        public static string Resolve(string nodeKey)
        {
            if (nodeKey == null) return null; // items with no sound node (the old loop's entry guard)
            var setting = SoundSetting(nodeKey);
            string id = setting is NullableChoiceSetting nc ? nc.EffectiveId
                : setting is ChoiceSetting c ? c.ValueId : null;
            return id == null || id == ScanTaxonomy.Silent ? null : id;
        }
    }
}
