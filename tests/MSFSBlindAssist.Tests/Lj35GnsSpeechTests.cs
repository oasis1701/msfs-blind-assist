using MSFSBlindAssist.Aircraft.Learjet35;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The GNS window's post-key announcement. The agent reports "ok|kind|context|cursor|entry|tuning"
/// (pipes never occur in the instrument's text) and the window says the one thing the key changed.
/// </summary>
public class Lj35GnsSpeechTests
{
    private const string Nav = "ok|page|NAV page 1 of 5, Navigation|||COM standby 124.850";
    private const string Menu = "ok|dialog|Page Menu|Display NEXRAD?||COM standby 124.850";
    private const string Entry = "ok|dialog|Direct To, select waypoint|EG___, cursor on E at 1|E|COM standby 124.850";
    private const string EntryBlank = "ok|dialog|Direct To, select waypoint|EG___, cursor on blank at 3|blank|COM standby 124.850";

    [Fact]
    public void ParsesTheSixFields()
    {
        var s = Lj35GnsSpeech.Parse(Entry);
        Assert.NotNull(s);
        Assert.Equal("dialog", s!.Kind);
        Assert.Equal("Direct To, select waypoint", s.Context);
        Assert.Equal("EG___, cursor on E at 1", s.Cursor);
        Assert.Equal("E", s.EntryChar);
        Assert.Equal("COM standby 124.850", s.Tuning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("error TypeError")]
    [InlineData("ok|page|only three")]
    public void AnythingElseIsNoState(string? raw) => Assert.Null(Lj35GnsSpeech.Parse(raw));

    [Fact]
    public void NoStateFallsBackToTheKeyName()
    {
        Assert.Equal("enter", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Button, null, "enter"));
        Assert.Equal("next page", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Knob, null, "next page"));
    }

    [Fact]
    public void KnobInAnIdentEntrySaysTheCharacterUnderTheCursor()
    {
        Assert.Equal("E", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Knob, Lj35GnsSpeech.Parse(Entry), "next page"));
        Assert.Equal("blank", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Knob, Lj35GnsSpeech.Parse(EntryBlank), "next page"));
    }

    [Fact]
    public void KnobInAListSaysTheHighlightedRow() =>
        Assert.Equal("Display NEXRAD?", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Knob, Lj35GnsSpeech.Parse(Menu), "next page"));

    [Fact]
    public void KnobWithNoCursorSaysThePage() =>
        Assert.Equal("NAV page 1 of 5, Navigation", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Knob, Lj35GnsSpeech.Parse(Nav), "next page"));

    [Fact]
    public void ButtonSaysTheLayerAndItsCursor()
    {
        Assert.Equal("NAV page 1 of 5, Navigation", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Button, Lj35GnsSpeech.Parse(Nav), "enter"));
        Assert.Equal("Page Menu. Cursor on Display NEXRAD?", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Button, Lj35GnsSpeech.Parse(Menu), "menu"));
    }

    [Theory]
    [InlineData("egkk", true, "EGKK")]
    [InlineData(" TNT ", true, "TNT")]
    [InlineData("DER09", true, "DER09")]
    [InlineData("", false, "")]
    [InlineData("EG KK", false, "")]
    [InlineData("A-9", false, "")]
    [InlineData("ABCDEFG", false, "")]
    public void TypedIdentIsLettersAndDigitsUpToSix(string typed, bool ok, string ident)
    {
        var r = Lj35GnsIdent.Validate(typed);
        Assert.Equal(ok, r.ok);
        Assert.Equal(ident, r.ident);
        if (!ok) Assert.NotEqual(string.Empty, r.message);
    }

    [Fact]
    public void RadioKnobSaysTheKeyAndLeavesTheFrequencyToTheMonitor()
    {
        Assert.Equal("megahertz up", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Radio, Lj35GnsSpeech.Parse(Nav), "megahertz up"));
        Assert.Equal("megahertz up", Lj35GnsSpeech.Compose(Lj35GnsKeyKind.Radio, Lj35GnsSpeech.Parse("ok|page|NAV page 1 of 5|||"), "megahertz up"));
    }
}
