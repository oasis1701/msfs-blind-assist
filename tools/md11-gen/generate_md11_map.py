#!/usr/bin/env python3
"""
Generate the TFDi MD-11 control map from the aircraft's ModelBehaviorDefs XML.

The MD-11's ModelBehaviorDefs are emitted by TFDi's own ModelBehaviorsExporter and
carry everything MSFSBA needs to build panels, in one place:

    <UseTemplate Name="TFDi_Design_MD11_Button_Template">
      <TOOLTIPID>Captain ND Map Mode</TOOLTIPID>
      <NODE_ID>MD11_LECP_MAP_BT</NODE_ID>          <- the L:var
      <LEFT_BUTTON_DOWN>86018</LEFT_BUTTON_DOWN>   <- CEVENT id (press)
      <LEFT_BUTTON_UP>86019</LEFT_BUTTON_UP>       <- CEVENT id (release)
    </UseTemplate>

TOOLTIPID is richer than it looks: beyond the label it embeds the aircraft's own
value->label state map as an RPN formatting expression, e.g.

    Flaps/Slats (%((L:MD11_FLAP_RNG))%{case}%{:0}Up/Retracted%{:20}Up/Extended%{end})

so the detent names, switch position names and annunciator wording all come straight
from TFDi rather than being invented here. That map becomes ValueDescriptions on the
generated SimVarDefinition, which is what a screen reader ends up speaking.

Output: md11_control_map.json (consumed by the C# definition + checked in for review).

Usage:
    python generate_md11_map.py [--pkg <community/tfdidesign-aircraft-md11>]
                                [--wasm <md11host.wasm>]
                                [--out md11_control_map.json]
"""

import argparse
import json
import os
import re
import sys
from collections import Counter, defaultdict

import md11_paths

# No hardcoded package path. The MD-11 is FOUND (md11_paths) across FS2020 and
# FS2024, MS Store, Steam and external/custom package folders. The previous
# default was one developer's absolute FS2020 Store path and worked nowhere
# else; the previous wasm path guess omitted the "common" level that a real
# FS2024 install has, and a miss silently produced a map with NO wasm-derived
# L:vars (the PFD speed tape and V-speeds, which have no other source).

# ---------------------------------------------------------------------------
# Template classification.
#
# Kind drives how MSFSBA renders the control:
#   button   -> momentary; press/release CEVENT pair (see PRESS-RELEASE note below)
#   knob     -> rotary; WHEEL_UP/WHEEL_DOWN step events
#   knob_pp  -> rotary + push/pull (the FCP's SPD/HDG/ALT selectors)
#   switch   -> multi-position; discrete inc/dec events
#   annun    -> read-only indicator lamp (no events, L:var only)
#   guard    -> hinged cover over another control
#   lever    -> flap / spoiler levers
#   handle   -> fire handles (pull + rotate)
# ---------------------------------------------------------------------------
TEMPLATE_KINDS = {
    "TFDi_Design_MD11_Button_Template": "button",
    "TFDi_Design_MD11_Knob_Template": "knob",
    "TFDi_Design_MD11_Volume_Knob": "knob",
    "TFDi_Design_MD11_Infinite_Knob": "knob",
    "TFDi_Design_MD11_ELEV_FEEL_Knob": "knob",
    "TFDi_Design_MD11_Knob_PushPull": "knob_pp",
    "TFDi_Design_MD11_Knob_Push": "knob_push",
    "TFDi_Design_MD11_Switch_Template": "switch",
    "TFDi_Design_MD11_Switch_SingleEvent_Template": "switch",
    "TFDi_Design_MD11_3Pos_Switch_Hold": "switch",
    "TFDi_Design_MD11_3Pos_Knob_Hold": "switch",
    "TFDi_Design_MD11_Annunciator": "annun",
    "TFDi_Design_MD11_Guard_Template": "guard",
    "MD11_Flap_Lever": "lever",
    "TFDi_Design_MD11_SpoilerLever": "lever",
    "MD11_Long_Trim_Switch": "switch",
    "TFDi_Design_MD11_ENG_Fire_Handle": "handle",
    "TFDi_Design_MD11_APU_Fire_Handle": "handle",
    "TFDi_Design_MD11_Clickspot": "button",
    "TFDi_Design_MD11_Clickspot_UD": "button",
    "TFDi_Design_MD11_Range_Template": "knob",
}

