using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// GSX's always-available system block. Under the OLD SimConnect transport
/// these were menu CHOICE INDICES 10-14 written into an L:var; the Remote API
/// has no such indices (menu.pick takes a real 0-based index into the current
/// entries array, where 10-14 are ordinary rows), so the letters have to map to
/// command.run verbs instead. They were dropped entirely in the transport swap.
/// </summary>
public class GsxSystemCommandsTests
{
    [Fact]
    public void The_four_runnable_commands_carry_GSXs_own_verbs()
    {
        Assert.Equal("CUSTOMIZE_AIRPORT_POSITION", GsxSystemCommands.ByShortcut("A")!.Command);
        Assert.Equal("CUSTOMIZE_AIRPLANE", GsxSystemCommands.ByShortcut("B")!.Command);
        Assert.Equal("RESTART_COUATL", GsxSystemCommands.ByShortcut("D")!.Command);
        Assert.Equal("RELOAD_SIMBRIEF", GsxSystemCommands.ByShortcut("E")!.Command);
    }

    [Fact]
    public void Settings_stays_local_and_asks_GSX_for_nothing()
    {
        // C opens MSFSBA's own settings window (AccessGSXForm.ProcessCmdKey);
        // a null Command is what keeps it off the command.run path.
        var settings = GsxSystemCommands.ByShortcut("C");
        Assert.NotNull(settings);
        Assert.Null(settings!.Command);
    }

    [Fact]
    public void The_block_reads_out_in_keyboard_order()
        => Assert.Equal(new[] { "A", "B", "C", "D", "E" },
                        GsxSystemCommands.All.Select(c => c.Shortcut));

    [Fact]
    public void Every_entry_has_a_label_a_pilot_can_hear()
        => Assert.All(GsxSystemCommands.All, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));

    [Fact]
    public void An_unbound_key_resolves_to_nothing()
    {
        Assert.Null(GsxSystemCommands.ByShortcut("F"));
        Assert.Null(GsxSystemCommands.ByShortcut("1"));
        Assert.Null(GsxSystemCommands.ByShortcut(""));
    }

    [Fact]
    public void Shortcut_lookup_is_case_insensitive()
        => Assert.Same(GsxSystemCommands.ByShortcut("A"), GsxSystemCommands.ByShortcut("a"));
}
