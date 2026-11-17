"""Measure a visual guidance flight from its recording.

    python tools/VirtualPilot/analyze_vg_flight.py RECORDING [--segment FROM,TO]

RECORDING is %APPDATA%\\MSFSBlindAssist\\logs\\visual_guidance_flight.log from the app (its rotated
older parts, visual_guidance_flight.1.log to .3.log, are read first, oldest first), or a file the
VirtualPilot harness wrote with `approach --trace NAME --record FILE`. Each line holding "VGREC" is
one record line (VisualGuidanceFlightRecord, VP-2); anything before "VGREC" on the line (the log's
time stamp and tags) is ignored.

The file is split into approaches at each `start` line. For each, it prints the runway, how long
it ran, the last phase, when it was established (within 0.1 nm and 5 degrees of the course for 3 s,
as the harness counts it), and, on the final segment from that moment (by default 5.0 to 0.5 nm
along the course, --segment to change it):

  finalXteRmsFt, finalXteMaxFt   cross-track, feet (+ right of the centreline)
  pathRmsFt                      height against visual guidance's own path (altErr)
  pitchRateRmsDegS, bankRateRmsDegS
                                 how fast the pitch and bank commands moved (RMS, degrees a second)
  followPitchRmsDeg, followBankRmsDeg
                                 how closely the pilot or autopilot flew the commands: the RMS of
                                 command minus actual
  sensor noise (vs, aoa, trk)    the standard deviation of each around its centred 1 s moving
                                 average, on the final segment (the whole approach without one)

and over the whole approach: touchdownVsFpm (the descent rate at the first frame whose wheel
height, alt - elev - flareBias, is at or below 0; the harness ends a run at wheel contact and
records no frame there, so when the last frame is above the runway but the next would reach it,
that next frame is extrapolated from the last two; NA if the wheels never got that low), bank
callouts a minute, and the seconds the vertical cue spent in each mode (Follow, Hold, Plan, Path,
Flare; recordings from before stage 5 of the redesign have no vmode field and read "none"), with
the learned trim (pitch minus flight path) at the end. Numbers are printed in the invariant style,
"12.3".
"""
import math
import os
import re
import sys

FT_PER_NM = 6076.12
ESTABLISHED_NM, ESTABLISHED_DEG, ESTABLISHED_HOLD_S = 0.1, 5.0, 3.0
# "Matched, level" was "On commanded path" before 2026-11-06; both stay, for older recordings.
BANK_CALLOUT = re.compile(r'^(\d+ (left|right)|matched|Matched, level|On commanded path)$')


def recording_files(path):
    """The rotated older parts (highest number oldest) then the file itself."""
    base, ext = os.path.splitext(path)
    older = []
    n = 1
    while os.path.exists(f'{base}.{n}{ext}'):
        older.append(f'{base}.{n}{ext}')
        n += 1
    return list(reversed(older)) + [path]


def parse_line(line):
    i = line.find('VGREC ')
    if i < 0:
        return None
    rest = line[i + 6:].rstrip('\r\n')
    kind, _, fields = rest.partition(' ')
    if kind == 'say':
        head, sep, text = fields.partition(' text=')
        rec = dict(kv.split('=', 1) for kv in head.split() if '=' in kv)
        rec['text'] = text if sep else ''
    else:
        rec = dict(kv.split('=', 1) for kv in fields.split() if '=' in kv)
    rec['kind'] = kind
    return rec


def num(rec, key, default=None):
    """A field as a number; a field an older recording lacks reads `default` when one is given."""
    if default is not None and key not in rec:
        return default
    return float(rec[key])


VERTICAL_MODES = ('Follow', 'Hold', 'Plan', 'Path', 'Flare')


def mode_seconds(frames):
    """Seconds in each vertical mode: each frame counts until the next one. Recordings from before
    stage 5 have no vmode field; their frames count as "none"."""
    seconds = {}
    for a, b in zip(frames, frames[1:]):
        mode = a.get('vmode', 'none')
        seconds[mode] = seconds.get(mode, 0.0) + max(0.0, num(b, 't') - num(a, 't'))
    return seconds


