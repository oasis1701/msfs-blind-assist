"""Builds small documents in the MSFS 2024 *compiled* ModelBehavior format for the generator's
tests. The real files (A300_Interior.behavior.xml, about 4 MB) cannot live in the repo; these
fixtures reproduce the exact shape the reader relies on:

  <ModelBehaviors Compiled="True">
    <Components> <Component ID=".." OwnerID="n"/> ... </Components>
    <Mouserects> <MouseRect OwnerID="n" HighlightNodeId=".."> <Callback><IMCodeInstances>
        <IMDefault StringID="h"/><IMDrag StringID="h"/></IMCodeInstances></Callback>
        <IMTooltipsInstances><IMDrag><TTTitle>KEY</TTTitle>
        <TooltipEntries><TooltipEntry>IE_ID</TooltipEntry></TooltipEntries></IMDrag>
        </IMTooltipsInstances></MouseRect> ... </Mouserects>
    <InputEvents><Instances><InputEvent OwnerID="n" ID=".."> ... </InputEvent></Instances></InputEvents>
    <Materials><Material> ... <Code StringID="h"/> ... </Material></Materials>
    <Strings Count="n">
        <h>escaped RPN</h>      one string per line
    </Strings></ModelBehaviors>
"""
from xml.sax.saxutils import escape

_APOS = {"'": "&apos;"}


class FixtureBuilder:
    def __init__(self):
        self._components = []
        self._mouserects = []
        self._inputevents = []
        self._materials = []
        self._strings = {}
        self._next = 1000

    def _string(self, code):
        h = str(self._next)
        self._next += 1
        self._strings[h] = code
        return h

    def component(self, cid, owner=None):
        self._components.append((cid, owner))
        return len(self._components) - 1

    def mouserect(self, owner, node, code, title=None, entries=()):
        h = self._string(code)
        tooltip = ''
        if title or entries:
            tooltip = ('<IMTooltipsInstances><IMDrag>'
                       + ('<TTTitle>%s</TTTitle>' % title if title else '')
                       + '<TooltipEntries>'
                       + ''.join('<TooltipEntry>%s</TooltipEntry>' % e for e in entries)
                       + '</TooltipEntries></IMDrag></IMTooltipsInstances>')
        self._mouserects.append(
            '<MouseRect OwnerID="%d" HighlightNodeId="%s"><Callback Type="CallbackCode">'
            '<IMCodeInstances><IMDefault StringID="%s"/><IMDrag StringID="%s"/></IMCodeInstances>'
            '</Callback>%s</MouseRect>' % (owner, node, h, h, tooltip))

    def inputevent(self, owner, ie_id, set_code=None, tt_value=None, units='', init=None,
                   tt_desc='', inc=None, dec=None):
        parts = ['<InputEvent OwnerID="%d" ID="%s">' % (owner, ie_id), '<Tooltip Inop="False">']
        if tt_desc:
            parts.append('<TTDescription>%s</TTDescription>' % tt_desc)
        if tt_value:
            parts.append('<TTValue StringID="%s"/>' % self._string(tt_value))
        parts.append('</Tooltip><Value><Units>%s</Units>' % units)
        if init:
            parts.append('<Init StringID="%s"/>' % self._string(init))
        parts.append('</Value>')
        for tag, code in (('Inc', inc), ('Dec', dec), ('Set', set_code)):
            if code:
                parts.append('<%s><Code StringID="%s"/><Parameters><Param Type="Float"/></Parameters></%s>'
                             % (tag, self._string(code), tag))
        parts.append('</InputEvent>')
        self._inputevents.append(''.join(parts))

    def material(self, code, owner=None):
        """An emissive material; owner is the index of the component (cockpit node) it lights."""
        attr = '' if owner is None else ' OwnerID="%d"' % owner
        self._materials.append('<Material%s><EmissiveFactor OverrideBaseEmissive="True"><Parameter>'
                               '<Code StringID="%s"/></Parameter></EmissiveFactor></Material>'
                               % (attr, self._string(code)))

    def build(self):
        comps = ''.join('<Component ID="%s"%s/>' % (cid, '' if owner is None else ' OwnerID="%d"' % owner)
                        for cid, owner in self._components)
        strings = '\n'.join('<%s>%s</%s>' % (h, escape(code, _APOS), h) for h, code in self._strings.items())
        return ('<ModelBehaviors Compiled="True" Version="1" Revision="2">'
                '<Components Count="%d">%s</Components>' % (len(self._components), comps)
                + '<Mouserects>%s</Mouserects>' % ''.join(self._mouserects)
                + '<InputEvents><Instances>%s</Instances></InputEvents>' % ''.join(self._inputevents)
                + '<Materials>%s</Materials>' % ''.join(self._materials)
                + '<Strings Count="%d">\n%s\n</Strings></ModelBehaviors>' % (len(self._strings), strings))
