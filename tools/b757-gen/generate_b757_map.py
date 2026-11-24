#!/usr/bin/env python3
"""
Generate the BlueBird 757-200 control map from the aircraft's cockpit behavior XML.

extract_controls.py (next to this file) expands the vendor's templates and classifies every interactive control;
this script turns those rows into the JSON the app embeds (MSFSBlindAssist/Resources/bbs757_control_map.json):
one entry per control the pilot can operate, with the L:var that holds its state, its positions and the control's
own click code as RPN, to be replayed through the MobiFlight calculator path.

    controls[]  node, section, panel, label, kind, state_var, positions[{value,label}],
                press, release, hold_ms, cw, ccw, variant
                kind: toggle | switch | button | held | select | guard
                state_var: the L:var read back (null when the control keeps no L:var state: a button)
                press / release / cw / ccw: RPN, null when the control has none; a switch with no click code
                (the sim's own 3-state template), whose click code curation leaves out ("direct_write") or that
                curation builds from a "levers" or a "direct_positions" entry, has all four null and is written
                through state_var
                guard, guard_closed, close_sets: on a guarded control only (curation's "guarded"): the node of the
                guard that covers it, the value that guard reads while closed and the value closing it writes to the
                control (null when closing leaves the control alone). A guarded control has no click code at all.
                variant: "pw" or "rr" on a control only that engine type's cockpit model has (its label ends " (PW)" or
                " (RR)", see cockpit below), null on every other; the last field of every control, after the guard
                fields too. The app's panels show a "pw" or "rr" control only on an aircraft whose title names that
                engine type (Bbs757Variant): today the four fuel control switches, the RR electronic engine control
                switches and their guards.
    status[]    key, panel, label, positions[{value,label}], announce: read-only L:vars worth speaking
                (APU state, master warning ...), listed in curation.json

What is derived and what is curated (curation.json):
  derived   kind, state var, positions (the clamps in the turn code, or the vendor's ANIMTIP texts when there is
            one per position), click code, hold time, section (node prefix), a label from the node name (a guard
            is named by what its tip says it covers).
  curated   which vendor banners and nodes are left out, which single nodes under an excluded banner are taken in
            anyway (include_nodes: the banner stays excluded, its panel and section come from a node_panels rule),
            the app's panel names, labels the node name cannot give or the vendor got wrong, position names no
            tooltip carries, the kind of a control whose click code the extractor misreads (kinds: a toggle the
            extractor took for a button, a radio-style select that is only ever pressed), the guarded controls
            (guarded: a switch whose click only runs while a guard is open), the switches whose click code is left
            out (direct_write: a lever whose code cannot take a pick where it aims), the levers the rules leave out
            and the pilot needs (levers: the gear lever and the flap lever, drag handles BlueBird publishes the
            detents of; the sun visors, headrests and stowage doors; and the seats, whose click code writes only
            O:vars), the knobs the rules leave out and a combo can drive (direct_positions: Auto Rate, the zone
            temperature selectors, the display brightness knobs), the buttons that must be held (hold_ms: the fire,
            CVR and squib tests, which show only while their button is down), the nodes kept although no cockpit model
            has them (keep_without_node), the status rows, and the ORDER the pilot tabs through: the panels of a section
            (panel_order) and the controls of a panel (control_order). A curated label, position list, kind, guarded,
            levers, direct_positions, direct_write, hold_ms or keep_without_node entry, exclusion or inclusion for a node
            the package does not have is an error (a stale entry never hides); so is a kind or an inclusion that does
            nothing, and so is an order that names a panel or a node that is not there. A kind only re-reads a control
            the rules keep: for one they drop it changes nothing and is only reported.
            The vendor's tooltip titles and toggle tips are never labels: the vendor copied them between controls.
  cockpit   BlueBird ships six interior models: a PW and an RR cockpit, each with an AWE and a no-cab variant. They agree
            on every control node today, and a control can be clicked only where a node of its name is in the model. The
            run reads the node names of the two standard ones, BBS_752_PW_INTERIOR.gltf and BBS_752_RR_INTERIOR.gltf,
            from the package (it stops when either is missing, found twice or not a glTF with nodes), and the map holds
            only controls the cockpit has. A control whose node is in neither model is left out: the last of the drop
            rules, counted as "no cockpit node" and named in the run's report with the label it would have had and its
            vendor banner (the rules before it keep their own counts). A control that one model alone has says which,
            with " (PW)" or " (RR)" at the end of its label unless the label already ends so ("L Fuel Control (PW)"
            stays) and with its variant field ("pw" or "rr"; null on every other control), and the run names each. A
            label that says (PW) or (RR) is true of exactly that model's cockpit: on a control the other model has too,
            or only the other has, it stops the run. Curation's keep_without_node keeps a control BlueBird's own list
            proves real although neither model has a node for it; it is empty unless that list proves one, and the run
            names each. An entry for a node the package does not have, that a model has,
            that curation also excludes or that the rules leave out for another reason is an error. No curation entry
            brings back a control no cockpit has, whatever the other rules make of it (their reason comes before the
            cockpit's): a guarded, a levers, a direct_positions or an include_nodes entry for one stops the run, a kind,
            label or positions entry for one changes nothing and is only reported, and an order or a direct_write entry
            that names it, or an order that names a panel the drop emptied, is a stale one. Only keep_without_node keeps
            such a control.
            A panel with no control left is not in the map (panels exist only through their controls).
  order     the map's order IS the order the app's panels, and their controls, come in. Sections keep SECTIONS'
            order. A panel in panel_order comes first, in the listed order; the panels not listed follow
            alphabetically, and the run names them (a panel BlueBird adds later turns up there). A node in
            control_order comes first in its panel, in the listed order; the rest follow in cockpit order. Labels are
            made in cockpit order before either is applied, so an order never renames or renumbers a control.
  a toggle, guard or select that writes a value other than 0 or 1 to its state var stops the run until its
            positions are named in curation (A/T ARM writes 10).
  guarded   a switch whose vendor click only runs while its guard is open (the emergency lights switch, the stab trim
            cutouts). The extractor records that gate as an '[if <test>] ' note in front of the click code, which is
            not RPN, so none of the click is kept: press, release, cw and ccw are empty, and the control carries the
            state var and the positions curation gives it, the guard's node, the value the guard reads while closed and
            the value closing it writes to the control. The app opens the guard, writes the position and closes it. The
            guard keeps its own row. Curation may take in a control the rules leave out (the emergency lights switch's
            animation reads an O: var) or re-read one they keep (the stab trims), and the run names each; it never
            overrides curation's own exclusions. The entry is checked against the vendor's code: the click writes the
            state var, its gate reads the guard's var, and the guard's own click writes close_sets to the control. Any
            other control whose click code carries a gate note stops the run.
  direct_write  a switch whose vendor click code cannot take a pick where it aims and writes nothing but its own L:var
            (and O:vars; the chronometer ET lever: its clockwise code steps the value down and its anticlockwise code
            steps it up but jumps HOLD straight to RESET, so a step can land on no position at all; the flight deck door
            switch: its clockwise and anticlockwise codes write 2 and 0 whatever it reads, so a step to Auto from an end
            lands on the other end). Curation lists the node and the map leaves all of its click code out: press,
            release, cw and ccw are empty, and the app writes the state var to the picked position, as it does for any
            switch with no click code. The control is otherwise built by the rules (state var, kind, label, panel and
            positions unchanged), and its positions are still checked against what the vendor's code writes. The entry
            is checked against that code, so that a change of BlueBird's stops the run instead of reaching the app: the
            map must keep the node, a switch with a state var and positions to pick from, and not also under kinds or
            guarded; its click code must exist (else the entry does nothing), carry no gate note (a gated click belongs
            under guarded, which opens the guard first) and write nothing but the state var (another L:var, a K: or an
            H: event would no longer run). A write to an O:var is let through: O:vars belong to the component, and the
            sim's calculator cannot set one, so the app's replay never performed it and leaving it out loses nothing it
            could do (the door switch's click stamps (>O:UpdateTime) for the component's own 1 Hz spring-back, which
            still runs after a direct write). The run names each, and what it left out.
  levers    a lever the rules leave out and the pilot needs: the gear lever, whose OFF detent the sim's gear keys cannot
            select, and the flap lever. The extractor reads each drag handle as a "lever", which has no click code a
            pick could replay, so the rules drop it. BlueBird publishes the state var of each with its detents ("Write
            a detent position"), so curation gives the state var and the positions WITH their values (a lever's row
            names none that a list of names could be laid onto: the flap lever runs 0..150) and the control is built as
            a switch with no click code: press, release, cw and ccw are empty, and the app writes the state var to the
            picked position, as it does for any switch with none. An entry takes in a lever the rules leave out, and
            the run names each; it never overrides curation's own exclusions (a lever under an excluded banner needs
            an include_nodes entry as well, which keeps the banner's other controls out), and a node the rules leave out
            for another reason than being a lever is an error. A node the rules keep is an error too, unless its click
            code writes only O:vars (plan 12 task 11: the seat levers, whose click sets an O: flag that the component's
            own Update turns into a write of the seat's var): the sim's calculator cannot set an O:var, so the app's
            replay of that click would do nothing, and the entry writes the var the component moves instead; a kept node
            whose click writes an L:var, a K: or H: event, or nothing at all, is refused, so that its click code is
            replayed. The entry is checked against the vendor's
            code, so that a mistyped var or a change of BlueBird's stops the run instead of reaching the app: the
            state var must be one that the lever's whole component reads or writes (the extractor's _component_vars:
            the gear lever's is set by its component's Update and shown by the lever's other animation, which the
            node the pilot grabs never mentions), and every position must lie in the range the lever's own code
            clamps to, where the extractor found one (the flap lever's 0..150). A node also under guarded,
            direct_write, kinds or positions is an error: the entry holds all the lever has.
  direct_positions  a knob the rules leave out and a combo can drive: the pressurization Auto Rate knob (0.1 a click, 131
            positions), the three zone temperature selectors (an infinite knob that wraps) and the eight display
            brightness knobs (30 or 20 clicks). The rules drop a finite knob with more than 12 positions and every
            infinite knob, and a pick by click steps would take dozens of clicks. The entry is the form of "levers": the
            state var the systems read and the positions to pick WITH their values, [{value, label}], and the control is
            built as a switch with no click code: press, release, cw and ccw are empty, and the app writes the state var
            to the picked position, as it does for any switch with none. Nothing of the vendor's click code is kept, so
            the positions are NOT held to the clamp in that code: the cockpit mouse stops the brightness knobs at 30 (20
            on the F/O ADI knob) and Auto Rate at 13, but BlueBird's systems take 0-40 for the brightness (measured
            2026-10-28, [B757-16]; 40 is every knob's load value). An entry takes in only a row the rules leave
            out as a knob whose positions they cannot list ("rotary knob infinite", "rotary knob finite with more than 12
            positions" or "rotary knob finite without positions"), and the run names each; it never overrides curation's
            own exclusions (a knob under an excluded banner needs an include_nodes entry as well), and a node the rules
            keep (use positions or direct_write for it), leave out for another reason (a lever: use levers) or no
            cockpit has is an error. The entry is checked against the vendor's code, so that a mistyped var or a change
            of BlueBird's stops the run instead of reaching the app: the state var must be one that the knob's whole
            component reads or writes (the extractor's _component_vars: a zone selector's row shows the derived
            ..._ANIM_FRAME var while its click writes the ..._KNOB_FRAME var the systems read; the ADI knobs' click
            writes B757_PFD_1_SCREEN_KNOB, the var BlueBird's brightness code reads for the PFD). A node also under
            levers, guarded, direct_write, kinds or positions is an error: the entry holds all the knob has.
  hold_ms   a button the cockpit shows a result for only while it is held: the fire tests, the CVR and squib tests and the
            other test buttons (measured in flight 2026-10-28; docs/b757.md, "Held tests and spring
            switches": the engine fire test raised the bell, the fire lights and both sides' master warning for as long as
            the button was down and cleared when it was let go, so the app's 200 ms press gives a blip at most). BlueBird's
            template for them is the plain momentary button, which the extractor reads as a button, so curation names the
            node and its hold in milliseconds, and the control is built as a held button (kind "held") with that hold_ms.
            Nothing else changes: press, release, label, panel and state var are the rules' own, so the app presses the
            control, holds it that long and lets go (Bbs757Write.PressFor). A hold under MIN_CURATED_HOLD_MS (500) is the
            app's own press and one over MAX_CURATED_HOLD_MS (10,000) is a button left down: both are errors when the
            file loads. The entry is checked against the control the map holds, so that a change of BlueBird's stops the
            run: the map must keep the node, and it must be a button (kind "button") with both a press and a release
            (a toggle, a switch, a guard or select, and a button the extractor already reads as held, which has the hold
            the vendor's template gives it, are errors), and not also under kinds. The run names each, and the hold.

press / release / cw / ccw are the cockpit's own click code, VERBATIM: the app replays exactly what a click in the
cockpit runs, so a slip the cockpit has stays a slip in the replay (a guarded, direct_write or levers control has none).
Code that looks like a slip (a set without its 'L:', a closing brace nothing opened) is only reported, as
suspected_slips; nothing is changed.

Dropped, and counted in the printed report: circuit breakers, CDU keys, levers (but curation's levers takes some in),
infinite knobs, finite knobs with more than 12 positions (but curation's direct_positions takes some of both in),
anything in an excluded banner or node (but an include_nodes node under an excluded banner is kept), controls with no
click code or whose state is not an L:var, controls the cockpit does not have (no node in either interior model: "no
cockpit node", named in the report), and the second row of a node the interior files repeat (the PW and RR variants).

Usage:  python -I tools/b757-gen/generate_b757_map.py [<package folder>] [--out <json>] [--curation <json>]
        With no folder the package is looked for in Community and Community2024 under
        %LOCALAPPDATA%\\Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalCache\\Packages. Reads the package only (its
        interior behavior files and the node names of its two interior models, BBS_752_PW_INTERIOR.gltf and
        BBS_752_RR_INTERIOR.gltf).
Tests:  python -I tools/b757-gen/test_generate_b757_map.py   (stdlib unittest, no aircraft needed)
"""
import argparse
import collections
import json
import os
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
if str(HERE) not in sys.path:  # python -I puts neither the script's folder nor the current one on the path
    sys.path.insert(0, str(HERE))

