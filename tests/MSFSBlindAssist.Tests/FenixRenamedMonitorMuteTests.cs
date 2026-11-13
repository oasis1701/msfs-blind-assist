// Fenix v3 (3.0.0.7426) renamed two lamps (fixed 2026-10-21): B_ELEC_BUS_POWER_DC_SERIVICE (a typo)
// became B_ELEC_BUS_POWER_DC_SERVICE, and the overhead cockpit-door VIDEO light I_OH_DOOR_VIDEO became
// I_OH_COCKPIT_DOOR_VIDEO. A pilot who had muted the old Ctrl+M row would suddenly hear the new one.
// The mute MOVES, as the A380's does (A380RenamedMonitorMuteTests): the old key leaves the list, so it
// is done once and never again.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class FenixRenamedMonitorMuteTests
{
    private static UserSettings With(params string[] muted) =>
        new() { FenixDisabledMonitorVariables = new List<string>(muted) };

    [Theory]
    [InlineData("B_ELEC_BUS_POWER_DC_SERIVICE", "B_ELEC_BUS_POWER_DC_SERVICE")]
    [InlineData("I_OH_DOOR_VIDEO", "I_OH_COCKPIT_DOOR_VIDEO")]
    public void A_mute_on_a_renamed_row_moves_to_its_new_row(string oldKey, string newKey)
    {
        var settings = With(oldKey);

        Assert.True(SettingsManager.CarryRenamedMonitorMutes(settings));
        Assert.Equal(new[] { newKey }, settings.FenixDisabledMonitorVariables);
    }

    // The literal keys in SettingsManager cannot reach the definition, so a typo there would carry a
    // mute onto a row that does not exist.
    [Theory]
    [InlineData("B_ELEC_BUS_POWER_DC_SERVICE")]
    [InlineData("I_OH_COCKPIT_DOOR_VIDEO")]
    public void The_new_key_is_a_registered_Fenix_row(string newKey)
    {
        Assert.True(new FenixA320Definition().GetVariables().ContainsKey(newKey));
    }

    [Fact]
    public void Both_renamed_rows_move_and_other_mutes_stay()
    {
        var settings = With("B_ELEC_BUS_POWER_DC_SERIVICE", "I_OH_DOOR_VIDEO", "E_FCU_SPEED");

        SettingsManager.CarryRenamedMonitorMutes(settings);

        Assert.Equal(
            new[] { "E_FCU_SPEED", "B_ELEC_BUS_POWER_DC_SERVICE", "I_OH_COCKPIT_DOOR_VIDEO" },
            settings.FenixDisabledMonitorVariables);
    }

    [Fact]
    public void A_pilot_who_muted_the_new_row_already_keeps_one_entry()
    {
        var settings = With("I_OH_DOOR_VIDEO", "I_OH_COCKPIT_DOOR_VIDEO");

        SettingsManager.CarryRenamedMonitorMutes(settings);

        Assert.Equal(new[] { "I_OH_COCKPIT_DOOR_VIDEO" }, settings.FenixDisabledMonitorVariables);
    }

    [Fact]
    public void Once_moved_an_unmute_of_the_new_row_sticks()
    {
        var settings = With("B_ELEC_BUS_POWER_DC_SERIVICE");
        SettingsManager.CarryRenamedMonitorMutes(settings);
        settings.FenixDisabledMonitorVariables.Remove("B_ELEC_BUS_POWER_DC_SERVICE");   // the pilot un-ticks it

        Assert.False(SettingsManager.CarryRenamedMonitorMutes(settings));              // next launch
        Assert.Empty(settings.FenixDisabledMonitorVariables);
    }

    // Another aircraft's list is never read for the Fenix's keys.
    [Fact]
    public void The_Fenix_keys_do_not_touch_another_aircrafts_mutes()
    {
        var settings = new UserSettings
        {
            A32NXDisabledMonitorVariables = new List<string> { "I_OH_DOOR_VIDEO" },
        };

        Assert.False(SettingsManager.CarryRenamedMonitorMutes(settings));
        Assert.Equal(new[] { "I_OH_DOOR_VIDEO" }, settings.A32NXDisabledMonitorVariables);
    }
}
