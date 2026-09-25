// FBW #10855 moved three A380 controls onto new variables (fixed 2026-09-25): the two flight-director
// combos (FD_1_CTL / FD_2_CTL, both Ctrl+M rows) became the ONE FD light, A32NX_FCU_FD_LIGHT_ON, and the
// altitude increment moved from XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT to the FCU's own
// A32NX_FCU_ALT_INCREMENT_1000. A pilot who had muted the old row would suddenly hear the new one.
// The mute MOVES: the old key leaves the list, so it is done once and never again — left in place,
// it would re-mute the new row on every launch after the pilot un-ticked it, with no row left to
// clear the old key from.

using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class A380RenamedMonitorMuteTests
{
    private static UserSettings With(params string[] muted) =>
        new() { A380DisabledMonitorVariables = new List<string>(muted) };

    [Theory]
    [InlineData("FD_1_CTL", "A32NX_FCU_FD_LIGHT_ON")]
    [InlineData("FD_2_CTL", "A32NX_FCU_FD_LIGHT_ON")]
    [InlineData("XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT", "A32NX_FCU_ALT_INCREMENT_1000")]
    public void A_mute_on_a_renamed_row_moves_to_its_new_row(string oldKey, string newKey)
    {
        var settings = With(oldKey);

        Assert.True(SettingsManager.CarryRenamedMonitorMutes(settings));
        Assert.Equal(new[] { newKey }, settings.A380DisabledMonitorVariables);
    }

    [Fact]
    public void Both_flight_director_rows_become_one_mute()
    {
        var settings = With("FD_1_CTL", "FD_2_CTL", "A32NX_ENGINE_N1:1");

        SettingsManager.CarryRenamedMonitorMutes(settings);

        Assert.Equal(new[] { "A32NX_ENGINE_N1:1", "A32NX_FCU_FD_LIGHT_ON" }, settings.A380DisabledMonitorVariables);
    }

    [Fact]
    public void Once_moved_an_unmute_of_the_new_row_sticks()
    {
        var settings = With("FD_1_CTL");
        SettingsManager.CarryRenamedMonitorMutes(settings);
        settings.A380DisabledMonitorVariables.Remove("A32NX_FCU_FD_LIGHT_ON");   // the pilot un-ticks it

        Assert.False(SettingsManager.CarryRenamedMonitorMutes(settings));      // next launch
        Assert.Empty(settings.A380DisabledMonitorVariables);
    }
}
