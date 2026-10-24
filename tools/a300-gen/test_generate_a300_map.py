import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_a300_map as gen
from a300_fixture import FixtureBuilder
from behavior_xml import Behavior

LOC = {
    'INI.TOOLTIPS.StormLight.TITLE': 'STORM LIGHT',
    'INI.TOOLTIPS.IRS_MODE_SELECT.TITLE': 'IRS DYSP SELECTOR',
    'INI.TOOLTIPS.IRS_MODE_SELECT.0': 'TEST',
    'INI.TOOLTIPS.IRS_MODE_SELECT.1': 'TK/GS',
    'INI.TOOLTIPS.IRS_MODE_SELECT.2': 'PPOS',
    'INI.TOOLTIPS.NOSE_TRIM.TITLE': 'RUDDER TRIM',
    'INI.TOOLTIPS.NOSE_TRIM.0': 'RIGHT',
    'INI.TOOLTIPS.NOSE_TRIM.1': 'NEUTRAL',
    'INI.TOOLTIPS.NOSE_TRIM.2': 'LEFT',
    'INI.TOOLTIPS.TCAS_ABV.TITLE': 'TCAS MODE',
    'INI.TOOLTIPS.TCAS_ABV.0': 'ABOVE',
    'INI.TOOLTIPS.TCAS_ABV.1': 'NEUTRAL',
    'INI.TOOLTIPS.TCAS_ABV.2': 'BELOW',
    'INI.TOOLTIPS.AUTO_PRESS_SELECT.TITLE': 'AUTO PRESS LIMIT',
    'INI.TOOLTIPS.ENG1_CUTOFF.TITLE': 'ENG1 MIXTURE',
    'INI.TOOLTIPS.CPT_VHF_TFR.TITLE': 'COM1 FREQUENCY',
    'INI.TOOLTIPS.ECAM_ENG.TITLE': 'ECAM ENG PAGE',
    'INI.TOOLTIPS.HEADING_KNOB.TITLE': 'HEADING KNOB',
    'INI.TOOLTIPS.HEADING_KNOB.PUSH': 'AIRCRAFT HEADING',
    'INI.TOOLTIPS.HEADING_KNOB.PULL': 'HEADING MODE',
    'INI.TOOLTIPS.VOR_CRS_CAPT.TITLE': 'VOR1 CRS',
    'INI.TOOLTIPS.RAM_AIR_COVER.TITLE': 'RAM AIR GUARD',
    'INI.TOOLTIPS.GEAR_LEVER.TITLE': 'GEAR LEVER',
    'INI.TOOLTIPS.AT_DISCO1.TITLE': 'AUTO THROTTLE DISCONNECT',
    'INI.TOOLTIPS.APU_START.TITLE': 'APU START',
    'INI.TOOLTIPS.FLAPS.TITLE': 'FLAPS LEVER',
}

CASE3 = ("p0 2 min 0 max (>O:{o}) (O:{o}) s0 l0 0 == if{{ 0 sp0 l0 (>L:{v}) g1 }} "
         "l0 1 == if{{ 1 sp0 l0 (>L:{v}) g1 }} l0 2 == if{{ 2 sp0 l0 (>L:{v}) g1 }} :1")


def tooltip_cases(name, n):
    return ' '.join("l0 %d == if{ (R:1:INI.TOOLTIPS.%s.%d) quit }" % (i, name, i) for i in range(n))


