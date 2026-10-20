// Every settable Cessna 172 switch is spoken when it moves outside MSFSBA, as on the other aircraft
// (C172-20): Continuous and announced, so MainForm's generic announcer says it once, the panel's own
// pick being said by the screen reader alone (CORE-8). The Starter's turn of the magneto key and the
// positions the panel does not list stay quiet.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class CessnaC172SwitchAnnouncementTests
{
    private static readonly string[] Switches =
    {
        "C172_BATTERY_STATE", "C172_ALTERNATOR_STATE", "C172_AVIONICS1_STATE", "C172_AVIONICS2_STATE",
        "C172_MAGNETO", "C172_FUEL_PUMP_STATE", "C172_FUEL_SELECTOR", "C172_FUEL_VALVE_STATE",
        "C172_NAV_LIGHT_STATE", "C172_BEACON_STATE", "C172_STROBE_STATE", "C172_LANDING_LIGHT_STATE",
        "C172_TAXI_LIGHT_STATE", "C172_PITOT_HEAT_STATE", "C172_PARKING_BRAKE_STATE", "C172_FLAPS",
        "C172G_STBY_BATTERY_STATE", "C172G_ALT_STATIC_STATE", "C172G_ELT",
    };

    [Fact]
    public void Every_switch_is_continuous_and_announced()
    {
        var vars = new CessnaC172G1000Definition().GetVariables();
        foreach (string key in Switches)
        {
            Assert.True(vars[key].UpdateFrequency == SimConnect.UpdateFrequency.Continuous && vars[key].IsAnnounced, key);
            Assert.False(vars[key].ExcludeFromMonitorManager, key);
        }
    }

    [Theory]
    [InlineData(false, 3, false)]   // a magneto change: spoken
    [InlineData(false, 0, false)]
    [InlineData(false, 4, true)]    // the Starter turns the key to START
    [InlineData(true, 3, true)]     // and it springs back to BOTH
    [InlineData(true, 4, true)]     // the Starter's backup START after a bounce
    public void The_starters_turn_of_the_key_is_not_a_magneto_change(bool wasAtStart, double value, bool starter) =>
        Assert.Equal(starter, CessnaC172BaseDefinition.IsStarterTurnOfTheKey(wasAtStart, value));

    [Fact]
    public void A_start_and_spring_back_say_nothing_and_the_combo_follows_only_the_listed_positions()
    {
        var def = new CessnaC172G1000Definition();
        var said = new GatedSpeechCapture();

        Assert.True(def.ProcessSimVarUpdate("C172_MAGNETO", 4, said));
        Assert.True(def.ProcessSimVarUpdate("C172_MAGNETO", 3, said));
        Assert.False(def.ProcessSimVarUpdate("C172_MAGNETO", 0, said));   // a real change: the generic announcer speaks it
        Assert.Empty(said.All);

        var magneto = def.GetVariables()["C172_MAGNETO"];
        Assert.True(magneto.RefreshesControlWhenDefHandled(3));
        Assert.False(magneto.RefreshesControlWhenDefHandled(4));
    }

    [Theory]
    [InlineData("C172G_STBY_BATTERY_STATE", 2, true)]   // TEST, springs back to OFF
    [InlineData("C172G_STBY_BATTERY_STATE", 1, false)]
    [InlineData("C172G_ELT", 2, true)]                  // the unnamed third position
    [InlineData("C172G_ELT", 0, false)]
    [InlineData("C172_BEACON_STATE", 2, false)]
    public void Unlisted_positions_are_never_spoken(string key, double value, bool unlisted) =>
        Assert.Equal(unlisted, CessnaC172G1000Definition.IsUnlistedSwitchPosition(key, value));

    [Fact]
    public void The_parking_brake_is_set_or_released()
    {
        var brake = new CessnaC172G1000Definition().GetVariables()["C172_PARKING_BRAKE_STATE"];
        Assert.Equal("Released", brake.ValueDescriptions[0]);
        Assert.Equal("Set", brake.ValueDescriptions[1]);
    }
}