import extract_controls  # noqa: E402

PACKAGE_NAME = "bluebirdsim-aircraft-757-200"
DEFAULT_OUT = HERE.parent.parent / "MSFSBlindAssist" / "Resources" / "bbs757_control_map.json"
DEFAULT_CURATION = HERE / "curation.json"

MAX_POSITIONS = 12
DEFAULT_HOLD_MS = 1500
# The holds curation's 'hold_ms' may give (milliseconds): under the first is the app's own 200 ms press, over the second is a
# button left down.
MIN_CURATED_HOLD_MS = 500
MAX_CURATED_HOLD_MS = 10000

# Section order in the map, and the node prefix that picks one (first match wins).
SECTIONS = ("Overhead", "Glareshield", "Forward panel", "Pedestal", "Other")
SECTION_PREFIXES = (
    ("B757_OVHD_", "Overhead"), ("BBS_OVHD_", "Overhead"),
    ("B757_MCP_", "Glareshield"),
    ("B757_MAIN_", "Forward panel"), ("B757_ADI_", "Forward panel"), ("B757_HSI_", "Forward panel"),
    ("B757_PED_", "Pedestal"),
)

# The extractor's kind -> the map's kind. A kind missing here is dropped.
KIND_MAP = {
    "toggle": "toggle",
    "multi-position switch": "switch",
    "rotary knob finite": "switch",  # only with 12 positions or fewer
    "momentary button": "button",
    "held button": "held",
    "select button": "select",
    "guard": "guard",
}
# What curation's 'kinds' may make of a control the rules keep: (the kind the extractor read, the kind curation asks for)
# -> the extractor kind the row is rebuilt as, so that everything downstream (state var, click code, positions) follows.
# Two re-readings only, each a known misreading: a click that sets the control's own state var by a gate rather than a
# flip reads as a momentary button (the APU fire handle) and is a toggle; a radio-style select that is only ever pressed
# (the audio mic selects) is a button. Their state var and click code come out the same either way, so only the kind,
# and the positions that follow from it, change.
KIND_OVERRIDES = {("momentary button", "toggle"): "toggle", ("select button", "button"): "momentary button"}
KIND_TARGETS = tuple(dict.fromkeys(wanted for _, wanted in KIND_OVERRIDES))
# Words the cockpit prints in capitals: they stay upper case in a label made from a node name.
ACRONYMS = frozenset((
    "APU IRS VHF HF ADF VOR DME EEC CVR EICAS CRT TCAS ILS GPS FMC CDU MCP EFIS ND PFD AT AP FD GND ENG ELEC HYD "
    "L R C BTL "
    # more initialisms the node names use: mode buttons, sides, engine makers
    "LNAV VNAV FLCH VS LOC APP CMD TOGA THR BCRS CLB CON CRZ SPD HDG DH WX WXR PTT RMI DDRMI ADI HSI EADI EHSI EFI "
    "CPT FO RR PW STBY PA ATC AM RAT FWD ET"
).split())
# Words that only name what kind of control a node is: dropped from the end of a label made from a node name.
ROLE_WORDS = frozenset(("BUTTON", "SWITCH", "KNOB", "SW", "CLICK", "TOGGLE", "BTN"))
# Vendor and area tokens dropped from the front of a node name.
VENDOR_TOKENS = frozenset(("B757", "BBS757", "BBS"))
AREA_TOKENS = frozenset(("OVHD", "PED", "MCP", "MAIN"))
# A tooltip that names a direction, not a position.
DIRECTION_WORDS = frozenset(("INC", "DEC", "INCREASE", "DECREASE", "CW", "CCW"))

CLAMP_STEP = re.compile(r"\d\s+[+-]\s+-?\d+(?:\.\d+)?\s+(?:min|max)\b")
STEP_UP = re.compile(r"(\d+(?:\.\d+)?)\s+\+\s+(-?\d+(?:\.\d+)?)\s+min\b")    # "<step> + <top> min"
STEP_DOWN = re.compile(r"(\d+(?:\.\d+)?)\s+-\s+(-?\d+(?:\.\d+)?)\s+max\b")  # "<step> - <bottom> max"
ANY_TOP = re.compile(r"(-?\d+(?:\.\d+)?)\s+min\b")
ANY_BOTTOM = re.compile(r"(-?\d+(?:\.\d+)?)\s+max\b")
CONSTANT_SET = re.compile(r"(?:^|\s)(-?\d+(?:\.\d+)?)\s*\(>L:")
ASSIGN = re.compile(r"(?:^|\s)(-?\d+(?:\.\d+)?)\s*\(>L:([^,()\s]+)")           # "10 (>L:X)": a constant written to X
FLIP = re.compile(r"\(L:([^,()\s]+)[^()]*\)\s*!\s*\(>L:\1\s*[,)]")              # "(L:X) ! (>L:X)": the var a click flips
MISSING_L = re.compile(r"\(>(?![A-Za-z]:)(?=[A-Za-z_])([^()]*)\)")  # "(>NAME, bool)": a set with no "L:" in front
RANGE_SPEC = re.compile(r"range\s+(-?\d+(?:\.\d+)?)\.\.(-?\d+(?:\.\d+)?)\s+step\s+(\d+(?:\.\d+)?)$")

# A 'guarded' entry holds all of these; close_sets is null when closing the guard leaves the control alone.
GUARDED_FIELDS = ("state_var", "positions", "guard", "guard_closed", "close_sets")
# A 'levers' or a 'direct_positions' entry holds both of these; its positions all carry their values.
LEVER_FIELDS = DIRECT_POSITIONS_FIELDS = ("state_var", "positions")
# The drop reason of a drag handle (the extractor's kind): the only row a 'levers' entry takes in.
LEVER = "lever"
# The drop reasons of a knob whose positions the rules cannot list, as drop_reason words them: the only rows a
# 'direct_positions' entry takes in (an infinite knob wraps, so it has none; a finite one has more than MAX_POSITIONS, or none
# the extractor could count).
UNLISTABLE_KNOBS = ("rotary knob infinite", "rotary knob finite with more than 12 positions",
                    "rotary knob finite without positions")
# The drop reasons that are curation's own word on a row: a guarded or levers entry never overrides them.
EXCLUSIONS = ("excluded banner", "excluded node")
# The extractor writes a click that only runs inside an enclosing if{} as '[if <test>] <code>'
# (extract_controls.split_callback). That note is not RPN, so no replayed click code may carry one: it comes first in the
# code, or after another code when several join in one (a node with two templates).
GATE_NOTE = re.compile(r"(?:^|\s)\[")

# The cockpit. BlueBird ships six interior models: a PW and an RR cockpit, each with an AWE and a no-cab variant (and two
# low-detail LOD01 models, which lack most control nodes). The six agree on every control node today, and a control can be
# clicked only where a node of its name is in the model, so the run reads the two standard ones. They are found by name
# under the package's SimObjects folder; of each only the node names are read.
INTERIOR_MODELS = {"pw": "BBS_752_PW_INTERIOR.gltf", "rr": "BBS_752_RR_INTERIOR.gltf"}
# What a label says of a control that exactly one model has (the variants as cockpit_presence names them).
VARIANT_TAGS = {"pw": "(PW)", "rr": "(RR)"}
# The drop reason of a control no cockpit has: the last of the rules, so it counts only what the others keep.
NO_COCKPIT_NODE = "no cockpit node"
# Who has a control, as cockpit_presence answers, for the error of a label that names the wrong variant.
WHO_HAS = {"both": "both do", "pw": "only the PW cockpit does", "rr": "only the RR cockpit does", "none": "neither does"}

Cockpit = collections.namedtuple("Cockpit", "pw rr")  # the node names of the PW and of the RR interior model

CURATION_KEYS = {"exclude_banners": list, "exclude_nodes": list, "include_nodes": list, "panels": dict,
                 "node_panels": list, "panel_order": dict, "control_order": dict, "labels": dict, "positions": dict,
                 "kinds": dict, "guarded": dict, "levers": dict, "direct_positions": dict, "direct_write": list,
                 "hold_ms": dict, "keep_without_node": list, "spelling": dict, "status": list}


