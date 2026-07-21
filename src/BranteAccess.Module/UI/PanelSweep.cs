using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using ObjectiveInitializer = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveInitializer;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;

namespace BranteAccess.Module.UI
{
    /// <summary>
    /// Generic panel reader for game-composed surfaces with no dedicated model adapter (the
    /// PopupsEnum popup family, generated stat panels, the chapter-transition conversion
    /// panel): visible texts become rows in hierarchy order, visible buttons become
    /// activatable nodes. The rendered text IS the game's own localized output for these
    /// surfaces, so reading the live components is the model read. Sweeps TMP and legacy
    /// uGUI Text both (the game mixes them). A ParameterComponent's name/value/segment
    /// texts fold onto one row with the scale breakdown on Space - one stop per stat.
    /// </summary>
    public static class PanelSweep
    {
        public static void Build(GraphBuilder b, GameObject root, string idPrefix)
        {
            var fold = FoldMap(root);
            var folded = new HashSet<ParameterComponent>();
            b.PushContext("", role: null, positions: false);
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>())
                AddText(b, tmp, tmp.text, idPrefix, fold, folded);
            foreach (var legacy in root.GetComponentsInChildren<UnityEngine.UI.Text>())
                AddText(b, legacy, legacy.text, idPrefix, fold, folded);
            b.PopContext();

            foreach (var btn in root.GetComponentsInChildren<UnityEngine.UI.Button>())
            {
                if (!UiWidgets.Visible(btn.gameObject)) continue;
                // The popup prefabs' full-screen click-away backdrop ("back", image-only,
                // identified live on AchievementPopup): dismissal stays on the labeled
                // Continue, so the backdrop would only add a bare unlabeled stop.
                if (btn.gameObject.name == "back"
                    && string.IsNullOrEmpty(UiWidgets.LabelText(btn.gameObject))) continue;
                AddButton(b, btn, idPrefix);
            }
        }

