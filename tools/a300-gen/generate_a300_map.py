"""Generates the iniBuilds A300-600 control map that MSFS Blind Assist embeds.

Reads the aircraft's compiled cockpit behavior XML and its en-US.locPak tooltip file, and writes
one JSON entry per cockpit input event: where it sits, what the aircraft calls it, how the app
writes it (the A300's own B: Set event), which variable holds its position and what each position
is called.

Deterministic: the same package always produces byte-identical output.

    python tools/a300-gen/generate_a300_map.py --package "<Community folder>\\inibuilds-aircraft-a300"

writes MSFSBlindAssist/Resources/a300_control_map.json (use --out to write elsewhere). Review a
regeneration with diff_maps.py before committing it.
"""
import argparse
import json
import os
import re
import sys
from collections import OrderedDict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from behavior_xml import Behavior

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
DEFAULT_OUT = os.path.join(REPO_ROOT, 'MSFSBlindAssist', 'Resources', 'a300_control_map.json')
BEHAVIOR_FILE = os.path.join('SimObjects', 'Airplanes', 'A300-600', 'attachments', 'inibuilds',
                             'Function_Interior_Freighter', 'model', 'A300_Interior.behavior.xml')
LOC_FILE = 'en-US.locPak'
PREFIX = 'AIRLINER_'

# Kinds the generator emits (A300Kinds in the app mirrors these strings).
TOGGLE = 'toggle'      # two positions; Set FLIPS the state whatever value is passed
SELECTOR = 'selector'  # n positions; Set n selects position n (absolute)
SPRING = 'spring'      # a selector that springs back to its rest position (trims, seats, cargo door)
KNOB = 'knob'          # 0..100; Set v writes the state variable (absolute, maybe scaled)
COMMAND = 'command'    # Set fires a command; the position lives in another variable and flips
BUTTON = 'button'      # momentary; Set <press> once
HOLD = 'hold'          # Set 2 pressed, Set 0 released
ENCODER = 'encoder'    # Set +1 / Set -1 steps it
COVER = 'cover'        # a guard cover: only moves the 3D model, never gates its switch
NONE = 'none'          # nothing the map can drive (levers handled by hand in A300Levers)

# Controls whose Set fires a TOGGLE K: event: the position is the stock variable the event flips,
# so Set must only be sent when that variable differs from the pilot's pick.
STOCK_COMMANDS = {
    'AIRLINER_EXT_PWR': ('A:EXTERNAL POWER ON:1', 'Bool', [('0', 'OFF'), ('1', 'ON')]),
    'AIRLINER_GEAR_LEVER': ('A:GEAR HANDLE POSITION', 'Bool', [('0', 'UP'), ('1', 'DOWN')]),
    'AIRLINER_PARKINGBRAKE': ('A:BRAKE PARKING POSITION', 'Bool', [('0', 'RELEASED'), ('1', 'SET')]),
}

# Three-position selectors with a NEUTRAL middle that do NOT spring back.
NOT_SPRING = {'AIRLINER_TCAS_ABV'}

