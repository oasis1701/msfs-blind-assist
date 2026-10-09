# Coherent debugger clients — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/coherent-clients.md`, which Claude Code loads when it reads matching code. Background: [tooling.md](../tooling.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## A380C-2

- Every Coherent client's `EnsureConnected` must re-install the agent on a still-open socket instead of reconnecting, and must Abort+Dispose any existing socket BEFORE `ConnectAsync` — skipping either orphans a healthy old socket and permanently loses the page for the process. → [a380x.md](../a380x.md)

Moved 2026-10-09 from docs/invariants/a380-coherent.md: the code is every persistent Coherent client's (the A32NX MCDU, flyPad, PMDG EFB, HS787, ND, display and E/WD clients, among others), not only the A380's, and `a380-coherent.md` no longer loads on the other aircraft's clients. The story behind it is in docs/troubleshooting-playbook.md. The ID keeps its prefix.

## A380C-3

- Any public on-demand scrape method competing with a background `RunLoop` must be serialized by its own connect-lock (`_connectLock`) — the existing `_sendLock` only covers `SendAsync`, not connection setup, and doesn't close the race. → [a380x.md](../a380x.md)

Moved 2026-10-09 from docs/invariants/a380-coherent.md: the code is shared, not the A380's alone (`CoherentDisplayClient` serves the A380 RMP and System Display and the HS787, and `CoherentEWDClient` has the same shape), and `a380-coherent.md` no longer loads on the other aircraft's clients. The ID keeps its prefix.
