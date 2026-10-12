# Stands and docking: domain boundary and stop position — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/gsx-stands-docking.md`, which Claude Code loads when it reads matching code. Background: [gsx.md](../gsx.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## DCK-1

- Domain boundary with PR #84: the docking and positioning code owns POSITIONING only (gate/stand selection, docking geometry, deice positioning) — never add live service-state logic to it, and docking must never read service vars. Live GSX service state and its announcements belong to Access GSX (`GsxService`, `GsxServiceAnnouncer` and their kin), which GSX-16, GSX-18 and GSX-19 govern. → [gsx.md](../gsx.md)

Corrected 2026-10-08: "this codebase" became "the docking and positioning code", as docs/gsx.md scopes the boundary; the app does announce GSX service state, through Access GSX. Evidence: the "Domain boundary with PR #84" paragraph in docs/gsx.md, and `Services/Gsx/Remote/GsxServiceAnnouncer`.

## DCK-3

- GSX gate spot-position priority is `this_parking_pos` → navdata → stop position as LAST resort — the GSX stop position is a VDGS nose-stop reference, not an aircraft-datum location; using it as the spot position teleports the datum into the stand. → [gsx.md](../gsx.md)

## DCK-4

- The `.py` per-aircraft stop offset must apply to ALL non-deice gates including `.ini` gates — skipping it for `.ini` gates left every `.ini`-airport 777 parking ~5m short. → [gsx.md](../gsx.md)

## DCK-5

- `GsxOffset.Zero` must be a strict no-op (skip the shift entirely) — any resolver miss at any layer must degrade to Zero, never throw or half-apply. → [gsx.md](../gsx.md)

## DCK-16

- Docking's forward distance math must stay datum-aligned — never reintroduce the per-aircraft `gsx.cfg` longitudinal door offset into the stop math; it describes door position on the airframe, not a stop offset, and parked a B777 ~26m short when subtracted. → [gsx.md](../gsx.md)

## DCK-26

- Docking's taxi-away disengage must use ABSOLUTE distance, never along-track — along-track goes negative once the stop is behind the aircraft and can never trip for a forward taxi-out. → [gsx.md](../gsx.md)

## DCK-31

- The runway-style stopped-misaligned pulse must NEVER be re-added to gate lineup — precision parking is docking's job; pulsing 3Hz at a correctly-parked pilot demanding precision to a possibly-offset navdata point is a misfeature. → [gsx.md](../gsx.md)

## DCK-39

- The Remote API does NOT publish the docking stop position (`stopPosition`/`objectPosition` are null on every parking) and exposes no method-invocation verb to compute one, so docking's stop geometry still comes from GSX's `.ini`/`.py` profile files (`GsxProfileLocator`/`GsxStopOffsetResolver`/`GsxPyOffsetEvaluator`/etc., all unchanged) — do NOT "finish the migration" by sourcing the stop from the API's `lat`/`lon`, which sit ~11.6 m from the real VDGS stop point, far outside docking's 0.3 m `StopToleranceMetres`. → [gsx.md](../gsx.md)
