// The A380 ND option filters WPT / VOR-DME / NDB are ONE mutually-exclusive selection, not
// three independent switches.
//
// A380FcuComputer.cpp:2271-2281 holds a single `pEfisFilter` enum (NONE/WPT/VORD/NDB):
// pressing the button that is already active clears it to NONE, pressing any other button
// REPLACES the selection. The three lights are each `efis_filter == <enum>`
// (A380FcuComputer.cpp:2567-2572), so two of them can never be lit at once — the earlier
// "live-verified all three lit simultaneously" claim is not reachable in this build.
//
// Modelling them as three On/Off combos is what produced the report "turn Waypoints on, then
// turn NDB on, and Waypoints turns off": the aircraft was behaving correctly and the app was
// offering a shape the aircraft does not have.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class NdFilterSelectionTests
{
    [Fact]
    public void No_light_lit_reads_as_off()
    {
        Assert.Equal(NdFilterSelection.Off, NdFilterSelection.FromLights(false, false, false));
    }

    [Theory]
    [InlineData(true, false, false, NdFilterSelection.Waypoints)]
    [InlineData(false, true, false, NdFilterSelection.VorDme)]
    [InlineData(false, false, true, NdFilterSelection.Ndb)]
    public void The_lit_light_names_the_selection(bool wpt, bool vord, bool ndb, int expected)
    {
        Assert.Equal(expected, NdFilterSelection.FromLights(wpt, vord, ndb));
    }

    // Structurally impossible on this build, but a partially-delivered batch can show two
    // lights for one frame. Resolve deterministically rather than flapping the readout.
    [Fact]
    public void Two_lights_at_once_resolve_deterministically_rather_than_flapping()
    {
        Assert.Equal(NdFilterSelection.Waypoints, NdFilterSelection.FromLights(true, true, true));
    }

    // THE REPORTED BUG. Selecting a different filter is ONE press, and it replaces rather
    // than adds — which is why three independent switches could never work.
    [Fact]
    public void Choosing_a_different_filter_presses_only_the_new_one()
    {
        Assert.Equal("A32NX.FCU_EFIS_L_NDB_PUSH",
            NdFilterSelection.PushEvent("L", NdFilterSelection.Waypoints, NdFilterSelection.Ndb));
    }

    // The subtle half: there is no "off" button. Clearing the selection means pressing the
    // button that is CURRENTLY ACTIVE, because that is what toggles the FCU back to NONE.
    [Fact]
    public void Clearing_the_selection_presses_the_currently_active_button()
    {
        Assert.Equal("A32NX.FCU_EFIS_L_VORD_PUSH",
            NdFilterSelection.PushEvent("L", NdFilterSelection.VorDme, NdFilterSelection.Off));
    }

    [Fact]
    public void Selecting_from_off_presses_the_wanted_button()
    {
        Assert.Equal("A32NX.FCU_EFIS_R_WPT_PUSH",
            NdFilterSelection.PushEvent("R", NdFilterSelection.Off, NdFilterSelection.Waypoints));
    }

    [Theory]
    [InlineData(NdFilterSelection.Off)]
    [InlineData(NdFilterSelection.Ndb)]
    public void Re_selecting_what_is_already_shown_presses_nothing(int position)
    {
        Assert.Null(NdFilterSelection.PushEvent("L", position, position));
    }

    [Theory]
    [InlineData(NdFilterSelection.Off, "Off")]
    [InlineData(NdFilterSelection.Waypoints, "Waypoints")]
    [InlineData(NdFilterSelection.VorDme, "VOR/DME")]
    [InlineData(NdFilterSelection.Ndb, "NDB")]
    public void Each_position_has_speakable_text(int position, string expected)
    {
        Assert.Equal(expected, NdFilterSelection.Text(position));
    }

    // The shape is the thing that regressed, so pin the shape and not just the maths: each
    // EFIS panel offers ONE filter selector, and must not offer the three On/Off switches that
    // the aircraft cannot honour.
    [Theory]
    [InlineData("EFIS Captain", "ND_FILTER_L", "L")]
    [InlineData("EFIS First Officer", "ND_FILTER_R", "R")]
    public void Each_efis_panel_offers_one_filter_selector_not_three_switches(
        string panel, string selectorKey, string side)
    {
        var controls = new FlyByWireA380Definition().GetPanelControls()[panel];

        Assert.Contains(selectorKey, controls);
        foreach (var button in new[] { "WPT", "VORD", "NDB" })
            Assert.DoesNotContain($"A32NX_FCU_EFIS_{side}_{button}_LIGHT_ON", controls);
    }
}
