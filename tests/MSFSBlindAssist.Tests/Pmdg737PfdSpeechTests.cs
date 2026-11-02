// What the PFD reader says by itself: PMDG's messages appearing ("PFD message: ALT DISAGREE") and going ("PFD message
// gone: …"), the items of one way on one line in layout order with the prefix once, a flag that replaces an instrument
// included (SPD, ALT …), WINDSHEAR and PULL UP never (the aircraft calls them out); nothing unless both readings have
// the displays lit and the Captain's PFD on a screen; and its status lines.

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.Pmdg737PfdTestMemory;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdSpeechTests
{
    private static readonly Pmdg737PfdLayout L = M.Layout;

    /// <summary>The test memory's reading (lit, the PFD on the Captain's unit) with exactly these messages up.</summary>
    internal static PfdSample With(params string[] up)
    {
        var all = L.AllMessages();
        foreach (var text in up) Assert.Contains(all, m => m.Text == text);
        return new M().Sample() with { MessagesUp = all.Select(m => up.Contains(m.Text)).ToList() };
    }

    private static IReadOnlyList<string> Changes(PfdSample before, PfdSample after) => Pmdg737PfdSpeech.Changes(before, after, L);

    [Fact]
    public void Message_appearing() =>
        Assert.Equal(new[] { "PFD message: ALT DISAGREE" }, Changes(With(), With("ALT DISAGREE")));

    [Fact]
    public void Message_going() =>
        Assert.Equal(new[] { "PFD message gone: ALT DISAGREE" }, Changes(With("ALT DISAGREE"), With()));

    [Fact]
    public void Two_messages_one_line_prefix_once() =>
        Assert.Equal(new[] { "PFD message: NO VSPD, ALT DISAGREE" }, Changes(With(), With("ALT DISAGREE", "NO VSPD")));

    [Fact]
    public void Appearing_and_going_are_two_lines() =>
        Assert.Equal(new[] { "PFD message: ALT DISAGREE", "PFD message gone: NO VSPD" }, Changes(With("NO VSPD"), With("ALT DISAGREE")));

    [Fact]
    public void Flag_replacing_an_instrument_is_spoken()
    {
        Assert.Equal(new[] { "PFD message: SPD" }, Changes(With(), With("SPD")));
        Assert.Equal(new[] { "PFD message gone: ALT" }, Changes(With("ALT"), With()));
    }

    [Fact]
    public void Windshear_is_never_spoken()
    {
        Assert.Empty(Changes(With(), With("WINDSHEAR")));
        Assert.Empty(Changes(With(), With("PULL UP")));
        Assert.Equal(new[] { "PFD message: VERT" }, Changes(With("PULL UP"), With("VERT")));
    }

    [Fact]
    public void Nothing_unless_lit_with_the_pfd_shown()
    {
        var before = With();
        var after = With("ALT DISAGREE");
        var dark = after with { UnitPages = after.UnitPages.Select(_ => L.Units.DarkPages[0]).ToList() };
        Assert.Empty(Changes(before, dark));
        Assert.Empty(Changes(before with { Panel = before.Panel with { Powered = false } }, after));
        var elsewhere = after with { UnitPages = after.UnitPages.Select(_ => 3).ToList() };   // the ND page on every unit
        Assert.Empty(Changes(before, elsewhere));
    }

    [Fact]
    public void Status_lines_exact()
    {
        Assert.Equal("Primary flight display starting", Pmdg737PfdSpeech.Starting);
        Assert.Equal("PFD announcements resumed", Pmdg737PfdSpeech.Resumed);
        Assert.Equal("PFD announcements unavailable: aircraft memory not found", Pmdg737PfdSpeech.Unavailable(FmaReasons.MemoryNotFound));
        Assert.Equal("PFD announcements stopped: values do not match the SDK", Pmdg737PfdSpeech.Stopped(FmaReasons.Mismatch));
        Assert.Equal("Primary flight display unavailable: PMDG file not recognised", Pmdg737PfdSpeech.ListUnavailable(FmaReasons.FileNotRecognised));
        Assert.Equal("Classic EFIS is selected, falling back to AI reading", Pmdg737PfdSpeech.KeyClassic);
        Assert.Equal("Primary flight display stopped: aircraft memory not found, trying again", Pmdg737PfdSpeech.ListStopped(FmaReasons.MemoryNotFound));
        Assert.Equal("waiting for simulator data", Pmdg737PfdSpeech.WaitingForSimulator);
    }
}
