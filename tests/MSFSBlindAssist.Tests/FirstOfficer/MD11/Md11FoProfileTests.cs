using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.MD11;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

public class Md11FoProfileTests
{
    private static Md11FoProfile Profile() => new(new TFDiMD11Definition());

    [Fact]
    public void Title_NamesTheMd11()
    {
        Assert.Equal("First Officer — TFDi MD-11", Profile().Title);
    }

    [Fact]
    public void TheEvaluatorIsCreatedOnce_AndIsTheExecutorsFlightState()
    {
        var p = Profile();
        var state = p.CreateEvaluator();
        Assert.Same(state, p.CreateEvaluator());
        var exec = p.CreateExecutor();
        Assert.Same(state, exec.FlightState);
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
    public void BuildsTheFlowsAndChecklists()
    {
        var p = Profile();
        Assert.Equal(12, p.BuildFlows().Count);
        Assert.Equal(28, p.BuildChecklists().Count);
    }
}
