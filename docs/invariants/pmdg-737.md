# PMDG 737-800 NG3 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/pmdg-737.md`, which Claude Code loads when it reads matching code. Background: [pmdg-737.md](../pmdg-737.md).
The text is verbatim from CLAUDE.md as of `1f37801a` (P737-3 and P737-4, the latter numbered P737-2 there, from PR #160's as of `642f48c1`; P737-2 is main's, added 2026-10-09); a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## P737-1

- Two CDUs (no observer), no FPA mode, annunciator names differ from the 777 (LVL_CHG/HDG_SEL/VOR_LOC), DU selectors reverse sequence for the F/O, and fire handles need an active fire to test — see docs/pmdg-737.md for the full gotcha list. → [pmdg-737.md](../pmdg-737.md)

## P737-2

- The PMDG 737 CDU form sends every CDU key (letters, LSKs, function keys, CLR/DEL/EXEC) as a `TransmitClientEvent` carrying `MOUSE_FLAG_LEFTSINGLE` (0x20000000), a self-contained press-and-release click, and NEVER as the CDA `{eventId, 1}` write the 777 form uses for most keys. The NG3 FMC ignores the CDA write for CDU keypad events: the click sound plays but nothing registers (the same momentary-button behavior proven for the MCP buttons). A copy of the 777 form's CDA path, or a cleanup that "harmonizes" the two forms, brings back keys that do nothing. The 777's own FMCCOMM and HOLD keys use the same transmit path for a different reason ([P777-6]). A single `LEFTSINGLE` is a complete click; no separate `LEFTRELEASE` is needed. Live-verified against the NG3 (CLR and letter entry) with `tools/CDUTest`. Evidence: `PMDG737CDUForm.SendCDUKey` (`Forms/PMDG737/PMDG737CDUForm.cs`), which calls `SendEventViaTransmitWithTarget`, and "CDU keys must use TransmitClientEvent, not the CDA write" in `docs/pmdg-737.md`. Added 2026-10-09.

## P737-3

- The 737 manual warning-test panel toggles (stick shaker / overspeed clacker) actuate via transmit LEFTSINGLE (engage, holds open-ended) / LEFTRELEASE (release) — never a CDA write; state is app-tracked in the def (no SDK readback), the keys stay OUT of `_simpleEventMap`, and `SwitchAircraft` must release any engaged test so it can't leak into the next aircraft. → [pmdg-737.md](../pmdg-737.md)

  **App exit too (2026-10-18).** `MainForm.OnFormClosing` releases an engaged test as well, before `Disconnect()`, while the sim can still receive the release; until then only the swap path did, so a test held when MSFSBA closed kept the stick shaker or clacker sounding in the sim. The exit path also runs the definition's `OnSwitchedAway`, the swap path's other teardown hook.

## P737-4

- A third write transport exists for PMDG switches, the stock `K:ROTOR_BRAKE` event (66587) with `param = (pmdgEventId - 69632) * 100 + mouseCode` — this is NOT a new discovery: `PMDG777Definition.cs:6188-6209` already drives three 777 soundpack switches through it (crediting the FSCopilot 777 profile), including the same wheel codes (07/08). The 2026-08-25 737 session independently re-confirmed mouse code 01 = left-single live on the 737 speedbrake (679201/679101 arm/disarm) — check here before rediscovering it; it does NOT rescue the gear lever. → [pmdg-737.md](../pmdg-737.md)

Numbered P737-2 on PR #160 until 2026-10-09, when main's #280 gave P737-2 to the CDU transmit rule; renumbered P737-4 when main was merged into the PR. The text is unchanged.
