# PMDG 737-800 NG3 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/pmdg-737.md`, which Claude Code loads when it reads matching code. Background: [pmdg-737.md](../pmdg-737.md).
The text is verbatim from CLAUDE.md as of `1f37801a` (P737-2 and P737-3 from PR #160's as of `642f48c1`); a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## P737-1

- Two CDUs (no observer), no FPA mode, annunciator names differ from the 777 (LVL_CHG/HDG_SEL/VOR_LOC), DU selectors reverse sequence for the F/O, and fire handles need an active fire to test — see docs/pmdg-737.md for the full gotcha list. → [pmdg-737.md](../pmdg-737.md)

## P737-2

- A third write transport exists for PMDG switches, the stock `K:ROTOR_BRAKE` event (66587) with `param = (pmdgEventId - 69632) * 100 + mouseCode` — this is NOT a new discovery: `PMDG777Definition.cs:6188-6209` already drives three 777 soundpack switches through it (crediting the FSCopilot 777 profile), including the same wheel codes (07/08). The 2026-08-25 737 session independently re-confirmed mouse code 01 = left-single live on the 737 speedbrake (679201/679101 arm/disarm) — check here before rediscovering it; it does NOT rescue the gear lever. → [pmdg-737.md](../pmdg-737.md)

## P737-3

- The 737 manual warning-test panel toggles (stick shaker / overspeed clacker) actuate via transmit LEFTSINGLE (engage, holds open-ended) / LEFTRELEASE (release) — never a CDA write; state is app-tracked in the def (no SDK readback), the keys stay OUT of `_simpleEventMap`, and `SwitchAircraft` must release any engaged test so it can't leak into the next aircraft. → [pmdg-737.md](../pmdg-737.md)
