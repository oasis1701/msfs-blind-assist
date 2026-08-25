namespace MSFSBlindAssist.Services;

/// <summary>
/// The single spoken breadcrumb the liftoff auto-handoff plays, and the announcement mute that
/// protects it. Pure so it can be pinned by LiftoffHandoffBreadcrumbTests — MainForm owns the
/// speaking, this owns the wording and the timing, and keeping the two together is what stops
/// the phrase being shortened while the mute sized for the old one is left behind.
///
/// The pilot pressed NO key, so this phrase is the whole spoken record that takeoff assist
/// stopped and hand fly took over. It is spoken LAST, with AnnounceImmediate, to supersede the
/// two Toggles' own callouts.
/// </summary>
public static class LiftoffHandoffBreadcrumb
{
    /// <summary>
    /// How long hand fly's spoken callouts hold after the handoff. The tone is NEVER affected.
    ///
    /// This lands exactly on rotation, so it is deliberately short. It used to be a flat
    /// 3500 ms sized for a much longer phrase, which opened a measured 3.504 s hole in the
    /// pitch callouts on a live Fenix A320 takeoff (2026-08-25) — takeoff assist had been
    /// calling pitch every ~0.5 s through the rotation ramp, and then nothing at all while
    /// pitch went 11.6° → 12.6°. Shortening the phrase is what paid for shortening this.
    /// </summary>
    public const int GraceMs = 1500;

    /// <summary>
    /// The mute for the quick-access-keys variant, which is roughly four times the words and
    /// carries a warning the pilot cannot get back. Left at the value the flat mute always
    /// used: this rare failure path is not made worse than it already was, and it is not
    /// widened either — even here the phrase was already longer than the mute.
    /// </summary>
    public const int GraceWithWarningMs = 3500;

    /// <summary>
    /// The breadcrumb text. Names the mode hand fly just ENTERED rather than reciting both
    /// state changes: "takeoff assist off" cost about a second of rotation to say, and takeoff
    /// assist does not run airborne, so the pilot loses nothing they cannot infer — the
    /// centerline tone stopping says it too.
    /// </summary>
    public static string Compose(bool activatedHandFly, bool quickKeysRegistered)
    {
        // Pre-armed: hand fly never stopped talking, so announcing it as newly active would be
        // wrong. The only news is that the aircraft is airborne and takeoff assist stood down.
        if (!activatedHandFly)
        {
            return "Airborne.";
        }

        // The warning MUST ride inside this utterance. AnnounceImmediate cancels pending speech
        // on all three backends, so OnHandFlyModeActiveChanged's standalone (queued) warning
        // would be silently swallowed and the pilot would never learn the keys are dead.
        // Excluded for a pre-armed hand fly above: that handler did not fire during this
        // handoff, and the pilot already heard the warning in full when they armed it manually.
        return quickKeysRegistered
            ? "Airborne. Hand fly."
            : "Airborne. Hand fly. Quick access keys unavailable. Use output mode for H, V, Q.";
    }

    /// <summary>
    /// The mute to apply for the phrase <see cref="Compose"/> will produce. Derived from the
    /// same two flags so the two can never describe different phrases.
    /// </summary>
    public static int GraceMsFor(bool activatedHandFly, bool quickKeysRegistered) =>
        activatedHandFly && !quickKeysRegistered ? GraceWithWarningMs : GraceMs;
}