# Cockpit-area prefixes, from the NODE_ID's second underscore token. TFDi's own
# naming; the labels here are what MSFSBA shows as panel section names.
# L:vars that describe the AIRFRAME, never a control's state. A tooltip may reference one to pick
# its wording between variants; that is not the control's position. Keep this list tiny and
# evidence-based — each entry needs a reason, because wrongly excluding a real state var silently
# repoints a control at its own node id.
LABEL_ONLY_VARS = {
    # The freighter/pax split. Used by the cabin-temperature knobs to say "Courier Cabin" /
    # "Main Cargo Deck" on the MD-11F where the passenger jet says "Forward Cabin" / "Middle
    # Cabin". Confirmed in Overhead.xml: both knobs carry NUM_STATES=8 and an ANIM_NAME equal to
    # their own node id, so the temperature — not the variant — is what they select.
    "MD11_EFB_IS_CARGO",
}

AREA_LABELS = {
    "OVHD": "Overhead",
    "AOVHD": "Aft Overhead",
    "PED": "Pedestal",
    "CGS": "Glareshield (Flight Control Panel)",
    "LECP": "Captain EFIS Control Panel",
    "RECP": "F/O EFIS Control Panel",
    "MIP": "Main Instrument Panel",
    "THR": "Throttle Quadrant",
    "BKR": "Circuit Breakers",
    "LSIDE": "Captain Side Panel",
    "RSIDE": "F/O Side Panel",
    "CTR": "Center Instrument",
    "EXT": "Doors and Exterior",
    "CARGO": "Cargo",
    "LTS": "Lighting",
    "LMCDU": "MCDU (Left)",
    "CMCDU": "MCDU (Center)",
    "RMCDU": "MCDU (Right)",
    "LYOKE": "Captain Yoke",
    "RYOKE": "F/O Yoke",
    "FLAP": "Flaps",
    "DIALAFLAP": "Dial-A-Flap",
    "SPDBRK": "Speedbrake",
    "GSL": "Ground Service (Left)",
    "GSR": "Ground Service (Right)",
    "CAB": "Cabin",
    "WIPER": "Wipers",
    "TOEBRAKE": "Toe Brakes",
    "STBY": "Standby Instruments",
    "ASU": "Air Start Unit",
    "CPT": "Audio Panel (Captain)",
    "FO": "Audio Panel (F/O)",
    "OBS": "Audio Panel (Observer)",
    "OPT": "Aircraft Options",
    "EFB": "EFB",
    "FLIGHTDECK": "Flight Deck Door",
    "YOKE": "Yoke",
}

# ---------------------------------------------------------------------------
# Curated overrides.
#
# The generic TOOLTIPID parser handles %{case} maps, but a few controls encode
# their state as an RPN *range* test rather than discrete cases, and the parser
# cannot see those. Rather than teach it to interpret arbitrary RPN, pin the
# handful of affected controls here with the values read out of the same tooltip.
#
# The flap lever is the important one. Its tooltip is:
#   %(38 65 (L:MD11_FLAP_RNG) rng)%{if}Dial-A-Flap %(10 (L:MD11_DIALAFLAP_IND_RNG) 6.6667 / +)%!d!/Extended
#   %{else}%((L:MD11_FLAP_RNG))%{case}%{:0}Up/Retracted%{:20}Up/Extended%{:70}28/Extended...
# i.e. FLAP_RNG in [38,65] IS the Dial-A-Flap detent -- a range, so %{case} misses
# it entirely and the lever reads as 5 positions instead of the real 6.
#
# The MD-11 handle is combined flap+slat and runs, clean to fully extended:
#   UP/RET -> 0/EXT -> DIAL-A-FLAP -> 28 -> 35 -> 50
# with a physical gate at 28 so the handle cannot slip straight between the
# take-off range and the landing range. A go-around from 35/50 retracts to 28
# first, which is why 28 is its own detent and not just a step on the way up.
CURATED = {
    "MD11_FLAP_LATCH": {
        "label": "Flaps/Slats",
        "detents": [
            {"value": 0, "name": "Flap Up / Slat Retracted"},
            {"value": 20, "name": "Flap 0 / Slat Extended"},
            # Handle in the variable take-off detent; the angle itself comes from
            # the Dial-A-Flap thumbwheel, not the handle position.
            {"value": 50, "range": [38, 65], "name": "Dial-A-Flap", "dial": True},
            {"value": 70, "name": "Flap 28"},
            {"value": 82, "name": "Flap 35"},
            {"value": 100, "name": "Flap 50"},
        ],
        "notes": "Combined flap/slat handle. Gate at 28 blocks take-off<->landing range slips.",
    },
    # Thumbwheel selecting the take-off flap angle used by the DIAL-A-FLAP detent.
    # Angle = 10 + IND_RNG / 6.6667  =>  IND_RNG 0..100 spans 10..25 degrees.
    "MD11_DIALAFLAP_WHEEL_RNG": {
        "label": "Dial-A-Flap Take-off Angle",
        "dial_a_flap": {
            "state_var": "MD11_DIALAFLAP_IND_RNG",
            "min_deg": 10,
            "max_deg": 25,
            "units_per_deg": 6.6667,
            "formula": "degrees = 10 + MD11_DIALAFLAP_IND_RNG / 6.6667",
        },
    },
}

