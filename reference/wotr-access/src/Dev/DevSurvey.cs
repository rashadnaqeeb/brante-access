#if DEBUG
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Area;
using Kingmaker.EntitySystem.Persistence;
using Kingmaker.UI._ConsoleUI.Overtips;
using Newtonsoft.Json;
using Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar;
using UnityEngine;
using WrathAccess.Exploration;

namespace WrathAccess.Dev
{
    /// <summary>
    /// The survey half of the environmental-descriptions authoring pipeline
    /// (docs/design/environmental-descriptions.md): tools/survey.py drives these methods through the
    /// dev server's /eval as one-liners, so all the logic lives HERE, compile-checked, with direct
    /// access to the mod's internals (RoomMap grid, WorldModel, EnvDescriptions) instead of fragile
    /// eval-string reflection. PUBLIC class because Mono.CSharp eval sees only public members
    /// (see <see cref="DevApi"/>); DEBUG-only like the rest of the dev subsystem.
    ///
    /// Every method returns a JSON string (the driver reads the "=> ..." eval result line).
    /// <see cref="Frame"/> saves camera + fog state on first use; <see cref="Restore"/> puts it back.
    /// </summary>
    public static class DevSurvey
    {
        // ---- read-only queries ----

        /// <summary>Current area: blueprint name (= the descriptions file name) + display name.</summary>
        public static string Area()
        {
            var area = Game.Instance != null ? Game.Instance.CurrentlyLoadedArea : null;
            if (area == null) return Err("no area loaded");
            return JsonConvert.SerializeObject(new
            {
                blueprint = area.name,
                display = TextUtil.StripRichText(area.AreaDisplayName),
            });
        }

        /// <summary>Every area blueprint in the game with its display name — pick a survey target
        /// without needing a save anywhere near it.</summary>
        public static string Areas()
        {
            var list = new List<object>();
            foreach (var a in Kingmaker.Cheats.Utilities.GetScriptableObjects<BlueprintArea>())
                if (a != null)
                    list.Add(new { blueprint = a.name, display = TextUtil.StripRichText(a.AreaDisplayName) });
            return JsonConvert.SerializeObject(new { areas = list });
        }

        /// <summary>Teleport the session into ANY area by blueprint name (the cheat console's transfer
        /// path — GameHelper.EnterToArea via the area's enter point; no save in the area needed, no
        /// autosave written). Asynchronous: poll <see cref="Status"/> until the area matches, loading
        /// is done, and the room map is built. On-enter cutscenes/etudes of story areas may fire —
        /// harmless for capturing permanent scene dressing, but expect a non-story unit population.</summary>
        public static string EnterArea(string blueprintName)
        {
            var bp = Kingmaker.Cheats.Utilities.GetBlueprintByName<BlueprintArea>(blueprintName);
            if (bp == null) return Err("no area blueprint named '" + blueprintName + "'");
            if (Game.Instance != null && Game.Instance.CurrentlyLoadedArea == bp)
                return JsonConvert.SerializeObject(new { ok = true, note = "already there" });
            // Not Utilities.GetEnterPoint: its lambda dereferences .Area unguarded, and at least one
            // enter-point blueprint in the cache has a null area reference (live NRE, 2026-07-08).
            BlueprintAreaEnterPoint ep = null;
            foreach (var p in Kingmaker.Cheats.Utilities.GetScriptableObjects<BlueprintAreaEnterPoint>())
                if (p != null && ReferenceEquals(p.Area, bp)) { ep = p; break; }
            if (ep == null) return Err("area has no enter point");
            Kingmaker.Designers.GameHelper.EnterToArea(ep, AutoSaveMode.None);
            return JsonConvert.SerializeObject(new { ok = true });
        }

        /// <summary>Load/readiness probe for the driver's post-EnterArea poll.</summary>
        public static string Status()
        {
            var lp = LoadingProcess.Instance;
            return JsonConvert.SerializeObject(new
            {
                area = Game.Instance != null && Game.Instance.CurrentlyLoadedArea != null
                    ? Game.Instance.CurrentlyLoadedArea.name : null,
                loading = lp != null && lp.IsLoadingScreenActive,
                roomMap = RoomMap.Ready,
            });
        }

