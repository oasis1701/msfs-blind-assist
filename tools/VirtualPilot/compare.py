import re,collections,sys
def load(f):
    d={}
    for l in open(f,encoding='utf-8',errors='ignore'):
        p=l.rstrip('\n').split('|')
        if len(p)<10: continue
        d['|'.join(p[:5])]={x.split('=',1)[0]:x.split('=',1)[1] for x in p[5:] if '=' in x}
    return d
b=load(sys.argv[1]); a=load(sys.argv[2])
worse=collections.Counter(); better=collections.Counter(); ex=collections.defaultdict(list)
def chosen(k): return k.split('|')[3]
for k in b:
    if k not in a: continue
    x,y=b[k],a[k]; kind=k.split('|')[0]
    if kind=='LANDING':
        bm=x['miss']!='-'; am=y['miss']!='-'
        if bm and not am: better['false miss removed']+=1
        if am and not bm: worse['NEW false miss']+=1; ex['NEW false miss'].append(k+' '+y['missText'])
        if x['arrived']=='True' and y['arrived']!='True': worse['no longer arrives']+=1; ex['no longer arrives'].append(k+' '+y['end'])
        if x['arrived']!='True' and y['arrived']=='True': better['now arrives']+=1
        bo,ao=float(x['off']),float(y['off'])
        if ao>12 and bo<=12: worse['NEW off-pavement']+=1; ex['NEW off-pavement'].append(k+f' {bo:.0f}->{ao:.0f}')
        if bo>12 and ao<=12: better['off-pavement fixed']+=1
        c=chosen(k)
        bx=('Taxiway '+c) in x['arrival']; ax=('Taxiway '+c) in y['arrival']
        if ax and not bx: better['now arrives at the chosen exit']+=1
        if bx and not ax: worse['no longer arrives at the chosen exit']+=1; ex['no longer arrives at the chosen exit'].append(k+' '+y['arrival'][:50])
    else:
        bmv=float(x['miss']) if x['miss']!='-' else None; amv=float(y['miss']) if y['miss']!='-' else None
        if bmv is not None and amv is None: worse['STRAIGHT: miss no longer called']+=1; ex['STRAIGHT: miss no longer called'].append(k)
        if bmv is not None and amv is not None and amv>bmv+1: worse['STRAIGHT: miss called later']+=1; ex['STRAIGHT: miss called later'].append(f'{amv-bmv:.0f}')
        if bmv is not None and amv is not None:
            tb=re.sub(r', [0-9,]+ metres ahead','',x['missText']); ta=re.sub(r', [0-9,]+ metres ahead','',y['missText'])
            if tb!=ta: worse['STRAIGHT: retarget changed']+=1; ex['STRAIGHT: retarget changed'].append(k+' | '+x['missText']+' -> '+y['missText'])
print('BETTER',dict(better)); print('WORSE',dict(worse))
for c,l in ex.items():
    if c=='STRAIGHT: miss called later':
        v=sorted(float(z) for z in l); print('== later by (m): median',v[len(v)//2],'p90',v[int(len(v)*.9)],'max',v[-1]); continue
    print('==',c,len(l))
    for i in l[:6]: print('  ',i)
