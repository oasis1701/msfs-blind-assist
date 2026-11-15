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
    // The tablet's view and the sim's panel, as the reader and the toolbar answer them.
    private sealed class FakeView
    {
        public bool Shown;                  // the sim draws the panel, so frames come
        public bool Draws = true;           // a press changes what the sim draws (false: paused, in a menu)
        public bool ToggleWorks = true;     // the toolbar's button was reached and pressed
        public bool ReaderAnswers = true;
        public bool ProbeAnswers = true;
        public bool ProbeDiesOnPress;       // the reader stops answering once the button is pressed
        public string Mark = "false";       // the page's "MSFSBA showed it"
        public readonly List<string> Evals = new();
        public int Toggles;

        public Task<string> Eval(string js)
        {
            Evals.Add(js);
            string r = js switch
            {
                _ when js == SimTabletPanel.FrameProbeStart => ProbeAnswers && ReaderAnswers ? "ok" : "",
                _ when js == SimTabletPanel.FrameProbeRead => Shown ? "frames" : "none",
                _ when js == SimTabletPanel.ReadShown => ReaderAnswers ? Mark : "",
                _ when js == SimTabletPanel.MarkShown => Set("true"),
                _ when js == SimTabletPanel.MarkHidden => Set("false"),
                _ => ReaderAnswers ? "ok" : "",
            };
            return Task.FromResult(r);
        }

        private string Set(string mark) { Mark = mark; return "ok"; }

        public Task<bool> Toggle()
        {
            Toggles++;
            Evals.Add("toggle");
            if (ToggleWorks && Draws) Shown = !Shown;
            if (ToggleWorks && ProbeDiesOnPress) ProbeAnswers = false;
            return Task.FromResult(ToggleWorks);
        }

        public bool StampedBeforeEveryPress()
        {
            for (int i = 0; i < Evals.Count; i++)
                if (Evals[i] == "toggle" && (i == 0 || Evals[i - 1] != SimTabletPanel.StampPress)) return false;
            return true;
        }
    }

    [Fact]
    public async Task Show_WhenFramesCome_IsShown_MarkedInThePage_AndTheKeyboardReleased()
    {
        var v = new FakeView();
        Assert.Equal(Outcome.Shown, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.Equal(1, v.Toggles);
        Assert.True(v.Shown);
        Assert.Equal("true", v.Mark);
        Assert.Contains(SimTabletPanel.ReleaseKeyboard, v.Evals);
        Assert.True(v.StampedBeforeEveryPress(), "the reader keeps the button listed while frames start");
    }

    [Fact]
    public async Task Show_WhenTheSimDrawsNothing_UndoesThePress_SoThePanelIsLeftAsItWas()
    {
        var v = new FakeView { Draws = false };
        Assert.Equal(Outcome.NotShown, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.Equal(2, v.Toggles);
        Assert.Equal("false", v.Mark);
        Assert.True(v.StampedBeforeEveryPress());
    }

    [Fact]
    public async Task Show_WithoutTheButton_FailsAndChecksNothing()
    {
        var v = new FakeView { ToggleWorks = false };
        Assert.Equal(Outcome.Failed, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.DoesNotContain(SimTabletPanel.FrameProbeStart, v.Evals);
    }

    [Fact]
    public async Task Show_WhileTheReaderDoesNotAnswer_PressesNothing()
    {
        var v = new FakeView { ReaderAnswers = false };
        Assert.Equal(Outcome.Unknown, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.Equal(0, v.Toggles);
    }

    [Fact]
    public async Task Show_WhenTheFrameCheckGetsNoAnswer_IsUnverified_AndNeverPressedTwice()
    {
        var v = new FakeView { ProbeAnswers = false };
        Assert.Equal(Outcome.Unverified, await SimTabletPanel.ShowAsync(v.Eval, v.Toggle));
        Assert.Equal(1, v.Toggles);
    }

    [Fact]
    public async Task Hide_APanelMsfsbaShowedThatStillShows_PressesOnce_AndCountsOnlyOnceFramesStop()
    {
        var v = new FakeView { Shown = true, Mark = "true" };
        Assert.Equal(Outcome.Hidden, await SimTabletPanel.HideAsync(v.Eval, v.Toggle));
        Assert.Equal(1, v.Toggles);
        Assert.False(v.Shown);
        Assert.Equal("false", v.Mark);
        Assert.True(v.StampedBeforeEveryPress());
    }

    [Fact]
    public async Task Hide_WhenThePressHidesNothing_IsStillShown_AndKeepsTheMark()
    {
        var v = new FakeView { Shown = true, Mark = "true", Draws = false };
        Assert.Equal(Outcome.StillShown, await SimTabletPanel.HideAsync(v.Eval, v.Toggle));
        Assert.Equal(1, v.Toggles);
        Assert.Equal("true", v.Mark);
    }

    [Fact]
    public async Task Hide_WhenTheCheckAfterThePressGetsNoAnswer_IsUnverified_NeverNothingPressed()
    {
        var v = new FakeView { Shown = true, Mark = "true", ProbeDiesOnPress = true };
        Assert.Equal(Outcome.Unverified, await SimTabletPanel.HideAsync(v.Eval, v.Toggle));
        Assert.Equal(1, v.Toggles);
        Assert.Equal("true", v.Mark);
    }

    [Fact]
    public async Task Hide_OnExit_DoesNotWaitForTheFramesToStop()
    {
        var v = new FakeView { Shown = true, Mark = "true", Draws = false };
        Assert.Equal(Outcome.Hidden, await SimTabletPanel.HideAsync(v.Eval, v.Toggle, verify: false));
        Assert.Equal(1, v.Toggles);
    }

    [Fact]
    public async Task Hide_APanelThePilotAlreadyClosed_IsNeverPressed()
    {
        var v = new FakeView { Shown = false, Mark = "true" };
        Assert.Equal(Outcome.AlreadyHidden, await SimTabletPanel.HideAsync(v.Eval, v.Toggle));
        Assert.Equal(0, v.Toggles);
        Assert.Equal("false", v.Mark);
    }

    [Fact]
    public async Task Hide_APanelMsfsbaNeverShowed_IsNeverPressed()
    {
        var v = new FakeView { Shown = true, Mark = "false" };
        Assert.Equal(Outcome.AlreadyHidden, await SimTabletPanel.HideAsync(v.Eval, v.Toggle));
        Assert.Equal(0, v.Toggles);
    }

    [Fact]
    public async Task Hide_WithNoAnswer_OrCancelled_KeepsTheMark_ToTryAgain()
    {
        var silent = new FakeView { Shown = true, Mark = "true", ReaderAnswers = false };
        Assert.Equal(Outcome.Unknown, await SimTabletPanel.HideAsync(silent.Eval, silent.Toggle));
        Assert.Equal(0, silent.Toggles);

        var reopened = new FakeView { Shown = true, Mark = "true" };
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