        /// <summary>All RoomMap rooms with survey points: centroid-seeded farthest-point sampling over
        /// the room's own cells, count scaling with area (1 + area/200 m², capped), stopping early when
        /// no candidate is ≥3 m from the picked set (small rooms stay one-shot). Exits included so the
        /// authoring data can mention where openings lead.</summary>
        public static string Rooms(int maxPoints = 4)
        {
            if (!RoomMap.Ready || !RoomMap.TryGetGrid(out var label, out _, out int w, out int _))
                return Err("no room map (area still loading?)");

            var rooms = new List<object>();
            var cells = new List<int>();
            foreach (var room in RoomMap.Rooms)
            {
                int lbl = room.Id - 1; // ids are the label indices, 1-based
                cells.Clear();
                for (int i = 0; i < label.Length; i++) if (label[i] == lbl) cells.Add(i);
                if (cells.Count == 0) continue;

                int want = Mathf.Clamp(1 + (int)(room.Area / 200f), 1, maxPoints);
                var picked = new List<int>();
                int seed = cells[0]; float bd = float.MaxValue;
                foreach (var i in cells)
                {
                    var p = RoomMap.CellCenter(i % w, i / w);
                    float dx = p.x - room.Centroid.x, dz = p.z - room.Centroid.z, d = dx * dx + dz * dz;
                    if (d < bd) { bd = d; seed = i; }
                }
                picked.Add(seed);
                while (picked.Count < want)
                {
                    int best = -1; float bestMin = -1f;
                    foreach (var i in cells)
                    {
                        var p = RoomMap.CellCenter(i % w, i / w);
                        float mn = float.MaxValue;
                        foreach (var s in picked)
                        {
                            var q = RoomMap.CellCenter(s % w, s / w);
                            float dx = p.x - q.x, dz = p.z - q.z, d = dx * dx + dz * dz;
                            if (d < mn) mn = d;
                        }
                        if (mn > bestMin) { bestMin = mn; best = i; }
                    }
                    if (best < 0 || bestMin < 9f) break; // nothing ≥3 m from the picked set
                    picked.Add(best);
                }

                var pts = new List<object>();
                foreach (var i in picked)
                {
                    var p = RoomMap.CellCenter(i % w, i / w);
                    pts.Add(new { x = R(p.x), y = R(p.y), z = R(p.z) });
                }
                var exits = new List<object>();
                foreach (var e in room.Exits)
                    exits.Add(new { x = R(e.Position.x), y = R(e.Position.y), z = R(e.Position.z), to = e.To != null ? e.To.Id : 0 });
                rooms.Add(new
                {
                    id = room.Id,
                    cls = room.ClassKey,
                    area = R(room.Area),
                    cx = R(room.Centroid.x), cy = R(room.Centroid.y), cz = R(room.Centroid.z),
                    points = pts,
                    exits,
                });
            }
            return JsonConvert.SerializeObject(new { rooms });
        }

        /// <summary>A room's live contents, pre-categorized for the stage-not-actors rule: map objects
        /// (stable, describable, with normalized asset keys + whether the global table already has text)
        /// vs units (dynamic — LISTED so authoring can consciously exclude them, never described).</summary>
        public static string Contents(int roomId)
        {
            var objects = new List<object>();
            var units = new List<object>();
            foreach (var it in WorldModel.Items)
            {
                Vector3 p;
                string name;
                try { p = it.Position; name = it.Name; }
                catch { continue; } // a proxy mid-despawn shouldn't sink the dump
                var room = RoomMap.RoomAt(p);
                if (room == null || room.Id != roomId) continue;
                if (it.IsUnit) units.Add(new { name });
                else objects.Add(new
                {
                    name,
                    asset = it.AssetKey,
                    kind = it.Primary,
                    x = R(p.x), y = R(p.y), z = R(p.z),
                    described = EnvDescriptions.HasAssetText(it.AssetKey),
                });
            }
            return JsonConvert.SerializeObject(new { objects, units });
        }

        /// <summary>The area's unique describable assets: every distinct normalized asset key among
        /// non-unit scan items, with one representative instance (for framing a capture), an instance
        /// count, and whether _assets.json already covers it (the driver skips those by default).</summary>
        public static string Assets()
        {
            var seen = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in WorldModel.Items)
            {
                if (it.IsUnit) continue;
                string key;
                Vector3 p;
                string name;
                try { key = it.AssetKey; p = it.Position; name = it.Name; }
                catch { continue; }
                if (string.IsNullOrEmpty(key)) continue;
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                if (!seen.ContainsKey(key))
                    seen[key] = new
                    {
                        asset = key,
                        name,
                        x = R(p.x), y = R(p.y), z = R(p.z),
                        described = EnvDescriptions.HasAssetText(key),
                    };
            }
            var list = new List<object>();
            foreach (var kv in seen)
                list.Add(new { entry = kv.Value, count = counts[kv.Key] });
            return JsonConvert.SerializeObject(new { assets = list });
        }

