"""Reader for the MSFS 2024 *compiled* ModelBehavior XML (Compiled="True") shipped by iniBuilds
aircraft. Read-only; no simulator, no network. Adapted from the 2026-10-02 investigation's
mbparse.py. The A300's file (A300_Interior.behavior.xml, package 1.0.11) has empty part-marker tags
that begin with a digit, which an XML parser rejects; generate_a300_map.read_behavior rewrites them
first.

The format is documented in a300_fixture.py. Every RPN string lives in the trailing <Strings> section,
one "<hash>escaped code</hash>" per line, and elements point at it through a StringID attribute.
Note: a TooltipEntry can spell an input event id in a different case from the InputEvent's own
ID (INSTRUMENT_TOGGLE_Battery_IE_ID vs INSTRUMENT_TOGGLE_BATTERY_IE_ID), so look ids up with
inputevent(), which ignores case.
"""
import html
import re
import xml.etree.ElementTree as ET

_STRING_LINE = re.compile(r'^\s*<(\d+)>(.*)</\1>\s*$')


class Behavior:
    def __init__(self, text):
        strings_at = text.find('<Strings')
        self.strings = {}
        if strings_at >= 0:
            for line in text[strings_at:].splitlines()[1:]:
                m = _STRING_LINE.match(line)
                if m:
                    self.strings[m.group(1)] = html.unescape(m.group(2))
            xml_text = text[:strings_at] + '</ModelBehaviors>'
        else:
            xml_text = text
        root = ET.fromstring(xml_text)

        self.components = []
        comps = root.find('Components')
        if comps is not None:
            for c in comps.findall('Component'):
                owner = c.attrib.get('OwnerID')
                self.components.append({'id': c.attrib.get('ID', ''),
                                        'owner': int(owner) if owner is not None else None})

        self.mouserects = []
        rects = root.find('Mouserects')
        if rects is not None:
            for r in rects.findall('MouseRect'):
                self.mouserects.append(self._mouserect(r))

        self.inputevents = []
        self._ie_by_upper = {}
        ies = root.find('InputEvents')
        instances = ies.find('Instances') if ies is not None else None
        if instances is not None:
            for e in instances.findall('InputEvent'):
                d = self._inputevent(e)
                self.inputevents.append(d)
                self._ie_by_upper.setdefault(d['id'].upper(), d)

        self.material_codes = []
        # The emissive materials: which node each lights (its OwnerID is that component's index, the root
        # component included) and the code for how brightly.
        self.emissive = []
        mats = root.find('Materials')
        if mats is not None:
            for m in mats.findall('Material'):
                for code_el in m.iter('Code'):
                    code = self._code(code_el)
                    if code:
                        self.material_codes.append(code)
                factor = m.find('EmissiveFactor')
                code = self._code(factor.find('.//Code')) if factor is not None else ''
                if code and m.attrib.get('OwnerID') is not None:
                    self.emissive.append({'owner': int(m.attrib['OwnerID']), 'code': code})

    @classmethod
    def from_file(cls, path):
        with open(path, 'r', encoding='utf-8-sig') as f:
            return cls(f.read())

    def _code(self, el):
        if el is None:
            return ''
        sid = el.attrib.get('StringID')
        if sid is not None:
            return self.strings.get(sid, '')
        return (el.text or '').strip()

    def _mouserect(self, r):
        codes = {}
        cb = r.find('Callback')
        imc = cb.find('IMCodeInstances') if cb is not None else None
        if imc is not None:
            for child in imc:
                codes[child.tag] = self._code(child)
        titles, entries = [], []
        tips = r.find('IMTooltipsInstances')
        if tips is not None:
            for inst in tips:
                t = inst.findtext('TTTitle')
                if t and t.strip() not in titles:
                    titles.append(t.strip())
                te = inst.find('TooltipEntries')
                if te is not None:
                    for e in te.findall('TooltipEntry'):
                        v = (e.text or '').strip()
                        if v and v not in entries:
                            entries.append(v)
        return {'owner': int(r.attrib.get('OwnerID', '-1')),
                'highlight': r.attrib.get('HighlightNodeId', ''),
                'codes': codes, 'titles': titles, 'entries': entries}

    def _inputevent(self, e):
        d = {'id': e.attrib.get('ID', ''), 'owner': int(e.attrib.get('OwnerID', '-1')),
             'tt_desc': '', 'tt_value': '', 'units': '', 'init': '',
             'set_code': '', 'inc_code': '', 'dec_code': ''}
        tt = e.find('Tooltip')
        if tt is not None:
            d['tt_desc'] = (tt.findtext('TTDescription') or '').strip()
            d['tt_value'] = self._code(tt.find('TTValue'))
        v = e.find('Value')
        if v is not None:
            d['units'] = (v.findtext('Units') or '').strip()
            d['init'] = self._code(v.find('Init'))
        for tag, key in (('Set', 'set_code'), ('Inc', 'inc_code'), ('Dec', 'dec_code')):
            el = e.find(tag)
            if el is not None:
                d[key] = self._code(el.find('Code'))
        return d

    def inputevent(self, ie_id):
        return self._ie_by_upper.get((ie_id or '').upper())

    def path(self, index):
        out, seen = [], set()
        while index is not None and 0 <= index < len(self.components) and index not in seen:
            seen.add(index)
            out.append(self.components[index]['id'])
            index = self.components[index]['owner']
        return list(reversed(out))
