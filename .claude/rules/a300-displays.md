---
paths:
  - "MSFSBlindAssist/Aircraft/A300/**"
  - "MSFSBlindAssist/Aircraft/IniA300*.cs"
  - "MSFSBlindAssist/Forms/A300/**"
  - "MSFSBlindAssist/SimConnect/A300/**"
  - "MSFSBlindAssist/MainForm.A300.cs"
  - "MSFSBlindAssist/Resources/coherent-a300*.js"
  - "tools/a300-efb-test/**"
  - "tests/MSFSBlindAssist.Tests/**/*A300*.cs"
---
# iniBuilds A300-600 displays, MCDUs, tablet and autoflight rules

Loaded when Claude reads matching code. Background: docs/a300.md. Full text of each rule: docs/invariants/a300-displays.md. The control, light and panel rules are in a300.md.

- [A300-4] The MCDU screens are client data `iniAirbusMCDU_1`/`_2`, published only while `L:INI_MCDU_OPTION` is 1: `EnsureMcduExport` sends it on EVERY open and after a load. One `A300McduDataManager` per connection, snapshot on a DIFFERENT request id; read glyphs via `A300McduText.Glyph`; ONE key queue; never CLR an empty scratchpad. (more: see full) Full: docs/invariants/a300-displays.md#a300-4
- [A300-5] A300 typed values go only through `A300TypedValues`: NAV frequencies as their `_MHZ`/`_KHZ` L:var parts; a speed in the other unit sends SPD/MACH, WAITS `ModeSwitchSettleMs`, then writes, never in one string; altimeters are `index value (>K:2:KOHLSMAN_SET)`, index FIRST. (more: see full) Full: docs/invariants/a300-displays.md#a300-5
- [A300-6] `coherent-a300-efb-agent.js` presses with ONE click event at `A.pressTarget` (where a finger would land), never on the stamped control alone; never requires size on a container, never `focus()`es a field, never emits or writes back a password, only whether one is set; overlays are found with `A.shown` and read ALONE. (more: see full) Full: docs/invariants/a300-displays.md#a300-6
- [A300-8] A300 status boxes and call-outs read ONLY aircraft variables and stock SimVars, never the sim's memory, a picture or AI; memos stay transcribed ([A300-27]). AI is ONLY the four on-demand reads (Alt+P, N, E, S; `A300DisplayReads`, design decision 2026-10-10) at measured views: an AI answer never feeds a box or call-out; Alt+I stays unbound. Full: docs/invariants/a300-displays.md#a300-8
- [A300-9] `A300Fma` transcribes PFD::drawFMA (v1.0.11); change a word only from a new characterization. It is composed at the end of the ONE batch carrying all 27 sources (`DeferredFlushWatchVariable`): a new batch-covered A300 var can split it, so give it its own subscription. It checks `IsMuted` itself. (more: see full) Full: docs/invariants/a300-displays.md#a300-9
- [A300-10] A300 display boxes: green dot, S and F only at the flap positions the tape draws them; VMAX is `INI_max_speed`, not `INI_VMAX_SPEED`; the standby altimeter is `INDICATED ALTITUDE:3`; the ILS is NAV 3; tank weights are KILOGRAMS. An ECAM page lists only what its drawing routine prints, never a guess. Full: docs/invariants/a300-displays.md#a300-10
- [A300-11] `A300TakeoffCallouts` takes V1/VR from `INI_V1_FMGS`/`INI_VR_FMGS`, and V2 from `INI_V2_FMGS` only when `PlausibleV2` accepts it (VR to VR+30), because it FOLLOWS the FCU speed window; never announce its changes. Drop the arm on every context reset and reconnect, never the speeds. Full: docs/invariants/a300-displays.md#a300-11
- [A300-14] Only the A300 altitude window speaks on its own (`A300FcuWindows`): the autopilot also writes the heading, speed and V/S windows, so their changes are not anyone's. A panel knob step is read back once it lands; MSFSBA's own typed altitude or altitude step mutes the call-out for 2.5 s. (more: see full) Full: docs/invariants/a300-displays.md#a300-14
- [A300-15] The A300 decision height is `L:INI_MINIMUMS_PILOT`/`_FO`, written directly by the typed box; the DH knob moves it only while `L:INI_DH_SELECTED` is 1 and steps the flight path angle otherwise, so never set it through the knob. Full: docs/invariants/a300-displays.md#a300-15
- [A300-16] The altimeter knob PULL is STD and PUSH is QNH (`XMLVAR_Baro1/2_Mode` 1/0), but STD is a FLAG ONLY: the setting does not change. Ctrl+B's STD (`A300Baro`) pulls only sides in QNH (a pull saves the setting in `INI_BARO1/2_PRESSURE`) and writes 1013.25; QNH restores the saved setting. (more: see full) Full: docs/invariants/a300-displays.md#a300-16
- [A300-17] Every A300 value box is one `ValueInputForm` type and `ShowTrackedWindow` keys on the type, so `ShowValueDialog` closes an open box with another title first (`A300AutoflightWindows.ReplacesOpenBox`); never show a value box through `ShowTrackedWindow` alone, or Ctrl+H re-shows an open speed box. Full: docs/invariants/a300-displays.md#a300-17
- [A300-18] Autoflight window labels are the panel row's name plus the knob's own push/pull words from the map's `action` (`A300AutoflightWindows.LabelFor`), never hand-typed; each control sits in one window only, and every press goes through the row's write path (`HandleUIVariableSet`, `PressRow`). Full: docs/invariants/a300-displays.md#a300-18
- [A300-22] Ctrl+B's one mode button reads the REPORTED modes (`A300Baro.ModeState`, each side while they differ) and runs STD only from both QNH, else QNH; a typed value first pushes every side in STD and confirms it reads QNH (`PressKnobsAsync`), else names that side and writes nothing; an unread mode or a running sequence is refused aloud. Full: docs/invariants/a300-displays.md#a300-22
- [A300-27] `A300EwdMemos` transcribes the twenty `Message_MemoN::is_active` (v1.0.11) in the E/WD's order; change one only from a new reading of the aircraft's code. A memo shows after 2.5 s active (`A300MemoTracker`); a load's first complete reading is the baseline. Memos are NEVER spoken (design decision, 2026-10-11): the box only, no Ctrl+M row. Full: docs/invariants/a300-displays.md#a300-27