RANGE_RE = re.compile(r'^\s*p0\s+(\d+)\s+min\s+0\s+max')
SET_EVENT_RE = re.compile(r'\(>B:([A-Za-z0-9_]+?)_Set\)')
TOGGLE_RE = re.compile(r'\(L:([^,)]+)(?:,\s*[A-Za-z]+)?\)\s*!\s*\(>L:([^,)]+)')
INIT_STATE_RE = re.compile(r'^\s*\((L|A):([^,)]+)(?:,\s*([^)]+))?\)\s*(?:sp0|s0|\d|!|$)')
FIRST_READ_RE = re.compile(r'\((L|A):([^,)]+)(?:,\s*([^)]+))?\)')
CASE_WRITE_RE = re.compile(r'l0\s+(-?\d+)\s*==\s*if\{\s*(-?\d+(?:\.\d+)?)\s+(?:sp0\s+l0\s+)?\(>L:([^,)]+)')
CASE_LOC_RE = re.compile(r'l0\s+(-?\d+)\s*==\s*if\{\s*\(R:1:([^)]+)\)')
OFF_ON_RE = re.compile(r"0\s*==\s*if\{\s*'([^']*)'\s*\}\s*els\{\s*'([^']*)'\s*\}")
ONE_EQ_RE = re.compile(r"1\s*==\s*if\{\s*'([^']*)'\s*\}\s*els\{\s*'([^']*)'\s*\}")
HOLD_RE = re.compile(r'p0\s+2\s*==\s*if\{\s*1\s+\(>L:([^,)]+)')
KNOB_WRITE_RE = re.compile(r'l0\s+(?:(\d+(?:\.\d+)?)\s+/\s+)?\(>L:([^,)]+)')
CONST_WRITE_RE = re.compile(r'(?<![\w.])(-?\d+(?:\.\d+)?)\s+\(>L:([^,)]+)')
P0_WRITE_RE = re.compile(r'\bp0\s+\(>L:([^,)]+)')
L_ENCODER_RE = re.compile(r'\bp0\s+0\s*>\s*if\{')
K_ENCODER_RE = re.compile(r'\bp0\s+p0\s+0\s*>\s*if\{\s*\(>K:')


def load_loc(package):
    with open(os.path.join(package, LOC_FILE), 'r', encoding='utf-8-sig') as f:
        return json.load(f)['LocalisationPackage']['Strings']


def loc_text(loc, key):
    return (loc.get(key) or '').strip() if key else ''


def num(text):
    """'1.0' -> '1': position values as the app parses them."""
    v = float(text)
    return str(int(v)) if v == int(v) else repr(v)


def state_from_init(init):
    m = INIT_STATE_RE.match(init or '')
    if not m:
        return None, None
    return '%s:%s' % (m.group(1), m.group(2).strip()), (m.group(3) or '').strip() or None


def first_read(code):
    for m in FIRST_READ_RE.finditer(code or ''):
        name = m.group(2).strip()
        if not name.upper().startswith('XMLVAR_ANIMATION'):
            return '%s:%s' % (m.group(1), name), (m.group(3) or '').strip() or None
    return None, None


def two_words(tt_value):
    """{'0': word, '1': word} from "(X) 0 == if{ 'OFF' } els{ 'ON' }" style tooltip values."""
    m = OFF_ON_RE.search(tt_value or '')
    if m:
        return OrderedDict([('0', m.group(1)), ('1', m.group(2))])
    m = ONE_EQ_RE.search(tt_value or '')
    if m:
        return OrderedDict([('0', m.group(2)), ('1', m.group(1))])
    return OrderedDict()


def case_words(tt_value, loc):
    return OrderedDict((num(v), loc_text(loc, k)) for v, k in CASE_LOC_RE.findall(tt_value or ''))


def set_event(ie):
    """The B: event the cockpit's own Inc/Dec code calls, with its case ("AIRLINER_StormLight_Set")."""
    for code in (ie['inc_code'], ie['dec_code'], ie['set_code']):
        m = SET_EVENT_RE.search(code or '')
        if m:
            return m.group(1) + '_Set'
    return ie['id'] + '_Set'


def title_key_for(behavior, ie):
    for mr in behavior.mouserects:
        if mr['titles'] and any(e.upper() == ie['id'].upper() for e in mr['entries']):
            return mr['titles'][0]
    return re.sub(r'\.(ACTION|ON|OFF|PUSH|PULL)$', '.TITLE', ie['tt_desc'] or '')


PUSH_PULL_RE = re.compile(r'_(PUSH|PULL)$', re.IGNORECASE)


