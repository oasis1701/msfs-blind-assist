# Summarise a VirtualPilot `turnpicks` run: what became of every Turn pick, and which finding codes
# a flown pick ADDS to (or removes from) the same clearance flown with no pick.
#   py turn_report.py runs/NAME [examples-per-code]
import sys, glob, collections

n_examples = int(sys.argv[2]) if len(sys.argv) > 2 else 6
outcomes = collections.Counter()
legs = []                                   # off-chart leg lengths, metres
base = {}                                   # (icao, plan) -> codes of the clearance with no pick
flights = collections.defaultdict(dict)     # kind -> (icao, plan ## pick) -> codes
for f in glob.glob(sys.argv[1] + '/*.results.txt'):
    for line in open(f, encoding='utf-8', errors='ignore'):
        line = line.rstrip('\n')
        if line.startswith('TURN|'):
            _, icao, rest = line.split('|', 2)
            key, outcome, _length, _base_length, leg, _turned = rest.rsplit('|', 5)
            outcomes[outcome] += 1
            if outcome == 'OFFCHART':
                legs.append(float(leg))
        elif line.startswith('CODES|TURN_'):
            _, kind, icao, rest = line.split('|', 3)
            key, codes = rest.rsplit('|', 1)
            codes = set() if codes == 'OK' else set(codes.split(','))
            if kind == 'TURN_BASE':
                base[(icao, key)] = codes
            else:
                flights[kind][(icao, key)] = codes

total = sum(outcomes.values())
print(f'picks {total} on {len(base)} clearances')
for o, n in outcomes.most_common():
    print(f'  {o:12} {n:6}  {100.0 * n / max(total, 1):5.1f} %')
if legs:
    legs.sort()
    print(f'off-chart legs: median {legs[len(legs) // 2]:.0f} m, 90th percentile {legs[int(len(legs) * 0.9)]:.0f} m, '
          f'longest {legs[-1]:.0f} m, {sum(1 for x in legs if x >= 40)} of {len(legs)} are 40 m or longer')

# Says what happened and is not a defect by itself.
INFO = {'TURN_MISS_SAID_MISSED', 'TURN_MISS_SAID_NOT_KEPT'}
for kind in sorted(flights):
    added, gone, examples = collections.Counter(), collections.Counter(), collections.defaultdict(list)
    for (icao, key), codes in flights[kind].items():
        b = base.get((icao, key.split(' ## ')[0]), set())
        for c in codes - b:
            added[c] += 1
            examples[c].append(f'{icao} {key}')
        for c in b - codes:
            gone[c] += 1
    print(f'\n== {kind}: {len(flights[kind])} scenarios')
    print('  codes the pick adds:')
    for c, n in added.most_common():
        print(f'    {c:34} {n}{"  (info)" if c in INFO else ""}')
    if gone:
        print('  codes the pick removes:')
        for c, n in gone.most_common():
            print(f'    {c:34} {n}')
    for c, n in added.most_common():
        if c in INFO:
            continue
        print(f'  -- {c}')
        for e in examples[c][:n_examples]:
            print(f'       {e}')
