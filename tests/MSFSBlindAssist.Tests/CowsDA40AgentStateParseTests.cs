using MSFSBlindAssist.Forms.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ The page jump parsed the agent's answer with four fields after it grew a fifth (the focus
/// index), so jumping to a page spoke "0|Aux - System Setup 1". It has since grown a sixth, the
/// editing flag.
/// </summary>
public class CowsDA40AgentStateParseTests
{
    [Fact]
    public void TheSummaryIsTheSixthFieldOnwards()
    {
        var s = CowsDA40DisplayForm.ParseAgentState("ok|1|AuxSystemSetup|0|0|Aux - System Setup 1");

        Assert.NotNull(s);
        Assert.True(s!.Value.Cursor);
        Assert.Equal("AuxSystemSetup", s.Value.View);
        Assert.Equal("0", s.Value.Focus);
        Assert.False(s.Value.Editing);
        Assert.Equal("Aux - System Setup 1", s.Value.Summary);
    }

    [Fact]
    public void AnOpenFieldIsEditing()
    {
        // The ADF frequency open on the PFD: the large knob moves between its digits.
        var s = CowsDA40DisplayForm.ParseAgentState("ok|1|ADFDME|1|1|0400.0, editing");

        Assert.True(s!.Value.Editing);
        Assert.Equal("0400.0, editing", s.Value.Summary);
    }

    [Fact]
    public void ASummaryContainingABarIsKeptWhole()
        => Assert.Equal("Flight plan | leg 3",
            CowsDA40DisplayForm.ParseAgentState("ok|0|FPL|2|0|Flight plan | leg 3")!.Value.Summary);

    [Theory]
    [InlineData("not available")]
    [InlineData("no instrument")]
    [InlineData("error TypeError")]
    [InlineData("ok|1|AuxSystemSetup|0|Aux - System Setup 1")]
    public void AnythingElseIsNotAState(string answer)
        => Assert.Null(CowsDA40DisplayForm.ParseAgentState(answer));
}