# Fields that carry a CEVENT id.
EVENT_FIELDS = (
    "LEFT_BUTTON_DOWN",
    "LEFT_BUTTON_UP",
    "RIGHT_BUTTON_DOWN",
    "RIGHT_BUTTON_UP",
    "WHEEL_UP",
    "WHEEL_DOWN",
    "PUSH_DOWN",
    "PUSH_UP",
    "PULL_DOWN",
    "PULL_UP",
)


def read_xml(path):
    """Read a behavior XML leniently.

    These files are exporter-generated and contain raw '&', stray degree signs and
    other tokens a strict XML parser rejects, so parse with a regex rather than
    ElementTree -- we only need flat <UseTemplate> blocks, not a real tree.

    Encoding is mixed: most files are UTF-8, but some carry cp1252 degree signs in
    tooltips (the bank-angle limiter's '5°'). Decoding those as UTF-8 yields U+FFFD
    and the degree silently turns into a replacement char the screen reader spells
    out, so fall back to cp1252 rather than lossily replacing.
    """
    with open(path, "rb") as fh:
        raw = fh.read()
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    for enc in ("utf-8", "cp1252", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="replace")


def speakable(text):
    """Normalize label text for a screen reader.

    Symbols that are fine to look at are noise to hear: NVDA reads a bare '°' as
    'degrees' only in some punctuation modes and skips it entirely in others, so
    spell it out here rather than depending on the reader's settings.
    """
    if not text:
        return text
    text = (
        text.replace("°", " degrees")
        .replace("△", "delta")
        .replace("�", "")
        .replace("–", "-")
        .replace("—", "-")
    )
    return re.sub(r"\s+", " ", text).strip()


USETEMPLATE_RE = re.compile(
    r'<UseTemplate\s+Name="([^"]+)"\s*(/>|>(.*?)</UseTemplate>)', re.S
)
FIELD_RE = re.compile(r"<([A-Z0-9_]+)>(.*?)</\1>", re.S)


