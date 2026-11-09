import contextlib
import io
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import diff_maps


def control(cid, kind='toggle', state='L:X'):
    return {'id': cid, 'key': 'A300_' + cid, 'kind': kind, 'state_var': state, 'positions': {}}


def lamp(node, state='(L:INI_ac_bus1_off, Bool)', power='DC'):
    return {'node': node, 'state': state, 'power': power}


def run_main(old, new):
    """(exit status, printed text) of `diff_maps.main` on two maps written to a scratch folder."""
    with tempfile.TemporaryDirectory() as folder:
        paths = []
        for name, content in (('old.json', old), ('new.json', new)):
            path = os.path.join(folder, name)
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(content, f)
            paths.append(path)
        printed = io.StringIO()
        with contextlib.redirect_stdout(printed):
            status = diff_maps.main(paths)
    return status, printed.getvalue()


class DiffMapsTests(unittest.TestCase):
    def test_identical_maps(self):
        m = {'controls': [control('A')]}
        self.assertEqual([], diff_maps.diff(m, m))

    def test_added_removed_and_changed(self):
        old = {'controls': [control('A'), control('B')]}
        new = {'controls': [control('A', state='L:Y'), control('C', 'button')]}
        lines = diff_maps.diff(old, new)
        self.assertIn('added   C (button)', lines)
        self.assertIn('removed B (toggle)', lines)
        self.assertIn('changed A state_var: "L:X" -> "L:Y"', lines)

    def test_a_changed_action_is_reported(self):
        old = {'controls': [dict(control('A'), action='AIRCRAFT HEADING')]}
        new = {'controls': [dict(control('A'), action='HEADING')]}
        self.assertIn('changed A action: "AIRCRAFT HEADING" -> "HEADING"', diff_maps.diff(old, new))

    def test_a_changed_checklist_step_is_reported(self):
        # It moves the row within its panel.
        old = {'controls': [dict(control('A'), checklist=None)]}
        new = {'controls': [dict(control('A'), checklist=12)]}
        self.assertIn('changed A checklist: null -> 12', diff_maps.diff(old, new))

    def test_a_changed_lamp_rule_or_bus_is_reported(self):
        # A light is lit by its rule AND its bus's light power: either one changing changes when it speaks.
        old = {'controls': [], 'lamps': [lamp('GEN_1_FAULT_LIGHT', '(L:INI_GEN1_FAULT)', 'DC')]}
        new = {'controls': [], 'lamps': [lamp('GEN_1_FAULT_LIGHT', '(L:INI_GEN1_FAULT) !', 'AC')]}
        self.assertEqual(['changed lamp GEN_1_FAULT_LIGHT state: "(L:INI_GEN1_FAULT)" -> "(L:INI_GEN1_FAULT) !"',
                          'changed lamp GEN_1_FAULT_LIGHT power: "DC" -> "AC"'],
                         diff_maps.diff(old, new))

    def test_an_added_or_removed_lamp_is_reported(self):
        old = {'controls': [], 'lamps': [lamp('GONE_LIGHT'), lamp('KEPT_LIGHT')]}
        new = {'controls': [], 'lamps': [lamp('KEPT_LIGHT'), lamp('NEW_LIGHT')]}
        self.assertEqual(['added   lamp NEW_LIGHT', 'removed lamp GONE_LIGHT'], diff_maps.diff(old, new))

    def test_a_changed_note_is_reported(self):
        old = {'controls': [dict(control('A'), note=None)]}
        new = {'controls': [dict(control('A'), note='no write the map can drive')]}
        self.assertEqual(['changed A note: null -> "no write the map can drive"'], diff_maps.diff(old, new))

    def test_a_changed_package_version_is_reported(self):
        old = {'package_version': '1.0.11', 'controls': [], 'lamps': []}
        new = {'package_version': '1.0.12', 'controls': [], 'lamps': []}
        self.assertEqual(['changed package_version: "1.0.11" -> "1.0.12"'], diff_maps.diff(old, new))

    def test_a_field_only_one_map_has_is_reported(self):
        # Every field is compared, not a fixed list: one a regeneration adds still shows.
        old = {'controls': [control('A')], 'lamps': [lamp('N')]}
        new = {'controls': [dict(control('A'), guard='G')], 'lamps': [dict(lamp('N'), flashes=True)]}
        self.assertEqual(['changed A guard: null -> "G"', 'changed lamp N flashes: null -> true'],
                         diff_maps.diff(old, new))

    def test_a_reordered_control_list_is_reported(self):
        # The array order is the row order of the panel rows the checklist does not name.
        old = {'controls': [control('A'), control('B')]}
        new = {'controls': [control('B'), control('A')]}
        line = 'order   controls: the surviving controls are listed in a new order'
        self.assertEqual([line], diff_maps.diff(old, new))
        self.assertEqual((1, line + '\n'), run_main(old, new))

    def test_only_the_order_of_the_shared_controls_counts(self):
        # A control added between two others moves none of the shared ones, and lamps are looked
        # up by node, so their order is not reported.
        old = {'controls': [control('A'), control('B')], 'lamps': [lamp('N'), lamp('M')]}
        new = {'controls': [control('A'), control('X'), control('B')], 'lamps': [lamp('M'), lamp('N')]}
        self.assertEqual(['added   X (toggle)'], diff_maps.diff(old, new))

    def test_exit_status_is_1_on_a_change_and_0_on_none(self):
        self.assertEqual(0, diff_maps.exit_status([]))
        self.assertEqual(1, diff_maps.exit_status(['changed package_version: "1.0.11" -> "1.0.12"']))
        same = {'controls': [control('A')], 'lamps': [lamp('N')]}
        self.assertEqual((0, 'no differences\n'), run_main(same, same))
        moved = {'controls': [control('A')], 'lamps': [lamp('N', power='AC')]}
        self.assertEqual((1, 'changed lamp N power: "DC" -> "AC"\n'), run_main(same, moved))


if __name__ == '__main__':
    unittest.main()
