using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The per-cylinder states the XLS's Mixture and Propeller panel speaks - plug fouling,
/// detonation, shock cooling and cylinder damage - each transcribed from the model.
/// </summary>
public class DA40CylinderStateTests
{
    // ---- plug fouling: DAMAGE_MAG_FOUL:nR / :nL, 0-100 per plug, eight of them ----

    [Fact]
    public void FoulingNamesTheWorstPlug()
    {
        // Order: 1R 1L 2R 2L 3R 3L 4R 4L.
        var plugs = new[] { 1.0, 1.1, 12.4, 0.9, 0.5, 0.4, 0.2, 0.1 };
        Assert.Equal("Worst plug cylinder 2 right, 12 percent fouled", DA40CylinderState.DescribeFouling(plugs));
        Assert.Equal("Clean", DA40CylinderState.DescribeFouling(new double[8]));
    }

    [Fact]
    public void FoulingOnsetIsTheGradedRule_TwentyFivePoints()
    {
        // Same shape as the coolant leak: speak the onset, and a material worsening.
        Assert.Null(DA40CylinderState.FoulingCallout(previousWorst: 10, worst: 24, plugName: "cylinder 2 right"));
        Assert.Equal("Plug fouling, cylinder 2 right at 25 percent",
            DA40CylinderState.FoulingCallout(previousWorst: 24, worst: 25, plugName: "cylinder 2 right"));
        Assert.Equal("Plug fouling worsening, cylinder 2 right at 50 percent",
            DA40CylinderState.FoulingCallout(previousWorst: 25, worst: 50, plugName: "cylinder 2 right"));
        Assert.Null(DA40CylinderState.FoulingCallout(previousWorst: 50, worst: 60, plugName: "cylinder 2 right"));
    }

    // ---- shock cooling: CHT_TEMP_INC:n under -0.15 damages the cylinder (Logic) ----

    [Fact]
    public void ShockCoolingIsTheModelsOwnThreshold()
    {
        Assert.Equal("None", DA40CylinderState.DescribeShockCooling(new[] { -0.1, -0.14, 0.0, 0.2 }));
        Assert.Equal("Shock cooling, cylinder 2", DA40CylinderState.DescribeShockCooling(new[] { -0.1, -0.16, 0.0, 0.2 }));
    }

    [Fact]
    public void AShockCoolingEpisodeIsNamedOnceAndOnlyNewCylindersAfter()
    {
        // VCBI climb-out, 2026-10-19, a power cut from full to 37 percent: cylinder 1, then 1-2,
        // 1-3 and 1-4 inside a second, then 1-3, 2-3 and 3 as they recovered - seven calls in
        // nine seconds. The episode is gathered for GatherMs and named once; a cylinder joining
        // later is named alone; one leaving is never said.
        var t0 = new System.DateTime(2026, 10, 19, 10, 16, 59, 633, System.DateTimeKind.Utc);
        var c = new ShockCoolingCallout();
        string?[] said =
        {
            c.Step(new[] { 1 }, t0),
            c.Step(new[] { 1, 2 }, t0.AddMilliseconds(6)),
            c.Step(new[] { 1, 2, 3 }, t0.AddMilliseconds(6)),
            c.Step(new[] { 1, 2, 3, 4 }, t0.AddMilliseconds(1000)),
            c.Step(new[] { 1, 2, 3, 4 }, t0.AddMilliseconds(ShockCoolingCallout.GatherMs)),
            c.Step(new[] { 1, 2, 3 }, t0.AddSeconds(8)),
            c.Step(new[] { 2, 3 }, t0.AddSeconds(9)),
            c.Step(new[] { 3 }, t0.AddSeconds(9)),
        };
        Assert.Equal(new string?[] { null, null, null, null, "Shock cooling, cylinders 1, 2, 3 and 4", null, null, null }, said);
    }

    [Fact]
    public void ACylinderJoiningLaterIsNamedAloneAndAClearedEpisodeCanSpeakAgain()
    {
        var t0 = new System.DateTime(2026, 10, 19, 12, 0, 0, System.DateTimeKind.Utc);
        var c = new ShockCoolingCallout();
        c.Step(new[] { 3 }, t0);
        Assert.Equal("Shock cooling, cylinder 3", c.Step(new[] { 3 }, t0.AddMilliseconds(ShockCoolingCallout.GatherMs)));
        Assert.Null(c.Step(new int[0], t0.AddSeconds(4)));          // gone: never said
        Assert.Null(c.Step(new[] { 3 }, t0.AddSeconds(6)));          // back on the line: same episode
        c.Step(new[] { 3, 4 }, t0.AddSeconds(10));
        Assert.Equal("Shock cooling, cylinder 4", c.Step(new[] { 3, 4 }, t0.AddSeconds(13)));
        // A minute clear ends the episode; the next onset is news again.
        Assert.Null(c.Step(new int[0], t0.AddSeconds(13 + ShockCoolingCallout.EpisodeEndMs / 1000.0)));
        c.Step(new[] { 3 }, t0.AddMinutes(5));
        Assert.Equal("Shock cooling, cylinder 3", c.Step(new[] { 3 }, t0.AddMinutes(5).AddMilliseconds(ShockCoolingCallout.GatherMs)));
    }

    // ---- damage: DAMAGE_CYL:n 0-100, dead over 99 ----

    [Fact]
    public void CylinderHealthIsHundredMinusDamage_AndNamesADeadOne()
    {
        Assert.Equal("All four at 100 percent", DA40CylinderState.DescribeHealth(new double[4]));
        Assert.Equal("Cylinder 3 at 88 percent, the rest at 100",
            DA40CylinderState.DescribeHealth(new[] { 0.0, 0.0, 12.0, 0.0 }));
        Assert.Equal("Cylinder 1 dead, cylinder 3 at 88 percent, the rest at 100",
            DA40CylinderState.DescribeHealth(new[] { 99.5, 0.0, 12.0, 0.0 }));
    }
}