        // One swept button node - shared with screens that hand-build part of a surface but
        // sweep its loose buttons (the interlude's conversion book).
        internal static void AddButton(GraphBuilder b, UnityEngine.UI.Button btn, string idPrefix)
        {
            var bt = btn;
            b.AddItem(ControlId.Referenced(bt, idPrefix + ":btn:" + bt.GetInstanceID()),
                new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => ButtonLabel(bt.gameObject),
                            kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(
                            () => UiWidgets.Interactable(bt.gameObject)
                                ? null : Loc.T("state.unavailable"),
                            kind: AnnouncementKinds.Enabled),
                    },
                    OnActivate = () =>
                    {
                        if (!UiWidgets.Interactable(bt.gameObject))
                        {
                            Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                            return;
                        }
                        UiWidgets.Click(bt.gameObject);
                    },
                });
        }

        // The panel's readable content as one string (rows in hierarchy order, button labels
        // excluded) - the delivery announcement for panels that appear or swap under the player.
        public static string JoinVisible(GameObject root)
        {
            var fold = FoldMap(root);
            var folded = new HashSet<ParameterComponent>();
            var parts = new List<string>();
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>())
                JoinText(parts, tmp, tmp.text, fold, folded);
            foreach (var legacy in root.GetComponentsInChildren<UnityEngine.UI.Text>())
                JoinText(parts, legacy, legacy.text, fold, folded);
            return string.Join(", ", parts.ToArray());
        }

        private static void JoinText(List<string> parts, Component text, string value,
            Dictionary<Component, ParameterComponent> fold, HashSet<ParameterComponent> folded)
        {
            if (!IsRow(text, value)) return;
            ParameterComponent pc;
            if (fold.TryGetValue(text, out pc))
            {
                if (folded.Add(pc)) parts.Add(ParameterLabel(pc));
                return;
            }
            parts.Add(Spoken(UiWidgets.LocalizedLabel(text.gameObject)));
        }

        // The id of the first row Build would create - for silently re-seating focus before a
        // JoinVisible delivery (ControlId equality is structural-key-only, so this matches the
        // built Referenced node).
        public static ControlId FirstTextId(GameObject root, string idPrefix)
        {
            var fold = FoldMap(root);
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>())
                if (IsRow(tmp, tmp.text))
                    return RowId(tmp, idPrefix, fold);
            foreach (var legacy in root.GetComponentsInChildren<UnityEngine.UI.Text>())
                if (IsRow(legacy, legacy.text))
                    return RowId(legacy, idPrefix, fold);
            return null;
        }

        private static ControlId RowId(Component text, string idPrefix,
            Dictionary<Component, ParameterComponent> fold)
        {
            ParameterComponent pc;
            return fold.TryGetValue(text, out pc)
                ? ControlId.Structural(idPrefix + ":param:" + pc.GetInstanceID())
                : ControlId.Structural(idPrefix + ":text:" + text.GetInstanceID());
        }

        // Some rows carry no numeric value (the chapter final's Deaths row), so the name-value
        // pair trims before the segment joins on.
        internal static string ParameterLabel(ParameterComponent pc)
        {
            var head = (pc.Name.text + " " + pc.TextValue.text).TrimEnd();
            return string.IsNullOrEmpty(pc.Descr.text) ? head : head + ", " + pc.Descr.text;
        }

        // A stat row renders as three sibling texts under one ParameterComponent; each maps to
        // its component so the sweep can speak them as a single row. Texts under the component
        // that are not the name/value/segment trio still sweep as their own rows.
        private static Dictionary<Component, ParameterComponent> FoldMap(GameObject root)
        {
            var map = new Dictionary<Component, ParameterComponent>();
            foreach (var pc in root.GetComponentsInChildren<ParameterComponent>())
            {
                if (pc.Name != null) map[pc.Name] = pc;
                if (pc.TextValue != null) map[pc.TextValue] = pc;
                if (pc.Descr != null) map[pc.Descr] = pc;
            }
            return map;
        }

        // Buttons already logged as unlabeled - once per hierarchy path per module
        // generation keeps the log readable while every new offender still surfaces.
        private static readonly HashSet<string> _unlabeledLogged = new HashSet<string>();

        // Image-only pager arrows carry no text anywhere in the game's prefabs; the prefab
        // object names are stable structure, the spoken words come from the mod's table.
        // Any other textless button is a coverage gap: it still activates but speaks a bare
        // role word, so it logs its path for identification (heard live as "button" on a
        // between-scenes popup, 2026-07-18, before this log existed).
        private static string ButtonLabel(GameObject go)
        {
            var label = UiWidgets.LocalizedLabel(go);
            if (!string.IsNullOrEmpty(label)) return label;
            if (go.name == "LeftArrow") return Loc.T("pager.prev");
            if (go.name == "RightArrow") return Loc.T("pager.next");
            var path = HierarchyPath(go);
            if (_unlabeledLogged.Add(path))
                Mod.Log("[panelsweep] unlabeled button swept: " + path);
            return label;
        }

        private static string HierarchyPath(GameObject go)
        {
            var path = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }

        private static string Spoken(string value) => Game.Readouts.DashAsNone(value);

        // A button's label is spoken by the button node, not as a separate row.
        private static bool IsRow(Component text, string value)
            => UiWidgets.Visible(text.gameObject)
            && !string.IsNullOrEmpty(value) && value.Trim().Length != 0
            && text.GetComponentInParent<UnityEngine.UI.Button>() == null;

        private static void AddText(GraphBuilder b, Component text, string value, string idPrefix,
            Dictionary<Component, ParameterComponent> fold, HashSet<ParameterComponent> folded)
        {
            if (!IsRow(text, value)) return;
            ParameterComponent pc;
            if (fold.TryGetValue(text, out pc))
            {
                if (!folded.Add(pc)) return;
                var p = pc;
                var scales = p.GetComponent<ParameterGetSet>();
                b.AddItem(ControlId.Referenced(p, idPrefix + ":param:" + p.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => ParameterLabel(p),
                                kind: AnnouncementKinds.Label),
                        },
                        OnTooltip = scales == null ? (System.Action)null
                            : () => Mod.Speech.Speak(Readouts.ParameterScales(scales.Parameter)),
                    });
                return;
            }
            var t = text;
            // Space on a plain stat text still reads the same scale detail the game's
            // ParameterValueTooltip shows on hover, composed from the parameter asset at
            // speech time. An objective row (the finals-reminder popup's ObjectiveInitializer
            // list) reads its hover tooltip the same way: description plus condition rows.
            var pgs = t.GetComponentInParent<ParameterGetSet>();
            var oi = t.GetComponentInParent<ObjectiveInitializer>();
            System.Action tooltip = null;
            if (pgs != null) tooltip = () => Mod.Speech.Speak(Readouts.ParameterScales(pgs.Parameter));
            else if (oi != null) tooltip = () => Mod.Speech.Speak(Readouts.ObjectiveDetails(oi));
            b.AddItem(ControlId.Referenced(t, idPrefix + ":text:" + t.GetInstanceID()),
                new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        // Localize-aware: a popup shown the same beat it is instantiated still
                        // renders its serialized editor text (seen live: the chapter title
                        // splash spoke Portuguese on first open).
                        new NodeAnnouncement(() => Spoken(UiWidgets.LocalizedLabel(t.gameObject)),
                            kind: AnnouncementKinds.Label),
                    },
                    OnTooltip = tooltip,
                });
        }
    }
}
