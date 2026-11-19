using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.BBS757;

/// <summary>Plan 10, task 1: the 757 First Officer profile, as the MD-11's.</summary>
public class Bbs757FoProfileTests
{
    private static Bbs757FoProfile Profile() => new(new BBS757Definition());

    /// <summary>
    /// The window's own title, in the form every profile uses ("First Officer — &lt;aircraft&gt;");
    /// "BlueBird 757 First Officer" is the menu/window-registry name (FirstOfficerWindowsTests).
    /// </summary>
    [Fact]
    public void Title_NamesTheBlueBird757()
        => Assert.Equal("First Officer — BlueBird 757", Profile().Title);

    [Fact]
    public void TheEvaluatorIsCreatedOnce_AndIsTheExecutorsState()
    {
        var p = Profile();
        var state = p.CreateEvaluator();
        Assert.Same(state, p.CreateEvaluator());
        Assert.Same(state, p.CreateExecutor().State);
    }

    [Fact]
    public void WithoutSimConnect_TheExecutorIsUnavailable()
    {
        var p = Profile();
        var exec = p.CreateExecutor();
        p.SetExecutorSimConnect(exec, null);
        Assert.False(exec.IsAvailable);
    }

    [Fact]
    public async System.Threading.Tasks.Task WithoutSimConnect_EveryWriteRefuses()
    {
        var p = Profile();
        var exec = p.CreateExecutor();
        p.SetExecutorSimConnect(exec, null);
        Assert.False(await exec.ExecuteStepAsync(new Bbs757Step { EventName = Bbs757FoKeys.Battery, TargetValue = 1 }));
        Assert.False(await exec.Set(Bbs757FoKeys.SeatBelts, 2));
        await exec.WaitForDispatchDrainAsync();   // an idle gate drains at once
    }

    [Fact]
    public void BuildsTheFlowsAndChecklists()
    {
        var p = Profile();
        Assert.Equal(12, p.BuildFlows().Count);        // the arrival is Descent and Before Landing (owner, 2026-11-08)
        Assert.Equal(20, p.BuildChecklists().Count);   // In Range and Approach one checklist; no Approach group
    }

    [Fact]
    public void EveryFlowHasAnActionGroupOfTheSameId()
    {
        var p = Profile();
        var groups = p.BuildChecklists().Select(g => g.Id).ToHashSet();
        Assert.All(p.BuildFlows(), f => Assert.Contains(f.Id, groups));
    }

    [Fact]
    public void AnUnboundEvaluatorReadsEverythingUnread()
    {
        var state = Profile().CreateEvaluator();
        Assert.False(state.IsAvailable);
        Assert.True(double.IsNaN(state.GetValue("FO_GEAR_DOWN")));
        Assert.True(double.IsNaN(state.GetValue("FO_ENG1_N2")));    // never a guessed 0 (FO-20)
        Assert.True(double.IsNaN(state.GetValue(Bbs757FoKeys.SeatBelts)));
        Assert.Null(state.IsPrattAndWhitney);
        Assert.Null(state.OnGround);
    }
}