def parse_tooltip(tooltip):
    """Split a TOOLTIPID into (label, state_var, value_map).

    TFDi tooltips come in two shapes.

    (a) Trailing state parenthetical -- the label proper, then the live state:
        'Engine 1 Fire Handle (%((L:MD11_..._SW))%{case}%{:0}Normal%{:1}GFD%{end})'
        -> label 'Engine 1 Fire Handle', value_map {0: Normal, 1: GFD}

    (b) Inline dynamic word -- the label itself changes with state:
        'Autopilot %((L:MD11_AP_HDG_TRK))%{if}Track%{else}Heading%{end} Select'
        -> label 'Autopilot Heading/Track Select'

    Shape (b) must not be treated as (a): there is no trailing parenthetical to
    strip, and naively cutting at the first '%' would throw away ' Select'. The
    inline block is collapsed to 'Heading/Track' so the spoken label stays a
    stable, complete phrase rather than flapping with the aircraft's state.

    Returns (label, state_var, value_map). value_map is lifted verbatim from the
    aircraft so detent/position wording is TFDi's, never invented here.
    """
    if not tooltip:
        return None, None, {}

    tooltip = re.sub(r"\s+", " ", tooltip.strip())

    # The first L:var mentioned anywhere is what the state text keys off.
    state_var = None
    m = re.search(r"L:([A-Za-z0-9_]+)", tooltip)
    if m:
        state_var = m.group(1)

    # ...unless it describes the AIRFRAME rather than the control. A shape-(b) tooltip can
    # reference a variant flag purely to choose its WORDING:
    #     '%((L:MD11_EFB_IS_CARGO))%{if}Courier Cabin%{else}Forward Cabin%{end} Temperature'
    # That is the freighter/pax split, not the knob's position — the knob is an 8-position
    # temperature selector whose real state is its own ANIM_NAME. Taking IS_CARGO as the state
    # makes the control read a 0/1 flag, so a walk to set it can never converge and every
    # selection reports "did not move". Falls through to VIS_VAR / node_id below, which is the
    # var the ANIM_NAME actually names.
    if state_var in LABEL_ONLY_VARS:
        state_var = None

    # --- (a) peel off a trailing '(<formatting expr>)' -------------------------
    expr = ""
    m = re.search(r"\s\((%.*)\)\s*$", tooltip, re.S)
    if m:
        expr = m.group(1)
        label = tooltip[: m.start()].strip()
    else:
        label = tooltip

    value_map = {}

    def _cases(text):
        out = {}
        for val, lbl in re.findall(r"%\{:\s*([-\d.]+)\s*\}([^%]*)", text):
            lbl = lbl.strip()
            if lbl:
                out[val] = lbl
        return out

    if expr:
        value_map = _cases(expr)
        if not value_map:
            m = re.search(r"%\{if\}([^%]*)%\{else\}([^%]*)%\{end\}", expr)
            if m:
                on, off = m.group(1).strip(), m.group(2).strip()
                if on and off:
                    value_map = {"1": on, "0": off}

    # --- (b) collapse inline dynamic blocks left in the label -------------------
    # '%(<rpn>)%{if}A%{else}B%{end}' -> 'B/A'  (false state first: it reads better
    # as the resting position, e.g. 'Heading/Track', 'IAS/Mach').
    def _inline_if(m):
        a, b = m.group(1).strip(), m.group(2).strip()
        if not value_map:
            value_map.update({"1": a, "0": b})
        return f"{b}/{a}" if a and b else (a or b)

    label = re.sub(
        r"%\([^)]*(?:\)[^)%]*)*?\)\s*%\{if\}([^%]*)%\{else\}([^%]*)%\{end\}",
        _inline_if,
        label,
    )

    # '%(<rpn>)%{case}%{:0}A%{:1}B%{end}' -> 'A/B'
    def _inline_case(m):
        cases = _cases(m.group(0))
        if cases and not value_map:
            value_map.update(cases)
        return "/".join(cases.values()) if cases else ""

    label = re.sub(
        r"%\([^)]*(?:\)[^)%]*)*?\)\s*%\{case\}.*?%\{end\}", _inline_case, label
    )

    # Any remaining numeric interpolation ('%(<rpn>)%!d!', '%!1.2f!') is live data,
    # not label text -- drop it.
    label = re.sub(r"%\([^)]*(?:\)[^)%]*)*?\)", "", label)
    label = re.sub(r"%![^!]*!", "", label)
    label = re.sub(r"%\{[^}]*\}", "", label)

    label = re.sub(r"\s+", " ", label).strip().strip("()").strip()
    label = re.sub(r"\s+([,/])", r"\1", label)
    label = speakable(label)
    value_map = {k: speakable(v) for k, v in value_map.items()}

    return (label or None), state_var, value_map


def area_of(node_id):
    """Cockpit area from an MD11_<AREA>_... node id."""
    if not node_id:
        return "Other"
    parts = node_id.split("_")
    if len(parts) >= 2 and parts[0] == "MD11":
        return AREA_LABELS.get(parts[1], parts[1].title())
    return "Other"


