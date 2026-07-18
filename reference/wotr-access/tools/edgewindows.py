import UnityPy, os
B=r"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Bundles"
env=UnityPy.load(os.path.join(B,"mainmenupcview.res"))
objs={o.path_id:o for o in env.objects}
def tt(o):
    try:return o.read_typetree()
    except:return None
def pid(p):return p.get("m_PathID",0) if isinstance(p,dict) else getattr(p,"path_id",0)
ms={};go_name={};comp_go={}
for o in env.objects:
    if o.type.name=="MonoScript":
        d=tt(o);ms[o.path_id]=d.get("m_ClassName","?") if d else "?"
for o in env.objects:
    if o.type.name=="GameObject":
        d=tt(o)
        if d: go_name[o.path_id]=d.get("m_Name","?")
# map MonoBehaviour path_id -> its GameObject (via m_GameObject)
mb_go={}
for o in env.objects:
    if o.type.name=="MonoBehaviour":
        d=tt(o)
        if d: mb_go[o.path_id]=pid(d.get("m_GameObject"))
def go_of(pathid):  # GameObject name for a component path_id
    g=mb_go.get(pathid); return go_name.get(g, f"?{pathid}")
# find EdgeWindowView MBs, read their custom fields if typetree present
for o in env.objects:
    if o.type.name!="MonoBehaviour": continue
    d=tt(o)
    if not d: continue
    cls=ms.get(pid(d.get("m_Script")),"")
    if "EdgeWindow" not in cls and "CharGenProgressionPCView" not in cls and "CharGenSpellbook" not in cls: continue
    keys=[k for k in d.keys() if k.lower().find("button")>=0 or k.lower().find("name")>=0 or k.lower().find("edge")>=0 or k.lower().find("window")>=0]
    host=go_of(o.path_id)
    info={}
    for k in keys:
        v=d[k]
        if isinstance(v,dict) and "m_PathID" in v:
            info[k]=go_of(pid(v))  # PPtr -> referenced component's GameObject
        else:
            info[k]=v
    print(f"[{cls}] on GO '{host}': {info}")
