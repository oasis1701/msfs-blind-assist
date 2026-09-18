"""Label and structure repairs in the MD-11 control-map generator.

Pure-function tests on tiny fixtures: no aircraft package, no wasm. Every case is a
defect seen in the shipped map (see docs/md11.md, "Labels").
"""
import contextlib
import io
import json
import os
import sys
import tempfile
import unittest
from unittest import mock

import generate_md11_map as g
import md11_paths


def ctl(node_id, kind="button", label=None, events=None, state_var=None, value_map=None,
        guard_id=None, area=None, source="FlightDeck/Overhead.xml"):
    return {
        "node_id": node_id, "kind": kind, "template": "", "area": area or g.area_of(node_id),
        "label": label, "label_source": "tooltip" if label else "derived",
        "state_var": state_var or node_id, "value_map": value_map or {},
        "num_states": None, "events": events or {}, "guard_id": guard_id, "source": source,
    }


def use_template(template, **fields):
    """One ModelBehaviorDefs <UseTemplate> block, its fields in the order given."""
    inner = "".join(f"<{name}>{value}</{name}>" for name, value in fields.items())
    return f'<UseTemplate Name="{template}">{inner}</UseTemplate>'


def write_package(root, files):
    """A minimal MD-11 package under `root`, for tests that run collect() or main() end to end.

    `files` maps a path under ModelBehaviorDefs/TFDi_Design/MD11 ('FlightDeck/Overhead.xml') to the
    XML inside its <ModelBehaviors> root. The wasm names one control var, which is all
    _exit_if_incomplete asks for. Returns (package dir, wasm path)."""
    pkg = os.path.join(root, "pkg")
    base = os.path.join(pkg, md11_paths.PACKAGE_MARKER)
    for rel, xml in files.items():
        path = os.path.join(base, *rel.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write("<ModelBehaviors>\n" + xml + "\n</ModelBehaviors>\n")
    wasm = os.path.join(root, "md11host.wasm")
    with open(wasm, "wb") as fh:
        fh.write(b"\0asm Aircraft::vars->MD11_OVHD_ELEC_X_OFF_LT\0")
    return pkg, wasm


def run_main(pkg, wasm, out):
    """generate_md11_map.main() on a fixture package: (the map it wrote, what it printed to stderr)."""
    err = io.StringIO()
    argv = ["generate_md11_map.py", "--pkg", pkg, "--wasm", wasm, "--out", out]
    with mock.patch.object(sys, "argv", argv), \
         contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(err):
        g.main()
    with open(out, encoding="utf-8") as fh:
        return json.load(fh), err.getvalue()


class SpeakableTests(unittest.TestCase):
    def test_html_entities_are_decoded_and_the_arrow_reads_as_to(self):
        self.assertEqual("Air System 1 to 2 Isolation Valve",
                         g.speakable("Air System 1&lt;-&gt;2 Isolation Valve"))

    def test_degrees_still_spelled_out(self):
        self.assertEqual("5 degrees", g.speakable("5°"))


class ParenthesisTests(unittest.TestCase):
    def test_balanced_trailing_parenthetical_is_kept(self):
        label, _, _ = g.parse_tooltip("APU Generator (APU Panel)")
        self.assertEqual("APU Generator (APU Panel)", label)

    def test_wrapping_parens_are_stripped_as_a_pair(self):
        self.assertEqual("Strobes", g.strip_outer_parens("(Strobes)"))
        self.assertEqual("High Intensity Lights (Strobes)",
                         g.strip_outer_parens("High Intensity Lights (Strobes)"))


class StrayPercentTests(unittest.TestCase):
    """The aircraft ships one tooltip with a '%' directly before a branch literal — a typo, not
    syntax (the directives are '%(', '%{', '%!' and the literal '%%'). Left unparsed it gave the
    Auxiliary IRS selector no positions, and MSFSBA rendered the third IRS switch read-only."""

    def test_a_stray_percent_before_a_branch_literal_is_dropped(self):
        label, var, value_map = g.parse_tooltip(
            "Auxiliary IRS (%((L:MD11_OVHD_IRS_3_KB))%{if}%Nav%{else}Off%{end})")
        self.assertEqual("Auxiliary IRS", label)
        self.assertEqual("MD11_OVHD_IRS_3_KB", var)
        self.assertEqual({"1": "Nav", "0": "Off"}, value_map)

    def test_a_literal_percent_sign_is_still_dropped_as_live_data_not_as_a_typo(self):
        # '%%' is a real percent sign after a number ('%!d!%%'); the brightness knobs use it.
        # Their tooltips carry no positions before or after the fix.
        label, var, value_map = g.parse_tooltip(
            "DU1 Brightness (%((L:MD11_PED_DU1_BRT_KB) 10 *)%!d!%%)")
        self.assertEqual("DU1 Brightness", label)
        self.assertEqual("MD11_PED_DU1_BRT_KB", var)
        self.assertEqual({}, value_map)

    def test_a_triple_percent_before_a_directive_is_untouched(self):
        # '%%%{else}': a literal '%%' straight before a directive — the audio volume knobs.
        # Neither '%' before the '{' is a stray sign, and the knob still yields NO positions
        # (its state is a live number).
        label, _, value_map = g.parse_tooltip(
            "Captain VHF1 Volume (%((L:MD11_PED_CPT_AUDIO_PNL_VHF1_VOL_BT))%{if}"
            "%((L:MD11_PED_CPT_AUDIO_PNL_VHF1_VOL_KB) 10 *)%!d!%%%{else}Disabled%{end})")
        self.assertEqual("Captain VHF1 Volume", label)
        self.assertEqual({}, value_map)


class CompanionVarTests(unittest.TestCase):
    """A trailing if/else keyed on a SECOND L:var describes that var, not this control. The EFIS
    minimums caps read their own value and then the mode switch's var for the Baro/Radio word;
    lifting that word gave a 0-15000 ft value knob a {Radio, Baro} map and MSFSBA a two-entry
    combo whose wheel moved the minimums, never the mode."""

    def test_if_else_words_keyed_on_a_second_var_are_not_this_controls_positions(self):
        label, var, value_map = g.parse_tooltip(
            "Captain Minimums Setting (%((L:MD11_CAP_MINIMUMS))%!d! "
            "%((L:MD11_LECP_MINIMUMS_KB))%{if}Baro%{else}Radio%{end})")
        self.assertEqual("Captain Minimums Setting", label)
        self.assertEqual("MD11_CAP_MINIMUMS", var)
        self.assertEqual({}, value_map)

    def test_if_else_words_on_the_state_var_itself_are_still_positions(self):
        # The mode switch's own tooltip: one var, and the words are its positions.
        label, var, value_map = g.parse_tooltip(
            "Captain Minimums Mode (%((L:MD11_LECP_MINIMUMS_KB))%{if}Baro%{else}Radio%{end})")
        self.assertEqual("Captain Minimums Mode", label)
        self.assertEqual("MD11_LECP_MINIMUMS_KB", var)
        self.assertEqual({"1": "Baro", "0": "Radio"}, value_map)

    def test_a_nested_two_var_expression_also_yields_no_positions(self):
        # The third and fourth controls the rule touches, deliberately: the ECON and TRIM AIR
        # buttons' tooltips NEST the air-system selector around their own var, so the expression
        # names two L:vars and the Off/On words are dropped with the caps'. Nothing consumes
        # them — a button never reads `values`, and the `state` block that composes its spoken
        # position is generated separately and is unchanged.
        label, var, value_map = g.parse_tooltip(
            "ECON Mode (%((L:MD11_OVHD_PNEU_SYSTEM_SEL_BT))%{if}"
            "%((L:MD11_OVHD_PNEU_ECON_BT))%{if}Off%{else}On%{end}%{else}Auto%{end})")
        self.assertEqual("ECON Mode", label)
        self.assertEqual("MD11_OVHD_PNEU_SYSTEM_SEL_BT", var)
        self.assertEqual({}, value_map)


class CuratedGuardTests(unittest.TestCase):
    """
    A guard cover TFDi's XML does not link to the control it covers. The generator never infers a
    link -- it reads GUARD_ID off the exported field -- so an undeclared cover leaves its control
    with guard_id None, MSFSBA's auto-open never runs, and the walk goes out against a closed cover
    and reports "did not move". CURATED_GUARDS supplies the three measured links.
    """

    def test_a_declared_guard_id_is_left_alone(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, {"FlightDeck/Overhead.xml": use_template(
                "TFDi_Design_MD11_Switch_Template", NODE_ID="MD11_OVHD_X_SW", TOOLTIPID="X",
                LEFT_BUTTON_DOWN="1", GUARD_ID="MD11_OVHD_X_GRD")})
            data, _ = run_main(pkg, wasm, os.path.join(tmp, "map.json"))
        self.assertEqual("MD11_OVHD_X_GRD", data["controls"][0]["guard_id"])

    def test_an_undeclared_cover_gets_its_curated_link(self):
        # The real GPWS switch: measured live, its cover blocks it and the guard's own event lifts
        # the cover, but TFDi's XML declares no GUARD_ID for it.
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, {"FlightDeck/Overhead.xml": use_template(
                "TFDi_Design_MD11_Switch_Template", NODE_ID="MD11_AOVHD_GPWS_SW", TOOLTIPID="GPWS",
                LEFT_BUTTON_DOWN="73769")})
            data, _ = run_main(pkg, wasm, os.path.join(tmp, "map.json"))
        self.assertEqual("MD11_AOVHD_GPWS_GRD", data["controls"][0]["guard_id"])

    def test_a_control_with_neither_keeps_none(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, {"FlightDeck/Overhead.xml": use_template(
                "TFDi_Design_MD11_Switch_Template", NODE_ID="MD11_OVHD_Y_SW", TOOLTIPID="Y",
                LEFT_BUTTON_DOWN="1")})
            data, _ = run_main(pkg, wasm, os.path.join(tmp, "map.json"))
        self.assertIsNone(data["controls"][0]["guard_id"])

    def test_the_curated_set_is_the_three_measured_pairs(self):
        self.assertEqual(
            {
                "MD11_AOVHD_EVAC_SW": "MD11_AOVHD_EVAC_GRD",
                "MD11_AOVHD_GPWS_SW": "MD11_AOVHD_GPWS_GRD",
                "MD11_EXT_DOOR_CRG_MAIN_ARM_SW": "MD11_EXT_DOOR_CRG_MAIN_ARM_GRD",
            },
            g.CURATED_GUARDS,
        )