def knob_action(loc, title_key, ie_id):
    """What a knob push or pull does, in iniBuilds' words ("AIRCRAFT HEADING"), or None.

    A knob's push and pull events (AIRLINER_<KNOB>_PUSH / _PULL) share the knob's mouse rect and
    title; the tooltip file carries what each does beside the title, as <knob>.PUSH and <knob>.PULL."""
    m = PUSH_PULL_RE.search(ie_id or '')
    if not m or not title_key or not title_key.endswith('.TITLE'):
        return None
    return loc_text(loc, title_key[:-len('.TITLE')] + '.' + m.group(1).upper()) or None


def new_entry(ie, area, panel, title):
    short = ie['id'][len(PREFIX):] if ie['id'].upper().startswith(PREFIX) else ie['id']
    return OrderedDict([
        ('id', ie['id']), ('key', 'A300_' + short.upper()), ('area', area), ('panel', panel),
        ('title', title), ('kind', NONE), ('event', set_event(ie)), ('state_var', None),
        ('state_unit', None), ('scale', None), ('positions', OrderedDict()), ('values', None),
        ('press', None), ('rest', None), ('note', None), ('checklist', None)])


def classify(entry, ie, loc):
    s, units, init, tt = ie['set_code'] or '', (ie['units'] or '').lower(), ie['init'] or '', ie['tt_value'] or ''
    writes_l = re.findall(r'\(>L:([^,)]+)', s)
    writes_k = re.findall(r'\(>K:([^,)]+)', s)

    if entry['id'] in STOCK_COMMANDS:
        var, unit, words = STOCK_COMMANDS[entry['id']]
        entry['kind'] = COMMAND
        entry['state_var'], entry['state_unit'] = var, unit
        entry['positions'] = OrderedDict(words)
        return entry

    # Guard covers: the Set code moves only O: variables.
    if 'Cover_Position' in s and not writes_l and not writes_k:
        entry['kind'] = COVER
        return entry

    # Encoders: Set picks an UP or DN command (L:) or an INC or DEC event (K:) by the step's sign.
    if K_ENCODER_RE.search(s) or (L_ENCODER_RE.search(s) and writes_l and 'p0 -1 *' in (ie['dec_code'] or '')):
        entry['kind'] = ENCODER
        entry['state_var'], entry['state_unit'] = first_read(tt)
        return entry

    # Hold buttons: Set 2 presses, anything else releases.
    if HOLD_RE.search(s):
        entry['kind'] = HOLD
        entry['press'] = 2
        return entry

    # Knobs: the value goes straight into an L:var, perhaps divided.
    if units == 'percent':
        m = KNOB_WRITE_RE.search(s)
        if m and not writes_k:
            entry['kind'] = KNOB
            entry['state_var'] = 'L:' + m.group(2).strip()
            entry['scale'] = 1.0 / float(m.group(1)) if m.group(1) else 1.0
        else:
            entry['note'] = 'percent control without a plain L:var write'
        return entry

    # Selectors: one absolute write per position.
    case = CASE_WRITE_RE.findall(s)
    if units == 'numbers' and case:
        var = case[0][2].strip()
        sv, su = state_from_init(init)
        entry['kind'] = SELECTOR
        entry['state_var'] = sv or 'L:' + var
        entry['state_unit'] = su
        values = OrderedDict((num(p), num(v)) for p, v, n in case if n.strip() == var)
        if any(p != v for p, v in values.items()):
            entry['values'] = values
        words = case_words(tt, loc)
        rng = RANGE_RE.match(s)
        positions = [str(v) for v in range(int(rng.group(1)) + 1)] if rng else list(values)
        entry['positions'] = OrderedDict((p, words.get(p, '')) for p in positions)
        if (len(positions) == 3 and entry['positions'].get('1', '').upper() == 'NEUTRAL'
                and entry['id'] not in NOT_SPRING):
            entry['kind'] = SPRING
            entry['rest'] = 1
        return entry

    # Two-position toggles: Set flips the state variable.
    m = TOGGLE_RE.search(s)
    if m and m.group(1).strip() == m.group(2).strip():
        sv, su = state_from_init(init)
        entry['kind'] = TOGGLE
        entry['state_var'] = sv or 'L:' + m.group(1).strip()
        entry['state_unit'] = su
        entry['positions'] = two_words(tt) or OrderedDict([('0', 'OFF'), ('1', 'ON')])
        return entry

    # A switch whose Set writes the parameter itself (the audio panel's receiver switches).
    m = P0_WRITE_RE.search(s)
    if m and units == 'boolean' and not writes_k:
        entry['kind'] = SELECTOR
        entry['state_var'] = 'L:' + m.group(1).strip()
        entry['positions'] = two_words(tt) or OrderedDict([('0', 'OFF'), ('1', 'ON')])
        return entry

    consts = CONST_WRITE_RE.findall(s)
    if consts and not writes_k:
        sv, su = first_read(tt)
        written = {n.strip() for _, n in consts}
        own_animation = bool(sv) and any(sv[2:] + '_COMMAND' == w for w in written)
        if sv and sv.startswith('L:') and sv[2:] not in written and two_words(tt) and not own_animation:
            # A command the WASM consumes; the position it flips is shown by another variable.
            entry['kind'] = COMMAND
            entry['state_var'], entry['state_unit'] = sv, su
            entry['positions'] = two_words(tt)
            return entry
        entry['kind'] = BUTTON
        entry['press'] = 1
        return entry

    if writes_k and units == 'boolean':
        entry['kind'] = BUTTON
        entry['press'] = 1
        return entry

    entry['note'] = ('drives a K: event: ' + ', '.join(sorted(set(writes_k)))) if writes_k else 'no write the map can drive'
    return entry


