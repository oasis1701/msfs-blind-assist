# Diff two VirtualPilot runs scenario by scenario on the finding CODES each produced.
import sys,glob,collections
def load(d):
    r={}
    for f in glob.glob(d+'/*.results.txt'):
        for l in open(f,encoding='utf-8',errors='ignore'):
            if not l.startswith('CODES|'): continue
            _,kind,icao,key,codes=l.rstrip('\n').split('|',4)
            r[(kind,icao,key)]=set() if codes=='OK' else set(codes.split(','))
    return r
a=load(sys.argv[1]); b=load(sys.argv[2])
INFO={'WRONG_TURN_WARNED','WRONG_TURN_REROUTED_NO_RUNWAY','WRONG_TURN_REROUTED_HELD'}
gone=collections.Counter(); new=collections.Counter(); ex=collections.defaultdict(list)
for k in a:
    if k not in b: continue
    for c in a[k]-b[k]-INFO: gone[c]+=1
    for c in b[k]-a[k]-INFO: new[c]+=1; ex[c].append(' '.join(k))
print('scenarios',len(set(a)&set(b)),'(only before',len(set(a)-set(b)),', only after',len(set(b)-set(a)),')')
print('FIXED (code gone):'); [print(f'  {c:30} {n}') for c,n in gone.most_common()]
print('NEW (code appeared):'); [print(f'  {c:30} {n}') for c,n in new.most_common()]
for c,l in ex.items():
    print('==',c); [print('   ',x) for x in l[:int(sys.argv[3]) if len(sys.argv)>3 else 8]]