class CurationError(Exception):
    """curation.json does not fit the package or its own schema."""


# ---- words -----------------------------------------------------------------------------------------------------
def title_word(word: str, spelling=None) -> str:
    key = word.upper()
    if spelling and key in spelling:
        return spelling[key]
    if key in ACRONYMS:
        return key
    if len(word) <= 3 and any(not ch.isalpha() for ch in word):  # F/O, A/P, N1, 1
        return key
    return word.capitalize()


def title_case(text: str, spelling=None) -> str:
    return " ".join(title_word(w, spelling) for w in text.split())


def humanize(node: str, spelling=None, keep_role=False) -> str:
    """A label from a node name: the vendor and area prefix and the 'BUTTON' style suffix go, acronyms stay upper,
    the rest is Title Case. The cockpit's own words, never expanded. keep_role keeps the suffix ("Mic L Light
    Knob"), for telling apart two controls whose names differ only there."""
    tokens = [t for t in re.split(r"[_\-]+", node) if t]
    while len(tokens) > 1 and tokens[0].upper() in VENDOR_TOKENS:
        tokens.pop(0)
    if len(tokens) > 1 and tokens[0].upper() in AREA_TOKENS:
        tokens.pop(0)
    while not keep_role and len(tokens) > 1 and tokens[-1].upper() in ROLE_WORDS:
        tokens.pop()
    return " ".join(title_word(t, spelling) for t in tokens)


def labels_from_tips(texts, spelling=None):
    """Position labels from the vendor's tooltips: the words every tooltip starts with (and ends with) are dropped
    ("BATTERY OFF" / "BATTERY ON" -> Off / On), never a whole label."""
    words = [t.split() for t in texts]
    if len(words) == 2 and all(words) and [w[-1].upper() for w in words] == ["OFF", "ON"]:
        return ["Off", "On"]  # "L ENG HYD PUMP OFF" / "L ENG HYD PUMPS ON": the vendor's wording drifts, the state does not
    shortest = min((len(w) for w in words), default=0)
    lead = 0
    while lead < shortest - 1 and len({w[lead].upper() for w in words}) == 1:
        lead += 1
    trail = 0
    while trail < shortest - lead - 1 and len({w[-1 - trail].upper() for w in words}) == 1:
        trail += 1
    return [title_case(" ".join(w[lead:len(w) - trail]), spelling) for w in words]


# ---- positions -------------------------------------------------------------------------------------------------
def number(x):
    return int(x) if float(x) == int(x) else float(f"{x:.10g}")


def tip_pairs(row):
    """[(index, text)] from a row's ANIMTIP texts; plain strings count from 0."""
    out = []
    for i, t in enumerate(row.get("_tips") or []):
        out.append((int(t[0]), str(t[1]).strip()) if isinstance(t, (tuple, list)) else (i, str(t).strip()))
    return out


def turn_code(row) -> str:
    clicks = row.get("_clicks") or {}
    return " ".join(code for slot in ("left", "cw", "ccw", "other") for _, code in clicks.get(slot, []))


def position_values(row):
    """The values a control can take, from the extractor's positions cell ('3', 'range 0..2 step 1'), or None
    when they cannot be listed (more than 12, or none known)."""
    spec = str(row.get("positions") or "").strip()
    m = RANGE_SPEC.match(spec)
    if m:
        lo, hi, step = float(m.group(1)), float(m.group(2)), float(m.group(3))
        span = (hi - lo) / step if step > 0 else -1
        if span < 1 or abs(span - round(span)) > 1e-6:
            return None
        n = int(round(span)) + 1
        return [number(lo + i * step) for i in range(n)] if n <= MAX_POSITIONS else None
    if not spec.isdigit():
        return None
    n = int(spec)
    if n < 2 or n > MAX_POSITIONS:
        return None
    code = turn_code(row)
    constants = sorted({number(c) for c in CONSTANT_SET.findall(code)})
    if len(constants) == n:  # a knob whose turns assign constants: those are its values
        return constants
    up, down = STEP_UP.findall(code), STEP_DOWN.findall(code)
    if up and down and float(up[0][0]) == float(down[0][0]) > 0:  # "<step> + <top> min" and "<step> - <bottom> max"
        step, top, bottom = float(up[0][0]), max(float(t) for _, t in up), min(float(b) for _, b in down)
        if abs((top - bottom) / step + 1 - n) < 1e-6:
            return [number(bottom + i * step) for i in range(n)]
    lows = [float(x) for x in ANY_BOTTOM.findall(code)]
    highs = [float(x) for x in ANY_TOP.findall(code)]
    lo = min(lows) if lows else 0.0
    if highs and abs((max(highs) - lo) + 1 - n) > 1e-6:  # the clamps do not describe n positions: count from 0
        lo = 0.0
    return [number(lo + i) for i in range(n)]


def usable_tips(tips, values) -> bool:
    """The tooltips name the positions when there is one per value, in order, none is a direction hint or a
    localization key, and the words they leave are all different."""
    if not tips or len(tips) != len(values) or [i for i, _ in tips] != list(range(len(values))):
        return False
    for _, text in tips:
        if not text or text.startswith(("TT:", "@")) or text.split()[-1].upper() in DIRECTION_WORDS \
                or text.upper() in DIRECTION_WORDS:
            return False
    return True


def positions_for(row, spelling=None):
    """[{value, label}] for a control row: buttons have none, toggles and guards two, switches as many as the
    clamps say. Labels come from the vendor's tooltips when those name the positions, else 'Position <n>'."""
    kind = row["kind"]
    if kind in ("momentary button", "held button"):
        return []
    if kind in ("toggle", "guard", "select button"):
        values, fallback = [0, 1], (["Closed", "Open"] if kind == "guard" else ["Off", "On"])
    else:
        values = position_values(row)
        if values is None:
            return []
        fallback = [f"Position {v}" for v in values]
    tips = tip_pairs(row)
    if usable_tips(tips, values):
        labels = labels_from_tips([t for _, t in tips], spelling)
        if len({l.lower() for l in labels}) == len(labels) and all(labels):
            return [{"value": v, "label": l} for v, l in zip(values, labels)]
    return [{"value": v, "label": l} for v, l in zip(values, fallback)]


# ---- the cockpit -----------------------------------------------------------------------------------------------
def cockpit_presence(node: str, pw: set, rr: set) -> str:
    """Which of the two cockpit models have a node of this name: "both", "pw", "rr" or "none". The name is matched whole and
    as written (the XML's names and the models' agree to the letter, every one of the controls the models have)."""
    in_pw, in_rr = node in pw, node in rr
    return "both" if in_pw and in_rr else "pw" if in_pw else "rr" if in_rr else "none"


def lacks_cockpit_node(node: str, cur: dict, cockpit) -> bool:
    """True when the cockpit is known, neither interior model has a node of this name and curation does not keep the control
    (keep_without_node). The one place that says so: the cockpit rule of drop_reason asks it, and so does curation's guarded
    entry, which takes in controls the other rules leave out. The rules answer first, so for such a control their reason is
    never the cockpit's: a mechanism that keeps what they drop must ask here, or it would keep a control the cockpit lacks."""
    return (cockpit is not None and node not in cur["keep_without_node"]
            and cockpit_presence(node, cockpit.pw, cockpit.rr) == "none")


def model_nodes(path: pathlib.Path) -> set:
    """The names of the nodes of a glTF model. The file is JSON and nodes[i].name is all the generator reads of it. A file
    that is not a glTF, or names no node, is an error: an empty set would leave every control out without a word."""
    try:
        data = json.loads(extract_controls.read_text(path))
    except (OSError, ValueError) as e:
        raise CurationError(f"{path.name} cannot be read as a glTF model ({e})") from None
    nodes = data.get("nodes") if isinstance(data, dict) else None
    names = {n["name"] for n in nodes if isinstance(n, dict) and isinstance(n.get("name"), str) and n["name"]} \
        if isinstance(nodes, list) else set()
    if not names:
        raise CurationError(f"{path.name} holds no named nodes (the 'nodes' of a glTF model): without them every control "
                            "would be left out")
    return names


def find_cockpit_model(pkg: pathlib.Path, variant: str) -> pathlib.Path:
    """The one file called INTERIOR_MODELS[variant] under the package's SimObjects folder."""
    name, root = INTERIOR_MODELS[variant], pkg / "SimObjects"
    found = sorted(p for p in root.rglob(name) if p.is_file()) if root.is_dir() else []
    if not found:
        raise CurationError(f"{name} (the {variant.upper()} cockpit model) is not in {root}: the map holds only controls "
                            "the cockpit has, which the generator cannot tell without both interior models")
    if len(found) > 1:
        raise CurationError(f"{name} (the {variant.upper()} cockpit model) is in more than one place under {root}: "
                            + ", ".join(extract_controls.rel(p, pkg) for p in found))
    return found[0]


def read_cockpit(pkg: pathlib.Path) -> Cockpit:
    """The node names of the two interior models, which say what the cockpit has. A model that is missing, found twice or
    not a glTF with nodes stops the run (CurationError): without both, no control could be judged."""
    return Cockpit(**{variant: model_nodes(find_cockpit_model(pkg, variant)) for variant in INTERIOR_MODELS})


def mark_variants(controls, cockpit) -> dict:
    """Says in the label of each control that exactly one model has which cockpit that is, " (PW)" or " (RR)" at the end,
    unless the label already ends so ("L Fuel Control (PW)"): the suffix is never doubled. Returns {node: "pw" | "rr"},
    in the order of `controls`; the labels are changed in place. A label that already names a variant is true of exactly
    that model's cockpit, so one on a control the other model has too, or only the other has, or neither has, is an
    error. Given no cockpit it judges nothing. Made before unique_labels, so that two controls of one name, one per
    model, are told apart by the suffix and not by a number."""
    if cockpit is None:
        return {}
    only = {}
    for c in controls:
        where = cockpit_presence(c["node"], cockpit.pw, cockpit.rr)
        for variant, tag in VARIANT_TAGS.items():
            if c["label"].endswith(tag) and where != variant:
                raise CurationError(f"{c['node']}: its label {c['label']!r} says only the {variant.upper()} cockpit has it, "
                                    f"but {WHO_HAS[where]}")
        if where in VARIANT_TAGS:
            only[c["node"]] = where
            if not c["label"].endswith(VARIANT_TAGS[where]):
                c["label"] += " " + VARIANT_TAGS[where]
    return only


# ---- one control -----------------------------------------------------------------------------------------------
def section_of(node: str) -> str:
    for prefix, section in SECTION_PREFIXES:
        if node.startswith(prefix):
            return section
    return "Other"


def node_rule(row, cur):
    """The first node_panels rule that fits this row (its banner and/or node prefix), or None."""
    node, banner = row["node"], row["panel"]
    for rule in cur["node_panels"]:
        if ("banner" not in rule or rule["banner"] == banner) and ("prefix" not in rule or node.startswith(rule["prefix"])):
            return rule
    return None


