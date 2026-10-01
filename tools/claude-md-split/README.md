# claude-md-split

One-off tooling from the 2026-10 move of CLAUDE.md's invariants into path-scoped rule files
(design: [docs/design/2026-09-30-lean-claude-md-design.md](../../docs/design/2026-09-30-lean-claude-md-design.md)).
Kept so a reviewer can re-run it; neither script is part of the build.

- `split.py` reads CLAUDE.md as it stood at `1f37801a`, assigns every invariant bullet to an area and
  writes `docs/invariants/<area>.md` (verbatim) plus a `.claude/rules/<area>.md` skeleton per area.
  `--dry-run` lists every assignment.
- `verify_moved.py` proves every invariant bullet and every paragraph that left the core of that
  CLAUDE.md appears verbatim somewhere in the repository now.

Adding a rule today needs neither script: see "Adding or changing a rule" in CLAUDE.md.
