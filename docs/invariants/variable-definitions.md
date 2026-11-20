# Aircraft variable definitions — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/variable-definitions.md`, which Claude Code loads when it reads matching code. Background: [aircraft-definitions.md](../aircraft-definitions.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## VAR-1

- Two controls in one panel must never share a `DisplayName`, and where panel rows share a leading phrase the discriminator comes FIRST ("Left Primary Engine Pump", not "Primary Engine Pump Left") — where a family has been bound (so far the PMDG 777's hydraulic pumps, outflow valves, isolation valves, fuel pumps, jettison nozzles and cargo-fire compartments, and the Fenix ACP reception knobs, pinned by `FenixAcpReceptionTests`) a switch and its annunciator are bound from one constant plus a suffix, never typed twice; the 777's remaining ~60 hand-typed `... Light` labels are a known residual, several already drifted from their switch (the list is in aircraft-definitions.md) — bind a pair when you touch it, and never assume a light follows a constant that does not exist. UNIQUENESS is pinned by `VarNameCollisionTests.Panel_rows_do_not_share_a_spoken_name` and `MonitorRowLabelUniquenessTests`; those two only pin that no label repeats — the discriminator-first ORDERING is pinned separately, per family, by exact-string assertions in `Pmdg777AirLabelTests`, `Pmdg777FuelPumpLabelTests`, `Pmdg777HydraulicPumpLabelTests`, and `Pmdg777OutflowValveLabelTests`. → [aircraft-definitions.md](../aircraft-definitions.md)

## VAR-2

- Never register a STOCK SimVar as an L:var. Stock SimVar names usually carry a space or a colon index (`INTERACTIVE POINT OPEN:n`, `LIGHT TAXI:2`), and forcing one through the L:var path corrupts SimConnect registration: force-registering `INTERACTIVE POINT OPEN:n` as an L:var broke A380 detection entirely. The space or colon alone does not decide it, though: an add-on's real L:vars can be colon-indexed (FBW's `A32NX_FUEL_USED:1`, `A32NX_AUTOTHRUST_TLA:1`; A380-26, where the old "any colon = SimVar" rule left the SD fuel pages blank) or contain spaces. Classify by the add-on's own prefix or source, and check what that add-on's L:vars actually look like before copying another aircraft's test: the A32NX's SD loop treats a space as stock (right for FBW, whose L:vars never contain one), and the A380's treats a missing FBW prefix as stock, which holds only because its unprefixed L:vars (`CPT_SLIDING_WINDOW`, `FO_SLIDING_WINDOW`) are declared before the loop. The A32NX (`A32NX_AUTOTHRUST_TLA:1`) and the HS787 (`B787_IRS_Knob_State:1`) register colon-indexed L:vars too; a new aircraft whose L:vars contain a space or a colon says so in its own rules. Writing is separate: today `SetLVar` keeps every space or colon name off the calculator route (SIM-12; for a colon-indexed L:var that is unmeasured, not a rule), so such an L:var gets the native data-def write, which DBG-1 calls unreliable for many add-on L:vars: read the write back before trusting it (DBG-4). → [architecture.md](../architecture.md)

