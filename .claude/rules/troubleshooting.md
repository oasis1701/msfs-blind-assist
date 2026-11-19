---
paths:
  - "MSFSBlindAssist/Aircraft/**/*Definition*.cs"
---
# Troubleshooting a control rules

Loaded when Claude reads matching code. Background: docs/troubleshooting-playbook.md. Full text of each rule: docs/invariants/troubleshooting.md.

- [DBG-1] Never conclude "doesn't work" from the MCP `set_lvar` tool (native data-def write) — it is unreliable for many FBW/add-on L:vars; always test writes via the calculator/MobiFlight path. Full: docs/invariants/troubleshooting.md#dbg-1
- [DBG-2] `L:NAME` and `L:1:NAME` are DIFFERENT variables — mirror whichever form the FBW source reads; the unprefixed write sticks on read-back yet the systems never see it, and `SetLVar` only emits the unprefixed form. Full: docs/invariants/troubleshooting.md#dbg-2
- [DBG-3] Never "verify" a 9-digit ECAM code by injecting it through the MobiFlight calc path — it is float32 (`310015001` lands as `310015008`); trigger the real condition or rely on the lookup unit tests. Full: docs/invariants/troubleshooting.md#dbg-3
- [DBG-4] A control can be fully working with NO audible/visible feedback — confirm by READ-BACK after ~1-2 s, never by "I can't tell if it did anything." Full: docs/invariants/troubleshooting.md#dbg-4
- [DBG-5] Read back the DOWNSTREAM effect (stock simvar/system output), not just the L:var you wrote: a dead mirror holds a write but drives nothing, and a real mirror can falsely pass a stickiness test written to its current value. Full: docs/invariants/troubleshooting.md#dbg-5
- [DBG-6] Test every state of a multi-position control, not just 0→1 — a control can stick at one value and revert at another. Full: docs/invariants/troubleshooting.md#dbg-6
- [DBG-7] The reliable existence test for an add-on control is its own SOURCE (cockpit XML/behavior template + a systems-side reader); the write-stick test alone passes even on a nonexistent variable. Full: docs/invariants/troubleshooting.md#dbg-7
- [DBG-8] Never assume a DOM/WebView control needs the visible widget you'd click — trace what the real cockpit hardware input writes underneath (an ECL via push-button L:var pulses, an MFD via keypress events). Full: docs/invariants/troubleshooting.md#dbg-8
- [DBG-9] Coherent GT allows only ONE inspector socket per page for ANY aircraft using it — never open a second client against a view another client already holds; share the connection. Full: docs/invariants/troubleshooting.md#dbg-9
- [DBG-10] A `_PRESSED`/`_Pressed`-style XMLVAR is almost always a model-only press-ANIMATION flag, not the real actuator — find the real K-event or state var instead of pulsing it. Full: docs/invariants/troubleshooting.md#dbg-10
- [DBG-11] BEFORE diagnosing any reported A380/MSFSBA behaviour, CHECK WHICH BINARY IS RUNNING (`startup.log`'s Application Directory, the DLL's ProductVersion commit); `debug.log`'s `Sending event:` lines show what the app actually emitted. Full: docs/invariants/troubleshooting.md#dbg-11
- [DBG-12] Never conclude anything about a PMDG control from the simconnect MCP's `send_pmdg_event`: its parameterless form coalesces identical consecutive events, and `MOUSE_FLAG_LEFTSINGLE` moves a lever without the stock mirrors following. Probe with the stock `K:ROTOR_BRAKE` transport instead. Full: docs/invariants/troubleshooting.md#dbg-12
- [DBG-13] MCP-probing gotcha (probing only): PMDG `send_pmdg_event` WITH a parameter is a silent no-op on the 737/777, WITHOUT one it acts as a coalescing TOGGLE; MSFSBA's own `TransmitClientEvent`-with-parameter path is absolute and idempotent, so draw no dispatch conclusions from the MCP. Full: docs/invariants/troubleshooting.md#dbg-13
