// Characterization tests for the Fenix ISIS's window and its variant gate: FenixChannelIsisForm (Forms/Fenix) and
// FenixChannelIsis.KeyMayOpen and RefusalFor (Services/FenixChannelIsis.cs). No sim and no socket: a monitor that is never
// started trusts nothing. The window is built, never shown (StartLive runs the first open's work without putting anything
// on screen), as the other display windows are tested.
//
// The contract: a Fenix with the round standby gauges (HasIsis false) never opens the window from Alt+I, which keeps its
// AI read, and its list entry says "This Fenix has no ISIS." instead; a standby not yet read (null) is left to the ISIS
// verdict. The window is a class of its own, titled "Fenix ISIS", its list named "ISIS display", and while the ISIS is
// not trusted it shows the one status row "ISIS: not available, <reason>".

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelIsisWindowTests
{
    private static DisplayListBox ListOf(Control form) => form.Controls.OfType<DisplayListBox>().Single();

    [Fact]
    public void Only_a_fenix_that_may_have_an_isis_opens_the_window()
    {
        Assert.True(FenixChannelIsis.KeyMayOpen(FenixVariant.Unknown with { HasIsis = true }));
        Assert.True(FenixChannelIsis.KeyMayOpen(FenixVariant.Unknown));
        Assert.False(FenixChannelIsis.KeyMayOpen(FenixVariant.Unknown with { HasIsis = false }));
    }

    [Fact]
    public void A_round_gauge_fenix_refuses_the_list_entry()
    {
        Assert.Equal("This Fenix has no ISIS.", FenixChannelIsis.RefusalFor(FenixVariant.Unknown with { HasIsis = false }));
        Assert.Null(FenixChannelIsis.RefusalFor(FenixVariant.Unknown with { HasIsis = true }));
        Assert.Null(FenixChannelIsis.RefusalFor(FenixVariant.Unknown));
    }

    [Fact]
    public void The_isis_window_is_named_for_its_display()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new FenixChannelIsisForm(neverStarted, () => FenixVariant.Unknown, _ => { });

        Assert.Equal("Fenix ISIS", form.Text);
        Assert.Equal("ISIS display", ListOf(form).AccessibleName);

        form.StartLive();

        string[] rows = ListOf(form).Items.Cast<string>().ToArray();
        Assert.Single(rows);
        Assert.Equal($"ISIS: not available, {neverStarted.VerdictFor(FenixChannelDisplay.Isis).Reason}", rows[0]);
    }

    // ---- The switch: Alt+I and the list entry ---------------------------------------------------

    // A context with only the pieces the ISIS's answer touches. The hotkey manager is null, so a key that went on to open
    // a window would throw: a key that must open nothing never reaches it.
    private static FenixA320Definition.ChannelKeyContext Context(SpeechCapture speech, FenixChannelMonitor? monitor)
        => new(null!, speech, null!, null!, monitor);

    private static FenixA320Definition RoundGaugeFenix()
    {
        var definition = new FenixA320Definition();
        definition.TryApplyConfigValue(FenixConfigVars.AircraftType, 3);
        definition.TryApplyConfigValue(FenixConfigVars.Standby, 1);
        Assert.False(definition.Variant.HasIsis);
        return definition;
    }

    // A monitor whose channel is healthy and whose ISIS is off: connected, the build checked, one good push that carries
    // no ISIS part. The key would open the window on its status row here, so a key that stays false was stopped earlier.
    private static FenixChannelMonitor IsisOffMonitor(MonitorTestKit.Lines lines)
    {
        FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.True(
            SpinWait.SpinUntil(
                () => lines.All.Contains($"build {MonitorTestKit.CheckedBuild}: checked"), MonitorTestKit.Generous),
            "the build was never checked. The log: " + lines);
        monitor.OnDataMessage(MonitorTestKit.GoodPush());
        Assert.True(monitor.VerdictFor(FenixChannelDisplay.Isis).IsDisplayOff);
        return monitor;
    }

    [Fact]
    public void A_round_gauge_fenix_keeps_the_ai_read_for_alt_i()
    {
        using FenixChannelMonitor monitor = IsisOffMonitor(new MonitorTestKit.Lines());
        var speech = new SpeechCapture();

        Assert.False(RoundGaugeFenix().AnswerChannelIsisKey(Hotkeys.HotkeyAction.ReadDisplayISIS, Context(speech, monitor)));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Alt_i_says_nothing_with_no_monitor_and_answers_no_other_key()
    {
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(definition.AnswerChannelIsisKey(Hotkeys.HotkeyAction.ReadDisplayISIS, Context(speech, null)));
        Assert.False(definition.AnswerChannelIsisKey(Hotkeys.HotkeyAction.ReadDisplayPFD, Context(speech, null)));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_isis_entry_is_listed_once()
    {
        FenixWindowEntry entry = Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "ISIS");

        Assert.Equal(
            "Speed, altitude, baro, attitude, bugs and ILS deviations, live from the Fenix's display channel",
            entry.Description);
    }

    [Fact]
    public void The_isis_entry_refuses_on_a_round_gauge_fenix()
    {
        var speech = new SpeechCapture();
        var context = new FenixWindowContext(RoundGaugeFenix(), null!, null!, speech, null!);

        Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "ISIS").Open(context);

        Assert.Equal(new[] { "This Fenix has no ISIS." }, speech.All.ToArray());
    }

    [Fact]
    public void The_isis_entry_with_no_monitor_says_the_display_is_not_available()
    {
        var speech = new SpeechCapture();
        var context = new FenixWindowContext(new FenixA320Definition(), null!, null!, speech, null!);

        Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "ISIS").Open(context);

        Assert.Equal(new[] { "ISIS display not available" }, speech.All.ToArray());
    }

    [Fact]
    public void The_isis_key_asks_the_variant_then_the_verdict()
    {
        // A source test: the variant gate comes before OpenChannelWindow, and the key's answer is that call's alone.
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.ChannelIsis.cs"));

        int gate = source.IndexOf("FenixChannelIsis.KeyMayOpen(Variant)", StringComparison.Ordinal);
        int open = source.IndexOf("OpenChannelWindow(ctx, Services.FenixChannelDisplay.Isis", StringComparison.Ordinal);
        Assert.True(gate >= 0 && open > gate, "KeyMayOpen(Variant) must come before OpenChannelWindow for the ISIS");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