# Expansions for the node-id humanizer. Annunciators (and ~a third of the buttons)
# carry no TOOLTIPID at all, so their spoken label has to be derived from the node
# id -- 'MD11_OVHD_ELEC_GEN1_ARM_LT' -> 'Generator 1 Arm'. A screen reader reads
# these aloud, so expand the abbreviations rather than spelling out consonants.
ABBREV = {
    "LT": "light", "BT": "button", "KB": "knob", "SW": "switch", "GRD": "guard",
    "LVR": "lever", "IND": "indicator", "PB": "pushbutton", "ANN": "annunciator",
    "GEN": "generator", "APU": "APU", "ELEC": "electrical", "HYD": "hydraulic",
    "PNEU": "pneumatic", "PRESS": "pressurization", "TEMP": "temperature",
    "PWR": "power", "EXT": "external", "XFER": "transfer", "XFEED": "crossfeed",
    "ISOL": "isolation", "VLV": "valve", "PMP": "pump", "ENG": "engine",
    "FIRE": "fire", "AGENT": "agent", "DISCH": "discharged", "ARM": "arm",
    "AUTO": "auto", "MAN": "manual", "NORM": "normal", "OVRD": "override",
    "STBY": "standby", "EMER": "emergency", "BATT": "battery", "BUS": "bus",
    "AC": "AC", "DC": "DC", "XPNDR": "transponder", "NAV": "nav", "COM": "com",
    "ADF": "ADF", "VOR": "VOR", "ILS": "ILS", "DME": "DME", "RA": "radio altimeter",
    "FD": "flight director", "AP": "autopilot", "AT": "autothrottle",
    "ATS": "autothrottle", "SPD": "speed", "HDG": "heading", "ALT": "altitude",
    "VS": "vertical speed", "FPA": "flight path angle", "IAS": "IAS",
    "MACH": "Mach", "TRK": "track", "PROF": "profile", "FMS": "FMS",
    "APPR": "approach", "LAND": "land", "GA": "go around", "TO": "takeoff",
    "CLB": "climb", "CRZ": "cruise", "DES": "descent", "FLAP": "flap",
    "SLAT": "slat", "GEAR": "gear", "BRK": "brake", "SPDBRK": "speedbrake",
    "ANTISKID": "antiskid", "STEER": "steering", "TILLER": "tiller",
    "TRIM": "trim", "AIL": "aileron", "ELEV": "elevator", "RUD": "rudder",
    "STAB": "stabilizer", "LTS": "lights", "FLOOD": "flood", "PNL": "panel",
    "DOME": "dome", "BCN": "beacon", "STROBE": "strobe", "TAXI": "taxi",
    "RWY": "runway", "TURNOFF": "turnoff", "LOGO": "logo", "WING": "wing",
    "ICE": "ice", "ANTIICE": "anti-ice", "WAI": "wing anti-ice",
    "EAI": "engine anti-ice", "PROBE": "probe", "WSHLD": "windshield",
    "WIPER": "wiper", "RAIN": "rain", "OXY": "oxygen", "MASK": "mask",
    "PAX": "passenger", "CRG": "cargo", "DOOR": "door", "SLIDE": "slide",
    "CAB": "cabin", "PA": "PA", "INT": "interphone", "CALL": "call",
    "ATT": "attendant", "MECH": "mechanic", "GND": "ground", "SVC": "service",
    "PACK": "pack", "BLEED": "bleed", "DUCT": "duct", "FAN": "fan",
    "RECIRC": "recirculation", "COND": "conditioning", "OUTFLOW": "outflow",
    "CPT": "captain", "FO": "first officer", "OBS": "observer",
    "L": "left", "R": "right", "CTR": "center", "UPR": "upper", "LWR": "lower",
    "FWD": "forward", "AFT": "aft", "MAIN": "main", "TAIL": "tail",
    "MSTR": "master", "WARN": "warning", "CAUT": "caution", "FAIL": "fail",
    "INOP": "inoperative", "TEST": "test", "RST": "reset", "SEL": "select",
    "MODE": "mode", "DSPL": "display", "DU": "display unit", "MCDU": "MCDU",
    "EAD": "EAD", "SD": "system display", "PFD": "PFD", "ND": "ND",
    "ISFD": "standby display", "BRT": "brightness", "DIM": "dim",
    "FUEL": "fuel", "TANK": "tank", "QTY": "quantity", "BOOST": "boost",
    "MAGTRU": "magnetic/true", "TCAS": "TCAS", "WXR": "weather radar",
    "TERR": "terrain", "GPWS": "GPWS", "EVAC": "evacuation", "SMOKE": "smoke",
    "SEATBELT": "seatbelt", "NOSMOKING": "no smoking", "IRS": "IRS",
    "ADIRU": "ADIRU", "ALIGN": "align", "ATTD": "attitude",
}