def panel_of(row, cur):
    """(app panel name, section): a node rule first, then the banner table, else the banner in Title Case."""
    node, banner = row["node"], row["panel"]
    rule = node_rule(row, cur)
    if rule is not None:
        return rule["panel"], rule.get("section") or section_of(node)
    entry = cur["panels"].get(banner)
    if entry is None:
        return title_case(banner, cur["spelling"]), section_of(node)
    if isinstance(entry, str):
        return entry, section_of(node)
    return entry["name"], entry.get("section") or section_of(node)


def state_var_of(row):
    for s in row.get("_state") or []:
        if s.startswith("L:"):
            return s[2:].split(" ")[0]
    return None


def join_codes(items):
    """A node can carry several interaction templates; a click reaches all of them, so their codes run together."""
    return " ".join(code for _, code in items) or None


def click_fields(row, out_kind):
    clicks = row["_clicks"]
    press, release = join_codes(clicks.get("left", [])), join_codes(clicks.get("release", []))
    cw, ccw = join_codes(clicks.get("cw", [])), join_codes(clicks.get("ccw", []))
    if out_kind == "switch" and not (cw or ccw) and press:
        right = join_codes([(l, c) for l, c in clicks.get("other", []) if l == "RightSingle"])
        if right and CLAMP_STEP.search(press):  # left click steps up, right click steps down, both clamped
            press, cw, ccw = None, press, right
    return press, release, cw, ccw


def suspected_slips(code):
    """What looks like a slip in a click code, as words; the code itself is never changed. Two things no RPN parser
    accepts: a set without its variable type, '(>B757_X_ON, bool)', and a closing brace nothing opened."""
    if not code:
        return []
    found = []
    if MISSING_L.search(code):
        found.append("set without a variable type")
    if code.count("}") > code.count("{"):
        found.append("closing brace nothing opened")
    return found


def hold_ms_of(row) -> int:
    try:
        seconds = float(str((row.get("_scope") or {}).get("MIN_HELD_DURATION", "")).strip())
    except ValueError:
        return DEFAULT_HOLD_MS
    return int(round(seconds * 1000)) if seconds > 0 else DEFAULT_HOLD_MS


def expand_positions(entry, values, node):
    """A curated position list: names in value order (one per value the control has), or explicit {value,label}s."""
    if all(isinstance(p, dict) for p in entry):
        return [{"value": p["value"], "label": p["label"]} for p in entry]
    if values is None or len(entry) != len(values):
        raise CurationError(f"positions for {node}: {len(entry)} names but the control has "
                            f"{'no listable positions' if values is None else str(len(values)) + ' positions'}")
    return [{"value": v, "label": l} for v, l in zip(values, entry)]


def tidy(text: str, spelling=None) -> str:
    """The vendor's wording, repaired: ALL CAPS becomes Title Case, mixed case is left as written."""
    text = " ".join(text.split())
    return title_case(text, spelling) if text == text.upper() else " ".join(
        (spelling or {}).get(w.upper(), w) for w in text.split())


def guard_label(row, out_kind, spelling=None):
    """A guard's single animation tip names what it covers ("Battery", "Left Generator Drive Disconnect"); its node
    name is only a number. Else None. No other vendor wording is used as a label: the tooltip titles and the
    toggles' tip pairs were copied between controls (every window heat button is titled 'Anti-ice Window', the center
    fuel pumps 'R Pump Aft'), so they are candidates for curation.json, never labels."""
    tips = tip_pairs(row)
    if out_kind == "guard" and len(tips) == 1 and tips[0][1] and not tips[0][1].startswith(("TT:", "@")):
        name = tidy(tips[0][1], spelling)
        return name if name.lower().endswith("guard") else f"{name} Guard"
    return None


def state_var_for(row, out_kind, press):
    """The L:var read back. A guard's own animation may read the switch under it, so a guard's state is the var its
    click flips."""
    if out_kind == "guard" and press:
        m = FLIP.search(press)
        if m:
            return m.group(1)
    return state_var_of(row)


def drop_reason(row, cur, cockpit=None):
    """Why the rules leave this row out, or None. It is decided on the row as the extractor read it: curation's 'kinds'
    comes after and never changes the answer, so a kind only ever re-reads a control the rules keep. The cockpit comes last:
    a control the other rules keep, and that neither interior model has a node for, is left out as NO_COCKPIT_NODE unless
    curation lists it under keep_without_node (lacks_cockpit_node). Given no cockpit (the models' node names, read_cockpit)
    nothing is judged on it: the tests of the other rules have rows and no models."""
    kind, node = row["kind"], row["node"]
    if row["panel"] in cur["exclude_banners"] and node not in cur["include_nodes"]:
        return "excluded banner"
    if node in cur["exclude_nodes"]:
        return "excluded node"
    if kind not in KIND_MAP:
        return kind
    out_kind = KIND_MAP[kind]
    if out_kind == "switch" and position_values(row) is None:
        spec = str(row.get("positions") or "").strip()
        return f"{kind} with more than 12 positions" if spec else f"{kind} without positions"
    press, _release, cw, ccw = click_fields(row, out_kind)
    state_var = state_var_for(row, out_kind, press)
    if not (press or cw or ccw or (out_kind == "switch" and state_var)):
        return "no click code"
    if out_kind not in ("button", "held") and state_var is None:
        return "state is not an L:var"
    if lacks_cockpit_node(node, cur, cockpit):
        return NO_COCKPIT_NODE
    return None


def reread(row, wanted):
    """The row as the extractor would have read it had it said `wanted` (curation 'kinds'). Only the re-readings in
    KIND_OVERRIDES are made; any other is a curation error, so that "only the kind changes" holds."""
    node, kind = row["node"], row["kind"]
    built_as = KIND_OVERRIDES.get((kind, wanted))
    if built_as is not None:
        return dict(row, kind=built_as)
    if KIND_MAP[kind] == wanted:
        raise CurationError(f"curation kinds name {wanted} for {node}, which the extractor already reads that way "
                            "(the entry does nothing, remove it)")
    allowed = " and ".join(f"a {read} can be made a {to}" for read, to in KIND_OVERRIDES)
    raise CurationError(f"curation kinds make {node} a {wanted}, but the extractor read it as a {kind}: only {allowed}")


def guarded_control(row, cur, cockpit=None):
    """The control curation's 'guarded' entry describes: a switch whose vendor click only runs while its guard is open.

    The extractor writes that gate as an '[if <test>] ' note in front of the click code, which is not RPN, and the emergency
    lights switch's animation reads an O: var, so the rules find no state var for it. Nothing of the click is kept: press,
    release, cw and ccw are empty, the state var and the positions come from the entry, and the guard's node, the value it
    reads while closed and the value closing it writes to the control ride along (Bbs757Control.Guard, GuardClosed,
    CloseSets), so that the app can open the guard, write the position and close it. The row gives the control's place in
    the cockpit and the vendor's count of positions, which the entry's names must fit. The rules' own drop reasons give way
    to the entry (a state that is not an L:var, a click the extractor cannot use); curation's exclusions never do, and nor
    does a cockpit without the node, WHATEVER the other rules say of the row: a switch no model has is not there to be
    guarded (a stale entry). Their reason comes first, so for a switch they leave out (the emergency lights switch) it is
    never the cockpit's, and this asks the cockpit itself (lacks_cockpit_node)."""
    node, entry = row["node"], cur["guarded"][row["node"]]
    why = drop_reason(row, cur, cockpit)
    if why in EXCLUSIONS:
        raise CurationError(f"curation guarded names {node}, which curation also leaves out ({why})")
    if lacks_cockpit_node(node, cur, cockpit):
        raise CurationError(f"curation guarded names {node}, which neither cockpit model has a node for: an entry never "
                            "keeps a control no cockpit has (remove it, or list the node under keep_without_node)")
    out_kind = KIND_MAP.get(row["kind"])
    if out_kind == "switch":  # the values the vendor's code gives it, as for any switch; two for a toggle, guard or select
        values = position_values(row)
    elif out_kind in ("toggle", "guard", "select"):
        values = [0, 1]
    else:
        values = None
    panel, section = panel_of(row, cur)
    close_sets = entry["close_sets"]
    return {
        "node": node, "section": section, "panel": panel,
        "label": cur["labels"].get(node) or humanize(node, cur["spelling"]),
        "kind": "switch", "state_var": entry["state_var"], "positions": expand_positions(entry["positions"], values, node),
        "press": None, "release": None, "hold_ms": 0, "cw": None, "ccw": None,
        "guard": entry["guard"], "guard_closed": number(entry["guard_closed"]),
        "close_sets": None if close_sets is None else number(close_sets),
    }


def click_writes(row):
    """Every variable the row's click code writes, as 'X:name' ('L:B757_X', 'O:Seat_Lever_Anim_Timer', 'K:EVENT', 'H:EVENT'),
    the code of every slot taken together (press, release, turns and the other mouse and drag codes)."""
    code = " ".join(c for slot in row["_clicks"].values() for _, c in slot)
    return {f"{kind}:{name}" for is_write, kind, name in extract_controls.var_refs(code) if is_write}


def lever_control(row, cur, cockpit=None):
    """The control curation's 'levers' entry describes: a lever the rules leave out, or a control whose click code writes only
    O:vars, built as a switch with no click code.

    The extractor reads a drag handle as a "lever", which the rules drop (a drag is no click a pick could replay), and BlueBird
    publishes its state var with its detents ("Write a detent position"), so the entry gives the state var and the positions
    with their values and the app writes the state var to the picked one, as for any switch with no click code
    (Bbs757Write.ToPosition). Nothing of the vendor's code is kept: press, release, cw and ccw are empty. The row gives the
    control's place in the cockpit (its banner and node, for its panel and section) and is what the entry is checked
    against (check_levers). The rules must leave the row out AS A LEVER and for no other reason, with one exception: a row
    the rules keep whose click code writes ONLY O:vars (no L:var, no K:, H: or other event; the seat levers, whose click sets
    O:CAPT_SEAT_MOVE_BACK or _FORWARD for the component's own Update to write the seat's var). An O:var belongs to the
    component and the sim's calculator cannot set one (spec 3.7 #6; see check_direct_write), so the app's replay of that
    click would do nothing, and the entry names the var the component moves instead (check_levers holds it to the
    component's reads and writes, as for any lever). A kept row whose click writes anything else, or nothing at all, is
    refused: a click the app can replay is replayed. Curation's exclusions never give way to an entry (a lever under an
    excluded banner needs include_nodes too, which keeps the banner's other controls out), nor does a cockpit without the
    node (a switch no model has is not there to be written: lacks_cockpit_node, asked here because the rules' reason for a
    lever never is the cockpit's), and a row the rules leave out for another reason is no lever for an entry to take in."""
    node, entry = row["node"], cur["levers"][row["node"]]
    why = drop_reason(row, cur, cockpit)
    if why in EXCLUSIONS:
        raise CurationError(f"curation levers names {node}, which curation also leaves out ({why})")
    if lacks_cockpit_node(node, cur, cockpit):
        raise CurationError(f"curation levers names {node}, which neither cockpit model has a node for: an entry never "
                            "keeps a control no cockpit has (remove it, or list the node under keep_without_node)")
    if why is None:
        writes = click_writes(row)
        others = sorted(w for w in writes if not w.startswith("O:"))
        if others or not writes:
            what = ", ".join(others) if others else "nothing"
            raise CurationError(f"curation levers names {node}, which the rules keep as a {KIND_MAP[row['kind']]} whose click "
                                f"code writes {what}: an entry takes in a lever the rules leave out, or a control whose click "
                                "code writes only O:vars, which the sim's calculator cannot set (remove it, so that the app "
                                "replays the click code)")
    elif why != LEVER:
        raise CurationError(f"curation levers names {node}, which the rules leave out for another reason than being a lever "
                            f"({why}): an entry takes in a lever the rules leave out (remove it)")
    return entry_switch(row, cur, entry)


