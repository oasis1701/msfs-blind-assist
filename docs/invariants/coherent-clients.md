# Coherent debugger clients — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/coherent-clients.md`, which Claude Code loads when it reads matching code. Background: [tooling.md](../tooling.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## A380C-2

- Every Coherent client's `EnsureConnected` must re-install the agent on a still-open socket instead of reconnecting, and must Abort+Dispose any existing socket BEFORE `ConnectAsync` — skipping either orphans a healthy old socket and permanently loses the page for the process. → [a380x.md](../a380x.md)

Moved 2026-10-09 from docs/invariants/a380-coherent.md: the code is every persistent Coherent client's (the A32NX MCDU, flyPad, PMDG EFB, HS787, ND, display and E/WD clients, among others), not only the A380's, and `a380-coherent.md` no longer loads on the other aircraft's clients. The story behind it is in docs/troubleshooting-playbook.md. The ID keeps its prefix.

## A380C-3

- Any public on-demand scrape method competing with a background `RunLoop` must be serialized by its own connect-lock (`_connectLock`) — the existing `_sendLock` only covers `SendAsync`, not connection setup, and doesn't close the race. → [a380x.md](../a380x.md)

Moved 2026-10-09 from docs/invariants/a380-coherent.md: the code is shared, not the A380's alone (`CoherentDisplayClient` serves the A380 RMP and System Display and the HS787, and `CoherentEWDClient` has the same shape), and `a380-coherent.md` no longer loads on the other aircraft's clients. The ID keeps its prefix.

## HS-4

- Each `Resources\coherent-hs787-*-agent.js` needs its OWN explicit `<None Update=...>` csproj entry — the build does NOT wildcard-copy `Resources\`. → [hs787.md](../hs787.md)

Generalised 2026-10-09: the rule holds for every Coherent agent script, not only the HS787's three. Each script is read from disk at run time, from `Path.Combine(AppContext.BaseDirectory, "Resources", <file>)`: by `CoherentDebuggerClient`, `CoherentEFBClient`, `CoherentEWDClient` (the E/WD, ECL and display agents), `CoherentNDClient`, `CoherentDisplayClient`, `CoherentPmdgEfbClient`, `CoherentA32nxMcduClient`, the three `CoherentHS787*Client`s, `FlyByWireDcduForm` and MainForm's A32NX flight-info read. A script the build leaves out of the output folder therefore compiles and passes every test, and fails only when its client starts in the sim. `MSFSBlindAssist.csproj` lists all fifteen `Resources\coherent-*.js` scripts under its "Coherent in-page agents" comment, each a `<None Update>` with `CopyToOutputDirectory` set to `PreserveNewest` (checked 2026-10-09), and `flypad-shell.html` has its own entry too (`Always`). A new agent script takes an entry of its own there. Evidence: the `<None Update="Resources\coherent-…">` block in `MSFSBlindAssist/MSFSBlindAssist.csproj`; the `Path.Combine` reads in the clients named above.

The csproj, where the entries live, loaded no rule file, and the other aircraft's agent scripts loaded no HS787 rule; the rule file now globs both. The ID keeps its prefix. Moved 2026-10-09 from hs787.md and generalised to every Coherent agent script.