        /// <summary>Which room a world point resolves to (-1 = none) — the validate mode's anchor check.</summary>
        public static string RoomIdAt(float x, float y, float z)
        {
            var room = RoomMap.RoomAt(new Vector3(x, y, z));
            return JsonConvert.SerializeObject(new { id = room != null ? room.Id : -1 });
        }

        // ---- camera / fog (stateful) ----

        private static bool _saved;
        private static Vector3 _savedPos;
        private static float _savedYaw, _savedZoom;
        private static readonly List<KeyValuePair<FogOfWarArea, bool>> _savedFog
            = new List<KeyValuePair<FogOfWarArea, bool>>();
        private static readonly List<KeyValuePair<FogOfWarArea, bool>> _savedFogEnabled
            = new List<KeyValuePair<FogOfWarArea, bool>>();

        /// <summary>Aim the camera for a capture: fog cheat-revealed, immediate scroll to the point,
        /// held yaw (135 = canonical: north upper-right), normalized zoom (0 = in, 1 = out; the zoom
        /// smooths over a few frames — the driver waits before /screenshot). Saves the previous camera +
        /// fog state on FIRST use so <see cref="Restore"/> can undo the whole session.</summary>
        public static string Frame(float x, float y, float z, float yaw = 135f, float zoom = 0.5f)
        {
            var rig = Game.Instance != null && Game.Instance.UI != null ? Game.Instance.UI.GetCameraRig() : null;
            if (rig == null) return Err("no camera rig");
            if (!_saved)
            {
                _saved = true;
                _savedPos = rig.transform.position;
                _savedYaw = rig.transform.rotation.eulerAngles.y;
                _savedZoom = rig.CameraZoom != null ? rig.CameraZoom.CurrentNormalizePosition : 0f;
                _savedFog.Clear();
                _savedFogEnabled.Clear();
                // FindObjectsOfType, not All: disabled areas drop out of the live set, and a prior
                // (crashed/foreign) fog-off must still be capturable as state to restore.
                foreach (var f in UnityEngine.Object.FindObjectsOfType<FogOfWarArea>())
                    if (f != null)
                    {
                        _savedFog.Add(new KeyValuePair<FogOfWarArea, bool>(f, f.IsCheatOffFog));
                        _savedFogEnabled.Add(new KeyValuePair<FogOfWarArea, bool>(f, f.enabled));
                    }
            }
            // IsCheatOffFog alone only lifts the fog OVERLAY — UNEXPLORED terrain still renders pure
            // black (a whole survey session's mystery, 2026-07-08). Disabling the FogOfWarArea itself
            // (what photo mode's IsDisableFogOfWar does) makes the renderer draw everything.
            // SNAPSHOT first: disabling an area removes it from the live All set mid-enumeration.
            var fogs = new List<FogOfWarArea>(FogOfWarArea.All);
            foreach (var f in fogs)
                if (f != null) { f.IsCheatOffFog = true; f.enabled = false; }
            SetUiHidden(true);
            rig.SetRotation(yaw);
            if (rig.CameraZoom != null) rig.CameraZoom.CurrentNormalizePosition = zoom;
            rig.ScrollToImmediately(new Vector3(x, y, z));
            return JsonConvert.SerializeObject(new { ok = true });
        }