def entry_switch(row, cur, entry):
    """The switch with no click code that a 'levers' or a 'direct_positions' entry describes: the entry gives the state var and
    the positions with their values, the row gives the control's place in the cockpit (its banner and node, for its panel and
    section), and press, release, cw and ccw are empty, so that the app writes the state var to the picked position."""
    node = row["node"]
    panel, section = panel_of(row, cur)
    return {
        "node": node, "section": section, "panel": panel,
        "label": cur["labels"].get(node) or humanize(node, cur["spelling"]),
        "kind": "switch", "state_var": entry["state_var"],
        "positions": [{"value": number(p["value"]), "label": p["label"]} for p in entry["positions"]],
        "press": None, "release": None, "hold_ms": 0, "cw": None, "ccw": None,
    }


def direct_positions_control(row, cur, cockpit=None):
    """The control curation's 'direct_positions' entry describes: a knob the rules leave out because they cannot list its
    positions, built as a switch with no click code.

    The pressurization Auto Rate knob has 131 positions of 0.1, a zone temperature selector is an infinite knob that wraps, and
    a display brightness knob has 21 to 31: the rules drop each ("rotary knob finite with more than 12 positions", "rotary knob
    infinite"), and the pilot needs none of the clicks, only the pick. The entry gives the state var the systems read and the
    positions with their values, and the app writes the state var to the picked position, as for any switch with no click code
    (Bbs757Write.ToPosition). Nothing of the vendor's code is kept: press, release, cw and ccw are empty, which is also why the
    positions are not held to the clamp in that code (the cockpit mouse stops a brightness knob at 30, or at 20; the systems take
    0-40). The row gives the control's place in the cockpit and is what the entry is checked against (check_direct_positions).
    The rules must leave the row out AS A KNOB WHOSE POSITIONS THEY CANNOT LIST (UNLISTABLE_KNOBS) and for no other reason:
    curation's exclusions never give way to an entry (a knob under an excluded banner needs include_nodes too, which keeps the
    banner's other controls out), nor does a cockpit without the node (lacks_cockpit_node, asked here because the rules' reason
    for such a knob never is the cockpit's), and a row the rules keep (positions and direct_write are the keys for those), or
    leave out for another reason (a lever has its own key), is no knob for an entry to take in."""
    node, entry = row["node"], cur["direct_positions"][row["node"]]
    why = drop_reason(row, cur, cockpit)
    if why in EXCLUSIONS:
        raise CurationError(f"curation direct_positions names {node}, which curation also leaves out ({why})")
    if lacks_cockpit_node(node, cur, cockpit):
        raise CurationError(f"curation direct_positions names {node}, which neither cockpit model has a node for: an entry "
                            "never keeps a control no cockpit has (remove it, or list the node under keep_without_node)")
    if why not in UNLISTABLE_KNOBS:
        what = (f"keep as a {KIND_MAP[row['kind']]} (use positions or direct_write for it)" if why is None
                else f"leave out for another reason than being a knob whose positions cannot be listed ({why})")
        raise CurationError(f"curation direct_positions names {node}, which the rules {what}: an entry takes in a knob the "
                            "rules leave out because they cannot list its positions (remove it)")
    return entry_switch(row, cur, entry)


def label_for(row, out_kind, cur):
    """The label a control has before any numbering: curation's, else a guard's own name, else one made from its node."""
    node, spelling = row["node"], cur["spelling"]
    return cur["labels"].get(node) or guard_label(row, out_kind, spelling) or humanize(node, spelling)


def convert_row(row, cur, cockpit=None):
    """(control, None, suspected slips) or (None, why it is left out, [])."""
    node = row["node"]
    if node in cur["guarded"]:
        return guarded_control(row, cur, cockpit), None, []
    if node in cur["levers"]:
        return lever_control(row, cur, cockpit), None, []
    if node in cur["direct_positions"]:
        return direct_positions_control(row, cur, cockpit), None, []
    why = drop_reason(row, cur, cockpit)
    if why:
        return None, why, []
    if node in cur["kinds"]:  # curation knows better how this kept control works: build it as the extractor would have said
        row = reread(row, cur["kinds"][node])
        why = drop_reason(row, cur, cockpit)
        if why:
            raise CurationError(f"curation kinds make {node} a {cur['kinds'][node]}, which cannot be built from it: {why}")
    out_kind = KIND_MAP[row["kind"]]
    values = position_values(row) if out_kind == "switch" else None
    press, release, cw, ccw = click_fields(row, out_kind)
    suspected = [f"{name}: {what}" for name, code in (("press", press), ("release", release), ("cw", cw), ("ccw", ccw))
                 for what in suspected_slips(code)]
    state_var = state_var_for(row, out_kind, press)
    spelling = cur["spelling"]
    if node in cur["positions"]:
        if values is None and out_kind in ("toggle", "guard", "select"):
            values = [0, 1]
        positions = expand_positions(cur["positions"][node], values, node)
    else:
        positions = positions_for(row, spelling)
    if positions:  # a control that writes a value its positions do not list: its positions are wrong, not the code
        listed = {number(p["value"]) for p in positions}
        written = {number(v) for code in (press, release, cw, ccw) for v, name in ASSIGN.findall(code or "")
                   if name == state_var}
        if written - listed:
            odd = ", ".join(str(v) for v in sorted(written - listed))
            raise CurationError(f"{node} writes {odd} to its state var {state_var}, outside its positions "
                                f"{sorted(listed)}: name its positions under 'positions' in curation, or exclude "
                                f"the node under 'exclude_nodes'")
    if node in cur["direct_write"]:  # the vendor's click is left out whole: the app writes the state var to the pick
        press = release = cw = ccw = None
        suspected = []  # code that is not replayed has no slip to suspect
    panel, section = panel_of(row, cur)
    control = {
        "node": node, "section": section, "panel": panel, "label": label_for(row, out_kind, cur),
        "kind": out_kind, "state_var": state_var, "positions": positions,
        "press": press, "release": release, "hold_ms": hold_ms_of(row) if out_kind == "held" else 0,
        "cw": cw, "ccw": ccw,
    }
    return control, None, suspected


def unique_labels(controls) -> None:
    """Inside one panel no two controls share a label: the second 'Light' becomes 'Light 2', the third 'Light 3'.
    The list is in map order and is changed in place."""
    reserved = collections.defaultdict(set)
    for c in controls:
        reserved[(c["section"], c["panel"])].add(c["label"])
    seen = collections.defaultdict(set)
    for c in controls:
        key = (c["section"], c["panel"])
        label = c["label"]
        if label in seen[key]:
            n = 2
            while f"{label} {n}" in reserved[key] or f"{label} {n}" in seen[key]:
                n += 1
            label = c["label"] = f"{label} {n}"
            reserved[key].add(label)
        seen[key].add(label)


def tell_apart(keyed, rows_by_node, cur) -> None:
    """Two controls in one panel whose labels (made from node names) differ only by the BUTTON / KNOB / SWITCH suffix
    keep that word on the later one ('Mic L Light' and 'Mic L Light Knob') before unique_labels would number it.
    A curated label or a guard's own name is never touched."""
    spelling = cur["spelling"]
    taken = collections.defaultdict(set)
    for _, c in keyed:
        taken[(c["section"], c["panel"])].add(c["label"])
    seen = collections.defaultdict(set)
    for _, c in keyed:
        key = (c["section"], c["panel"])
        row = rows_by_node[c["node"]]
        auto = c["node"] not in cur["labels"] and guard_label(row, c["kind"], spelling) is None
        if auto and c["label"] in seen[key]:
            longer = humanize(c["node"], spelling, keep_role=True)
            if longer not in taken[key]:
                c["label"] = longer
                taken[key].add(longer)
        seen[key].add(c["label"])


def click_vars(row):
    """(the L:vars the row's click code reads, the L:vars it writes), the code of every slot taken together."""
    code = " ".join(c for slot in row["_clicks"].values() for _, c in slot)
    refs = extract_controls.var_refs(code)
    return {n for w, k, n in refs if k == "L" and not w}, {n for w, k, n in refs if k == "L" and w}


def check_guarded(controls, first_row, cur):
    """Each guarded entry against the vendor's own click code, so a mistyped fact, or a change of BlueBird's, stops the run
    instead of reaching the app: the click writes the entry's state var, its gate reads the var the guard control holds, and
    the guard's own click writes close_sets to the control (nothing, when close_sets is null)."""
    by_node = {c["node"]: c for c in controls}
    for node, entry in cur["guarded"].items():
        guard, state_var = by_node[entry["guard"]], entry["state_var"]
        reads, writes = click_vars(first_row[node])
        if state_var not in writes:
            raise CurationError(f"curation guarded: {node}'s click code never writes {state_var} (it writes "
                                f"{', '.join(sorted(writes)) or 'no L:var'}): the state var is the L:var its click sets")
        if guard["state_var"] not in reads:
            raise CurationError(f"curation guarded: {node}'s click is not gated on {entry['guard']}'s var "
                                f"{guard['state_var']} (it reads {', '.join(sorted(reads)) or 'no L:var'}): is that the "
                                "guard that covers it?")
        closing = {number(v) for code in (guard["press"], guard["release"]) for v, name in ASSIGN.findall(code or "")
                   if name == state_var}
        wanted = set() if entry["close_sets"] is None else {number(entry["close_sets"])}
        if closing != wanted:
            raise CurationError(f"curation guarded: closing {entry['guard']} writes "
                                f"{', '.join(str(v) for v in sorted(closing)) or 'nothing'} to {state_var}, but close_sets "
                                f"is {entry['close_sets']}")


def check_component_var(table, what, node, row, state_var) -> None:
    """The state var of a 'levers' or a 'direct_positions' entry must be one the control's whole vendor component reads or
    writes (the extractor's _component_vars): the node the pilot grabs often never mentions it, and a mistyped var, or a
    change of BlueBird's, must stop the run instead of reaching the app."""
    own = row.get("_component_vars") or {}
    reads, writes = own.get("reads", []), own.get("writes", [])
    if state_var not in reads and state_var not in writes:
        raise CurationError(f"curation {table}: {node}'s own vendor code never reads or writes {state_var} (its component "
                            f"reads {', '.join(sorted(reads)) or 'no L:var'} and writes "
                            f"{', '.join(sorted(writes)) or 'no L:var'}): the state var is the L:var the {what}'s "
                            "component shows or sets")


