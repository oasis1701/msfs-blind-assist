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

    // Flutter guard: while the aircraft hovers within this many feet of a round thousand it
    // just announced, the band index flips back and forth across the line — those re-crossings
    // must NOT repeat the callout. This is ONLY a same-boundary debounce; it does not delay the
    // first announcement of a crossing (that fires AT the band change — see ProcessAltitude).
    private const double HysteresisFeet = 80.0;

    // -1 = no baseline yet (first sample establishes it silently).
    private int lastAnnouncedBand = -1;

    // The 1,000-ft boundary value last spoken (int.MinValue = none yet). Only used by the
    // same-boundary hover debounce above.
    private int lastAnnouncedThousand = int.MinValue;

    public AltitudeCalloutAnnouncer(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;
    }

    /// <summary>Re-baselines so the next sample is silent (after a teleport / long pause).</summary>
    public void ResetBaseline() { lastAnnouncedBand = -1; lastAnnouncedThousand = int.MinValue; }

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

        // Announce the 1,000-ft boundary just CROSSED — the SAME thousand whether climbing or
        // descending (crossing 4,000 says "4000" both ways). Climbing INTO band N crosses
        // N×1000; descending INTO band N crosses (N+1)×1000. Previously the descending callout
        // spoke the band ENTERED, i.e. one thousand LOW ("3000" the instant you dipped below
        // 4,000 at 3,999) — which understates the real (MSL) altitude and, over ~1,000-ft
        // terrain, coincides with AGL so it READS as AGL. The feed is INDICATED ALTITUDE (MSL);
        // only the announced value is corrected to name the thousand crossed, not the band below.
        bool climbing = newBand > lastAnnouncedBand;
        int crossedThousand = (climbing ? newBand : newBand + 1) * BandFeet;

        // Announce AT the boundary — the band change IS the crossing, so the callout fires when
        // the aircraft actually reaches the round thousand (not ~80 ft past it, which was the old
        // hysteresis-delay bug: "36,000" spoke at ~36,100 climbing / below 36,000 descending).
        // The ONLY suppression is a hover on the SAME thousand we just announced: leveling off on
        // a round thousand flips the band across the line, and those re-crossings must not repeat
        // the callout. A crossing to a DIFFERENT thousand always announces immediately.
        if (crossedThousand == lastAnnouncedThousand &&
            System.Math.Abs(altitudeFeet - (double)crossedThousand) < HysteresisFeet)
        {
            lastAnnouncedBand = newBand;   // track the flip so the hover doesn't re-fire each frame
            return;
        }

        lastAnnouncedBand = newBand;
        lastAnnouncedThousand = crossedThousand;
        // Plain Announce (queued) so a fading altitude callout doesn't displace the most
        // recent actionable instruction in any feature's Repeat-Last buffer. Bare number
        // ("32000", "5000") per Gus's preference; this is the ONLY altitude callout.
        announcer.Announce($"{crossedThousand}");
    }
}
