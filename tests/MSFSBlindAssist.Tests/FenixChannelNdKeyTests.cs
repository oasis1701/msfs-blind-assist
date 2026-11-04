// Characterization tests for the ND's switch commit (plan 03, task 14): the windows list entry, the answer to Alt+N and
// Ctrl+W (FenixA320Definition.AnswerChannelNdKey), the messages sampler's speech and the panel's write path for the
// window's EFIS controls (Aircraft/FenixA320Definition.ChannelNd.cs, Forms/Fenix/FenixWindowRegistry.ChannelNd.cs). No sim
// and no socket. What a pilot hears from the keys is checked in the sim ([CORE-5]).

using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdKeyTests
{
    private static FenixA320Definition.ChannelKeyContext Context(SpeechCapture speech, FenixChannelMonitor? monitor)
        => new(null!, speech, null!, null!, monitor);

    // A trusted ND snapshot in ARC mode, with a next waypoint, and listed messages.
    private static FenixChannelSnapshot Snap(string[]? listed = null)
    {
        var remote = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(1, 3)).Message(1, new FenixChannelProtoWriter())
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456").String(2, "g213w / g37"))
            .Message(68, new FenixChannelProtoWriter().String(1, "VAKSU g318c").String(2, "g84 csNM"));
        foreach (string entry in listed ?? []) remote.Message(103, new FenixChannelProtoWriter().String(1, entry));

        FenixChannelPush data = FenixChannelPush.From(remote.ToArray())!;
        return new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), null, data, null, 0, 1);
    }

    [Fact]
    public void The_window_list_offers_the_nd_display_once()
    {
        FenixWindowEntry entry = Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "ND");

        Assert.Equal(
            "The captain's navigation display, live from the Fenix: the map, the route, navaids and traffic, with the EFIS controls",
            entry.Description);
        Assert.NotNull(entry.Open);
    }

    [Fact]
    public void The_nd_keys_read_the_nd_verdict()
    {
        string source = File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.ChannelNd.cs"));

        Assert.Contains("HotkeyAction.ReadDisplayND", source);
        Assert.Contains("HotkeyAction.ReadNDWaypoint", source);
        var members = Regex.Matches(source, @"FenixChannelDisplay\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(members);
        Assert.All(members, member => Assert.Equal("Nd", member));
    }

    [Fact]
    public void Ctrl_w_speaks_the_readout_or_not_available()
    {
        // With no monitor and with one that trusts nothing, the key is answered and says so at once.
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        var definition = new FenixA320Definition();

        foreach (FenixChannelMonitor? monitor in new[] { null, neverStarted })
        {
            var speech = new SpeechCapture();

            Assert.True(definition.AnswerChannelNdKey(HotkeyAction.ReadNDWaypoint, Context(speech, monitor)));

            Assert.Equal(new[] { "Next waypoint not available" }, speech.Interrupts);
            Assert.Equal(speech.Interrupts, speech.All);
        }

        // The readout of a trusted ND is what the key speaks.
        string? expected = FenixChannelNdWaypoint.Readout(Snap(), _ => { });
        Assert.NotNull(expected);
        Assert.StartsWith("VAKSU", expected);
    }

    [Fact]
    public void Alt_n_with_no_trusted_nd_says_nothing_and_opens_nothing()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(definition.AnswerChannelNdKey(HotkeyAction.ReadDisplayND, Context(speech, null)));
        Assert.False(definition.AnswerChannelNdKey(HotkeyAction.ReadDisplayND, Context(speech, neverStarted)));
        Assert.False(definition.AnswerChannelNdKey(HotkeyAction.ReadDisplayPFD, Context(speech, neverStarted)));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_sampler_speaks_without_interrupting()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        definition.SpeakNdMessages(speech, Snap());                       // the baseline: silent
        Assert.Empty(speech.All);

        definition.SpeakNdMessages(speech, Snap(["aMAP PARTLY DISPLAYED"]));

        Assert.Single(speech.All);
        Assert.StartsWith("ND message: MAP PARTLY DISPLAYED", speech.All[0]);
        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void The_nd_controls_all_exist_as_panel_variables()
    {
        var variables = new FenixA320Definition().GetVariables();

        Assert.Contains(FenixChannelNdControls.ModeKey, variables.Keys);
        Assert.Contains(FenixChannelNdControls.RangeKey, variables.Keys);
        Assert.Equal(6, FenixChannelNdControls.Buttons.Count);
        Assert.All(FenixChannelNdControls.Buttons, b => Assert.Contains(b.Key, variables.Keys));
    }

    [Fact]
    public void A_key_the_definition_does_not_know_writes_nothing()
    {
        // The sim is null: a write would throw. An unknown key returns before touching it.
        new FenixA320Definition().WriteNdControl("S_NOT_A_CONTROL", 1, null!, new SpeechCapture());
    }
}
