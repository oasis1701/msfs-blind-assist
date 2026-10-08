# iFly 737 MAX8 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/ifly-737.md`, which Claude Code loads when it reads matching code. Background: [ifly-737.md](../ifly-737.md).
The text is verbatim from docs/ifly-737.md as of `42d4a67b`; a cross-reference such as "below" points at that doc.

## IFLY-1

- **`IFlySdkOffsets`/`IFlySdkFields` are GENERATED** (`tools/ifly-gen/generate_ifly_sdk.py` from SDK_Defines.h) — regenerate, never hand-edit. ⚠ The generator truncates every enum comment at `Value1:`, so the vendor's per-command Value2/Value3 encoding docs exist ONLY in the raw GBK `key_command.h`, never in the generated `IFlyKeyCommand.cs` — read the raw header before declaring a command encoding undocumented (the speedbrake-lever "no Value2 detail" claim, corrected below, was exactly this trap). → [ifly-737.md](../ifly-737.md)

## IFLY-2

- Support for the iFly 737 MAX8 (`IFLY_737MAX8`) via the **official iFly SDK** — shared-memory reads + WM_COPYDATA writes. No MobiFlight, no L:var writes — except the deliberate cockpit-clickspot trigger replays via `SetLVar` where the SDK command is broken or absent: the autopilot window's engage fallback (`VC_Automatic_Flight_trigger_VAL`), the NAV transfer fallback and the transponder squawk keypad (both `VC_Navigation_trigger_VAL` — the SDK XPNDR keypad clicks cannot commit an entry, see below). → [ifly-737.md](../ifly-737.md)

## IFLY-3

- **Spring-loaded electrical momentaries (GRD PWR + ENG/APU generator switches) actuate ONLY via their momentary CLICK commands, never SET** (live-verified 2026-07-23): `ELECTRICAL_GRD_PWR_SET` moves the switch animation (audible click) but the electrical connect/trip logic NEVER fires — dead in both directions with ground power available; the old SET-hold-release-to-NEUTRAL emulation was equally dead. The click commands are the cockpit-clickspot path and work: **Move DOWN = ON/connect, Move UP = OFF/trip** (yes, DOWN is ON), and the SDK springs the switch back to NEUTRAL itself (verified on GRD PWR and APU GEN 1, both directions, watching `ENG_TRANSFER_BUS_OFF`/`APU_GEN_OFF_BUS` — including real 737 last-source-wins bus takeover). The APU GEN SET happened to work in testing, but SETs proved untrustworthy switch-by-switch, so all five route through clicks. They render as stateless On/Off momentary button pairs (`BTN_GRD_PWR_ON`/`_OFF`, `BTN_GEN_n_ON`/`_OFF`, `BTN_APU_GEN_n_ON`/`_OFF`) — the exact PMDG 737 shape — because the switch's resting position is always NEUTRAL and connect state lives in the annunciators. The APU START knob is NOT in this class: `ENGAPU_APU_SET 2` latches a real APU start and springs back on its own. → [ifly-737.md](../ifly-737.md)

## IFLY-4

- **Per-send plugin-window resolution** (`SendCommand`): the WM_COPYDATA target HWND is re-resolved via `FindWindowEx` on EVERY send, never cached — a cached handle can be recycled by Windows after a sim restart, and a send to the recycled window "succeeds" (delivered) while the aircraft hears nothing; the title scan costs microseconds against 40-120 ms-paced commands. Send-failure logging is edge-triggered (`_lastSendFailed`) so a sustained-failure loop logs once, not once per send. → [ifly-737.md](../ifly-737.md)