class ComputedHeadTests(unittest.TestCase):
    """D16: a block's words describe the value of the RPN in front of it, so they are the
    VARIABLE's positions only when that RPN is a BARE READ -- '(L:NAME)' and nothing else.

    The gear lever is the casualty that named the rule. CenterInstrument.xml tests
    '(L:MD11_MIP_GEAR_SW) 20 >=' for Down, and the flat lift wrote {1 Down, 0 Up} over a variable
    that is really the lever's 0-25 TRAVEL. Nothing in the repo could detect it: the generator
    keeps no raw tooltip, emits no statistic for a computed head, and a two-entry map looks
    entirely ordinary -- so it was found by a blind pilot hearing the wrong position, and patched
    one control at a time in C# (Md11GearLever).

    It sits BESIDE the companion-var rule rather than replacing it (CompanionVarTests): that one
    refuses an expression naming a SECOND var, this one refuses a head that COMPUTES. The EFIS
    minimums caps are refused by the first with a head that would pass the second.

    The nested-inline shape is pinned next door: the APU fire handle's centre position is a whole
    %{if} block, and it carries a head of its own that must not split the case it names
    (CaseLabelTests / CompositeTests both assert its three positions)."""

    # Verbatim from the package; '&gt;' because read_xml does not unescape.
    GEAR = "Gear Lever (%((L:MD11_MIP_GEAR_SW) 20 &gt;=)%{if}Down%{else}Up%{end})"
    FLAP = ("Flaps/Slats (%(38 65 (L:MD11_FLAP_RNG) rng)%{if}Dial-A-Flap "
            "%(10 (L:MD11_DIALAFLAP_IND_RNG) 6.6667 / +)%!d!/Extended%{else}"
            "%((L:MD11_FLAP_RNG))%{case}%{:0}Up/Retracted%{:20}Up/Extended%{:70}28/Extended"
            "%{:82}35/Extended%{:100}50/Extended%{end}%{end})")
    WXR_GAIN = ("Weather Radar Gain (%(1 7 (L:MD11_PED_WXR_GAIN_KB) rng)%{if}"
                "%((L:MD11_PED_WXR_GAIN_KB) 8 -)%!d!%{else}%((L:MD11_PED_WXR_GAIN_KB))%{case}"
                "%{:0}Minimum%{:8}Maximum%{:9}Calibration%{end}%{end})")

    def test_the_gear_levers_comparison_is_no_boolean_map_over_its_travel(self):
        threshold, withheld = {}, []
        label, var, value_map = g.parse_tooltip(self.GEAR, node_id="MD11_MIP_GEAR_SW",
                                                threshold=threshold, withheld=withheld)
        self.assertEqual("Gear Lever", label)
        self.assertEqual("MD11_MIP_GEAR_SW", var)
        self.assertEqual({}, value_map)
        # The comparison is recorded instead: the rule the AIRCRAFT applies, so one reader can
        # serve every such control rather than a hand-written class per casualty.
        self.assertEqual({"var": "MD11_MIP_GEAR_SW", "op": ">=", "value": 20,
                          "when_true": "Down", "when_false": "Up"}, threshold)
        self.assertEqual([], withheld)   # recorded, so not also reported as lost

    def test_the_same_comparison_written_without_entities_reads_the_same(self):
        # parse_tooltip is called directly (here and by curation work) as well as on read_xml's
        # output, so both spellings of the operator must reach the same threshold.
        threshold = {}
        g.parse_tooltip("Gear Lever (%((L:MD11_MIP_GEAR_SW) 20 >=)%{if}Down%{else}Up%{end})",
                        node_id="MD11_MIP_GEAR_SW", threshold=threshold)
        self.assertEqual({"var": "MD11_MIP_GEAR_SW", "op": ">=", "value": 20,
                          "when_true": "Down", "when_false": "Up"}, threshold)

    def test_a_bare_var_read_still_gives_the_control_its_positions(self):
        threshold, withheld = {}, []
        label, var, value_map = g.parse_tooltip(
            "Nosewheel Steering (%((L:MD11_PED_NWS_SW))%{if}On%{else}Off%{end})",
            node_id="MD11_PED_NWS_SW", threshold=threshold, withheld=withheld)
        self.assertEqual("Nosewheel Steering", label)
        self.assertEqual("MD11_PED_NWS_SW", var)
        self.assertEqual({"1": "On", "0": "Off"}, value_map)
        self.assertEqual(({}, []), (threshold, withheld))

    def test_a_bare_case_head_still_gives_the_control_its_positions(self):
        withheld = []
        _, var, value_map = g.parse_tooltip(
            "Test Selector (%((L:MD11_OVHD_X_SEL_SW))%{case}%{:0}Off%{:1}Auto%{:2}On%{end})",
            node_id="MD11_OVHD_X_SEL_SW", withheld=withheld)
        self.assertEqual("MD11_OVHD_X_SEL_SW", var)
        self.assertEqual({"0": "Off", "1": "Auto", "2": "On"}, value_map)
        self.assertEqual([], withheld)

    def test_the_companion_var_rule_keeps_its_own_refusal(self):
        # The EFIS minimums cap: its %{if} head IS a bare read, so D16 would lift the mode
        # switch's words onto a 0-15000 ft value knob. The second var in the expression is what
        # refuses it, exactly as before -- the two rules catch different things.
        threshold, withheld = {}, []
        _, var, value_map = g.parse_tooltip(
            "Captain Minimums Setting (%((L:MD11_CAP_MINIMUMS))%!d! "
            "%((L:MD11_LECP_MINIMUMS_KB))%{if}Baro%{else}Radio%{end})",
            node_id="MD11_LECP_MINIMUMS_CAP", threshold=threshold, withheld=withheld)
        self.assertEqual("MD11_CAP_MINIMUMS", var)
        self.assertEqual({}, value_map)
        self.assertEqual(({}, []), (threshold, withheld))

    def test_a_case_under_a_computed_head_is_refused_and_reported(self):
        # The %{case} half of the same defect. No tooltip in the package is shaped this way today;
        # flat, one would ship a two-entry map over a variable that holds neither value, and in
        # silence -- which is the whole reason the gear lever survived review.
        withheld = []
        _, _, value_map = g.parse_tooltip(
            "Test Lever (%((L:MD11_X_RNG) 20 &gt;=)%{case}%{:0}Up%{:1}Down%{end})",
            node_id="MD11_X_RNG", withheld=withheld)
        self.assertEqual({}, value_map)
        self.assertEqual(["%{case} on (L:MD11_X_RNG) 20 >="], withheld)

    def test_a_computed_head_that_is_no_threshold_is_refused_never_guessed(self):
        # TFDi's rudder-trim shape: a distance from centre, not a threshold. Refusing is the point
        # -- a counted refusal is visible, a wrong value_map is not.
        threshold, withheld = {}, []
        _, _, value_map = g.parse_tooltip(
            "Rudder Trim (%((L:MD11_PED_RUD_TRIM_IND) 25 - abs 0.1 &lt;)"
            "%{if}Neutral%{else}Offset%{end})",
            node_id="MD11_PED_RUD_TRIM_SW", threshold=threshold, withheld=withheld)
        self.assertEqual({}, value_map)
        self.assertEqual({}, threshold)
        self.assertEqual(["%{if} on (L:MD11_PED_RUD_TRIM_IND) 25 - abs 0.1 <"], withheld)

    def test_a_bare_headed_case_under_a_computed_if_keeps_its_positions(self):
        # The two levers the rule must NOT touch: a block is judged by its OWN head, not by the
        # outermost one. The flap lever's positions hang off '%((L:MD11_FLAP_RNG))%{case}' nested
        # inside a range test, and the weather-radar gain's off its own var inside another.
        for tooltip, node, var, positions in (
            (self.FLAP, "MD11_FLAP_LATCH", "MD11_FLAP_RNG",
             {"0": "Up/Retracted", "20": "Up/Extended", "70": "28/Extended",
              "82": "35/Extended", "100": "50/Extended"}),
            (self.WXR_GAIN, "MD11_PED_WXR_GAIN_KB", "MD11_PED_WXR_GAIN_KB",
             {"0": "Minimum", "8": "Maximum", "9": "Calibration"}),
        ):
            with self.subTest(node):
                withheld = []
                _, got_var, value_map = g.parse_tooltip(tooltip, node_id=node, withheld=withheld)
                self.assertEqual(var, got_var)
                self.assertEqual(positions, value_map)
                # The computed outer block carries no positions of its own, so nothing was lost:
                # reporting it would bury the controls that really did lose words.
                self.assertEqual([], withheld)

    def test_an_inline_label_block_under_a_computed_head_lifts_no_positions(self):
        # Shape (b), where the label itself changes with state. The collapsed wording is still the
        # best spoken name -- the label is unchanged -- but the words are not the variable's
        # positions, for exactly the reason the trailing block's are not.
        withheld = []
        label, _, value_map = g.parse_tooltip(
            "Gear %((L:MD11_MIP_GEAR_SW) 20 &gt;=)%{if}Down%{else}Up%{end} Lever",
            node_id="MD11_MIP_GEAR_SW", withheld=withheld)
        self.assertEqual("Gear Up/Down Lever", label)
        self.assertEqual({}, value_map)
        self.assertEqual(["inline %{if} on (L:MD11_MIP_GEAR_SW) 20 >="], withheld)

    def test_only_a_single_var_comparison_reads_as_a_threshold(self):
        self.assertEqual({"var": "MD11_X", "op": ">=", "value": 20},
                         g._threshold("(L:MD11_X) 20 &gt;="))
        self.assertEqual({"var": "MD11_X", "op": "<", "value": 2.5},
                         g._threshold("(L:MD11_X) 2.5 &lt;"))
        self.assertEqual({"var": "MD11_X", "op": "==", "value": -1},
                         g._threshold(" (L:MD11_X) -1 == "))
        # A range, a distance from centre and a two-var test are not thresholds, and guessing at
        # one would be worse than the refusal it replaced.
        self.assertIsNone(g._threshold("38 65 (L:MD11_FLAP_RNG) rng"))
        self.assertIsNone(g._threshold("(L:MD11_X) 25 - abs 0.1 &lt;"))
        self.assertIsNone(g._threshold("(L:MD11_A) 1 == (L:MD11_B) 0 == and"))
        self.assertIsNone(g._threshold("(L:MD11_X)"))

    def test_main_counts_and_prints_the_threshold(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, {"FlightDeck/CenterInstrument.xml": use_template(
                "TFDi_Design_MD11_Switch_SingleEvent_Template", NODE_ID="MD11_MIP_GEAR_SW",
                TOOLTIPID=self.GEAR, LEFT_BUTTON_DOWN="94976")})
            data, err = run_main(pkg, wasm, os.path.join(tmp, "map.json"))
        self.assertEqual(1, data["counts"]["rpn_thresholds"])
        self.assertEqual(0, data["counts"]["unkeyed_words"])
        self.assertIn("MD11_MIP_GEAR_SW -- (L:MD11_MIP_GEAR_SW) >= 20", err)
        gear = data["controls"][0]
        self.assertEqual({"var": "MD11_MIP_GEAR_SW", "op": ">=", "value": 20,
                          "when_true": "Down", "when_false": "Up"}, gear["threshold"])
        # The PARSE refuses to key a map on the comparison's variable, which is the fix — but the
        # gear's two POSITIONS are still what its combo offers and what a pick writes, so CURATED
        # pins them back. Without that the empty map makes the row read-only (values.Count == 0)
        # and leaves the walker nothing to walk. What the parse no longer does is CLASSIFY a
        # reading onto one of those keys; that is Md11GearLever's job until a reader consumes
        # `threshold` generically.
        self.assertEqual({"0": "Up", "1": "Down"}, gear["value_map"])
        self.assertEqual({"0": "Up", "1": "Down"}, g.CURATED["MD11_MIP_GEAR_SW"]["value_map"])

    def test_main_counts_and_prints_a_block_it_could_not_key(self):
        tooltip = ("Test Lever (%((L:MD11_OVHD_X_RNG) 25 - abs 0.1 &lt;)"
                   "%{if}Neutral%{else}Offset%{end})")
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, {"FlightDeck/Overhead.xml": use_template(
                "TFDi_Design_MD11_Switch_Template", NODE_ID="MD11_OVHD_X_SW", TOOLTIPID=tooltip,
                LEFT_BUTTON_DOWN="1")})
            data, err = run_main(pkg, wasm, os.path.join(tmp, "map.json"))
        self.assertEqual(0, data["counts"]["rpn_thresholds"])
        self.assertEqual(1, data["counts"]["unkeyed_words"])
        self.assertIn("MD11_OVHD_X_SW %{if} on (L:MD11_OVHD_X_RNG) 25 - abs 0.1 <", err)
        self.assertEqual({}, data["controls"][0]["value_map"])
        self.assertNotIn("threshold", data["controls"][0])


