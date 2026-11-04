# Visual guidance, hand fly and the liftoff handoff — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/visual-guidance.md`, which Claude Code loads when it reads matching code. Background: [visual-guidance.md](../visual-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## VG-1

- The liftoff handoff's cue WORDING and the announcement mute that protects it are ONE decision in `Services/LiftoffHandoffBreadcrumb` (`For(...)` returns both) — never split them back into a phrase and a separate duration, and never size a mute by ESTIMATE. Every mute there is derived from a phrase rendered through `System.Speech` at `Rate = 0` (what `ScreenReaderAnnouncer` hardcodes, so the only SAPI rate this app produces) with trailing silence trimmed, plus about a fifth: "Airborne." 0.63 s → 900 ms, "Airborne, hand fly." 1.68 s → 2000 ms, "Airborne, hand fly, quick keys failed." 3.09 s → 3500 ms. A words-per-minute estimate does NOT work here and has already misled once — it put "Airborne, hand fly." at ~1.05 s when it is 1.68 s, and a 1500 ms mute was shipped against that guess. Two costs dominate and neither is word count: SAPI's inter-sentence pause is ~0.9 s (the two-sentence "Airborne. Hand fly." does not BEGIN its second clause until 1.56 s, so under a 1500 ms mute those words were never spoken at all), and a spelled letter list with commas ("H, V, Q") is far slower than it looks — it was most of why the old quick-keys warning ran 8.15 s. The ceiling on every mute is 3504 ms, the hole measured on the live Fenix A320 takeoff this area exists to close; when a phrase will not fit under it, SHORTEN THE PHRASE, never widen the mute. The quick-keys warning is now the single shared fragment "quick keys failed" (`QuickKeysWarning`) — FAILED, not "off", because it is an error condition and there is no such setting for a pilot to have turned off, used by BOTH the rotation cue and `MainForm.Hotkeys`'s manual-arm announcement so one condition never gets two phrasings; it names neither the keys nor a remedy, and must not regain them — hand fly captures NINE keys (H, V, Q, S, D, B, P, A, F), so the old "Use output mode for H, V, Q" was wrong as well as long, and which keys are captured belongs in the docs rather than in an announcement spoken over a rotation. The mute never affects the TONE, only the spoken callouts. ⚠️ CORRECTION to an earlier telling: the phrase and the mute never actually drifted — `git log -S` shows 3500 ms was introduced already sized for the phrase it shipped with, so the 3.504 s hole was the mute working exactly as designed, and it had to shrink because it lands on ROTATION. The cue deliberately does NOT say takeoff assist stopped, and the "centerline tone stopping says it too" justification is FALSE in three configurations (legacy mode builds no tone; the default heading-tone-threshold holds it at volume 0 through an on-centerline rotation; a pre-armed hand fly's own tone masks it) — an accepted gap, recorded so it is not re-derived as sound. → [visual-guidance.md](../visual-guidance.md)

## VG-2

- **Every path that activates hand fly or visual guidance must leave OUTPUT HOTKEY MODE first.** `RegisterHandFlyHotkeys`/`RegisterVisualGuidanceHotkeys` early-return false while `outputHotkeyModeActive` is set, skipping quick-access registration wholesale — and NOTHING re-acquires those keys when output mode later exits, so all nine stay dead for the rest of the session while the pilot has been told only "quick keys failed". `HotkeyManager` guards its own two entry points (`HOTKEY_HAND_FLY_MODE`, `HOTKEY_VISUAL_GUIDANCE`) by calling `DeactivateOutputHotkeyMode()` before triggering; the liftoff auto-handoff bypasses the dispatcher by calling `handFlyManager.Toggle()` directly, so it calls `hotkeyManager.ExitOutputHotkeyMode()` itself. Output mode has NO auto-timeout ("stays active until used or escape pressed"), so a pilot who armed it and was then distracted really can still be in it at rotation. The call is SILENT by construction — `OnOutputHotkeyModeChanged` speaks only for `Activated`/`Cancelled` and this raises `Deactivated` — which is what makes it safe on a path that must not gain speech; keep it that way. → [visual-guidance.md](../visual-guidance.md)

## VG-3

- Visual guidance must NOT require HandFly mode — never reintroduce a `!handFlyManager.IsActive` gate; that produced a confusing three-tone overlap. → [visual-guidance.md](../visual-guidance.md)

## VG-4

- HandFly's tone must auto-mute while VG is active (`SuppressAudio`/`ResumeAudio`) — VG's two tones share HandFly's Hz/pan mapping, so all three together is acoustically incoherent. → [visual-guidance.md](../visual-guidance.md)

