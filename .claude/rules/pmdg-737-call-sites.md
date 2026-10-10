---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG737Definition.cs"
  - "MSFSBlindAssist/SimConnect/PMDGNG3DataManager.cs"
---
# Rules whose code the PMDG 737 definition and its NG3 data manager hold

MIRRORS: copied word for word from first-officer-boeing.md and pmdg-777.md, whose globs leave these files out; the code is the transponder and position-light click walks, the self-test transmit paths and the speed-brake lever. Change both together (ClaudeContextBudgetTests checks).

- [FOB-8] 737 `EVT_TCAS_MODE` (transponder) and `EVT_OH_LIGHTS_POS_STROBE` (position lights) are CDA-deaf walked rotaries that step only on transmit mouse-clicks; probe actuation with `tools/CDUTest cda`, never the simconnect MCP's `send_pmdg_event` (its CDA write silently fails on the NG3). Full: docs/invariants/first-officer-boeing.md#fob-8
- [FOB-9] PMDG self-tests (TCAS/WXR/GPWS) actuate ONLY via transmit press/release: 777 `XPDR_Test` uses a dedicated `HandleUIVariableSet` transmit branch (out of `_simpleEventMap`); WXR is a managed overlay/TEST sequence; the 737 GPWS variant comes from `FOGpws737LongTest`. Their items are `ActionManualAsync`, never Auto (more: see full). Full: docs/invariants/first-officer-boeing.md#fob-9
- [P777-15] Speed-brake ARM is EXACT (`PmdgLeverDetent.Tolerance`, 0.25) on the PMDG 737/777 and iFly 737 MAX, never widened into the settle band; read the 777 lever from `L:switch_498_a` (0-400), NEVER the truncated SDK `FCTL_Speedbrake_Lever` byte. (more: see full) Full: docs/invariants/pmdg-777.md#p777-15