def check_direct_positions(first_row, cur):
    """Each direct_positions entry against the vendor's own code: the state var must be one the knob's whole component reads
    or writes (check_component_var). A zone selector's row shows the derived ..._ANIM_FRAME var, but its click writes the
    ..._KNOB_FRAME var the systems read, which the extractor keeps with the component. The positions are NOT checked against
    the range the click code clamps to, as a lever's are: none of that code is replayed, and the systems accept more than the
    cockpit mouse reaches (the brightness knobs: 0-40, the mouse stops at 30 or 20; measured 2026-10-28, [B757-16])."""
    for node, entry in cur["direct_positions"].items():
        check_component_var("direct_positions", "knob", node, first_row[node], entry["state_var"])


def check_levers(first_row, cur):
    """Each levers entry against the vendor's own code, so a mistyped var, or a change of BlueBird's, stops the run instead of
    reaching the app: the state var must be one the lever's whole component reads or writes (the extractor's _component_vars:
    the gear lever's state var is set by its component's Update and shown by the lever's other animation, which the node the
    pilot grabs never mentions, and the flap lever's is read by its animation and written by its drag), and every position
    must lie in the range the lever's own code clamps to, where the extractor found one (its positions cell, for the flap
    lever 'range 0..150 step 1')."""
    for node, entry in cur["levers"].items():
        row, state_var = first_row[node], entry["state_var"]
        check_component_var("levers", "lever", node, row, state_var)
        found = RANGE_SPEC.match(str(row.get("positions") or "").strip())
        if found:
            low, high = float(found.group(1)), float(found.group(2))
            for p in entry["positions"]:
                if not low <= p["value"] <= high:
                    raise CurationError(f"curation levers: {node} lists the position {p['value']:g}, which is outside the "
                                        f"range {low:g}..{high:g} its own code clamps to")


def click_codes(row):
    """[(press | release | cw | ccw, the vendor's code)] of what the map would replay for this row, in that order."""
    return [(name, code) for name, code in zip(("press", "release", "cw", "ccw"), click_fields(row, KIND_MAP[row["kind"]]))
            if code]


def check_direct_write(controls, kept_row, dropped_why, cur):
    """Each direct_write entry against the control the map holds for it and against the vendor's own click code, which the
    nulling in convert_row never touches (kept_row holds the row as the extractor read it).

    The control: the map must keep the node (a stale entry never hides), and the app writes the state var only of a switch
    (the one control that may have no click code, Every_control_has_a_writer in the C# tests), so it must be one, with a
    state var to write and positions to pick a value from, and not also under kinds, which keeps the click code verbatim.
    The click code: leaving it out must lose nothing, so a change of BlueBird's stops the run instead of reaching the app.
    There is code to leave out (else the entry does nothing), none of it gated on a guard (that control belongs under
    'guarded', which opens the guard first), and it writes nothing but the state var: another L:var, or a K: or H: event it
    fires, would no longer run. A write to an O:var is let through (ruling R12, the flight deck door switch: its click
    stamps (>O:UpdateTime) beside the state var). An O:var belongs to the component, and the sim's calculator cannot set
    one (spec 3.7 #6: the fire handle relock runs on O:vars it cannot set), so the app's replay of that click never
    performed the write either, and leaving it out loses nothing the app could do. Reads are no loss at all."""
    by_node = {c["node"]: c for c in controls}
    for node in cur["direct_write"]:
        control = by_node.get(node)
        if control is None:
            raise CurationError(f"curation direct_write names {node}, which the map does not keep (the rules leave it out: "
                                f"{dropped_why.get(node, '?')}): remove the entry")
        if node in cur["kinds"]:
            raise CurationError(f"curation direct_write and kinds both name: {node} (kinds keeps a control's click code "
                                "verbatim, direct_write leaves it out)")
        if control["state_var"] is None:
            raise CurationError(f"curation direct_write names {node}, which has no state var: there is nothing to write")
        if not control["positions"]:
            raise CurationError(f"curation direct_write names {node}, which has no positions: there is no value to write")
        if control["kind"] != "switch":
            raise CurationError(f"curation direct_write names {node}, which is a {control['kind']}, not a switch: only a "
                                "switch may have no click code and be written through its state var")
        codes = click_codes(kept_row[node])
        if not codes:
            raise CurationError(f"curation direct_write names {node}, whose vendor row has no click code: the entry does "
                                "nothing, remove it")
        for field, code in codes:
            if GATE_NOTE.search(code):
                note = re.search(r"\[[^\]]*\]", code)
                raise CurationError(f"curation direct_write names {node}: {field} carries the extractor's gate note "
                                    f"'{note.group(0) if note else code[:40]}': its click only runs while a guard is open, so "
                                    "list it under 'guarded' (the app opens the guard first), not under 'direct_write'")
        state_var = control["state_var"]
        # An O: write is no loss (see the docstring): only another L:var, or a K: or H: event, would no longer run.
        writes = {f"{kind}:{name}" for _, code in codes for is_write, kind, name in extract_controls.var_refs(code)
                  if is_write and kind != "O" and (kind, name) != ("L", state_var)}
        if writes:
            raise CurationError(f"curation direct_write names {node}: its click code also writes {', '.join(sorted(writes))} "
                                f"besides its state var {state_var}, and leaving the click code out would lose that: remove "
                                "the entry, so that the app replays the click code")


def apply_hold_ms(controls, dropped_why, cur) -> None:
    """Curation's 'hold_ms': each named button becomes a held button with that hold, in place, and nothing else of it changes.

    The control is checked first, so that a change of BlueBird's stops the run instead of reaching the app: the map must keep
    the node (a stale entry never hides), it must not also be under kinds (which re-reads a control's kind and keeps the
    click code verbatim: a node under both would be built twice over), and it must be a button, with a press to hold and a
    release to let go with. A toggle, switch, guard or select is not pressed and let go, and a button the extractor already
    reads as held keeps the hold the vendor's template gave it. Run after the labels are made and the controls are kept,
    before the order: the hold changes neither."""
    by_node = {c["node"]: c for c in controls}
    for node in cur["hold_ms"]:
        control = by_node.get(node)
        if control is None:
            raise CurationError(f"curation hold_ms names {node}, which the map does not keep (the rules leave it out: "
                                f"{dropped_why.get(node, '?')}): remove the entry")
        if node in cur["kinds"]:
            raise CurationError(f"curation hold_ms and kinds both name: {node} (kinds re-reads a control's kind, hold_ms "
                                "builds a button as a held one)")
        if control["kind"] != "button":
            raise CurationError(f"curation hold_ms names {node}, which is a {control['kind']}, not a button: only a button "
                                "is pressed, held and let go (a held button the vendor's template times has its own hold)")
        for field in ("press", "release"):
            if not control[field]:
                raise CurationError(f"curation hold_ms names {node}, which has no {field} code: a held button is pressed "
                                    "and let go with the cockpit's own click code")
    for node, ms in cur["hold_ms"].items():
        by_node[node]["kind"], by_node[node]["hold_ms"] = "held", ms


def reject_gate_notes(controls, cur):
    """No control the map replays carries the extractor's gate note (GATE_NOTE) in its press, release, cw or ccw: its click
    only runs while a guard is open, and what the app replays would never run. Such a control belongs under 'guarded'."""
    for c in controls:
        if c["node"] in cur["guarded"]:
            continue
        for field in ("press", "release", "cw", "ccw"):
            code = c[field]
            if code and GATE_NOTE.search(code):
                note = re.search(r"\[[^\]]*\]", code)
                raise CurationError(f"{c['node']}: {field} carries the extractor's gate note "
                                    f"'{note.group(0) if note else code[:40]}': its click only runs while a guard is open, "
                                    "so list the node under 'guarded' in curation (the app opens the guard and writes the "
                                    "state var) or leave it out under 'exclude_nodes'")


def guarded_how(row, cur):
    """What curation's 'guarded' did to this row, in words, for the run's report."""
    why = drop_reason(row, cur)
    if why:
        return f"taken in (the rules alone leave it out: {why})"
    return f"re-read (the rules alone keep it as a {KIND_MAP[row['kind']]} and replay its click code)"


def lever_how(row, cur):
    """What curation's 'levers' did to this row, in words, for the run's report: it took in a lever the rules leave out, or a
    control they keep whose click code writes only O:vars (lever_control has already stopped the run for any other row)."""
    why = drop_reason(row, cur)
    if why is None:
        return (f"taken in (the rules alone keep it as a {KIND_MAP[row['kind']]}, but its click code writes only O:vars, which "
                "the sim's calculator cannot set)")
    return f"taken in (the rules alone leave it out: {why})"


def direct_positions_how(row, cur):
    """What curation's 'direct_positions' did to this row, in words, for the run's report: it took in a knob the rules leave
    out (direct_positions_control has already stopped the run for a row they do not leave out as one)."""
    return f"taken in (the rules alone leave it out: {drop_reason(row, cur)})"


def direct_write_how(row):
    """What curation's 'direct_write' left out of this row's click code, in words, for the run's report (check_direct_write
    has already stopped the run for a row with none)."""
    return f"{', '.join(name for name, _ in click_codes(row))} left out"


def hold_ms_how(ms) -> str:
    """What curation's 'hold_ms' did to this control, in words, for the run's report (apply_hold_ms has already stopped the
    run for a control that is not a button)."""
    return f"held {ms} ms (the rules alone keep it as a button)"


def check_keep_without_node(kept, dropped_why, cur, cockpit):
    """Each keep_without_node entry must do what it says: keep a control the rules would keep but for its node, which
    neither interior model has. So it may not also be excluded (conflicting), may not name a node a model has (it would do
    nothing) and may not name one the rules leave out for another reason (the entry would not bring it back). An entry for
    a node the package does not have is caught with the other tables. Given no cockpit there is nothing to judge an entry
    against."""
    both = sorted(set(cur["keep_without_node"]) & set(cur["exclude_nodes"]))
    if both:
        raise CurationError(f"curation keep_without_node and exclude_nodes both name: {', '.join(both)}")
    if cockpit is None:
        return
    has_node = sorted(n for n in cur["keep_without_node"] if cockpit_presence(n, cockpit.pw, cockpit.rr) != "none")
    if has_node:
        raise CurationError("curation keep_without_node names nodes a cockpit model has (the entry does nothing, remove "
                            "it): " + ", ".join(has_node))
    left_out = sorted(n for n in cur["keep_without_node"] if n not in kept)
    if left_out:
        raise CurationError("curation keep_without_node names nodes the rules still leave out: "
                            + ", ".join(f"{n} ({dropped_why.get(n, '?')})" for n in left_out))


def status_row(entry):
    for field in ("key", "label", "panel", "announce"):
        if field not in entry:
            raise CurationError(f"status entry {entry!r} has no {field}")
    positions = entry.get("positions") or []
    if not all(isinstance(p, dict) for p in positions):
        positions = [{"value": i, "label": p} for i, p in enumerate(positions)]
    return {"key": entry["key"], "panel": entry["panel"], "label": entry["label"], "positions": positions,
            "announce": bool(entry["announce"])}


