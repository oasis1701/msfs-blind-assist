---
paths:
  - ".claude/hooks/**"
  - ".claude/settings.json"
  - "tests/MSFSBlindAssist.Tests/ClaudeRulesHookTests.cs"
  - "tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs"
---
# Claude Code tooling rules

Loaded when Claude reads matching code. Background: docs/development.md. Full text of each rule: docs/invariants/claude-tooling.md.

- [CCT-1] `rules-hook.ps1` fails open: any error, missing file or unexpected input exits 0 with no output, and `shell-guard` refuses only a write it has positively matched to a covered file. Never let a hook bug block work. Full: docs/invariants/claude-tooling.md#cct-1
- [CCT-2] The hook's glob translation and front-matter parsing stay identical to `ClaudeContextBudgetTests` (`GlobRegex`, `SplitFrontMatter`); the parity test in `ClaudeRulesHookTests` pins it, so change both together. Full: docs/invariants/claude-tooling.md#cct-2
- [CCT-3] After changing a hook's matcher or `if` filter in `.claude/settings.json`, re-run the live checks in docs/development.md: hooks load at session start, and some filters never match (`Write(...)`, redirect forms such as `Bash(cat >*)`). Full: docs/invariants/claude-tooling.md#cct-3
- [CCT-4] The budgets in `ClaudeContextBudgetTests` are this repository's own: raise one only as the owner's decision, after the remedies its failure message lists. Full: docs/invariants/claude-tooling.md#cct-4
