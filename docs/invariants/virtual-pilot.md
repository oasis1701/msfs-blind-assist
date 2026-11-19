# VirtualPilot harness hooks — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/virtual-pilot.md`, which Claude Code loads when it reads matching code. Background: [virtual-pilot.md](../virtual-pilot.md).
These rules came with the taxi/landing port (`feat/taxi-landing-port`) and are verbatim from that branch's CLAUDE.md as of `4854ec3d`; a trailing "→ doc" pointer is the original's.

## VP-1

- The VirtualPilot hooks (`SimClock.Override`, `TaxiSteeringTone.Headless`, `Log.Redirect`, the `…ForHarness` accessors and `Legacy…ForHarness` A/B switches) are internal and must stay INERT in the app — never set one outside `tools/VirtualPilot`. Any new clock read in the taxi/rollout guidance must go through `SimClock.UtcNow`, or the harness's simulated seconds silently stop applying to it. → [virtual-pilot.md](../virtual-pilot.md)
