# FlyByWire A380X systems and panels — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/a380-systems.md`, which Claude Code loads when it reads matching code. Background: [a380x.md](../a380x.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## A380-1

- The annunciator/integral LT-TEST knob is render-only — never synthesize a spoken narration of what the bulbs would show; only announce the knob's own position and let real per-system fault lights announce genuine faults. → [a380x.md](../a380x.md)

## A380-4

- Never treat the Surveillance pedestal panel as a working feature — FBW's own docs say it's not yet implemented; transponder AUTO-mode and squawk are the only real controls, reachable via the MFD SURV page. → [a380x.md](../a380x.md)

## A380-5

- Before assuming an announced ARINC var can't be injection-tested, check whether it has a per-frame writer (Rust systems/rendering instrument) — only vars the writer leaves alone are injectable; real verification of writer-owned vars needs a live scenario. → [a380x.md](../a380x.md)

## A380-7

- Every A380 RMP calc-path write must be made unique per call with a `{seq} 0 *` prefix — MobiFlight's command channel coalesces two consecutive IDENTICAL calc strings, silently dropping a repeated-digit keystroke or a double-press of the same LSK/ADK. → [a380x.md](../a380x.md)

Corrected 2026-10-09: the rule named only the second of the RMP writer's two critical constraints. The first is that a key tap fires PRESS and RELEASE in ONE calculator call (`{seq} 0 * (>H:RMP_n_KEY_PRESSED) (>H:RMP_n_KEY_RELEASED)`): MobiFlight's command channel is a single shared buffer it reads once per frame, so two back-to-back `ExecuteCalculatorCode` calls land in the same frame and the RELEASE overwrites the PRESS before the WASM module processes it, and the key never registers (live-verified: page switch and digit entry only work this way). That is the opposite shape to A320-14's L:var button pulse, which is two calls spaced about 250 ms apart; RMP keys are H-events the WASM module handles, those are L:var writes the FBW Rust sampler reads, and the two must never be harmonised in either direction. The one exception is the Clear HOLD (`SendRmpKeyPress`, then `SendRmpKeyRelease` 1150 ms later from `FBWA380RmpForm`), which is separate calls a second apart; each still carries its own `{seq} 0 *` prefix. Evidence: `FlyByWireA380Definition.SendRmpKey`, `SendRmpKeyPress` and `SendRmpKeyRelease` (`Aircraft/FlyByWireA380Definition.Rmp.cs`, CRITICAL #1 and #2 comments), the Clear-hold call in `Forms/FBWA380/FBWA380RmpForm.cs`, and `BaseAircraftDefinition.PulseMomentaryLVar` for the other shape.

## A380-9

- Seat-motor writes must use a per-frame UNIQUE calc string (`<seq> 0 *` prefix) — identical strings are registered+fired only once by MobiFlight, which is why a naive write only ticked the seat motor once instead of sustaining it. → [a380x.md](../a380x.md)

## A380-11

- Every FBW unit/feature with an observable effect must be wired into MSFSBA's OWN read-outs — MSFSBA bypasses the cockpit displays, so a display-only conversion never reaches the blind pilot unless MSFSBA applies it itself. → [a380x.md](../a380x.md)

## A380-14

- When FBW moves a subsystem, diff EVERY `A32NX_`/`A380X_` token in their tree before vs after the commit and intersect with the def's own references — a spot check of the obvious vars found 6 of the 14 casualties in #10855. **⚠️ But do NOT run that intersection against SOURCE TEXT: a full sweep did, and got 137 A380 + 135 A320 "unresolved" tokens of which ZERO were real.** Four false-positive sources, all of which must be handled: a KEY is not a NAME (`A32NX_FCU_LEFT_EIS_BARO_IS_STD`'s Name is `A32NX_FCU_EFIS_L_DISPLAY_BARO_IS_STD`) — enumerate `GetVariables()` from the BUILT ASSEMBLY and check `Name`, which drops comments and concatenation fragments too; BOTH sides build names dynamically (FBW's Rust indexed families never appear literally); EVENTS are registered in TWO places (`SimConnectInterface.cpp` AND the Rust systems WASM — checking one reports five live A32NX autobrake events as missing); and `SimVar.GetRegisteredId` is NOT an existence oracle (per-view, returns a fresh id for a WASM-written var, polluted by your own probes). SCOPE the diff to a COMMIT ("removed and never restored") — dynamic names never appear as removals. Run four passes a name diff structurally cannot do: CONSTANT WRITERS (`idFoo->set(literal)` or a local only ever assigned one — extend to CONTRIBUTORS of a composed write, which is the only way the A32NX's dead `tcasArmed` bit shows up), CHANGED WRITERS (a surviving name with a new writer can change UNITS/ENCODING silently — this is how V/S announced 500 fpm as "98400"), a LIVE batch read through the Coherent debugger, which may only CLEAR a candidate (non-zero proves existence; zero proves nothing, most vars are legitimately zero in the cruise), and STOCK NAMES — every stock SimVar the def reads and stock K-event it sends, which a token diff cannot see and which never disappear: is the event now MASKED and re-meant, is its input still consumed, does anything still write the var (the #10855 flight-director casualty went unseen for five weeks this way). → [a380x.md](../a380x.md)

## A380-15

- That intersection must cover INPUT EVENTS as well as variables (`Type == SimVarType.Event`, plus every `H:` literal) — a deleted event has NO failure signal at all (the sim silently discards an unregistered K-event while the combo keeps reporting the right state from a still-live read var), which is how #10855's first pass shipped 7 dead A380 controls: heading push/pull, V/S pull and both baro STD/QNH pairs. Pinned by `FlyByWireA380EventContractTests`. → [a380x.md](../a380x.md)

## A380-20

- Annunciators must be stripped from panel DISPLAY variable sets — a pilot navigates a panel to operate controls, not to scan "X Fault: Normal" rows; numeric/analog and 3+-state status fields are kept. → [a380x.md](../a380x.md)