def humanize(node_id):
    """Turn 'MD11_OVHD_ELEC_GEN1_ARM_LT' into 'Electrical generator 1 arm light'."""
    if not node_id:
        return None
    parts = node_id.split("_")
    if parts and parts[0] == "MD11":
        parts = parts[1:]
    # Drop the leading area token; the area is carried separately.
    if parts and parts[0] in AREA_LABELS:
        parts = parts[1:]
    words = []
    for p in parts:
        # Split a trailing digit run: GEN1 -> generator 1
        m = re.match(r"^([A-Z]+)(\d+)$", p)
        if m:
            stem, num = m.group(1), m.group(2)
            words.append(ABBREV.get(stem, stem.lower()))
            words.append(num)
            continue
        words.append(ABBREV.get(p, p.lower() if not p.isdigit() else p))
    if not words:
        return None
    text = " ".join(w for w in words if w)
    return text[:1].upper() + text[1:]


def collect(pkg_dir):
    base = os.path.join(pkg_dir, "ModelBehaviorDefs", "TFDi_Design", "MD11")
    if not os.path.isdir(base):
        sys.exit(f"ModelBehaviorDefs not found under {base}")

    controls = []
    seen = set()
    stats = Counter()

    for root, _dirs, files in os.walk(base):
        # Templates/ holds the definitions, not the instances -- skip.
        if os.path.basename(root) == "Templates":
            continue
        for fname in sorted(files):
            if not fname.endswith(".xml"):
                continue
            path = os.path.join(root, fname)
            source = os.path.relpath(path, base).replace("\\", "/")
            text = read_xml(path)

            for m in USETEMPLATE_RE.finditer(text):
                tmpl = m.group(1)
                body = m.group(3) or ""
                kind = TEMPLATE_KINDS.get(tmpl)
                if kind is None:
                    stats["skipped_template:" + tmpl] += 1
                    continue

                fields = {}
                for fm in FIELD_RE.finditer(body):
                    fields[fm.group(1)] = (fm.group(2) or "").strip()

                node_id = fields.get("NODE_ID")
                if not node_id:
                    stats["no_node_id"] += 1
                    continue

                events = {}
                for ef in EVENT_FIELDS:
                    v = fields.get(ef)
                    if v and v.lstrip("-").isdigit():
                        events[ef] = int(v)

                # An annunciator with no events is a lamp; its lit state is the
                # L:var itself (VIS_VAR overrides which var drives visibility).
                label, state_var, value_map = parse_tooltip(fields.get("TOOLTIPID"))

                key = (node_id, kind, tuple(sorted(events.items())))
                if key in seen:
                    stats["duplicate"] += 1
                    continue
                seen.add(key)

                # Prefer TFDi's own wording; fall back to the node id only when the
                # exporter emitted no tooltip (every annunciator, ~a third of buttons).
                if label:
                    label_source = "tooltip"
                else:
                    label = humanize(node_id)
                    label_source = "derived"

                # Curated truth wins for the few controls whose state is an RPN
                # range test rather than a %{case} map (see CURATED).
                curated = CURATED.get(node_id)
                if curated:
                    if curated.get("label"):
                        label = curated["label"]
                        label_source = "curated"
                    stats["curated"] += 1

                num_states = fields.get("NUM_STATES")
                controls.append(
                    {
                        "node_id": node_id,
                        "kind": kind,
                        "template": tmpl,
                        "area": area_of(node_id),
                        "label": label,
                        "label_source": label_source,
                        **(
                            {k: v for k, v in curated.items() if k != "label"}
                            if curated
                            else {}
                        ),
                        "state_var": state_var or fields.get("VIS_VAR") or node_id,
                        "value_map": value_map,
                        "num_states": int(num_states)
                        if num_states and num_states.isdigit()
                        else None,
                        "events": events,
                        "guard_id": fields.get("GUARD_ID"),
                        "source": source,
                    }
                )
                stats["kind:" + kind] += 1

    return controls, stats


# Event-name suffixes. The wasm also embeds strings like
# 'MD11_FLAP_LATCH_WHEEL_UP' which are event names, not variables -- filter them
# out of the export-var scan or they read as phantom L:vars.
EVENT_NAME_SUFFIX = re.compile(
    r"_(WHEEL_UP|WHEEL_DOWN|LEFT_BUTTON_DOWN|LEFT_BUTTON_UP|RIGHT_BUTTON_DOWN"
    r"|RIGHT_BUTTON_UP|PUSH_UP|PUSH_DOWN|PULL_UP|PULL_DOWN)$"
)