def _fixture():
    f = FixtureBuilder()
    root = f.component('A300_INTERIOR_COMPONENT')
    overhead = f.component('OVERHEAD', root)
    lights = f.component('OVERHEAD_LIGHTS', overhead)
    irs = f.component('IRS', overhead)
    pedestal = f.component('PEDESTAL', root)
    trim = f.component('TRIM', pedestal)

    def add(owner, ie_id, title, **ie):
        f.mouserect(owner, ie_id.replace('AIRLINER_', ''), '(>B:%s_Toggle)' % ie_id, title=title, entries=[ie_id])
        f.inputevent(owner, ie_id, **ie)

    add(lights, 'AIRLINER_StormLight', 'INI.TOOLTIPS.StormLight.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:AIRLINER_StormLight_Position) (L:INI_STORM_LIGHT_SWITCH) ! (>L:INI_STORM_LIGHT_SWITCH)",
        init="(L:INI_STORM_LIGHT_SWITCH) sp0 l0 (>O:AIRLINER_StormLight_Position)",
        tt_value="(L:INI_STORM_LIGHT_SWITCH) 0 == if{ 'OFF' } els{ 'ON' }",
        inc="(O:AIRLINER_StormLight_Position) p0 + (>B:AIRLINER_StormLight_Set)",
        dec="(O:AIRLINER_StormLight_Position) p0 - (>B:AIRLINER_StormLight_Set)")
    add(irs, 'AIRLINER_IRS_MODE_SELECT', 'INI.TOOLTIPS.IRS_MODE_SELECT.TITLE', units='numbers',
        set_code=("p0 2 min 0 max (>O:P) (O:P) s0 l0 0 == if{ 0 (>L:INI_IRS_MODE_KNOB) g1 } "
                  "l0 1 == if{ 1 (>L:INI_IRS_MODE_KNOB) g1 } l0 2 == if{ 2 (>L:INI_IRS_MODE_KNOB) g1 } :1"),
        init="(L:INI_IRS_MODE_KNOB) sp0 l0 (>O:P)",
        tt_value="(B:AIRLINER_IRS_MODE_SELECT, Number) sp0 " + tooltip_cases('IRS_MODE_SELECT', 3))
    add(trim, 'AIRLINER_NOSE_TRIM', 'INI.TOOLTIPS.NOSE_TRIM.TITLE', units='numbers',
        set_code=CASE3.format(o='O:N', v='XMLVAR_RudderTrim'),
        init="(L:XMLVAR_RudderTrim) sp0 l0 (>O:N)",
        tt_value="(B:AIRLINER_NOSE_TRIM, Number) sp0 " + tooltip_cases('NOSE_TRIM', 3))
    add(trim, 'AIRLINER_TCAS_ABV', 'INI.TOOLTIPS.TCAS_ABV.TITLE', units='numbers',
        set_code=CASE3.format(o='O:T', v='INI_tcas_mode'),
        init="(L:INI_tcas_mode) sp0 l0 (>O:T)",
        tt_value="(B:AIRLINER_TCAS_ABV, Number) sp0 " + tooltip_cases('TCAS_ABV', 3))
    add(overhead, 'AIRLINER_AUTO_PRESS_SELECT', 'INI.TOOLTIPS.AUTO_PRESS_SELECT.TITLE', units='percent',
        set_code="p0 0 max 100 min s0 (>O:A) l0 10 / (>L:INI_CABIN_AUTO_PRESS_ACTUAL)",
        init="(L:INI_CABIN_AUTO_PRESS_ACTUAL) 10 * (>O:A)")
    add(pedestal, 'AIRLINER_ENG1_CUTOFF', 'INI.TOOLTIPS.ENG1_CUTOFF.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:E) 1 (>L:INI_ENG1_MIXTURE_COMMAND) 1 (>O:_ButtonAnimVar)",
        tt_value="(L:INI_MIXTURE_RATIO1_HANDLE) 1 == 0 == if{ 'OFF' } els{ 'ON' }")
    add(pedestal, 'AIRLINER_CPT_VHF_TFR', 'INI.TOOLTIPS.CPT_VHF_TFR.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:C) 1 (>L:INI_CPT_VHF_TRANSFER_SWITCH_COMMAND) 1 (>O:_ButtonAnimVar)",
        tt_value="(L:INI_CPT_VHF_TRANSFER_SWITCH, bool) 0 == if{ 'OFF' } els{ 'ON' }")
    add(pedestal, 'AIRLINER_ECAM_ENG', 'INI.TOOLTIPS.ECAM_ENG.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:G) p0 2 == if{ 1 (>L:PUSH_ECAM_ENG) } els{ 0 (>L:PUSH_ECAM_ENG) }")
    add(pedestal, 'AIRLINER_HEADING_KNOB', 'INI.TOOLTIPS.HEADING_KNOB.TITLE', units='',
        set_code=("p0 p0 0 > if{ 1 (>L:INI_HEADING_DIAL_UP_COMMAND) } els{ 1 (>L:INI_HEADING_DIAL_DN_COMMAND) } "
                  "(O:_KnobAnimVar) 10 p0 * + dnor (>O:_KnobAnimVar)"),
        tt_value="(L:INI_HEADING_DIAL) flr '%ddeg' (F:Format)",
        inc="p0 (>B:AIRLINER_HEADING_KNOB_Set)", dec="p0 -1 * (>B:AIRLINER_HEADING_KNOB_Set)")
    for part, cmd in (('PUSH', 'INI_FCU_SYNC_HEADING_BUTTON'), ('PULL', 'INI_FCU_SELECTED_HEADING_BUTTON')):
        add(pedestal, 'AIRLINER_HEADING_KNOB_' + part, 'INI.TOOLTIPS.HEADING_KNOB.TITLE', units='',
            set_code=("p0 if{ 1 (>L:%s) } 1 (>O:IsPushed) (E:SIMULATION TIME, second) (>O:_LastPushTime) "
                      "(O:IsPulled) if{ 0 (>O:IsPulled) }" % cmd),
            tt_value='INI.TOOLTIPS.HEADING_KNOB.PUSH_VALUE',
            inc='1 (>B:AIRLINER_HEADING_KNOB_%s_Set)' % part.capitalize(),
            dec='0 (>B:AIRLINER_HEADING_KNOB_%s_Set)' % part.capitalize())
    add(pedestal, 'AIRLINER_VOR_CRS_CAPT', 'INI.TOOLTIPS.VOR_CRS_CAPT.TITLE', units='',
        set_code="p0 p0 0 > if{ (>K:VOR1_OBI_INC) } els{ (>K:VOR1_OBI_DEC) } (O:_KnobAnimVar) 10 p0 * + dnor (>O:_KnobAnimVar)",
        tt_value="(A:NAV OBS:1, Degrees) flr '%dDeg' (F:Format)",
        inc="p0 (>B:AIRLINER_VOR_CRS_CAPT_Set)", dec="p0 -1 * (>B:AIRLINER_VOR_CRS_CAPT_Set)")
    add(overhead, 'AIRLINER_RAM_AIR_COVER', 'INI.TOOLTIPS.RAM_AIR_COVER.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:AIRLINER_RAM_AIR_Cover_Position) (O:AIRLINER_RAM_AIR_Cover_Position) s0 l0 0 == if{ 0 (>O:AIRLINER_RAM_AIR_Cover_Position) g1 } :1")
    add(pedestal, 'AIRLINER_GEAR_LEVER', 'INI.TOOLTIPS.GEAR_LEVER.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:L) 1 (>K:GEAR_TOGGLE) 1 (>O:_ButtonAnimVar)", tt_value="'PRESS'")
    add(pedestal, 'AIRLINER_AT_DISCO1', 'INI.TOOLTIPS.AT_DISCO1.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:D) 1 (>K:AUTO_THROTTLE_DISCONNECT) 1 (>O:_ButtonAnimVar)")
    add(overhead, 'AIRLINER_APU_START', 'INI.TOOLTIPS.APU_START.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:S) (L:INI_apu_master_switch, Bool) if{ 1 (>L:INI_APU_START_BUTTON) } 1 (>O:_ButtonAnimVar)")
    add(pedestal, 'AIRLINER_FLAPS', 'INI.TOOLTIPS.FLAPS.TITLE', units='percent',
        set_code="p0 0 max 16384 min (>K:FLAPS_SET) (A:FLAPS HANDLE PERCENT, percent) (>O:F)")
    # A part-marker tag that begins with a digit, as the A300 file has.
    return f.build().replace('<Components Count=', '<0FAB/><Components Count=', 1)


def _map():
    path = os.path.join(tempfile.mkdtemp(), 'A300_Interior.behavior.xml')
    with open(path, 'w', encoding='utf-8') as fh:
        fh.write(_fixture())
    return gen.build_map(gen.read_behavior(path), LOC, '1.0.11')


def _by_id():
    return {c['id']: c for c in _map()['controls']}


class GenerateA300MapTests(unittest.TestCase):
    def test_reads_a_file_with_digit_tags(self):
        self.assertEqual(17, len(_map()['controls']))

    def test_a_knob_push_and_pull_carry_the_knobs_own_words(self):
        self.assertEqual('AIRCRAFT HEADING', _by_id()['AIRLINER_HEADING_KNOB_PUSH']['action'])
        self.assertEqual('HEADING MODE', _by_id()['AIRLINER_HEADING_KNOB_PULL']['action'])

    def test_other_controls_carry_no_action(self):
        self.assertNotIn('action', _by_id()['AIRLINER_StormLight'])
        self.assertNotIn('action', _by_id()['AIRLINER_HEADING_KNOB'])

    def test_a_push_with_no_tooltip_words_has_no_action(self):
        self.assertIsNone(gen.knob_action(LOC, 'INI.TOOLTIPS.CWS_PUSH.TITLE', 'AIRLINER_CWS_PUSH'))
        self.assertIsNone(gen.knob_action(LOC, '', 'AIRLINER_HEADING_KNOB_PUSH'))

    def test_toggle(self):
        c = _by_id()['AIRLINER_StormLight']
        self.assertEqual('toggle', c['kind'])
        self.assertEqual('A300_STORMLIGHT', c['key'])
        self.assertEqual(('OVERHEAD', 'OVERHEAD_LIGHTS'), (c['area'], c['panel']))
        self.assertEqual('STORM LIGHT', c['title'])
        self.assertEqual('AIRLINER_StormLight_Set', c['event'])
        self.assertEqual('L:INI_STORM_LIGHT_SWITCH', c['state_var'])
        self.assertEqual({'0': 'OFF', '1': 'ON'}, c['positions'])

    def test_selector_takes_words_from_the_tooltip_file(self):
        c = _by_id()['AIRLINER_IRS_MODE_SELECT']
        self.assertEqual('selector', c['kind'])
        self.assertEqual('L:INI_IRS_MODE_KNOB', c['state_var'])
        self.assertEqual({'0': 'TEST', '1': 'TK/GS', '2': 'PPOS'}, c['positions'])
        self.assertIsNone(c['values'])
        self.assertEqual('AIRLINER_IRS_MODE_SELECT_Set', c['event'])

    def test_a_neutral_middle_is_a_spring_unless_listed(self):
        trim = _by_id()['AIRLINER_NOSE_TRIM']
        self.assertEqual('spring', trim['kind'])
        self.assertEqual(1, trim['rest'])
        self.assertEqual('selector', _by_id()['AIRLINER_TCAS_ABV']['kind'])

    def test_knob_records_its_scale(self):
        c = _by_id()['AIRLINER_AUTO_PRESS_SELECT']
        self.assertEqual('knob', c['kind'])
        self.assertEqual('L:INI_CABIN_AUTO_PRESS_ACTUAL', c['state_var'])
        self.assertAlmostEqual(0.1, c['scale'])

    def test_command_reads_the_variable_the_tooltip_shows(self):
        c = _by_id()['AIRLINER_ENG1_CUTOFF']
        self.assertEqual('command', c['kind'])
        self.assertEqual('L:INI_MIXTURE_RATIO1_HANDLE', c['state_var'])
        self.assertEqual({'0': 'OFF', '1': 'ON'}, c['positions'])

    def test_a_command_whose_state_is_its_own_animation_is_a_button(self):
        c = _by_id()['AIRLINER_CPT_VHF_TFR']
        self.assertEqual('button', c['kind'])
        self.assertEqual(1, c['press'])

    def test_hold(self):
        c = _by_id()['AIRLINER_ECAM_ENG']
        self.assertEqual('hold', c['kind'])
        self.assertEqual(2, c['press'])

    def test_encoders_name_their_display(self):
        self.assertEqual(('encoder', 'L:INI_HEADING_DIAL'),
                         (_by_id()['AIRLINER_HEADING_KNOB']['kind'], _by_id()['AIRLINER_HEADING_KNOB']['state_var']))
        vor = _by_id()['AIRLINER_VOR_CRS_CAPT']
        self.assertEqual(('encoder', 'A:NAV OBS:1', 'Degrees'), (vor['kind'], vor['state_var'], vor['state_unit']))

    def test_cover(self):
        self.assertEqual('cover', _by_id()['AIRLINER_RAM_AIR_COVER']['kind'])

    def test_stock_toggle_commands_read_the_stock_variable(self):
        c = _by_id()['AIRLINER_GEAR_LEVER']
        self.assertEqual('command', c['kind'])
        self.assertEqual(('A:GEAR HANDLE POSITION', 'Bool'), (c['state_var'], c['state_unit']))
        self.assertEqual({'0': 'UP', '1': 'DOWN'}, c['positions'])

    def test_other_k_event_buttons(self):
        self.assertEqual('button', _by_id()['AIRLINER_AT_DISCO1']['kind'])
        self.assertEqual('button', _by_id()['AIRLINER_APU_START']['kind'])

    def test_a_k_event_lever_is_left_to_the_app(self):
        c = _by_id()['AIRLINER_FLAPS']
        self.assertEqual('none', c['kind'])
        self.assertTrue(c['note'])

    def test_output_is_deterministic(self):
        self.assertEqual(gen.to_json(_map()), gen.to_json(_map()))
        self.assertEqual('1.0.11', json.loads(gen.to_json(_map()))['package_version'])


# The aircraft's checklist in its real shapes (common/Checklist/Airbus_A300_Checklist.xml, package 1.0.11): a
# checkpoint names the cockpit parts it points at (Instrument) and the variables it tests and sets (Code).
_CHECKLIST = """<?xml version="1.0" encoding="utf-8"?>
<SimBase.Document><Checklist.Checklist><Step>
<!-- <Checkpoint ReferenceId="A300.CHECKLISTS.PRELIM.STORM"><Instrument id="StormLight"/></Checkpoint> -->
<Checkpoint Id="A300.CHECKLISTS.PRELIM.TRIM"><Instrument id="NOSE_TRIM"/></Checkpoint>
<Checkpoint Id="A300.CHECKLISTS.PRELIM.HYDP"><Instrument id="HYD_PRESS_B_POINTER"/></Checkpoint>
<Checkpoint Id="A300.CHECKLISTS.PRELIM.STRM"><Sequence SeqType="Parallel"><Test><TestValue>
  <Val Code="(L:INI_STORM_LIGHT_SWITCH, Number) 1 =="/></TestValue>
  <Action Copilot="True" Condition="TestValueFalse" Code="1 (&gt;L:INI_STORM_LIGHT_SWITCH, Number)"/>
</Test></Sequence></Checkpoint>
<Checkpoint Id="A300.CHECKLISTS.TAXI.GEAR"><Action Code="1 (&gt;B:AIRLINER_GEAR_LEVER_Set)"/>
  <Instrument id="StormLight"/></Checkpoint>
</Step></Checklist.Checklist></SimBase.Document>
"""


class ChecklistTests(unittest.TestCase):
    """Each control's first checklist step, which orders a panel's rows as the checklist sets them."""

    def _steps(self):
        data = _map()
        gen.add_checklist_steps(data, _CHECKLIST)
        return {c['id']: c['checklist'] for c in data['controls']}

    def test_a_control_takes_the_first_step_that_names_it(self):
        steps = self._steps()
        # Steps count every live checkpoint, commented ones excluded: TRIM 1, HYDP 2, STRM 3, GEAR 4.
        self.assertEqual(1, steps['AIRLINER_NOSE_TRIM'])               # by the part it points at
        self.assertEqual(3, steps['AIRLINER_StormLight'])              # by the variable it tests, before step 4
        self.assertEqual(4, steps['AIRLINER_GEAR_LEVER'])              # by the event it fires

    def test_a_control_no_step_names_has_none(self):
        self.assertIsNone(self._steps()['AIRLINER_TCAS_ABV'])

    def test_every_control_carries_the_field(self):
        self.assertTrue(all('checklist' in c for c in _map()['controls']))


_TAIL = ' (L:INI_ANNLT_SWITCH) 0 == + 1 min (L:INI_GENERAL_LIGHT_MULTIPLIER) * 1 1 * * {power}'


def _lamp_map():
    """Cockpit lamps in the emissive code's real shapes (package 1.0.11, A300_Interior.behavior.xml)."""
    f = FixtureBuilder()
    root = f.component('A300_INTERIOR_COMPONENT')
    def lamp(node, code):
        f.material(code, owner=f.component(node, root))
    lamp('ENG_1_START_SEQ1_LIGHT', '(L:INI_STARTER1_OPEN)' + _TAIL.format(power='(L:INI_AC_LIGHTS_FAILURE) *'))
    lamp('BATT_1_SEQ2_LIGHT', '(L:INI_BAT1_ON) !' + _TAIL.format(power='(L:INI_DC_LIGHTS_FAILURE) *'))
    lamp('EXT_PWR_SEQ1_LIGHT', '(L:INI_gpu_avail, Bool) (A:EXTERNAL POWER ON:1, Bool) ! and' + _TAIL.format(power='1 *'))
    lamp('GEN_1_SEQ2_LIGHT', '(L:INI_gpu_avail, Bool) (A:EXTERNAL POWER ON:1, Bool) and (L:INI_AC_LIGHTS_FAILURE) *'
         + _TAIL.format(power=''))
    lamp('INDICATOR_LOWER_DOWN1_LIGHT', '(A:GEAR POSITION:1, Percent) 100 ==' + _TAIL.format(power='(L:INI_AC_LIGHTS_FAILURE) *'))
    lamp('FIRE_HANDLE_ENG1_LIGHT', '(L:INI_ENG1_FIRE_TEST, Bool) (L:INI_ANNLT_SWITCH) 0 == or'
         + _TAIL.format(power='(L:INI_DC_LIGHTS_FAILURE) *').replace(' 1 1 * *', ' 0.1 *'))
    lamp('B_RSVR_005_LIGHT', '0' + _TAIL.format(power='(L:INI_AC_LIGHTS_FAILURE) *'))
    # iniBuilds' SELCAL lamps read only the test switch, in a malformed form: a test-only lamp.
    lamp('SELCAL_1_SEQ1_LIGHT', '(L:INI_ANNLT_SWITCH) == 0' + _TAIL.format(power='(L:INI_AC_LIGHTS_FAILURE) *'))
    lamp('OVERHEAD_FUEL', '(L:INI_POTENTIOMETER_3) 0.01 *')   # panel backlight: no annunciator test, not a lamp
    return gen.build_map(Behavior(f.build()), {}, '1.0.11')


def _lamps():
    return {l['node']: l for l in _lamp_map()['lamps']}


class LampTests(unittest.TestCase):
    def test_a_lamp_keeps_its_state_and_its_bus(self):
        self.assertEqual({'node': 'ENG_1_START_SEQ1_LIGHT', 'state': '(L:INI_STARTER1_OPEN)', 'power': 'AC'},
                         _lamps()['ENG_1_START_SEQ1_LIGHT'])

    def test_a_negated_state_is_kept_whole(self):
        self.assertEqual(('(L:INI_BAT1_ON) !', 'DC'),
                         (_lamps()['BATT_1_SEQ2_LIGHT']['state'], _lamps()['BATT_1_SEQ2_LIGHT']['power']))

    def test_a_lamp_with_no_light_power_term_has_none(self):
        lamp = _lamps()['EXT_PWR_SEQ1_LIGHT']
        self.assertEqual('(L:INI_gpu_avail, Bool) (A:EXTERNAL POWER ON:1, Bool) ! and', lamp['state'])
        self.assertIsNone(lamp['power'])

    def test_a_power_term_before_the_test_term_is_lifted_out(self):
        lamp = _lamps()['GEN_1_SEQ2_LIGHT']
        self.assertEqual(('(L:INI_gpu_avail, Bool) (A:EXTERNAL POWER ON:1, Bool) and', 'AC'), (lamp['state'], lamp['power']))

    def test_a_stock_variable_state_is_kept(self):
        self.assertEqual('(A:GEAR POSITION:1, Percent) 100 ==', _lamps()['INDICATOR_LOWER_DOWN1_LIGHT']['state'])

    def test_a_lamp_that_also_lights_in_the_test_drops_the_test_term(self):
        self.assertEqual(('(L:INI_ENG1_FIRE_TEST, Bool)', 'DC'),
                         (_lamps()['FIRE_HANDLE_ENG1_LIGHT']['state'], _lamps()['FIRE_HANDLE_ENG1_LIGHT']['power']))

    def test_a_test_only_lamp_and_a_backlight_are_not_lamps(self):
        self.assertNotIn('B_RSVR_005_LIGHT', _lamps())
        self.assertNotIn('OVERHEAD_FUEL', _lamps())
        self.assertNotIn('SELCAL_1_SEQ1_LIGHT', _lamps())

    def test_lamps_are_sorted_by_node(self):
        nodes = [l['node'] for l in _lamp_map()['lamps']]
        self.assertEqual(sorted(nodes), nodes)


if __name__ == '__main__':
    unittest.main()
