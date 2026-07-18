"""For each UnitProgressionView (PC) instance in a bundle, report which optional
sub-views are actually wired in the prefab (Feats / class-list / Shared list).
If a field is 0 (null PPtr), that instance's RefreshView skips that section."""
import UnityPy, os, sys
B=r"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Bundles"
bundle = sys.argv[1] if len(sys.argv)>1 else "mainmenupcview.res"
env=UnityPy.load(os.path.join(B,bundle))
objs={o.path_id:o for o in env.objects}
def tt(o):
    try:return o.read_typetree()
    except:return None
def pid(p):return p.get("m_PathID",0) if isinstance(p,dict) else getattr(p,"path_id",0)
ms={};go_name={};mb_go={}
for o in env.objects:
    n=o.type.name
    if n=="MonoScript":
        d=tt(o); ms[o.path_id]=d.get("m_ClassName","?") if d else "?"
    elif n=="GameObject":
        d=tt(o)
        if d: go_name[o.path_id]=d.get("m_Name","?")
    elif n=="MonoBehaviour":
        d=tt(o)
        if d: mb_go[o.path_id]=pid(d.get("m_GameObject"))
def cls_of(p):
    o=objs.get(p); d=tt(o) if o else None
    return ms.get(pid(d.get("m_Script")),"") if d else ""
def go_for_comp(p): return go_name.get(mb_go.get(p), f"?{p}") if p else "(null)"
# Walk up transform parents to find an ancestor GO matching a script name (rough "context" finder)
go_tr={}; tr_go={}; tr_father={}
for o in env.objects:
    if o.type.name in("Transform","RectTransform"):
        d=tt(o)
        if d:
            g=pid(d.get("m_GameObject")); go_tr[g]=o.path_id; tr_go[o.path_id]=g
            tr_father[o.path_id]=pid(d.get("m_Father"))
def ancestors_of_go(gid):
    out=[]
    tid=go_tr.get(gid)
    while tid:
        out.append(go_name.get(tr_go.get(tid),"?"))
        ntid=tr_father.get(tid,0)
        if ntid==0 or ntid==tid: break
        tid=ntid
    return out
def context_label(gid):
    # find the nearest ancestor whose name suggests a phase or screen
    anc=ancestors_of_go(gid)
    hints=[a for a in anc if any(k in a for k in ("Phase","CharGen","UnitProgression","ServiceWindow","CharacterInfo","Mechanic","Levelup","Short","DetailedView","ContentWrapper"))]
    return " / ".join(hints[:4]) or " / ".join(anc[:4])
# Find UnitProgressionView/PCView MBs, report per-instance wiring
print(f"=== {bundle} ===")
for o in env.objects:
    if o.type.name!="MonoBehaviour": continue
    c=cls_of(o.path_id)
    if c not in ("UnitProgressionView","UnitProgressionPCView"): continue
    d=tt(o)
    feat=pid(d.get("m_FeatProgressionView"))
    classes=pid(d.get("m_WidgetListClasses"))
    shared=pid(d.get("m_WidgetListSharedProgressions"))
    lvls=pid(d.get("m_LevelProgressionView"))
    host=go_for_comp(o.path_id)
    ctx=context_label(mb_go.get(o.path_id,0))
    print(f"\n[{c}] '{host}'  context: {ctx}")
    print(f"  m_LevelProgressionView      -> {go_for_comp(lvls)}")
    print(f"  m_FeatProgressionView       -> {go_for_comp(feat)}")
    print(f"  m_WidgetListClasses         -> {go_for_comp(classes)}")
    print(f"  m_WidgetListSharedProgressions -> {go_for_comp(shared)}")