class FinalizeTests(unittest.TestCase):
    def test_guard_is_named_after_the_control_it_covers(self):
        out = g.finalize_controls([
            ctl("MD11_OVHD_ELEC_BATT_BT", label="Battery", guard_id="MD11_OVHD_ELEC_BATT_GRD",
                events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2}),
            ctl("MD11_OVHD_ELEC_BATT_GRD", kind="guard", label="Battery", events={"LEFT_BUTTON_DOWN": 3}),
        ])
        labels = {c["node_id"]: c["label"] for c in out}
        self.assertEqual("Battery", labels["MD11_OVHD_ELEC_BATT_BT"])
        self.assertEqual("Battery guard", labels["MD11_OVHD_ELEC_BATT_GRD"])

    def test_breaker_carries_its_grid_position_and_drops_the_word_breaker(self):
        out = g.finalize_controls([ctl("MD11_BKR_BWU_C24", label="Tank 1 Transfer Pump Power Breaker",
                                       value_map={"1": "Pulled", "0": "Pushed"})])
        self.assertEqual("C24 Tank 1 Transfer Pump Power", out[0]["label"])

    def test_second_clickspot_of_one_button_is_dropped(self):
        ev = {"LEFT_BUTTON_DOWN": 90297, "LEFT_BUTTON_UP": 90298}
        out = g.finalize_controls([
            ctl("MD11_OVHD_PNEU_ECON_BT", label="ECON Mode", events=ev),
            ctl("MD11_OVHD_PNEU_ECON_BT001", label="ECON Mode", events=ev),
            ctl("MD11_OVHD_LTS_CREW_REST_BT", label="Crew Rest Call", events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2}),
            ctl("MD11_OVHD_LTS_CREW_REST_BT_F", label="Crew Rest Call", events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2}),
        ])
        self.assertEqual(["MD11_OVHD_PNEU_ECON_BT", "MD11_OVHD_LTS_CREW_REST_BT"], [c["node_id"] for c in out])

    def test_two_nodes_firing_the_same_events_are_one_control(self):
        # An event id IS the action on this aircraft, so identical events mean one physical
        # control reached from two 3D nodes — however differently the nodes are named. The
        # MD11_-prefixed id wins over a raw 3D name; between two MD11_ ids the shortest wins.
        out = g.finalize_controls([
            ctl("GA_BT_ALT", events={"LEFT_BUTTON_DOWN": 77851, "LEFT_BUTTON_UP": 77852}),
            ctl("MD11_THR_GA_BT", label="Go Around Mode", events={"LEFT_BUTTON_DOWN": 77851, "LEFT_BUTTON_UP": 77852}),
            ctl("MD11_EFB_TOGGLE", events={"LEFT_BUTTON_DOWN": 94465}),
            ctl("MD11_EFB_TOGGLE_FO", events={"LEFT_BUTTON_DOWN": 94465}),
        ])
        self.assertEqual(["MD11_THR_GA_BT", "MD11_EFB_TOGGLE"], [c["node_id"] for c in out])
        # And the surviving EFB row no longer claims a seat the events do not distinguish.
        self.assertEqual("EFB Toggle", out[1]["label"])

    def test_the_survivor_does_not_depend_on_the_order_nodes_were_collected_in(self):
        ev = {"LEFT_BUTTON_DOWN": 94465}
        pair = [ctl("MD11_EFB_TOGGLE_FO", events=ev), ctl("MD11_EFB_TOGGLE", events=ev)]
        self.assertEqual(["MD11_EFB_TOGGLE"], [c["node_id"] for c in g.finalize_controls(pair)])

    def test_a_control_with_no_events_is_never_deduped(self):
        # Annunciators and any node the exporter gave no events must not collapse onto each
        # other just because both maps are empty.
        out = g.finalize_controls([ctl("MD11_A_SW", kind="switch", events={}), ctl("MD11_B_SW", kind="switch", events={})])
        self.assertEqual(2, len(out))

    def test_distinct_events_are_not_duplicates(self):
        out = g.finalize_controls([
            ctl("MD11_LYOKE_TRIM_SW", kind="switch", label="Captain Elevator Trim Switch", events={"LEFT_BUTTON_DOWN": 1}),
            ctl("MD11_LYOKE_TRIM_SW001", kind="switch", label="First Officer Elevator Trim Switch", events={"LEFT_BUTTON_DOWN": 9}),
        ])
        self.assertEqual(2, len(out))

    def test_second_lamp_node_on_the_same_var_is_dropped(self):
        out = g.finalize_controls([
            ctl("MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT", kind="annun"),
            ctl("MD11_CPT_AUDIO_PNL_VHF1_MIC_LT", kind="annun", state_var="MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT"),
            ctl("MD11_OVHD_PNEU_ECON_OFF_LT", kind="annun"),
            ctl("MD11_OVHD_PNEU_ECON_OFF_LT001", kind="annun", state_var="MD11_OVHD_PNEU_ECON_OFF_LT"),
        ])
        self.assertEqual(["MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT", "MD11_OVHD_PNEU_ECON_OFF_LT"], [c["node_id"] for c in out])

    def test_option_flags_become_kind_option(self):
        out = g.finalize_controls([ctl("MD11_OPT_EFB", kind="annun")])
        self.assertEqual("option", out[0]["kind"])

    def test_curated_labels_replace_derived_garbage(self):
        out = g.finalize_controls([
            ctl("MD11_OVHD_L_RAIN_REPLNT_BT"), ctl("MD11_PED_XPNDR_7_BT"), ctl("MD11_LMCDU_LSK_1L_BT"),
            ctl("MD11_CTR_FLTNO2_SW", kind="switch"), ctl("MD11_OVHD_100_PAX_LOAD_SW", kind="switch", label="Pax Load Selector"),
        ])
        labels = {c["node_id"]: c["label"] for c in out}
        self.assertEqual("Left Rain Repellent", labels["MD11_OVHD_L_RAIN_REPLNT_BT"])
        self.assertEqual("Transponder 7", labels["MD11_PED_XPNDR_7_BT"])
        self.assertEqual("LSK 1L", labels["MD11_LMCDU_LSK_1L_BT"])
        self.assertEqual("Flight Number Digit 2", labels["MD11_CTR_FLTNO2_SW"])
        self.assertEqual("Passenger Load Hundreds", labels["MD11_OVHD_100_PAX_LOAD_SW"])

    def test_raw_3d_names_get_a_real_area(self):
        out = g.finalize_controls([ctl("Cylinder11904", label="Door 1L Slides"), ctl("knob_kohlsman", kind="knob", label="Standby Altimeter Setting")])
        areas = {c["node_id"]: c["area"] for c in out}
        self.assertEqual("Doors and Exterior", areas["Cylinder11904"])
        self.assertEqual("Main Instrument Panel", areas["knob_kohlsman"])

    def test_the_mirrored_window_shade_is_the_first_officers(self):
        # "Mirror_" names the modelling mirror the node was made with, not the left side of the
        # cockpit: FOAux_Light.xml declares it with ANIM_NAME MD11_RSIDE_WINDOW_SHADE (event
        # 95518, beside MD11_RSIDE_WINDOW's 95517). Calling it "Left Window Shade (mirror)" on
        # the Captain panel had a pilot operating the RIGHT shade, with no shade on the F/O side
        # at all.
        out = g.finalize_controls([ctl("l_window_shade_pull"), ctl("Mirror_l_window_shade_pull")])
        by = {c["node_id"]: c for c in out}
        self.assertEqual("Left Window Shade", by["l_window_shade_pull"]["label"])
        self.assertEqual("Captain Side Panel", by["l_window_shade_pull"]["area"])
        self.assertEqual("Right Window Shade", by["Mirror_l_window_shade_pull"]["label"])
        self.assertEqual("F/O Side Panel", by["Mirror_l_window_shade_pull"]["area"])

    def test_glareshield_warning_areas_are_named_correctly(self):
        self.assertEqual("Glareshield (Captain)", g.area_of("MD11_GSL_MST_WRN_BT"))
        self.assertEqual("Glareshield (First Officer)", g.area_of("MD11_GSR_MST_WRN_BT"))

    def test_curated_value_map_replaces_a_leaked_or_missing_one(self):
        # The parser cannot see these knobs' positions (their tooltips carry no %{case}), and on
        # two of them the freighter/pax WORDING used to leak into the map through the inline
        # if/else: {"1": "Courier Cabin", "0": "Forward Cabin"} on an 8-position knob. The parser
        # no longer lifts it (VariantWordingTests); a curated map still wins over any parsed one.
        out = g.finalize_controls([
            ctl("MD11_OVHD_PNEU_FWD_CAB_TEMP", kind="knob", label="Forward Cabin/Courier Cabin Temperature",
                value_map={"1": "Courier Cabin", "0": "Forward Cabin"}),
            ctl("MD11_OVHD_PNEU_COCKPIT_TEMP", kind="knob", label="Cockpit Temperature"),
            ctl("MD11_OVHD_PNEU_FWD_CARGO_TEMP", kind="knob", label="Forward Lower Cargo Temperature"),
            ctl("MD11_OVHD_PNEU_AFT_CARGO_TEMP", kind="knob", label="Aft Lower Cargo Temperature"),
        ])
        by = {c["node_id"]: c for c in out}
        self.assertEqual("1 (full cold)", by["MD11_OVHD_PNEU_FWD_CAB_TEMP"]["value_map"]["0"])
        self.assertEqual("8 (full hot)", by["MD11_OVHD_PNEU_FWD_CAB_TEMP"]["value_map"]["7"])
        self.assertEqual(8, len(by["MD11_OVHD_PNEU_COCKPIT_TEMP"]["value_map"]))
        self.assertEqual({"0": "1 (full cold)", "1": "2", "2": "3 (full hot)"},
                         by["MD11_OVHD_PNEU_FWD_CARGO_TEMP"]["value_map"])
        self.assertEqual(7, len(by["MD11_OVHD_PNEU_AFT_CARGO_TEMP"]["value_map"]))
        self.assertEqual("7 (full hot)", by["MD11_OVHD_PNEU_AFT_CARGO_TEMP"]["value_map"]["6"])
        # The zone is named by LABEL_FIXES too: TFDi's tooltip names it once per airframe variant.
        self.assertEqual("Forward Zone Temperature", by["MD11_OVHD_PNEU_FWD_CAB_TEMP"]["label"])

    def test_temperature_positions_are_generated_from_the_count(self):
        self.assertEqual({"0": "1 (full cold)", "1": "2", "2": "3 (full hot)"}, g.temperature_positions(3))
        self.assertEqual("4", g.temperature_positions(8)["3"])

    def test_the_fire_test_button_is_named_for_every_loop_it_tests(self):
        # TFDi's tooltip says "APU Fire Test"; their Systems Guide calls the button ENG/APU FIRE
        # TEST and says it lights ENG 1, 2, 3 and APU FIRE — and a live press (2026-09-06) lit all
        # four plus the master warning. A pilot told "APU" would think the engine loops are untestable.
        out = g.finalize_controls([ctl("MD11_AOVHD_FIRETEST_BT", label="APU Fire Test",
                                       events={"LEFT_BUTTON_DOWN": 73748, "LEFT_BUTTON_UP": 73749})])
        self.assertEqual("Engine and APU Fire Test", out[0]["label"])
        self.assertEqual("curated", out[0]["label_source"])


class KindCountsTests(unittest.TestCase):
    def test_reclassified_option_is_counted_once_not_under_annun_too(self):
        # MD11_OPT_* nodes arrive from collect() tagged "annun" (that's their template
        # kind) and finalize_controls repoints them to "option". kind_counts() must be
        # called on that FINALIZED list, so the row is tallied under "option" only --
        # never counted a second time under "annun", which is what happened when the
        # generator instead patched an "option" tally onto collect()'s pre-finalize
        # per-kind stats (the two counts landed on the same 7 rows).
        out = g.finalize_controls([
            ctl("MD11_OPT_EFB", kind="annun"),
            ctl("MD11_OVHD_PNEU_ECON_OFF_LT", kind="annun"),
        ])
        counts = g.kind_counts(out)
        self.assertEqual(1, counts["option"])
        self.assertEqual(1, counts["annun"])
        self.assertEqual(len(out), sum(counts.values()))


