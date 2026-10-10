# TCAS RA guidance (A32NX and A380) — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/tcas-ra.md`, which Claude Code loads when it reads matching code. Background: [a380x.md](../a380x.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## A380-17

- Never leave only the unindexed TCAS RA-guidance vars registered — the fly-to/avoid V/S bands exist ONLY as the `:1`/`:2` indexed L:vars; the unindexed names are never written by FBW. → [a380x.md](../a380x.md)

Moved 2026-10-10 from a380-systems.md (split for the rule budget).

## A380-18

- The TCAS RA-guidance compose must be DEFERRED (~800ms), never synchronous off the state edge — FBW resets the V/S band vars only in TCAS STBY (not on clear-of-conflict), so a synchronous compose at RA-onset can speak the previous RA's stale sense. → [a380x.md](../a380x.md)

Moved 2026-10-10 from a380-systems.md (split for the rule budget).

## A380-19

- The TCAS `VSPEED_GREEN/RED:1/:2` + `RA_RATE_TO_MAINTAIN` L:vars MUST be registered `Units="number"`, NEVER a velocity unit — FBW writes them unitless-but-already-in-fpm, so a "feet per minute" data-def read multiplies by 196.85 (assumes native m/s) and speaks garbage RA numbers (1500 → "295276"); the fpm label is hardcoded in the compose/display, not derived from Units. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

Moved 2026-10-10 from a380-systems.md (split for the rule budget).