# Prefixes of the documented integration/state surface (TFDi's Integration Guide
# 'Variables' page). These are NOT in the Aircraft::vars control table -- they are
# the read-only state and external-control exports, and they are exactly what the
# blind-pilot read-outs need (FCP window values, AP state, fuel, APU).
EXPORT_PREFIXES = (
    "MD11_AFS_",      # FCP selected SPD/HDG/ALT/VS windows
    "MD11_AP_",       # AP_STATE, IAS_MACH, HDG_TRK, VS_FPA, FT_M
    "MD11_ATS_",      # ATS_STATE, ATS_CLAMP (autothrottle)
    "MD11_APU_",      # APU N1/N2/STATE
    "MD11_EXTCTL_",   # writable external control (fuel, FCP, flap, spoiler, baro)
    "MD11_OVHD_TANK_",  # fuel tank quantities
    "MD11_YOKE_",     # normalized yoke position (added v1.1.18 for 3rd-party HW)
    "MD11_FLAPS_",    # FLAPS_MOVING
    "MD11_STBY_",     # standby instrument state
    "MD11_WBS_",      # weight and balance
    "MD11_CAP_",      # CAP_ALTIMETER, CAP_MINIMUMS
    "MD11_FO_",       # FO_ALTIMETER, FO_MINIMUMS
)

# Documented exports with no shared prefix to key on. The V-speeds are single
# tokens (MD11_V1, MD11_VR, ...) so a prefix rule would either miss them or, with
# a bare "MD11_V" prefix, drag in every unrelated MD11_VLV_*/MD11_VOR_* control.
# These are the take-off and retraction speeds a blind pilot cannot read off the
# PFD speed tape -- the DUs are WASM-rendered, so there is no other source.
EXPORT_EXACT = (
    "MD11_V1",        # take-off decision speed
    "MD11_VR",        # rotation speed
    "MD11_V2",        # take-off safety speed
    "MD11_VSR",       # slat retraction speed
    "MD11_VFR",       # flap retraction speed
)


def wasm_vars(wasm_path):
    """Pull the L:var surface out of md11host.wasm.

    The module embeds two distinct tables and they must not be conflated:

      * 'Aircraft::vars->MD11_...'  -- the ~1500 clickable-cockpit control vars
        that the ModelBehaviorDefs also reference.
      * bare 'MD11_...' strings     -- everything else, including the documented
        integration exports (MD11_AFS_SPD, MD11_AP_STATE, MD11_EXTCTL_*). None of
        these appear under Aircraft::vars, so a control-table-only scan misses the
        entire read-out surface.

    Returns (control_vars, export_vars).
    """
    if not wasm_path or not os.path.isfile(wasm_path):
        return set(), set()
    with open(wasm_path, "rb") as fh:
        data = fh.read()
    control = {m.decode() for m in re.findall(rb"Aircraft::vars->([A-Za-z0-9_]+)", data)}
    every = {m.decode() for m in re.findall(rb"\b(MD11_[A-Za-z0-9_]{2,60})\b", data)}
    export = {
        v
        for v in every - control
        if (v.startswith(EXPORT_PREFIXES) or v in EXPORT_EXACT)
        and not EVENT_NAME_SUFFIX.search(v)
    }
    return control, export


def _exit_no_wasm(package_dir):
    """One owner for the missing-wasm error: both the --pkg path and the
    discovery path reach it, and they must say the same thing."""
    sys.exit(
        "Found the MD-11 package but not %s inside it:\n  %s\n"
        "Searched every folder under SimObjects/. Pass --wasm to point at it "
        "directly." % (md11_paths.WASM_NAME, package_dir)
    )


