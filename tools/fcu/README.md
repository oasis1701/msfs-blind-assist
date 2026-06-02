# A380 FCU verification harness

Reads/verifies the FlyByWire A380X FCU live, via the Coherent debugger
(`tools/coherent-eval.ps1`, port 19999 — MSFS open with the A380X loaded; no Dev Mode).

- `fcu-read.ps1` — print the full live FCU state (the primary verification tool).
- `fcu-set.ps1 -Event <name> [-Value <n>]` — best-effort fire an event from the page.
- `fcu-roundtrip.ps1` — read → set → read PASS/FAIL.

## Single-socket caveat
Coherent GT allows ONE inspector connection per page. Close the app's Coherent
clients (e.g. the MCDU/EFB windows) before driving the page with these tools, and
vice-versa. The FCU view (`A380X_FCU`) is normally free.

## Set-path findings (Task 1 live discovery, 2026-06-01)
- Probe-side `SimVar.SetSimVarValue('K:A32NX.FCU_*')` does **NOT** move the selected
  value (confirmed live: heading stayed 0 after a probe-side `FCU_HDG_SET 123`).
  These FBW input events only respond to the app's SimConnect TransmitClientEvent.
  **So: set FCU values via the app window, then verify with `fcu-read.ps1`.** The
  harness's guaranteed role is READING/verifying state.
- `KOHLSMAN_SET` both-sides: inconclusive on a COLD/DARK aircraft — the EFIS baro
  word (`A32NX_FCU_LEFT/RIGHT_EIS_BARO_HPA`) is frozen at the 1013.2 default until
  avionics are powered, so neither side moved. CLAUDE.md asserts (from prior live
  verification) that `CAPT_QNH_SET` → `KOHLSMAN_SET` "moves both altimeters
  together"; re-confirm on a powered aircraft via the Baro window in Task 9.