        /// <summary>Screen-space labels for the CURRENT frame: every scan item within radius of the
        /// camera target that projects on screen, as image-style coordinates (origin TOP-left — Unity's
        /// bottom-left y is flipped here) — so the authoring pass can put a name on everything visible
        /// without hide-renderer tricks. Units flagged so prose can exclude them at a glance.</summary>
        public static string Labels(float radius = 30f)
        {
            var rig = Game.Instance != null && Game.Instance.UI != null ? Game.Instance.UI.GetCameraRig() : null;
            var cam = rig != null ? rig.Camera : Camera.main;
            if (cam == null) return Err("no camera");
            var center = rig != null ? rig.transform.position : Vector3.zero;
            var labels = new List<object>();
            foreach (var it in WorldModel.Items)
            {
                Vector3 p;
                string name;
                string asset;
                bool unit;
                try { p = it.Position; name = it.Name; asset = it.AssetKey; unit = it.IsUnit; }
                catch { continue; }
                float dx = p.x - center.x, dz = p.z - center.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > radius) continue;
                var sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) continue;
                // room lets authoring attribute a labeled thing to ITS room — the radius reaches
                // through walls (a neighbouring room's puzzle buttons ended up in the wrong prose).
                var itemRoom = RoomMap.RoomAt(p);
                labels.Add(new
                {
                    name,
                    asset,
                    unit,
                    room = itemRoom != null ? itemRoom.Id : -1,
                    sx = (int)sp.x,
                    sy = Screen.height - (int)sp.y, // image coords: origin top-left
                    d = R(d),
                });
            }
            return JsonConvert.SerializeObject(new { w = Screen.width, h = Screen.height, labels });
        }

        /// <summary>Undo the survey session: restore each fog area's cheat flag and the camera's
        /// position/yaw/zoom captured at the first <see cref="Frame"/>.</summary>
        public static string Restore()
        {
            // UI-unhide runs UNCONDITIONALLY: a Frame in a restarted/foreign session can leave the UI
            // hidden with _saved false, and a player staring at a working game with an invisible HUD
            // (and an un-OCR-able pause menu) has no way to know why. Never gate this on _saved.
            SetUiHidden(false);
            if (!_saved) return JsonConvert.SerializeObject(new { ok = true, note = "ui unhidden; no camera/fog state to restore" });
            foreach (var kv in _savedFog)
                if (kv.Key != null) kv.Key.IsCheatOffFog = kv.Value;
            foreach (var kv in _savedFogEnabled)
                if (kv.Key != null) kv.Key.enabled = kv.Value;
            var rig = Game.Instance != null && Game.Instance.UI != null ? Game.Instance.UI.GetCameraRig() : null;
            if (rig != null)
            {
                rig.SetRotation(_savedYaw);
                if (rig.CameraZoom != null) rig.CameraZoom.CurrentNormalizePosition = _savedZoom;
                rig.ScrollToImmediately(_savedPos);
            }
            _saved = false;
            _savedFog.Clear();
            return JsonConvert.SerializeObject(new { ok = true });
        }

        // ---- helpers ----

        /// <summary>UI-free captures. The photo-mode SetHideHUD only strips world-camera layers — the
        /// HUD actually renders through the dedicated UICamera, so we disable THAT (kills every
        /// screen-space-camera canvas) plus the MainCanvas group (covers overlay-mode panels), plus
        /// overtips and the photo-mode layers for good measure. IMGUI overlays (the leftover UMM
        /// window) draw with no camera at all — reflected shut separately.</summary>
        private static void SetUiHidden(bool hidden)
        {
            var cam = Kingmaker.UI.UICamera.Instance != null
                ? Kingmaker.UI.UICamera.Instance.GetComponent<Camera>() : null;
            if (cam != null) cam.enabled = !hidden;
            var group = Kingmaker.Assets.UI.MainCanvas.Instance != null
                ? Kingmaker.Assets.UI.MainCanvas.Instance.CanvasGroup : null;
            if (group != null) group.alpha = hidden ? 0f : 1f;
            Game.Instance?.PhotoModeController?.SetHideHUD(hidden);
            OvertipsView.ShowOvertips(!hidden);
            if (hidden) CloseUmmWindow();
        }

        // The retired UMM doorstop is still injected on this machine and pops its IMGUI window every
        // boot, photobombing captures. Reflect it closed; harmless no-op when UMM isn't present.
        private static void CloseUmmWindow()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.IndexOf("UnityModManager", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var ui = asm.GetType("UnityModManagerNet.UnityModManager+UI");
                    var inst = ui?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                    var toggle = ui?.GetMethod("ToggleWindow", new[] { typeof(bool) });
                    if (inst != null && toggle != null) toggle.Invoke(inst, new object[] { false });
                    return;
                }
            }
            catch { /* cosmetic only — never sink a capture over it */ }
        }

        private static float R(float v) => (float)Math.Round(v, 2);
        private static string Err(string msg) => JsonConvert.SerializeObject(new { error = msg });
    }
}
#endif
