namespace MSFSBlindAssist.Services;

/// <summary>
/// The single spoken cue the liftoff auto-handoff plays, and the announcement mute that
/// protects it. ONE method returns both, so the phrase and the window sized for it are
/// literally one decision and cannot drift apart. Pure so it can be pinned by
/// LiftoffHandoffBreadcrumbTests — MainForm owns the speaking, this owns the wording and the
/// timing. It is spoken LAST, with AnnounceImmediate, to supersede the Toggles' own callouts.
///
/// SCOPE OF THE PHRASE — read this before re-lengthening it. The pilot pressed no key, so this
/// is the only cue the handoff itself speaks. It deliberately does NOT say that takeoff assist
/// stopped: that wording was dropped by an explicit decision, on the grounds that takeoff
/// assist does not run airborne. Be aware the backup that decision was once justified by —
/// "the centerline tone stopping says it too" — does NOT hold in three configurations, so on
/// those the pilot gets no cue at all that takeoff assist stood down:
///   * TakeoffAssistLegacyMode constructs no AudioToneGenerator at all, so nothing can stop;
///   * at the DEFAULT TakeoffAssistHeadingToneThreshold of 1 the tone is held at volume 0
///     while the aircraft is inside the band — i.e. through a well-flown, on-centerline
///     rotation — so Stop() silences an already-silent tone;
///   * on the pre-armed path hand fly's OWN tone is sounding simultaneously (SuppressAudio is
///     wired only to Visual Guidance), so a stopping tone is ambiguous.
/// TakeoffAssistManager's own "Takeoff assist off" is cancelled by this cue's AnnounceImmediate
/// in every case. That is a known, accepted gap, recorded here so nobody re-derives the false
/// justification from the code.
/// </summary>
public static class LiftoffHandoffBreadcrumb
{
    /// <summary>
    /// EVERY mute below is sized from a MEASURED phrase duration, not estimated from a
    /// words-per-minute model. The model does not work here: spelled letters ("H, V, Q") and
    /// comma pauses cost far more than their word count, and SAPI's inter-sentence pause
    /// (~0.9 s) can exceed all the phonation around it. Rendered through
    /// System.Speech at Rate = 0 — the rate ScreenReaderAnnouncer hardcodes for its SAPI
    /// fallback, and therefore the only SAPI rate this app actually produces — with trailing
    /// silence trimmed off the buffer:
    ///
    ///     "Airborne."                            0.63 s
    ///     "Airborne, hand fly."                  1.68 s
    ///     "Airborne, hand fly, quick keys off."  3.00 s
    ///
    /// Each mute is that number plus roughly a fifth, rounded. NVDA and Tolk run at the
    /// pilot's own rate, which the app cannot read; Rate 0 is the honest reference because it
    /// is the one this process can produce, and a faster reader simply finishes early.
    ///
    /// The ceiling on all of them is 3504 ms — the callout hole measured on a live Fenix A320
    /// takeoff (2026-08-25) that this whole area exists to close, with takeoff assist calling
    /// pitch every ~0.5 s through the rotation ramp and then nothing while pitch went
    /// 11.6° → 12.6°. No mute here may reach it. That ceiling is what caps the phrase length,
    /// not the other way round: see <see cref="For"/>.
    ///
    /// The mute never affects the TONE, only the spoken callouts.
    /// </summary>
    public const int GraceMs = 2000;

    /// <summary>
    /// The mute for the pre-armed cue, which is one word. Sized separately rather than sharing
    /// <see cref="GraceMs"/>: hand fly was ALREADY speaking on this path, so every millisecond
    /// of mute is a callout the pilot would otherwise have had, and "Airborne." needs less than
    /// half of what "Airborne, hand fly." does.
    /// </summary>
    public const int GracePreArmedMs = 900;

    /// <summary>
    /// The mute for the quick-access-keys variant. Held at the value the old flat mute used, so
    /// this path is never made worse — and the PHRASE was shortened to fit it rather than the
    /// mute widened to fit the phrase, because widening past ~3.5 s reaches the very hole this
    /// area exists to close. The old wording ran 8.15 s against this window, so 57% of a warning
    /// the pilot needs was cut off mid-word.
    /// </summary>
    public const int GraceWithWarningMs = 3500;

