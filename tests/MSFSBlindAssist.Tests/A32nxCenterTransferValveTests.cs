// The SD FUEL page's center-to-inner transfer valves, read the way the A32NX and Headwind A339X SDs
// draw them (FBW Fuel.tsx CentreToInnerTransfer; the A339X's sd.js carries the same component):
// one indication per side from a pair of valves in series, the auto valve the fuel triggers move
// (FUELSYSTEM VALVE OPEN:11 / :12) and the valve the CTR TK XFR button commands (:9 / :10).
// Junction 4 / 5 above 1.5 is MANUAL routing, which bypasses the auto valve.
//
// FBW #10971 (acbfe58df, 2026-10-23) added triggers that close the auto valves when an inner tank
// falls below 1,651 gallons, so with an empty center tank the SD now shows them closed. The readout
// used to have one "Center pump" row reading only the left jet pump, which neither SD draws.

using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class A32nxCenterTransferValveTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    // CI runs under en-US; a comma-decimal culture is the one that would leak into spoken text.
    public A32nxCenterTransferValveTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private const double Auto = 1, Manual = 2;   // junction settings: option 1 / option 2
    private const double On = 1, Off = 0;

    private static string Describe(double? auto, double? transfer, double? junction, double? button, double? manual) =>
        FlyByWireCenterTransferValve.Describe(auto, transfer, junction, button, manual);

    [Fact]
    public void Auto_mode_with_both_valves_open_reads_open()
    {
        Assert.Equal("open", Describe(auto: 1, transfer: 1, Auto, On, manual: 0));
    }

    [Fact]
    public void Auto_mode_with_the_auto_valve_shut_reads_closed_though_the_button_valve_is_open()
    {
        // The #10971 case: an empty center tank, the inner tank below 1,651 gallons.
        Assert.Equal("closed", Describe(auto: 0, transfer: 1, Auto, On, manual: 0));
    }

    [Fact]
    public void Manual_mode_bypasses_the_auto_valve()
    {
        Assert.Equal("open", Describe(auto: 0, transfer: 1, Manual, On, manual: 1));
    }

    [Fact]
    public void A_valve_under_1_percent_open_reads_closed()
    {
        Assert.Equal("closed", Describe(auto: 1, transfer: 0.009, Auto, On, manual: 0));
    }

    [Fact]
    public void A_valve_at_1_percent_already_reads_open()
    {
        Assert.Equal("open, Amber", Describe(auto: 1, transfer: 0.01, Auto, On, manual: 0));
    }

    [Fact]
    public void A_partly_open_valve_in_auto_reads_amber()
    {
        Assert.Equal("open, Amber", Describe(auto: 1, transfer: 0.5, Auto, On, manual: 0));
    }

    [Fact]
    public void A_valve_at_99_percent_in_auto_is_not_yet_fully_open()
    {
        Assert.Equal("open, Amber", Describe(auto: 1, transfer: 0.99, Auto, On, manual: 0));
    }

    [Fact]
    public void A_partly_open_valve_in_manual_reads_green()
    {
        Assert.Equal("open", Describe(auto: 0, transfer: 0.5, Manual, On, manual: 1));
    }

    [Fact]
    public void An_open_valve_with_its_button_off_reads_amber_in_either_mode()
    {
        Assert.Equal("open, Amber", Describe(auto: 1, transfer: 1, Auto, Off, manual: 0));
        Assert.Equal("open, Amber", Describe(auto: 0, transfer: 1, Manual, Off, manual: 1));
    }

    [Fact]
    public void A_closed_valve_is_never_amber()
    {
        // The SD's closed-amber term needs the valve fully open, which a closed valve cannot be.
        Assert.Equal("closed", Describe(auto: 1, transfer: 0, Auto, On, manual: 0));
        Assert.Equal("closed", Describe(auto: 1, transfer: 0, Auto, Off, manual: 0));
        Assert.Equal("closed", Describe(auto: 0, transfer: 0, Manual, Off, manual: 1));
    }

    [Fact]
    public void The_color_follows_the_mode_selector_not_the_junction()
    {
        // The SD takes the routing from the junction but the color from the MODE SEL L:var.
        Assert.Equal("open", Describe(auto: 1, transfer: 0.5, Auto, On, manual: 1));
    }

    [Fact]
    public void An_unread_valve_or_junction_reads_unknown()
    {
        Assert.Equal("--", Describe(auto: 1, transfer: null, Auto, On, manual: 0));
        Assert.Equal("--", Describe(auto: 1, transfer: 1, null, On, manual: 0));
    }

    [Fact]
    public void An_unread_auto_valve_is_unknown_in_auto_and_irrelevant_in_manual()
    {
        Assert.Equal("--", Describe(auto: null, transfer: 1, Auto, On, manual: 0));
        Assert.Equal("open", Describe(auto: null, transfer: 1, Manual, On, manual: 1));
    }

    [Fact]
    public void An_unread_button_or_mode_gives_no_color_rather_than_a_guess()
    {
        Assert.Equal("open", Describe(auto: 1, transfer: 0.5, Auto, null, manual: 0));
        Assert.Equal("open", Describe(auto: 1, transfer: 0.5, Auto, On, null));
        // A button read as off settles amber without the mode.
        Assert.Equal("open, Amber", Describe(auto: 1, transfer: 0.5, Auto, Off, null));
    }

    [Fact]
    public void A_nan_input_reads_like_an_unread_one()
    {
        // MSFSBA reads a variable an airframe lacks back as NaN, which every comparison calls false.
        Assert.Equal("--", Describe(auto: 1, transfer: double.NaN, Auto, On, manual: 0));
        Assert.Equal("--", Describe(auto: 1, transfer: 1, double.NaN, On, manual: 0));
        Assert.Equal("--", Describe(auto: double.NaN, transfer: 1, Auto, On, manual: 0));
        Assert.Equal("open", Describe(auto: 1, transfer: 0.5, Auto, double.NaN, manual: 0));
        Assert.Equal("open", Describe(auto: 1, transfer: 0.5, Auto, On, double.NaN));
    }
}

