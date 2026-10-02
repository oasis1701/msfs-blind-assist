using System;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>Auto seat-belt-sign mode (persisted as int in UserSettings.FOAutoSeatbeltMode).</summary>
public enum FoSeatbeltMode { Disabled = 0, TenThousand = 1, TocTod = 2 }

/// <summary>
/// Shared seat-belt-sign automation state machine. Aircraft-agnostic LOGIC; the actual
/// sign is set through the injected <paramref name="setSign"/> sink (true = signs ON),
/// which each aircraft's phase monitor wires to its own SetSeatbeltSign(bool). Fed
/// (altitudeFt MSL, vsFpm, onGround) from the FirstOfficer position feed.
///
/// 10k mode: OFF climbing through 10,000 ft, ON descending through it (300 ft hysteresis).
///
/// TOC/TOD mode: OFF once at Top of Climb, ON once at Top of Descent, re-armed for the next
/// leg ONLY on the ground. Top of Climb is a sustained level-off (|VS| &lt; 200 fpm) that is:
///   - above 10,000 ft;
///   - at the highest altitude this leg has reached (within 500 ft) — a level-off below
///     the peak is a step on the arrival, never a cruise;
///   - after a real climb: at least 3,000 ft gained this leg, so a state started at
///     cruise or in the descent (the FO window first opened there, or a SimBrief load,
///     which resets it) never switches signs the pilot may have set for a reason;
///   - held long enough: 20 s within 2,000 ft of the SimBrief cruise
///     (<see cref="PlannedCruiseFt"/>); 10 minutes when the plan is known but the level is
///     further below it (ATC kept the aircraft low); 3 minutes with no plan. A climb step
///     is usually shorter than those — a judgement, not a measurement.
/// The plan is for ONE flight: it is forgotten on the first ground sample after the
/// aircraft has been airborne, so a stale plan's cruise cannot match the next flight's
/// climb step (a new SimBrief load sets it again).
/// Top of Descent: after Top of Climb, 15 s of VS &lt; -500 fpm AND 1,000 ft lost from the
/// peak (the altitude loss defeats turbulence VS spikes).
///
/// Every duration is CLOCK time, never a sample count: AIRCRAFT_POSITION answers reach
/// every subscriber whichever feature asked (the FO window, the universal automation
/// timer, TCAS …), so counting deliveries made "20 seconds level" a few seconds.
///
/// Why the leg re-arms only on the ground (2026-09-29 report — long arrivals with several
/// level-offs toggled the signs): the old machine re-armed whenever the altitude dipped
/// below 10,000 ft and took ANY level-off above it as Top of Climb, so a climb held at an
/// intermediate level turned the signs off early, an arrival that levelled at a
/// restriction just above 10,000 ft after dipping below it heard "Cruise. Seat belt signs
/// off.", and so did any state started during the descent (a SimBrief load resets it).
/// </summary>
public sealed class SeatbeltAutomation
{
    private readonly Action<bool> _setSign;
    private readonly Action<string> _announce;
    private readonly Func<DateTime> _utcNow;

    public FoSeatbeltMode Mode { get; set; } = FoSeatbeltMode.Disabled;

    /// <summary>The planned cruise altitude (feet, SimBrief initial altitude), or null when
    /// no plan is loaded. Survives <see cref="Reset"/> — the form sets it and then resets.</summary>
    public int? PlannedCruiseFt { get; set; }

    private const int FloorFt = 10_000;
    private const int HysteresisFt = 300;
    private const double TocLevelVsFpm = 200;
    private const double TocLevelSeconds = 20;            // with a SimBrief cruise altitude
    private const double UnplannedTocLevelSeconds = 180;  // without one
    private const double OffPlanTocLevelSeconds = 600;    // plan known, level well below it
    private const double PlannedCruiseBandFt = 2_000;     // ATC's assigned cruise vs the plan
    private const double PeakToleranceFt = 500;
    private const double ClimbEvidenceFt = 3_000;
    private const double TodDescendVsFpm = -500;
    private const double TodDescendSeconds = 15;
    private const double TodAltLossFt = 1_000;

    private bool? _prevAbove10k;   // 10k-mode crossing latch

