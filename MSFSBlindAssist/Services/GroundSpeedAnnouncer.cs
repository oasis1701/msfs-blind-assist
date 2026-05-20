using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Global periodic ground-speed announcer. Speaks the aircraft's ground speed rounded to
/// the nearest multiple of the user-configured interval (5 or 10 kt; 0 = off).
///
/// Runs independent of taxi / takeoff / visual-guidance mode. It is fed by the always-on
/// GROUND_VELOCITY continuous SimConnect variable (registered as a base variable in
/// <see cref="Aircraft.BaseAircraftDefinition"/>), so callouts continue uninterrupted
/// through the takeoff roll, the landing rollout, and taxi.
///
/// This logic previously lived inside <c>TaxiGuidanceManager.UpdatePosition</c>, which only
/// ran while taxi guidance was active — so the callouts stopped the instant takeoff assist
/// took over, or after touchdown before taxi guidance re-engaged. Extracting it here makes
/// the feature behave as users expect: enabled in settings ⇒ global.
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
    /// Feed a fresh ground-speed sample (knots). Announces when the rounded bucket changes,
    /// with hysteresis to ride out SimConnect jitter near a bucket boundary. No-op when the
    /// interval setting is 0 (off) or the speed is negative/invalid.
    /// </summary>
    public void ProcessGroundSpeed(double groundSpeedKts)
    {
        int interval = SettingsManager.Current.TaxiGuidanceGroundSpeedAnnounceInterval;
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
