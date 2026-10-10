---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.PanelControls.cs"
---
# Radios-panel rule for the A380 panel controls

MIRRORS: copied word for word from a380-coherent.md, whose globs leave this partial out; it built the removed stock-COM "Radios" panel the rule forbids re-adding (its panel and display lists say so). Change both together (ClaudeContextBudgetTests checks).

- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
