using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class ChecklistItemSpeechTests
{
    [Fact]
    public void Tick_without_a_value_is_unchanged()
    {
        Assert.Equal("Beacon: ON: checked", ChecklistItemSpeech.TickText("Beacon: ON", null, true));
        Assert.Equal("Beacon: ON: unchecked", ChecklistItemSpeech.TickText("Beacon: ON", "", false));
    }

    [Fact]
    public void Tick_with_a_value_speaks_it_between_label_and_status()
    {
        Assert.Equal("Flaps setting: SET, flaps 1: checked",
            ChecklistItemSpeech.TickText("Flaps setting: SET", "flaps 1", true));
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
