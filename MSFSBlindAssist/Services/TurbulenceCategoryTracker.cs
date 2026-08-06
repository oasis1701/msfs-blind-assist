namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure decision logic for the turbulence auto-announcement (spec 2026-07-11 §2.1).
/// Categories reuse the Weather Radar's CategorizeTurbulence boundaries verbatim
/// (≤25 smooth / ≤50 light / ≤75 moderate / ≤90 severe / else extreme). Rising
/// transitions happen at the boundary; easing requires the value to clear each
/// re-crossed boundary by HYSTERESIS points, so a value oscillating on a boundary
/// never flaps. Baseline-first: the first read is silent, and the baseline
/// deliberately survives AS-unreachable gaps (a change across a gap announces).
/// Words only — the raw 1–100 number is never part of an utterance, and smooth
/// (≤25) is never named as a category, per the documented turbulence invariant.
/// </summary>
internal sealed class TurbulenceCategoryTracker
{
    private const double HYSTERESIS = 5.0;

    // Index 0 = smooth (never named), 1..4 = spoken category words.
    private static readonly string[] CategoryWords = { "", "light", "moderate", "severe", "extreme" };

    // Lower boundary of each category: category N is entered when the value
    // exceeds Boundaries[N-1] (light > 25, moderate > 50, severe > 75, extreme > 90).
    private static readonly double[] LowerBoundaries = { 25, 50, 75, 90 };

    private int _category = -1;   // -1 = no baseline yet

    public string? Observe(double turbulence)
    {
        if (double.IsNaN(turbulence)) return null;

        if (_category < 0)
        {
            _category = RawCategory(turbulence);
            return null;                               // baseline-first: silent
        }

        int target = CategoryWithHysteresis(turbulence, _category);
        if (target == _category) return null;

        int previous = _category;
        _category = target;

        if (target == 0) return "Smooth air";
        if (previous == 0) return $"Entering {CategoryWords[target]} turbulence";
        return target > previous
            ? $"Turbulence now {CategoryWords[target]}"
            : $"Turbulence easing to {CategoryWords[target]}";
    }

    public void Reset() => _category = -1;

    private static int RawCategory(double v)
    {
        if (v <= 25) return 0;
        if (v <= 50) return 1;
        if (v <= 75) return 2;
        if (v <= 90) return 3;
        return 4;
    }

    /// <summary>Rising uses raw boundaries; falling steps down only through
    /// boundaries the value clears by the hysteresis margin.</summary>
    private static int CategoryWithHysteresis(double v, int current)
    {
        int raw = RawCategory(v);
        if (raw >= current) return raw;
        int cat = current;
        while (cat > raw && v <= LowerBoundaries[cat - 1] - HYSTERESIS)
            cat--;
        return cat;
    }
}
