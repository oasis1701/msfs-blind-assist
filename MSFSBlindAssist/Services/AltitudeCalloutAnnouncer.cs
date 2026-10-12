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
///
/// Under metric altitude (the FBW A380's FCU MTRS) the bands are 300 m instead: about the
/// 1,000-ft rhythm (300 m = 984 ft), round numbers that match the metric flight levels up to
/// 8,400 m, and the unit spoken ("3000 meters") so the switch is audible, as the A / Q altitude
/// read-outs do. The boundary rules are the same in both units.
/// </summary>
public class AltitudeCalloutAnnouncer
{
    private readonly Action<string> announce;

    private const int BandFeet = 1000;
    private const int BandMeters = 300;

    // -1 = no baseline yet (first sample establishes it silently).
    private int lastAnnouncedBand = -1;

    // The boundary value last spoken, in the current unit (int.MinValue = none yet).
    // Re-crossing THIS boundary is always silent — level-off flutter and turbulence at a
    // round-thousand cruise altitude re-cross it constantly, and re-announcing it is noise
    // (user ruling). It re-arms only when a DIFFERENT boundary announces or on a
    // ground/teleport reset.
    private int lastAnnouncedBoundary = int.MinValue;

    // The unit the band state above is in (null = no sample yet). Band numbers mean other
    // heights in the other unit, so a sample in a new unit re-baselines silently.
    private bool? lastMetric;

    public AltitudeCalloutAnnouncer(ScreenReaderAnnouncer screenReaderAnnouncer)
        : this(screenReaderAnnouncer.Announce)
    {
    }

    /// <summary>Announce-sink constructor — the state machine is pure apart from this
    /// sink and the settings gate, so the characterization tests inject a list-collector
    /// here instead of constructing a real ScreenReaderAnnouncer (Tolk/NVDA/SAPI).</summary>
    public AltitudeCalloutAnnouncer(Action<string> announceSink)
    {
        announce = announceSink;
    }

    /// <summary>Re-baselines so the next sample is silent (after a teleport / long pause).</summary>
    public void ResetBaseline() { lastAnnouncedBand = -1; lastAnnouncedBoundary = int.MinValue; }

    /// <summary>
    /// Feed a fresh indicated-altitude sample (feet) plus the current air/ground state, and
    /// whether altitudes read in metres (<paramref name="metric"/>: 300-m bands, see the class
    /// summary). Announces when the band changes, except re-crossings of the last-announced
    /// boundary (always silent). No-op when the setting is off, the value is invalid, or on the
    /// ground.
    /// </summary>
    public void ProcessAltitude(double altitudeFeet, bool onGround, bool metric = false)
    {
        // Full reset on the ground (band AND announced-boundary latch): the next flight's
        // climb through the boundary last heard on approach must announce again.
        if (onGround) { ResetBaseline(); return; }
        if (!SettingsManager.Current.AltitudeCalloutsEnabled) return;
        if (double.IsNaN(altitudeFeet) || altitudeFeet < -2000 || altitudeFeet > 70000) return;

        // The unit changed (MTRS pressed): the bands so far are the other unit's, so this
        // sample is a silent baseline, never a crossing.
        if (lastMetric != metric) { ResetBaseline(); lastMetric = metric; }
        int bandSize = metric ? BandMeters : BandFeet;
        double altitude = metric ? altitudeFeet * 0.3048 : altitudeFeet;

        int newBand = (int)System.Math.Floor(altitude / bandSize);

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
        int crossedBoundary = (climbing ? newBand : newBand + 1) * bandSize;

        // Announce AT the boundary — the band change IS the crossing, so the callout fires when
        // the aircraft actually reaches the round thousand (not ~80 ft past it, which was the old
        // hysteresis-delay bug: "36,000" spoke at ~36,100 climbing / below 36,000 descending).
        // The ONLY suppression is a re-cross of the SAME thousand just announced — always silent,
        // with NO distance window: level-off flutter and turbulence at a round-thousand cruise
        // altitude re-cross it constantly (and turbulent excursions can step >80 ft between
        // samples, so a window can't be trusted to catch them). A crossing to a DIFFERENT
        // thousand always announces immediately, which re-arms the previous one.
        if (crossedBoundary == lastAnnouncedBoundary)
        {
            lastAnnouncedBand = newBand;   // track the flip so the re-cross doesn't re-fire each frame
            return;
        }

        lastAnnouncedBand = newBand;
        lastAnnouncedBoundary = crossedBoundary;
        // Plain Announce (queued) so a fading altitude callout doesn't displace the most
        // recent actionable instruction in any feature's Repeat-Last buffer. Bare number
        // ("32000", "5000") per Gus's preference; this is the ONLY altitude callout. In metres
        // the unit is spoken ("3000 meters"), so a bare number never means metres.
        announce(metric ? $"{crossedBoundary} meters" : $"{crossedBoundary}");
    }
}
