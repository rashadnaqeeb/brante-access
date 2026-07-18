import UnityPy, os
B=r"C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure\Bundles"
env=UnityPy.load(os.path.join(B,"mainmenupcview.res"))
objs={o.path_id:o for o in env.objects}
def tt(o):
    try:return o.read_typetree()
    except:return None
def pid(p):return p.get("m_PathID",0) if isinstance(p,dict) else getattr(p,"path_id",0)
go_name={};go_tr={};tr_kids={};tr_go={};ms={}
for o in env.objects:
    if o.type.name=="MonoScript":
        d=tt(o);ms[o.path_id]=d.get("m_ClassName","?") if d else "?"
for o in env.objects:
    n=o.type.name
    if n=="GameObject":
        d=tt(o)
        if d:go_name[o.path_id]=d.get("m_Name","?")
    elif n in("RectTransform","Transform"):
        d=tt(o)
        if d:
            g=pid(d.get("m_GameObject"));go_tr[g]=o.path_id;tr_go[o.path_id]=g
            tr_kids[o.path_id]=[pid(k) for k in d.get("m_Children",[])]
def scripts(gid):
    out=[];o=objs.get(gid);d=tt(o) if o else None
    if d:
        for c in d.get("m_Component",[]):
            cid=pid(c["component"] if isinstance(c,dict) and "component" in c else c)
            co=objs.get(cid)
            if co and co.type.name=="MonoBehaviour":
                cd=tt(co);sid=pid(cd.get("m_Script")) if cd else 0
                if ms.get(sid):out.append(ms[sid])
    return out
def dump(gid,depth,maxd=3):
    tid=go_tr.get(gid);kids=tr_kids.get(tid,[]) if tid is not None else []
    nm=go_name.get(gid,"?");s=scripts(gid);tag=f"  <{','.join(s)}>" if s else ""
    print("  "+"  "*depth+f"- {nm}{tag}")
    if depth>=maxd:return
    # collapse repeated-name runs (e.g. LevelEntry)
    i=0
    while i<len(kids):
        kg=tr_go.get(kids[i]);knm=go_name.get(kg,"?")
        j=i
        while j<len(kids) and go_name.get(tr_go.get(kids[j]),"?")==knm: j+=1
        if j-i>3:
            print("  "+"  "*(depth+1)+f"- [{knm} x{j-i}]"); i=j; continue
        if kg:dump(kg,depth+1,maxd)
        i+=1
roots=[gid for gid,nm in go_name.items() if "CharGenPCView" in scripts(gid)]
for r in roots:
    print(f"### {go_name.get(r)} (CharGenPCView) — layout ###")
    dump(r,0,maxd=3)