## VG-5

- The quick-access hotkey set must stay reference-counted/shared between HandFly and VG — never split it back into per-mode key sets, which caused a double-register conflict. → [visual-guidance.md](../visual-guidance.md)

## VG-6

- There is no single-tone VG mode — never reintroduce a flag to gate off the current tone; the dual-tone is the design. → [visual-guidance.md](../visual-guidance.md)

## VG-7

- Always route `cachedBank` through `VisualGuidanceManager.StandardBank()` before any tone/bank-error use — SimConnect's `PLANE_BANK_DEGREES` is left-positive but the tone API is right-positive. → [visual-guidance.md](../visual-guidance.md)

## VG-8

- `VisualGuidanceManager.Initialize` must stay idempotent: it disposes any existing tones first (`DisposeTones()`) — don't remove that guard, and never replace it with `Stop()`, which announces "Visual guidance off" just before Initialize's own "Visual guidance active" and nulls the state Initialize is about to set. → [visual-guidance.md](../visual-guidance.md)

Corrected 2026-10-08: the guard is `DisposeTones()`, not `Stop()`; following the old wording would bring back the off-then-active announcement pair. Evidence: `VisualGuidanceManager.Initialize`, whose comment says not to call `Stop()` and which calls `DisposeTones()`.

## VG-9

- Never re-add a `Start()` call inside `Initialize` — tone start must stay deferred to the first `ProcessUpdate` or a brief fused-tone glitch reappears. → [visual-guidance.md](../visual-guidance.md)

## VG-10

- The follower (current) tone must only start if the desired tone started — don't reorder `StartTonesIfNeeded`'s try blocks. → [visual-guidance.md](../visual-guidance.md)

## VG-11

- VG auto-deactivation on the airborne→on-ground edge must not be gated on GS or any other condition — landings of any speed must trigger it. → [visual-guidance.md](../visual-guidance.md)

## VG-12

- Never split VG's lateral and vertical guidance into separate tones — the matching idiom needs one oscillator per role (desired + current). → [visual-guidance.md](../visual-guidance.md)

## VG-13

- Desired and current tone waveforms must default to different shapes (triangle + sine: `UserSettings.VisualGuidanceToneWaveform` and `VisualGuidanceCurrentToneWaveform`, both pilot-settable in `HandFlyPanel`) — identical waveforms at a matched state phase-cancel exactly when the pilot most needs the difference audible. → [visual-guidance.md](../visual-guidance.md)

Corrected 2026-10-08: triangle and sine are the defaults of two user settings, not fixed values. Evidence: `UserSettings.VisualGuidanceToneWaveform` (Triangle) and `VisualGuidanceCurrentToneWaveform` (Sine), set from `Forms/Settings/HandFlyPanel`.

## VG-14

- Never bake aircraft-specific VG numbers back into `VisualGuidanceManager` as consts — they belong on `IAircraftDefinition.GetVisualGuidanceProfile()`. → [visual-guidance.md](../visual-guidance.md)

## VG-15

- `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` are applied in different code paths (glideslope error vs. phase detection) and were measured separately — never collapse them into one shared constant. → [visual-guidance.md](../visual-guidance.md)

## VG-16

- The `MAX_DESCENT_RATE_FPM` safety clamp must stay dynamic (`min(-1500, natural×1.3)`) so a legitimate steep-approach descent rate (e.g. a 5.5° ILS) is never clipped. → [visual-guidance.md](../visual-guidance.md)

## VG-17

- The pitch PID's `fpmError`/`fpmErrorRate` coefficients must stay POSITIVE (same sign as the error) — never reintroduce the old leading-minus; it produced wrong-direction guidance masked by tight-tracking autopilot tests. → [visual-guidance.md](../visual-guidance.md)

## VG-18

- Visual guidance must fail as a missing audible reference, never as a wrong steering command. Tone work leaves the guidance math (the vertical law, the phase machine, the lateral arc capture and precision track) alone. → [visual-guidance.md](../visual-guidance.md)
- Rewritten 2026-11-04 for the visual guidance redesign, which changes the guidance math on purpose. A deliberate change to it keeps left and right as `VisualGuidanceHarnessHookTests` pins them (right of final and parallel banks LEFT toward the line; right of final on an intercept heading banks RIGHT at capture, on two runways), and is measured first in the VirtualPilot approach harness against the baseline with `compare_approach.py` ([virtual-pilot.md](../virtual-pilot.md), "Approaches"). If a sign test fails, never flip the test: work out by hand which way the aircraft has to turn. The original wording ("tone work must leave the PID math, phase machine and lateral arc capture untouched") kept its intent: a broken tone is silence, never a wrong command.

