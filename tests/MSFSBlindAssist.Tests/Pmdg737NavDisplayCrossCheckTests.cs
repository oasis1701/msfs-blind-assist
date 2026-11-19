// The navigation display gate's checks against what the app already trusts: the Captain's EFIS mode and
// range selectors in memory must match the PMDG SDK's (EFIS_ModeSel_0, EFIS_RangeSel_0) in some sample of
// the last 3 s (the SDK lags); nothing counts while the displays are dark; the EFIS object's display units carry their
// own indexes in order. The SDK's distance to the destination is no part of the gate: live at KATL
// (2026-10-25) it held the last waypoint to the destination, which no route node carries.

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.NdTestMemory;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplayCrossCheckTests
{
    private static readonly Pmdg737NavDisplayLayout L = M.Layout;

    /// <summary>The live reading of NdTestMemory: MAP, range index 2, powered, unit 0 on the full ND.</summary>
    private static NavDisplaySample Live()
    {
        using var sampler = new M().Sampler();
        Assert.True(sampler.TryRead(out var sample, out var corrupt), corrupt);
        return sample;
    }

    private static NavDisplaySample WithPanel(NavDisplaySample s, int mode, int range) => s with { Panel = s.Panel with { Mode = mode, Range = range } };

    [Fact]
    public void Mode_and_range_match_in_the_window()
    {
        var s = Live();
        Assert.True(Pmdg737NavDisplayCrossCheck.Passes(new NavDisplaySdkSnapshot(L.Geometry.Modes.Map, 2), new[] { (9_900L, s) }, 10_000));
    }

    [Fact]
    public void A_lagging_sdk_matches_an_older_sample()
    {
        var older = Live();
        var newer = WithPanel(older, L.Geometry.Modes.Map, 3);
        var sdk = new NavDisplaySdkSnapshot(L.Geometry.Modes.Map, 2);

        Assert.True(Pmdg737NavDisplayCrossCheck.Passes(sdk, new[] { (7_500L, older), (9_900L, newer) }, 10_000));
        Assert.False(Pmdg737NavDisplayCrossCheck.Passes(sdk, new[] { (10_000L - Pmdg737NavDisplayCrossCheck.WindowMs - 1, older), (9_900L, newer) }, 10_000));
    }

    [Fact]
    public void A_mode_mismatch_fails()
    {
        var s = Live();
        Assert.False(Pmdg737NavDisplayCrossCheck.Passes(new NavDisplaySdkSnapshot(L.Geometry.Modes.Plan, 2), new[] { (9_900L, s) }, 10_000));
    }

    [Fact]
    public void Mode_and_range_must_match_in_the_same_sample()
    {
        var mapAt2 = Live();
        var planAt3 = WithPanel(mapAt2, L.Geometry.Modes.Plan, 3);
        var sdk = new NavDisplaySdkSnapshot(L.Geometry.Modes.Plan, 2);
        Assert.False(Pmdg737NavDisplayCrossCheck.Passes(sdk, new[] { (9_000L, mapAt2), (9_900L, planAt3) }, 10_000));
    }

    [Fact]
    public void Range_index_is_the_sdk_index_not_the_label()
    {
        // Index 2 is 20 NM on the knob; the ND prints "10" (half the range) on its ring.
        var s = Live();
        var map = L.Geometry.Modes.Map;
        Assert.True(Pmdg737NavDisplayCrossCheck.Passes(new NavDisplaySdkSnapshot(map, 2), new[] { (9_900L, s) }, 10_000));
        Assert.False(Pmdg737NavDisplayCrossCheck.Passes(new NavDisplaySdkSnapshot(map, 20), new[] { (9_900L, s) }, 10_000));
        Assert.False(Pmdg737NavDisplayCrossCheck.Passes(new NavDisplaySdkSnapshot(map, 10), new[] { (9_900L, s) }, 10_000));
    }

    [Fact]
    public void No_history_fails()
    {
        Assert.False(Pmdg737NavDisplayCrossCheck.Passes(new NavDisplaySdkSnapshot(L.Geometry.Modes.Map, 2), Array.Empty<(long, NavDisplaySample)>(), 10_000));
    }

    [Fact]
    public void Dark_displays_are_not_lit()
    {
        var s = Live();
        var dark = s with { UnitPages = s.UnitPages.Select(_ => L.Units.DarkPages[0]).ToList() };
        Assert.False(Pmdg737NavDisplayCrossCheck.DisplaysLit(dark, L));
        // Cold and dark live (2026-10-25): every unit on page 0 and the panel unpowered.
        var unpowered = s with { Panel = s.Panel with { Powered = false } };
        Assert.False(Pmdg737NavDisplayCrossCheck.DisplaysLit(unpowered, L));
    }

    [Fact]
    public void Lit_when_the_captain_nd_unit_shows_a_page()
    {
        var s = Live();
        Assert.True(Pmdg737NavDisplayCrossCheck.DisplaysLit(s, L));
        // Ground power live (2026-10-25): pages PFD, ND, engines… on the six units.
        var ground = s with { UnitPages = new[] { 2, 3, 5, 6, 3, 2 } };
        Assert.True(Pmdg737NavDisplayCrossCheck.DisplaysLit(ground, L));
    }

    [Fact]
    public void Unit_indices_in_order_are_the_fingerprint()
    {
        var s = Live();
        Assert.True(Pmdg737NavDisplayCrossCheck.Fingerprint(s));
        Assert.False(Pmdg737NavDisplayCrossCheck.Fingerprint(s with { UnitIndices = new[] { 0, 1, 2, 3, 5, 4 } }));
        Assert.False(Pmdg737NavDisplayCrossCheck.Fingerprint(s with { UnitIndices = new[] { 0, 1, 2 } }));
    }
}
