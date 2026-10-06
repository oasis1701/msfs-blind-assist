# First Officer — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/first-officer.md`, which Claude Code loads when it reads matching code. Background: [first-officer.md](../first-officer.md).
The text is verbatim from PR #160's CLAUDE.md as of `642f48c1`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below" or "above" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## FO-1

- Screen-reader First Officer (flows + checklists) across seven aircraft via `IFoProfile<TExec,TState>`; the form is identical, the profile injects executor/evaluator/data. Full architecture + gotchas in the doc. → [first-officer.md](../first-officer.md)

## FO-2

- No FMC programming, ever (deliberate user decision) — keep checklist text + V-speed reads + SimBrief load-only; never reintroduce CDU-keystroke automation. → [first-officer.md](../first-officer.md)

## FO-3

- `*_CL` readback groups are action-free (no item has a non-null `CheckAction`); state/action groups fire the switch on tick and auto-detect from live state. → [first-officer.md](../first-officer.md)

## FO-4

- The First Officer's "<flow> flow complete" (`FlowManager`) is NON-INTERRUPTING (`Announce`), never `AnnounceImmediate` — it runs straight after the last step, and `AnnounceImmediate` interrupts the screen reader and cancels its buffered speech, so the last step of a flow was routinely cut off on every aircraft: every flow-final Captain reminder, and worst of all a skipped wait's "Timed out waiting for… / Skipping…" (the PMDG 737 gear checks), which a blind pilot heard as silence and then "flow complete": success (owner decision 2026-09-22). FlowManager itself is not unit-testable (its ScreenReaderAnnouncer drives a real screen reader), so it is pinned by the source-text guard `FlowManager_FlowComplete_IsNonInterrupting` in `FoPr160ProcedureFixTests`. → [first-officer.md](../first-officer.md)

## FO-5

- `FlowManager`'s 2 s inter-step pause (`InterStepPauseMs`, includes "Already set" skips) is deliberate realism pacing for a screen-reader pilot — never remove or shrink it as an optimization; it layers on top of executor write spacing. → [first-officer.md](../first-officer.md)

## FO-6

