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
- [CCT-3] After changing a hook's matcher or `if` filter in `.claude/settings.json`, re-run the live checks in docs/development.md in a fresh session: filters fail silently, since some never match (`Write(...)`, `Bash(cat >*)`) and one naming more than the command (`Bash(git diff*)`) runs on every command holding `$VAR` or `$()`. Full: docs/invariants/claude-tooling.md#cct-3
- [CCT-4] The budgets in `ClaudeContextBudgetTests` are this repository's own: raise one only as the owner's decision, after the remedies its failure message lists. Full: docs/invariants/claude-tooling.md#cct-4
- [CCT-5] Keep every hook output within `$ContextBudget` (9,000 characters): Claude Code saves hook context over about 10,000 characters to a file and shows the model a 2 KB preview, so show whole rule files that fit and name the rest for Claude to Read. Full: docs/invariants/claude-tooling.md#cct-5
- [CCT-6] `subagent-start` recognises the session's own worktree by the folder Claude Code files its transcripts under (`transcript_path`), never by `CLAUDE_PROJECT_DIR`, which is the main checkout in a desktop-app worktree session; with no `transcript_path` it warns nothing, and the Plan agent's CLAUDE.md comes from its own checkout. Full: docs/invariants/claude-tooling.md#cct-6
- [CCT-7] The shell guard refuses a Python script only for a write sink (`open` with a w/a/x/+ mode, `write_text`/`write_bytes`, a copy, move or replace destination) whose target resolves from literals, through a name's nearest earlier binding, to a covered file; a script that only reads covered files, or whose target it cannot resolve, always runs. Full: docs/invariants/claude-tooling.md#cct-7