    // TOC/TOD leg state — cleared on the ground and by Reset().
    private bool _tocDone;
    private bool _todDone;
    private bool _airborneSeen;   // this flight has flown — the plan is forgotten on landing
    private double _peakAltFt = double.NaN;
    private double _lowAltFt = double.NaN;
    private double _maxRiseFt;
    private DateTime? _levelSince;
    private DateTime? _descendSince;

    public SeatbeltAutomation(Action<bool> setSign, Action<string> announce, Func<DateTime>? utcNow = null)
    {
        _setSign = setSign;
        _announce = announce;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void Reset()
    {
        _prevAbove10k = null;
        ResetLeg();
    }

    private void ResetLeg()
    {
        _tocDone = false;
        _todDone = false;
        _peakAltFt = double.NaN;
        _lowAltFt = double.NaN;
        _maxRiseFt = 0;
        _levelSince = null;
        _descendSince = null;
    }

    public void Update(double altitudeFt, double vsFpm, bool onGround)
    {
        switch (Mode)
        {
            case FoSeatbeltMode.TenThousand: Update10k(altitudeFt, vsFpm); break;
            case FoSeatbeltMode.TocTod:      UpdateTocTod(altitudeFt, vsFpm, onGround); break;
            default: break; // Disabled
        }
    }

    private void Update10k(double alt, double vs)
    {
        bool climbing   = vs >  150;
        bool descending = vs < -150;
        bool nowAbove = alt > FloorFt + HysteresisFt;
        bool nowBelow = alt < FloorFt - HysteresisFt;

        if (!descending && nowAbove && _prevAbove10k == false)
        {
            _setSign(false);
            _announce("Above ten thousand. Seat belt signs off.");
        }
        else if (!climbing && nowBelow && _prevAbove10k == true)
        {
            _setSign(true);
            _announce("Below ten thousand. Seat belt signs on.");
        }

        if (nowAbove)      _prevAbove10k = true;
        else if (nowBelow) _prevAbove10k = false;
    }

    private void UpdateTocTod(double alt, double vs, bool onGround)
    {
        // On the ground the next leg starts. Never actuates signs there (belts for taxi,
        // takeoff and landing are the pilot's / the flows' job).
        if (onGround)
        {
            if (_airborneSeen) { PlannedCruiseFt = null; _airborneSeen = false; }
            ResetLeg();
            return;
        }
        // Airborne at or below 0 ft MSL is a bogus reading (flight load, teleport), and
        // nothing actuates that low anyway; kept out so it cannot fake a climb.
        if (double.IsNaN(alt) || double.IsNaN(vs) || alt <= 0) return;
        _airborneSeen = true;

        DateTime now = _utcNow();
        if (double.IsNaN(_peakAltFt) || alt > _peakAltFt) _peakAltFt = alt;
        if (double.IsNaN(_lowAltFt) || alt < _lowAltFt) _lowAltFt = alt;
        _maxRiseFt = Math.Max(_maxRiseFt, alt - _lowAltFt);   // the climb seen this leg

        if (!_tocDone)
        {
            // A level-off below the peak is a step on the way down (or a climb stepped down
            // for traffic), never the cruise — that alone keeps arrival level-offs silent.
            bool levelAtTop = alt >= FloorFt
                && Math.Abs(vs) < TocLevelVsFpm
                && alt >= _peakAltFt - PeakToleranceFt;
            bool climbSeen = _maxRiseFt >= ClimbEvidenceFt;

            if (!(levelAtTop && climbSeen)) { _levelSince = null; return; }

            _levelSince ??= now;
            double required = PlannedCruiseFt is int plan && plan > 0
                ? (alt >= plan - PlannedCruiseBandFt ? TocLevelSeconds : OffPlanTocLevelSeconds)
                : UnplannedTocLevelSeconds;
            if ((now - _levelSince.Value).TotalSeconds >= required)
            {
                _setSign(false);
                _announce("Cruise. Seat belt signs off.");
                _tocDone = true;
            }
            return; // TOD is only evaluated after TOC
        }

        if (!_todDone)
        {
            if (vs >= TodDescendVsFpm) { _descendSince = null; return; }
            _descendSince ??= now;
            if ((now - _descendSince.Value).TotalSeconds >= TodDescendSeconds
                && (_peakAltFt - alt) >= TodAltLossFt)
            {
                _setSign(true);
                _announce("Top of descent. Seat belt signs on.");
                _todDone = true;
            }
        }
    }
}
