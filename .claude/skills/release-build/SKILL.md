---
name: release-build
description: Use when the user wants a local Release build of MSFS Blind Assist compiled and verified. Local only - never tags or publishes a GitHub release.
disable-model-invocation: true
---

# Local Release Build

Produce the local Release build and report the result:

1. Run `scripts/build-release.ps1` (PowerShell tool, or `pwsh -NoProfile -File scripts/build-release.ps1` via Bash) from the repo root. The script owns all build and verification logic — do not hand-roll `dotnet build` here.
2. Relay to the user: the `RELEASE BUILD OK.`/`RELEASE BUILD FAILED` line, the exe path and written timestamp, and every `WARNING` or `Note` line, verbatim.
3. If the script failed and mentioned MSB3021 / a locked file: MSFS Blind Assist is still running. Ask the user to close it, then run the script again.
4. This command is LOCAL ONLY. Creating a `v*` tag or GitHub release (`.github/workflows/release.yml`) publishes to all users via the auto-updater and happens only on a separate, explicit user request.
