"""Recover the localization Key for each CombatLogPCView channel toggle, by following
its LocalizedUIText.Text -> SharedStringAsset -> LocalizedString.Key."""
import UnityPy, os, sys, json
B = r"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Bundles"
bundle = sys.argv[1] if len(sys.argv) > 1 else "ingamepcview.res"
env = UnityPy.load(os.path.join(B, bundle))
objs = {o.path_id: o for o in env.objects}

def tt(o):
    try: return o.read_typetree()
    except: return None
def pid(p): return p.get("m_PathID", 0) if isinstance(p, dict) else getattr(p, "path_id", 0)

ms = {}; go_name = {}
for o in env.objects:
    n = o.type.name; d = tt(o)
    if not d: continue
    if n == "MonoScript": ms[o.path_id] = d.get("m_ClassName", "?")
    elif n == "GameObject": go_name[o.path_id] = d.get("m_Name", "?")

def cls_of(p):
    o = objs.get(p)
    if not o: return "?"
    if o.type.name != "MonoBehaviour": return o.type.name
    d = tt(o); return ms.get(pid(d.get("m_Script")), "MB?") if d else "MB?"

def comps(gid):
    return [pid(c.get("component") if isinstance(c, dict) else c) for c in (tt(objs.get(gid)) or {}).get("m_Component", [])]

views = [o.path_id for o in env.objects if o.type.name == "MonoBehaviour" and cls_of(o.path_id) == "CombatLogPCView"]
d = tt(objs[views[0]])
dumped_one = False
for i, p in enumerate(d.get("m_ToggleTexts") or []):
    gid = pid(tt(objs[pid(p)]).get("m_GameObject"))
    name = go_name.get(gid, "?")
    # find the LocalizedUIText component on this GO
    key = None; ssa_pid = None
    for cp in comps(gid):
        if cls_of(cp) == "LocalizedUIText":
            lut = tt(objs[cp]); ssa_pid = pid(lut.get("Text"))
            ssa = objs.get(ssa_pid); sd = tt(ssa) if ssa else None
            if sd is not None:
                if not dumped_one:
                    print("SharedStringAsset typetree sample:\n", json.dumps(sd, indent=1, ensure_ascii=False, default=str)[:800], "\n")
                    dumped_one = True
                s = sd.get("String") or sd.get("m_String") or {}
                key = s.get("m_Key") or s.get("Key") or s.get("stringkey")
            break
    print(f"toggle[{i}] GO={name} ssa_pid={ssa_pid} KEY={key}")
