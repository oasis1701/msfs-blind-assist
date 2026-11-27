---
paths:
  - "tools/coherent-coverage.js"
---
# Coherent debugger tooling rules

Loaded when Claude reads matching code. Background: docs/tooling.md.

Mirrored from da40-shared-code.md (it governs the shared coverage tool; change it there and here together):
- [DA40S-10] Use `tools/coherent-coverage.js` before claiming any Coherent display is fully read: its visibility test walks the ancestor chain and chrome is reported once; it reports candidates, not verdicts. Full: docs/invariants/da40-shared-code.md#da40s-10
