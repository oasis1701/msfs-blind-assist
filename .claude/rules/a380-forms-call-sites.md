---
paths:
  - "MSFSBlindAssist/Forms/FBWA380/**"
---
# Coherent socket rule for the A380 forms

MIRRORS: copied word for word from troubleshooting.md, whose globs leave the A380 forms that build Coherent clients out; change both together (ClaudeContextBudgetTests checks).

- [DBG-9] Coherent GT allows only ONE inspector socket per page for ANY aircraft using it — never open a second client against a view another client already holds; share the connection. Full: docs/invariants/troubleshooting.md#dbg-9