def flow_order(controls, cur):
    """(the controls in the order the pilot tabs through them, the 'Section / Panel' of every panel panel_order does
    not list, in that order).

    panel_order (section -> panel names) puts those panels first in their section, in the listed order; the rest
    follow alphabetically. control_order (panel -> node names) puts those controls first in their panel, in the listed
    order; the rest follow in the order they arrive in, which is cockpit order. The sections keep SECTIONS' order.
    A section the map does not have, a name that matches nothing, a node that is not in its panel, a repeat and an
    empty list are errors: an order entry that does nothing is a stale one."""
    where = {c["node"]: (c["section"], c["panel"]) for c in controls}
    panels = set(where.values())
    for section, names in cur["panel_order"].items():
        if section not in SECTIONS:
            raise CurationError(f"curation panel_order names a section the map does not have: {section}")
        if not names:
            raise CurationError(f"curation panel_order lists no panels for {section} (remove the entry)")
        twice = sorted({n for n in names if names.count(n) > 1})
        if twice:
            raise CurationError(f"curation panel_order lists a panel twice in {section}: {', '.join(twice)}")
        missing = [n for n in names if (section, n) not in panels]
        if missing:
            raise CurationError(f"curation panel_order names panels {section} does not have: {', '.join(missing)}")
    homes = collections.defaultdict(list)  # panel name -> the sections that have a panel of that name
    for section, panel in sorted(panels, key=lambda sp: SECTIONS.index(sp[0])):
        homes[panel].append(section)
    for panel, nodes in cur["control_order"].items():
        if panel not in homes:
            raise CurationError(f"curation control_order names a panel no control is on: {panel}")
        if len(homes[panel]) > 1:
            raise CurationError(f"curation control_order names {panel}, which is in more than one section "
                                f"({', '.join(homes[panel])})")
        if not nodes:
            raise CurationError(f"curation control_order lists no nodes for {panel} (remove the entry)")
        twice = sorted({n for n in nodes if nodes.count(n) > 1})
        if twice:
            raise CurationError(f"curation control_order lists a node twice in {panel}: {', '.join(twice)}")
        elsewhere = [n for n in nodes if where.get(n) != (homes[panel][0], panel)]
        if elsewhere:
            raise CurationError(f"curation control_order lists nodes that are not in {panel}: " + ", ".join(
                f"{n} (in {where[n][0]} / {where[n][1]})" if n in where else f"{n} (not in the map)"
                for n in elsewhere))

    panel_rank = {s: {n: i for i, n in enumerate(names)} for s, names in cur["panel_order"].items()}
    node_rank = {p: {n: i for i, n in enumerate(nodes)} for p, nodes in cur["control_order"].items()}

    def key(c):
        rank = panel_rank.get(c["section"], {}).get(c["panel"])
        place = node_rank.get(c["panel"], {}).get(c["node"])
        return (SECTIONS.index(c["section"]),
                (0, rank, "") if rank is not None else (1, 0, c["panel"]),
                (0, place) if place is not None else (1, 0))

    ordered = sorted(controls, key=key)  # stable: what no order lists keeps the order it came in
    unlisted = [f"{section} / {panel}" for section, panel in dict.fromkeys((c["section"], c["panel"]) for c in ordered)
                if panel not in panel_rank.get(section, {})]
    return ordered, unlisted


def build_map(rows, cur, aircraft, version, cockpit=None):
    """(the map, a report of what was dropped and which curation entries did nothing).

    cockpit is what read_cockpit made of the package's two interior models: with it the map holds only controls the cockpit
    has (a control no model has a node for is left out, named in the report under "absent", and one only one model has says
    so in its label, named under "variant_only"). Given None nothing is judged on the cockpit, which is what the tests of
    the other rules want; the run itself always has one."""
    for table in ("guarded", "direct_write", "kinds", "positions"):  # the entry holds all a lever has: before any row is built
        twice = sorted(set(cur["levers"]) & set(cur[table]))
        if twice:
            raise CurationError(f"curation levers and {table} both name: {', '.join(twice)} (a lever is a switch with the "
                                "state var and the positions of its levers entry, no click code to leave out and no guard)")
    for table in ("levers", "guarded", "direct_write", "kinds", "positions"):  # likewise: the entry holds all the knob has
        twice = sorted(set(cur["direct_positions"]) & set(cur[table]))
        if twice:
            raise CurationError(f"curation direct_positions and {table} both name: {', '.join(twice)} (a direct_positions "
                                "knob is a switch with the state var and the positions of its entry, no click code to leave "
                                "out, no lever to drag and no guard)")
    keyed, seen = [], set()
    dropped = collections.Counter()
    suspected = {}
    unmapped = set()
    dropped_why = {}
    absent = {}
    kept_row = {}
    for index, row in enumerate(rows):
        control, why, slips = convert_row(row, cur, cockpit)
        if why:
            dropped[why] += 1
            dropped_why[row["node"]] = why
            if why == NO_COCKPIT_NODE and row["node"] not in absent:  # named by the label it would have had, and its banner
                absent[row["node"]] = {"banner": row["panel"], "label": label_for(row, KIND_MAP[row["kind"]], cur)}
        elif control["node"] in seen:
            dropped["duplicate node"] += 1
        else:
            seen.add(control["node"])
            kept_row[control["node"]] = row
            if row["panel"] not in cur["panels"] and node_rule(row, cur) is None:
                unmapped.add(row["panel"])
            if slips:
                suspected[control["node"]] = slips
            keyed.append(((SECTIONS.index(control["section"]), control["panel"], row.get("line", 0), index), control))
    keyed.sort(key=lambda kc: kc[0])
    tell_apart(keyed, {r["node"]: r for r in reversed(rows)}, cur)
    controls = [c for _, c in keyed]
    variant_only = mark_variants(controls, cockpit)  # before unique_labels: the suffix tells two same-named controls apart
    for c in controls:  # the engine type whose cockpit alone has it, else null: the last field of every control
        c["variant"] = variant_only.get(c["node"])
    unique_labels(controls)

    in_package = {r["node"] for r in rows}
    first_row = {}
    for r in rows:
        first_row.setdefault(r["node"], r)
    for table in ("labels", "positions", "kinds", "exclude_nodes", "include_nodes", "guarded", "levers", "direct_positions",
                  "direct_write", "hold_ms", "keep_without_node"):
        stale = sorted(n for n in cur[table] if n not in in_package)
        if stale:
            raise CurationError(f"curation {table} name nodes the package does not have: {', '.join(stale)}")
    for table in ("positions", "kinds"):  # one place for a guarded control's positions and kind: its entry
        twice = sorted(set(cur["guarded"]) & set(cur[table]))
        if twice:
            raise CurationError(f"curation guarded and {table} both name: {', '.join(twice)} (a guarded control is a "
                                "switch with the positions of its guarded entry)")
    twice = sorted(set(cur["guarded"]) & set(cur["direct_write"]))
    if twice:
        raise CurationError(f"curation guarded and direct_write both name: {', '.join(twice)} (a guarded control already "
                            "has no click code: the app opens its guard and writes the position)")
    kept = {c["node"] for c in controls}
    check_keep_without_node(kept, dropped_why, cur, cockpit)
    included = set(cur["include_nodes"])
    both = sorted(included & set(cur["exclude_nodes"]))
    if both:
        raise CurationError(f"curation include_nodes and exclude_nodes both name: {', '.join(both)}")
    hidden = {r["node"] for r in rows if r["panel"] in cur["exclude_banners"]}
    idle = sorted(included - hidden)
    if idle:
        raise CurationError(f"curation include_nodes name nodes no excluded banner hides (the entry does nothing, "
                            f"remove it): {', '.join(idle)}")
    left_out = sorted(included - kept)
    if left_out:
        raise CurationError("curation include_nodes name nodes the rules still leave out: "
                            + ", ".join(f"{n} ({dropped_why.get(n, '?')})" for n in left_out))
    # A taken-in control must be placed by a node rule: without one it would land on a panel named after the very
    # banner that is excluded.
    unplaced = sorted(n for n in included if node_rule(first_row[n], cur) is None)
    if unplaced:
        raise CurationError(f"curation include_nodes name nodes no node_panels rule places: {', '.join(unplaced)} "
                            "(add a node_panels rule naming their panel)")
    for node, entry in cur["guarded"].items():  # the guard keeps its own row: the app opens it
        guard = entry["guard"]
        if guard not in kept:
            reason = ("the package does not have it" if guard not in in_package
                      else f"the rules leave it out: {dropped_why.get(guard, '?')}")
            raise CurationError(f"curation guarded: the guard {guard} of {node} has no row in the map ({reason}); a "
                                "guard keeps its own row, which the app opens")
    check_direct_write(controls, kept_row, dropped_why, cur)
    check_guarded(controls, first_row, cur)
    check_levers(first_row, cur)
    check_direct_positions(first_row, cur)
    apply_hold_ms(controls, dropped_why, cur)
    reject_gate_notes(controls, cur)
    # Last, after every label is made: the order the pilot tabs through (panel_order, control_order).
    controls, unordered = flow_order(controls, cur)
    banners = {r["panel"] for r in rows}
    report = {
        "dropped": dropped,
        "absent": absent,
        "variant_only": {c["node"]: {"variant": variant_only[c["node"]], "label": c["label"]}
                         for c in controls if c["node"] in variant_only},
        "kept_without_node": sorted(set(cur["keep_without_node"])) if cockpit is not None else [],
        "taken_in": sorted(included),
        "guarded": {n: guarded_how(first_row[n], cur) for n in cur["guarded"]},
        "levers": {n: lever_how(first_row[n], cur) for n in cur["levers"]},
        "direct_positions": {n: direct_positions_how(first_row[n], cur) for n in cur["direct_positions"]},
        "direct_write": {n: direct_write_how(kept_row[n]) for n in cur["direct_write"]},
        "hold_ms": {n: hold_ms_how(ms) for n, ms in cur["hold_ms"].items()},
        "suspected_slips": suspected,
        "overrides_on_dropped": sorted({n for t in ("labels", "positions", "kinds") for n in cur[t] if n not in kept}),
        "unused_banners": sorted((set(cur["exclude_banners"]) | set(cur["panels"])) - banners),
        "unmapped_banners": sorted(unmapped),
        "unordered_panels": unordered,
    }
    status = [status_row(s) for s in cur["status"]]
    return {"aircraft": aircraft, "package_version": version, "controls": controls, "status": status}, report


def dumps(map_) -> str:
    return json.dumps(map_, indent=1, ensure_ascii=False) + "\n"


# ---- curation and package --------------------------------------------------------------------------------------
def is_number(x) -> bool:
    return isinstance(x, (int, float)) and not isinstance(x, bool)


def valid_guarded(entry) -> bool:
    """An entry of curation's 'guarded': every one of GUARDED_FIELDS, the positions as names or as {value, label} pairs, a
    number for the value the guard reads while closed and a number or null for the value closing it writes."""
    if not (isinstance(entry, dict) and set(entry) == set(GUARDED_FIELDS)):
        return False
    names = entry["positions"]

    def pair(p):
        return (isinstance(p, dict) and set(p) == {"value", "label"} and is_number(p["value"])
                and isinstance(p["label"], str))

    return (all(isinstance(entry[k], str) and entry[k] for k in ("state_var", "guard"))
            and isinstance(names, list) and bool(names)
            and (all(isinstance(n, str) and n for n in names) or all(pair(n) for n in names))
            and is_number(entry["guard_closed"]) and (entry["close_sets"] is None or is_number(entry["close_sets"])))


