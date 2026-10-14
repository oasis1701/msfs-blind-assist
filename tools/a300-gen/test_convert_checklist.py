import os
import sys
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_checklist as cc

XML = b"""<?xml version="1.0" encoding="Windows-1252"?>
<SimBase.Document Type="Checklist" version="1,0"><Checklist.Checklist>
<Step ChecklistStepId="PREFLIGHT_GATE">
  <Page SubjectTT="TT:A300.CHECKLISTS.PRELIM">
    <Checkpoint Id="A300.CHECKLISTS.PRELIM.BATT">
      <CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.PRELIM.BATT" ExpectationTT="TT:A300.CHECKLISTS.ACTION.AUTO"/>
    </Checkpoint>
    <Checkpoint Id="B"><CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.PRELIM.IRS" ExpectationTT="TT:A300.CHECKLISTS.ACTION.NAV"/></Checkpoint>
    <Block SubjectTT="TT:A300.CHECKLISTS.PRELIM.APUA.BLOCK">
      <Checkpoint Id="C"><CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.PRELIM.APUM" ExpectationTT="TT:A300.CHECKLISTS.ACTION.ON"/></Checkpoint>
    </Block>
  </Page>
</Step>
<Step ChecklistStepId="LANDING_GROUNDROLL"><Page SubjectTT="TT:A300.CHECKLISTS.AFTERLAND"><Checkpoint Id="D"><CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.AFTERLAND.FMC" ExpectationTT="TT:MISSING"/></Checkpoint></Page></Step>
</Checklist.Checklist></SimBase.Document>"""

LOC = {
    'A300.CHECKLISTS.PRELIM': 'Preliminary Cockpit Preparation',
    'A300.CHECKLISTS.PRELIM.BATT': 'BATTERIES',
    'A300.CHECKLISTS.PRELIM.IRS': 'IRS MODE SELECTORS',
    'A300.CHECKLISTS.PRELIM.APUA.BLOCK': 'APU (As required)',
    'A300.CHECKLISTS.PRELIM.APUM': 'APU Master Switch',
    'A300.CHECKLISTS.ACTION.AUTO': 'AUTO',
    'A300.CHECKLISTS.ACTION.NAV': 'NAV',
    'A300.CHECKLISTS.ACTION.ON': 'ON',
    'A300.CHECKLISTS.AFTERLAND': 'After Landing Procedure',
    'A300.CHECKLISTS.AFTERLAND.FMC': 'FMC MCDU',
}


class ConvertChecklistTests(unittest.TestCase):
    def test_format(self):
        self.assertEqual(
            '[Preflight - Preliminary cockpit preparation]\n'
            'Batteries: auto\n'
            'IRS mode selectors: nav\n'
            'APU (as required) - APU master switch: on\n'
            '\n'
            '[Landing roll - After landing procedure]\n'
            'FMC MCDU\n',
            cc.convert(XML, LOC))

    def test_sentence_case_keeps_a300_abbreviations(self):
        self.assertEqual('FMC (initialise using MCDU)', cc.sentence_case('FMC (INITIALISE USING MCDU)'))
        self.assertEqual('EFIS control panel - PFD and ND brightness', cc.sentence_case('EFIS CONTROL PANEL - PFD AND ND BRIGHTNESS'))

    def test_an_abbreviation_followed_by_punctuation_keeps_its_case(self):
        # "SID," is the abbreviation SID, not the word "sID,".
        self.assertEqual('F-pln: SID, airways, STAR', cc.item_text('F-pln', 'SID, airways, STAR'))
        self.assertEqual('F-pln: SID, airways, STAR', cc.item_text('F-pln:', 'SID, airways, STAR'))
        self.assertEqual('Bleed source: APU bleed', cc.item_text('Bleed source', 'APU bleed'))

    def test_a_slash_joined_abbreviation_keeps_its_case(self):
        # "ILS/VOR" is not in the list as a word; its part before the slash, ILS, is.
        self.assertEqual('Radio: ILS/VOR approach', cc.item_text('Radio', 'ILS/VOR approach'))
        self.assertEqual('Radio: ILS/VOR', cc.item_text('Radio', 'ILS/VOR'))
        # Any other slash-joined word is still lowercased.
        self.assertEqual('Switch: on/auto', cc.item_text('Switch', 'On/auto'))

    def test_a_slash_joined_entry_in_the_list_matches_as_a_whole_word(self):
        # An "A/THR"-style entry: its part before the slash, "A", is no abbreviation, so only
        # the whole word keeps its case.
        with mock.patch.object(cc, 'ABBREVIATIONS', cc.ABBREVIATIONS | {'A/THR'}):
            self.assertEqual('Autothrust: A/THR armed', cc.item_text('Autothrust', 'A/THR armed'))

    def test_a_subject_ending_in_a_colon_is_not_doubled(self):
        self.assertEqual('Init a: altn', cc.item_text('Init a:', 'altn'))

    def test_a_block_title_ending_in_a_colon_is_not_doubled(self):
        xml = b"""<?xml version="1.0" encoding="Windows-1252"?>
<SimBase.Document Type="Checklist" version="1,0"><Checklist.Checklist>
<Step ChecklistStepId="PREFLIGHT_PUSHBACK">
  <Page SubjectTT="TT:A300.CHECKLISTS.START">
    <Block SubjectTT="TT:A300.CHECKLISTS.START.BOTH">
      <Checkpoint Id="A"><CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.START.GEN" ExpectationTT="TT:A300.CHECKLISTS.ACTION.ON"/></Checkpoint>
      <Checkpoint Id="B"><CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.START.PACKS" ExpectationTT="TT:MISSING"/></Checkpoint>
    </Block>
  </Page>
</Step>
</Checklist.Checklist></SimBase.Document>"""
        loc = dict(LOC)
        loc.update({
            'A300.CHECKLISTS.START': 'Engine Start',
            'A300.CHECKLISTS.START.BOTH': 'ONCE BOTH ENGINES RUNNING:',
            'A300.CHECKLISTS.START.GEN': 'GENERATORS',
            'A300.CHECKLISTS.START.PACKS': 'PACKS',
        })
        text = cc.convert(xml, loc)
        self.assertNotIn(': - ', text)
        self.assertEqual('[Pushback - Engine start]\n'
                         'Once both engines running - Generators: on\n'
                         'Once both engines running - Packs\n',
                         text)


if __name__ == '__main__':
    unittest.main()