- A flow step's checklist links are `FlowStep.LinkedChecklistItemIds` (`CompletesChecklistItemId` first, then `AlsoCompletesChecklistItemIds`): `FlowManager` marks — and on a SKIP excludes from the group latch — EVERY linked id, never only `CompletesChecklistItemId`. A WRITE step must link the line it sets, or a failed write is still latched done when the flow finishes — and "the lines a flow latches" is `FlowDefinition.CompletionGroupIds` (the flow's `RelatedChecklistGroupIds` PLUS the group named after the flow and its `_CL` read-back, the ONE rule `FirstOfficerForm` itself uses), never `RelatedChecklistGroupIds` alone: auditing that alone missed twelve 737 read-back lines. The PMDG 737, PMDG 777 and iFly 737 MAX8 link every switch-write step, each pinned by a `*FlowChecklistLinkTests` audit over the shared `FlowChecklistLinkAudit` (derives each step's lines from the fields its writes drive, and pins the short list of lines still latched with no step behind them); the engine-start SELECTOR lines are never linked (their StayComplete exception above), nor is a plan-gated step family whose non-planned siblings skip as "Already set" (777 Before Taxi flaps). The Fenix, FBW A32NX/A380 and Headwind A330 flows have NOT been audited this way — a known gap. The 777 flap-lever events are keyed by DEGREES (`_0/_1/_5/_15/_20/_25/_30`), not the 737's detent indices; a 777 FO write to an event missing from `PMDG777Definition.EventIds` fails at dispatch (`Every_written_event_is_in_the_777_event_table`). → [first-officer.md](../first-officer.md)

## FO-7

- **A checklist item with no flow step behind it is still TICKED AND LATCHED when its flow finishes** (`ChecklistManager.MarkGroupComplete` excludes only steps that FAILED), so every actionable item in a state group needs a delivering step or it becomes a silent false completion — `BS_TRANSPONDER` read "Transponder: XPNDR" complete with the selector untouched. Adding a step is the fix; never rely on the exclusion set, which a missing step never reaches. → [first-officer.md](../first-officer.md)

## FO-8

- A completed checklist group with participation (a user manual tick or a flow MarkComplete) is completion-LATCHED — RevertToState must not un-tick it until reset/manual untick; never latch on coincidental auto-ticks alone, and never reintroduce item-level StayComplete or an unconditional 100% latch. `MarkGroupComplete`'s group latch is UNCONDITIONAL even when a flow's `excludeItemIds` names a step it could not deliver — the phase's completed steps stay a flight-long historical record — and the one undelivered item is exempted PER-ITEM via `ChecklistItem.ExemptFromCompletionLatch` so IT ALONE keeps mirroring live state inside the otherwise-latched group; this is NOT the item-level StayComplete the rule above forbids (that would freeze every item, not free one). The exemption is set whenever the flow did not itself deliver the item — un-checked, OR checked only via a pilot hand-tick still `AwaitingActionConfirmation` — and its clear must be UNCONDITIONAL, never only when that same call is the one that ticks the item: a hand-tick can check the item before a later pass (state agreeing, or a real re-delivery) tries to clear the exemption, and gating the clear on `!IsChecked` leaves it permanently exempt despite doing exactly what it was supposed to do. → [first-officer.md](../first-officer.md)

## FO-9

- A checklist item the pilot hand-ticks that fires a linked `CheckAction` sets `AwaitingActionConfirmation`; if `RevertToState` later un-ticks it because the switch never actually moved, `ChecklistManager` raises `ItemActionFailed` and the form speaks `"Unable to complete: {label}"` — the only channel that reaches a blind pilot who has navigated away from the checklist tree, where the correction is otherwise silent and visual-only. Never raised for an ordinary revert (the pilot moved the switch back themselves), and it fires from inside a latched group exactly like an unlatched one whenever the item is exempted. → [first-officer.md](../first-officer.md)

## FO-10

- Checklist revert grace is action-aware: `ChecklistManager.RunCheckActionWithGraceAsync` holds revert until the tick's action completes AND the executor's dispatch gate drains (`IFoActionExecutor.WaitForDispatchDrainAsync`), covering the multi-second closed-loop selector walks (transponder / position lights) and anything queued behind them. → [first-officer.md](../first-officer.md)

## FO-11

- **A checklist item whose state field is a SLOW condition needs its CheckAction to hold open until that condition is true — the 10 s `ManualTickGrace` will not save it.** `ChecklistManager`'s own remarks say so ("SLOW actions are covered by ActionSettling, not by inflating this constant") and the Fenix APU is the case that proves it: `BS_APU`/`AL_APU` detect on the AVAIL lamp, which lights only when the APU reaches ~95% N about 45 s after START, while `StartApuAsync` returned at the START pulse. That left ~13 s of grace against a ~45 s condition, so a hand-tick un-ticked itself, `ItemActionFailed` fired, and the pilot heard *"Unable to complete: APU: ON and available"* over an APU starting perfectly — after which it silently re-ticked. `FenixActionExecutor.StartApuAsync` now polls the lamp to `ApuAvailTimeoutMs` (180 s, the SAME budget as the Before Start flow's `WaitForField`, so both paths give up together) and a genuine failure still surfaces with the same message, now truthfully. The dispatch gate is released between writes, so the wait never blocks `WaitForDispatchDrainAsync`. ⚠️ Fenix is the ONLY profile that detects an APU item on a lamp rather than an instant switch position (PMDG 737 `APU_Selector`, iFly `APU_Switch_Status`, FBW `A32NX_OVHD_APU_MASTER_SW_PB_IS_ON`) — do not "harmonize" it onto a switch, the AVAIL gate before external power is dropped is the point. The FLOW never had this bug: it waits in its own `WaitForField` step; the asymmetry between the two paths WAS the bug. → [first-officer.md](../first-officer.md) ⚠️ WHICH lamp that is was settled only by live measurement (2026-09-04, Fenix A319): **`I_OH_ELEC_APU_START_U` is AVAIL (persistent) and `_L` is the ON legend (transient, lit only while the start runs)** — reversed against both the real A320 and the Fenix’s own APU MASTER pushbutton, so the suffixes cannot be trusted on this button. Both readings had shipped once each on reasoning alone, each reproducing the other’s bug (with `_L`, a running APU read as not-available, so the item never auto-ticked and a hand-tick waited on a lamp that would never light again). Every site now reads the ONE constant `FenixActionExecutor.ApuAvailField`; never repeat the literal, and never infer a Fenix legend’s meaning — read it in the sim. → [first-officer.md](../first-officer.md)

## FO-12

- **A hand-tick must never re-do work the pilot already did (2026-09-04).** `ChecklistManager.ToggleItem` fired an item’s `CheckAction` unconditionally, so a pilot who had started the APU themselves — outside the First Officer window — and then ticked the item had the FO re-run the whole start sequence on a live APU (MASTER re-written, START pushbutton re-pulsed) before falsely announcing *“Unable to complete”*. The FLOW never had this bug — `FlowManager` honours a step’s `SkipCondition` and says *“Already set”* — and that asymmetry IS the bug. `ToggleItem` now skips the action, and the `AwaitingActionConfirmation` mark with it, when the item’s OWN state condition already reads DEFINITELY true; an indeterminate (NaN) read still runs it. Gated on `IsAutoDetectable`, so an ACTIONABLE item with no state field (the TCAS/WXR/GPWS self-tests) still fires every tick — those exist to be re-run. → [first-officer.md](../first-officer.md)

## FO-13

