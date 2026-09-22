// MSFSBlindAssist/Aircraft/C172/Cessna172Warnings.cs
namespace MSFSBlindAssist.Aircraft.C172;

/// <summary>C172 POH-derived limits behind the background warnings (spec §5).</summary>
public static class Cessna172Limits
{
    public const double FuelLowGallons = 5.0;
    public const double FuelRearmGallons = 6.0;
    public const double VoltsLow = 24.0;            // 28 V system; the battery alone reads ~24 V
    public const double VoltsRearm = 25.0;
    public const double OilPressureLowPsi = 20.0;   // POH minimum at idle
    public const double OilPressureRearmPsi = 25.0;
    public const double OilTempHighF = 245.0;       // POH red line
    public const double OilTempRearmF = 235.0;
    /// <summary>"Engine stopped" on the ground only above this IAS — a parked shutdown is not a failure.</summary>
    public const double EngineStoppedMinIasKnots = 20.0;
}

/// <summary>
/// One-shot rising-edge detector, baseline-first: the first sample only records the level, so a
/// flag already raised when the app connects is never spoken as though it had just come on.
/// Re-arms when the flag falls.
/// </summary>
public sealed class RisingEdge
{
    private bool? _last;

    /// <summary>True exactly once per false→true transition after the baseline.</summary>
    public bool Feed(bool active)
    {
        bool fires = _last == false && active;
        _last = active;
        return fires;
    }

    public void Reset() => _last = null;
}

/// <summary>
/// One-shot threshold warning with hysteresis, baseline-first, optionally gated. Trips once when
/// the value crosses <c>triggerAt</c> (below it when <c>triggerBelow</c>, above it otherwise)
/// and re-arms silently once the value crosses <c>rearmAt</c> back the other way. A sample taken
/// while <c>enabled</c> is false is ignored entirely (it is neither a baseline nor an event), so
/// the first ENABLED sample is the baseline. A baseline sample already out of limits trips
/// without speaking — the state is true but was not a change.
/// </summary>
public sealed class ThresholdWarning
{
    private readonly double _triggerAt;
    private readonly double _rearmAt;
    private readonly bool _triggerBelow;
    private bool _seeded;
    private bool _tripped;

    public ThresholdWarning(double triggerAt, double rearmAt, bool triggerBelow)
    {
        _triggerAt = triggerAt;
        _rearmAt = rearmAt;
        _triggerBelow = triggerBelow;
    }

    public bool IsTripped => _tripped;

    /// <summary>True exactly once per excursion, and never on the baseline sample.</summary>
    public bool Feed(double value, bool enabled)
    {
        if (!enabled) return false;
        bool beyondTrigger = _triggerBelow ? value < _triggerAt : value > _triggerAt;
        bool beyondRearm = _triggerBelow ? value > _rearmAt : value < _rearmAt;

        if (!_seeded)
        {
            _seeded = true;
            _tripped = beyondTrigger;
            return false;
        }
        if (_tripped)
        {
            if (beyondRearm) _tripped = false;
            return false;
        }
        if (beyondTrigger)
        {
            _tripped = true;
            return true;
        }
        return false;
    }

    public void Reset()
    {
        _seeded = false;
        _tripped = false;
    }
}
