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
import html
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
# its wording between variants; that is not the control's position, and its words are never a
# position either (parse_tooltip lifts nothing from an expression that reads one). A LABEL that
# reads one has a wording per variant that nothing here can choose between, so parse_tooltip
# refuses to generate until LABEL_FIXES names the control. Keep this list tiny and
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
    "GSL": "Glareshield (Captain)",
    "GSR": "Glareshield (First Officer)",
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

# Raw 3D node names carry no MD11_<AREA> token. Place them by hand.
AREA_FIXES = {
    **{n: "Doors and Exterior" for n in (
        "1L_DN", "1L_UP", "1R_DN", "1R_UP", "2L_DN", "2L_UP", "2R_DN", "2R_UP",
        "Object7524", "Object7525", "Object7526", "Object7527", "Object7530", "Object7531",
        "Object7536", "Object7537", "Cylinder11762_03", "Cylinder11813", "Cylinder11904",
        "Cylinder12038", "Cylinder12057_08", "Cylinder12058", "Cylinder12061", "Cylinder12064")},
    "GA_BT_ALT": "Throttle Quadrant",
    "knob_kohlsman": "Main Instrument Panel",
    "l_window_shade_pull": "Captain Side Panel",
    # Named "Mirror_l_..." after the 3D modelling mirror it was made with, NOT after the left
    # side of the cockpit: it is declared in FlightDeck/FOAux_Light.xml with
    # ANIM_NAME MD11_RSIDE_WINDOW_SHADE and event 95518, next to MD11_RSIDE_WINDOW's 95517,
    # while the Captain's shade (94238) sits beside MD11_LSIDE_WINDOW in CaptainAux_Light.xml.
    "Mirror_l_window_shade_pull": "F/O Side Panel",
    "MANF_DRAIN_LT": "Overhead",
    "MD11_OVHD_1_PAX_LOAD_SW": "Aircraft Options",
    "MD11_OVHD_10_PAX_LOAD_SW": "Aircraft Options",
    "MD11_OVHD_100_PAX_LOAD_SW": "Aircraft Options",
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
def temperature_positions(count):
    """Value map for an n-position temperature selector: raw 0..n-1 → "1 (full cold)" … "n (full hot)".

    TFDi's Systems Guide (Overhead → AIR panel) describes the selectors' ends as full COLD ("all
    3 packs driven to full cold") and HOT ("trim air is added"), 65–85 °F with 75 °F at centre,
    and the tooltips carry no %{case} map, so the positions have no names of TFDi's to lift.
    Numbered positions are the honest read-out; the end words are the guide's. Left click lowered
    the raw value on a live aircraft (2026-09-06), which matches the cold→hot arc.
    """
    out = {}
    for raw in range(count):
        pos = str(raw + 1)
        if raw == 0:
            pos += " (full cold)"
        elif raw == count - 1:
            pos += " (full hot)"
        out[str(raw)] = pos
    return out


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
    # AIR panel temperature selectors. NUM_STATES from Overhead.xml: 8 for the cockpit and the
    # three cabin zones (raw 0–7), 3 for the forward lower cargo, 7 for the aft lower cargo. Two
    # of them (FWD_CAB, MID_CAB) choose their WORDING with
    # %((L:MD11_EFB_IS_CARGO))%{if}Courier Cabin%{else}Forward Cabin%{end}; the inline if/else
    # once leaked those two words into value_map, so an 8-position knob rendered as a two-item
    # combo. parse_tooltip no longer lifts a variant flag's words (LABEL_ONLY_VARS) and their
    # names are LABEL_FIXES entries; finalize_controls still lets a curated value_map win.
    "MD11_OVHD_PNEU_COCKPIT_TEMP": {"value_map": temperature_positions(8)},
    "MD11_OVHD_PNEU_FWD_CAB_TEMP": {"value_map": temperature_positions(8)},
    "MD11_OVHD_PNEU_MID_CAB_TEMP": {"value_map": temperature_positions(8)},
    "MD11_OVHD_PNEU_AFT_CAB_TEMP": {"value_map": temperature_positions(8)},
    "MD11_OVHD_PNEU_FWD_CARGO_TEMP": {"value_map": temperature_positions(3)},
    "MD11_OVHD_PNEU_AFT_CARGO_TEMP": {"value_map": temperature_positions(7)},
}

# Controls whose exported tooltip is missing or garbage. TFDi's wording where it exists,
# otherwise the cockpit's own placard wording. Discriminator first ("Left Rain Repellent").
LABEL_FIXES = {
    "MD11_OVHD_AICE_AUTO_BT": "Anti-Ice Auto",
    "MD11_OVHD_L_RAIN_REPLNT_BT": "Left Rain Repellent",
    "MD11_OVHD_R_RAIN_REPLNT_BT": "Right Rain Repellent",
    "MD11_OVHD_PNEU_OUTFLOW_VALVE_POS_SW": "Outflow Valve Position",
    # Cabin-temperature knobs whose tooltip names the zone per AIRFRAME variant
    # (MD11_EFB_IS_CARGO: "Forward Cabin" on the passenger jet, "Courier Cabin" on the MD-11F;
    # "Middle Cabin" / "Main Cargo Deck"). One name for both variants (Robin, 2026-09-11); the aft
    # knob's tooltip names no variant and keeps "Aft Cabin Temperature".
    "MD11_OVHD_PNEU_FWD_CAB_TEMP": "Forward Zone Temperature",
    "MD11_OVHD_PNEU_MID_CAB_TEMP": "Middle Zone Temperature",
    "MD11_OVHD_1_PAX_LOAD_SW": "Passenger Load Units",
    "MD11_OVHD_10_PAX_LOAD_SW": "Passenger Load Tens",
    "MD11_OVHD_100_PAX_LOAD_SW": "Passenger Load Hundreds",
    "MD11_AOVHD_EVAC_GRD": "EVAC guard",
    "MD11_AOVHD_GPWS_GRD": "GPWS guard",
    "MD11_CTR_FLTNO1_SW": "Flight Number Digit 1",
    "MD11_CTR_FLTNO2_SW": "Flight Number Digit 2",
    "MD11_CTR_FLTNO3_SW": "Flight Number Digit 3",
    "MD11_CTR_FLTNO4_SW": "Flight Number Digit 4",
    # The standby altimeter's STD: the node the ISFD baro knob's push animates (TFDi gives the node
    # itself no events). Named for what it sets, like the Captain/First Officer Altimeter STD rows.
    "MD11_MIP_ISFD_STD_BT": "Standby Altimeter STD",
    "MD11_PED_XPNDR_CLR_BT": "Transponder Clear",
    "MD11_CABIN_OXY_MASKS_DOOR": "Cabin Oxygen Masks Door",
    # ONE toggle: MD11_EFB_TOGGLE and MD11_EFB_TOGGLE_FO both fire event 94465, so the
    # "(Captain)" / "(First Officer)" labels this used to carry asserted a distinction the
    # aircraft does not make. Only the first survives _second_clickspots.
    "MD11_EFB_TOGGLE": "EFB Toggle",
    "MD11_FLIGHTDECK_DOOR": "Flight Deck Door",
    "l_window_shade_pull": "Left Window Shade",
    "Mirror_l_window_shade_pull": "Right Window Shade",
    "MD11_LYOKE_TRIM_SW001": "First Officer Elevator Trim Switch",
    "MD11_THR_L_ATS_BT": "Left Autothrust Disconnect",
    "MD11_THR_R_ATS_BT": "Right Autothrust Disconnect",
    # TFDi's tooltip says "APU Fire Test", but their Systems Guide names the button ENG/APU FIRE
    # TEST and says it lights "ENG 1,2,3 and APU FIRE alerts" — and holding it on a live aircraft
    # (2026-09-06) lit all three engine FIRE lights, the APU FIRE light and the master warning.
    # It is the aircraft's ONE fire-detection test; the tooltip's "APU" would tell a pilot the
    # engine loops cannot be tested.
    "MD11_AOVHD_FIRETEST_BT": "Engine and APU Fire Test",
    # Same lever as Cylinder11904 / Cylinder11813 (same event, same L:var), which is why only
    # one of each pair survives; the "(cabin lever)" qualifier named a second row that no
    # longer exists, and it read oddly beside the plain "Door 2L Slides" of every other door.
    "MD11_EXT_DOOR_PAX_1L_ARMED_LVR_OBJ": "Door 1L Slides",
    "MD11_EXT_DOOR_PAX_1R_ARMED_LVR_OBJ": "Door 1R Slides",
}

# MCDU keys: the derived 'Lsk 1l button' / 'Dir intc button' forms, spelled as the keycap.
MCDU_KEY_LABELS = {
    "DIR_INTC": "DIR INTC", "FPLN": "F-PLN", "SEC_FPLN": "SEC F-PLN", "NAV_RAD": "NAV RAD",
    "NEXTPAGE": "NEXT PAGE", "TOAPPR": "TO/APPR", "ENG_OUT": "ENG OUT", "CLR": "CLR",
    "INIT": "INIT", "REF": "REF", "PERF": "PERF", "PROG": "PROG", "MENU": "MENU", "FIX": "FIX",
    "UP": "Up", "DOWN": "Down", "SP": "Space", "DOT": "Dot",
    "MINUS": "Minus", "PLUS": "Plus", "SLASH": "Slash",
    # NOTE: MD11_xMCDU_L_BT / R_BT are the LETTERS L and R (the humanizer's ABBREV table turned
    # them into "Left"/"Right"); they fall through to the key token itself.
}
MCDU_SIDES = {"LMCDU": "Left", "CMCDU": "Center", "RMCDU": "Right"}

# ---------------------------------------------------------------------------
# Control STATE. A blind pilot cannot see a legend light, so each button's state is composed
# by MSFSBA from (1) the legend lamps that belong to it, (2) its own L:var where TFDi's own
# tooltip reads state from it (a proven latch), (3) what "all legends dark" means. Everything
# below is TFDi wording from the Systems Guide; live evidence is in docs/md11.md.
# ---------------------------------------------------------------------------

# Legend token (the text printed on the light) -> plain state word spoken when it is lit.
LEGEND_MEANINGS = {
    "OFF": "Off", "ON": "On", "ARM": "Armed", "AVAIL": "Available", "FAULT": "Fault", "FAIL": "Fail",
    "DISAG": "Disagree", "LOW": "Low", "PRESS": "Low pressure", "FLOW": "Flow", "MANF": "Manifold hot",
    "TEMP_HI": "Temperature high", "SEL": "Select", "MAN": "Manual", "OVRD": "Override",
    "DISC": "Disconnected", "DISCONNECT": "Disconnected", "ALTN": "Alternate", "FILL": "Filling",
    "TRANS": "Transfer", "RESET": "Reset", "SMOKE": "Smoke", "HEAT": "Heat", "TEST": "Test",
    "DISARM": "Disarmed", "GREEN": "Down and locked", "RED": "Unsafe", "CALL": "Call", "MIC": "Selected",
    "VOL": "On", "IDENT": "Ident", "MSG": "Message", "DSPY": "Display", "OFST": "Offset", "AUTO": "Auto",
    "HIGH": "High", "NORM": "Normal", "USE_ENG_AIR": "Use engine air", "CAB_ALT": "Cabin altitude",
    "AVIONICS_OVHT": "Avionics overheat", "CLOSED": "Closed", "OPEN": "Open", "TEL": "Telephone",
    "TELL": "Telephone", "MECH": "Mech call", "INHIBIT": "Inhibited", "LOCK": "Locked",
    "UNLOCK": "Unlocked", "PWR": "Powered", "CLSD_READY": "Closed and ready", "DOOR": "Door",
    "FUEL": "Fuel low", "GEN": "Generator", "STOP": "Stop", "BLANK": "",
}