class StateTests(unittest.TestCase):
    def lamp(self, nid, area=None):
        return ctl(nid, kind="annun", area=area)

    def state_of(self, controls, nid):
        out = g.apply_state(g.finalize_controls(controls))
        return {c["node_id"]: c for c in out}[nid]

    def test_stem_rule_attaches_single_token_legends_only(self):
        gen = ctl("MD11_OVHD_ELEC_GEN1_BT", label="Generator 1", events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2})
        drive = ctl("MD11_OVHD_ELEC_GEN1_DRIVE_BT", label="Generator 1 IDG Disconnect", events={"LEFT_BUTTON_DOWN": 3, "LEFT_BUTTON_UP": 4})
        lamps = [self.lamp(n) for n in ("MD11_OVHD_ELEC_GEN1_OFF_LT", "MD11_OVHD_ELEC_GEN1_ARM_LT",
                                        "MD11_OVHD_ELEC_GEN1_DRIVE_FAULT_LT", "MD11_OVHD_ELEC_GEN1_DRIVE_DISCONNECT_LT")]
        s = self.state_of([gen, drive] + lamps, "MD11_OVHD_ELEC_GEN1_BT")["state"]
        self.assertEqual([("MD11_OVHD_ELEC_GEN1_OFF_LT", "OFF", "Off"), ("MD11_OVHD_ELEC_GEN1_ARM_LT", "ARM", "Armed")],
                         [(l["var"], l["legend"], l["lit"]) for l in s["lamps"]])
        d = self.state_of([gen, drive] + lamps, "MD11_OVHD_ELEC_GEN1_DRIVE_BT")["state"]
        self.assertEqual({"FAULT", "DISCONNECT"}, {l["legend"] for l in d["lamps"]})

    def test_multi_token_legend_and_dark_rule(self):
        econ = ctl("MD11_OVHD_PNEU_ECON_BT", label="ECON Mode", value_map={"1": "Off", "0": "On"},
                   state_var="MD11_OVHD_PNEU_SYSTEM_SEL_BT", events={"LEFT_BUTTON_DOWN": 1})
        s = self.state_of([econ, self.lamp("MD11_OVHD_PNEU_ECON_OFF_LT"), self.lamp("MD11_OVHD_PNEU_ECON_CAB_ALT_LT")],
                          "MD11_OVHD_PNEU_ECON_BT")["state"]
        self.assertEqual({"OFF": "Off", "CAB_ALT": "Cabin altitude"}, {l["legend"]: l["lit"] for l in s["lamps"]})
        self.assertEqual("On", s["dark"])          # an OFF legend dark means the system is on
        self.assertNotIn("latch", s)               # its tooltip reads a FOREIGN var: not a latch

    def test_bare_lamp_defaults_to_on_and_honours_the_override(self):
        nav = ctl("MD11_OVHD_LTS_NAV_BT", label="Navigation Lights", events={"LEFT_BUTTON_DOWN": 1})
        stby = ctl("MD11_OVHD_LTS_STBY_COMP_BT", label="Standby Compass Light", events={"LEFT_BUTTON_DOWN": 2})
        out = g.apply_state(g.finalize_controls([nav, stby, self.lamp("MD11_OVHD_LTS_NAV_LT"), self.lamp("MD11_OVHD_LTS_STBY_COMP_LT")]))
        by = {c["node_id"]: c for c in out}
        self.assertEqual([("MD11_OVHD_LTS_NAV_LT", "OFF", "Off")], [(l["var"], l["legend"], l["lit"]) for l in by["MD11_OVHD_LTS_NAV_BT"]["state"]["lamps"]])
        self.assertEqual("On", by["MD11_OVHD_LTS_NAV_BT"]["state"]["dark"])
        self.assertEqual([("MD11_OVHD_LTS_STBY_COMP_LT", "ON", "On")], [(l["var"], l["legend"], l["lit"]) for l in by["MD11_OVHD_LTS_STBY_COMP_BT"]["state"]["lamps"]])
        self.assertEqual("Off", by["MD11_OVHD_LTS_STBY_COMP_BT"]["state"]["dark"])
        self.assertEqual("Navigation Lights OFF light", by["MD11_OVHD_LTS_NAV_LT"]["label"])
        self.assertEqual("paired", by["MD11_OVHD_LTS_NAV_LT"]["label_source"])

    def test_curated_pairing_and_dark_override(self):
        tie = ctl("MD11_OVHD_ELEC_AC_TIE1_BT", label="AC Bus Tie 1", events={"LEFT_BUTTON_DOWN": 1})
        ext = ctl("MD11_OVHD_ELEC_EXT_PWR_BT", label="External Power", events={"LEFT_BUTTON_DOWN": 2})
        lamps = [self.lamp(n) for n in ("MD11_OVHD_ELEC_AC1_TIE_ARM_LT", "MD11_OVHD_ELEC_AC1_TIE_OFF_LT",
                                        "MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT", "MD11_OVHD_ELEC_EXT_PWR_ON_LT")]
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([tie, ext] + lamps))}
        self.assertEqual({"ARM", "OFF"}, {l["legend"] for l in out["MD11_OVHD_ELEC_AC_TIE1_BT"]["state"]["lamps"]})
        self.assertEqual("Closed", out["MD11_OVHD_ELEC_AC_TIE1_BT"]["state"]["dark"])
        self.assertEqual("Not available", out["MD11_OVHD_ELEC_EXT_PWR_BT"]["state"]["dark"])
        self.assertEqual("External Power AVAIL light", out["MD11_OVHD_ELEC_EXT_PWR_AVAIL_LT"]["label"])

    def test_latch_from_own_tooltip_keeps_tfdi_polarity(self):
        aice = ctl("MD11_OVHD_AICE_ENG1_BT", label="Engine 1 Anti Ice", value_map={"1": "On", "0": "Off"}, events={"LEFT_BUTTON_DOWN": 1})
        defog = ctl("MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", label="Windshield Defog", value_map={"1": "Off", "0": "On"}, events={"LEFT_BUTTON_DOWN": 2})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([aice, defog]))}
        self.assertEqual({"var": "MD11_OVHD_AICE_ENG1_BT", "on": "On", "off": "Off"}, out["MD11_OVHD_AICE_ENG1_BT"]["state"]["latch"])
        self.assertEqual({"var": "MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", "on": "Off", "off": "On"}, out["MD11_OVHD_WNDSHLD_AICE_DEFOG_BT"]["state"]["latch"])

    def test_battery_and_guards_latch(self):
        batt = ctl("MD11_OVHD_ELEC_BATT_BT", label="Battery", guard_id="MD11_OVHD_ELEC_BATT_GRD", events={"LEFT_BUTTON_DOWN": 1})
        grd = ctl("MD11_OVHD_ELEC_BATT_GRD", kind="guard", label="Battery", events={"LEFT_BUTTON_DOWN": 2})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([batt, grd, self.lamp("MD11_OVHD_ELEC_BATT_OFF_LT")]))}
        self.assertEqual({"var": "MD11_OVHD_ELEC_BATT_BT", "on": "On", "off": "Off"}, out["MD11_OVHD_ELEC_BATT_BT"]["state"]["latch"])
        self.assertEqual({"var": "MD11_OVHD_ELEC_BATT_GRD", "on": "Open", "off": "Closed"}, out["MD11_OVHD_ELEC_BATT_GRD"]["state"]["latch"])

    def test_a_guard_whose_tooltip_reads_the_covered_button_still_reads_its_own_cover(self):
        # TFDi's Fuel Dump cover tooltip reads the BUTTON's var for its Open/Closed wording, so
        # parse_tooltip hands back MD11_OVHD_FUEL_DUMP_BT (that is the parser doing its job). A
        # guard's position is its own node id — the var TFDi animates the cover on — never the
        # control under it: keyed on the button, the auto-open read "valve closed" as "cover
        # closed" and lowered an open cover onto the press. Same shape on the Fuel Dump
        # Emergency Stop, Center Gear Uplock and Main Cargo Door Arm covers.
        label, state_var, value_map = g.parse_tooltip(
            "Fuel Dump (%((L:MD11_OVHD_FUEL_DUMP_BT))%{if}Open%{else}Closed%{end})")
        self.assertEqual("MD11_OVHD_FUEL_DUMP_BT", state_var)
        grd = ctl("MD11_OVHD_FUEL_DUMP_GRD", kind="guard", label=label, state_var=state_var,
                  value_map=value_map, events={"LEFT_BUTTON_DOWN": 2})
        dump = ctl("MD11_OVHD_FUEL_DUMP_BT", label="Fuel Dump", guard_id="MD11_OVHD_FUEL_DUMP_GRD",
                   value_map={"1": "Open", "0": "Closed"}, events={"LEFT_BUTTON_DOWN": 1})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([dump, grd]))}
        self.assertEqual("MD11_OVHD_FUEL_DUMP_GRD", out["MD11_OVHD_FUEL_DUMP_GRD"]["state_var"])
        self.assertEqual({}, out["MD11_OVHD_FUEL_DUMP_GRD"]["value_map"])
        self.assertEqual({"var": "MD11_OVHD_FUEL_DUMP_GRD", "on": "Open", "off": "Closed"},
                         out["MD11_OVHD_FUEL_DUMP_GRD"]["state"]["latch"])
        # The button under it is untouched: its own var, its own latch words.
        self.assertEqual("MD11_OVHD_FUEL_DUMP_BT", out["MD11_OVHD_FUEL_DUMP_BT"]["state_var"])
        self.assertEqual({"var": "MD11_OVHD_FUEL_DUMP_BT", "on": "Open", "off": "Closed"},
                         out["MD11_OVHD_FUEL_DUMP_BT"]["state"]["latch"])

    def test_fault_only_button_is_normal_when_dark(self):
        self.assertEqual("Normal", g.dark_text(["FAULT", "DISAG"], "X"))
        self.assertEqual("On", g.dark_text(["OFF", "LOW"], "X"))
        self.assertEqual("Off", g.dark_text(["ON", "AVAIL"], "X"))
        self.assertIsNone(g.dark_text([], "X"))

    def test_standalone_lamp_gets_a_system_name_and_states(self):
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([self.lamp("MD11_OVHD_ELEC_AC1_OFF_LT"), self.lamp("MD11_OVHD_HYD_SYS_2_PRESS_LT")]))}
        ac = out["MD11_OVHD_ELEC_AC1_OFF_LT"]
        self.assertEqual("AC Bus 1", ac["label"])
        self.assertEqual([{"var": "MD11_OVHD_ELEC_AC1_OFF_LT", "legend": "OFF", "lit": "Off"}], ac["state"]["lamps"])
        self.assertEqual("Powered", ac["state"]["dark"])
        self.assertEqual("Hydraulic System 2 Pressure", out["MD11_OVHD_HYD_SYS_2_PRESS_LT"]["label"])

    def test_colliding_lamp_names_are_curated_apart(self):
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls(
            [self.lamp("MD11_GSL_MST_CAUT_LT"), self.lamp("MD11_GSR_MST_CAUT_LT")]))}
        self.assertEqual("Captain Master Caution CAUT light", out["MD11_GSL_MST_CAUT_LT"]["label"])
        self.assertEqual("First Officer Master Caution CAUT light", out["MD11_GSR_MST_CAUT_LT"]["label"])
        self.assertEqual("curated", out["MD11_GSL_MST_CAUT_LT"]["label_source"])
        # The override touches the label only: the legend and lit word still come from the tables.
        self.assertEqual("CAUT", out["MD11_GSL_MST_CAUT_LT"]["state"]["lamps"][0]["legend"])

    def test_two_lamps_with_one_spoken_name_refuse_to_generate(self):
        from unittest import mock
        # Force a collision through the override table itself: two different lamps curated onto
        # one name must stop the generator, not ship as two indistinguishable Ctrl+M rows.
        with mock.patch.dict(g.LAMP_NAME_OVERRIDES, {"MD11_A_LT": "Same light", "MD11_B_LT": "Same light"}):
            with self.assertRaises(ValueError):
                g.apply_state(g.finalize_controls([self.lamp("MD11_A_LT"), self.lamp("MD11_B_LT")]))

    def test_lamp_of_a_knob_becomes_a_named_row_not_a_fold(self):
        knob = ctl("MD11_OVHD_ELEC_EMER_PWR_KB", kind="knob", label="Emergency Power", value_map={"0": "Off", "1": "Armed", "2": "On"}, events={"LEFT_BUTTON_DOWN": 1})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([knob, self.lamp("MD11_OVHD_ELEC_EMER_PWR_ON_LT")]))}
        self.assertNotIn("state", out["MD11_OVHD_ELEC_EMER_PWR_KB"])
        row = out["MD11_OVHD_ELEC_EMER_PWR_ON_LT"]
        self.assertEqual("Emergency Power ON light", row["label"])
        self.assertEqual("On", row["state"]["lamps"][0]["lit"])
        self.assertEqual("Off", row["state"]["dark"])

    def test_side_panel_source_lamps_are_named(self):
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls([self.lamp("MD11_LSIDE_INP_APPRCAP2_LT"), self.lamp("MD11_RSIDE_INP_EIS_FOAUX_LT")]))}
        self.assertEqual("Captain ILS Source CAP 2 light", out["MD11_LSIDE_INP_APPRCAP2_LT"]["label"])
        self.assertEqual("First Officer EIS Source FO AUX light", out["MD11_RSIDE_INP_EIS_FOAUX_LT"]["label"])

    def test_fuel_dump_stop_lamp_pairs_to_the_stop_button_not_the_dump_button(self):
        # MD11_OVHD_FUEL_DUMP_STOP_LT's stem is a superset of MD11_OVHD_FUEL_DUMP_BT's, so it
        # matches BOTH buttons' stem rule (DUMP_BT via "<stem>_STOP_LT", STOP a LEGEND_MEANINGS
        # key; DUMP_STOP_BT via the bare "<stem>_LT"). Per TFDi's Systems Guide the lamp is the
        # STOP button's own indicator. Listing the DUMP button first reproduces the ordering
        # that used to let it win the lamp before the curated STATE_LAMPS entry was added.
        dump = ctl("MD11_OVHD_FUEL_DUMP_BT", label="Fuel Dump", value_map={"1": "Open", "0": "Closed"})
        stop = ctl("MD11_OVHD_FUEL_DUMP_STOP_BT", label="Fuel Dump Emergency Stop", value_map={"1": "Stop", "0": "Normal"})
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls(
            [dump, stop, self.lamp("MD11_OVHD_FUEL_DUMP_LT"), self.lamp("MD11_OVHD_FUEL_DUMP_STOP_LT")]))}
        self.assertEqual([("MD11_OVHD_FUEL_DUMP_STOP_LT", "STOP", "Stop")],
                         [(l["var"], l["legend"], l["lit"]) for l in out["MD11_OVHD_FUEL_DUMP_STOP_BT"]["state"]["lamps"]])
        self.assertEqual([("MD11_OVHD_FUEL_DUMP_LT", "OPEN", "Open")],
                         [(l["var"], l["legend"], l["lit"]) for l in out["MD11_OVHD_FUEL_DUMP_BT"]["state"]["lamps"]])
        self.assertEqual("Fuel Dump Emergency Stop STOP light", out["MD11_OVHD_FUEL_DUMP_STOP_LT"]["label"])

    def test_stem_rule_tie_break_prefers_the_longer_more_specific_stem(self):
        # With no STATE_LAMPS curation at all, a lamp matching two buttons' stems must go to
        # whichever stem is longer (more specific) -- MD11_OVHD_X_TEST_BT's bare "<stem>_LT"
        # match, not MD11_OVHD_X_BT's shorter "<stem>_TEST_LT" match (TEST is a LEGEND_MEANINGS
        # key). Listing the shorter-stem button first would have won the old, order-dependent
        # code.
        x = ctl("MD11_OVHD_X_BT", label="X")
        x_test = ctl("MD11_OVHD_X_TEST_BT", label="X Test")
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls(
            [x, x_test, self.lamp("MD11_OVHD_X_TEST_LT")]))}
        self.assertEqual([("MD11_OVHD_X_TEST_LT", "ON", "On")],
                         [(l["var"], l["legend"], l["lit"]) for l in out["MD11_OVHD_X_TEST_BT"]["state"]["lamps"]])
        self.assertNotIn("state", out["MD11_OVHD_X_BT"])


