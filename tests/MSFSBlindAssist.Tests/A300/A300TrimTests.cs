using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The pedestal's trim and flap readings (measured 2026-10-10): the rudder trim display (a direction code,
/// 0 left, 1 centered, 2 right, then tens, units and tenths), the pitch trim (the stabilizer angle, positive
/// nose up, measured 2026-10-06) and the slat and flap angles the flap and slat indicator shows.
/// </summary>
public class A300TrimTests
{
    [Theory]
    [InlineData(0, 0, 3, 3, "left 3.3")]
    [InlineData(2, 0, 7, 2, "right 7.2")]
    [InlineData(2, 1, 2, 5, "right 12.5")]
    [InlineData(1, 0, 0, 0, "0.0")]
    public void The_rudder_trim_reads_its_display(double code, double tens, double units, double tenths, string text) =>
        Assert.Equal(text, A300Trim.Rudder(code, tens, units, tenths));

    [Theory]
    [InlineData(1.0983, "1.1 degrees nose up")]
    [InlineData(-0.5, "0.5 degrees nose down")]
    [InlineData(0.01, "0.0 degrees")]
    public void The_pitch_trim_reads_the_stabilizer_angle(double degrees, string text) =>
        Assert.Equal(text, A300Trim.Pitch(degrees));

    [Fact]
    public void Trim_text_reads_the_same_in_any_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("1.1 degrees nose up", A300Trim.Pitch(1.0983));
            Assert.Equal("left 3.3", A300Trim.Rudder(0, 0, 3, 3));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_trim_box_reads_the_pitch_and_rudder_trim_and_the_reset_light()
    {
        var box = new IniA300Definition().GetPanelDisplayVariables()["Trim"];
        Assert.Equal(new[] { "A300_LT_TRIM_KORRY_SEQ2_LIGHT", "A300_RO_PITCH_TRIM", A300Trim.RudderKey }, box);
        Assert.Equal("Rudder trim reset light", A300PanelLamps.All.Single(l => l.Node == "TRIM_KORRY_SEQ2_LIGHT").Name);
        Assert.Equal("A300_LT_TRIM_KORRY_SEQ2_LIGHT", A300PanelLamps.ByButton["A300_TRIM_KORRY"]);
    }

    [Fact]
    public void The_rudder_trim_line_is_composed_from_its_display()
    {
        var cache = new Dictionary<string, double>();
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        foreach (var (key, v) in A300Trim.RudderDigitKeys.Zip(new double[] { 0, 0, 3 }))
            cache[key] = v;
        Assert.True(def.TryGetDisplayOverride(A300Trim.RudderKey, 3, out var text));
        Assert.Equal("left 3.3", text);
    }

    [Fact]
    public void The_flaps_box_reads_the_slat_and_flap_angles()
    {
        var flaps = A300Readouts.All.Where(r => r.Panel == "Flaps and Speed Brake").ToArray();
        Assert.Equal(new[] { ("Slats", "LEADING EDGE FLAPS LEFT ANGLE"), ("Flaps", "TRAILING EDGE FLAPS LEFT ANGLE") },
            flaps.Select(r => (r.Name, r.Var)).ToArray());
        Assert.Equal("15 degrees", flaps[0].Format(15.0004));
    }

    [Theory]
    [InlineData("INDICATOR_REVLK1_LIGHT", "Engine 1 reverser deployed light", "INI_ENG1_REVERSE_LIGHT")]
    [InlineData("INDICATOR_REVLK2_LIGHT", "Engine 2 reverser deployed light", "INI_ENG2_REVERSE_LIGHT")]
    [InlineData("INDICATOR_REV1_LIGHT", "Engine 1 reverser unlocked light", "INI_ENG1_REVERSE_UNLK_LIGHT")]
    [InlineData("INDICATOR_REV2_LIGHT", "Engine 2 reverser unlocked light", "INI_ENG2_REVERSE_UNLK_LIGHT")]
    public void The_reverser_lights_are_on_the_thrust_levers_panel(string node, string name, string var)
    {
        // Design decision 2026-10-09: the reverser lights are spoken.
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((name, "Thrust Levers", var), (lamp.Lamp.Name, lamp.Lamp.Panel, lamp.Primary.Name));
    }
}
