#!/usr/bin/env python3
# Generate C# SimVarDefinition entries + panel mappings for the HS787 gap L:vars.
# Output three snippets to stdout sections: <<DEFS>>, <<STRUCT>>, <<CONTROLS>>.
import re, sys

gap = [l.strip() for l in open('hs787_gap_lvars.txt') if l.strip()]

# Skip non-control/noise tokens that aren't real readouts.
SKIP = {'wt', 'HS_787_DynamicRegistration', 'MSFSBA_IRS_DEBUG'}

# prefix -> panel sub-section
def panel_of(v):
    if v.startswith('WTAP_VNav') or v.startswith('WTAP_VNAV'): return 'VNAV'
    if v.startswith('WTAP_LNav') or v.startswith('WT_LNavData') or v.startswith('WTBoeing_LNavData') or v.startswith('WTAP_LPV'): return 'LNAV and Progress'
    if v.startswith('WTAP_GP'): return 'Glidepath'
    if v.startswith('WTAP_Boeing'): return 'VNAV'
    if v.startswith('WT_FADEC'): return 'Engine Data'
    if v.startswith('WTFltTimer'): return 'Timers'
    if v.startswith('WT_78_'): return 'Flight Control Inputs'
    return 'Other Data'

def keyfor(v):
    return 'HS787_LV_' + re.sub(r'[^A-Za-z0-9]', '_', v)

def display(v):
    s = v
    for p in ('WTAP_Boeing_','WTAP_','WTBoeing_','WT_Boeing_','WT_FADEC_','WT_LNavData_','WTFltTimer_','WT_78_','WT_','B787_10_'):
        if s.startswith(p): s = s[len(p):]; break
    s = s.replace('_', ' ').replace(':', ' ').strip()
    s = re.sub(r'\s+', ' ', s)
    # nice-case but preserve known acronyms
    words = []
    for w in s.split(' '):
        if w.upper() in ('N1','N2','TPR','EGT','FPA','VS','TOD','TOC','BOD','BOC','RNP','XTK','DTK','CDI','GP','GPS','LPV','FAF','IRS','VNAV','LNAV','AP','UTC') or w.isdigit():
            words.append(w.upper())
        elif w in ('VNav','LNav'):
            words.append(w)
        else:
            words.append(w[:1].upper() + w[1:])
    return ' '.join(words)

def valdesc(v):
    u = v.upper()
    if u.endswith('_AMBER') or u.endswith('_RED'):
        return '{ [0] = "Normal", [1] = "Exceedance" }'
    if u.endswith('_AVAILABLE') or u.endswith('_PATH_AVAILABLE'):
        return '{ [0] = "Not available", [1] = "Available" }'
    if '_IS_TRACKING' in u: return '{ [0] = "Not tracking", [1] = "Tracking" }'
    if '_IS_SUSPENDED' in u: return '{ [0] = "Not suspended", [1] = "Suspended" }'
    return None

defs, controls = [], {}
seen = set()
for v in gap:
    if v in SKIP: continue
    k = keyfor(v)
    if k in seen: continue
    seen.add(k)
    vd = valdesc(v)
    lines = [f'            ["{k}"] = new SimConnect.SimVarDefinition',
             '            {',
             f'                Name = "{v}",',
             f'                DisplayName = "{display(v)}",',
             '                Type = SimConnect.SimVarType.LVar,',
             '                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,',
             '                IsAnnounced = false' + (',' if vd else '')]
    if vd:
        lines.append(f'                ValueDescriptions = new Dictionary<double, string> {vd}')
    lines.append('            },')
    defs.append('\n'.join(lines))
    controls.setdefault(panel_of(v), []).append(k)

print('<<DEFS>>')
print('\n'.join(defs))
print('<<STRUCT>>')
# panel sub-sections in a stable order
order = ['VNAV','LNAV and Progress','Glidepath','Engine Data','Flight Control Inputs','Timers','Other Data']
for p in order:
    if p in controls: print(f'                "{p}",')
print('<<CONTROLS>>')
for p in order:
    if p not in controls: continue
    print(f'            ["{p}"] = new List<string>')
    print('            {')
    for k in controls[p]:
        print(f'                "{k}",')
    print('            },')
print('<<END>>')
print(f'// total defs: {len(defs)}', file=sys.stderr)