public class A32nxSdFuelTransferRowsTests
{
    private const string LeftRow = "Left center transfer valve", RightRow = "Right center transfer valve";

    private static readonly Dictionary<string, double> BothOpen = new()
    {
        ["FUELSYSTEM VALVE OPEN:9"] = 1, ["FUELSYSTEM VALVE OPEN:10"] = 1,
        ["FUELSYSTEM VALVE OPEN:11"] = 1, ["FUELSYSTEM VALVE OPEN:12"] = 1,
        ["FUELSYSTEM JUNCTION SETTING:4"] = 1, ["FUELSYSTEM JUNCTION SETTING:5"] = 1,
        ["FUEL_PUMP_C1"] = 1, ["FUEL_PUMP_C2"] = 1,
        ["A32NX_OVHD_FUEL_MODESEL_MANUAL"] = 0,
    };

    private static (string left, string right) Read(FlyByWireA320Definition def, Dictionary<string, double> cache)
    {
        var rows = def.SdSystemRows(8, k => cache.TryGetValue(k, out double v) ? v : null);
        string Text(string label)
        {
            var row = rows.Single(r => r.label == label);
            return row.fmt(cache[row.var]);
        }
        return (Text(LeftRow), Text(RightRow));
    }

    private static Dictionary<string, double> With(params (string key, double value)[] changes)
    {
        var cache = new Dictionary<string, double>(BothOpen);
        foreach (var (key, value) in changes) cache[key] = value;
        return cache;
    }

    public static TheoryData<string> Airframes() => new() { "A320", "HW_A330" };

    private static FlyByWireA320Definition New(string airframe) =>
        airframe == "HW_A330" ? new HeadwindA330Definition() : new FlyByWireA320Definition();

