# Diff two VirtualPilot runs scenario by scenario on the ROUTE each loaded (metres, taxiway names,
# a hash of the node ids), and say what the findings did on each changed route.
# py compare_routes.py runs/BEFORE runs/AFTER [n]   (n: routes listed per kind, default all)
import sys,glob,collections
def load(d):
    routes={}; codes={}
    for f in glob.glob(d+'/*.results.txt'):
        for l in open(f,encoding='utf-8',errors='ignore'):
            p=l.rstrip('\n').split('|')
            if p[0]=='ROUTE' and len(p)>=7:
                routes[(p[1],p[2],p[3])]=(float(p[4]),p[5],p[6])
            elif p[0]=='CODES' and len(p)>=5:
                codes[(p[1],p[2],p[3])]=set() if p[4]=='OK' else set(p[4].split(','))
    return routes,codes
ra,ca=load(sys.argv[1]); rb,cb=load(sys.argv[2])
n=int(sys.argv[3]) if len(sys.argv)>3 else None
both=set(ra)&set(rb)
changed=sorted(k for k in both if ra[k][2]!=rb[k][2])
print('routes',len(both),'(only before',len(set(ra)-set(rb)),', only after',len(set(rb)-set(ra)),') changed',len(changed),
      'at',len({k[1] for k in changed}),'airports')
longer=[k for k in changed if rb[k][0]>ra[k][0]+0.5]; shorter=[k for k in changed if rb[k][0]<ra[k][0]-0.5]
print('longer',len(longer),'shorter',len(shorter),'same length',len(changed)-len(longer)-len(shorter))
bykind=collections.defaultdict(list)
for k in changed: bykind[k[0]].append(k)
for kind,ks in sorted(bykind.items()):
    print('==',kind,len(ks))
    for k in ks[:n]:
        gone=sorted(ca.get(k,set())-cb.get(k,set())); new=sorted(cb.get(k,set())-ca.get(k,set()))
        print(f'   {k[1]} {k[2]}')
        print(f'      {ra[k][0]:.0f} m via {ra[k][1]}')
        print(f'   -> {rb[k][0]:.0f} m via {rb[k][1]}'+(f'   gone: {",".join(gone)}' if gone else '')+(f'   new: {",".join(new)}' if new else ''))
