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
    /// How long hand fly's spoken callouts hold after the handoff. The tone is NEVER affected.
    ///
    /// This lands exactly on rotation, so it is deliberately short. It used to be a flat
    /// 3500 ms, which opened a measured 3.504 s hole in the pitch callouts on a live Fenix
    /// A320 takeoff (2026-08-25) — takeoff assist had been calling pitch every ~0.5 s through
    /// the rotation ramp, and then nothing at all while pitch went 11.6° → 12.6°.
    ///
    /// 1500 ms is only safe because the phrase is ONE SENTENCE — see <see cref="For"/>.
    /// Residual, recorded rather than tuned away: the pre-armed "Airborne." measures ~0.66 s at
    /// SAPI Rate 0, so it holds this same floor for ~0.8 s longer than its own phrase needs,
    /// rather than being given a third separately-tuned constant.
    /// </summary>
    public const int GraceMs = 1500;

    /// <summary>
    /// The mute for the quick-access-keys variant, which is several times the words and
    /// carries a warning the pilot cannot get back. Left at the value the flat mute always
    /// used: this rare failure path is not made worse than it already was, and it is not
    /// widened either. Note it remains badly under-muted — measured 8.62 s of speech at SAPI
    /// Rate 0 against 3500 ms — which is pre-existing and not addressed here.
    /// </summary>
    public const int GraceWithWarningMs = 3500;

    /// <summary>
    /// The quick-access-keys warning, shared with MainForm.Hotkeys.cs's standalone
    /// announcement for a failed MANUAL arm. One wording, one place: a byte-identical copy
    /// used to live in both, so rewording one would have given the pilot two different
    /// sentences for the same condition depending on how hand fly was armed.
    /// </summary>
    public const string QuickKeysWarning =
        "Quick access keys unavailable. Use output mode for H, V, Q.";

    /// <summary>
    /// The cue to speak and the mute to apply for it, as ONE decision. Naming the mode hand
    /// fly just ENTERED rather than reciting both state changes: "takeoff assist off" cost
    /// about a second of rotation to say.
    ///
    /// WHY ONE SENTENCE. Measured by rendering through SAPI and segmenting on the energy
    /// envelope, at the Rate = 0 this app hardcodes (ScreenReaderAnnouncer, ~206 wpm):
    /// "Airborne. Hand fly." segments as [0.11-0.67] [1.56-2.16] — SAPI's inter-sentence pause
    /// is ~0.89 s, longer than all the phonation in "Hand fly" — so against the 1500 ms mute
    /// the second clause was NEVER SPOKEN AT ALL, and the cue carried no information beyond
    /// "Airborne." The comma form measures ~1.05 s and fits with headroom down to ~135 wpm.
    /// Do NOT restore a second sentence here without re-measuring: the sentence pause, not the
    /// word count, is the dominant cost.
    /// </summary>
    public static (string Text, int GraceMs) For(bool activatedHandFly, bool quickKeysRegistered)
    {
        // Pre-armed: hand fly never stopped talking, so announcing it as newly active would be
        // wrong. The only news is that the aircraft is airborne.
        if (!activatedHandFly)
        {
            return ("Airborne.", GraceMs);
        }

        // The warning MUST ride inside this utterance. AnnounceImmediate cancels pending speech
        // on all three backends, so OnHandFlyModeActiveChanged's standalone (queued) warning
        // would be silently swallowed and the pilot would never learn the keys are dead.
        // Excluded for a pre-armed hand fly above: that handler did not fire during this
        // handoff, and the pilot already heard the warning in full when they armed it manually.
        return quickKeysRegistered
            ? ("Airborne, hand fly.", GraceMs)
            : ($"Airborne, hand fly. {QuickKeysWarning}", GraceWithWarningMs);
    }
}
