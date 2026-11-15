using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The sim's own tablet panel, shown from its toolbar (STAB-16): these pin how the toolbar view is
/// found in the debugger's page list and how the one toggle reply is read.
/// </summary>
public class SimTabletPanelTests
{
    [Fact]
    public void FindToolbarPage_PicksTheViewTitledToolbar_ByExactTitle()
    {
        const string list = """
            [{"id":8,"title":"Electronic Flight Bag"},{"id":9,"title":"Mission Steps"},
             {"id":10,"title":"Toolbar"},{"id":40,"title":"VCockpit01 - Toolbar thing"}]
            """;
        Assert.Equal(10, SimTabletPanel.FindToolbarPage(list));
    }

    [Fact]
    public void FindToolbarPage_ReadsAnIdGivenAsText()
    {
        Assert.Equal(12, SimTabletPanel.FindToolbarPage("""[{"id":"12","title":"Toolbar"}]"""));
    }

    [Theory]
    [InlineData("""[{"id":8,"title":"Electronic Flight Bag"}]""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void FindToolbarPage_IsNull_WithoutAToolbarView(string list)
    {
        Assert.Null(SimTabletPanel.FindToolbarPage(list));
    }

    [Fact]
    public void ReplyValue_ReadsTheToggleReply()
    {
        Assert.Equal("clicked", SimTabletPanel.ReplyValue("""{"id":1,"result":{"result":{"type":"string","value":"clicked"}}}"""));
        Assert.Equal("no-icon", SimTabletPanel.ReplyValue("""{"id":1,"result":{"result":{"type":"string","value":"no-icon"}}}"""));
    }

    [Fact]
    public void ReplyValue_SkipsOtherMessages_AndCallsAThrownReplyAnError()
    {
        Assert.Equal("", SimTabletPanel.ReplyValue("""{"method":"Runtime.executionContextCreated","params":{}}"""));
        Assert.Equal("", SimTabletPanel.ReplyValue("""{"id":2,"result":{"result":{"type":"string","value":"clicked"}}}"""));
        Assert.Equal("error", SimTabletPanel.ReplyValue("""{"id":1,"result":{"result":{"type":"object","subtype":"error"},"wasThrown":true}}"""));
        Assert.Equal("", SimTabletPanel.ReplyValue("{broken"));
    }
}
