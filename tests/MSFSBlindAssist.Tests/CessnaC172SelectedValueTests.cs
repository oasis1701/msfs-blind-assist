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
            Assert.Equal("Selected airspeed 85 knots", CessnaC172G1000Definition.DescribeSelectedAirspeed(84.6));
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