class VariantWordingTests(unittest.TestCase):
    """D1: an airframe variant flag (LABEL_ONLY_VARS) picks a tooltip's WORDING, never a position.
    A label that reads one has a wording per variant and the parser cannot choose between them:
    collapsed, the cabin knobs read "Forward Cabin/Courier Cabin Temperature" on every airframe."""

    FWD = "%((L:MD11_EFB_IS_CARGO))%{if}Courier Cabin%{else}Forward Cabin%{end} Temperature"

    def test_a_label_reading_a_variant_flag_refuses_to_generate_without_a_curated_name(self):
        with self.assertRaises(ValueError) as cm:
            g.parse_tooltip(self.FWD, node_id="MD11_OVHD_PNEU_NEW_CAB_TEMP")
        self.assertIn("MD11_OVHD_PNEU_NEW_CAB_TEMP", str(cm.exception))
        self.assertIn("LABEL_FIXES", str(cm.exception))

    def test_with_a_curated_name_the_variant_words_reach_neither_the_label_nor_the_map(self):
        label, state_var, value_map = g.parse_tooltip(self.FWD, node_id="MD11_OVHD_PNEU_FWD_CAB_TEMP")
        self.assertEqual("Temperature", label)
        self.assertIsNone(state_var)
        self.assertEqual({}, value_map)

    def test_a_variant_flag_in_the_state_expression_yields_no_positions(self):
        # Not in TFDi's package today. The rule is that a variant's words are never positions,
        # wherever the tooltip carries them; here the label itself reads no flag, so no error.
        label, state_var, value_map = g.parse_tooltip(
            "Cargo Heat (%((L:MD11_EFB_IS_CARGO))%{if}Installed%{else}Not installed%{end})")
        self.assertEqual("Cargo Heat", label)
        self.assertIsNone(state_var)
        self.assertEqual({}, value_map)

    def test_the_zone_and_standby_std_names_are_curated(self):
        out = g.finalize_controls([
            ctl("MD11_OVHD_PNEU_FWD_CAB_TEMP", kind="knob", label="Temperature"),
            ctl("MD11_OVHD_PNEU_MID_CAB_TEMP", kind="knob", label="Temperature"),
            ctl("MD11_MIP_ISFD_STD_BT"),
        ])
        named = {c["node_id"]: (c["label"], c["label_source"]) for c in out}
        self.assertEqual(("Forward Zone Temperature", "curated"), named["MD11_OVHD_PNEU_FWD_CAB_TEMP"])
        self.assertEqual(("Middle Zone Temperature", "curated"), named["MD11_OVHD_PNEU_MID_CAB_TEMP"])
        self.assertEqual(("Standby Altimeter STD", "curated"), named["MD11_MIP_ISFD_STD_BT"])


class InlineIfGuardTests(unittest.TestCase):
    """D1: an inline %{if}/%{else} lifts positions under the trailing path's rule -- the label
    names exactly one L:var. It used to lift whatever block it met first."""

    def test_the_fcp_mode_knobs_keep_their_mode_words_on_their_export(self):
        # Intended read-only mode rows (Md11ExportBacked): each label names ONE var, its mode
        # export, so the words stay; the button beside the row switches the mode. Passes before
        # and after the change -- it pins that the guard leaves these three alone.
        for tooltip, label, var, positions in (
            ("Autopilot %((L:MD11_AP_HDG_TRK))%{if}Track%{else}Heading%{end} Select",
             "Autopilot Heading/Track Select", "MD11_AP_HDG_TRK", {"1": "Track", "0": "Heading"}),
            ("Autopilot %((L:MD11_AP_IAS_MACH))%{if}MACH%{else}IAS%{end} Select",
             "Autopilot IAS/MACH Select", "MD11_AP_IAS_MACH", {"1": "MACH", "0": "IAS"}),
            ("Autopilot %((L:MD11_AP_VS_FPA))%{if}FPA%{else}VS%{end} Select",
             "Autopilot VS/FPA Select", "MD11_AP_VS_FPA", {"1": "FPA", "0": "VS"}),
        ):
            with self.subTest(var=var):
                self.assertEqual((label, var, positions), g.parse_tooltip(tooltip))

    def test_an_inline_block_beside_a_second_var_lifts_no_positions(self):
        # The minimums caps' shape, inline instead of trailing: the Baro/Radio words describe the
        # mode switch's var, not the value this control reads first.
        _, var, value_map = g.parse_tooltip(
            "Captain Minimums %((L:MD11_CAP_MINIMUMS))%!d! "
            "%((L:MD11_LECP_MINIMUMS_KB))%{if}Baro%{else}Radio%{end}")
        self.assertEqual("MD11_CAP_MINIMUMS", var)
        self.assertEqual({}, value_map)


class CaseLabelTests(unittest.TestCase):
    """D2: a %{case} position's label runs to the next position, not to the first '%'."""

    APU = ("APU Fire Handle (%((L:MD11_AOVHD_APUFIRE_KB))%{case}%{:0}Bottle 1"
           "%{:1}%((L:MD11_AOVHD_APUFIRE_SW))%{if}Shutoff%{else}Normal%{end}%{:2}Bottle 2%{end})")

    def test_a_nested_block_names_its_position_by_the_resting_word(self):
        # TFDi's APU fire handle tooltip, verbatim. Its centre used to vanish, leaving the combo
        # blank at rest with no way to select the centre.
        label, var, value_map = g.parse_tooltip(self.APU)
        self.assertEqual("APU Fire Handle", label)
        self.assertEqual("MD11_AOVHD_APUFIRE_KB", var)
        self.assertEqual({"0": "Bottle 1", "1": "Normal", "2": "Bottle 2"}, value_map)
        self.assertEqual(["0", "1", "2"], list(value_map))

    def test_a_literal_percent_sign_stays_in_a_position_name(self):
        _, _, value_map = g.parse_tooltip(
            "Thrust Cue (%((L:MD11_THR_X_SW))%{case}%{:0}Off%{:1}70%% N1%{end})")
        self.assertEqual({"0": "Off", "1": "70% N1"}, value_map)

    def test_an_empty_position_is_reported_and_kept_out_of_the_map(self):
        empty = []
        _, _, value_map = g.parse_tooltip(
            "Test Selector (%((L:MD11_OVHD_X_SEL_SW))%{case}%{:0}Off%{:1}%{:2}On%{end})",
            node_id="MD11_OVHD_X_SEL_SW", empty_cases=empty)
        self.assertEqual({"0": "Off", "2": "On"}, value_map)
        self.assertEqual(["1"], empty)

    def test_live_data_in_a_position_is_not_reported_as_empty(self):
        empty = []
        g.parse_tooltip("Readout (%((L:MD11_X_KB))%{case}%{:0}Off%{:1}%((L:MD11_X_KB) 10 *)%!d!%{end})",
                        empty_cases=empty)
        self.assertEqual([], empty)


class EmptyCaseReportTests(unittest.TestCase):
    def test_main_counts_and_prints_every_empty_position(self):
        tooltip = "Test Selector (%((L:MD11_OVHD_X_SEL_SW))%{case}%{:0}Off%{:1}%{:2}On%{end})"
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, {"FlightDeck/Overhead.xml": use_template(
                "TFDi_Design_MD11_Switch_Template", NODE_ID="MD11_OVHD_X_SEL_SW", TOOLTIPID=tooltip,
                LEFT_BUTTON_DOWN="1", RIGHT_BUTTON_DOWN="2")})
            data, err = run_main(pkg, wasm, os.path.join(tmp, "map.json"))
        self.assertEqual(1, data["counts"]["empty_case_labels"])
        self.assertIn("MD11_OVHD_X_SEL_SW %{:1}", err)
        self.assertEqual({"0": "Off", "2": "On"}, data["controls"][0]["value_map"])


class TrailingStateTests(unittest.TestCase):
    """D7: the trailing state block ends where the tooltip's own text resumes."""

    def test_a_parenthetical_after_the_state_block_stays_in_the_label(self):
        label, var, value_map = g.parse_tooltip(
            "Nosewheel Steering (%((L:MD11_PED_NWS_SW))%{if}On%{else}Off%{end}) (Tiller)")
        self.assertEqual("Nosewheel Steering (Tiller)", label)
        self.assertEqual("MD11_PED_NWS_SW", var)
        self.assertEqual({"1": "On", "0": "Off"}, value_map)

    def test_a_parenthetical_inside_the_state_block_stays_in_its_position(self):
        # Every real tooltip's shape: the block runs to the end, and a lazy match must not stop at
        # the ')' inside a position's name. Passes before and after the change.
        label, _, value_map = g.parse_tooltip(
            "Slat Handle (%((L:MD11_X_SW))%{case}%{:0}Up (Retracted)%{:1}Down%{end})")
        self.assertEqual("Slat Handle", label)
        self.assertEqual({"0": "Up (Retracted)", "1": "Down"}, value_map)


class NestedTemplateTests(unittest.TestCase):
    """D6: the parse is flat, so a nested <UseTemplate> must stop the generator, not merge."""

    def test_a_nested_use_template_names_the_file_and_both_nodes(self):
        xml = ('<UseTemplate Name="TFDi_Design_MD11_Button_Template">'
               '<TOOLTIPID>Outer Button</TOOLTIPID>'
               + use_template("TFDi_Design_MD11_Annunciator", NODE_ID="MD11_OVHD_INNER_LT")
               + '<NODE_ID>MD11_OVHD_OUTER_BT</NODE_ID></UseTemplate>')
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Panel.xml": xml})
            with self.assertRaises(ValueError) as cm:
                g.collect(pkg)
        message = str(cm.exception)
        self.assertIn("FlightDeck/Panel.xml", message)
        self.assertIn("MD11_OVHD_OUTER_BT", message)
        self.assertIn("MD11_OVHD_INNER_LT", message)

    def test_sibling_blocks_are_not_nesting(self):
        # TFDi's Lighting.xml writes 58 of its (skipped) blocks as Name = "..." (48
        # MD11_IntegralLighting_Template, 10 MD11_PA_Lights_Template). They sit BETWEEN the other
        # blocks, never inside one, and since D12 they are read too -- as the skipped templates
        # they are -- so the nesting check sees their bodies as well.
        xml = (use_template("TFDi_Design_MD11_Annunciator", NODE_ID="MD11_OVHD_A_LT")
               + '<UseTemplate Name = "MD11_IntegralLighting_Template">'
                 '<NODE_ID>MD11_OVHD_B_KB</NODE_ID></UseTemplate>'
               + use_template("TFDi_Design_MD11_Annunciator", NODE_ID="MD11_OVHD_C_LT"))
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Lighting.xml": xml})
            controls, stats = g.collect(pkg)
        self.assertEqual(["MD11_OVHD_A_LT", "MD11_OVHD_C_LT"], [c["node_id"] for c in controls])
        self.assertEqual(1, stats["skipped_template:MD11_IntegralLighting_Template"])


