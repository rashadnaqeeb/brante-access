"""Report the visual layout order of the CharacterInfoPCView component views (block order)
by reading their RectTransform parent + sibling index + anchored Y from the prefab."""
import UnityPy, os, sys
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
    o = objs.get(p); d = tt(o) if o else None
    return ms.get(pid(d.get("m_Script")), "?") if d else "?"

go_tr = {}; tr_go = {}; tr_father = {}; tr_children = {}; tr_y = {}
for o in env.objects:
    if o.type.name in ("RectTransform", "Transform"):
        d = tt(o)
        if not d: continue
        g = pid(d.get("m_GameObject")); go_tr[g] = o.path_id; tr_go[o.path_id] = g
        tr_father[o.path_id] = pid(d.get("m_Father"))
        tr_children[o.path_id] = [pid(c) for c in d.get("m_Children", [])]
        ap = d.get("m_AnchoredPosition", {}); tr_y[o.path_id] = ap.get("y", 0) if isinstance(ap, dict) else 0

views = [o.path_id for o in env.objects if o.type.name == "MonoBehaviour" and cls_of(o.path_id) == "CharacterInfoPCView"]
print("CharacterInfoPCView:", views)
FIELDS = ["NameAndPortraitPCView", "m_LevelClassScoresView", "m_AttacksBlockView", "m_DefenceBlockView",
          "m_SkillsBlockView", "m_BuffsAndConditionsView", "m_AbilitiesView", "m_MartialView",
          "m_MartialAttacksBlockView", "m_AlignmentWheelView", "m_AlignmentHistoryView", "m_StoriesView",
          "NameFullPortraitPCView", "m_BiographyAlignmentHistoryView", "m_BiographyStoriesView", "m_ProgressionView"]
for v in views:
    d = tt(objs[v])
    rows = []
    for f in FIELDS:
        p = pid(d.get(f))
        if not p: continue
        cd = tt(objs.get(p)); g = pid(cd.get("m_GameObject")) if cd else 0
        tr = go_tr.get(g); father = tr_father.get(tr, 0)
        sibs = tr_children.get(father, [])
        sib = sibs.index(tr) if tr in sibs else -1
        rows.append((f, go_name.get(g, "?"), go_name.get(tr_go.get(father), "?"), sib, tr_y.get(tr, 0)))
    for r in sorted(rows, key=lambda r: (str(r[2]), r[3])):
        print(f"  {r[0]:34} go={r[1]:26} parent={r[2]:24} sib={r[3]:3} y={r[4]}")

# Within-block order: dump the child subtree (sib order) of the Summary block GameObjects.
def subtree(go_id, depth, indent=0):
    tr = go_tr.get(go_id)
    if tr is None: return
    for ch in tr_children.get(tr, []):
        g = tr_go.get(ch)
        print("    " + "  " * indent + f"- {go_name.get(g, '?')} (y={tr_y.get(ch, 0)})")
        if depth > 1: subtree(g, depth - 1, indent + 1)

want = {"LevelClassScores", "ACAndSavingThrow", "Attack", "NamePortrait", "Skills"}
print("\n=== within-block child order ===")
for gid, nm in go_name.items():
    if nm in want and go_tr.get(gid):
        print(f"[{nm}]"); subtree(gid, 2)