_TEST_TERM = '(L:INI_ANNLT_SWITCH) 0 =='
_POWER = {'(L:INI_AC_LIGHTS_FAILURE)': 'AC', '(L:INI_DC_LIGHTS_FAILURE)': 'DC'}


def lamp_rule(code):
    """(state, power) of an annunciator lamp's emissive code, or None for anything else.

    Every annunciator lamp's brightness is (state OR annunciator test) x brightness x its bus's light
    power, in one of two shapes: "STATE (L:INI_ANNLT_SWITCH) 0 == + 1 min ... (L:INI_DC_LIGHTS_FAILURE) *"
    or, for a lamp the test also lights a second way, "STATE (L:INI_ANNLT_SWITCH) 0 == or (L:...) 0 == + ...".
    The state is everything before the first test term; a power factor written into it
    ("... and (L:INI_AC_LIGHTS_FAILURE) *") is lifted out. A lamp whose state is a bare number lights only
    in the test, and a code with no test term (a panel backlight) is not a lamp."""
    at = code.find(_TEST_TERM)
    if at < 0:
        return None
    state, rest = code[:at].strip(), code[at:]
    power = None
    for token, bus in _POWER.items():
        if state.endswith(token + ' *'):
            state, power = state[:-len(token + ' *')].strip(), bus
        elif token in rest:
            power = bus
    if not state or re.fullmatch(r'[0-9.]+', state):
        return None
    # A state that reads nothing but the test switch (iniBuilds' SELCAL lamps) lights only in the test.
    if set(re.findall(r'\((?:L|A):[^,)]+', state)) == {'(L:INI_ANNLT_SWITCH'}:
        return None
    return state, power


def lamps(behavior):
    """Every annunciator lamp in the cockpit: the node it lights, its state rule and its bus."""
    found = {}
    for m in behavior.emissive:
        rule = lamp_rule(m['code'])
        if rule is None or not 0 <= m['owner'] < len(behavior.components):
            continue
        node = behavior.components[m['owner']]['id']
        found.setdefault(node, OrderedDict([('node', node), ('state', rule[0]), ('power', rule[1])]))
    return [found[n] for n in sorted(found)]