class GuardLabelTests(unittest.TestCase):
    """D3 and D8: a guard is named after the FINAL label of the control it covers."""

    def test_a_guard_uses_its_controls_final_label(self):
        # finalize_controls repairs the covered control's label itself -- a LABEL_FIXES name, or a
        # derived label losing its " button" -- so the guard must be named after the repaired one.
        fixed = ctl("MD11_OVHD_L_RAIN_REPLNT_BT", guard_id="MD11_OVHD_L_RAIN_REPLNT_GRD",
                    events={"LEFT_BUTTON_DOWN": 1, "LEFT_BUTTON_UP": 2})
        derived = ctl("MD11_OVHD_FUEL_X_BT", guard_id="MD11_OVHD_FUEL_X_GRD",
                      events={"LEFT_BUTTON_DOWN": 3, "LEFT_BUTTON_UP": 4})
        derived["label"] = "Fuel x button"       # humanize()'s form, as collect() stores it
        out = g.finalize_controls([
            ctl("MD11_OVHD_L_RAIN_REPLNT_GRD", kind="guard", events={"LEFT_BUTTON_DOWN": 5}),
            fixed,
            ctl("MD11_OVHD_FUEL_X_GRD", kind="guard", events={"LEFT_BUTTON_DOWN": 6}),
            derived,
        ])
        labels = {c["node_id"]: c["label"] for c in out}
        self.assertEqual("Left Rain Repellent guard", labels["MD11_OVHD_L_RAIN_REPLNT_GRD"])
        self.assertEqual("Fuel x guard", labels["MD11_OVHD_FUEL_X_GRD"])

    def test_a_guard_over_a_breaker_or_an_mcdu_key_is_still_named_as_a_guard(self):
        # The breaker branch read the guard's own id as a grid position ("GRD ..."), and the MCDU
        # branch as a keycap ("CLR_GRD"), because both were tested before the guard branch.
        out = g.finalize_controls([
            ctl("MD11_BKR_BWU_C24", label="Tank 1 Transfer Pump Power Breaker",
                guard_id="MD11_BKR_BWU_C24_GRD", events={"LEFT_BUTTON_DOWN": 1}),
            ctl("MD11_BKR_BWU_C24_GRD", kind="guard", label="Breaker Guard", events={"LEFT_BUTTON_DOWN": 2}),
            ctl("MD11_LMCDU_CLR_BT", guard_id="MD11_LMCDU_CLR_GRD",
                events={"LEFT_BUTTON_DOWN": 3, "LEFT_BUTTON_UP": 4}),
            ctl("MD11_LMCDU_CLR_GRD", kind="guard", events={"LEFT_BUTTON_DOWN": 5}),
        ])
        labels = {c["node_id"]: c["label"] for c in out}
        self.assertEqual("C24 Tank 1 Transfer Pump Power guard", labels["MD11_BKR_BWU_C24_GRD"])
        self.assertEqual("CLR guard", labels["MD11_LMCDU_CLR_GRD"])


class LampOwnerTests(unittest.TestCase):
    """D5: a lamp under two knob/switch stems belongs to the longer, more specific one."""

    def test_a_lamp_under_two_stems_belongs_to_the_longer_one(self):
        short = ctl("MD11_OVHD_X_KB", kind="knob", label="X")
        longer = ctl("MD11_OVHD_X_TEST_KB", kind="knob", label="X Test")
        owner, rest = g._owner_by_stem("MD11_OVHD_X_TEST_ON_LT", [short, longer])
        self.assertEqual("MD11_OVHD_X_TEST_KB", owner["node_id"])
        self.assertEqual("ON", rest)
        out = {c["node_id"]: c for c in g.apply_state(g.finalize_controls(
            [short, longer, ctl("MD11_OVHD_X_TEST_ON_LT", kind="annun")]))}
        self.assertEqual("X Test ON light", out["MD11_OVHD_X_TEST_ON_LT"]["label"])


class UseTemplateSpellingTests(unittest.TestCase):
    """D12: `Name = "..."` is the same attribute as `Name="..."`. TFDi's Lighting.xml spells 58
    blocks that way (48 MD11_IntegralLighting_Template, 10 MD11_PA_Lights_Template: skipped
    templates, so the map does not change), and a CONTROL spelt so used to vanish without a trace."""

    def test_a_control_written_with_spaces_around_the_equals_sign_is_collected(self):
        xml = "".join(
            f'<UseTemplate Name{equals}"TFDi_Design_MD11_Button_Template">'
            f"<TOOLTIPID>Test {n}</TOOLTIPID><NODE_ID>MD11_OVHD_X{n}_BT</NODE_ID>"
            f"<LEFT_BUTTON_DOWN>{n}</LEFT_BUTTON_DOWN></UseTemplate>"
            for n, equals in enumerate(("=", " = ", " =", "= "), start=1))
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Overhead.xml": xml})
            controls, _ = g.collect(pkg)
        self.assertEqual(["MD11_OVHD_X1_BT", "MD11_OVHD_X2_BT", "MD11_OVHD_X3_BT", "MD11_OVHD_X4_BT"],
                         [c["node_id"] for c in controls])


class XmlCommentTests(unittest.TestCase):
    """A commented-out <UseTemplate> defines nothing: read_xml strips comments before the parse.

    The installed package holds 444 blocks inside comments. 388 have a live twin, but in 236 of
    those the commented copy sorts EARLIER in the walk and was the copy `seen` kept -- so a TFDi
    update that edited one of those LIVE blocks would have been read from the stale comment and
    diff_maps would have printed nothing."""

    def test_a_commented_out_block_is_not_collected_and_the_live_one_is(self):
        xml = ("<!-- " + use_template("TFDi_Design_MD11_Button_Template", TOOLTIPID="Old",
                                      NODE_ID="MD11_OVHD_X_BT", LEFT_BUTTON_DOWN="1") + " -->"
               + use_template("TFDi_Design_MD11_Button_Template", TOOLTIPID="New",
                              NODE_ID="MD11_OVHD_X_BT", LEFT_BUTTON_DOWN="2"))
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Overhead.xml": xml})
            controls, _ = g.collect(pkg)
        self.assertEqual(["MD11_OVHD_X_BT"], [c["node_id"] for c in controls])
        self.assertEqual("New", controls[0]["label"])
        self.assertEqual({"LEFT_BUTTON_DOWN": 2}, controls[0]["events"])

    def test_a_use_template_inside_a_comment_inside_a_block_is_not_a_nesting(self):
        # D6 refuses a <UseTemplate> nested in another block's body; one inside a COMMENT there is
        # not a nesting, and stripping first settles that by construction rather than by a check.
        xml = ('<UseTemplate Name="TFDi_Design_MD11_Button_Template">'
               "<TOOLTIPID>Test</TOOLTIPID><NODE_ID>MD11_OVHD_X_BT</NODE_ID>"
               "<!-- " + use_template("TFDi_Design_MD11_Button_Template", NODE_ID="MD11_OVHD_Y_BT")
               + " --><LEFT_BUTTON_DOWN>1</LEFT_BUTTON_DOWN></UseTemplate>")
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Overhead.xml": xml})
            controls, _ = g.collect(pkg)
        self.assertEqual(["MD11_OVHD_X_BT"], [c["node_id"] for c in controls])
        self.assertEqual({"LEFT_BUTTON_DOWN": 1}, controls[0]["events"])


class CuratedLampTests(unittest.TestCase):
    """CURATED_LAMPS: the eight gear lights TFDi's package defines ONLY inside a comment."""

    def test_a_curated_lamp_is_added_only_when_the_wasm_carries_its_var(self):
        node = "MD11_MIP_NOSE_GREEN_LT"
        stats = g.Counter()
        added = g.add_curated_lamps([], {node}, stats)

        self.assertEqual([node], [c["node_id"] for c in added])
        self.assertEqual("annun", added[0]["kind"])
        self.assertEqual(node, added[0]["state_var"])
        self.assertEqual("Main Instrument Panel", added[0]["area"])
        self.assertIn("commented out", added[0]["source"])
        self.assertEqual(1, stats["curated_lamp"])

        # No var in the wasm's control table, no row: a fixture package gets none, and a future
        # TFDi build that drops the variable drops the row with it (diff_maps then reports a
        # REMOVED node, which is the STOP rule working).
        empty = g.Counter()
        self.assertEqual([], g.add_curated_lamps([], set(), empty))
        self.assertEqual(len(g.CURATED_LAMPS), empty["curated_lamp_absent_from_wasm"])

    def test_a_live_block_wins_over_the_curated_entry(self):
        node = "MD11_MIP_NOSE_GREEN_LT"
        live = ctl(node, kind="annun", source="FlightDeck/Overhead.xml")
        stats = g.Counter()

        self.assertEqual([live], g.add_curated_lamps([live], {node}, stats))
        self.assertEqual(1, stats["curated_lamp_is_live"])
        self.assertEqual(0, stats["curated_lamp"])

    def test_every_curated_lamp_has_a_curated_name(self):
        # apply_state names an annunciator from STANDALONE_LAMPS; one missing there would fall
        # through to the humanized node id ("Main Instrument Panel Nose GREEN light").
        for node in g.CURATED_LAMPS:
            self.assertIn(node, g.STANDALONE_LAMPS)


_REAL_WALK = os.walk


def _walk_in_order(reverse):
    """os.walk with every folder and file list sorted ascending or descending -- two orders a file
    system is free to hand back. Sorting `dirs` in place steers the rest of the real walk."""
    def walk(top, *args, **kwargs):
        for root, dirs, files in _REAL_WALK(top, *args, **kwargs):
            dirs.sort(reverse=reverse)
            files.sort(reverse=reverse)
            yield root, dirs, files
    return walk


class DeterministicOutputTests(unittest.TestCase):
    """D4: the map depends on the package, never on the order the file system lists it in."""

    def test_the_surviving_lamp_does_not_depend_on_the_order_nodes_were_collected_in(self):
        twin = ctl("MD11_OVHD_ELEC_X_OFF_LT001", kind="annun", state_var="MD11_OVHD_ELEC_X_OFF_LT")
        own = ctl("MD11_OVHD_ELEC_X_OFF_LT", kind="annun")
        for order in ([twin, own], [own, twin]):
            with self.subTest(first=order[0]["node_id"]):
                kept = g.finalize_controls([dict(c) for c in order])
                self.assertEqual(["MD11_OVHD_ELEC_X_OFF_LT"], [c["node_id"] for c in kept])

    def test_the_lamp_named_after_its_var_beats_a_shorter_twin(self):
        # The audio panels' MD11_CPT_* nodes are SHORTER than the MD11_PED_* var they light, so
        # _second_clickspots' rule alone (shortest wins) would rename 34 of the shipped map's 37
        # deduplicated lamps; the var-named node is the one the map has always kept.
        own = ctl("MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT", kind="annun")
        twin = ctl("MD11_CPT_AUDIO_PNL_VHF1_MIC_LT", kind="annun",
                   state_var="MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT")
        kept = g.finalize_controls([twin, own])
        self.assertEqual(["MD11_PED_CPT_AUDIO_PNL_VHF1_MIC_LT"], [c["node_id"] for c in kept])

    def test_the_map_is_byte_identical_whatever_order_the_file_system_lists_folders_in(self):
        # Two folders, each holding one copy of a duplicated lamp and of a duplicated button node.
        # The lamp's survivor is _second_lamps' to choose; the button's copy is the first one read,
        # which is Alpha's only when folders are read in name order.
        files = {
            "Alpha/Panel.xml": use_template("TFDi_Design_MD11_Annunciator",
                                            NODE_ID="MD11_OVHD_ELEC_X_OFF_LT001",
                                            VIS_VAR="MD11_OVHD_ELEC_X_OFF_LT")
                               + use_template("TFDi_Design_MD11_Button_Template", TOOLTIPID="Y Alpha",
                                              NODE_ID="MD11_OVHD_ELEC_Y_BT",
                                              LEFT_BUTTON_DOWN="1", LEFT_BUTTON_UP="2"),
            "Beta/Panel.xml": use_template("TFDi_Design_MD11_Annunciator", NODE_ID="MD11_OVHD_ELEC_X_OFF_LT")
                              + use_template("TFDi_Design_MD11_Button_Template", TOOLTIPID="Y Beta",
                                             NODE_ID="MD11_OVHD_ELEC_Y_BT",
                                             LEFT_BUTTON_DOWN="1", LEFT_BUTTON_UP="2"),
        }
        outputs = []
        with tempfile.TemporaryDirectory() as tmp:
            pkg, wasm = write_package(tmp, files)
            for reverse in (False, True):
                out = os.path.join(tmp, f"map-{int(reverse)}.json")
                with mock.patch.object(os, "walk", _walk_in_order(reverse)):
                    data, _ = run_main(pkg, wasm, out)
                with open(out, "rb") as fh:
                    outputs.append(fh.read())
        self.assertEqual(outputs[0], outputs[1])
        by = {c["node_id"]: c for c in data["controls"]}
        self.assertEqual("Y Alpha", by["MD11_OVHD_ELEC_Y_BT"]["label"])
        self.assertIn("MD11_OVHD_ELEC_X_OFF_LT", by)
        self.assertNotIn("MD11_OVHD_ELEC_X_OFF_LT001", by)


