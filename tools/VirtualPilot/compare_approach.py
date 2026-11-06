"""Compare two VirtualPilot approach runs scenario by scenario.

    python tools/VirtualPilot/compare_approach.py BEFORE.results.txt AFTER.results.txt

Reads the VG| lines `approach` writes and prints BETTER and WORSE counts per measure, with up to six
examples each, the way compare.py does for landings, then CHANGED: moves with no single right
direction (the touchdown point along the runway, an end reason changing between two non-touchdown
outcomes, the first joining decision, the vertical warnings' and "Flare" counts and where they came,
when the joining hold began). Two runs of the same build print BETTER {}, WORSE {} and CHANGED {}: the
harness is deterministic. Measures written NA on either side are not compared, except that a cue
asking for a climb on final or in the flare (maxTargetClimbDeg or maxFlareClimbDeg above 0.05) is worse
even against NA.
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
    # A landing firmer by more than 100 fpm at wheel contact.
    rise(name, x, y, 'touchdownVsFpm', lambda b, a: a - b > 100, lambda b, a: b - a > 100, '{:.0f}')
    # Up by 20 %.
    rise(name, x, y, 'pitchJitterDeg', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.4f}')
    rise(name, x, y, 'pitchRateRmsDegS', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.4f}')
    rise(name, x, y, 'bankRateRmsDegS', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.4f}')
    rise(name, x, y, 'bankCalloutsPerMin', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.2f}')

    # Gust runs: the first callout after a knock later by more than 0.3 s; recovery slower by 20 %;
    # the cross-track after a knock up by 10 % and 15 ft.
    rise(name, x, y, 'gustCalloutDelayS', lambda b, a: a - b > 0.3, lambda b, a: b - a > 0.3, '{:.2f}')
    rise(name, x, y, 'gustRecoveryS', lambda b, a: a > b * 1.2, lambda b, a: a < b * 0.8, '{:.2f}')
    rise(name, x, y, 'gustXteMaxFt',
         lambda b, a: a > b * 1.1 and a - b > 15,
         lambda b, a: a < b / 1.1 and b - a > 15)

    # A command that starts running away from the pilot reads as a smaller magnitude, so a sign
    # change is worse, whatever the magnitudes do, unless the new value is within 0.1 of zero: the
    # vertical path (visual guidance stage 5) aims at near zero, and a sign flip there is noise.
    b, a = num(x, 'selfFeedback10s'), num(y, 'selfFeedback10s')
    if b is not None and a is not None:
        detail = f'{b:.3f} -> {a:.3f}'
        if sign(a) != sign(b) and abs(a) > 0.1:
            note(worse, 'selfFeedback10s sign changed', name, detail)
        elif abs(a) - abs(b) > 0.1:
            note(worse, '|selfFeedback10s| up', name, detail)
        elif abs(b) - abs(a) > 0.1:
            note(better, '|selfFeedback10s| down', name, detail)

    # Joining final (stage 4): the first decision changing has no single right direction. Lined up
    # more than 0.5 nm closer in, ending inside the minimum final (6 nm, 1 nm for a light run), is
    # worse; the mirror is better.
    if x.get('decided', 'NA') != 'NA' and y.get('decided', 'NA') != 'NA' and x['decided'] != y['decided']:
        note(changed, 'decided', name, f"{x['decided']} -> {y['decided']}")
    minimum_final = 1.0 if 'LIGHT' in name else 6.0
    b, a = num(x, 'establishedAlongNm'), num(y, 'establishedAlongNm')
    if b is not None and a is not None:
        if b - a > 0.5 and a < minimum_final:
            note(worse, 'establishedAlongNm down', name, f'{b:.2f} -> {a:.2f}')
        elif a - b > 0.5 and b < minimum_final:
            note(better, 'establishedAlongNm up', name, f'{b:.2f} -> {a:.2f}')

    # The vertical path (stage 5). Farther off the path when established, or at the stable gate,
    # by more than 30 ft is worse; the overshoot of the path after joining it up by more than 30 ft.
    for key in ('joinPathErrFt', 'gateErrFt'):
        b, a = num(x, key), num(y, key)
        if b is not None and a is not None:
            if abs(a) - abs(b) > 30:
                note(worse, '|' + key + '| up', name, f'{b:.0f} -> {a:.0f}')
            elif abs(b) - abs(a) > 30:
                note(better, '|' + key + '| down', name, f'{b:.0f} -> {a:.0f}')
    rise(name, x, y, 'pathOvershootFt', lambda b, a: a - b > 30, lambda b, a: b - a > 30, '{:.0f}')
    # A cue that asks for a climb on final is worse whatever it was before, so it is flagged even
    # against an app that did not report its target (NA before).
    # The flare's cue (stage 6) the same way.
    for key, asks, no_longer in (('maxTargetClimbDeg', 'asks for a climb', 'no longer asks for a climb'),
                                 ('maxFlareClimbDeg', 'flare asks for a climb', 'flare no longer asks for a climb')):
        b, a = num(x, key), num(y, key)
        if a is not None and a > 0.05 and not (b is not None and b > 0.05):
            note(worse, asks, name, ('NA' if b is None else f'{b:.2f}') + f' -> {a:.2f}')
        elif b is not None and b > 0.05 and a is not None and a <= 0.05:
            note(better, no_longer, name, f'{b:.2f} -> {a:.2f}')
    # The joining hold's drift up by 20 % and 20 ft.
    rise(name, x, y, 'holdDriftMaxFt',
         lambda b, a: a > b * 1.2 and a - b > 20,
         lambda b, a: a < b / 1.2 and b - a > 20)
    # The warnings and the hold's start have no single right direction: listed for a person to judge.
    for key in ('tooHighCalls', 'tooLowCalls', 'highAtFinalCalls', 'flareCalls'):
        b, a = num(x, key), num(y, key)
        if b is not None and a is not None and a != b:
            note(changed, key, name, f'{b:.0f} -> {a:.0f}')
    for key, limit in (('firstTooHighAlongNm', 0.1), ('firstTooLowAlongNm', 0.1), ('holdEngageS', 0.5)):
        b, a = num(x, key), num(y, key)
        if b is not None and a is not None and abs(a - b) > limit:
            note(changed, key, name, f'{b:.2f} -> {a:.2f}')

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
