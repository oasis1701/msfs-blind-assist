# Takeoff assist and flight callouts — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/takeoff-and-callouts.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## TKO-1

- Takeoff Assist's `Toggle(off)` must unconditionally clear the runway reference — within-session preservation let a turnaround flight silently reuse flight 1's runway on flight 2's CTRL+T; the teleport dialog path still sets the reference unconditionally so teleport always wins. → [taxi-guidance.md](../taxi-guidance.md)

## TKO-2

- Auto-activate-Takeoff-Assist-on-lineup is a one-shot latch (`_autoActivateFired`) and must NOT reset on lineup drift-out — re-engaging after a deliberate manual deactivation would surprise the pilot. → [taxi-guidance.md](../taxi-guidance.md)

## TKO-3

- GroundSpeedAnnouncer is mode-independent (every on-ground phase: taxi, takeoff roll, landing rollout) — do NOT move it back into a per-mode manager like TaxiGuidanceManager; it stopped the instant takeoff-assist/touchdown took over when it lived there. → [taxi-guidance.md](../taxi-guidance.md)

## TKO-4

- The 1,000-ft altitude callout (`AltitudeCalloutAnnouncer`) must fire AT the boundary (the band change IS the crossing), not ~80 ft late; and it announces the thousand CROSSED (same value climbing or descending), not the band entered. Re-crossing the LAST-announced thousand is ALWAYS silent with NO distance window (turbulent-cruise excursions step >80 ft between samples, so a window can't be trusted — user ruling: never re-announce); it re-arms only when a different thousand announces or on the ground/teleport reset (which must clear the announced-thousand latch too, or the next flight's climb-out swallows it). → [taxi-guidance.md](../taxi-guidance.md)

## TKO-5

- The take-off callouts (MD-11, iFly and — since 2026-09-25 — FBW A380, the shared `TakeoffVSpeedCallouts`) are spoken as ONE `AnnounceImmediate` per sample: `TakeoffVSpeedCallouts.Compose` joins the calls one sample crossed ("V1, Rotate" when V1 = VR, routine on a limiting runway), leaving out a Ctrl+M-muted call and returning nothing when all are muted. Never speak them one call each: `AnnounceImmediate` cuts off whatever is already being spoken, on every output path, so the second call cut the first off on every such take-off. EVERY definition drops the ARM on a reconnect (`ResetAnnouncementBaselines`) AND on every context reset (`OnSimContextReset`), never the speeds: the flight-load hazard — a per-frame IAS delivered ahead of the 1 Hz ground flag, so an arm kept from a parked aircraft calls V1/Rotate/V2 at the loaded cruise — belongs to the shared machine, not to one airframe, and the iFly carried only the reconnect half until 2026-09-11. A second user of a shared announcer machine needs every reset the first one has. The A380's (`A380TakeoffCallouts`) reads the FMS's `AIRLINER_V1/VR/V2_SPEED` through the existing V-speed rows — peeked, never consumed, so "V1: 142 knots" as they are typed is unchanged, and each call is muted by its speed's row — and airspeed on its own per-frame subscription. All three name their feed and speeds through ONE `TakeoffCalloutKeys` (the each-call-muted-by-its-speed's-row rule, fail open for a call with no row), never a hand-kept copy per airframe. The per-frame feed is PAUSED while `TakeoffVSpeedCallouts.NeedsSamples` is false — airborne with no roll armed — and resumed at touchdown (`IAircraftDefinition.TakeoffCalloutFeedKey`/`TakeoffCalloutFeedNeeded`, `MainForm.UpdateTakeoffCalloutFeed`, `SimConnectManager.SetSimFrameSubscriptionActive`); never gate it on the ground flag alone — V2 routinely completes after liftoff, and a roll still armed in the air must keep its samples. → [md11.md](../md11.md), [a380x.md](../a380x.md)
