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
