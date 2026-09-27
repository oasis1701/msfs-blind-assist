"""diff_maps: the by-node-id comparison a control-map regeneration is reviewed with.

stdlib unittest only, like the rest of this folder; nothing here reads the real map.
"""
import contextlib
import io
import json
import os
import tempfile
import unittest

import diff_maps as d


def control(node_id, **fields):
    c = {"node_id": node_id, "kind": "button", "label": node_id, "state_var": node_id,
         "value_map": {}, "events": {}}
    c.update(fields)
    return c


def a_map(*controls, **top):
    return {"counts": {"controls": len(controls)}, "controls": list(controls), **top}


class DiffMapsTests(unittest.TestCase):
    def test_identical_maps_have_no_differences(self):
        self.assertEqual([], d.diff_maps(a_map(control("A")), a_map(control("A"))))

    def test_added_and_removed_nodes_are_named_with_their_kind_and_label(self):
        lines = d.diff_maps(a_map(control("A"), control("B")), a_map(control("A"), control("C")))
        self.assertEqual(['REMOVED B (button, "B")', 'ADDED   C (button, "C")'], lines)

    def test_a_changed_field_shows_both_values(self):
        lines = d.diff_maps(a_map(control("A", label="Old")), a_map(control("A", label="New")))
        self.assertEqual(['CHANGED A.label: "Old" -> "New"'], lines)

    def test_a_position_gained_is_a_change_not_a_reorder(self):
        lines = d.diff_maps(a_map(control("H", value_map={"0": "Bottle 1", "2": "Bottle 2"})),
                            a_map(control("H", value_map={"0": "Bottle 1", "1": "Normal", "2": "Bottle 2"})))
        self.assertEqual(['CHANGED H.value_map: {"0": "Bottle 1", "2": "Bottle 2"} -> '
                          '{"0": "Bottle 1", "1": "Normal", "2": "Bottle 2"}'], lines)

    def test_a_reorder_is_reported_apart_from_changes(self):
        lamps = [{"var": "L1", "legend": "ON", "lit": "On"}, {"var": "L2", "legend": "OFF", "lit": "Off"}]
        old = a_map(control("A", value_map={"0": "Off", "1": "On"}, state={"lamps": lamps}), control("B"))
        new = a_map(control("B"), control("A", value_map={"1": "On", "0": "Off"},
                                          state={"lamps": list(reversed(lamps))}))
        lines = d.diff_maps(old, new)
        self.assertIn("ORDER   controls: the surviving nodes are listed in a new order", lines)
        self.assertTrue(any(line.startswith("ORDER   A.value_map:") for line in lines))
        self.assertTrue(any(line.startswith("ORDER   A.state:") for line in lines))
        self.assertEqual(0, d.exit_status(lines))

    def test_a_field_added_to_a_node_is_named(self):
        lines = d.diff_maps(a_map(control("A")), a_map(control("A", state={"lamps": [], "dark": "Off"})))
        self.assertEqual(['ADDED   A.state: {"lamps": [], "dark": "Off"}'], lines)

    def test_top_level_keys_are_compared_entry_by_entry(self):
        old = a_map(control("A"), export_vars=["MD11_X", "MD11_Y"])
        new = a_map(control("A"), export_vars=["MD11_X", "MD11_Z"])
        new["counts"]["empty_case_labels"] = 0
        lines = d.diff_maps(old, new)
        self.assertEqual(["ADDED   counts.empty_case_labels: 0",
                          'REMOVED export_vars: "MD11_Y"',
                          'ADDED   export_vars: "MD11_Z"'], lines)
        self.assertEqual(1, d.exit_status(lines))

    def test_main_prints_every_line_and_a_summary_and_exits_one_on_a_change(self):
        with tempfile.TemporaryDirectory() as tmp:
            paths = []
            for name, label in (("old.json", "Old"), ("new.json", "New")):
                path = os.path.join(tmp, name)
                with open(path, "w", encoding="utf-8") as fh:
                    json.dump(a_map(control("A", label=label)), fh)
                paths.append(path)
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                status = d.main(paths)
        self.assertEqual(1, status)
        self.assertEqual(['CHANGED A.label: "Old" -> "New"', "-- 1 change(s), 0 order-only difference(s)"],
                         out.getvalue().splitlines())


if __name__ == "__main__":
    unittest.main()
