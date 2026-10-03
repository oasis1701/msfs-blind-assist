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

- `VisualGuidanceManager.Initialize` must stay idempotent (calls `Stop()` first) — don't remove that guard. → [visual-guidance.md](../visual-guidance.md)

## VG-9

- Never re-add a `Start()` call inside `Initialize` — tone start must stay deferred to the first `ProcessUpdate` or a brief fused-tone glitch reappears. → [visual-guidance.md](../visual-guidance.md)

## VG-10

- The follower (current) tone must only start if the desired tone started — don't reorder `StartTonesIfNeeded`'s try blocks. → [visual-guidance.md](../visual-guidance.md)

## VG-11

- VG auto-deactivation on the airborne→on-ground edge must not be gated on GS or any other condition — landings of any speed must trigger it. → [visual-guidance.md](../visual-guidance.md)

## VG-12

- Never split VG's lateral and vertical guidance into separate tones — the matching idiom needs one oscillator per role (desired + current). → [visual-guidance.md](../visual-guidance.md)

## VG-13

- Desired and current tone waveforms must stay different (triangle + sine) — identical waveforms at a matched state phase-cancel exactly when the pilot most needs the difference audible. → [visual-guidance.md](../visual-guidance.md)

## VG-14

- Never bake aircraft-specific VG numbers back into `VisualGuidanceManager` as consts — they belong on `IAircraftDefinition.GetVisualGuidanceProfile()`. → [visual-guidance.md](../visual-guidance.md)

## VG-15

- `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` are applied in different code paths (glideslope error vs. phase detection) and were measured separately — never collapse them into one shared constant. → [visual-guidance.md](../visual-guidance.md)

## VG-16

- The `MAX_DESCENT_RATE_FPM` safety clamp must stay dynamic (`min(-1500, natural×1.3)`) so a legitimate steep-approach descent rate (e.g. a 5.5° ILS) is never clipped. → [visual-guidance.md](../visual-guidance.md)

## VG-17

- The pitch PID's `fpmError`/`fpmErrorRate` coefficients must stay POSITIVE (same sign as the error) — never reintroduce the old leading-minus; it produced wrong-direction guidance masked by tight-tracking autopilot tests. → [visual-guidance.md](../visual-guidance.md)

## VG-18

- PID math, phase machine, and lateral arc-capture logic must stay untouched by tone work — VG's failure mode must always be "missing audible reference," never "wrong steering command." → [visual-guidance.md](../visual-guidance.md)

## VG-19

- VG's manual-query grace window must only suppress the two chatty per-second callouts (bank guidance, centerline deviation) — phase changes and distance callouts must still fire during a manual hotkey readout. → [visual-guidance.md](../visual-guidance.md)