    /// <summary>
    /// The quick-access-keys warning — ONE wording, used both by the cue below and by
    /// MainForm.Hotkeys.cs's standalone announcement for a failed MANUAL arm, so the pilot
    /// cannot hear two different sentences for one condition depending on how hand fly was
    /// armed. A lower-case FRAGMENT rather than a sentence, because both callers fold it into a
    /// clause: making it a second sentence costs SAPI's ~0.9 s inter-sentence pause, which at
    /// rotation does not fit.
    ///
    /// "FAILED", not "off" — this is an ERROR CONDITION that should not normally arise, and the
    /// word has to say so. "Off" reads like a setting the pilot chose and can flip back; there
    /// is no such setting. Registration fails only when another application already owns one of
    /// the bare letter keys globally, or when hand fly was activated while output hotkey mode
    /// was still up — and HotkeyManager already calls DeactivateOutputHotkeyMode() before
    /// toggling on both hotkey entry points precisely so that second case cannot happen there,
    /// which is what leaves "failed" the honest description of what is left.
    ///
    /// It deliberately does NOT name the affected keys or a remedy. It used to read
    /// "Quick access keys unavailable. Use output mode for H, V, Q.", which was both wrong and
    /// expensive: hand fly captures NINE keys (H, V, Q, S, D, B, P, A, F — see
    /// HotkeyManager.QuickAccessKeys), so naming three understated what the pilot had lost, and
    /// the spelled letter list was most of the cost — 8.15 s of speech against a 3500 ms mute,
    /// i.e. 57% of it cut off mid-word. Which keys hand fly captures is documented
    /// (docs/visual-guidance.md, "Quick-access hotkeys"); an announcement spoken over a
    /// rotation is not the place to recite them.
    /// </summary>
    public const string QuickKeysWarning = "quick keys failed";

    /// <summary>
    /// The cue to speak and the mute to apply for it, as ONE decision. Naming the mode hand
    /// fly just ENTERED rather than reciting both state changes: "takeoff assist off" cost
    /// about a second of rotation to say.
    ///
    /// WHY ONE SENTENCE, AND WHY SO FEW WORDS. Every phrase here is spoken over a rotation, so
    /// its length is bought with pitch callouts the pilot does not get. Two costs dominate, and
    /// neither is word count: SAPI's inter-sentence pause is ~0.9 s, and spelled letters with
    /// commas ("H, V, Q") are far slower than they look. Measured at Rate 0,
    /// "Airborne. Hand fly." runs 2.13 s and does not even BEGIN its second clause until
    /// 1.56 s, so against a 1500 ms mute those words were never spoken at all; the comma form
    /// runs 1.68 s. Do NOT add a sentence, a spelled letter list, or a clause here without
    /// re-measuring and re-sizing the matching mute — and if the new mute would approach
    /// 3.5 s, shorten the phrase instead. That ceiling is the defect this area exists to fix.
    /// </summary>
    public static (string Text, int GraceMs) For(bool activatedHandFly, bool quickKeysRegistered)
    {
        // Pre-armed: hand fly never stopped talking, so announcing it as newly active would be
        // wrong. The only news is that the aircraft is airborne — and because the callout
        // stream is already running here, this one gets the shortest mute of the three.
        if (!activatedHandFly)
        {
            return ("Airborne.", GracePreArmedMs);
        }

        // The warning MUST ride inside this utterance. AnnounceImmediate cancels pending speech
        // on all three backends, so OnHandFlyModeActiveChanged's standalone (queued) warning
        // would be silently swallowed and the pilot would never learn the keys are dead. It
        // rides in its SHORT form — the full sentence cannot be delivered inside a mute this
        // area can afford. Excluded for a pre-armed hand fly above: that handler did not fire
        // during this handoff, and the pilot already heard the warning in full when they armed
        // it manually.
        return quickKeysRegistered
            ? ("Airborne, hand fly.", GraceMs)
            : ($"Airborne, hand fly, {QuickKeysWarning}.", GraceWithWarningMs);
    }
}
