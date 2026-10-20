# iFly 737 MAX8 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/ifly-737.md`, which Claude Code loads when it reads matching code. Background: [ifly-737.md](../ifly-737.md).
The text is verbatim from docs/ifly-737.md as of `42d4a67b`; a cross-reference such as "below" points at that doc.

## IFLY-1

- **`IFlySdkOffsets`/`IFlySdkFields`/`IFlyKeyCommand` are GENERATED** (`tools/ifly-gen/generate_ifly_sdk.py` from the SDK headers) — regenerate, never hand-edit. ⚠ The generator truncates every enum comment at `Value1:`, so the vendor's per-command Value2/Value3 encoding docs almost always exist only in the raw GBK `key_command.h` (a comment with no `Value1:` column, such as `ENGAPU_THROTTLE1_POS`/`ENGAPU_THROTTLE2_POS`, keeps its text in the generated `IFlyKeyCommand.cs`) — read the raw header before declaring a command encoding undocumented (the speedbrake-lever "no Value2 detail" claim, corrected below, was exactly this trap). → [ifly-737.md](../ifly-737.md)

Corrected 2026-10-09: the generated files now include `IFlyKeyCommand`, and "never in the generated `IFlyKeyCommand.cs`" became "almost always only in `key_command.h`": a comment with no `Value1:` column survives the cut. Evidence: `generate_ifly_sdk.py` (the truncation at `Value1:`) and `IFlyKeyCommand.cs`, where `ENGAPU_THROTTLE1_POS` and `ENGAPU_THROTTLE2_POS` keep their Value2/Value3 text.

Split out on 2026-10-09: IFLY-5 (read the raw `key_command.h` before calling an encoding undocumented). This text keeps it as written; it has its own section below.

## IFLY-2

- Support for the iFly 737 MAX8 (`IFLY_737MAX8`) via the **official iFly SDK** — shared-memory reads + WM_COPYDATA writes. No MobiFlight, no L:var writes — except the deliberate cockpit-clickspot trigger replays via `SetLVar` where the SDK command is broken or absent: the autopilot window's engage fallback (`VC_Automatic_Flight_trigger_VAL`), the NAV transfer fallback and the transponder squawk keypad (both `VC_Navigation_trigger_VAL` — the SDK XPNDR keypad clicks cannot commit an entry, see below). → [ifly-737.md](../ifly-737.md)

Corrected 2026-10-09: the rule named only the clickspot replays as exceptions, but three writes are stock SimConnect events (`simConnect.SendEvent`) by design, and an "SDK-first" cleanup that moved them to WM_COPYDATA would break them. NAV tuning (`TuneNavActiveAsync`) sends `NAVn_STBY_SET` with the BCD16 frequency and then `NAVn_RADIO_SWAP` as its PRIMARY path, because simulating the SDK panel keypad dropped digits and errored the transfer (live log 2026-07-19: keyed 0950, standby read 109.10, TFR flagged ERR); the SDK keypad, then the `VC_Navigation_trigger_VAL` replay, remain only as fallbacks. The flaps combo (`HandleUIVariableSet`, `FLAP_Status`) walks `FLAPS_INCR`/`FLAPS_DECR` from the current detent, because the SDK `FLTCTRL_FLAP_SET` is a complete no-op. The Ctrl+B baro dialog sets `KOHLSMAN_SET` (millibars times 16), because the SDK has INC/DEC only and the aircraft tracks the stock Kohlsman value. Evidence: `IFly737MAXDefinition.TuneNavActiveAsync`, the `FLAP_Status` branch of `HandleUIVariableSet`, and `ShowBaroDialog`'s input callback, all in `Aircraft/IFly737MAXDefinition.cs`; and the "third sanctioned stock-event exception" note in `docs/ifly-737.md`.

## IFLY-3

- **Spring-loaded electrical momentaries (GRD PWR + ENG/APU generator switches) actuate ONLY via their momentary CLICK commands, never SET** (live-verified 2026-07-23): `ELECTRICAL_GRD_PWR_SET` moves the switch animation (audible click) but the electrical connect/trip logic NEVER fires — dead in both directions with ground power available; the old SET-hold-release-to-NEUTRAL emulation was equally dead. The click commands are the cockpit-clickspot path and work: **Move DOWN = ON/connect, Move UP = OFF/trip** (yes, DOWN is ON), and the SDK springs the switch back to NEUTRAL itself (verified on GRD PWR and APU GEN 1, both directions, watching `ENG_TRANSFER_BUS_OFF`/`APU_GEN_OFF_BUS` — including real 737 last-source-wins bus takeover). The APU GEN SET happened to work in testing, but SETs proved untrustworthy switch-by-switch, so all five route through clicks. They render as stateless On/Off momentary button pairs (`BTN_GRD_PWR_ON`/`_OFF`, `BTN_GEN_n_ON`/`_OFF`, `BTN_APU_GEN_n_ON`/`_OFF`) — the exact PMDG 737 shape — because the switch's resting position is always NEUTRAL and connect state lives in the annunciators. The APU START knob is NOT in this class: `ENGAPU_APU_SET 2` latches a real APU start and springs back on its own. → [ifly-737.md](../ifly-737.md)

## IFLY-4

- **Per-send plugin-window resolution** (`SendCommand`): the WM_COPYDATA target HWND is re-resolved via `FindWindowEx` on EVERY send, never cached — a cached handle can be recycled by Windows after a sim restart, and a send to the recycled window "succeeds" (delivered) while the aircraft hears nothing; the title scan costs microseconds against 40-120 ms-paced commands. Send-failure logging is edge-triggered (`_lastSendFailed`) so a sustained-failure loop logs once, not once per send. → [ifly-737.md](../ifly-737.md)

## IFLY-5

- `tools/ifly-gen/generate_ifly_sdk.py` truncates every `IFlyKeyCommand` enum comment at `Value1:`, so a command's Value2/Value3 encoding is documented only in the iFly SDK's raw GBK `key_command.h`, bar a comment with no `Value1:` column (`ENGAPU_THROTTLE1_POS`, `ENGAPU_THROTTLE2_POS`), which keeps its text. Read the raw header before calling a command's encoding undocumented: the speedbrake-lever "no Value2 detail" claim (see docs/ifly-737.md) was exactly this trap. Nothing loaded this rule on the generated file or the definitions before it had its own line. → [ifly-737.md](../ifly-737.md)

Split from IFLY-1 on 2026-10-09: one mechanism per ID.
