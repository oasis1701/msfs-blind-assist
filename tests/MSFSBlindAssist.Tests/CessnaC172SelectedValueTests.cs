// Output-mode Shift+A, Shift+H, Shift+S and Shift+V read the GFC 700's SELECTED values, each followed by
// its mode when that mode is active (C172-21). The port left the first three unanswered: its note said
// MainForm answered them, and MainForm answers only the aircraft's current values.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests;

public class CessnaC172SelectedValueTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_selected_values_read_the_same_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.Equal("Selected altitude 3500 feet", CessnaC172G1000Definition.DescribeSelectedAltitude(3500));
            // The FLC target is named for the mode, never "Selected airspeed": the C172 has no
            // autothrottle, FLC flies it with pitch. The reading names FLC, so its mode says "active" alone.
            Assert.Equal("FLC speed 85 knots", CessnaC172G1000Definition.DescribeFlcSpeed(84.6));
            Assert.Equal("FLC speed 85 knots, active",
                CessnaC172G1000Definition.WithActiveMode(CessnaC172G1000Definition.DescribeFlcSpeed(85), true,
                    CessnaC172G1000Definition.SelectedValueKeys[HotkeyAction.ReadSpeed].Mode));
            Assert.Equal("Heading bug 270, HDG active",
                CessnaC172G1000Definition.WithActiveMode("Heading bug 270", true, "HDG"));
            Assert.Equal("Vertical speed -500 feet per minute, VS active",
                CessnaC172G1000Definition.WithActiveMode(CessnaC172G1000Definition.DescribeVerticalSpeedTarget(-500), true, "VS"));
            Assert.Equal("Selected altitude 3500 feet",
                CessnaC172G1000Definition.WithActiveMode("Selected altitude 3500 feet", false, "ALT hold"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // Input Ctrl+S, the FLC speed (C172-22): the GFC 700 took any number, so the aircraft's own
    // stall (48) and never-exceed (160) speeds bound a typed value; parsed as typed (CORE-17), so a
    // comma or a dot is a decimal and a grouped "1,500" is refused, on any PC.
    [Theory]
    [InlineData("85", true)]
    [InlineData("48", true)]
    [InlineData("160", true)]
    [InlineData("85.5", true)]
    [InlineData("85,5", true)]
    [InlineData("47", false)]
    [InlineData("161", false)]
    [InlineData("1,500", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void A_typed_flc_airspeed_is_taken_only_inside_the_aircrafts_own_speeds(string typed, bool taken)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                string? refusal = CessnaC172G1000Definition.ValidateSelectedAirspeed(typed);
                Assert.Equal(taken, refusal == null);
                if (!taken) Assert.Equal("Enter an FLC speed between 48 and 160 knots", refusal);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }
    }

    [Fact]
    public void All_four_keys_are_answered_with_a_value_and_a_mode_the_cache_keeps_current()
    {
        var keys = CessnaC172G1000Definition.SelectedValueKeys;
        Assert.Equal(new[] { HotkeyAction.ReadAltitude, HotkeyAction.ReadHeading, HotkeyAction.ReadSpeed,
            HotkeyAction.ReadFCUVerticalSpeedFPA }.OrderBy(a => a), keys.Keys.OrderBy(a => a));

        var vars = new CessnaC172G1000Definition().GetVariables();
        foreach (var (valueKey, _, modeKey, _, _) in keys.Values)
        {
            Assert.True(vars.ContainsKey(valueKey), valueKey);
            // The mode is read from the cache, which holds only what the batch keeps current (C172-19).
            Assert.True(vars[modeKey].UpdateFrequency == SimConnect.UpdateFrequency.Continuous && vars[modeKey].IsAnnounced,
                modeKey);
        }
    }
}
