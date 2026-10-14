"""Converts the iniBuilds A300-600's own checklist (common/Checklist/Airbus_A300_Checklist.xml, the
native MSFS checklist with 328 checkpoints) into MSFS Blind Assist's checklist text format:

    [Phase - Page title]
    Item: expected state

Text comes from the package's en-US.locPak; it is upper case there, so it is put into sentence
case with the cockpit's abbreviations kept in capitals.

    python tools/a300-gen/convert_checklist.py --package "<Community folder>\\inibuilds-aircraft-a300"

writes MSFSBlindAssist/Checklists/iniBuilds_A300_Checklist.txt (--out to change).
"""
import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
DEFAULT_OUT = os.path.join(REPO_ROOT, 'MSFSBlindAssist', 'Checklists', 'iniBuilds_A300_Checklist.txt')
CHECKLIST = os.path.join('SimObjects', 'Airplanes', 'A300-600', 'common', 'Checklist', 'Airbus_A300_Checklist.xml')

PHASES = {
    'PREFLIGHT_GATE': 'Preflight', 'PREFLIGHT_PUSHBACK': 'Pushback', 'PREFLIGHT_TAXI_OUT': 'Taxi out',
    'FLIGHT_RUNWAY': 'Runway', 'FLIGHT_TAKEOFF': 'Takeoff', 'FLIGHT_CLIMB': 'Climb',
    'FLIGHT_CRUISE': 'Cruise', 'FLIGHT_DESCENT': 'Descent', 'LANDING_APPROACH': 'Approach',
    'LANDING_APPROACH_VFR': 'Visual approach', 'LANDING_FINAL': 'Final', 'LANDING_GROUNDROLL': 'Landing roll',
    'LANDING_TAXI_IN': 'Taxi in', 'LANDING_GATE': 'Parking',
}

ABBREVIATIONS = {
    'AC', 'ACARS', 'ADF', 'ADI', 'AOC', 'APU', 'ATC', 'ATS', 'CG', 'CPDLC', 'CPT', 'CVR', 'CWS', 'DC',
    'DH', 'DME', 'ECAM', 'EFB', 'EFIS', 'EGT', 'EPR', 'ESS', 'FCU', 'FD', 'FMA', 'FMC', 'FMS', 'FO',
    'FPV', 'GPS', 'GPU', 'GPWS', 'HF', 'IDC', 'IDG', 'ILS', 'IRS', 'ISDU', 'LOC', 'MCDU', 'MCT', 'N1',
    'N2', 'ND', 'OFP', 'PA', 'PFD', 'PTU', 'QNH', 'RAT', 'SID', 'STAR', 'STD', 'TCAS', 'TO', 'TOGA',
    'TRP', 'V1', 'V2', 'VHF', 'VOR', 'VR', 'VS', 'WXR', 'XPDR', 'ZFW',
}
WORD_RE = re.compile(r"[A-Za-z0-9']+")
FIRST_WORD_RE = re.compile(r"[A-Za-z0-9/]+")


def sentence_case(text):
    text = ' '.join((text or '').split())
    if not text:
        return text

    def fix(m):
        w = m.group(0)
        return w if w.upper() in ABBREVIATIONS else w.lower()

    out = WORD_RE.sub(fix, text)
    for i, ch in enumerate(out):
        if ch.isalpha():
            return out[:i] + ch.upper() + out[i + 1:]
    return out


def lookup(loc, attr):
    key = attr or ''
    if key.startswith('TT:'):
        key = key[3:]
    return loc.get(key, '').strip()


def item_text(subject, expectation):
    if not expectation:
        return subject
    # The first WORD decides, not the text up to the first space: "SID," is the abbreviation SID.
    first = FIRST_WORD_RE.match(expectation)
    keep = first is not None and first.group(0).upper() in ABBREVIATIONS
    lowered = expectation if keep else expectation[0].lower() + expectation[1:]
    return '%s: %s' % (subject.rstrip(':'), lowered)


def _items(container, loc, prefix, out):
    """Checkpoints in document order. A checkpoint inside a <Block> (a named group such as the
    standby power check) carries the block's title in front of it, so each line still makes
    sense on its own when a screen reader lands on it."""
    for child in container:
        if child.tag == 'Block':
            title = sentence_case(lookup(loc, child.attrib.get('SubjectTT')))
            _items(child, loc, title or prefix, out)
        elif child.tag == 'Checkpoint':
            desc = child.find('CheckpointDesc')
            if desc is None:
                continue
            subject = sentence_case(lookup(loc, desc.attrib.get('SubjectTT')))
            if not subject:
                continue
            text = item_text(subject, sentence_case(lookup(loc, desc.attrib.get('ExpectationTT'))))
            label = prefix.rstrip(':')  # a title that ends in a colon must not give ": - "
            out.append('%s - %s' % (label, text) if label else text)


def convert(xml_bytes, loc):
    root = ET.fromstring(xml_bytes)
    lines = []
    for step in root.iter('Step'):
        step_id = step.attrib.get('ChecklistStepId', '')
        phase = PHASES.get(step_id, step_id)
        for page in step.findall('Page'):
            title = sentence_case(lookup(loc, page.attrib.get('SubjectTT')))
            items = []
            _items(page, loc, '', items)
            if items:
                if lines:
                    lines.append('')
                lines.append('[%s - %s]' % (phase, title) if title else '[%s]' % phase)
                lines.extend(items)
    return '\n'.join(lines) + '\n'


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--package', required=True)
    ap.add_argument('--out', default=DEFAULT_OUT)
    args = ap.parse_args(argv)
    with open(os.path.join(args.package, 'en-US.locPak'), 'r', encoding='utf-8-sig') as f:
        loc = json.load(f)['LocalisationPackage']['Strings']
    with open(os.path.join(args.package, CHECKLIST), 'rb') as f:
        xml_bytes = f.read()
    text = convert(xml_bytes, loc)
    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    print('%d lines -> %s' % (text.count('\n'), args.out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