# Lamps named as a bare "<stem>_LT" whose legend is NOT "ON" (the default for a bare lamp), and
# lamps whose legend token differs from what is printed. From the Systems Guide.
LAMP_LEGEND_OVERRIDES = {
    "MD11_OVHD_LTS_NAV_LT": "OFF", "MD11_OVHD_LTS_BCN_LT": "OFF", "MD11_OVHD_LTS_HI_INT_LT": "OFF",
    "MD11_OVHD_WNDSHLD_AICE_DEFOG_LT": "OFF", "MD11_OVHD_PNEU_BLEED_1_OFF_LT": "OFF",
    "MD11_OVHD_PNEU_BLEED_2_OFF_LT": "OFF", "MD11_OVHD_PNEU_BLEED_3_OFF_LT": "OFF",
    "MD11_AOVHD_APU_GEN_LT": "OFF", "MD11_CTR_ANTISKID_LT": "OFF",
    "MD11_OVHD_ELEC_GALLEY_BUS_1_LT": "OFF", "MD11_OVHD_ELEC_GALLEY_BUS_2_LT": "OFF", "MD11_OVHD_ELEC_GALLEY_BUS_3_LT": "OFF",
    "MD11_OVHD_FUEL_TANK_1_TRANS_LT": "ON", "MD11_OVHD_FUEL_TANK_2_TRANS_LT": "ON", "MD11_OVHD_FUEL_TANK_3_TRANS_LT": "ON",
    "MD11_OVHD_FUEL_SYSTEM_SEL_LT": "SEL", "MD11_OVHD_PNEU_SYSTEM_SEL_LT": "SEL",
    "MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_LT": "SEL", "MANF_DRAIN_LT": "OPEN", "MD11_OVHD_FUEL_DUMP_LT": "OPEN",
    "MD11_OVHD_FUEL_DUMP_STOP_LT": "STOP", "MD11_OVHD_HYD_TEST_LT": "TEST", "MD11_MIP_CTR_GEAR_LT": "UP",
    "MD11_CTR_SLAT_STOW_LT": "STOW", "MD11_GSL_MST_WRN_LT": "WARN", "MD11_GSR_MST_WRN_LT": "WARN",
    "MD11_GSL_MST_CAUT_LT": "CAUT", "MD11_GSR_MST_CAUT_LT": "CAUT", "MD11_OVHD_ENG_A_LT": "A",
    "MD11_OVHD_ENG_B_LT": "B", "MD11_OVHD_LTS_MECH_LT": "CALL", "MD11_OVHD_LTS_MECH_CALL_ON_LT": "CALL",
    "MD11_OVHD_LTS_FWD_ATTND_LT": "CALL", "MD11_OVHD_LTS_MID_ATTND_LT": "CALL", "MD11_OVHD_LTS_AFT_ATTND_LT": "CALL",
    "MD11_OVHD_LTS_OVW_ATTND_LT": "CALL", "MD11_OVHD_LTS_CREW_REST_LT": "CALL", "MD11_OVHD_GEN_BUS_1_RESET_LT": "FAULT",
    "MD11_OVHD_GEN_BUS_2_RESET_LT": "FAULT", "MD11_OVHD_GEN_BUS_3_RESET_LT": "FAULT",
    **{f"MD11_AOVHD_CRGSMK_{p}_AGNT{n}_LT": "FIRE" for p in ("FWD", "AFT") for n in (1, 2)},
    **{f"MD11_AOVHD_CRGSMK_{p}_AGNT{n}LO_LT": "LOW" for p in ("FWD", "AFT") for n in (1, 2)},
    **{f"MD11_PED_SD_{p}_LT": "ALERT" for p in ("AIR", "CONFIG", "ELEC", "ENG", "FUEL", "HYD", "MISC")},
    **{f"MD11_PED_{s}_RADIO_PNL_{r}_LT": "SEL" for s in ("CPT", "FO", "OBS") for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2")},
    # Rows attached to knobs/switches: the legend the guide prints on that light.
    "MD11_OVHD_FLTCTL_ELEVFEEL_LT": "MANUAL", "MD11_OVHD_FLTCTL_FLAPLIM_LT": "MANUAL",
    "MD11_OVHD_IRS_1_LT": "NAV_OFF", "MD11_OVHD_IRS_2_LT": "NAV_OFF", "MD11_OVHD_IRS_3_LT": "NAV_OFF",
    "MD11_THR_L_FUEL_LT": "FIRE", "MD11_THR_C_FUEL_LT": "FIRE", "MD11_THR_R_FUEL_LT": "FIRE",
    # Audio panel MIC / IDENT buttons light "MIC" / "IDENT", not "ON".
    **{f"{p}_{r}_MIC_LT": "MIC" for p in ("MD11_PED_CPT_AUDIO_PNL", "MD11_PED_FO_AUDIO_PNL", "MD11_OBS_AUDIO_PNL")
       for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2", "SAT", "INT", "CAB")},
    **{f"{p}_IDENT_LT": "IDENT" for p in ("MD11_PED_CPT_AUDIO_PNL", "MD11_PED_FO_AUDIO_PNL", "MD11_OBS_AUDIO_PNL")},
}
# Spoken word for the overriding legends above that LEGEND_MEANINGS does not carry.
LEGEND_MEANINGS.update({"UP": "Up", "STOW": "Stowed", "WARN": "Warning", "CAUT": "Caution", "A": "Selected",
                        "B": "Selected", "FIRE": "Fire", "ALERT": "Alert", "MANUAL": "Manual", "NAV_OFF": "NAV OFF"})

# Spoken word when lit, where the legend's generic word reads wrong for this lamp.
LAMP_LIT_OVERRIDES = {
    **{f"MD11_PED_{s}_RADIO_PNL_{r}_LT": "Selected" for s in ("CPT", "FO", "OBS") for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2")},
}

# Curated pairings where TFDi's lamp name does not start with the button's stem.
STATE_LAMPS = {
    **{f"MD11_OVHD_ELEC_AC_TIE{n}_BT": [(f"MD11_OVHD_ELEC_AC{n}_TIE_ARM_LT", "ARM"), (f"MD11_OVHD_ELEC_AC{n}_TIE_OFF_LT", "OFF")] for n in (1, 2, 3)},
    "MD11_OVHD_ELEC_DC_TIE1_BT": [("MD11_OVHD_ELEC_DC1_TIE_OFF_LT", "OFF")],
    "MD11_OVHD_ELEC_DC_TIE3_BT": [("MD11_OVHD_ELEC_DC3_TIE_OFF_LT", "OFF")],
    "MD11_OVHD_ELEC_CAB_BUS_BT": [("MD11_OVHD_ELEC_CABIN_BUS_OFF_LT", "OFF")],
    "MD11_OVHD_ELEC_SYSTEM_SEL_BT": [("MD11_OVHD_ELEC_SYS_SEL_LT", "SEL"), ("MD11_OVHD_ELEC_SYS_MANUAL_LT", "MAN")],
    **{f"MD11_OVHD_GALLEY_BUS_{n}_BT": [(f"MD11_OVHD_ELEC_GALLEY_BUS_{n}_LT", "OFF")] for n in (1, 2, 3)},
    "MD11_OVHD_HYD_HYD_TEST_BT": [("MD11_OVHD_HYD_TEST_LT", "TEST")],
    "MD11_OVHD_HYD_SYSTEM_SEL_BT": [("MD11_OVHD_HYD_SYS_SEL_LT", "SEL"), ("MD11_OVHD_HYD_SYS_MANUAL_LT", "MAN")],
    "MD11_OVHD_FUEL_SYSTEM_SEL_BT": [("MD11_OVHD_FUEL_SYSTEM_MAN_LT", "MAN")],
    "MD11_OVHD_PNEU_SYSTEM_SEL_BT": [("MD11_OVHD_PNEU_SYSTEM_MAN_LT", "MAN")],
    "MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_BT": [("MD11_OVHD_PNEU_CABIN_SYSTEM_MAN_LT", "MAN")],
    "MD11_OVHD_AICE_SYSTEM_SEL_BT": [("MD11_OVHD_AICE_SYSTEM_MAN_LT", "MAN")],
    **{f"MD11_OVHD_FUEL_PUMP_TANK_{n}_BT": [(f"MD11_OVHD_FUEL_TANK_{n}_PUMP_OFF_LT", "OFF"), (f"MD11_OVHD_FUEL_TANK_{n}_PUMP_LOW_LT", "LOW")] for n in (1, 2, 3)},
    **{f"MD11_OVHD_FUEL_TRANS_TANK_{n}_BT": [(f"MD11_OVHD_FUEL_TANK_{n}_TRANS_LT", "ON"), (f"MD11_OVHD_FUEL_TANK_{n}_TRANS_LOW_LT", "LOW")] for n in (1, 2, 3)},
    **{f"MD11_OVHD_FUEL_XFEED_TANK_{n}_BT": [(f"MD11_OVHD_FUEL_TANK_{n}_XFEED_ON_LT", "ON"), (f"MD11_OVHD_FUEL_TANK_{n}_XFEED_DISAG_LT", "DISAG")] for n in (1, 2, 3)},
    **{f"MD11_OVHD_FUEL_FILL_TANK_{n}_BT": [(f"MD11_OVHD_FUEL_TANK_{n}_FILL_ARM_LT", "ARM"), (f"MD11_OVHD_FUEL_TANK_{n}_FILL_FILL_LT", "FILL")] for n in (1, 2, 3)},
    "MD11_OVHD_FUEL_FWDAUX_L_TRANS_BT": [("MD11_OVHD_FUEL_FWDAUX_LTRANS_ON_LT", "ON"), ("MD11_OVHD_FUEL_FWDAUX_LTRANS_LOW_LT", "LOW")],
    "MD11_OVHD_FUEL_FWDAUX_R_TRANS_BT": [("MD11_OVHD_FUEL_FWDAUX_RTRANS_ON_LT", "ON"), ("MD11_OVHD_FUEL_FWDAUX_RTRANS_LOW_LT", "LOW")],
    "MD11_OVHD_FUEL_MANF_DRAIN_BT": [("MANF_DRAIN_LT", "OPEN")],
    # MD11_OVHD_FUEL_DUMP_STOP_LT's stem ("MD11_OVHD_FUEL_DUMP_STOP") has
    # "MD11_OVHD_FUEL_DUMP" (MD11_OVHD_FUEL_DUMP_BT's own stem) as a PREFIX, so the lamp also
    # matches that shorter button's stem rule as a "<stem>_STOP_LT" legend match (STOP is a
    # LEGEND_MEANINGS key). Per TFDi's Systems Guide (Forward Overhead: "FUEL DUMP EMER STOP
    # Switch (amber): legend amber when illuminated -- dump valves commanded closed"), the lamp
    # belongs to the STOP button, not the DUMP button -- curate it explicitly so this is never
    # left to iteration order (see pair_lamps).
    "MD11_OVHD_FUEL_DUMP_STOP_BT": [("MD11_OVHD_FUEL_DUMP_STOP_LT", "STOP")],
    **{f"MD11_OVHD_FLTCTL_{c}_BT": [(f"MD11_OVHD_FLTCTL_{c}FAIL_LT", "FAIL"), (f"MD11_OVHD_FLTCTL_{c}FOFF_LT", "OFF")] for c in ("LLI", "LLO", "RLI", "RLO")},
    **{f"MD11_OVHD_FLTCTL_{c}_BT": [(f"MD11_OVHD_FLTCTL_{c}FAIL_LT", "FAIL"), (f"MD11_OVHD_FLTCTL_{c}OFF_LT", "OFF")] for c in ("LYDA", "LYDB", "UYDA", "UYDB")},
    **{f"MD11_OVHD_PNEU_BLEED_{n}_OFF_BT": [(f"MD11_OVHD_PNEU_BLEED_{n}_PRESS_LT", "PRESS")] for n in (1, 2, 3)},
    **{f"MD11_OVHD_PNEU_BLEED_{n}_MANF_TEMP_HI_BT": [(f"MD11_OVHD_PNEU_BLEED_{n}_MANF_LT", "MANF"), (f"MD11_OVHD_PNEU_BLEED_{n}_TEMP_HI_LT", "TEMP_HI")] for n in (1, 2, 3)},
    "MD11_OVHD_PNEU_1_2_ISOL_BT": [("MD11_OVHD_PNEU_ISOL_1_2_ON_LT", "ON"), ("MD11_OVHD_PNEU_ISOL_1_2_DISAG_LT", "DISAG")],
    "MD11_OVHD_PNEU_1_3_ISOL_BT": [("MD11_OVHD_PNEU_ISOL_1_3_ON_LT", "ON"), ("MD11_OVHD_PNEU_ISOL_1_3_DISAG_LT", "DISAG")],
    "MD11_OVHD_PNEU_APU_BLEED_BT": [("MD11_OVHD_PNEU_APU_ON_LT", "ON"), ("MD11_OVHD_PNEU_APU_USE_ENG_AIR_LT", "USE_ENG_AIR")],
    "MD11_OVHD_LTS_MECH_BT": [("MD11_OVHD_LTS_MECH_CALL_ON_LT", "CALL")],
    "MD11_OVHD_LTS_DOME_BT": [("MD11_LTS_DOME", "ON")],
    "MD11_AOVHD_APU_START_BT": [("MD11_AOVHD_APU_ON_LT", "ON"), ("MD11_AOVHD_APU_OFF_LT", "OFF")],
    **{f"MD11_AOVHD_CRGSMK_{p}_AGNT{n}_BT": [(f"MD11_AOVHD_CRGSMK_{p}_AGNT{n}LO_LT", "LOW")] for p in ("FWD", "AFT") for n in (1, 2)},
}

# What "every legend dark" means, where the legend-set rule (OFF→On; ON/AVAIL/ARM→Off;
# fault-class only→Normal) gives the wrong answer.
DARK_OVERRIDES = {
    "MD11_OVHD_ELEC_EXT_PWR_BT": "Not available", "MD11_OVHD_ELEC_APU_PWR_BT": "Not available",
    "MD11_OVHD_ELEC_GLY_EXT_PWR_BT": "Not available",
    **{f"MD11_OVHD_ELEC_AC_TIE{n}_BT": "Closed" for n in (1, 2, 3)},
    **{f"MD11_OVHD_ELEC_DC_TIE{n}_BT": "Closed" for n in (1, 3)},
    "MD11_OVHD_ELEC_CAB_BUS_BT": "Powered",
    **{f"MD11_OVHD_GALLEY_BUS_{n}_BT": "Powered" for n in (1, 2, 3)},
    "MD11_AOVHD_APU_START_BT": "Off",
    **{f"MD11_PED_SD_{p}_BT": "No alert" for p in ("AIR", "CONFIG", "ELEC", "ENG", "FUEL", "HYD", "MISC")},
    **{f"MD11_PED_{s}_RADIO_PNL_{r}_BT": "Not selected" for s in ("CPT", "FO", "OBS") for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2")},
}

# Buttons proven live to latch their position in their own L:var although their tooltip does
# not read it (battery: 0→1 on press, stays 1, 2026-09-05). Tooltip-read buttons need no entry.
LATCH_FIXED = {"MD11_OVHD_ELEC_BATT_BT": ("On", "Off")}

# Lamps that belong to no button: spoken name, lit state, dark state.
STANDALONE_LAMPS = {
    **{f"MD11_OVHD_ELEC_AC{n}_OFF_LT": (f"AC Bus {n}", "Off", "Powered") for n in (1, 2, 3)},
    **{f"MD11_OVHD_ELEC_DC{n}_BUS_OFF_LT": (f"DC Bus {n}", "Off", "Powered") for n in (1, 2, 3)},
    "MD11_OVHD_ELEC_BATT_BUS_OFF_LT": ("Battery Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_L_EMER_AC_OFF_LT": ("Left Emergency AC Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_R_EMER_AC_OFF_LT": ("Right Emergency AC Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_L_EMER_DC_OFF_LT": ("Left Emergency DC Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_R_EMER_DC_OFF_LT": ("Right Emergency DC Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_AC_GND_SVC_OFF_LT": ("AC Ground Service Bus", "Off", "Powered"),
    "MD11_OVHD_ELEC_DC_GND_SVC_OFF_LT": ("DC Ground Service Bus", "Off", "Powered"),
    **{f"MD11_OVHD_HYD_SYS_{n}_PRESS_LT": (f"Hydraulic System {n} Pressure", "Abnormal", "Normal") for n in (1, 2, 3)},
    "MD11_OVHD_PNEU_OUTFLOW_CLOSED_LT": ("Outflow Valve", "Closed", "Not closed"),
    "MD11_OVHD_PNEU_NO_MASKS_LT": ("No Masks light", "On", "Off"),
    "MD11_OVHD_ENG_IGN_OFF_LT": ("Engine Ignition", "Off", "Selected"),
    "MD11_OVHD_LOCK_AUTO_LT": ("Cockpit Door Lock AUTO light", "On", "Off"),
    "MD11_OVHD_LOCK_FAIL_LT": ("Cockpit Door Lock FAIL light", "On", "Off"),
    "MD11_OVHD_LTS_PAINUSE_LT": ("PA in use", "Yes", "No"),
    "MD11_OVHD_LTS_MOVIE_LT": ("Movie light", "On", "Off"),
    "MD11_AOVHD_APU_FUEL_LT": ("APU FUEL light", "On", "Off"),
    "MD11_AOVHD_APU_DOOR_LT": ("APU DOOR light", "On", "Off"),
    "MD11_AOVHD_APU_FAIL_LT": ("APU FAIL light", "On", "Off"),
    "MD11_AOVHD_APU_BLANK_LT": ("APU blank light", "", ""),
    "MD11_AOVHD_APUFIRE_LT": ("APU Fire", "Fire", "Normal"),
    **{f"MD11_AOVHD_ENG{n}FIRE_LT": (f"Engine {n} Fire", "Fire", "Normal") for n in (1, 2, 3)},
    **{f"MD11_AOVHD_ENG{n}AGENT{b}LO_LT": (f"Engine {n} Agent {b} LOW light", "On", "Off") for n in (1, 2, 3) for b in (1, 2)},
    **{f"MD11_AOVHD_CRGSMK_{p}_HEAT_LT": (f"{'Forward' if p == 'FWD' else 'Aft'} Cargo HEAT light", "On", "Off") for p in ("FWD", "AFT")},
    **{f"MD11_AOVHD_CRGSMK_{p}_SMOKE_LT": (f"{'Forward' if p == 'FWD' else 'Aft'} Cargo SMOKE light", "On", "Off") for p in ("FWD", "AFT")},
    **{f"MD11_AOVHD_CRGSMK_{p}_VENTDISAG_LT": (f"{'Forward' if p == 'FWD' else 'Aft'} Cargo Ventilation DISAG light", "On", "Off") for p in ("FWD", "AFT")},
    **{f"MD11_AOVHD_CRGSMK_{p}_VENTOFF_LT": (f"{'Forward' if p == 'FWD' else 'Aft'} Cargo Ventilation OFF light", "On", "Off") for p in ("FWD", "AFT")},
    "MD11_AOVHD_EMER_LT": ("Aft overhead EMER light", "On", "Off"),
    **{f"MD11_MIP_{g}_GREEN_LT": (f"{n} Gear GREEN light", "On", "Off") for g, n in (("NOSE", "Nose"), ("LEFT", "Left"), ("RIGHT", "Right"), ("CTR", "Center"))},
    **{f"MD11_MIP_{g}_RED_LT": (f"{n} Gear RED light", "On", "Off") for g, n in (("NOSE", "Nose"), ("LEFT", "Left"), ("RIGHT", "Right"), ("CTR", "Center"))},
    **{f"MD11_{s}_ABS_DISARM_LT": (f"{n} Autobrake DISARM light", "On", "Off") for s, n in (("GSL", "Captain"), ("GSR", "First Officer"))},
    **{f"MD11_{s}_BELOW_GS_LT": (f"{n} BELOW G/S light", "On", "Off") for s, n in (("GSL", "Captain"), ("GSR", "First Officer"))},
    **{f"MD11_{s}_ENG_FAIL_LT": (f"{n} ENG FAIL light", "On", "Off") for s, n in (("GSL", "Captain"), ("GSR", "First Officer"))},
    "MD11_PED_XPNDR_FAIL_LT": ("Transponder FAIL light", "On", "Off"),
    "MD11_PED_CKPTDOOR_AUTO_LT": ("Cockpit Door AUTO light", "On", "Off"),
    "MD11_PED_CKPTDOOR_FAIL_LT": ("Cockpit Door FAIL light", "On", "Off"),
    **{f"MD11_{m}MCDU_{l}_LT": (f"{n} MCDU {l} light", "On", "Off") for m, n in (("L", "Left"), ("C", "Center"), ("R", "Right")) for l in ("DSPY", "FAIL", "MSG", "OFST")},
    "MD11_CABIN_OXY_MASKS": ("Cabin Oxygen Masks", "Deployed", "Stowed"),
    "MD11_CABIN_POWER": ("Cabin Power", "On", "Off"),
    "MD11_LSIDE_OXY_FLOW_IND": ("Captain Oxygen Flow indicator", "Flow", "No flow"),
    "MD11_RSIDE_OXY_FLOW_IND": ("First Officer Oxygen Flow indicator", "Flow", "No flow"),
    "MD11_EXT_DOOR_CRG_MAIN_OPEN_LT": ("Main Cargo Door OPEN light", "On", "Off"),
    "MD11_EXT_DOOR_CRG_MAIN_CLSD_READY_LT": ("Main Cargo Door CLOSED READY light", "On", "Off"),
    "MD11_EXT_DOOR_CRG_MAIN_LOCK_LT": ("Main Cargo Door LOCK light", "On", "Off"),
    "MD11_EXT_DOOR_CRG_MAIN_UNLOCK_LT": ("Main Cargo Door UNLOCK light", "On", "Off"),
    "MD11_EXT_DOOR_CRG_MAIN_PWR_LT": ("Main Cargo Door PWR light", "On", "Off"),
    **{f"MD11_EXT_DOOR_PAXC_{d}_DISARM_LT": (f"Door {d} DISARM light", "On", "Off") for d in ("1L", "1R")},
    **{f"MD11_LTS_MAP_{n}": (f"Map Light {n}", "On", "Off") for n in (1, 2, 3)},
    # Audio panel call lights (the MIC/VOL lights belong to their button/knob; these do not).
    **{f"{p}_{r}_CALL_LT": (f"{seat} {r} CALL light", "On", "Off")
       for p, seat in (("MD11_PED_CPT_AUDIO_PNL", "Captain"), ("MD11_PED_FO_AUDIO_PNL", "First Officer"), ("MD11_OBS_AUDIO_PNL", "Observer"))
       for r in ("VHF1", "VHF2", "VHF3", "HF1", "HF2", "CAB")},
    **{f"{p}_INT_MECH_LT": (f"{seat} MECH call light", "On", "Off")
       for p, seat in (("MD11_PED_CPT_AUDIO_PNL", "Captain"), ("MD11_PED_FO_AUDIO_PNL", "First Officer"), ("MD11_OBS_AUDIO_PNL", "Observer"))},
    "MD11_PED_CPT_AUDIO_PNL_SAT_TEL_LT": ("Captain SAT TEL light", "On", "Off"),
    "MD11_PED_FO_AUDIO_PNL_SAT_TELL_LT": ("First Officer SAT TEL light", "On", "Off"),
    "MD11_OBS_AUDIO_PNL_SAT_TEL_LT": ("Observer SAT TEL light", "On", "Off"),
}
# Side-panel source-select lights: "<seat> <source> Source CAP 2 light" etc.
for _side, _seat in (("LSIDE", "Captain"), ("RSIDE", "First Officer")):
    for _src, _word in (("APPR", "ILS"), ("CADC", "Air Data"), ("FLTDIR", "Flight Director"), ("FMS", "FMS"), ("VOR", "VOR")):
        STANDALONE_LAMPS[f"MD11_{_side}_INP_{_src}CAP2_LT"] = (f"{_seat} {_word} Source CAP 2 light", "On", "Off")
        STANDALONE_LAMPS[f"MD11_{_side}_INP_{_src}FO1_LT"] = (f"{_seat} {_word} Source FO 1 light", "On", "Off")
    for _tok, _tail in (("EIS_CAP2", "CAP 2"), ("EIS_CAPAUX", "CAP AUX"), ("EIS_FO1", "FO 1"), ("EIS_FOAUX", "FO AUX")):
        STANDALONE_LAMPS[f"MD11_{_side}_INP_{_tok}_LT"] = (f"{_seat} EIS Source {_tail} light", "On", "Off")
    for _tok, _tail in (("IRS_CAPTAUX", "CAPT AUX"), ("IRS_FOAUX", "FO AUX")):
        STANDALONE_LAMPS[f"MD11_{_side}_INP_{_tok}_LT"] = (f"{_seat} IRS Source {_tail} light", "On", "Off")


# Lamps whose ONLY <UseTemplate> block in TFDi's package is COMMENTED OUT: node id -> the source
# recorded for it. read_xml strips comments, so a commented-out block now defines nothing -- and
# these eight would have left the map with it. They are added back EXPLICITLY, by decision rather
# than by parser accident:
#   * all eight blocks sit inside one comment in FlightDeck/Lighting.xml and have no live
#     definition of any template anywhere in the package;
#   * all eight VARIABLES are live elsewhere in the package and in the wasm's Aircraft::vars
#     control table, which is what add_curated_lamps gates on;
#   * they already carry curated names in STANDALONE_LAMPS ("Nose Gear GREEN light", ...) and
#     Md11PanelLayout places them as the Landing Gear panel's Status Display rows. Gear position
#     is exactly the status a blind pilot wants, and they ship in the map today.
# UNVERIFIED: whether the aircraft actually DRIVES them. Nobody has yet watched a gear-down MD-11
# read "Nose Gear GREEN light: On" (the in-sim list in docs/md11.md carries the check). If it does
# not, these eight rows read "Off" forever and should be dropped here and from Md11PanelLayout
# together -- not left half-removed, which is how a listed key with no map entry becomes a
# `missing` row in Md11PanelLayout.Place.
CURATED_LAMPS = {
    f"MD11_MIP_{_g}_{_c}_LT": "FlightDeck/Lighting.xml (commented out)"
    for _g in ("NOSE", "LEFT", "CTR", "RIGHT") for _c in ("GREEN", "RED")
}


def add_curated_lamps(controls, all_vars, stats):
    """Add each CURATED_LAMPS entry the wasm carries a variable for. Pure; call before finalize_controls.

    Gated on the wasm's own control table, so this rule can only ever add a lamp the aircraft
    really has: a fixture package gets none, and a future TFDi build that drops one of the
    variables drops the row with it (which diff_maps then reports as a REMOVED node -- the STOP
    rule, working). A LIVE <UseTemplate> for one of these wins outright: it is already collected,
    and this skips it.
    """
    have = {c["node_id"] for c in controls}
    for node_id, source in sorted(CURATED_LAMPS.items()):
        if node_id in have:
            stats["curated_lamp_is_live"] += 1
            continue
        if node_id not in all_vars:
            stats["curated_lamp_absent_from_wasm"] += 1
            continue
        # Shaped exactly as collect() shapes an annunciator, so everything downstream --
        # finalize_controls, apply_state (which gives it its STANDALONE_LAMPS name and its state
        # block), the panel layout -- treats it identically to a parsed one.
        controls.append({
            "node_id": node_id,
            "kind": "annun",
            "template": "TFDi_Design_MD11_Annunciator",
            "area": area_of(node_id),
            "label": humanize(node_id),
            "label_source": "derived",
            "state_var": node_id,
            "value_map": {},
            "num_states": None,
            "events": {},
            "guard_id": None,
            "source": source,
        })
        stats["curated_lamp"] += 1
    return controls


def breaker_label(node_id, label):
    """'MD11_BKR_BWU_C24' + 'Tank 1 Transfer Pump Power Breaker' -> 'C24 Tank 1 Transfer Pump Power'.

    The grid position is how the real panel identifies a breaker and it is the only thing
    separating the two 'Tank 1 Transfer Pump Power' breakers (C24 and D24). The word
    'Breaker' is dropped because every row of the Circuit Breakers panel is one.
    """
    grid = node_id.rsplit("_", 1)[-1]
    text = re.sub(r"\s+Breaker$", "", label or "").strip() or grid
    return f"{grid} {text}"


def mcdu_key_label(node_id, label):
    """'MD11_LMCDU_LSK_1L_BT' -> 'LSK 1L'; 'MD11_LMCDU_A_BT' -> 'A'; brightness knob keeps its tooltip."""
    parts = node_id.split("_")
    if len(parts) < 3 or parts[1] not in MCDU_SIDES:
        return label
    key = "_".join(parts[2:-1]) if parts[-1] in ("BT", "KB") else "_".join(parts[2:])
    if parts[-1] == "KB":
        return label or f"{MCDU_SIDES[parts[1]]} MCDU Brightness"
    if key.startswith("LSK_"):
        return "LSK " + key[4:]
    return MCDU_KEY_LABELS.get(key, key)


def _strip_suffix(node_id):
    for suf in ("_BT001", "_BT_F", "_BT", "_GRD", "_SW", "_KB", "_LVR"):
        if node_id.endswith(suf):
            return node_id[: -len(suf)]
    return node_id


def _second_clickspots(controls):
    """Node ids that are a SECOND clickspot for a control kept elsewhere in the list.

    The test is the EVENTS map, not the name: two controls of the same kind whose events are
    byte-identical are one physical action reached from two 3D nodes, because an event id IS the
    action on this aircraft. That catches the '001' / '_F' twins ('MD11_OVHD_PNEU_ECON_BT001'),
    and also the pairs no naming rule would have found -- 'GA_BT_ALT' beside 'MD11_THR_GA_BT',
    'MD11_EFB_TOGGLE_FO' beside 'MD11_EFB_TOGGLE' (one toggle, 94465, from either seat), and the
    door-slide levers reachable at the door and from the cabin. Spec 3.8 wants one row per
    physical control, and a second row asserts a distinction the aircraft does not make.

    Which one survives is decided deterministically, never by file-traversal order: the
    MD11_-prefixed node id (the aircraft's own naming, over a raw 3D name), then the shortest,
    then the ordinally smallest. That keeps the base of a '001' / '_F' pair, exactly as the
    name-shaped rule this replaces did.
    """
    groups = defaultdict(list)
    for c in controls:
        if c["kind"] == "annun" or not c["events"]:
            continue
        groups[(c["kind"], tuple(sorted(c["events"].items())))].append(c["node_id"])

    drop = set()
    for nids in groups.values():
        if len(nids) < 2:
            continue
        keep = min(nids, key=lambda n: (not n.startswith("MD11_"), len(n), n))
        drop.update(n for n in nids if n != keep)
    return drop


def _second_lamps(controls):
    """Node ids that are a SECOND lamp node on an L:var another lamp node keeps.

    VIS_VAR duplicates: the '_LT001' / '_LT_F' twins, and the MD11_CPT_* / MD11_FO_* / MD11_OBS_*
    audio-panel nodes that light an MD11_PED_* var. Two lamps on one var would be two continuous
    batch entries with one Name, which shifts every later batch slot (the VarNameCollision
    invariant), so one survives -- decided by rule, never by file-traversal order: the node NAMED
    AFTER THE VAR (the aircraft's own name for that light), then _second_clickspots' rule. The
    var-named node is the one the old first-seen rule kept in all 37 of the shipped map's pairs
    (measured 2026-09-11); shortest-first alone would have renamed 34 of those lamps.
    """
    groups = defaultdict(list)
    for c in controls:
        if c["kind"] == "annun":
            groups[c["state_var"]].append(c["node_id"])

    drop = set()
    for var, nids in groups.items():
        if len(nids) < 2:
            continue
        keep = min(nids, key=lambda n: (n != var, not n.startswith("MD11_"), len(n), n))
        drop.update(n for n in nids if n != keep)
    return drop


def finalize_controls(controls):
    """Labels, duplicates, areas and option flags — pure, so it is testable on fixtures.

    Order matters: duplicates are collapsed FIRST (so a guard names the surviving button),
    then areas, then labels, and guard labels LAST (from the covered control's final label).
    """
    # 1. Collapse duplicates: a second clickspot of one control (same kind, same events — see
    #    _second_clickspots), and a second lamp node on the same L:var (see _second_lamps). Both
    #    survivors are chosen by rule, never by the order the package's files were read in.
    second_clickspots = _second_clickspots(controls)
    second_lamps = _second_lamps(controls)
    kept = [c for c in controls
            if c["node_id"] not in (second_lamps if c["kind"] == "annun" else second_clickspots)]

    for c in kept:
        nid = c["node_id"]
        # 2. Option flags are not lamps: they describe the installed configuration.
        if nid.startswith("MD11_OPT_"):
            c["kind"] = "option"
        # 3. Areas for raw 3D names and the misplaced options.
        if nid in AREA_FIXES:
            c["area"] = AREA_FIXES[nid]
        # 3b. A guard cover's position is its OWN L:var — TFDi animates the cover on its node id
        #     and the aircraft's state files carry it (Aircraft::vars->MD11_OVHD_FUEL_DUMP_GRD).
        #     Four covers' tooltips read the control UNDER them for their Open/Closed wording
        #     (Fuel Dump, Fuel Dump Emergency Stop, Center Gear Uplock, Main Cargo Door Arm), so
        #     parse_tooltip's "first L:var" rule handed the guard the covered button's var: the
        #     auto-open then read "button off" as "cover closed" and lowered an OPEN cover onto
        #     the press. latch_for already keys a guard on nid; the state var must agree, and a
        #     cover has no positions of its own (the other 28 guards ship an empty map).
        if c["kind"] == "guard":
            c["state_var"] = nid
            c["value_map"] = {}
        # 4. Labels. The guard test comes before the breaker and MCDU ones: a guard over a
        #    breaker or an MCDU key is still a guard (the breaker branch read the guard's own id as
        #    a grid position, "GRD ..."). Its label is composed in step 6, once the control it
        #    covers has its FINAL label -- composed here, it read that label as it arrived, before
        #    this loop repaired it (a stale "... button guard", or a name LABEL_FIXES replaces).
        if nid in LABEL_FIXES:
            c["label"], c["label_source"] = LABEL_FIXES[nid], "curated"
        elif c["kind"] == "guard":
            pass
        elif nid.startswith("MD11_BKR_"):
            c["label"] = breaker_label(nid, c["label"])
        elif nid.split("_")[1:2] and nid.split("_")[1] in MCDU_SIDES and c["kind"] != "annun":
            c["label"] = mcdu_key_label(nid, c["label"])
        elif c["label_source"] == "derived":
            # collect() already runs humanize() when there is no tooltip, so in the real
            # pipeline `c["label"]` is never actually None here -- but finalize_controls is a
            # pure function tested on fixtures that skip collect() entirely, so it must be
            # able to derive the fallback itself rather than assume a caller already did.
            text = c["label"] or humanize(nid)
            if text and text.lower().endswith(" button"):
                text = text[: -len(" button")]
            c["label"] = text
        # 5. Curated positions. collect() spreads CURATED into the control but then writes the
        #    PARSED value_map over it (later dict keys win), so a curated map has to be applied
        #    here, where it can also replace a wrong parsed one — see the temperature knobs.
        curated_map = (CURATED.get(nid) or {}).get("value_map")
        if curated_map:
            c["value_map"] = dict(curated_map)
        if c["label"]:
            c["label"] = speakable(c["label"])

    # 6. Guard labels, from the covered control's final label (see step 4).
    for c in kept:
        nid = c["node_id"]
        if c["kind"] != "guard" or nid in LABEL_FIXES:
            continue
        covered = next((o for o in kept if o.get("guard_id") == nid), None)
        if covered is not None:
            c["label"] = f"{covered['label'] or covered['node_id']} guard"
        elif c["label"] and c["label"].lower().endswith(" guard"):
            pass
        else:
            c["label"] = f"{c['label'] or humanize(nid)} guard"
        c["label"] = speakable(c["label"])
    return kept


def _legend_from_id(lamp_id):
    """The printed legend of a lamp with no owner, read off its node id: '..._AC1_OFF_LT' -> 'OFF'.
    Falls back to 'ON' when the last token is not a known legend ('..._IRS_1_LT')."""
    m = re.search(r"_([A-Z0-9]+)_LT$", lamp_id)
    return m.group(1) if m and m.group(1) in LEGEND_MEANINGS else "ON"


def dark_text(legends, node_id):
    """Meaning of every legend dark: the legend-set rule, unless curated."""
    if node_id in DARK_OVERRIDES:
        return DARK_OVERRIDES[node_id]
    legends = list(legends)
    if not legends:
        return None
    if "OFF" in legends:
        return "On"
    if any(l in ("ON", "AVAIL", "ARM", "SEL", "A", "B", "OPEN", "UP", "STOW", "MIC", "VOL") for l in legends):
        return "Off"
    return "Normal"


def latch_for(control, node_ids):
    """The L:var that holds this button's position, ONLY where that is proven.

    TFDi's tooltip reading '(L:<node>)' for its state text is the proof for ~167 buttons
    (anti-ice, system-mode selectors, source selects, breakers…); the battery is proven live.
    A tooltip reading ANOTHER CONTROL's var (ECON reads the air-system mode button) is not a
    latch; a tooltip reading a plain state var that is no control (the door-slide levers read
    MD11_EXT_DOOR_PAX_1L_ARMED_LVR) is.
    """
    nid = control["node_id"]
    if control["kind"] == "guard":
        return {"var": nid, "on": "Open", "off": "Closed"}
    if control["kind"] != "button":
        return None
    if nid in LATCH_FIXED:
        on, off = LATCH_FIXED[nid]
        return {"var": nid, "on": on, "off": off}
    vm = control.get("value_map") or {}
    sv = control.get("state_var")
    if sv and "1" in vm and "0" in vm and (sv == nid or sv not in node_ids):
        return {"var": sv, "on": vm["1"], "off": vm["0"]}
    return None


def pair_lamps(controls):
    """lamp node id -> (owner button, legend).

    Runs in two FULL passes over every button -- never interleaved per control -- so the
    ordering is deterministic regardless of what order `controls` lists the buttons in:

    1. Curated pairings (STATE_LAMPS), for every button, first. A curated entry always wins a
       collision no matter where its button sits in `controls`, because this whole pass
       finishes before the stem-rule pass (2) below even starts.
    2. The stem rule -- <stem>_<LEGEND>_LT with LEGEND a key of LEGEND_MEANINGS, or the bare
       <stem>_LT (legend ON unless overridden) -- visiting buttons in order of DESCENDING stem
       length, longest (most specific) first. A longer stem's BARE match must beat a shorter
       stem's <LEGEND> match on the very same lamp: MD11_OVHD_FUEL_DUMP_STOP_BT's bare
       "<stem>_LT" match on MD11_OVHD_FUEL_DUMP_STOP_LT would otherwise lose to
       MD11_OVHD_FUEL_DUMP_BT's shorter "<stem>_STOP_LT" match on the same lamp whenever DUMP_BT
       happened to be iterated first (STOP is itself a LEGEND_MEANINGS key). The curated pairing
       above already settles that specific case; this ordering is what stops a FUTURE, un-curated
       collision from silently flipping to whichever button the exported XML happens to list
       first on a package rebuild.

    Only BUTTONS fold lamps into their state; a knob's or switch's lamps become named rows
    instead (see lamp_name)."""
    lamps = {c["node_id"]: c for c in controls if c["kind"] == "annun"}
    owners = {}

    def attach(owner, lamp_id, legend):
        if lamp_id in lamps and lamp_id not in owners:
            owners[lamp_id] = (owner, legend)

    buttons = [c for c in controls if c["kind"] == "button"]

    for c in buttons:
        for lamp_id, legend in STATE_LAMPS.get(c["node_id"], []):
            attach(c, lamp_id, legend)

    for c in sorted(buttons, key=lambda c: len(_strip_suffix(c["node_id"])), reverse=True):
        stem = _strip_suffix(c["node_id"])
        for lamp_id in lamps:
            if not (lamp_id.startswith(stem + "_") and lamp_id.endswith("_LT")):
                continue
            rest = lamp_id[len(stem) + 1:-3]
            if rest and rest not in LEGEND_MEANINGS:
                continue
            attach(c, lamp_id, LAMP_LEGEND_OVERRIDES.get(lamp_id, rest or "ON"))
    return owners


def _owner_by_stem(lamp_id, non_buttons):
    """The knob/switch/handle/lever whose stem the lamp id starts with, if any.

    Longest stem first, as pair_lamps' stem pass does for buttons: a lamp under two stems
    ('MD11_X_TEST_ON_LT' under 'MD11_X' and 'MD11_X_TEST') belongs to the more specific one, not
    to whichever control happened to be listed first."""
    for c in sorted(non_buttons, key=lambda c: len(_strip_suffix(c["node_id"])), reverse=True):
        stem = _strip_suffix(c["node_id"])
        if lamp_id.startswith(stem + "_") and lamp_id.endswith("_LT"):
            return c, lamp_id[len(stem) + 1:-3]
    return None, None


# Labels for lamps whose generated name would collide with another lamp's. Every lamp is a
# Ctrl+M row (and a status row), and two rows with one name cannot be told apart by a screen
# reader — the Captain's and First Officer's master caution lights were both "Master Caution
# CAUT light". Discriminator first, per the house rule. The LTS panel's bare `_LT` vars drive
# the BUTTON's own emissive (TFDi's MD11_PA_Lights_Template binds MD11_OVHD_LTS_PA_LT to the PA
# button node), not a legend of their own, so they are named as the button's light; the
# `_CALL_LT` / `_ON_LT` annunciators beside them keep their legend names. Only the LABEL is
# curated here — the lamp's legend and lit word (its share of the composed state) are untouched.
LAMP_NAME_OVERRIDES = {
    "MD11_GSL_MST_CAUT_LT": "Captain Master Caution CAUT light",
    "MD11_GSR_MST_CAUT_LT": "First Officer Master Caution CAUT light",
    "MD11_GSL_MST_WRN_LT": "Captain Master Warning WARN light",
    "MD11_GSR_MST_WRN_LT": "First Officer Master Warning WARN light",
    "MD11_GSL_GS_INHIBIT_LT": "Captain Glideslope Inhibit INHIBIT light",
    "MD11_GSR_GS_INHIBIT_LT": "First Officer Glideslope Inhibit INHIBIT light",
    "MD11_CTR_AUX_HYD_PUMP_LT": "Center Panel Auxiliary Hydraulic Pump ON light",
    "MD11_OVHD_LTS_PA_LT": "Passenger Addressing light",
    "MD11_OVHD_LTS_FWD_ATTND_LT": "Forward Attendant Call light",
    "MD11_OVHD_LTS_MID_ATTND_LT": "Middle Attendant Call light",
    "MD11_OVHD_LTS_OVW_ATTND_LT": "Overwing Attendant Call light",
    "MD11_OVHD_LTS_AFT_ATTND_LT": "Aft Attendant Call light",
    "MD11_OVHD_LTS_MAINT_INTP_LT": "Maintenance Interphone light",
    "MD11_OVHD_LTS_MECH_LT": "Mechanic Call light",
    "MD11_OVHD_LTS_CREW_REST_LT": "Crew Rest Call light",
}


def lamp_name(lamp, owners, non_buttons):
    """(label, lit, dark, label_source) for one lamp; LAMP_NAME_OVERRIDES wins the label only."""
    label, lit, dark, source = _lamp_name(lamp, owners, non_buttons)
    nid = lamp["node_id"]
    if nid in LAMP_NAME_OVERRIDES:
        return LAMP_NAME_OVERRIDES[nid], lit, dark, "curated"
    return label, lit, dark, source


def _lamp_name(lamp, owners, non_buttons):
    """The generated (label, lit, dark, label_source) for one lamp, before LAMP_NAME_OVERRIDES."""
    nid = lamp["node_id"]
    if nid in owners:
        owner, legend = owners[nid]
        legend_word = legend.replace("_", " ")
        lit = LAMP_LIT_OVERRIDES.get(nid, LEGEND_MEANINGS.get(legend, legend_word.title()))
        return f"{owner['label']} {legend_word} light", lit, None, "paired"
    if nid in STANDALONE_LAMPS:
        name, lit, dark = STANDALONE_LAMPS[nid]
        return name, lit, dark, "curated"
    owner, rest = _owner_by_stem(nid, non_buttons)
    if owner is not None:
        legend = LAMP_LEGEND_OVERRIDES.get(nid, rest or "ON").replace("_", " ")
        return f"{owner['label']} {legend} light", "On", "Off", "curated"
    return humanize(nid), "On", "Off", "derived"


def apply_state(controls):
    """Attach the 'state' block and lamp names. Pure; call after finalize_controls."""
    owners = pair_lamps(controls)
    # Widened with STATE_LAMPS' own keys: those are hand-curated, always-real OTHER buttons
    # (e.g. the system-mode selectors ECON's tooltip borrows for its Off/On wording), so a
    # var equal to one is "another control's var", never this control's own latch, even on a
    # fixture too small to include that other button as a control of its own. A no-op against
    # the real ~1500-control run: every STATE_LAMPS key is already collected as a real button.
    node_ids = {c["node_id"] for c in controls} | set(STATE_LAMPS)
    non_buttons = [c for c in controls if c["kind"] in ("knob", "knob_push", "knob_pp", "switch", "handle", "lever")]
    by_lamp_owner = {}
    for lamp_id, (owner, legend) in owners.items():
        by_lamp_owner.setdefault(owner["node_id"], []).append((lamp_id, legend))

    for c in controls:
        nid = c["node_id"]
        if c["kind"] == "annun":
            label, lit, dark, source = lamp_name(c, owners, non_buttons)
            c["label"], c["label_source"] = label, source
            legend = owners[nid][1] if nid in owners else LAMP_LEGEND_OVERRIDES.get(nid, _legend_from_id(nid))
            state = {"lamps": [{"var": c["state_var"], "legend": legend, "lit": lit}]}
            if dark is not None:
                state["dark"] = dark
            c["state"] = state
            continue
        if c["kind"] not in ("button", "guard"):
            continue
        lamps = []
        for lamp_id, legend in by_lamp_owner.get(nid, []):
            lamp = next(l for l in controls if l["node_id"] == lamp_id)
            lamps.append({"var": lamp["state_var"], "legend": legend,
                          "lit": LAMP_LIT_OVERRIDES.get(lamp_id, LEGEND_MEANINGS.get(legend, legend.replace("_", " ").title()))})
        latch = latch_for(c, node_ids)
        dark = dark_text([l["legend"] for l in lamps], nid)
        if not lamps and latch is None and dark is None:
            continue            # a momentary button: no state block at all, so MainForm shows a bare label
        state = {"lamps": lamps}
        if latch:
            state["latch"] = latch
        if dark is not None:
            state["dark"] = dark
        c["state"] = state
    # Two lamps with one spoken name are two Ctrl+M rows a screen reader cannot tell apart.
    # Refuse to generate rather than ship the collision — add a LAMP_NAME_OVERRIDES entry.
    seen = {}
    for c in controls:
        if c["kind"] != "annun":
            continue
        if c["label"] in seen:
            raise ValueError(f"lamp label collision: {c['label']!r} on {seen[c['label']]} and {c['node_id']} "
                             f"— add a LAMP_NAME_OVERRIDES entry")
        seen[c["label"]] = c["node_id"]

    return controls


def kind_counts(controls):
    """Per-kind tallies for the JSON `counts.by_kind` block and the printed summary.

    Must be called on the FINALIZED control list, never the pre-finalize one
    `collect()` returns. `finalize_controls` reclassifies every `MD11_OPT_*` row from
    'annun' to 'option'; at the source-template level those rows are still 'annun',
    so tallying `collect()`'s pre-finalize `stats` and then patching an 'option'
    count in on the side counts each reclassified row twice -- once under 'annun'
    (still in the pre-finalize tally) and once under 'option' (the patch). Counting
    the finalized list instead means every control lands under exactly the one kind
    it actually renders as, and the tallies sum to `len(controls)`.
    """
    return Counter(c["kind"] for c in controls)


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


# An XML comment, non-greedy so each one ends at its own '-->'.
COMMENT_RE = re.compile(r"<!--.*?-->", re.S)


def read_xml(path):
    """Read a behavior XML leniently, with its comments removed.

    These files are exporter-generated and contain raw '&', stray degree signs and
    other tokens a strict XML parser rejects, so parse with a regex rather than
    ElementTree -- we only need flat <UseTemplate> blocks, not a real tree.

    Encoding is mixed: most files are UTF-8, but some carry cp1252 degree signs in
    tooltips (the bank-angle limiter's '5°'). Decoding those as UTF-8 yields U+FFFD
    and the degree silently turns into a replacement char the screen reader spells
    out, so fall back to cp1252 rather than lossily replacing.

    COMMENTS ARE STRIPPED HERE, which is what makes the flat parse read only LIVE
    blocks. The package carries 444 <UseTemplate> blocks inside comments, and
    USETEMPLATE_RE matches inside one: 388 had a live twin, but in 236 of those the
    commented copy sorts EARLIER in the walk (FlightDeck/Lighting.xml before
    Overhead.xml / RObserverAudio.xml / MiscOptions.xml) and so was the copy `seen`
    kept, the live block being counted as a duplicate. The map read correctly today
    only because the two copies were identical -- but a TFDi update that edits one of
    those live blocks and leaves the old copy commented out would have been read from
    the comment, and diff_maps would have printed NOTHING: the STOP rule blind for
    17% of the map, which is the whole safeguard for every future regeneration.
    Stripping here also settles the D6 nesting check by construction -- a
    <UseTemplate inside a comment inside a block's body is gone before collect() can
    mistake it for a real nesting. Eight lamps reached the map ONLY through a comment
    and are curated back explicitly instead (see CURATED_LAMPS).
    """
    with open(path, "rb") as fh:
        raw = fh.read()
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    for enc in ("utf-8", "cp1252", "latin-1"):
        try:
            return COMMENT_RE.sub("", raw.decode(enc))
        except UnicodeDecodeError:
            continue
    return COMMENT_RE.sub("", raw.decode("utf-8", errors="replace"))


def speakable(text):
    """Normalize label text for a screen reader.

    Symbols that are fine to look at are noise to hear: NVDA reads a bare '°' as
    'degrees' only in some punctuation modes and skips it entirely in others, so
    spell it out here rather than depending on the reader's settings. TFDi's XML
    carries HTML entities ('1&lt;-&gt;2'); decode them, and render the arrow as
    'to' because no reader has a good reading for '<->'.
    """
    if not text:
        return text
    text = html.unescape(text)
    text = (
        text.replace("<->", " to ")
        .replace("°", " degrees")
        .replace("△", "delta")
        .replace("�", "")
        .replace("–", "-")
        .replace("—", "-")
    )
    return re.sub(r"\s+", " ", text).strip()


def strip_outer_parens(label):
    """Strip ONE wrapping pair of parentheses, never a lone one.

    The old ``label.strip("()")`` removed the closing parenthesis of a label that merely
    ENDS with a parenthetical — 'APU Generator (APU Panel)' became 'APU Generator (APU
    Panel', and four labels shipped that way.
    """
    label = label.strip()
    if label.startswith("(") and label.endswith(")") and label.count("(") == 1:
        return label[1:-1].strip()
    return label


# `Name = "..."` is the same attribute as `Name="..."`. TFDi's Lighting.xml spells 58 blocks with
# spaces around the '=' (48 MD11_IntegralLighting_Template, 10 MD11_PA_Lights_Template -- skipped
# templates, so the map does not change), and a CONTROL spelt that way was skipped by the scan
# without a trace: never counted, never reported, and invisible to the nesting check too.
USETEMPLATE_RE = re.compile(
    r'<UseTemplate\s+Name\s*=\s*"([^"]+)"\s*(/>|>(.*?)</UseTemplate>)', re.S
)
FIELD_RE = re.compile(r"<([A-Z0-9_]+)>(.*?)</\1>", re.S)

# One %{case} position marker: '%{:0}', '%{:20}', '%{:-1}'.
CASE_MARK_RE = re.compile(r"%\{:\s*([-\d.]+)\s*\}")
# '%(<rpn>)%{if}A%{else}B%{end}': a two-way dynamic word. Group 1 is the TRUE word, group 2 the
# resting (false) one.
INLINE_IF_RE = re.compile(r"%\([^)]*(?:\)[^)%]*)*?\)\s*%\{if\}([^%]*)%\{else\}([^%]*)%\{end\}")
# Stands in for a literal '%%' while a case label or an if/else word is cut at its first directive.
_LITERAL_PERCENT = "\0"

# The HEAD of any %{if} or %{case} block: the '%(<rpn>)' whose VALUE the block's words describe.
# Group 1 is the RPN, group 2 the block kind. CASE_HEAD_RE matches only the bare-read shape and
# is used where that shape is already required; this one matches EVERY shape, which is what makes
# a computed head visible instead of silently read as the var's own value (D16, below).
BLOCK_HEAD_RE = re.compile(r"%\(([^)]*(?:\)[^)%]*)*?)\)\s*%\{(if|case)\}")
# A BARE VAR READ: '(L:NAME)' and nothing else, whitespace aside.
BARE_VAR_READ_RE = re.compile(r"\s*\(L:([A-Za-z0-9_]+)\)\s*")
# A single-var THRESHOLD: '(L:NAME) <number> <op>' -- the one computed head shape whose meaning is
# recoverable, so the words it chooses between can be recorded rather than merely refused.
THRESHOLD_RE = re.compile(r"\s*\(L:([A-Za-z0-9_]+)\)\s+(-?\d+(?:\.\d+)?)\s*(>=|<=|==|!=|>|<)\s*")
# Block kinds, for finding the head that governs a block found at a known offset.
_BLOCK_TOKEN = {"if": "%{if}", "case": "%{case}"}


def _bare_var_read(rpn):
    """The L:var `rpn` reads when it is a BARE READ -- '(L:NAME)' and nothing else -- else None.

    D16. A block's words describe the value of the RPN in front of it, and only a bare read hands
    that block the VARIABLE's own value: every other shape -- a comparison, arithmetic, a test
    across two vars -- yields something computed, so words keyed on it are not the var's positions.
    The gear lever is the casualty that named this rule: CenterInstrument.xml tests
    '(L:MD11_MIP_GEAR_SW) 20 >=' for Down, and the flat lift wrote {1 Down, 0 Up} over a variable
    that is really the lever's 0-25 TRAVEL. Nothing could detect it -- the generator keeps no raw
    tooltip and a two-entry map looks perfectly ordinary -- so it was found by a blind pilot
    hearing the wrong position and patched, one control at a time, in C#.
    """
    m = BARE_VAR_READ_RE.fullmatch(rpn)
    return m.group(1) if m else None


def _threshold(rpn):
    """`rpn` as a threshold block -- {var, op, value} -- when it is '(L:NAME) <number> <op>', else None.

    The one computed shape that can be recorded instead of merely refused: a later pass can key a
    control's positions off the COMPARISON the aircraft itself makes, which is the general form of
    what Md11GearLever.cs does by hand. The entities matter -- the XML is read without unescaping
    (read_xml), so TFDi's '>=' arrives as '&gt;=' and a raw match would see no operator at all.

    Every other computed shape stays a refusal, because none of them has a clean reading here:
    '38 65 (L:MD11_FLAP_RNG) rng' is a RANGE (curated, see CURATED), '(L:X) 25 - abs 0.1 <' is a
    distance from centre, and '(L:A) 1 == (L:B) 0 == and' is two vars at once. Teach this rule the
    next shape only when a real tooltip pairs it with plain words -- today none does.
    """
    m = THRESHOLD_RE.fullmatch(html.unescape(rpn))
    if not m:
        return None
    value = float(m.group(2))
    return {"var": m.group(1), "op": m.group(3),
            "value": int(value) if value == int(value) else value}


def _rpn_note(what, rpn):
    """A short note naming a refused block and the RPN that governs it, for the run's report.

    Unescaped, because the XML is read raw (read_xml): '&gt;=' in a line a human is meant to read
    as an operator is the difference between a report and a puzzle.
    """
    return f"{what} on {html.unescape(rpn or '').strip() or '(no head)'}"


def _head_rpn(block):
    """The RPN of the head a WHOLE block starts with ('%(<rpn>)%{if}...'), or None.

    The inline-label form of _governing_rpn: there the block is handed over already matched, so
    its head is simply its first token.
    """
    m = BLOCK_HEAD_RE.match(block)
    return m.group(1) if m else None


def _governing_rpn(text, block_start):
    """The RPN of the head whose %{if}/%{case} token starts at `block_start` in `text`, or None.

    A block with no head at all is not a shape this aircraft ships (all 310 %{if}/%{case} blocks
    in the package carry one), so None means "cannot say what these words key on" and the caller
    refuses -- a counted refusal is visible, a wrong value_map is not.
    """
    for h in BLOCK_HEAD_RE.finditer(text):
        if h.end() - len(_BLOCK_TOKEN[h.group(2)]) == block_start:
            return h.group(1)
    return None


def _var_keyed_blocks(expr, withheld=None):
    """`expr` reduced to the blocks whose words key on a BARE VAR READ (D16).

    A %{case} position marker belongs to the head in front of it, but _case_labels scans markers
    FLAT -- by design, so a nested case can overwrite an outer one's words. Flat, a case under a
    computed head hands its 0/1 words to the control's variable exactly as the gear lever's
    %{if} did. So the markers are filtered by their own head here, before the flat scan, and each
    accepted region is terminated with an '%{end}' so its last position cannot run into the next.

    `withheld`, when given, collects a short note for every region refused while carrying words --
    the ones a reader would otherwise have heard. A region with no words is not reported: the
    altimeter and trim tooltips compute their whole readout ('%(...)%!1.2f!') and have no
    positions to lose, so counting them would bury the ones that do.

    The PLAIN inline if/else words are read by their resting word first (_collapse_inline_ifs), as
    every other scan over a block's body does -- here because such a word carries a HEAD of its
    own, which would split the position it names away from the case it belongs to. The APU fire
    handle is the shape that proves it: its centre position is
    '%{:1}%((L:MD11_AOVHD_APUFIRE_SW))%{if}Shutoff%{else}Normal%{end}', and split there the centre
    lost its word and 'Bottle 2' was attributed to the nested head. The collapse is deliberately
    NOT applied to the if/else fallback in parse_tooltip: there the top-level block IS the words,
    and collapsing it would leave nothing to read (or to record as a threshold).
    """
    expr = _collapse_inline_ifs(expr)
    heads = list(BLOCK_HEAD_RE.finditer(expr))
    if not heads:
        return expr
    # Anything before the first head carries no marker in any shape this aircraft ships; kept
    # whole so a malformed tooltip reads exactly as it did before this rule existed.
    kept = [expr[:heads[0].start()]]
    for i, head in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(expr)
        region = expr[head.end():end]
        if _bare_var_read(head.group(1)):
            kept.append(region + "%{end}")
        elif withheld is not None and _case_labels(region):
            withheld.append(_rpn_note("%%{%s}" % head.group(2), head.group(1)))
    return "".join(kept)


def _lvars(text):
    """Every L:var `text` names, in order, repeats included."""
    return re.findall(r"L:([A-Za-z0-9_]+)", text)


def _reads_one_state_var(text):
    """True when `text` names exactly ONE L:var (one occurrence) and it is no airframe variant flag.

    The rule for lifting if/else words as a control's positions, on the trailing state expression
    and on an inline block alike: the words describe the var the expression reads, so they are this
    control's positions only when that var is the only one there. A second var brings a companion's
    words (the EFIS minimums caps); a LABEL_ONLY_VARS flag brings the variant's (the cabin zones).
    """
    names = _lvars(text)
    return len(names) == 1 and names[0] not in LABEL_ONLY_VARS


def _collapse_inline_ifs(text):
    """`text` with every PLAIN inline if/else read by its resting (false) word, and each '%%' held
    first as _LITERAL_PERCENT, for a position's reader to give back as '%'. Plain is INLINE_IF_RE's
    shape: neither word carries a directive of its own.

    One pass, applied by both a case position (_case_labels) and a composite's nested search
    (_composite_block): an inline word carries an %{end} of its own, and a nested %{case} is cut at
    its first %{end}.

    Nothing else is touched. An else-if chain keeps its outer if, and an if word holding an
    interpolation or a nested %{case} on a third var passes through whole -- so a leftover %{if} or
    a second case head stays inside the block, which is what _case_composite_block's guard tests
    for and refuses.
    """
    return INLINE_IF_RE.sub(lambda nested: nested.group(2), text.replace("%%", _LITERAL_PERCENT))


def _case_labels(text, empty_cases=None):
    """value -> label for every %{case} position marker in `text`, in TFDi's words.

    A position's label runs from its marker to the NEXT marker, so it can hold a nested block: the
    APU fire handle's centre is
      %{:1}%((L:MD11_AOVHD_APUFIRE_SW))%{if}Shutoff%{else}Normal%{end}
    and cutting at the first '%' threw that position away (the combo sat blank at rest, and the
    centre could not be selected). A nested if/else names the position by its resting (false)
    word -- the word the inline collapse also reads first -- and '%%' is a literal percent sign
    ("70%% N1" -> "70% N1"). Anything else is still cut at its first directive, and a later marker
    with the same value wins: a nested %{case} read here whole overwrites the outer words with its
    own, which is why a composite is split before its words are read (_composite_block).

    `empty_cases`, when given, collects the value of every position TFDi left without words (TEXT,
    not live data): it cannot be a combo entry, but it is reported for curation, never dropped
    unseen.
    """
    out = {}
    marks = list(CASE_MARK_RE.finditer(text))
    for i, mark in enumerate(marks):
        end = marks[i + 1].start() if i + 1 < len(marks) else len(text)
        seg = _collapse_inline_ifs(text[mark.end():end])
        seg = seg.split("%{end}", 1)[0]
        lbl = seg.split("%", 1)[0].replace(_LITERAL_PERCENT, "%").strip()
        if lbl:
            out[mark.group(1)] = lbl
        elif empty_cases is not None and "%(" not in seg:
            empty_cases.append(mark.group(1))
    return out


# D10: a state expression that is ONE outer %{case} on one var: '%((L:<var>))%{case}...%{end}'.
OUTER_CASE_RE = re.compile(r"%\(\s*\(L:([A-Za-z0-9_]+)\)\s*\)\s*%\{case\}(.*)%\{end\}", re.S)
# D13: the same with ONE outer %{if}: '%((L:<var>))%{if}...%{end}'.
OUTER_IF_RE = re.compile(r"%\(\s*\(L:([A-Za-z0-9_]+)\)\s*\)\s*%\{if\}(.*)%\{end\}", re.S)
# An outer %{if}'s two branches when its ELSE is a plain word (no directive): '<true>%{else}<word>'.
IF_BRANCHES_RE = re.compile(r"(.*)%\{else\}([^%]*)", re.S)
# The head of a %{case} on one var, wherever it sits.
CASE_HEAD_RE = re.compile(r"%\(\s*\(L:([A-Za-z0-9_]+)\)\s*\)\s*%\{case\}")
# A %{case} nested inside an outer block, ending at its own (first) %{end} -- which is why the
# block's inline if/else words are read first (_collapse_inline_ifs): theirs would end it early.
NESTED_CASE_RE = re.compile(r"%\(\s*\(L:([A-Za-z0-9_]+)\)\s*\)\s*%\{case\}.*?%\{end\}", re.S)
# Stands in for the nested block while the outer positions are read, so the position it fills is
# recognisable by its label.
_NESTED_CASE_MARK = "\x01"


def _composite_block(expr, node_id, empty_cases=None, source=None):
    """The `composite` block of a state expression, or None; a ValueError when it cannot be split.

    A composite is an outer %{case} on ANOTHER var one of whose positions is, whole, a nested
    %{case} on the control's OWN var (`node_id`). TFDi's three engine fire handles are exactly
    that: the outer case reads the PULL (MD11_AOVHD_ENGnFIRE_SW: 0 Normal, 1 Generator Field
    Disconnect) and position 2, fully pulled, hands over to the handle's own ROTATION
    (MD11_AOVHD_ENGnFIRE_KB: 0 Bottle 1, 1 Fuel and Hydraulic Disconnect, 2 Bottle 2). No single
    value_map can say that: scanned flat, the nested 0/1/2 overwrote the pull's words, so a stowed
    handle read "Bottle 1", and the row's walker turned the bottle-discharge wheel while it read
    the pull -- a walk that could never land. The block keeps both halves in TFDi's words:
    outer_var, outer_words (without the delegate position), delegate, inner_var, inner_words.

    An outer %{if} is the same shape with two positions, the else word and then the nested case
    (D13, _if_composite_block): the Elevator Feel knob.

    Both rules first read the block's PLAIN inline if/else words by their resting word, as every
    case position is read (_collapse_inline_ifs). The nested case still ends at its first %{end},
    and an inline word's own %{end} ends it there: cut short, the rest of the rotation reads as
    the pull's positions -- a wrong block -- or leaves no whole delegate position, and the handle
    falls back to the flat map over the pull without a word. The collapse removes that cut for
    the plain shape alone: a shape it cannot clean -- an else-if chain, an if word holding an
    interpolation, a case on a third var -- is still cut short, and there it is the REFUSAL that
    stops the wrong block, the case rule's guard on the leftover %{if} or second case head and
    the if rule's wholeness check.

    Not a composite: an outer case or if on the control's own var (the APU fire handle -- its
    nested block is an if/else on the pull, named by its resting word), a nested case on a third
    var, anything else the rules cannot split, and anything reading an airframe variant flag (a
    variant's words are never positions, D1). When such an expression nests a case on a var other
    than the outer one, the flat scan would put that var's words on the outer var -- the wrong-axis
    map D10 removes -- so it raises instead, naming `source` and the node, as a nested <UseTemplate>
    does. Only a single-var outer %{case} or %{if} is judged: the Spoilers and Flaps levers nest
    their case under a COMPUTED condition and are read exactly as before.

    `empty_cases` hears of an empty position only with a block: the rules scan into a list of their
    own, so a refusal never leaves a report behind.
    """
    if LABEL_ONLY_VARS.intersection(_lvars(expr)):
        return None
    expr = expr.strip()
    m, rule = OUTER_IF_RE.fullmatch(expr), _if_composite_block
    if not m:
        m, rule = OUTER_CASE_RE.fullmatch(expr), _case_composite_block
    if not m:
        return None
    outer_var, body = m.group(1), _collapse_inline_ifs(m.group(2))
    nested_vars = set(CASE_HEAD_RE.findall(body))
    if not nested_vars:
        return None
    found = []
    block = rule(outer_var, body, node_id, found)
    if block is not None:
        if empty_cases is not None:
            empty_cases.extend(found)
        return block
    foreign = sorted(nested_vars - {outer_var})
    if foreign:
        raise ValueError(
            f"{source or 'a tooltip'}: {node_id} reads {outer_var} and nests a %{{case}} on "
            f"{', '.join(foreign)} that no composite rule can split -- read flat, those words would "
            f"land on {outer_var}; teach _composite_block the shape before regenerating")
    return None


def _case_composite_block(outer_var, body, node_id, found):
    """D10's split of an outer %{case} on `outer_var`, whose `body` is already collapsed: the block,
    or None. Its scans report empty positions into `found`."""
    if outer_var == node_id:
        return None
    nested = list(NESTED_CASE_RE.finditer(body))
    if len(nested) != 1 or nested[0].group(1) != node_id:
        return None
    n = nested[0]
    # The body was collapsed in one pass (_collapse_inline_ifs), which reads an if/else by its
    # resting word only when both its words are plain. An else-if chain or an if word holding an
    # interpolation leaves an %{if} inside the nested case, and a case on a third var a second
    # case head; each carries an %{end} of its own, and the nested case was cut at it. Split
    # there, the rest of the rotation lands on the pull, or a third var's words on the rotation --
    # a wrong block, emitted without a word. A nested case the collapse could not clean cannot be
    # trusted, so it gets no block and reaches the refusal (_composite_block).
    if "%{if}" in n.group(0) or len(CASE_HEAD_RE.findall(n.group(0))) != 1:
        return None
    outer = _case_labels(body[:n.start()] + _NESTED_CASE_MARK + body[n.end():], found)
    delegates = [k for k, v in outer.items() if v == _NESTED_CASE_MARK]
    inner = _case_labels(n.group(0), found)
    if len(delegates) != 1 or not inner:
        return None
    return {
        "outer_var": outer_var,
        "outer_words": {k: speakable(v) for k, v in outer.items() if k != delegates[0]},
        "delegate": delegates[0],
        "inner_var": node_id,
        "inner_words": {k: speakable(v) for k, v in inner.items()},
    }


def _if_composite_block(outer_var, body, node_id, found):
    """D13's split of an outer %{if} on `outer_var`, whose `body` is already collapsed: the block,
    or None. Its scan reports empty positions into `found`.

    The if/else twin of D10's rule: an outer %{if} on ANOTHER var whose TRUE branch is, whole, a
    nested %{case} on the control's OWN var (`node_id`), and whose ELSE branch is a plain word once
    its inline words are read ('%%' a percent sign, as in a case label). TFDi's Elevator Feel knob
    is exactly that, and the only such tooltip in the package: its MANUAL latch
    (MD11_OVHD_FLTCTL_ELEVFEEL_BT) reads "Auto" while it is off, and once it is on the knob's OWN
    five positions take over (MD11_OVHD_FLTCTL_ELEVFEEL_KB: Decrease Reference Speed Fast ...
    Increase Reference Speed Fast). Scanned flat, the knob's words became the LATCH's positions: an
    aircraft in Auto read "Decrease Reference Speed Fast", the row's walker turned the knob's wheel
    while it read the latch -- a walk that could never land -- and the direct-write fallback then
    wrote the latch itself. So the false position (0) keeps the else word, and the true one (1) is
    the delegate.
    """
    split = IF_BRANCHES_RE.fullmatch(body)
    if not split or outer_var == node_id:
        return None
    branch = split.group(1).strip()
    word = split.group(2).replace(_LITERAL_PERCENT, "%").strip()
    n = NESTED_CASE_RE.match(branch)
    if not word or not n or n.end() != len(branch) or n.group(1) != node_id:
        return None
    inner = _case_labels(n.group(0), found)
    if not inner:
        return None
    return {
        "outer_var": outer_var,
        "outer_words": {"0": speakable(word)},
        "delegate": "1",
        "inner_var": node_id,
        "inner_words": {k: speakable(v) for k, v in inner.items()},
    }


def parse_tooltip(tooltip, node_id=None, empty_cases=None, composite=None, source=None,
                  threshold=None, withheld=None):
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

    `node_id` names the control in the one error this raises: a LABEL that reads an airframe
    variant flag (LABEL_ONLY_VARS) is refused unless LABEL_FIXES names the node. `empty_cases`,
    when given, collects the value of every %{case} position TFDi left without words (text, not
    live data) -- collect() reports those rather than letting them vanish.

    `node_id` is also the control's OWN var for the composite rule (_composite_block): a state
    expression that is one gets NO value_map -- not from an inline word in its label either -- and
    `composite`, when given (a dict), receives the block. Without a node id nothing is a composite.
    An expression the rule cannot split raises a ValueError naming `source` (the file the tooltip
    came from, when given) and the node.

    D16: words are lifted only from a block whose HEAD is a bare var read (_bare_var_read), because
    only then is the value they are keyed on the variable's own. `threshold`, when given (a dict),
    receives {var, op, value, when_true, when_false} for the one computed head that can still be
    read -- a single-var comparison choosing between two plain words -- and `withheld`, when given
    (a list), collects a note for every other refused block that carried words. Both are how a
    regeneration REPORTS this class; it used to ship a plausible-looking two-entry map instead.
    """
    if not tooltip:
        return None, None, {}

    tooltip = re.sub(r"\s+", " ", tooltip.strip())

    # A '%' directly followed by a letter is not tooltip syntax (the directives are '%(', '%{',
    # '%!' and the literal '%%'); it is a typo in the aircraft. The Auxiliary IRS selector ships
    # '%{if}%Nav%{else}Off%{end}', which left it with no positions at all and made MSFSBA render
    # the third IRS switch as a read-only row. Drop the stray sign and parse what TFDi meant.
    # The lookbehind keeps the second '%' of a literal '%%' in front of a letter.
    tooltip = re.sub(r"(?<!%)%(?=[A-Za-z])", "", tooltip)

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
    # The expression ends at the FIRST ')' after which nothing but an optional plain parenthetical
    # remains, and that parenthetical stays in the label: 'Nosewheel Steering (%(...)%{end})
    # (Tiller)' reads 'Nosewheel Steering (Tiller)'. Greedy, the match ran to the last ')' and
    # swallowed the qualifier into the expression. Making it lazy alone would not do: the '$'
    # anchor still drags a lazy match to the final ')'; the optional tail group is what stops it.
    expr = ""
    m = re.search(r"\s\((%.*?)\)(\s*\([^()%]*\))?\s*$", tooltip, re.S)
    if m:
        expr = m.group(1)
        label = (tooltip[: m.start()] + (m.group(2) or "")).strip()
    else:
        label = tooltip

    value_map = {}

    def _cases(text):
        return _case_labels(text, empty_cases)

    # A composite (D10, D13) takes no value_map, from its state or from its label (_inline_if and
    # _inline_case below): its words describe TWO vars (_composite_block).
    block = _composite_block(expr, node_id, empty_cases, source) if expr and node_id else None
    if block is not None:
        if composite is not None:
            composite.update(block)
    # A variant flag's words are never positions: nothing is lifted from an expression that
    # reads one (LABEL_ONLY_VARS).
    elif expr and not LABEL_ONLY_VARS.intersection(_lvars(expr)):
        # D16: only the blocks keyed on a bare var read; a case under a computed head describes
        # what that head COMPUTED, not the variable (_var_keyed_blocks).
        value_map = _cases(_var_keyed_blocks(expr, withheld))
        if not value_map:
            # '%%' is a literal percent sign in these words too, exactly as in a case label (D11):
            # the oxygen flow regulators read '%{if}100%%%{else}Normal%{end}' on their OWN var, and
            # cut at the '%' they had no words -- so no latch, and no spoken state at all.
            marked = expr.replace("%%", _LITERAL_PERCENT)
            m = re.search(r"%\{if\}([^%]*)%\{else\}([^%]*)%\{end\}", marked)
            # The if/else words are THIS control's positions only when the expression reads ONE
            # L:var -- the state var. The EFIS minimums caps read two: their own value first
            # ('%((L:MD11_CAP_MINIMUMS))%!d!'), then the mode SWITCH's var for the Baro/Radio
            # word. Lifting that word gave a 0-15000 ft value knob a {0 Radio, 1 Baro} map, so
            # MSFSBA offered a two-entry combo whose wheel moved the minimums, never the mode.
            # A companion's words belong to the companion: its own tooltip carries them.
            #
            # Two more controls are touched, deliberately: the ECON and TRIM AIR buttons NEST the
            # air-system selector around their own var, so their expressions name two L:vars too
            # and lose an {Off, On} map that nothing consumed (a button never reads `values`, and
            # the `state` block that composes its spoken position is generated separately).
            if m and _reads_one_state_var(expr):
                on, off = (word.replace(_LITERAL_PERCENT, "%").strip() for word in m.groups())
                rpn = _governing_rpn(marked, m.start())
                if on and off and rpn is not None and _bare_var_read(rpn):
                    value_map = {"1": on, "0": off}
                elif on and off:
                    # D16: the words are real, the KEY is not -- the head computed the boolean the
                    # %{if} tested, so {1: on, 0: off} would be a map over a variable that never
                    # holds 1 or 0. The gear lever: '(L:MD11_MIP_GEAR_SW) 20 >=' over a 0-25 travel.
                    # Record the comparison where it can be read, and refuse (visibly) where not.
                    block = _threshold(rpn) if rpn else None
                    if block is not None and threshold is not None:
                        block.update(when_true=speakable(on), when_false=speakable(off))
                        threshold.update(block)
                    elif withheld is not None:
                        withheld.append(_rpn_note("%{if}", rpn))

    # --- (b) collapse inline dynamic blocks left in the label -------------------
    # A label that reads an airframe variant flag has one wording per variant, and nothing here
    # can choose: collapsed, TFDi's cabin knobs read "Forward Cabin/Courier Cabin Temperature" on
    # every airframe. Refuse until LABEL_FIXES names the control; with the name, the variant
    # blocks below collapse to nothing and give value_map nothing.
    variant_vars = sorted(LABEL_ONLY_VARS.intersection(_lvars(label)))
    if variant_vars and node_id not in LABEL_FIXES:
        raise ValueError(
            f"{node_id or 'a control'}: its tooltip label reads the airframe variant flag "
            f"{', '.join(variant_vars)}, so it has one wording per variant -- add a LABEL_FIXES "
            f"entry that names it for every variant")

    # Positions come from an inline block under the trailing path's rule, judged on the WHOLE
    # label: the FCP mode knobs name ONE var here (their mode export) and keep Heading/Track,
    # while a block beside a second var lifts nothing -- judged on the block alone it would hand
    # a companion's words to a control whose state var is the other one.
    label_reads_one_var = _reads_one_state_var(label)

    # '%(<rpn>)%{if}A%{else}B%{end}' -> 'B/A'  (false state first: it reads better
    # as the resting position, e.g. 'Heading/Track', 'IAS/Mach').
    def _inline_if(m):
        if LABEL_ONLY_VARS.intersection(_lvars(m.group(0))):
            return ""
        a, b = m.group(1).strip(), m.group(2).strip()
        if not value_map and label_reads_one_var and block is None:
            # D16 here too: the label still reads 'Heading/Track' whatever the head computes --
            # the collapsed wording is the best spoken name either way -- but only a bare var read
            # makes those words the variable's POSITIONS. No tooltip in the package pairs a
            # computed head with an inline label block today; the gate is what keeps the next one
            # from shipping a boolean map the way the gear lever's trailing block did.
            if _bare_var_read(_head_rpn(m.group(0)) or ""):
                value_map.update({"1": a, "0": b})
            elif withheld is not None:
                withheld.append(_rpn_note("inline %{if}", _head_rpn(m.group(0))))
        return f"{b}/{a}" if a and b else (a or b)

    label = INLINE_IF_RE.sub(_inline_if, label)

    # '%(<rpn>)%{case}%{:0}A%{:1}B%{end}' -> 'A/B'
    def _inline_case(m):
        if LABEL_ONLY_VARS.intersection(_lvars(m.group(0))):
            return ""
        cases = _cases(m.group(0))
        if cases and not value_map and label_reads_one_var and block is None:
            if _bare_var_read(_head_rpn(m.group(0)) or ""):
                value_map.update(cases)
            elif withheld is not None:
                withheld.append(_rpn_note("inline %{case}", _head_rpn(m.group(0))))
        return "/".join(cases.values()) if cases else ""

    label = re.sub(
        r"%\([^)]*(?:\)[^)%]*)*?\)\s*%\{case\}.*?%\{end\}", _inline_case, label
    )

    # Any remaining numeric interpolation ('%(<rpn>)%!d!', '%!1.2f!') is live data,
    # not label text -- drop it.
    label = re.sub(r"%\([^)]*(?:\)[^)%]*)*?\)", "", label)
    label = re.sub(r"%![^!]*!", "", label)
    label = re.sub(r"%\{[^}]*\}", "", label)

    label = strip_outer_parens(re.sub(r"\s+", " ", label))
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


NODE_ID_RE = re.compile(r"<NODE_ID>(.*?)</NODE_ID>", re.S)


def _nested_template_error(source, tmpl, text, m):
    """The error for a <UseTemplate> found inside another block's body (see collect()).

    The parse is flat: a block's body runs to the FIRST </UseTemplate>, so a nested block ends its
    parent early, lends the parent its fields and drops the parent's remainder -- all silently.
    Names the file, the parent's node (its NODE_ID sits before the nested block, or after the
    nested block's close, where this match stopped reading) and the nested node."""
    body = m.group(3) or ""
    cut = body.find("<UseTemplate")
    after = text[m.end():]
    close = after.find("</UseTemplate>")
    parent = NODE_ID_RE.search(body[:cut]) or NODE_ID_RE.search(after[:close] if close >= 0 else after)
    nested = NODE_ID_RE.search(body[cut:])
    return ValueError(
        f"{source}: a <UseTemplate> is nested inside a {tmpl} block "
        f"(node {parent.group(1).strip() if parent else 'unknown'}, nested node "
        f"{nested.group(1).strip() if nested else 'unknown'}) -- the flat parse would merge the two "
        f"silently; teach collect() nesting before regenerating")


def collect(pkg_dir):
    base = os.path.join(pkg_dir, md11_paths.PACKAGE_MARKER)
    if not os.path.isdir(base):
        sys.exit(f"ModelBehaviorDefs not found under {base}")

    controls = []
    seen = set()
    stats = Counter()

    for root, dirs, files in os.walk(base):
        # Folders in name order as well as files: os.walk lists them in whatever order the file
        # system returns, and where two files define one node the first one read is the one kept
        # (`seen` below). Sorting the list in place is what steers the rest of the walk.
        dirs.sort()
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
                if "<UseTemplate" in body:
                    raise _nested_template_error(source, tmpl, text, m)
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
                empty_cases = []
                composite = {}
                threshold = {}
                withheld = []
                label, state_var, value_map = parse_tooltip(
                    fields.get("TOOLTIPID"), node_id=node_id, empty_cases=empty_cases,
                    composite=composite, source=source, threshold=threshold, withheld=withheld)

                key = (node_id, kind, tuple(sorted(events.items())))
                if key in seen:
                    stats["duplicate"] += 1
                    continue
                seen.add(key)
                # A %{case} position TFDi left without words: main() prints and counts it for
                # curation, so it never leaves the map unseen.
                for case_value in empty_cases:
                    stats[f"empty_case_label:{source} {node_id} %{{:{case_value}}}"] += 1
                # D16. Words the flat lift would have keyed on a COMPUTED head: recorded as a
                # threshold where the comparison can be read, counted as a refusal where not.
                # Neither used to leave a trace of any kind -- that is what let a boolean map over
                # the gear lever's 0-25 travel ship and stay invisible until a pilot heard it.
                if threshold:
                    stats[f"rpn_threshold:{source} {node_id} -- "
                          f"(L:{threshold['var']}) {threshold['op']} {threshold['value']}"] += 1
                for what in withheld:
                    stats[f"unkeyed_words:{source} {node_id} {what}"] += 1

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
                        # Only on a composite (D10, D13): every other control's JSON is unchanged.
                        **({"composite": composite} if composite else {}),
                        # Only where TFDi's own words are chosen by a COMPARISON rather than by the
                        # variable's value (D16): the rule the aircraft applies, so one reader can
                        # serve every such control instead of a hand-written class per casualty.
                        **({"threshold": threshold} if threshold else {}),
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
    # An explicit --wasm is taken on faith below (`wasm_arg or ...`), and a
    # missing/unreadable file there degrades silently: wasm_vars() just returns
    # empty sets, so the map still gets written but with ZERO wasm-derived
    # L:vars -- the same silent-partial-map failure this function exists to
    # rule out, just arriving through the override instead of a bad guess.
    # The discovered path (find_wasm) is never checked here: it only ever
    # returns paths it found by walking, so it already exists.
    if wasm_arg and not os.path.isfile(wasm_arg):
        sys.exit(
            "--wasm does not exist: %s\n"
            "Expected an %s file at that path."
            % (wasm_arg, md11_paths.WASM_NAME)
        )

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
        if not (sys.stdin and sys.stdin.isatty()):
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


def _exit_if_incomplete(out_path, pkg, wasm, controls, all_vars):
    """Refuse to write a map missing controls or the wasm-derived L:vars.

    The owner's ruling: refuse to write anything rather than emit a partial
    map. A map with no controls is missing every clickable cockpit control; a
    map with no wasm control vars is missing the PFD speed tape and V-speed
    read-outs, which have no other source. Either looks like an ordinary
    successful run (exit 0, a file written) unless something checks for it
    here -- collect() and wasm_vars() both degrade silently (an empty XML
    folder, or a wasm with no 'Aircraft::vars->' symbols, just produce empty
    results, not an exception).
    """
    problems = []
    if not controls:
        problems.append("no controls (found 0 in the package's ModelBehaviorDefs)")
    if not all_vars:
        problems.append(
            "no wasm control vars (found 0 'Aircraft::vars->MD11_...' symbols in the wasm)"
        )
    if not problems:
        return
    sys.exit(
        "Refusing to write %s: %s.\n"
        "Package : %s\n"
        "Wasm    : %s\n"
        "A partial map is worse than none, so nothing was written. This "
        "usually means --wasm points at the wrong file, the wasm is "
        "truncated or partial, or a TFDi update changed the symbol "
        "convention this script parses -- not that the aircraft genuinely "
        "has no controls." % (out_path, "; ".join(problems), pkg, wasm)
    )


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pkg", default=None,
                    help="MD-11 package folder. Omit to search this PC.")
    ap.add_argument("--wasm", default=None,
                    help="md11host.wasm path. Omit to use the one found next to --pkg "
                         "(or discovered automatically).")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "md11_control_map.json"))
    args = ap.parse_args()

    pkg, wasm = resolve_paths(args.pkg, args.wasm)

    controls, stats = collect(pkg)
    # The wasm is read BEFORE the curated lamps, which are gated on its control table.
    all_vars, export_vars = wasm_vars(wasm)
    controls = add_curated_lamps(controls, all_vars, stats)
    controls = finalize_controls(controls)
    controls = apply_state(controls)
    # by_kind (below) and the printed summary both come from the FINALIZED list via
    # kind_counts(), never from `stats` (collect()'s pre-finalize per-kind tally) --
    # see kind_counts()'s docstring for why patching stats on the side double-counts.
    by_kind = kind_counts(controls)

    _exit_if_incomplete(args.out, pkg, wasm, controls, all_vars)

    referenced = {c["node_id"] for c in controls} | {
        c["state_var"] for c in controls if c["state_var"]
    }
    orphan_vars = sorted(v for v in all_vars if v not in referenced)

    by_area = defaultdict(list)
    for c in controls:
        by_area[c["area"]].append(c)

    # %{case} positions with no words (collect() records them): counted in the map, printed below.
    empty_case_labels = sorted(k[len("empty_case_label:"):] for k in stats
                               if k.startswith("empty_case_label:"))
    # D16, both halves: state words whose head COMPUTES its value rather than reading a variable.
    rpn_thresholds = sorted(k[len("rpn_threshold:"):] for k in stats
                            if k.startswith("rpn_threshold:"))
    unkeyed_words = sorted(k[len("unkeyed_words:"):] for k in stats
                           if k.startswith("unkeyed_words:"))

    out = {
        "_generated_by": "tools/md11-gen/generate_md11_map.py",
        "_source": "TFDi MD-11 ModelBehaviorDefs + md11host.wasm",
        "counts": {
            "controls": len(controls),
            "wasm_control_vars": len(all_vars),
            "export_vars": len(export_vars),
            "state_only_vars": len(orphan_vars),
            "empty_case_labels": len(empty_case_labels),
            "rpn_thresholds": len(rpn_thresholds),
            "unkeyed_words": len(unkeyed_words),
            "by_kind": dict(sorted(by_kind.items())),
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
    print(f"  empty case lbls : {len(empty_case_labels)}")
    print(f"  rpn thresholds  : {len(rpn_thresholds)}")
    print(f"  unkeyed words   : {len(unkeyed_words)}")
    print(f"  curated lamps   : {stats['curated_lamp']}")   # lamps TFDi defines only inside a comment
    print("  by kind         :")
    for k, v in sorted(by_kind.items()):
        print(f"    {k:10s} {v}")
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
    for where in empty_case_labels:
        print(f"empty case label (a position with no name; curate it): {where}", file=sys.stderr)
    for where in rpn_thresholds:
        print(f"rpn threshold (words chosen by a comparison, recorded as one): {where}",
              file=sys.stderr)
    for where in unkeyed_words:
        print(f"unkeyed words (a computed head, so no value_map; curate it if a pilot needs the "
              f"positions): {where}", file=sys.stderr)


if __name__ == "__main__":
    main()