    [Theory]
    [MemberData(nameof(Airframes))]
    public void Each_side_reads_its_own_auto_valve(string airframe)
    {
        var def = New(airframe);
        Assert.Equal(("closed", "open"), Read(def, With(("FUELSYSTEM VALVE OPEN:11", 0))));
        Assert.Equal(("open", "closed"), Read(def, With(("FUELSYSTEM VALVE OPEN:12", 0))));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void Each_side_reads_its_own_button_valve(string airframe)
    {
        var def = New(airframe);
        Assert.Equal(("closed", "open"), Read(def, With(("FUELSYSTEM VALVE OPEN:9", 0))));
        Assert.Equal(("open", "closed"), Read(def, With(("FUELSYSTEM VALVE OPEN:10", 0))));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void Each_side_reads_its_own_junction(string airframe)
    {
        var def = New(airframe);
        // Both auto valves shut: only the side routed MANUAL still shows open.
        var autoShut = With(("FUELSYSTEM VALVE OPEN:11", 0), ("FUELSYSTEM VALVE OPEN:12", 0));
        autoShut["FUELSYSTEM JUNCTION SETTING:4"] = 2;
        Assert.Equal(("open", "closed"), Read(def, autoShut));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void Each_side_reads_its_own_button(string airframe)
    {
        var def = New(airframe);
        Assert.Equal(("open, Amber", "open"), Read(def, With(("FUEL_PUMP_C1", 0))));
        Assert.Equal(("open", "open, Amber"), Read(def, With(("FUEL_PUMP_C2", 0))));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void The_color_reads_the_mode_selector(string airframe)
    {
        var def = New(airframe);
        var partlyOpen = With(("FUELSYSTEM VALVE OPEN:9", 0.5), ("FUELSYSTEM VALVE OPEN:10", 0.5));
        Assert.Equal(("open, Amber", "open, Amber"), Read(def, partlyOpen));
        partlyOpen["A32NX_OVHD_FUEL_MODESEL_MANUAL"] = 1;
        Assert.Equal(("open", "open"), Read(def, partlyOpen));
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void The_fuel_page_has_no_center_pump_row(string airframe)
    {
        var def = New(airframe);
        var labels = def.SdSystemRows(8, _ => null).Select(r => r.label).ToList();
        Assert.DoesNotContain("Center pump", labels);
        Assert.Contains(LeftRow, labels);
        Assert.Contains(RightRow, labels);
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void Every_transfer_input_is_registered_and_refreshed(string airframe)
    {
        var def = New(airframe);
        var vars = def.GetVariables();
        foreach (string valve in new[] { "FUELSYSTEM VALVE OPEN:9", "FUELSYSTEM VALVE OPEN:10",
                                         "FUELSYSTEM VALVE OPEN:11", "FUELSYSTEM VALVE OPEN:12" })
        {
            Assert.True(vars.TryGetValue(valve, out var v), $"{valve} is not registered");
            Assert.Equal("percent over 100", v!.Units);
            Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.SimVar, v.Type);
        }
        foreach (string junction in new[] { "FUELSYSTEM JUNCTION SETTING:4", "FUELSYSTEM JUNCTION SETTING:5" })
        {
            Assert.True(vars.TryGetValue(junction, out var j), $"{junction} is not registered");
            Assert.Equal("number", j!.Units);
            Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.SimVar, j.Type);
        }
        Assert.False(vars.ContainsKey("FUELSYSTEM PUMP ACTIVE:1"), "the dropped center pump row's var is still registered");

        // The refresh force-reads every row the null pass returns, hidden rows included, so the
        // on-request siblings a row reads from the cache must be among them.
        var refreshed = def.SdSystemRows(8).Select(r => r.var).ToHashSet();
        foreach (string input in OnRequestInputs)
            Assert.Contains(input, refreshed);

        // The buttons and MODE SEL ride the continuous batch, so the cache always holds them: a
        // force-read would only re-fire an unchanged value through MainForm every refresh tick.
        foreach (string live in BatchInputs)
        {
            Assert.True(vars.TryGetValue(live, out var b), $"{live} is not registered");
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous, b!.UpdateFrequency);
            Assert.True(b.IsAnnounced, $"{live} is not in the continuous batch");
            Assert.DoesNotContain(live, refreshed);
        }
    }

    private static readonly string[] OnRequestInputs =
    {
        "FUELSYSTEM VALVE OPEN:9", "FUELSYSTEM VALVE OPEN:10", "FUELSYSTEM VALVE OPEN:11", "FUELSYSTEM VALVE OPEN:12",
        "FUELSYSTEM JUNCTION SETTING:4", "FUELSYSTEM JUNCTION SETTING:5",
    };

    private static readonly string[] BatchInputs = { "FUEL_PUMP_C1", "FUEL_PUMP_C2", "A32NX_OVHD_FUEL_MODESEL_MANUAL" };

    [Theory]
    [MemberData(nameof(Airframes))]
    public void The_outer_to_inner_transfer_valves_read_open_or_closed(string airframe)
    {
        // Both SDs draw a transfer arrow from each outer tank while FUELSYSTEM VALVE OPEN:4 / :5 is set.
        var def = New(airframe);
        var cache = new Dictionary<string, double> { ["FUELSYSTEM VALVE OPEN:4"] = 1, ["FUELSYSTEM VALVE OPEN:5"] = 0 };
        var rows = def.SdSystemRows(8, k => cache.TryGetValue(k, out double v) ? v : null);
        string Text(string label)
        {
            var row = rows.Single(r => r.label == label);
            return row.fmt(cache[row.var]);
        }
        Assert.Equal("open", Text("Left outer transfer valve"));
        Assert.Equal("closed", Text("Right outer transfer valve"));

        var vars = def.GetVariables();
        foreach (string valve in new[] { "FUELSYSTEM VALVE OPEN:4", "FUELSYSTEM VALVE OPEN:5" })
        {
            Assert.True(vars.TryGetValue(valve, out var v), $"{valve} is not registered");
            Assert.Equal("bool", v!.Units);
            Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.SimVar, v.Type);
        }
    }

    [Fact]
    public void The_button_valve_is_not_named_as_the_whole_indication()
    {
        // Valve 9 / 10 is one half of the pair; the row of the same side names the indication.
        var vars = new FlyByWireA320Definition().GetVariables();
        Assert.Equal("Left center transfer button valve", vars["FUELSYSTEM VALVE OPEN:9"].DisplayName);
        Assert.Equal("Right center transfer button valve", vars["FUELSYSTEM VALVE OPEN:10"].DisplayName);
    }
}