def build_map(behavior, loc, package_version=''):
    controls = []
    for ie in behavior.inputevents:
        path = behavior.path(ie['owner'])
        area = path[1] if len(path) > 1 else ''
        panel = path[2] if len(path) > 2 else area
        title_key = title_key_for(behavior, ie)
        entry = classify(new_entry(ie, area, panel, loc_text(loc, title_key)), ie, loc)
        action = knob_action(loc, title_key, ie['id'])
        if action:
            entry['action'] = action
        controls.append(entry)
    return OrderedDict([
        ('generator', 'tools/a300-gen/generate_a300_map.py'),
        ('package', 'inibuilds-aircraft-a300'),
        ('package_version', package_version),
        ('controls', controls),
        ('lamps', lamps(behavior))])


CHECKLIST_FILE = os.path.join('SimObjects', 'Airplanes', 'A300-600', 'common', 'Checklist', 'Airbus_A300_Checklist.xml')
CODE_L_RE = re.compile(r'\(>?L:([A-Za-z0-9_]+)')
CODE_B_RE = re.compile(r'\(>B:([A-Za-z0-9_]+)\)')


def add_checklist_steps(data, checklist_text):
    """Gives each control the number of the first checklist step that names it, counting the live checkpoints
    of the aircraft's own checklist in order: by a cockpit part the step points at (Instrument id = the control's
    id without AIRLINER_), a variable it tests or sets (the control's state variable) or an event it fires (the
    control's Set event). A panel's rows follow these steps. Commented-out checkpoints are not steps."""
    import xml.etree.ElementTree as ET
    root = ET.fromstring(re.sub(r'<!--.*?-->', '', checklist_text, flags=re.S).encode('utf-8'))
    by_part, by_state, by_event = {}, {}, {}
    for c in data['controls']:
        by_part.setdefault(c['id'].upper(), []).append(c)
        if (c['state_var'] or '').startswith('L:'):
            by_state.setdefault(c['state_var'][2:].upper(), []).append(c)
        if c['event']:
            by_event.setdefault(c['event'].upper(), []).append(c)
    for step, checkpoint in enumerate(root.iter('Checkpoint'), start=1):
        named = []
        for el in checkpoint.iter():
            if el.tag == 'Instrument':
                named += by_part.get(PREFIX + el.attrib.get('id', '').upper(), [])
            code = el.attrib.get('Code', '')
            for var in CODE_L_RE.findall(code):
                named += by_state.get(var.upper(), [])
            for event in CODE_B_RE.findall(code):
                named += by_event.get(event.upper(), [])
        for c in named:
            if c['checklist'] is None:
                c['checklist'] = step


def to_json(data):
    return json.dumps(data, indent=1, ensure_ascii=False) + '\n'


def read_behavior(path):
    """The A300 file has empty part-marker tags that begin with a digit (<0FAB/>), which an XML
    parser rejects; they carry nothing the map needs, so they become <P/> before parsing."""
    with open(path, 'r', encoding='utf-8-sig') as f:
        text = f.read()
    head, sep, tail = text.partition('<Strings')
    head = re.sub(r'<[0-9][^ />]*/>', '<P/>', head)
    return Behavior(head + sep + tail)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--package', required=True, help='the inibuilds-aircraft-a300 package folder')
    ap.add_argument('--out', default=DEFAULT_OUT)
    args = ap.parse_args(argv)
    with open(os.path.join(args.package, 'manifest.json'), 'r', encoding='utf-8-sig') as f:
        version = json.load(f).get('package_version', '')
    data = build_map(read_behavior(os.path.join(args.package, BEHAVIOR_FILE)), load_loc(args.package), version)
    with open(os.path.join(args.package, CHECKLIST_FILE), 'r', encoding='utf-8-sig') as f:
        add_checklist_steps(data, f.read())
    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        f.write(to_json(data))
    kinds = {}
    for c in data['controls']:
        kinds[c['kind']] = kinds.get(c['kind'], 0) + 1
    print('controls %d %s -> %s' % (len(data['controls']), dict(sorted(kinds.items())), args.out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
