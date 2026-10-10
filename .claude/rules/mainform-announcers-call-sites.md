---
paths:
  - "MSFSBlindAssist/MainForm.Announcers.cs"
---
# Rules whose code MainForm.Announcers.cs holds

MIRRORS: copied word for word from their areas' rule files, whose globs leave this partial out; change both together (ClaudeContextBudgetTests checks).

- [AI-1] ONE AI capture at a time app-wide (`Services/DisplayReadGate.Shared`): `ReadDisplay` AND `MainForm.DescribeSceneAsync` take the same gate, and the holder must RELEASE it before any modal dialog, or every later read answers "already in progress". Full: docs/invariants/ai-display.md#ai-1
- [VG-1] The liftoff cue wording and its mute are one decision in `LiftoffHandoffBreadcrumb.For(...)`; never split them or size a mute by estimate (measure at SAPI `Rate = 0`), and keep every mute under the 3504 ms ceiling by shortening the phrase, never widening the mute (more: see full). Full: docs/invariants/visual-guidance.md#vg-1
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
- [A320-5] The A32NX MCDU runs over Coherent (`CoherentA32nxMcduClient`) with SimBridge as fallback, picked by `FbwMcduTransportArbiter`, which never replays a remembered frame; the client claims its view (`CoherentViewOwnership`), and never add a Captain/FO side selector. (more: see full) Full: docs/invariants/a32nx-mcdu.md#a320-5
- [A320-32] One socket per Coherent view rests on `CoherentViewOwnership`: one-shots (`CoherentEvalClient`) enter via `TryEnterOneShot`; `CoherentA32nxMcduClient` `Claim`s its view Start→Stop, reconnect gaps included, and never connects while `OneShotInFlight`. Never gate a caller on 'holds a socket now?' (D/Shift+D rides `EvalOnMcduViewAsync`). Full: docs/invariants/a32nx-mcdu.md#a320-32
- [VAR-8] A def that announces from inside `ProcessSimVarUpdate` needs the `announcer.Suppressed` wrap for Ctrl+M mutes (which airframes: `Services/DefAnnounceMuteSets` alone); a branch speaking a call-out ANOTHER row owns must be in `IsMuteWrapExempt` and check each row itself. Full: docs/invariants/variable-definitions.md#var-8
- [SUR-17] Every spoken line of a surroundings lookup goes through `SpeakLookupLine` (`SurroundingsLookupNotice.Delivery`, timed from the PRESS); the catalog build starts before the Where-Am-I line and runs beside it, and "Looking around." waits `NoticeWait` from the press: never re-time from the position callback or a build's start. Full: docs/invariants/surroundings.md#sur-17
- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/gsx-docking.md#dck-34