class CompositeTests(unittest.TestCase):
    """D10: an outer %{case} on ANOTHER var that hands one position over to a nested %{case} on the
    control's OWN var. TFDi's engine fire handles: the pull (0 Normal, 1 Generator Field Disconnect)
    and, fully pulled, the handle's own rotation (0 Bottle 1, 1 Fuel and Hydraulic Disconnect,
    2 Bottle 2). Scanned flat, the rotation's 0/1/2 overwrote the pull's words, so a stowed handle
    read "Bottle 1" and the row's walker turned the bottle-discharge wheel while it read the pull."""

    ENG1 = ("Engine 1 Fire Handle (%((L:MD11_AOVHD_ENG1FIRE_SW))%{case}%{:0}Normal"
            "%{:1}Generator Field Disconnect%{:2}%((L:MD11_AOVHD_ENG1FIRE_KB))%{case}%{:0}Bottle 1"
            "%{:1}Fuel and Hydraulic Disconnect%{:2}Bottle 2%{end}%{end})")

    BLOCK = {
        "outer_var": "MD11_AOVHD_ENG1FIRE_SW",
        "outer_words": {"0": "Normal", "1": "Generator Field Disconnect"},
        "delegate": "2",
        "inner_var": "MD11_AOVHD_ENG1FIRE_KB",
        "inner_words": {"0": "Bottle 1", "1": "Fuel and Hydraulic Disconnect", "2": "Bottle 2"},
    }

    def test_an_engine_fire_handle_is_a_composite_with_no_value_map(self):
        # TFDi's Engine 1 handle, verbatim (2 and 3 differ only in the digit).
        block = {}
        label, var, value_map = g.parse_tooltip(self.ENG1, node_id="MD11_AOVHD_ENG1FIRE_KB",
                                                composite=block)
        self.assertEqual("Engine 1 Fire Handle", label)
        self.assertEqual("MD11_AOVHD_ENG1FIRE_SW", var)     # the row's key keeps reading the pull
        self.assertEqual({}, value_map)
        self.assertEqual(self.BLOCK, block)
        self.assertEqual(["0", "1"], list(block["outer_words"]))

    def test_the_apu_fire_handle_is_not_a_composite(self):
        # Its OUTER case reads its OWN var (the rotation); the block nested at position 1 is an
        # if/else on the pull, which D2 names by its resting word. It stays a walkable combo.
        block = {}
        _, var, value_map = g.parse_tooltip(CaseLabelTests.APU, node_id="MD11_AOVHD_APUFIRE_KB",
                                            composite=block)
        self.assertEqual({}, block)
        self.assertEqual("MD11_AOVHD_APUFIRE_KB", var)
        self.assertEqual({"0": "Bottle 1", "1": "Normal", "2": "Bottle 2"}, value_map)

    def test_a_nested_case_on_a_third_var_stops_the_generator(self):
        # Only the control's OWN var can take a position over. Read flat, a nested case on any
        # other var puts that var's words on the pull -- the wrong-axis map D10 removes -- so the
        # generator refuses the expression, naming the node, rather than emit it.
        with self.assertRaises(ValueError) as cm:
            g.parse_tooltip(self.ENG1, node_id="MD11_AOVHD_OTHER_KB", composite={})
        self.assertIn("MD11_AOVHD_OTHER_KB", str(cm.exception))
        self.assertIn("MD11_AOVHD_ENG1FIRE_KB", str(cm.exception))

    def test_a_control_without_nesting_is_unchanged(self):
        block = {}
        label, var, value_map = g.parse_tooltip(
            "Emergency Power (%((L:MD11_OVHD_ELEC_EMER_PWR_KB))%{case}%{:0}Off%{:1}Armed%{:2}On%{end})",
            node_id="MD11_OVHD_ELEC_EMER_PWR_KB", composite=block)
        self.assertEqual({}, block)
        self.assertEqual(("Emergency Power", "MD11_OVHD_ELEC_EMER_PWR_KB"), (label, var))
        self.assertEqual({"0": "Off", "1": "Armed", "2": "On"}, value_map)

    def test_collect_writes_the_block_on_the_composite_only(self):
        xml = (use_template("TFDi_Design_MD11_ENG_Fire_Handle", GUARD_ID="MD11_AOVHD_ENG1FIRE_GRD",
                            WHEEL_UP="73734", WHEEL_DOWN="73735", PULL_DOWN="73732", PUSH_UP="73733",
                            TOOLTIPID=self.ENG1, NODE_ID="MD11_AOVHD_ENG1FIRE_KB")
               + use_template("TFDi_Design_MD11_Switch_Template", NODE_ID="MD11_OVHD_ELEC_EMER_PWR_KB",
                              TOOLTIPID="Emergency Power (%((L:MD11_OVHD_ELEC_EMER_PWR_KB))%{case}"
                                        "%{:0}Off%{:1}Armed%{:2}On%{end})",
                              LEFT_BUTTON_DOWN="1", RIGHT_BUTTON_DOWN="2"))
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/AftOverhead.xml": xml})
            controls, _ = g.collect(pkg)
        by = {c["node_id"]: c for c in controls}
        handle = by["MD11_AOVHD_ENG1FIRE_KB"]
        self.assertEqual("handle", handle["kind"])
        self.assertEqual("MD11_AOVHD_ENG1FIRE_SW", handle["state_var"])
        self.assertEqual({}, handle["value_map"])
        self.assertEqual(self.BLOCK, handle["composite"])
        self.assertNotIn("composite", by["MD11_OVHD_ELEC_EMER_PWR_KB"])


class IfElseWordTests(unittest.TestCase):
    """D11: '%%' is a literal percent sign in a trailing if/else word, as D2 made it in a case
    label. TFDi's oxygen flow regulators read their OWN var -- the proof that a button's own L:var
    holds its position -- as '%{if}100%%%{else}Normal%{end}'; cut at the '%', the words were lost,
    so the regulators had no positions, no latch and no spoken state."""

    CAPTAIN = ("Captain Oxygen Flow Regulator "
               "(%((L:MD11_LSIDE_OXY_FLOW_SW))%{if}100%%%{else}Normal%{end})")

    def test_the_oxygen_regulator_gets_both_words(self):
        label, var, value_map = g.parse_tooltip(self.CAPTAIN, node_id="MD11_LSIDE_OXY_FLOW_SW")
        self.assertEqual("Captain Oxygen Flow Regulator", label)
        self.assertEqual("MD11_LSIDE_OXY_FLOW_SW", var)
        self.assertEqual({"1": "100%", "0": "Normal"}, value_map)

    def test_so_the_regulator_speaks_the_latch_its_own_tooltip_reads(self):
        label, var, value_map = g.parse_tooltip(self.CAPTAIN, node_id="MD11_LSIDE_OXY_FLOW_SW")
        regulator = ctl("MD11_LSIDE_OXY_FLOW_SW", label=label, state_var=var, value_map=value_map,
                        events={"LEFT_BUTTON_DOWN": 94234})
        out = g.apply_state(g.finalize_controls([regulator]))
        self.assertEqual({"lamps": [], "latch": {"var": "MD11_LSIDE_OXY_FLOW_SW", "on": "100%",
                                                 "off": "Normal"}}, out[0]["state"])

    def test_a_percent_sign_in_either_word_is_kept(self):
        _, _, value_map = g.parse_tooltip(
            "Test Flow (%((L:MD11_OVHD_X_FLOW_SW))%{if}Max 100%%%{else}Min 20%%%{end})")
        self.assertEqual({"1": "Max 100%", "0": "Min 20%"}, value_map)


class IfCompositeTests(unittest.TestCase):
    """D13: an outer %{if} on ANOTHER var whose TRUE branch is, whole, a nested %{case} on the
    control's OWN var and whose ELSE branch is a plain word is a composite with two outer positions:
    0 the else word, 1 handing over to the nested case. TFDi's Elevator Feel knob, the only one in
    the package: its MANUAL latch reads "Auto" while it is off, and once it is on the knob's own five
    reference-speed positions take over. Scanned flat, the knob's words became the latch's
    positions, so an aircraft in Auto read "Decrease Reference Speed Fast" and the row's walker
    turned the knob's wheel while it read the latch."""

    ELEVFEEL = ("Elevator Feel (%((L:MD11_OVHD_FLTCTL_ELEVFEEL_BT))%{if}"
                "%((L:MD11_OVHD_FLTCTL_ELEVFEEL_KB))%{case}%{:0}Decrease Reference Speed Fast"
                "%{:1}Decrease Reference Speed Slow%{:2}Neutral%{:3}Increase Reference Speed Slow"
                "%{:4}Increase Reference Speed Fast%{end}%{else}Auto%{end})")

    KNOB_WORDS = {"0": "Decrease Reference Speed Fast", "1": "Decrease Reference Speed Slow",
                  "2": "Neutral", "3": "Increase Reference Speed Slow",
                  "4": "Increase Reference Speed Fast"}

    BLOCK = {
        "outer_var": "MD11_OVHD_FLTCTL_ELEVFEEL_BT",
        "outer_words": {"0": "Auto"},
        "delegate": "1",
        "inner_var": "MD11_OVHD_FLTCTL_ELEVFEEL_KB",
        "inner_words": KNOB_WORDS,
    }

    def _parse(self, tooltip, node_id="MD11_OVHD_FLTCTL_ELEVFEEL_KB"):
        block = {}
        label, var, value_map = g.parse_tooltip(tooltip, node_id=node_id, composite=block)
        return label, var, value_map, block

    def test_the_elevator_feel_knob_is_a_composite_with_no_value_map(self):
        # TFDi's tooltip, verbatim (FlightDeck/Overhead.xml).
        label, var, value_map, block = self._parse(self.ELEVFEEL)
        self.assertEqual("Elevator Feel", label)
        self.assertEqual("MD11_OVHD_FLTCTL_ELEVFEEL_BT", var)   # the row's key keeps reading the latch
        self.assertEqual({}, value_map)
        self.assertEqual(self.BLOCK, block)

    # Not a composite, each for one reason. Where the nested case's var is not the outer one, read
    # flat the knob's words would land on the latch, so the generator stops (_composite_block);
    # otherwise the tooltip is read exactly as the flat scan reads it today.

    def test_a_true_branch_on_a_third_var_stops_the_generator(self):
        # The nested case reads the knob, which is not this control's own var.
        with self.assertRaises(ValueError) as cm:
            self._parse(self.ELEVFEEL, node_id="MD11_OVHD_FLTCTL_OTHER_KB")
        self.assertIn("MD11_OVHD_FLTCTL_OTHER_KB", str(cm.exception))

    def test_an_else_that_is_not_a_plain_word_stops_the_generator(self):
        for where, else_branch in (("a directive", "%((L:MD11_OVHD_FLTCTL_ELEVFEEL_KB))%!d!"),
                                   ("nothing", "")):
            with self.subTest(else_holds=where), self.assertRaises(ValueError):
                self._parse(self.ELEVFEEL.replace("%{else}Auto%{end}", "%{else}" + else_branch + "%{end}"))

    def test_a_true_branch_that_is_not_the_whole_case_stops_the_generator(self):
        for where, tooltip in (("before", self.ELEVFEEL.replace("%{if}%((L:", "%{if}Manual %((L:")),
                               ("after", self.ELEVFEEL.replace("%{end}%{else}", "%{end} Manual%{else}"))):
            with self.subTest(word=where), self.assertRaises(ValueError):
                self._parse(tooltip)

    def test_an_expression_reading_an_airframe_variant_flag_is_not_a_composite(self):
        # The same shape keyed on the freighter/pax flag: a variant's words are never positions (D1),
        # so nothing is lifted and nothing is refused.
        _, var, value_map, block = self._parse(
            self.ELEVFEEL.replace("L:MD11_OVHD_FLTCTL_ELEVFEEL_BT", "L:MD11_EFB_IS_CARGO"))
        self.assertEqual({}, block)
        self.assertIsNone(var)
        self.assertEqual({}, value_map)

    def test_an_if_on_the_controls_own_var_is_not_a_composite(self):
        # One var throughout, so reading it flat mixes nothing, and nothing is refused.
        _, var, value_map, block = self._parse(
            self.ELEVFEEL.replace("L:MD11_OVHD_FLTCTL_ELEVFEEL_BT", "L:MD11_OVHD_FLTCTL_ELEVFEEL_KB"))
        self.assertEqual({}, block)
        self.assertEqual("MD11_OVHD_FLTCTL_ELEVFEEL_KB", var)
        self.assertEqual(self.KNOB_WORDS, value_map)

    def test_collect_writes_the_block_on_the_elevator_feel_knob_only(self):
        xml = (use_template("TFDi_Design_MD11_ELEV_FEEL_Knob", NODE_ID="MD11_OVHD_FLTCTL_ELEVFEEL_KB",
                            ANIM_NAME_PUSHPULL="MD11_OVHD_FLTCTL_ELEVFEEL_BT", PUSHPULL_ID="90480",
                            RESET_ID="90481", WHEEL_UP="90388", WHEEL_DOWN="90389",
                            TOOLTIPID=self.ELEVFEEL)
               + use_template("TFDi_Design_MD11_Switch_Template", NODE_ID="MD11_OVHD_FLTCTL_X_SW",
                              TOOLTIPID="Test Switch (%((L:MD11_OVHD_FLTCTL_X_SW))%{if}On%{else}Off%{end})",
                              LEFT_BUTTON_DOWN="1", RIGHT_BUTTON_DOWN="2"))
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/Overhead.xml": xml})
            controls, _ = g.collect(pkg)
        by = {c["node_id"]: c for c in controls}
        knob = by["MD11_OVHD_FLTCTL_ELEVFEEL_KB"]
        self.assertEqual("knob", knob["kind"])
        self.assertEqual("MD11_OVHD_FLTCTL_ELEVFEEL_BT", knob["state_var"])
        self.assertEqual({}, knob["value_map"])
        self.assertEqual(self.BLOCK, knob["composite"])
        # An ordinary if/else on the control's own var keeps its two words and gets no block.
        switch = by["MD11_OVHD_FLTCTL_X_SW"]
        self.assertNotIn("composite", switch)
        self.assertEqual({"1": "On", "0": "Off"}, switch["value_map"])


