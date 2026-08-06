// Pins the turbulence announcement rules (spec 2026-07-11 §2.1): baseline-first
// silence, category boundaries identical to the radar form's CategorizeTurbulence
// (≤25 smooth — never named — / ≤50 light / ≤75 moderate / ≤90 severe / else
// extreme), rising at the boundary, easing only 5 points below each re-crossed
// boundary, words only (never the raw number).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class TurbulenceCategoryTrackerTests
{
    [Fact]
    public void First_read_baselines_silently_even_inside_turbulence()
    {
        var t = new TurbulenceCategoryTracker();
        Assert.Null(t.Observe(60));                    // moderate at startup: silent
        Assert.Null(t.Observe(60));                    // unchanged: silent
    }

    [Fact]
    public void Entering_from_smooth_names_the_category()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(10);                                 // baseline smooth
        Assert.Equal("Entering light turbulence", t.Observe(30));
    }

    [Fact]
    public void Jump_from_smooth_straight_to_moderate_announces_moderate()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(10);
        Assert.Equal("Entering moderate turbulence", t.Observe(60));
    }

    [Fact]
    public void Worsening_between_categories_says_now()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);                                 // baseline light
        Assert.Equal("Turbulence now moderate", t.Observe(60));
        Assert.Equal("Turbulence now extreme", t.Observe(95));
    }

    [Fact]
    public void Easing_between_categories_says_easing()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(95);                                 // baseline extreme
        Assert.Equal("Turbulence easing to moderate", t.Observe(60));
    }

    [Fact]
    public void Easing_to_smooth_says_smooth_air()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);                                 // baseline light
        Assert.Equal("Smooth air", t.Observe(10));
    }

    [Fact]
    public void Rising_edge_is_exactly_the_boundary()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(10);
        Assert.Null(t.Observe(25));                    // 25 is still smooth
        Assert.Equal("Entering light turbulence", t.Observe(26));
    }

    [Fact]
    public void Easing_needs_five_points_below_the_boundary()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);                                 // light
        Assert.Null(t.Observe(25));                    // raw smooth, but > 20: stays light
        Assert.Null(t.Observe(21));                    // still inside the margin
        Assert.Equal("Smooth air", t.Observe(20));     // cleared 25 − 5
    }

    [Fact]
    public void Boundary_oscillation_never_flaps()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(10);
        Assert.Equal("Entering light turbulence", t.Observe(26));
        Assert.Null(t.Observe(25));
        Assert.Null(t.Observe(26));
        Assert.Null(t.Observe(24));
        Assert.Null(t.Observe(27));                    // never re-announced
    }

    [Fact]
    public void Deep_drop_steps_only_through_cleared_boundaries()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(60);                                 // moderate
        // 22 clears the 50-boundary (≤45) but NOT the 25-boundary (needs ≤20):
        Assert.Equal("Turbulence easing to light", t.Observe(22));
        Assert.Equal("Smooth air", t.Observe(19));
    }

    [Fact]
    public void Gap_survival_a_change_across_a_gap_announces()
    {
        // The monitor never calls Observe while AS is unreachable; the baseline
        // survives, so a genuine category change across the gap announces.
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);                                 // light, then gap
        Assert.Equal("Turbulence now severe", t.Observe(80));
    }

    [Fact]
    public void Reset_rebaselines_silently()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);
        t.Reset();
        Assert.Null(t.Observe(80));                    // first read after reset: silent
        Assert.Equal("Turbulence now extreme", t.Observe(95));
    }
}
