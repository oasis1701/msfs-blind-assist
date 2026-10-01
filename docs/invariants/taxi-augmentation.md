# Online taxi-data augmentation — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/taxi-augmentation.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## AUG-1

- Taxi-data augmentation: navdata is AUTHORITATIVE — an existing navdata taxiway/gate name is never overwritten, online names only fill UNNAMED segments, and online-only geometry is IGNORED (never steer on an offset online line). → [taxi-guidance.md](../taxi-guidance.md)

## AUG-2

- Every non-null assignment of `MainForm.airportDataProvider` goes through `MainForm.WithTaxiAugmentation` (pinned by `ProviderWrapGuardTests`): `RefreshDatabaseProvider` once assigned the raw provider, so after any Database Settings visit or database auto-switch the session silently lost online taxiway names, aliases and the route briefing's OpenStreetMap tier. The online-data cache is shared across the switch (online data only, never navdata). The same switch carries the flight plan over with `FlightPlan.CopyFrom` — every property, the SimBrief text and aircraft fields included (`FlightPlanCopyTests`); a field-by-field copy dropped `ExtractedFlightData` and left Describe Route disabled until SimBrief was reloaded. That is a SWITCH (`RefreshDatabaseProvider`); a database BUILD first runs `CloseDatabaseConnections`, which releases the provider through `WithTaxiAugmentation(null)` (so the augmenting decorator goes with it) and drops the flight plan manager, so no plan is carried over a build. → [taxi-guidance.md](../taxi-guidance.md)

## AUG-3

- Taxi-data augmentation is anti-grass: online data NEVER sets a gate Name/position and NEVER adds a selectable gate — gate identity is authoritative from GSX/navdata, online contributes searchable aliases only. → [taxi-guidance.md](../taxi-guidance.md)

## AUG-4

- Do NOT implement OSM `holding_position` hold-short sharpening without an explicit sim-verified ask — augmentation stays NAME-only and must never touch the tuned, safety-critical hold-short placement. → [taxi-guidance.md](../taxi-guidance.md)