## VG-19

- VG's manual-query grace window must only suppress the bank callouts — phase changes and distance callouts must still fire during a manual hotkey readout. → [visual-guidance.md](../visual-guidance.md)
- Originally (before 2026-11) the window covered "the two chatty per-second callouts (bank guidance, centerline deviation)". The centreline deviation callouts ("0.4 nm left of center", "on runway center") were removed in stage 3 of the visual guidance redesign, because F now speaks the sideways position in every phase (VG-22; decided with the owner, 2026-11-02, on that condition). `VisualGuidanceCalloutTests.No_centreline_callout_is_ever_spoken` pins the removal.
- Inside the window the bank callout gate keeps tracking but may not speak (VG-21), so when the window ends a callout that came due during it speaks the error as it is then, at once.

## VG-20

- Every rate limit, smoothing filter and derivative in `VisualGuidanceManager` runs on the measured frame time through `VisualGuidanceTiming`: a rate limit moves at most rate × dt, a smoothing factor is per second (α = factor^dt), and a derivative is a rate over a time window (`VisualGuidanceWindowedRate`), never a frame-to-frame difference or a sample count. → [visual-guidance.md](../visual-guidance.md)
- Why: `StartVisualGuidanceMonitoring` requests the visual guidance data at `SIMCONNECT_PERIOD.SIM_FRAME`, and MainForm calls `ProcessUpdate` on every frame's AGL event (per-frame since at least 2025-11-16). Every limit and filter had been written for one update a second, so at 30 frames a second the 3°/s bank limit and 2.5°/s pitch limit allowed about 90°/s and 75°/s, the 0.7 vertical-speed smoothing averaged over about 0.1 s instead of 3 s, the vertical-speed damping term divided a frame-to-frame change by about 0.033 s, and the 5-sample cross-track window covered about 0.15 s instead of 4 s.
- Measured in the VirtualPilot approach harness at the stage 1 baseline: the commanded pitch's RMS rate of change on a clean jet final was 0.153, 0.275 and 0.761°/s at 20, 30 and 60 frames a second, and with assumed sensor noise 27.6°/s at 30 and 90.1°/s at 60. A command on real time reads about the same at every frame rate.
- The numbers are the 1 Hz design's, now per second: the bank and pitch rates come from `VisualGuidanceProfile` (VG-14), the vertical-speed and AoA smoothing are 0.7 per second, the cross-track rate is taken over 4 s and smoothed at 0.85 per second, and the vertical-speed error's rate over 1 s. One exception: the flare's pitch limit, `MAX_FLARE_PITCH_RATE`, is 10°/s, not the design's 1.5. At 1.5°/s on real time the flare cue took 2.7 s to reach the flare pitch, after a jet's wheels touched (30 ft is about 2.4 s up), and the harness's jets landed about 230 fpm firmer; at 10°/s no landing is more than 100 fpm firmer than before (docs/virtual-pilot.md, "Stage 2", "The flare"). `VisualGuidanceRealTimeTests.The_flare_cue_reaches_the_flare_pitch_within_half_a_second` pins it.
- `VisualGuidanceTiming.FrameSeconds` caps dt at 1 s, so a pause, a stalled frame stream or a menu moves the command no more than one second's worth, and counts a backwards clock step as 0, never a reverse step. A `VisualGuidanceWindowedRate` given a sample earlier than its newest (the clock stepped back) starts afresh, so the damping rates never read samples stamped in the future. Two updates at the same instant (SimConnect can deliver two frames in one message-pump turn) therefore move nothing the second time.
- The first update after `Initialize` takes the guidance's command with no rate limit: there is nothing yet to be continuous with, and the guidance tone should start where the guidance says instead of ramping up from level. `VisualGuidanceRealTimeTests` pins all of this, each test on its own fake clock (`ClockForHarness`).

## VG-21

