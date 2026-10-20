using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Ctrl+B on the DA40: one field per altimeter, a Set button each, and Set all.</summary>
public class CowsDA40BaroSetTests
{
    /// <summary>
    /// ⚠️ The standby's own Set button wrote nothing: the main field was parsed first and an
    /// empty main returned before the standby was ever read.
    /// </summary>
    [Fact]
    public void TheStandbySetButtonSetsTheStandbyAloneEvenWithTheMainEmpty()
    {
        var plan = CowsDA40Definition.PlanBaroSet(1, "", "29.92");

        Assert.NotNull(plan);
        Assert.Null(plan!.Value.MainInHg);
        Assert.Equal(29.92, plan.Value.StbyInHg!.Value, 2);
    }

    [Fact]
    public void TheMainSetButtonSetsTheMainAlone()
    {
        var plan = CowsDA40Definition.PlanBaroSet(0, "1013", "garbage");

        Assert.Equal(29.91, plan!.Value.MainInHg!.Value, 2);
        Assert.Null(plan.Value.StbyInHg);
    }

    [Fact]
    public void SetAllSetsEachToItsOwnValue()
    {
        var plan = CowsDA40Definition.PlanBaroSet(-1, "30.12", "1013");

        Assert.Equal(30.12, plan!.Value.MainInHg!.Value, 2);
        Assert.Equal(29.91, plan.Value.StbyInHg!.Value, 2);
    }

    [Fact]
    public void NothingReadableSetsNothing()
        => Assert.Null(CowsDA40Definition.PlanBaroSet(1, "29.92", ""));
}
