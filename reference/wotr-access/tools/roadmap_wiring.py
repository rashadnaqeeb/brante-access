"""Dump each chargen roadmap entry view's prefab wiring from a UI bundle: the
HasLabels flag + which content fields are wired (null vs GameObject name)."""
import UnityPy, os, sys
B=r"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Bundles"
bundle=sys.argv[1] if len(sys.argv)>1 else "mainmenupcview.res"
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
    o=objs.get(p);d=tt(o) if o else None
    return ms.get(pid(d.get("m_Script")),"") if d else ""
def resolve(v):
    if isinstance(v,dict) and "m_PathID" in v:
        pi=pid(v)
        if pi==0: return "(null)"
        # PPtr to a component -> its GameObject name
        return go_name.get(mb_go.get(pi), go_name.get(pi, f"#{pi}"))
    return v
for o in env.objects:
    if o.type.name!="MonoBehaviour": continue
    c=cls_of(o.path_id)
    if not c.endswith("RoadmapPCView"): continue
    d=tt(o)
    host=go_name.get(mb_go.get(o.path_id),"?")
    print(f"\n=== {c}  (GO {host}) ===")
    for k,v in d.items():
        if k in ("m_GameObject","m_Script","m_Enabled","m_Name"): continue
        # show scalars + PPtr resolution; skip big nested
        if isinstance(v,(bool,int,float,str)):
            print(f"  {k} = {v}")
        elif isinstance(v,dict) and "m_PathID" in v:
            print(f"  {k} -> {resolve(v)}")