class CompositeHardeningTests(unittest.TestCase):
    """D10 hardening (the Task 4.5 review): both composite rules read an inline if/else by its
    resting word before they look for the nested case, a nested case they cannot split stops the
    generator instead of reaching the flat scan, and a composite never gains a value_map."""

    ENG1 = CompositeTests.ENG1

    # The review's fixture W: an inline word inside a rotation position. Cut at that word's own
    # %{end}, rotation position 3's word landed on the pull and the rotation lost it.
    W = ("Test Handle (%((L:MD11_T_SW))%{case}%{:0}Normal%{:1}Pulled%{:2}%((L:MD11_T_KB))%{case}"
         "%{:0}A%{:1}B%{:2}%((L:MD11_T_Q))%{if}Qon%{else}Qoff%{end}%{:3}D%{end}%{end})")

    def _parse(self, tooltip, node_id, empty_cases=None):
        block = {}
        label, var, value_map = g.parse_tooltip(tooltip, node_id=node_id, empty_cases=empty_cases,
                                                composite=block)
        return label, var, value_map, block

    def test_an_inline_word_in_a_rotation_position_is_read_by_its_resting_word(self):
        _, _, value_map, block = self._parse(self.W, "MD11_T_KB")
        self.assertEqual({}, value_map)
        self.assertEqual({"outer_var": "MD11_T_SW", "outer_words": {"0": "Normal", "1": "Pulled"},
                          "delegate": "2", "inner_var": "MD11_T_KB",
                          "inner_words": {"0": "A", "1": "B", "2": "Qoff", "3": "D"}}, block)

    def test_a_rotation_position_the_collapse_cannot_clean_stops_the_generator(self):
        # The Task 4.6b review's fixtures W2, W5, W3 and W7. One collapse pass reads an if/else by
        # its resting word only when both its words are plain; each of these keeps an %{end} of its
        # own inside the rotation, the nested case was cut at it, and the case rule emitted a
        # plausible wrong block without a word.
        inline = "%((L:MD11_T_Q))%{if}Qon%{else}Qoff%{end}"        # W's rotation position 2
        chain = "%((L:MD11_T_Q))%{if}Qon%{else}%((L:MD11_T_R))%{if}Ron%{else}Qoff%{end}%{end}"
        for name, tooltip in (
                # The rotation's D landed on the pull.
                ("W2, an else-if chain", self.W.replace(inline, chain)),
                # D on the pull, and the rotation lost its position 2.
                ("W5, an if word holding an interpolation",
                 self.W.replace(inline, "%((L:MD11_T_Q))%{if}Q %((L:MD11_T_Q))%!d!%{else}Qoff%{end}")),
                # D on the pull, and the third var's Qoff and Qon became the rotation's words.
                ("W3, a case on a third var",
                 self.W.replace(inline, "%((L:MD11_T_Q))%{case}%{:0}Qoff%{:1}Qon%{end}")),
                # The rotation's B renamed the pull's Pulled, and the rotation kept only Qoff.
                ("W7, an else-if chain in rotation position 0",
                 "Test Handle (%((L:MD11_T_SW))%{case}%{:0}Normal%{:1}Pulled%{:2}%((L:MD11_T_KB))%{case}"
                 "%{:0}" + chain + "%{:1}B%{end}%{end})")):
            with self.subTest(fixture=name):
                with self.assertRaises(ValueError) as cm:
                    self._parse(tooltip, "MD11_T_KB")
                self.assertIn("MD11_T_KB", str(cm.exception))
                self.assertIn("MD11_T_SW", str(cm.exception))

    def test_an_apu_style_centre_word_keeps_the_composite(self):
        # The review's fixture C: TFDi's Engine 1 handle with the rotation's centre worded the way
        # the APU handle words its own. Cut early, the delegate was lost and the handle fell back to
        # the flat Bottle map over the pull -- the walkable combo D10 removes -- without a word.
        apu_style = self.ENG1.replace(
            "%{:1}Fuel and Hydraulic Disconnect",
            "%{:1}%((L:MD11_AOVHD_ENG1FIRE_SW))%{if}Shutoff%{else}Fuel and Hydraulic Disconnect%{end}")
        _, _, value_map, block = self._parse(apu_style, "MD11_AOVHD_ENG1FIRE_KB")
        self.assertEqual({}, value_map)
        self.assertEqual(CompositeTests.BLOCK, block)

    def test_an_inline_word_in_a_knob_position_is_read_by_its_resting_word(self):
        # The same collapse in the if rule (D13).
        knob = IfCompositeTests.ELEVFEEL.replace(
            "%{:2}Neutral", "%{:2}%((L:MD11_T_Q))%{if}Centre%{else}Neutral%{end}")
        _, _, value_map, block = self._parse(knob, "MD11_OVHD_FLTCTL_ELEVFEEL_KB")
        self.assertEqual({}, value_map)
        self.assertEqual(IfCompositeTests.BLOCK, block)

    def test_the_case_rule_never_takes_a_variant_flag_as_the_outer_var(self):
        # Without that gate the flag would become outer_var; a variant's words are never positions.
        _, var, value_map, block = self._parse(
            self.ENG1.replace("L:MD11_AOVHD_ENG1FIRE_SW", "L:MD11_EFB_IS_CARGO"), "MD11_AOVHD_ENG1FIRE_KB")
        self.assertEqual({}, block)
        self.assertIsNone(var)
        self.assertEqual({}, value_map)

    def test_the_case_rule_never_takes_the_controls_own_var_as_the_outer_var(self):
        # Without that gate outer_var and inner_var would be one var. One var throughout, so it is
        # read flat, as before, and nothing is refused.
        _, var, value_map, block = self._parse(
            self.ENG1.replace("L:MD11_AOVHD_ENG1FIRE_SW", "L:MD11_AOVHD_ENG1FIRE_KB"), "MD11_AOVHD_ENG1FIRE_KB")
        self.assertEqual({}, block)
        self.assertEqual("MD11_AOVHD_ENG1FIRE_KB", var)
        self.assertEqual({"0": "Bottle 1", "1": "Fuel and Hydraulic Disconnect", "2": "Bottle 2"},
                         value_map)

    def test_without_a_node_id_nothing_is_a_composite(self):
        # collect() always passes one; a direct call without it gets the flat scan, and no refusal.
        block = {}
        _, var, value_map = g.parse_tooltip(self.ENG1, composite=block)
        self.assertEqual({}, block)
        self.assertEqual("MD11_AOVHD_ENG1FIRE_SW", var)
        self.assertEqual({"0": "Bottle 1", "1": "Fuel and Hydraulic Disconnect", "2": "Bottle 2"},
                         value_map)

    def test_an_empty_position_is_reported_once_and_never_by_a_refusal(self):
        empty_centre = self.ENG1.replace("%{:1}Fuel and Hydraulic Disconnect", "%{:1}")
        empty = []
        _, _, _, block = self._parse(empty_centre, "MD11_AOVHD_ENG1FIRE_KB", empty)
        self.assertEqual("2", block["delegate"])
        self.assertEqual(["1"], empty)
        # Text after the nested block leaves no whole delegate position: refused, after both of
        # the rule's scans ran.
        empty = []
        with self.assertRaises(ValueError):
            self._parse(empty_centre.replace("Bottle 2%{end}%{end}", "Bottle 2%{end} Extra%{end}"),
                        "MD11_AOVHD_ENG1FIRE_KB", empty)
        self.assertEqual([], empty)

    def test_a_composite_gets_no_value_map_from_its_label(self):
        # The review's fixture L: an inline word in the LABEL filled the composite's empty value_map.
        tooltip = ("Engine %((L:MD11_T_OPT))%{if}One%{else}Two%{end} Fire Handle "
                   "(%((L:MD11_T_SW))%{case}%{:0}Normal%{:1}Pulled%{:2}%((L:MD11_T_KB))%{case}"
                   "%{:0}A%{:1}B%{:2}C%{end}%{end})")
        _, _, value_map, block = self._parse(tooltip, "MD11_T_KB")
        self.assertEqual("MD11_T_SW", block["outer_var"])
        self.assertEqual({}, value_map)

    def test_a_composite_gets_no_value_map_from_an_inline_case_in_its_label(self):
        # The same through _inline_case: without its own guard, an inline %{case} in the LABEL
        # fills the composite's empty value_map with the label's words, One and Two.
        tooltip = ("Engine %((L:MD11_T_OPT))%{case}%{:0}One%{:1}Two%{end} Fire Handle "
                   "(%((L:MD11_T_SW))%{case}%{:0}Normal%{:1}Pulled%{:2}%((L:MD11_T_KB))%{case}"
                   "%{:0}A%{:1}B%{:2}C%{end}%{end})")
        _, _, value_map, block = self._parse(tooltip, "MD11_T_KB")
        self.assertEqual("MD11_T_SW", block["outer_var"])
        self.assertEqual({}, value_map)

    def test_collect_names_the_file_and_the_node_when_it_refuses(self):
        xml = use_template("TFDi_Design_MD11_ENG_Fire_Handle", NODE_ID="MD11_AOVHD_ENG9FIRE_KB",
                           WHEEL_UP="1", WHEEL_DOWN="2", TOOLTIPID=self.ENG1)
        with tempfile.TemporaryDirectory() as tmp:
            pkg, _ = write_package(tmp, {"FlightDeck/AftOverhead.xml": xml})
            with self.assertRaises(ValueError) as cm:
                g.collect(pkg)
        self.assertIn("FlightDeck/AftOverhead.xml", str(cm.exception))
        self.assertIn("MD11_AOVHD_ENG9FIRE_KB", str(cm.exception))

    def test_a_computed_condition_is_never_judged(self):
        # TFDi's Spoilers and Flaps/Slats levers, verbatim (FlightDeck/FootPedestalLower.xml): each
        # nests its %{case} under a COMPUTED %{if}, never a single-var one. Neither rule looks at
        # them, nothing is refused, and each keeps the map it always had.
        spoilers = ("Spoilers (%((L:MD11_SPDBRK_HANDLE) 1 == (L:MD11_SPDBRK_RNG) 0 == and)%{if}"
                    "Ground Spoilers Armed%{else}%((L:MD11_SPDBRK_HANDLE) 2 ==)%{if}Ground Spoilers "
                    "Extended%{else}%((L:MD11_SPDBRK_RNG))%{case}%{:0}Retracted%{:17.5}1/3 Extended"
                    "%{:25}2/3 Extended%{:32.5}3/3 Extended%{end}%{end}%{end})")
        flaps = ("Flaps/Slats (%(38 65 (L:MD11_FLAP_RNG) rng)%{if}Dial-A-Flap "
                 "%(10 (L:MD11_DIALAFLAP_IND_RNG) 6.6667 / +)%!d!/Extended%{else}"
                 "%((L:MD11_FLAP_RNG))%{case}%{:0}Up/Retracted%{:20}Up/Extended%{:70}28/Extended"
                 "%{:82}35/Extended%{:100}50/Extended%{end}%{end})")
        _, _, value_map, block = self._parse(spoilers, "MD11_SPDBRK_HANDLE")
        self.assertEqual({}, block)
        self.assertEqual({"0": "Retracted", "17.5": "1/3 Extended", "25": "2/3 Extended",
                          "32.5": "3/3 Extended"}, value_map)
        _, _, value_map, block = self._parse(flaps, "MD11_FLAP_LATCH")
        self.assertEqual({}, block)
        self.assertEqual({"0": "Up/Retracted", "20": "Up/Extended", "70": "28/Extended",
                          "82": "35/Extended", "100": "50/Extended"}, value_map)


if __name__ == "__main__":
    unittest.main()
