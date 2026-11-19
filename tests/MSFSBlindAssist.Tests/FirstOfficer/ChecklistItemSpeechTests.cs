using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class ChecklistItemSpeechTests
{
    // CORE-7: NVDA speaks the tree's own "checked" / "not checked", so a tick never repeats
    // the label or the state.
    [Fact]
    public void Tick_without_a_value_adds_nothing_to_the_screen_readers_own_checked()
    {
        Assert.Null(ChecklistItemSpeech.TickText(null));
        Assert.Null(ChecklistItemSpeech.TickText(""));
        Assert.Null(ChecklistItemSpeech.TickText("  "));
    }

    [Fact]
    public void Tick_with_a_value_speaks_only_the_value()
    {
        Assert.Equal("flaps 1", ChecklistItemSpeech.TickText("flaps 1"));
    }

    [Fact]
    public void Status_text_matches_the_old_format_without_a_value()
    {
        Assert.Equal("Beacon: ON — Complete", ChecklistItemSpeech.StatusText("Beacon: ON", null, true));
        Assert.Equal("Beacon: ON — Incomplete", ChecklistItemSpeech.StatusText("Beacon: ON", null, false));
    }

    [Fact]
    public void Status_text_carries_the_value()
    {
        Assert.Equal("TCAS: TA/RA, TA only — Incomplete",
            ChecklistItemSpeech.StatusText("TCAS: TA/RA", "TA only", false));
    }

    private sealed class Eval : MSFSBlindAssist.FirstOfficer.Generic.LVarStateEvaluator { }

    [Fact]
    public void ReadLiveValue_is_null_when_unset_throwing_or_empty()
    {
        var item = new ChecklistItem<MSFSBlindAssist.FirstOfficer.Fenix.FenixActionExecutor, Eval>();
        Assert.Null(item.ReadLiveValue(new Eval()));
        item.LiveValue = _ => throw new System.InvalidOperationException();
        Assert.Null(item.ReadLiveValue(new Eval()));
        item.LiveValue = _ => "  ";
        Assert.Null(item.ReadLiveValue(new Eval()));
        item.LiveValue = _ => "flaps 2";
        Assert.Equal("flaps 2", item.ReadLiveValue(new Eval()));
    }
}
