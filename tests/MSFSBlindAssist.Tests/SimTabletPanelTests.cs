using System.Text.RegularExpressions;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using Outcome = MSFSBlindAssist.Services.SimTabletPanel.Outcome;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The sim's own tablet panel, shown and hidden from its toolbar (STAB-16). Its button only toggles,
/// so every outcome is read from the tablet view's frames; these pin how a press is checked, undone
/// when nothing shows, and never repeated on a panel the pilot already closed.
/// </summary>
public class SimTabletPanelTests
{
    // A tablet view as the reader answers it: whether frames come, and the page's "MSFSBA showed it" mark.
    private sealed class FakeView
    {
        public string Frames = "frames";          // what FrameProbeRead answers: "frames", "none" or ""
        public string? Mark = "false";             // ReadShown's answer; null = no answer
        public readonly List<string> Evals = new();
        public int Toggles;
        public bool ToggleWorks = true;

        public Task<string> Eval(string js)
        {
            Evals.Add(js);
            if (js == SimTabletPanel.FrameProbeStart) return Task.FromResult(Frames.Length == 0 ? "" : "ok");
            if (js == SimTabletPanel.FrameProbeRead) return Task.FromResult(Frames);
            if (js == SimTabletPanel.ReadShown) return Task.FromResult(Mark ?? "");
            if (js == SimTabletPanel.MarkShown) { Mark = "true"; return Task.FromResult("ok"); }
            if (js == SimTabletPanel.MarkHidden) { Mark = "false"; return Task.FromResult("ok"); }
            return Task.FromResult("ok");
        }

        public Task<bool> Toggle() { Toggles++; return Task.FromResult(ToggleWorks); }
    }

    [Fact]
    public async Task Show_WhenFramesCome_IsShown_MarkedInThePage_AndTheKeyboardReleased()
    {
        var v = new FakeView();
        Assert.Equal(Outcome.Shown, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.Equal(1, v.Toggles);
        Assert.Equal("true", v.Mark);
        Assert.Contains(SimTabletPanel.ReleaseKeyboard, v.Evals);
    }

    [Fact]
    public async Task Show_WhenNoFramesCome_UndoesThePress_SoThePanelIsLeftAsItWas()
    {
        var v = new FakeView { Frames = "none" };
        Assert.Equal(Outcome.NotShown, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.Equal(2, v.Toggles);
        Assert.Equal("false", v.Mark);
    }

    [Fact]
    public async Task Show_WithoutTheButton_FailsAndChecksNothing()
    {
        var v = new FakeView { ToggleWorks = false };
        Assert.Equal(Outcome.Failed, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.DoesNotContain(SimTabletPanel.FrameProbeStart, v.Evals);
    }

    [Fact]
    public async Task Show_WhenTheReaderDoesNotAnswer_IsUnknown_AndNeverPressedTwice()
    {
        var v = new FakeView { Frames = "" };
        Assert.Equal(Outcome.Unknown, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.Equal(1, v.Toggles);
    }

    [Fact]
    public async Task Hide_APanelMsfsbaShowedThatStillShows_PressesOnce_AndClearsTheMark()
    {
        var v = new FakeView { Mark = "true" };
        Assert.Equal(Outcome.Hidden, await SimTabletPanel.HideAsync(v.Eval, v.Toggle));
        Assert.Equal(1, v.Toggles);
        Assert.Equal("false", v.Mark);
    }

    [Fact]
    public async Task Hide_APanelThePilotAlreadyClosed_IsNeverPressed()
    {
        var v = new FakeView { Mark = "true", Frames = "none" };
        Assert.Equal(Outcome.AlreadyHidden, await SimTabletPanel.HideAsync(v.Eval, v.Toggle));
        Assert.Equal(0, v.Toggles);
        Assert.Equal("false", v.Mark);
    }

    [Fact]
    public async Task Hide_APanelMsfsbaNeverShowed_IsNeverPressed()
    {
        var v = new FakeView { Mark = "false" };
        Assert.Equal(Outcome.AlreadyHidden, await SimTabletPanel.HideAsync(v.Eval, v.Toggle));
        Assert.Equal(0, v.Toggles);
    }

    [Fact]
    public async Task Hide_WithNoAnswer_OrCancelled_KeepsTheMark_ToTryAgain()
    {
        var silent = new FakeView { Mark = null };
        Assert.Equal(Outcome.Unknown, await SimTabletPanel.HideAsync(silent.Eval, silent.Toggle));

        var reopened = new FakeView { Mark = "true" };
        Assert.Equal(Outcome.Unknown, await SimTabletPanel.HideAsync(reopened.Eval, reopened.Toggle, () => true));
        Assert.Equal(0, reopened.Toggles);
        Assert.Equal("true", reopened.Mark);
    }

    [Fact]
    public void ToolbarView_IsMatchedByItsExactTitle_NeverAViewThatOnlyHoldsTheWord()
    {
        const string list = """
            [{"id":40,"title":"VCockpit01 - Toolbar thing"},{"id":8,"title":"Electronic Flight Bag"},{"id":"10","title":"Toolbar"}]
            """;
        Assert.Equal(10, CoherentEvalClient.ResolvePageId(list, "Toolbar", exact: true));
        Assert.Equal(40, CoherentEvalClient.ResolvePageId(list, "Toolbar"));
    }

    [Theory]
    [InlineData("""{"id":10,"title":"Toolbar"}""")]
    [InlineData("""[{"id":10,"title":7},{"id":1.5,"title":"Toolbar"},"Toolbar"]""")]
    [InlineData("[]")]
    public void APageListOfAnotherShape_FindsNothing_AndNeverThrows(string list)
    {
        Assert.Equal(-1, CoherentEvalClient.ResolvePageId(list, "Toolbar", exact: true));
    }

    [Fact]
    public void TheClientsActionIdxs_AreTheReadersOwn()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-sim-tablet-agent.js"));
        int Idx(string name) => int.Parse(Regex.Match(agent, @"A\." + name + @"\s*=\s*(\d+);").Groups[1].Value);
        Assert.Equal(SimTabletPanel.ActionIdx, Idx("SHOW_TABLET_IDX"));
        Assert.Equal(SimTabletSimBriefImport.ActionIdx, Idx("SIMBRIEF_IDX"));
    }
}