Narrowed 2026-10-07. The original text, verbatim from CLAUDE.md as of `1f37801a`, was: "Never register a name containing a space or colon as an L:var — those are stock SimVars (force-registering `INTERACTIVE POINT OPEN:n` as an L:var broke A380 detection entirely)." It contradicted A380-26 on main, and the space-named L:vars of the A220 and the colon-indexed L:vars of the DA40 in their open PRs (#244, #242).

Corrected 2026-10-08: the one-line form now carries the write half of this rule (`SetLVar`'s data-def write, SIM-12), which it had dropped; this text already said it. Evidence: `SimConnectManager.SetLVar` in `SimConnectManager.EventSend.cs`, whose calculator branch requires a name with no space and no colon.

## VAR-3

- Do NOT delete the per-prefix `ExecuteCalculatorCode` routing in the FBW defs' `HandleUIVariableSet` catch-alls as "redundant" now that `SetLVar` routes globally — they write through the calculator UNCONDITIONALLY, whereas the global routing is gated on a probe proven capable of silent failure. They are why the overhead panel kept working through that outage while the FCU combos did not. Only ~7 of ~71 calc sites in those defs are even that shape; the rest are RPN logic and parameterised K-/H-events. → [architecture.md](../architecture.md)

## VAR-4

- Any VALUELESS calc write (a bare K-event toggle) must go through `ExecuteCalculatorCodeUnique` — repeats are byte-identical and the second is dropped. The wiper circuit toggle is the live example: Off→Slow→Off→Slow silently loses the last step. → [architecture.md](../architecture.md)

## VAR-5

- Every `ExecuteCalculatorCode` call embedding a computed double must use invariant fixed-point formatting — never default `{0}`/`$"{double}"` interpolation; both can emit scientific notation or comma-decimal output the MSFS RPN parser rejects. → [architecture.md](../architecture.md)

## VAR-6

- When adding a Continuous+IsAnnounced background-monitoring variable, do NOT also add it to `BuildPanelControls()` — batched monitoring registration is automatic. The ONE sanctioned exception is a monitored var that IS a panel control's own read-back, where one key must carry both so MainForm's UI-echo suppression matches the pick (the PMDG 737/777 speed-brake levers, `MON_PMDG737_SpeedBrake` / `FCTL_Speedbrake`): it stays batch-covered (setup gives it no individual def), and because its `ProcessSimVarUpdate` returns true an open combo on it does NOT follow a change made elsewhere — it re-reads on the next panel build. The iFly 737 MAX lever (`Spoiler_Lever_Status`) has the same shape on its OWN transport — an SDK shared-memory field, never in a SimConnect batch — and, unlike the PMDG ones, its open combo DOES follow a change, but only at a position: it sets `SimVarDefinition.RefreshControlWhenDefHandled` to a per-value predicate (`IFly737SpeedBrakeLever.IsAtPosition`), which MainForm's def-handled branch honours — never accept the values a lever sweeps through, or a focused combo is narrated at every one. Never add a monitoring var to a panel merely to have it registered. → [architecture.md](../architecture.md)

## VAR-7

- Two var keys may share an underlying `Name` (routine: `PFD_VLS` + `A32NX_SPEEDS_VLS`), but NEVER when both are `Continuous` and batched — the batch sorts by name to mirror SimConnect's ordering, so duplicates shift every later var's struct slot and unrelated readouts return each other's values. Use ONE var and derive extra announcements in `ProcessSimVarUpdate`, or exclude a copy from the batch. Pinned by `VarNameCollisionTests`. → [a380x.md](../a380x.md)

## VAR-8

- The Ctrl+M monitor-manager disabled-var gate must WRAP `ProcessSimVarUpdate` in `announcer.Suppressed` for the HS787, never rely on the generic post-return gate alone — the HS787 announces ~100 of its vars from INSIDE `ProcessSimVarUpdate`, which returns true and skips the generic gate entirely; apply the same wrap to any future aircraft with the same self-announcing pattern. Which list wraps which airframe is `Services/DefAnnounceMuteSets` alone. The FBW A380 joined it on 2026-09-25: it had relied on per-branch checks, and its baro, spoilers, thrust-lever, minimums, autoland-capability and weight-unit rows muted nothing. The wrap assumes a branch speaks only for its OWN row: a branch that also speaks a call-out ANOTHER row owns must be named in `IAircraftDefinition.IsMuteWrapExempt` (checked by `DefAnnounceMuteSets.ShouldWrap`) and check each row itself, or muting its row silences the other one too. The A380's FMA vertical, lateral and armed vertical modes speak "Altitude Mode"; wrapped, muting "Vertical Mode" silenced it (`A380MuteWrapTests`). → [hs787.md](../hs787.md), [a380x.md](../a380x.md)

## VAR-9

- A var that `ProcessSimVarUpdate` consumes SILENTLY (a cache for hotkey readouts or dialog fields, never spoken) must ALSO set `ExcludeFromMonitorManager = true` (HS787: add it to `CacheOnlyVariables`) - otherwise it earns a Ctrl+M checkbox that mutes nothing

Moved word for word from CLAUDE.md's Quick Reference ("Adding Background Monitoring", step 4) as of `6ba751ec`; docs/QUICK-REFERENCE.md keeps the same words in its walkthrough.

## DA40S-3

- **An L:var must be registered `Units = "number"`, never a converting unit** - it holds a RAW NUMBER, so a data definition asking for `inHg`/`percent`/a velocity makes SimConnect convert from its own base unit. The DA40 standby subscale asked in `inHg` and read ZERO while set correctly ("Standby 0 hectopascals, 0.01 inches"); where a scale IS wanted, use `SimVarDefinition.Scale` and leave the unit `number`. Same trap as the A380 TCAS vertical-speed L:vars. -> [da40.md](../da40.md)

Moved here from da40-shared-code.md on 2026-10-11, when main was merged into PR #242: it governs every aircraft definition, the DA40's included, and homed in da40-shared-code.md with a mirror here it loaded twice on the DA40.

## DA40S-4

- `ABS_AMBIENT_TEMPERATURE` is KELVIN and the L:var's Units field is a LABEL, not a conversion — use the `AMBIENT TEMPERATURE` SimVar. → [da40.md](../da40.md)

Moved here from da40-shared-code.md on 2026-10-11, when main was merged into PR #242: it governs every aircraft definition, the DA40's included, and homed in da40-shared-code.md with a mirror here it loaded twice on the DA40.

## DA40S-6

- A non-percentage numeric control must NOT set `RenderAsSlider` — MainForm's `TrackBar` is hardcoded 0-100 and maps the value as a percentage of that range; a key ending `_SET` renders as a typed entry instead. → [da40.md](../da40.md)

- **`SimVarDefinition.HelpText` reaches the pilot on BUTTONS and nowhere else** — `MainForm.PanelBuilder` sets `AccessibleDescription` at exactly one of its eleven control sites, so a combo, typed entry, slider, readout or status row discards it. ⚠️ That was briefly "fixed" by wiring all eleven, and the fix was REVERTED on the pilot's ruling: the DA40's help text was then deleted outright and the rest of the app's fifteen lines are all on buttons, where they already worked. So the one-site render is CORRECT and must stay — do not re-wire the other ten to "rescue" a help line. If a fact cannot be carried by a control's NAME, it belongs in the aircraft's doc, not in a description the pilot has to hunt for. → [da40.md](../da40.md)

Moved here from da40-shared-code.md on 2026-10-11, when main was merged into PR #242: it governs every aircraft definition, the DA40's included, and homed in da40-shared-code.md with a mirror here it loaded twice on the DA40.
