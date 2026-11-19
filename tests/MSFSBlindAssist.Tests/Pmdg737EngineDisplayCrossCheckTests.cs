// The engine display gate's SDK cross-check: each tank quantity the PMDG SDK reports must match one memory
// sample of the last 3 s within 1 lb (measured live 2026-10-24: memory and SDK agree to 0.02 lb with fuel
// burning), left being tank 1 and right tank 2; and the display units' fingerprint: their indexes 0 to 5.

using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737EngineDisplayCrossCheckTests
{
    private static EngineDisplaySample Sample(double center, double tank1, double tank2, params int[] units) =>
        EngineDisplaySampleFactory.Idle() with
        {
            FuelQuantity = new[] { center, tank1, tank2 },
            UnitIndices = units.Length > 0 ? units : new[] { 0, 1, 2, 3, 4, 5 },
        };

    private static readonly EngineDisplaySdkSnapshot Sdk = new(FuelLeftLb: 8620.771, FuelCenterLb: 7812.977, FuelRightLb: 8628.594);

    [Fact]
    public void Tanks_match_within_tolerance_in_the_window()
    {
        var history = new[] { (1000L, Sample(7813.5, 8620.2, 8629.0)) };
        Assert.True(Pmdg737EngineDisplayCrossCheck.Passes(Sdk, history, 1000));
        Assert.Equal(1.0, Pmdg737EngineDisplayCrossCheck.ToleranceLb);
    }

    [Fact]
    public void A_lagging_sdk_matches_an_older_sample()
    {
        // Memory has moved on by 5 lb; the SDK still shows what memory held 2 s ago.
        var history = new[] { (0L, Sample(7812.977, 8620.771, 8628.594)), (2000L, Sample(7812.977, 8615.0, 8628.594)) };
        Assert.True(Pmdg737EngineDisplayCrossCheck.Passes(Sdk, history, 2000));
        Assert.False(Pmdg737EngineDisplayCrossCheck.Passes(Sdk, history, 3001)); // the older sample is out of the window
    }

    [Fact]
    public void A_tank_off_by_more_than_tolerance_fails()
    {
        Assert.False(Pmdg737EngineDisplayCrossCheck.Passes(Sdk, new[] { (0L, Sample(7812.977, 8620.771, 8630.0)) }, 0));
    }

    [Fact]
    public void Left_is_tank_1_and_right_is_tank_2()
    {
        var swapped = Sample(7812.977, 8628.594, 8620.771);
        Assert.False(Pmdg737EngineDisplayCrossCheck.Passes(Sdk, new[] { (0L, swapped) }, 0));
        Assert.False(Pmdg737EngineDisplayCrossCheck.Passes(Sdk with { FuelCenterLb = 8620.771, FuelLeftLb = 7812.977 }, new[] { (0L, Sample(7812.977, 8620.771, 8628.594)) }, 0));
    }

    [Fact]
    public void No_history_fails()
    {
        Assert.False(Pmdg737EngineDisplayCrossCheck.Passes(Sdk, Array.Empty<(long, EngineDisplaySample)>(), 0));
    }

    [Fact]
    public void Fingerprint_needs_unit_indices_0_to_5()
    {
        Assert.True(Pmdg737EngineDisplayCrossCheck.Fingerprint(Sample(0, 0, 0)));
        Assert.False(Pmdg737EngineDisplayCrossCheck.Fingerprint(Sample(0, 0, 0, 0, 1, 2, 3, 5, 4)));
        Assert.False(Pmdg737EngineDisplayCrossCheck.Fingerprint(Sample(0, 0, 0, 0, 1, 2)));
    }
}