def valid_state_positions(entry) -> bool:
    """An entry of curation's 'levers' or 'direct_positions': a state var and at least two {value, label} positions (a lever's
    or a knob's row names no values a list of names could be laid onto: Auto Rate has 131), the values rising in the order of
    the control's travel (so no value repeats) and every label different and not empty."""
    if not (isinstance(entry, dict) and set(entry) == set(LEVER_FIELDS)):
        return False
    state_var, positions = entry["state_var"], entry["positions"]

    def pair(p):
        return (isinstance(p, dict) and set(p) == {"value", "label"} and is_number(p["value"])
                and isinstance(p["label"], str) and bool(p["label"]))

    if not (isinstance(state_var, str) and state_var and isinstance(positions, list) and len(positions) >= 2
            and all(pair(p) for p in positions)):
        return False
    values = [p["value"] for p in positions]
    return all(a < b for a, b in zip(values, values[1:])) and len({p["label"].lower() for p in positions}) == len(positions)


def load_curation(path) -> dict:
    with open(path, encoding="utf-8-sig") as fh:
        data = json.load(fh)
    unknown = sorted(k for k in data if k not in CURATION_KEYS and not k.startswith("_"))
    if unknown:
        raise CurationError(f"{path}: unknown key(s) {', '.join(unknown)} (known: {', '.join(CURATION_KEYS)})")
    cur = {k: kind() for k, kind in CURATION_KEYS.items()}
    for key, kind in CURATION_KEYS.items():
        if key in data:
            if not isinstance(data[key], kind):
                raise CurationError(f"{path}: {key} must be a {kind.__name__}")
            cur[key] = data[key]
    cur["spelling"] = {k.upper(): v for k, v in cur["spelling"].items()}
    for banner, entry in cur["panels"].items():
        if not isinstance(entry, str) and not (isinstance(entry, dict) and "name" in entry):
            raise CurationError(f"{path}: panels[{banner!r}] is a name or {{name, section}}")
        if isinstance(entry, dict) and entry.get("section", "Other") not in SECTIONS:
            raise CurationError(f"{path}: panels[{banner!r}] names an unknown section")
    for node, kind in cur["kinds"].items():
        if not isinstance(kind, str) or kind not in KIND_TARGETS:
            raise CurationError(f"{path}: kinds[{node!r}] is {' or '.join(KIND_TARGETS)}")
    for node, entry in cur["guarded"].items():
        if not valid_guarded(entry):
            raise CurationError(f"{path}: guarded[{node!r}] is {{{', '.join(GUARDED_FIELDS)}}}: names or {{value, label}} "
                                "for positions, numbers for guard_closed and close_sets (which may be null)")
    for node, entry in cur["levers"].items():
        if not valid_state_positions(entry):
            raise CurationError(f"{path}: levers[{node!r}] is {{{', '.join(LEVER_FIELDS)}}}: a state var and at least two "
                                "{value, label} positions, the values rising in the order of the lever's travel and the "
                                "labels all different")
    for node, entry in cur["direct_positions"].items():
        if not valid_state_positions(entry):
            raise CurationError(f"{path}: direct_positions[{node!r}] is {{{', '.join(DIRECT_POSITIONS_FIELDS)}}}: a state var "
                                "and at least two {value, label} positions, the values rising (none repeated) and the labels "
                                "all different and not empty")
    for key in ("direct_write", "keep_without_node"):
        if not all(isinstance(node, str) and node for node in cur[key]):
            raise CurationError(f"{path}: {key} is a list of node names")
    for node, ms in cur["hold_ms"].items():
        if not (node and isinstance(ms, int) and not isinstance(ms, bool)
                and MIN_CURATED_HOLD_MS <= ms <= MAX_CURATED_HOLD_MS):
            raise CurationError(f"{path}: hold_ms[{node!r}] is a node name and a hold in whole milliseconds from "
                                f"{MIN_CURATED_HOLD_MS} to {MAX_CURATED_HOLD_MS}")
    for rule in cur["node_panels"]:
        if "panel" not in rule or not ({"banner", "prefix"} & set(rule)):
            raise CurationError(f"{path}: node_panels rule {rule!r} needs a panel and a banner or prefix")
        if rule.get("section", "Other") not in SECTIONS:
            raise CurationError(f"{path}: node_panels rule {rule!r} names an unknown section")
    for section, names in cur["panel_order"].items():  # which names fit the package is judged in flow_order
        if not (isinstance(names, list) and all(isinstance(n, str) for n in names)):
            raise CurationError(f"{path}: panel_order[{section!r}] is a list of panel names")
    for panel, nodes in cur["control_order"].items():
        if not (isinstance(nodes, list) and all(isinstance(n, str) for n in nodes)):
            raise CurationError(f"{path}: control_order[{panel!r}] is a list of node names")
    return cur


def package_roots(local_app_data=None):
    base = pathlib.Path(local_app_data or os.environ.get("LOCALAPPDATA", ""), "Packages",
                        "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages")
    return [base / "Community", base / "Community2024"]


def find_package(local_app_data=None):
    """The installed package folder, or None. Community first, then Community2024."""
    if not (local_app_data or os.environ.get("LOCALAPPDATA")):
        return None
    for root in package_roots(local_app_data):
        candidate = root / PACKAGE_NAME
        if (candidate / "manifest.json").is_file():
            return candidate
    return None


def package_version(pkg: pathlib.Path) -> str:
    try:
        return str(json.loads(extract_controls.read_text(pkg / "manifest.json")).get("package_version", "?"))
    except (OSError, ValueError):
        return "?"


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description="Generate the BlueBird 757-200 control map.")
    ap.add_argument("package", nargs="?", help="package folder (the one holding manifest.json). Omit to look in "
                                               "Community and Community2024 under %%LOCALAPPDATA%%.")
    ap.add_argument("--out", default=str(DEFAULT_OUT), help="the JSON to write (default: the app's Resources)")
    ap.add_argument("--curation", default=str(DEFAULT_CURATION))
    args = ap.parse_args(argv)

    pkg = pathlib.Path(args.package).resolve() if args.package else find_package()
    if pkg is None:
        sys.exit(f"{PACKAGE_NAME} not found under %LOCALAPPDATA%\\Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\"
                 f"LocalCache\\Packages\\Community or Community2024: pass the package folder")
    if not (pkg / "manifest.json").is_file():
        sys.exit(f"{pkg} has no manifest.json: pass the package folder itself")

    try:  # first, before the slow reading of the behavior files: without both models no control can be judged
        cockpit = read_cockpit(pkg)
    except CurationError as e:
        sys.exit(f"curation: {e}")
    rows, _lights, summary = extract_controls.extract(pkg)
    if summary["errors"]:
        print("files that did not parse: " + "; ".join(summary["errors"]), file=sys.stderr)
    if not rows:
        sys.exit(f"no controls found in {pkg}")
    try:
        cur = load_curation(args.curation)
        map_, report = build_map(rows, cur, pkg.name, package_version(pkg), cockpit)
    except CurationError as e:
        sys.exit(f"curation: {e}")

    out = pathlib.Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", encoding="utf-8", newline="\n") as fh:
        fh.write(dumps(map_))

    controls = map_["controls"]
    print(f"wrote {out}")
    print(f"  package          : {pkg.name} {map_['package_version']}")
    print(f"  extracted rows   : {len(rows)}")
    print(f"  controls: {len(controls)}")
    for kind, n in sorted(collections.Counter(c["kind"] for c in controls).items()):
        print(f"    {kind:8s} {n}")
    print(f"  status rows      : {len(map_['status'])}")
    print(f"  taken in by node : {len(report['taken_in'])} (under excluded banners)")
    print(f"  guarded controls : {len(report['guarded'])} (no click code of their own: the app opens the guard and writes "
          "the state var)")
    for node, how in report["guarded"].items():
        print(f"    {node}: {how}")
    print(f"  levers           : {len(report['levers'])} (a lever the rules leave out, built as a switch with no click code: "
          "the app writes the state var to the picked position)")
    for node, how in report["levers"].items():
        print(f"    {node}: {how}")
    print(f"  direct positions : {len(report['direct_positions'])} (a knob the rules leave out, built as a switch with no click "
          "code: the app writes the state var to the picked position)")
    for node, how in report["direct_positions"].items():
        print(f"    {node}: {how}")
    print(f"  direct-write     : {len(report['direct_write'])} (the vendor's click code is left out: the app writes the "
          "state var to the picked position)")
    for node, how in report["direct_write"].items():
        print(f"    {node}: {how}")
    print(f"  curated holds    : {len(report['hold_ms'])} (a button the rules keep as a button, held by curation: the app "
          "presses it, holds it that long and lets go)")
    for node, how in report["hold_ms"].items():
        print(f"    {node}: {how}")
    print(f"  cockpit models   : {INTERIOR_MODELS['pw']} ({len(cockpit.pw)} nodes), {INTERIOR_MODELS['rr']} "
          f"({len(cockpit.rr)} nodes)")
    print(f"  no cockpit node  : {len(report['absent'])} (left out: a node of neither model)")
    for node, what in report["absent"].items():
        print(f"    {node}: {what['label']} (vendor banner {what['banner']})")
    print(f"  one variant only : {len(report['variant_only'])} (labeled {VARIANT_TAGS['pw']} or {VARIANT_TAGS['rr']}: "
          "a node of one model only)")
    for node, what in report["variant_only"].items():
        print(f"    {node}: {what['variant'].upper()} only, {what['label']}")
    print(f"  kept without node: {len(report['kept_without_node'])} (curation's keep_without_node: a control no model has a "
          "node for, kept)")
    for node in report["kept_without_node"]:
        print(f"    {node}: kept without a cockpit node")
    print("  dropped:")
    for why, n in sorted(report["dropped"].items(), key=lambda kv: (-kv[1], kv[0])):
        print(f"    {n:5d}  {why}")
    panels = collections.Counter((c["section"], c["panel"]) for c in controls)  # in map order: the order they are tabbed
    print(f"  panels           : {len(panels)}")
    for (section, panel), n in panels.items():
        print(f"    {section} / {panel}: {n}")
    if report["suspected_slips"]:
        print(f"  suspected slips in the vendor's click code, replayed verbatim: {len(report['suspected_slips'])} controls")
        for node, what in report["suspected_slips"].items():
            print(f"    {node}: {'; '.join(what)}")
    for node in report["overrides_on_dropped"]:
        print(f"note: curation names {node}, which the rules leave out", file=sys.stderr)
    for banner in report["unused_banners"]:
        print(f"note: curation names the banner {banner!r}, which no control uses", file=sys.stderr)
    for banner in report["unmapped_banners"]:
        print(f"note: the banner {banner!r} has no panel name in curation (its Title Case is used)", file=sys.stderr)
    if report["unordered_panels"]:
        print("note: panel_order does not list " + ", ".join(report["unordered_panels"])
              + " (they follow the listed panels, alphabetically)", file=sys.stderr)


if __name__ == "__main__":
    main()
