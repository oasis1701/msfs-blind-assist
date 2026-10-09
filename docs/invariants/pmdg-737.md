# PMDG 737-800 NG3 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/pmdg-737.md`, which Claude Code loads when it reads matching code. Background: [pmdg-737.md](../pmdg-737.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## P737-1

- Two CDUs (no observer), no FPA mode, annunciator names differ from the 777 (LVL_CHG/HDG_SEL/VOR_LOC), DU selectors reverse sequence for the F/O, and fire handles need an active fire to test — see docs/pmdg-737.md for the full gotcha list. → [pmdg-737.md](../pmdg-737.md)

## P737-2

- The PMDG 737 CDU form sends every CDU key (letters, LSKs, function keys, CLR/DEL/EXEC) as a `TransmitClientEvent` carrying `MOUSE_FLAG_LEFTSINGLE` (0x20000000), a self-contained press-and-release click, and NEVER as the CDA `{eventId, 1}` write the 777 form uses for most keys. The NG3 FMC ignores the CDA write for CDU keypad events: the click sound plays but nothing registers (the same momentary-button behavior proven for the MCP buttons). A copy of the 777 form's CDA path, or a cleanup that "harmonizes" the two forms, brings back keys that do nothing. The 777's own FMCCOMM and HOLD keys use the same transmit path for a different reason ([P777-6]). A single `LEFTSINGLE` is a complete click; no separate `LEFTRELEASE` is needed. Live-verified against the NG3 (CLR and letter entry) with `tools/CDUTest`. Evidence: `PMDG737CDUForm.SendCDUKey` (`Forms/PMDG737/PMDG737CDUForm.cs`), which calls `SendEventViaTransmitWithTarget`, and "CDU keys must use TransmitClientEvent, not the CDA write" in `docs/pmdg-737.md`. Added 2026-10-09.
