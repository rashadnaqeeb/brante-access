import UnityPy, os
B=r"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Bundles"
env=UnityPy.load(os.path.join(B,"mainmenupcview.res"))
objs={o.path_id:o for o in env.objects}
def tt(o):
    try:return o.read_typetree()
    except:return None
def pid(p):return p.get("m_PathID",0) if isinstance(p,dict) else getattr(p,"path_id",0)
ms={};go_name={};mb_go={}
for o in env.objects:
    if o.type.name=="MonoScript":
        d=tt(o);ms[o.path_id]=d.get("m_ClassName","?") if d else "?"
    elif o.type.name=="GameObject":
        d=tt(o)
        if d: go_name[o.path_id]=d.get("m_Name","?")
for o in env.objects:
    if o.type.name=="MonoBehaviour":
        d=tt(o)
        if d: mb_go[o.path_id]=pid(d.get("m_GameObject"))
def cls_of(pathid):
    o=objs.get(pathid); d=tt(o) if o else None
    return ms.get(pid(d.get("m_Script")),"") if d else ""
def goname_of(comp_pathid): return go_name.get(mb_go.get(comp_pathid), f"?{comp_pathid}")
def field(comp_pathid, key):
    o=objs.get(comp_pathid); d=tt(o) if o else None
    return d.get(key) if d else None
# For each panel view, follow m_EdgeWindowView -> EdgeWindowView -> m_EdgeWindowButton + m_ButtonName
for o in env.objects:
    if o.type.name!="MonoBehaviour": continue
    cls=cls_of(o.path_id)
    if cls not in ("CharGenProgressionPCView","CharGenSpellbookPCView","CharGenMythicProgressionPCView"): continue
    d=tt(o); ewv=pid(d.get("m_EdgeWindowView"))
    edd=tt(objs.get(ewv)) if ewv in objs else None
    if not edd:
        print(f"{cls}: edge view {ewv} unreadable"); continue
    btn=pid(edd.get("m_EdgeWindowButton"))
    bname=edd.get("m_ButtonName")
    print(f"{cls} (GO {goname_of(o.path_id)}) -> EdgeWindowView -> button GO '{goname_of(btn)}'  label/name={bname!r}")
