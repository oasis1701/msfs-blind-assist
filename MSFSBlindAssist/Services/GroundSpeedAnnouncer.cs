using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Periodic ground-speed announcer. Speaks the aircraft's ground speed rounded to the
/// nearest multiple of the user-configured interval (5 or 10 kt; 0 = off).
///
/// Runs independent of taxi / takeoff / visual-guidance mode — it is fed by the always-on
/// GROUND_VELOCITY continuous SimConnect variable (registered as a base variable in
/// <see cref="Aircraft.BaseAircraftDefinition"/>), so callouts cover every ON-GROUND phase:
/// taxi, the takeoff roll, and the landing rollout. Callouts are intentionally silenced
/// while airborne — ground speed isn't a useful continuous callout in the air, and the
/// caller passes the air/ground state into <see cref="ProcessGroundSpeed"/>.
///
/// This logic previously lived inside <c>TaxiGuidanceManager.UpdatePosition</c>, which only
/// ran while taxi guidance was active — so the callouts stopped the instant takeoff assist
/// took over, or after touchdown before taxi guidance re-engaged. Extracting it here makes
/// the feature behave as users expect: enabled in settings ⇒ works in every ground phase.
///
/// The backing setting is still <c>UserSettings.TaxiGuidanceGroundSpeedAnnounceInterval</c>
/// (kept under that name to avoid a settings migration; it is surfaced in the Taxi Guidance
/// Options dialog). Despite the name, the behaviour is now global.
/// </summary>
public class GroundSpeedAnnouncer
{
    private readonly ScreenReaderAnnouncer announcer;

    // -1 = no baseline yet; the first sample establishes the baseline silently so the
    // user doesn't get a spurious callout the moment the app connects.
    private int lastAnnouncedBucket = -1;

    // The effective interval used on the previous sample. When it changes — a taxi<->takeoff
    // transition, or a settings edit — the baseline is reset so the first sample at the new
    // cadence is silent instead of emitting a stale bucket. 0 = no cadence seen yet.
    private int lastEffectiveInterval = 0;

    // Once a bucket has been announced, the speed must be at least this many knots past
    // the rounding boundary into the new bucket before we re-announce. Kills "5 / 10 / 5"
    // flutter when the throttle holds steady near a midpoint (e.g. 7.5 kt with interval 5).
    private const double HysteresisKts = 0.5;

    public GroundSpeedAnnouncer(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;
    }

    /// <summary>
    /// Re-baselines so the next sample is silent. Useful after a teleport or a long pause
    /// where a sudden ground-speed jump would otherwise produce a meaningless callout.
    /// </summary>
    public void ResetBaseline() => lastAnnouncedBucket = -1;

    /// <summary>
    /// Feed a fresh ground-speed sample (knots) plus the current air/ground state. Announces
    /// when the rounded bucket changes, with hysteresis to ride out SimConnect jitter near a
    /// bucket boundary. No-op when: the interval setting is 0 (off), the speed is invalid, OR
    /// the aircraft is airborne — GS callouts are an on-ground-only feature (taxi, takeoff
    /// roll, landing rollout).
    ///
    /// While airborne the bucket baseline is left FROZEN (not reset) — so the first sample
    /// after touchdown compares the rollout speed against the last on-ground bucket and
    /// announces immediately, rather than spending a sample re-establishing a baseline.
    ///
    /// While Takeoff Assist is active the cadence is governed by the separate
    /// <see cref="UserSettings.TakeoffAssistGroundSpeedAnnounceInterval"/> setting: its -1
    /// sentinel means "same as taxi" (existing behaviour), 0 silences the roll, and 5/10
    /// override the taxi interval with a coarser cadence so GS callouts don't crowd out the
    /// centerline-deviation announcements.
    /// </summary>
    public void ProcessGroundSpeed(double groundSpeedKts, bool onGround, bool takeoffAssistActive)
    {
        if (!onGround)
            return;  // airborne — GS callouts are on-ground only; baseline left frozen

        // Snapshot once per tick (SV-5) instead of two separate Current reads —
        // avoids acquiring SettingsManager's static lock twice per sample.
        // UserSettings is mutated in place by the settings dialog, so this
        // snapshot observes the same live-applied values a repeated Current
        // read would; live-apply keeps working tick-to-tick.
        var settings = SettingsManager.Current;

        // Resolve the effective cadence. While Takeoff Assist is active the takeoff setting
        // wins; its -1 sentinel means "same as taxi" (so existing users keep roll callouts),
        // 0 means silence the roll, 5/10 override the cadence.
        int taxiInterval = settings.TaxiGuidanceGroundSpeedAnnounceInterval;
        int interval;
        if (takeoffAssistActive)
        {
            int takeoffInterval = settings.TakeoffAssistGroundSpeedAnnounceInterval;
            interval = takeoffInterval < 0 ? taxiInterval : takeoffInterval;
        }
        else
        {
            interval = taxiInterval;
        }

        // Re-baseline on a cadence change so a taxi<->takeoff transition (or a settings edit)
        // doesn't emit a stale bucket at the new interval.
        if (interval != lastEffectiveInterval)
        {
            lastAnnouncedBucket = -1;
            lastEffectiveInterval = interval;
        }

        if (interval <= 0 || groundSpeedKts < 0)
            return;

        // Round to the NEAREST multiple of the interval — 4/5/6 kt all read as "5 knots",
        // 9/10/11 kt all read as "10 knots". (A floor-bucket would flip 0↔5 every time the
        // raw value crossed 5.000 exactly.)
        int newBucket = (int)System.Math.Round(groundSpeedKts / interval, System.MidpointRounding.AwayFromZero);

        if (lastAnnouncedBucket < 0)
        {
            // First sample establishes the baseline silently.
            lastAnnouncedBucket = newBucket;
            return;
        }

        if (newBucket == lastAnnouncedBucket)
            return;

        // Hysteresis: require the speed to be at least HysteresisKts past the rounding
        // boundary into the new bucket before committing.
        double newBucketCenter = newBucket * interval;
        double distanceIntoNewBucket = System.Math.Abs(groundSpeedKts - newBucketCenter);
        double bucketHalfWidth = interval / 2.0;
        if (distanceIntoNewBucket <= bucketHalfWidth - HysteresisKts)
        {
            // Plain Announce (queued, not AnnounceImmediate) so a fading ground-speed
            // callout doesn't displace the most recent actionable instruction in any
            // feature's Repeat-Last buffer.
            announcer.Announce($"{newBucket * interval} knots.");
            lastAnnouncedBucket = newBucket;
        }
    }
}
