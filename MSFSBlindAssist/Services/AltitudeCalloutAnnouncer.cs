using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Announces each 1,000-foot crossing while airborne ("5,000 feet", "10,000 feet", …),
/// in both the climb and the descent — situational-awareness callouts for a blind pilot
/// who can't watch the altitude tape.
///
/// Fed by the always-on INDICATED ALTITUDE continuous SimConnect variable (routed in
/// MainForm.OnSimVarUpdated). On-ground samples are ignored so taxiing at field elevation
/// doesn't fire callouts; the band baseline is established silently on the first sample so
/// connecting mid-flight doesn't produce a spurious callout.
///
/// Controlled by <c>UserSettings.AltitudeCalloutsEnabled</c> (default on). Mirrors the
/// shape of <see cref="GroundSpeedAnnouncer"/>.
/// </summary>
public class AltitudeCalloutAnnouncer
{
    private readonly ScreenReaderAnnouncer announcer;

    private const int BandFeet = 1000;

    // Once a band has been announced, the altitude must be at least this many feet past the
    // 1,000-ft boundary into the new band before re-announcing — kills flutter when the
    // aircraft levels off right on a round thousand.
    private const double HysteresisFeet = 80.0;

    // -1 = no baseline yet (first sample establishes it silently).
    private int lastAnnouncedBand = -1;

    public AltitudeCalloutAnnouncer(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;
    }

    /// <summary>Re-baselines so the next sample is silent (after a teleport / long pause).</summary>
    public void ResetBaseline() => lastAnnouncedBand = -1;

    /// <summary>
    /// Feed a fresh indicated-altitude sample (feet) plus the current air/ground state.
    /// Announces when the 1,000-ft band changes, with hysteresis to ride out jitter at a
    /// boundary. No-op when the setting is off, the value is invalid, or on the ground.
    /// </summary>
    public void ProcessAltitude(double altitudeFeet, bool onGround)
    {
        if (onGround) { lastAnnouncedBand = -1; return; }   // re-baseline on the ground
        if (!SettingsManager.Current.AltitudeCalloutsEnabled) return;
        if (double.IsNaN(altitudeFeet) || altitudeFeet < -2000 || altitudeFeet > 70000) return;

        int newBand = (int)System.Math.Floor(altitudeFeet / BandFeet);

        if (lastAnnouncedBand < 0) { lastAnnouncedBand = newBand; return; }   // silent baseline
        if (newBand == lastAnnouncedBand) return;

        // Hysteresis: require the altitude to be at least HysteresisFeet into the new band
        // (measured from whichever boundary we just crossed) before committing.
        double crossedBoundary = (newBand > lastAnnouncedBand ? newBand : lastAnnouncedBand + 1) * (double)BandFeet;
        if (System.Math.Abs(altitudeFeet - crossedBoundary) < HysteresisFeet) return;

        lastAnnouncedBand = newBand;
        int feet = newBand * BandFeet;
        // Plain Announce (queued) so a fading altitude callout doesn't displace the most
        // recent actionable instruction in any feature's Repeat-Last buffer. Format is the bare
        // number ("32000", "5000") per Gus's preference — this is now the ONLY altitude callout
        // (the duplicate legacy in-base announce was removed).
        announcer.Announce($"{feet}");
    }
}
