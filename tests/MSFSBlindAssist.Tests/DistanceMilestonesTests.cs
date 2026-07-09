// Characterization tests for MSFSBlindAssist.Services.DistanceMilestones.
//
// Ports the golden cases from tools/DistanceUnitsProbe/Program.cs. Milestones are
// UNIT-NATIVE (round numbers in the active display unit, not literal conversions) with
// triggers always in metres internally. Shares the "DistanceUnitGlobalState" collection
// with DistanceFormatterTests since both touch DistanceFormatter.UnitProvider, which is
// process-global mutable state (see DistanceUnitGlobalStateCollection.cs).
//
// This is characterization, not spec verification: values are taken from the probe /
// derived by reasoning about the source and confirmed by running the tests; if a
// literal ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("DistanceUnitGlobalState")]
public class DistanceMilestonesTests
{
    private const double MetresPerFoot = DistanceFormatter.MetresPerFoot;

    // --- ParkingArrival --------------------------------------------------------

    [Fact]
    public void ParkingArrival_metres_labels_and_triggers_are_15_10_5()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;

        var pm = DistanceMilestones.ParkingArrival();

        Assert.Equal("15 metres", pm[0].Label);
        Assert.Equal("10 metres", pm[1].Label);
        Assert.Equal("5 metres", pm[2].Label);
        Assert.Equal(15.0, pm[0].TriggerMetres, 0.01);
        Assert.Equal(10.0, pm[1].TriggerMetres, 0.01);
        Assert.Equal(5.0, pm[2].TriggerMetres, 0.01);
    }

    [Fact]
    public void ParkingArrival_feet_labels_are_50_20_10_with_metre_triggers()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;

        var pf = DistanceMilestones.ParkingArrival();

        Assert.Equal("50 feet", pf[0].Label);
        Assert.Equal("20 feet", pf[1].Label);
        Assert.Equal("10 feet", pf[2].Label);
        Assert.Equal(50 * MetresPerFoot, pf[0].TriggerMetres, 0.01);
    }

    // --- ExitApproach ------------------------------------------------------

    [Fact]
    public void ExitApproach_metres_labels_are_500_300_150()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;

        var xm = DistanceMilestones.ExitApproach();

        Assert.Equal("500 metres", xm[0].Label);
        Assert.Equal("300 metres", xm[1].Label);
        Assert.Equal("150 metres", xm[2].Label);
    }

    [Fact]
    public void ExitApproach_feet_labels_are_1500_900_500_with_metre_triggers()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;

        var xf = DistanceMilestones.ExitApproach();

        Assert.Equal("1500 feet", xf[0].Label);
        Assert.Equal("900 feet", xf[1].Label);
        Assert.Equal("500 feet", xf[2].Label);
        Assert.Equal(1500 * MetresPerFoot, xf[0].TriggerMetres, 0.01);
    }

    // --- RunwayEnd -----------------------------------------------------------

    [Fact]
    public void RunwayEnd_metres_labels_are_500_150_30()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;

        var rm = DistanceMilestones.RunwayEnd();

        Assert.Equal("500 metres", rm[0].Label);
        Assert.Equal("150 metres", rm[1].Label);
        Assert.Equal("30 metres", rm[2].Label);
    }

    [Fact]
    public void RunwayEnd_feet_labels_are_1500_500_100()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;

        var rf = DistanceMilestones.RunwayEnd();

        Assert.Equal("1500 feet", rf[0].Label);
        Assert.Equal("500 feet", rf[1].Label);
        Assert.Equal("100 feet", rf[2].Label);
    }

    // --- Docking (4-tier) ------------------------------------------------------

    [Fact]
    public void Docking_metres_labels_are_30_20_10_5()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;

        var dm = DistanceMilestones.Docking();

        Assert.Equal("30 metres", dm[0].Label);
        Assert.Equal("20 metres", dm[1].Label);
        Assert.Equal("10 metres", dm[2].Label);
        Assert.Equal("5 metres", dm[3].Label);
    }

    [Fact]
    public void Docking_feet_labels_are_100_60_30_15_and_distinct()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;

        var df = DistanceMilestones.Docking();

        Assert.Equal("100 feet", df[0].Label);
        Assert.Equal("60 feet", df[1].Label);
        Assert.Equal("30 feet", df[2].Label);
        Assert.Equal("15 feet", df[3].Label);
        Assert.NotEqual(df[0].Label, df[1].Label);
        Assert.NotEqual(df[1].Label, df[2].Label);
        Assert.NotEqual(df[2].Label, df[3].Label);
    }

    // --- Ordering invariant: every table is FAR -> NEAR --------------------

    [Theory]
    [InlineData(DistanceUnit.Metres)]
    [InlineData(DistanceUnit.Feet)]
    public void All_tables_are_ordered_from_farthest_to_nearest_trigger(DistanceUnit unit)
    {
        DistanceFormatter.UnitProvider = () => unit;

        void AssertDescending(IReadOnlyList<DistanceMilestone> table)
        {
            for (int i = 1; i < table.Count; i++)
                Assert.True(table[i - 1].TriggerMetres > table[i].TriggerMetres,
                    $"expected {table[i - 1]} to trigger farther out than {table[i]}");
        }

        AssertDescending(DistanceMilestones.ExitApproach());
        AssertDescending(DistanceMilestones.RunwayEnd());
        AssertDescending(DistanceMilestones.ParkingArrival());
        AssertDescending(DistanceMilestones.Docking());
    }
}