- Every First Officer checklist line that confirms the landing gear — the After Takeoff Checklist's "Landing gear: UP" and the Landing Checklist's "Landing gear: DOWN" — is backed by a READ-ONLY wait that is the LAST step of the flow that latches it (20 s, Skip, completes the line, never writes). PMDG 737 and iFly 737 read the gear LIGHTS ("gear up, lights out" / "three green" via `GearLightRules`; `GearConfirmation`, `IFly737GearConfirmation`, synthetic `FO_GEAR_UP`/`FO_GEAR_DOWN`; the iFly counts a DIM light as on). The Fenix reads "lights out" for UP and "three green" for DOWN (`FenixGearConfirmation`, `FO_GEAR_DOWN`, read by the Landing Checklist's `LDC_GEAR`): the green is each wheel's LOWER legend `I_MIP_GEAR_n_L`, and the upper `_U` legends plus `I_MIP_GEAR_RED` count as reds — MEASURED 2026-09-25 (Fenix A320 CFM, powered, gear down and locked: every `_L` = 1, every `_U` and the arrow 0; the annunciator light TEST lights all three `_U` and the arrow, proving they are live; the DIM setting leaves `_L` at 1), never inferred from the suffix or the real A320. `_U`'s colour in transit was not observed. The Fenix has no Landing flow, so nothing latches `LDC_GEAR`; if one is added it needs a read-only gear-down check completing `LDC_GEAR`. The PMDG 777, whose SDK exposes no gear lights, composes the same checks from the CDA lever AND the stock `GEAR LEFT/CENTER/RIGHT POSITION` SimVars (`Pmdg777GearConfirmation`, synthetic `FO_GEAR_UP`/`FO_GEAR_DOWN`: UP = lever up and every leg ≤ 1 %, DOWN = lever down and every leg ≥ 99 %, the System Display Gear page's own thresholds), fed by `FirstOfficerForm`'s 1 Hz `RequestFOGearPositions()` (777 only, FO-private definition/request 385) into `AircraftStateEvaluator.SetGearPosition` (`ATKOF_GEAR_UP_CHECK`, `LD_GEAR_DOWN_CHECK`, and the checklist lines `ATKOF_GEAR`/`LDG_GEAR`). The action-group LEVER lines are completed by the step that WRITES the lever — iFly "Gear lever: UP" by `AT_GEAR_OFF`, 777 "Gear: UP" by `ATKOF_GEAR_UP` and 777 "Flaps: UP" by `ATKOF_FLAPS_UP` (the After Takeoff Checklist's "Flaps: UP" is completed by the read-only lever check `ATKOF_FLAPS_UP_CHECK`, placed before the gear check) — so a failed write is skipped aloud and left live rather than latched. A synthetic gear field is NaN when any reading it rests on is unknown. Never add a gear WRITE to a check. → [pmdg-737.md](../pmdg-737.md), [ifly-737.md](../ifly-737.md), [a32nx.md](../a32nx.md), [pmdg-777.md](../pmdg-777.md)

## FO-14

- The landing autobrake is a CAPTAIN item on every aircraft — never automate it in a descent/approach flow or checklist action; the reminder names the panel location, with no suggested setting (RTO arm before takeoff + autobrake OFF after takeoff/landing stay automated). → [first-officer.md](../first-officer.md)

## FO-15

- PMDG auto-flaps was REMOVED (2026-07-08, user decision) — never reintroduce a 737/777 flap schedule; the "Auto-manage flaps" setting acts on the FBW A380 and A32NX only (Fenix stores-but-ignores it, the PMDG managers now do the same). → [first-officer.md](../first-officer.md)

## FO-16

- The FO AP engagement height is the user setting `FOAutoApEngageAltitudeAgl` (default 350 ft AGL, one global value) — never hardcode an engagement altitude in a `FOAutoManager`; the 737's LNAV/VNAV push height (400 ft AGL) is deliberately FIXED and each push is annunciator-guarded (NaN = skip). → [first-officer.md](../first-officer.md)

## FO-17

- Auto-AP-engage is floored by `IAircraftDefinition.MinimumAutopilotEngageAltitudeAgl` (**PMDG 737 = 400 ft** — its AFDS INHIBITS CMD below 400 ft RA after takeoff; 777 = 200) — never let the user setting press below an aircraft's own engage limit, and never lower the 737's floor: at ~1 Hz the old 350 ft default landed in [350, 400) on every takeoff, so the press was silently rejected while the app announced success. → [first-officer.md](../first-officer.md)

## FO-18

- Auto-AP-engage is CLOSED-LOOP wherever `IAircraftDefinition.IsAutopilotEngaged` returns non-null: announce only on a CONFIRMED engagement, retry a rejected press (bounded, `MaxApEngageAttempts`), and report a Captain action if it never takes — never re-add the announce-on-press + one-shot latch (a blind pilot was told "Autopilot engaged" with the AP off and no retry ever came). `IsAutopilotEngaged` must return **null**, never false, before the first CDA snapshot — the PMDG engage switches are TOGGLES and a guessed false would disconnect an engaged AP on retry. → [first-officer.md](../first-officer.md)

## FO-19

- The 10 k landing-light switching is gated by `FOAutoLights10kEnabled` (default ON) and ONLY the light actions/announcements are gated — baro pushes, the no-transition reminder, and the crossing latch always run. The checklist tree must NEVER use `TreeView.CheckBoxes = true` — a TVS_CHECKBOXES tree announces every selected/expanded-once header as "check box" in NVDA regardless of hidden state images (2026-07-16 fix); checkboxes come from `CheckboxStateImages` + per-node `ShowCheckBox`/`HideCheckBox`, and `.Checked` must never be set programmatically on headers/separators. → [first-officer.md](../first-officer.md)
