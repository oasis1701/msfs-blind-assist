# PMDG 737-800 NG3 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/pmdg-737.md`, which Claude Code loads when it reads matching code. Background: [pmdg-737.md](../pmdg-737.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## P737-1

- Two CDUs (no observer), no FPA mode, annunciator names differ from the 777 (LVL_CHG/HDG_SEL/VOR_LOC), DU selectors reverse sequence for the F/O, and fire handles need an active fire to test — see docs/pmdg-737.md for the full gotcha list. → [pmdg-737.md](../pmdg-737.md)
