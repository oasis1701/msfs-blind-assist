---
paths:
  - "MSFSBlindAssist/MainForm.PanelBuilder.cs"
---
# Aircraft panel rules whose code MainForm.PanelBuilder.cs holds

MIRRORS: copied word for word from pmdg-777.md, a32nx-fenix.md, a380-systems.md and a380-coherent.md, whose globs leave this partial out; change both together (ClaudeContextBudgetTests checks).

- [P777-10] MainForm's PMDG panel-populate loop must force-read only `Type == PMDGVar` controls: `GetFieldValue` on a non-PMDG control (e.g. an LVar combo) returns the 0.0 "unknown field" sentinel and silently resets it on every panel re-show. Full: docs/invariants/pmdg-777.md#p777-10
- [A320-27] A32NX nose/landing lights use the indexed stock events in the FBW template's verbatim RPN form `<value> <index> r (>K:2:LANDING_LIGHTS_SET/TAXI_LIGHTS_SET)`; the `LIGHTING_LANDING_x` L:vars drive nothing. Keep the template-verbatim form. Full: docs/invariants/a32nx-fenix.md#a320-27
- [A380-24] Multi-position switches stay ONE combo, never On/Off pairs: Nose light T.O./Taxi/Off (actuated by indexed `LANDING_LIGHTS_SET`/`TAXI_LIGHTS_SET`; `LIGHTING_LANDING_1` only mirrors it), Seat Belts ON/AUTO/OFF (always write `XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position`, AUTO's input; ON/OFF add `CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE`). Full: docs/invariants/a380-systems.md#a380-24
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
