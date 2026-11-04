"""Compare two VirtualPilot approach runs scenario by scenario.

    python tools/VirtualPilot/compare_approach.py BEFORE.results.txt AFTER.results.txt

Reads the VG| lines `approach` writes and prints BETTER and WORSE counts per measure, with up to six
examples each, the way compare.py does for landings, then CHANGED: moves with no single right
direction (the touchdown point along the runway, an end reason changing between two non-touchdown
outcomes). Two runs of the same build print BETTER {}, WORSE {} and CHANGED {}: the harness is
deterministic. Measures written NA on either side are not compared.
"""
import collections
import sys


def load(path):
    runs = {}
    for line in open(path, encoding='utf-8', errors='ignore'):
        parts = line.rstrip('\n').split('|')
        if len(parts) < 3 or parts[0] != 'VG':
            continue
        runs[parts[1]] = dict(p.split('=', 1) for p in parts[2:] if '=' in p)
    return runs


def num(run, key):
    value = run.get(key, 'NA')
    return None if value == 'NA' else float(value)


def sign(v):
    return (v > 0) - (v < 0)


before, after = load(sys.argv[1]), load(sys.argv[2])
better, worse, changed = collections.Counter(), collections.Counter(), collections.Counter()
examples = collections.defaultdict(list)


def note(counter, category, name, detail):
    counter[category] += 1
    label = 'BETTER ' if counter is better else 'WORSE ' if counter is worse else 'CHANGED '
    examples[label + category].append(f'{name} {detail}')


def rise(name, x, y, key, worse_if, better_if, fmt='{:.1f}'):
    b, a = num(x, key), num(y, key)
    if b is None or a is None:
        return
    detail = (fmt + ' -> ' + fmt).format(b, a)
    if worse_if(b, a):
        note(worse, key + ' up', name, detail)
    elif better_if(b, a):
        note(better, key + ' down', name, detail)


common = [n for n in before if n in after]
for name in common:
    x, y = before[name], after[name]

    if x.get('established') == 'True' and y.get('established') == 'False':
        note(worse, 'no longer established', name, '')
    if x.get('established') == 'False' and y.get('established') == 'True':
        note(better, 'now established', name, '')

    if x.get('endReason') == 'touchdown' and y.get('endReason') != 'touchdown':
        note(worse, 'no longer touches down', name, '-> ' + y.get('endReason', '?'))
    if x.get('endReason') != 'touchdown' and y.get('endReason') == 'touchdown':
        note(better, 'now touches down', name, x.get('endReason', '?') + ' ->')
    if 'touchdown' not in (x.get('endReason'), y.get('endReason')) and x.get('endReason') != y.get('endReason'):
        note(changed, 'endReason', name, f"{x.get('endReason', '?')} -> {y.get('endReason', '?')}")

    # Where the wheels touched: sideways farther off is worse; along the runway there is no
    # single right answer, so a move of more than 300 ft is listed for a person to judge.
    b, a = num(x, 'touchdownXteFt'), num(y, 'touchdownXteFt')
    if b is not None and a is not None:
        if abs(a) - abs(b) > 25:
            note(worse, '|touchdownXteFt| up', name, f'{b:.0f} -> {a:.0f}')
        elif abs(b) - abs(a) > 25:
            note(better, '|touchdownXteFt| down', name, f'{b:.0f} -> {a:.0f}')
    b, a = num(x, 'touchdownAlongFt'), num(y, 'touchdownAlongFt')
    if b is not None and a is not None and abs(a - b) > 300:
        note(changed, 'touchdownAlongFt moved', name, f'{b:.0f} -> {a:.0f}')

    # Up by more than 10 % AND by more than 15 ft.
    for key in ('finalXteRmsFt', 'pathRmsFt'):
        rise(name, x, y, key,
             lambda b, a: a > b * 1.1 and a - b > 15,
             lambda b, a: a < b / 1.1 and b - a > 15)
    rise(name, x, y, 'overshootFt', lambda b, a: a - b > 100, lambda b, a: b - a > 100, '{:.0f}')
    rise(name, x, y, 'maxDescentFpm', lambda b, a: a - b > 200, lambda b, a: b - a > 200, '{:.0f}')
    # Up by 20 %.
    rise(name, x, y, 'pitchJitterDeg', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.4f}')
    rise(name, x, y, 'pitchRateRmsDegS', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.4f}')
    rise(name, x, y, 'bankRateRmsDegS', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.4f}')
    rise(name, x, y, 'bankCalloutsPerMin', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.2f}')

    # A command that starts running away from the pilot reads as a smaller magnitude, so a sign
    # change is always worse, whatever the magnitudes do.
    b, a = num(x, 'selfFeedback10s'), num(y, 'selfFeedback10s')
    if b is not None and a is not None:
        detail = f'{b:.3f} -> {a:.3f}'
        if sign(a) != sign(b):
            note(worse, 'selfFeedback10s sign changed', name, detail)
        elif abs(a) - abs(b) > 0.1:
            note(worse, '|selfFeedback10s| up', name, detail)
        elif abs(b) - abs(a) > 0.1:
            note(better, '|selfFeedback10s| down', name, detail)

only_before = [n for n in before if n not in after]
only_after = [n for n in after if n not in before]
print(f'compared {len(common)} scenarios; only before {len(only_before)}, only after {len(only_after)}')
print('BETTER', dict(better))
print('WORSE', dict(worse))
print('CHANGED', dict(changed))
for category, lines in examples.items():
    print('==', category, len(lines))
    for line in lines[:6]:
        print('  ', line)
