import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from behavior_xml import Behavior
from a300_fixture import FixtureBuilder


def _storm_light_fixture():
    f = FixtureBuilder()
    root = f.component('A300_INTERIOR_COMPONENT')
    area = f.component('OVERHEAD', root)
    panel = f.component('OVERHEAD_LIGHTS', area)
    node = f.component('STORMLIGHT', panel)
    f.mouserect(node, 'STORMLIGHT', '(>B:AIRLINER_StormLight_Toggle)',
                title='INI.TOOLTIPS.StormLight.TITLE', entries=['AIRLINER_StormLight'])
    f.inputevent(node, 'AIRLINER_STORMLIGHT',
                 set_code="p0 1 min 0 max (>O:P) (L:INI_STORM_LIGHT_SWITCH) ! (>L:INI_STORM_LIGHT_SWITCH)",
                 tt_value="(L:INI_STORM_LIGHT_SWITCH) 0 == if{ 'OFF' } els{ 'ON' }",
                 units='Boolean', tt_desc='INI.TOOLTIPS.StormLight.TITLE',
                 inc="(O:P) p0 + (>B:AIRLINER_StormLight_Set)")
    return f.build()


class BehaviorXmlTests(unittest.TestCase):
    def test_components_and_paths(self):
        b = Behavior(_storm_light_fixture())
        self.assertEqual(['A300_INTERIOR_COMPONENT', 'OVERHEAD', 'OVERHEAD_LIGHTS', 'STORMLIGHT'], b.path(3))

    def test_mouserect_title_and_entry(self):
        mr = Behavior(_storm_light_fixture()).mouserects[0]
        self.assertEqual('STORMLIGHT', mr['highlight'])
        self.assertEqual(['INI.TOOLTIPS.StormLight.TITLE'], mr['titles'])
        self.assertEqual(['AIRLINER_StormLight'], mr['entries'])

    def test_inputevent_codes_are_unescaped(self):
        ie = Behavior(_storm_light_fixture()).inputevents[0]
        self.assertEqual('AIRLINER_STORMLIGHT', ie['id'])
        self.assertIn('! (>L:INI_STORM_LIGHT_SWITCH)', ie['set_code'])
        self.assertIn("'OFF'", ie['tt_value'])
        self.assertIn('(>B:AIRLINER_StormLight_Set)', ie['inc_code'])

    def test_input_event_lookup_ignores_case(self):
        b = Behavior(_storm_light_fixture())
        self.assertEqual('AIRLINER_STORMLIGHT', b.inputevent('airliner_stormlight')['id'])

    def test_an_emissive_material_keeps_the_node_it_lights(self):
        f = FixtureBuilder()
        root = f.component('A300_INTERIOR_COMPONENT')
        lamp = f.component('ENG_1_START_SEQ1_LIGHT', root)
        f.material('(L:INI_STARTER1_OPEN) (L:INI_ANNLT_SWITCH) 0 == + 1 min', owner=lamp)
        b = Behavior(f.build())
        self.assertEqual([{'owner': 1, 'code': '(L:INI_STARTER1_OPEN) (L:INI_ANNLT_SWITCH) 0 == + 1 min'}],
                         b.emissive)


if __name__ == '__main__':
    unittest.main()