def resolve_paths(pkg_arg, wasm_arg):
    """Settle the package + wasm paths, or exit with a message explaining why not.

    Every failure exits non-zero WITHOUT writing a map: a partial map is worse
    than none, because it silently drops the wasm-derived read-outs.
    """
    if pkg_arg:
        pkg = pkg_arg
        if not os.path.isdir(os.path.join(pkg, md11_paths.PACKAGE_MARKER)):
            sys.exit(
                "Not an MD-11 package: %s\n"
                "Expected it to contain %s"
                % (pkg, md11_paths.PACKAGE_MARKER)
            )
        wasm = wasm_arg or md11_paths.find_wasm(pkg)
        if not wasm:
            _exit_no_wasm(pkg)
        return pkg, wasm

    finds = md11_paths.discover()

    if not finds:
        roots = md11_paths.describe_roots()
        searched = ("\n".join("  " + r for r in roots)
                    if roots else "  (no MSFS package folders found at all)")
        sys.exit(
            "Could not find the TFDi MD-11 on this PC.\n"
            "Searched these package folders (and up to %d levels below each) "
            "for a folder containing %s:\n%s\n"
            "If it lives somewhere else, pass --pkg <folder>."
            % (md11_paths.MAX_DEPTH, md11_paths.PACKAGE_MARKER, searched)
        )

    if len(finds) > 1:
        print("The MD-11 is installed in more than one place:")
        for i, f in enumerate(finds, 1):
            print("  %d) %s  %s" % (i, f.sim_label, f.package_dir))
        if not sys.stdin.isatty():
            sys.exit(
                "Re-run with --pkg <folder> to choose one "
                "(no terminal attached, so cannot prompt)."
            )
        chosen = None
        while chosen is None:
            try:
                answer = input("Which one? [1-%d] " % len(finds))
            except (EOFError, KeyboardInterrupt):
                sys.exit("\nCancelled.")
            chosen = md11_paths.parse_choice(answer, len(finds))
            if chosen is None:
                print("Enter a number from 1 to %d." % len(finds))
        find = finds[chosen]
    else:
        find = finds[0]

    print("Using %s: %s" % (find.sim_label, find.package_dir))

    wasm = wasm_arg or find.wasm_path
    if not wasm:
        _exit_no_wasm(find.package_dir)
    return find.package_dir, wasm


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pkg", default=None,
                    help="MD-11 package folder. Omit to search this PC.")
    ap.add_argument("--wasm", default=None)
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "md11_control_map.json"))
    args = ap.parse_args()

    pkg, wasm = resolve_paths(args.pkg, args.wasm)

    controls, stats = collect(pkg)
    all_vars, export_vars = wasm_vars(wasm)

    referenced = {c["node_id"] for c in controls} | {
        c["state_var"] for c in controls if c["state_var"]
    }
    orphan_vars = sorted(v for v in all_vars if v not in referenced)

    by_area = defaultdict(list)
    for c in controls:
        by_area[c["area"]].append(c)

    out = {
        "_generated_by": "tools/md11-gen/generate_md11_map.py",
        "_source": "TFDi MD-11 ModelBehaviorDefs + md11host.wasm",
        "counts": {
            "controls": len(controls),
            "wasm_control_vars": len(all_vars),
            "export_vars": len(export_vars),
            "state_only_vars": len(orphan_vars),
            "by_kind": {k.split(":", 1)[1]: v for k, v in stats.items() if k.startswith("kind:")},
            "by_area": {a: len(v) for a, v in sorted(by_area.items())},
        },
        "controls": sorted(controls, key=lambda c: (c["area"], c["node_id"])),
        # The documented read-out / external-control surface (not clickable controls).
        "export_vars": sorted(export_vars),
        # Control-table vars no ModelBehaviorDefs control references -- animation
        # ranges (_RNG), exterior states, and the FCP push/pull latch vars.
        "state_only_vars": orphan_vars,
    }

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(out, fh, indent=1, ensure_ascii=False)

    print(f"wrote {args.out}")
    print(f"  controls        : {len(controls)}")
    print(f"  wasm ctrl vars  : {len(all_vars)}")
    print(f"  export vars     : {len(export_vars)}")
    print(f"  state-only vars : {len(orphan_vars)}")
    print("  by kind         :")
    for k, v in sorted(stats.items()):
        if k.startswith("kind:"):
            print(f"    {k[5:]:10s} {v}")
    tip = sum(1 for c in controls if c["label_source"] == "tooltip")
    derived = sum(1 for c in controls if c["label_source"] == "derived")
    mapped = sum(1 for c in controls if c["value_map"])
    print(f"  label from TFDi : {tip}/{len(controls)}")
    print(f"  label derived   : {derived}/{len(controls)}")
    print(f"  with value map  : {mapped}/{len(controls)}")
    skipped = {k[len("skipped_template:"):]: v for k, v in stats.items() if k.startswith("skipped_template:")}
    if skipped:
        print("  skipped templates (not controls):")
        for k, v in sorted(skipped.items(), key=lambda kv: -kv[1])[:10]:
            print(f"    {v:5d}  {k}")


if __name__ == "__main__":
    main()