def wrap180(deg):
    return (deg + 180.0) % 360.0 - 180.0


def rms(values):
    values = list(values)
    return math.sqrt(sum(v * v for v in values) / len(values)) if values else float('nan')


def rate_rms(series):
    """RMS of (v[i] - v[i-1]) / (t[i] - t[i-1]), as the harness's ApproachMetrics.RateRms."""
    if len(series) < 2:
        return 0.0
    total, n = 0.0, 0
    for (t0, v0), (t1, v1) in zip(series, series[1:]):
        if t1 > t0:
            total += ((v1 - v0) / (t1 - t0)) ** 2
            n += 1
    return math.sqrt(total / n) if n else 0.0


def noise_sd(series):
    """Standard deviation of a series around its centred 1 s moving average."""
    if len(series) < 3:
        return float('nan')
    resid = []
    lo = hi = 0
    total = 0.0
    for t, v in series:
        while hi < len(series) and series[hi][0] <= t + 0.5:
            total += series[hi][1]
            hi += 1
        while series[lo][0] < t - 0.5:
            total -= series[lo][1]
            lo += 1
        resid.append(v - total / (hi - lo))
    mean = sum(resid) / len(resid)
    return math.sqrt(sum((r - mean) ** 2 for r in resid) / (len(resid) - 1))


def unwrap(series):
    out, offset, prev = [], 0.0, None
    for t, v in series:
        if prev is not None:
            d = v + offset - prev
            if d > 180:
                offset -= 360
            elif d < -180:
                offset += 360
        out.append((t, v + offset))
        prev = v + offset
    return out


def fmt(v, digits=1):
    return 'NA' if v is None or (isinstance(v, float) and math.isnan(v)) else f'{v:.{digits}f}'


def touchdown_sink(frames, elev, flare_bias):
    """The descent rate at the first frame whose wheels are at or below the runway. The harness ends a
    run at wheel contact and records no frame there, so when the last frame is still above the
    runway but the next would reach it, that next frame is extrapolated from the last two (in the
    flare the sink changes several fpm a frame); an approach that never got that low reads NA."""
    def wheel(f):
        return num(f, 'alt') - elev - flare_bias
    contact = next((f for f in frames if wheel(f) <= 0), None)
    if contact is not None:
        return f'touchdownVsFpm={fmt(-num(contact, "vs"), 0)} (at t={fmt(num(contact, "t"), 2)} s, wheels {fmt(wheel(contact))} ft)'
    if len(frames) >= 2:
        a, b = frames[-2], frames[-1]
        # Whether the next frame reaches the runway, from the height change (vs can be noisy).
        if num(b, 't') > num(a, 't') and wheel(b) + (wheel(b) - wheel(a)) <= 0:
            vs_next = 2 * num(b, 'vs') - num(a, 'vs')
            return (f'touchdownVsFpm={fmt(-vs_next, 0)} (one frame past the last, extrapolated; '
                    f'wheels {fmt(wheel(b))} ft at t={fmt(num(b, "t"), 2)} s)')
    return f'touchdownVsFpm=NA (the wheels never reached the runway; lowest {fmt(min(wheel(f) for f in frames))} ft)'


