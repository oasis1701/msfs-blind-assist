using MSFSBlindAssist.Forms.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ The page jump parsed the agent's answer with four fields after it grew a fifth (the focus
/// index), so jumping to a page spoke "0|Aux - System Setup 1".
/// </summary>
public class CowsDA40AgentStateParseTests
{
    [Fact]
    public void TheSummaryIsTheFifthFieldOnwards()
    {
        var s = CowsDA40DisplayForm.ParseAgentState("ok|1|AuxSystemSetup|0|Aux - System Setup 1");

        Assert.NotNull(s);
        Assert.True(s!.Value.Cursor);
        Assert.Equal("AuxSystemSetup", s.Value.View);
        Assert.Equal("0", s.Value.Focus);
        Assert.Equal("Aux - System Setup 1", s.Value.Summary);
    }

    [Fact]
    public void ASummaryContainingABarIsKeptWhole()
        => Assert.Equal("Flight plan | leg 3",
            CowsDA40DisplayForm.ParseAgentState("ok|0|FPL|2|Flight plan | leg 3")!.Value.Summary);

    [Theory]
    [InlineData("not available")]
    [InlineData("no instrument")]
    [InlineData("error TypeError")]
    public void AnythingElseIsNotAState(string answer)
        => Assert.Null(CowsDA40DisplayForm.ParseAgentState(answer));
}
