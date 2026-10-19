---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG737Definition.cs"
  - "MSFSBlindAssist/SimConnect/PMDGNG3DataManager.cs"
---
# Rules whose code the PMDG 737 definition and its NG3 data manager hold

MIRRORS: copied word for word from first-officer-boeing.md, whose globs leave these files out; the code is the transponder and position-light click walks and the self-test transmit paths. Change both together (ClaudeContextBudgetTests checks).

- [FOB-8] 737 `EVT_TCAS_MODE` (transponder) and `EVT_OH_LIGHTS_POS_STROBE` (position lights) are CDA-deaf rotaries the FO walks by transmit mouse-clicks; probe actuation with `tools/CDUTest cda`, never the simconnect MCP's `send_pmdg_event` (its CDA write silently fails on the NG3). Full: docs/invariants/first-officer-boeing.md#fob-8
- [FOB-9] PMDG self-tests (TCAS/WXR/GPWS) actuate ONLY via transmit press/release: 777 `XPDR_Test` uses a dedicated `HandleUIVariableSet` transmit branch (out of `_simpleEventMap`); WXR is a managed overlay/TEST sequence; the 737 GPWS variant comes from `FOGpws737LongTest`. Their items are `ActionManualAsync`, never Auto (more: see full). Full: docs/invariants/first-officer-boeing.md#fob-9