- Bank callouts ("3 right", "1 left", "matched", "On commanded path") are spoken only when `VisualGuidanceBankCalloutGate.Next` returns one; `VisualGuidanceManager.AnnounceBankGuidance` feeds it every update and speaks what it returns. With the error e = desired − actual: "matched" is entered at |e| ≤ 0.4° and left only at |e| ≥ 1.0°, and "matched" and "On commanded path" count as one callout. A callout is due when it differs from the last one spoken and has held: matched 0.25 s; a steady error under 1° outside matched 1.5 s; an error the tones can still show (under the profile's `ToneBankRangeDeg`, 5° on airliners, 10° on the C172) 0.25 s; a larger one, where both tones are pinned, once the aircraft's bank has not moved more than 0.5° for 1 s. A due callout speaks only 1 s or more after the last one, and only outside a manual-query grace window (VG-19); nothing is queued, so the next allowed one speaks the error as it is then. → [visual-guidance.md](../visual-guidance.md)
- Why: the stage 1 harness heard "3 right", "6 right", "9 right" within 0.07 s, a median of 52 lines a run cut off by the next, and callouts flipping between "matched" and "1 right" around half a degree many times a second, each cutting off the one before before "left" or "right" was spoken. The gap is a fixed second, not an estimate of speaking time, because VG-1 forbids sizing anything by estimate.
- The spec (2026-11-02) said "an error within about 2°" for the 0.25 s hold. The owner changed it on 2026-11-04 to the tone bank range: a gust that knocks the aircraft 2 to 4° must be heard at once, and only an error past what the tones can show waits for the bank to stop changing. Their worry: gated callouts make small corrections slower to hear.
- Measured in the VirtualPilot GUST runs with the callouts before the gate (`approach_stage2_v2`): the first callout came 0.04 to 0.12 s after a knock began, while it was still building (a 4° knock was spoken "1 right", "2 right", "3 right", "4 right" within 0.27 s, each cutting off the last); a pilot steering by speech alone took about 11 s to recover from a 4° knock, because the old callouts stayed silent while the error rounded to the "1" already spoken ([virtual-pilot.md](../virtual-pilot.md), "Gusts with today's callouts").
- An error callout still due is spoken again once 3 s (`RepeatSeconds`) have passed since it was spoken; "matched" is never repeated. The owner decided this on 2026-11-04 ("if the aircraft is one or two degrees off, it should be announced") after the harness showed the never-repeat rule leaving a pilot who steers by speech alone sitting 0.6 to 1.4° off in silence: in `GUST-A-LIGHT-g2-speech` "1 right" was spoken once and the aircraft drifted 30 to 37 ft for over 17 s. With the repeat, the speech-led gust runs track better than with the old callouts in every case (worst cross-track after a knock 57.5, 50.3, 30.9 and 29.2 ft before, 27.2, 23.8, 11.7 and 21.1 after), and the median bank callouts a minute over all 159 runs is 5.7 (15.0 before the gate, 1.7 with the gate and no repeat). 3 s is a pilot's reaction plus a 1° roll plus the 1 s gap; it was measured once, not swept.
- `VisualGuidanceBankCalloutGateTests` pins the timing (VG-21): one callout for an error hovering around half a degree, silence then one "matched" for a clean roll, a steady 0.7° spoken once at 1.5 s, a large error waiting for the bank to settle, a 2° knock spoken within a quarter of a second of settling, a callout due in the gap dropped and the next speaking the error then, and a callout held back by a manual readout speaking the current error when the window ends.

## VG-22

- F (`HotkeyAction.ReadTargetFPM`, a quick-access key while visual guidance is on) speaks `VisualGuidanceManager.GetStatusReadout()`, which `VisualGuidanceStatusText.Compose` builds: the phase first, in the words visual guidance speaks it, then the sideways position, then the vertical. For example "On final, 6.2 miles. On the centreline. 40 feet high, target 750 down.", "Intercepting final, 8.2 miles out. 1.2 miles right of the centreline. 200 feet low, target level.", "Flare, 20 feet. On the centreline." Before the first update has placed the aircraft it says "Visual guidance starting.", never "0.0 miles". Every number is culture-invariant ("6.2", "2,260"), tested under sv-SE and de-DE. → [visual-guidance.md](../visual-guidance.md)
- Rule: whatever visual guidance speaks on its own in a phase belongs in that phase's readout, so every automatic announcement can be heard again from one key. The redesign's spec calls this "one key answers everything" (2026-11-02), and it is the owner's standing rule for anything the app speaks on its own.
- The centreline deviation callouts were removed on that condition (VG-19): F gives the sideways position in every phase, on final included. Cross-track is signed as the lateral law takes it (+ right of the centreline facing the runway); `VisualGuidanceCalloutTests.Right_of_the_centreline_reads_right` pins the side.
- The spec says F works "in the quick-access set while visual guidance is on, and in Output mode". Output mode's F is Fuel Quantity, so F stays a quick-access key only; which Output-mode key, if any, reads the status is the owner's decision (asked at the end of stage 3, 2026-11).
