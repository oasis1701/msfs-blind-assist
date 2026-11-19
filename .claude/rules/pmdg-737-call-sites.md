---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG737Definition.cs"
  - "MSFSBlindAssist/SimConnect/PMDGNG3DataManager.cs"
---
# Rules whose code the PMDG 737 definition and its NG3 data manager hold

MIRRORS: copied word for word from first-officer-boeing.md and wasm-memory.md, whose globs leave these files out; the code is the transponder and position-light click walks, the self-test transmit paths, the announcements WASM-5 and WASM-9 keep beside the module-memory readers, and the Alt+E, Alt+N and Alt+P fallback (WASM-9, WASM-29). Change both together (ClaudeContextBudgetTests checks).

- [FOB-8] 737 `EVT_TCAS_MODE` (transponder) and `EVT_OH_LIGHTS_POS_STROBE` (position lights) are CDA-deaf rotaries the FO walks by transmit mouse-clicks; probe actuation with `tools/CDUTest cda`, never the simconnect MCP's `send_pmdg_event` (its CDA write silently fails on the NG3). Full: docs/invariants/first-officer-boeing.md#fob-8
- [FOB-9] PMDG self-tests (TCAS/WXR/GPWS) actuate ONLY via transmit press/release: 777 `XPDR_Test` uses a dedicated `HandleUIVariableSet` transmit branch (out of `_simpleEventMap`); WXR is a managed overlay/TEST sequence; the 737 GPWS variant comes from `FOGpws737LongTest`. Their items are `ActionManualAsync`, never Auto (more: see full). Full: docs/invariants/first-officer-boeing.md#fob-9
- [WASM-5] The FMA never suppresses or replaces the MCP-light announcements (owner, 2026-10-23); its changes and status lines use the non-interrupting announce. Full: docs/invariants/wasm-memory.md#wasm-5
- [WASM-9] The engine display reader never suppresses or replaces the SDK start-valve, overhead-light, MCP or FMA announcements (owner, 2026-10-23); everything it says by itself uses the non-interrupting announce, and Output Alt+E falls back to the AI read saying nothing ([WASM-29]). Full: docs/invariants/wasm-memory.md#wasm-9
- [WASM-29] With no layout file loaded (no builds), the four readers say only `Pmdg737DisplaySpeech.FilesNotLoaded`, once per module session (`Pmdg737DisplayFilesNotice`), by the first not muted; any other reason keeps a reader's own line. Output Alt+E, Alt+N and Alt+P that cannot read go to the AI read, silent but for the PFD's classic EFIS line. Full: docs/invariants/wasm-memory.md#wasm-29
