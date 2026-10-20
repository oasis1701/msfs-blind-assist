---
paths:
  - "MSFSBlindAssist/SimConnect/Coherent*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Coherent*.cs"
  - "MSFSBlindAssist/MSFSBlindAssist.csproj"
  - "MSFSBlindAssist/Resources/coherent-*.js"
---
# Coherent debugger client rules

Loaded when Claude reads matching code. Background: docs/tooling.md. Full text of each rule: docs/invariants/coherent-clients.md.

- [A380C-2] Every Coherent client's `EnsureConnected` must re-install the agent on a still-open socket instead of reconnecting, and Abort+Dispose any existing socket before `ConnectAsync`; skipping either loses the page for the process. Full: docs/invariants/coherent-clients.md#a380c-2
- [A380C-3] A public on-demand scrape competing with a background `RunLoop` must be serialized by its own `_connectLock`; `_sendLock` covers only `SendAsync`, not connection setup. Full: docs/invariants/coherent-clients.md#a380c-3
- [HS-4] Every `Resources\coherent-*.js` agent script needs its own explicit `<None Update=...>` entry in `MSFSBlindAssist.csproj`: the build does not wildcard-copy `Resources\`, and each client reads its script from the output folder's `Resources` at run time, so a missing entry fails only in the sim. Full: docs/invariants/coherent-clients.md#hs-4

Mirrored from troubleshooting.md (it governs every Coherent client in SimConnect/Coherent*.cs; change it there and here together):
- [DBG-9] Coherent GT allows only ONE inspector socket per page for ANY aircraft using it — never open a second client against a view another client already holds; share the connection. Full: docs/invariants/troubleshooting.md#dbg-9