def analyze(app, seg_from, seg_to):
    start, frames, says = app['start'], app['frames'], app['says']
    crs_mag, elev, flare_bias = num(start, 'crsMag'), num(start, 'elev'), num(start, 'flareBias')
    print(f"== approach {start.get('apt', '?')} runway {start.get('rwy', '?')}")
    if not frames:
        print('  no frames')
        return
    duration = num(frames[-1], 't')
    print(f'  durationS={fmt(duration)} frames={len(frames)} lastPhase={frames[-1]["ph"]}')

    established, hold = None, None
    for f in frames:
        t = num(f, 't')
        inside = abs(num(f, 'xte')) <= ESTABLISHED_NM and abs(wrap180(num(f, 'trk') - crs_mag)) <= ESTABLISHED_DEG
        if not inside:
            hold = None
            continue
        if hold is None:
            hold = t
        if t - hold >= ESTABLISHED_HOLD_S:
            established = t
            break
    print(f'  established={"True" if established is not None else "False"} timeToEstablishS={fmt(established)}')

    def along(f):
        d, x = num(f, 'dist'), num(f, 'xte')
        return math.sqrt(max(0.0, d * d - x * x))

    seg = [f for f in frames if established is not None and num(f, 't') >= established and seg_to <= along(f) <= seg_from]
    if seg:
        print(f'  final segment {seg_from:.1f} to {seg_to:.1f} nm: {len(seg)} frames')
        print(f'  finalXteRmsFt={fmt(rms(num(f, "xte") * FT_PER_NM for f in seg))} '
              f'finalXteMaxFt={fmt(max(abs(num(f, "xte")) for f in seg) * FT_PER_NM)} '
              f'pathRmsFt={fmt(rms(num(f, "altErr") for f in seg))}')
        print(f'  pitchRateRmsDegS={fmt(rate_rms([(num(f, "t"), num(f, "dPitch")) for f in seg]), 4)} '
              f'bankRateRmsDegS={fmt(rate_rms([(num(f, "t"), num(f, "dBank")) for f in seg]), 4)}')
        print(f'  followPitchRmsDeg={fmt(rms(num(f, "dPitch") - num(f, "pitch") for f in seg), 3)} '
              f'followBankRmsDeg={fmt(rms(num(f, "dBank") - num(f, "bank") for f in seg), 3)}')
    else:
        print('  no final segment (not established, or never between the segment limits)')

    noise_frames = seg if seg else frames
    vs = [(num(f, 't'), num(f, 'vs')) for f in noise_frames]
    aoa = [(num(f, 't'), num(f, 'aoa')) for f in noise_frames if not math.isnan(num(f, 'aoa'))]
    trk = unwrap([(num(f, 't'), num(f, 'trk')) for f in noise_frames])
    print(f'  noise ({"final segment" if seg else "whole approach"}): vsSdFpm={fmt(noise_sd(vs))} '
          f'aoaSdDeg={fmt(noise_sd(aoa), 3)} trkSdDeg={fmt(noise_sd(trk), 3)}')

    print('  ' + touchdown_sink(frames, elev, flare_bias))

    bank = sum(1 for s in says if BANK_CALLOUT.match(s['text']))
    minutes = max(duration, 1e-9) / 60.0
    print(f'  bankCalloutsPerMin={fmt(bank / minutes, 2)} spokenLines={len(says)}')

    seconds = mode_seconds(frames)
    order = [m for m in VERTICAL_MODES if m in seconds] + sorted(m for m in seconds if m not in VERTICAL_MODES)
    print('  verticalModeS: ' + (' '.join(f'{m}={fmt(seconds[m])}' for m in order) or 'none'))
    trim = num(frames[-1], 'trim', float('nan'))
    print(f'  trimDeg at the end={fmt(trim, 2)}')


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path, seg_from, seg_to = argv[1], 5.0, 0.5
    if '--segment' in argv:
        seg_from, seg_to = (float(x) for x in argv[argv.index('--segment') + 1].split(','))
    approaches, current = [], None
    for name in recording_files(path):
        with open(name, encoding='utf-8', errors='replace') as fh:
            for line in fh:
                rec = parse_line(line)
                if rec is None:
                    continue
                if rec['kind'] == 'start':
                    current = {'start': rec, 'frames': [], 'says': [], 'stopped': None}
                    approaches.append(current)
                elif current is None:
                    continue
                elif rec['kind'] == 'f':
                    current['frames'].append(rec)
                elif rec['kind'] == 'say':
                    current['says'].append(rec)
                elif rec['kind'] == 'stop':
                    current['stopped'] = rec
    if not approaches:
        print('no approach in the recording (no VGREC start line)')
        return 1
    for app in approaches:
        analyze(app, seg_from, seg_to)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
