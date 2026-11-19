// What the engine display reader says by itself: alerts appearing ("Upper DU alert: …") and going ("Upper
// DU alert gone: …"), gauge colours ("Upper DU: engine 1 EGT amber") and the thrust mode ("Upper DU:
// thrust mode CLB"), each on its own Ctrl+M row. Items of one form found together share a line with the
// prefix once; the prefix names the display the item is drawn on now. Nothing is said for readings taken
// before PMDG's aircraft initialises (constructor values).

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using static MSFSBlindAssist.Services.PMDG.Pmdg737EngineDisplaySpeech;

namespace MSFSBlindAssist.Tests;

public class Pmdg737EngineDisplaySpeechTests
{
    private static readonly Pmdg737EngineDisplayLayout Layout = GoldenLayouts.EngineDisplays;

    private static EngineDisplaySample Idle => EngineDisplaySampleFactory.Idle();

    private static EngineValues E(int n) => EngineDisplaySampleFactory.IdleEngine(n);

    private static EngineDisplaySample With(EngineValues e1, EngineValues e2, EngineDisplaySample? s = null) =>
        (s ?? Idle) with { Engines = new[] { e1, e2 } };

    private static IReadOnlyList<EngineDisplayAnnouncement> Say(EngineDisplaySample before, EngineDisplaySample after) =>
        Changes(before, after, Layout);

    private static EngineDisplayAnnouncement Alert(string text) => new(EngineDisplayMuteRow.Alerts, text);

    [Fact]
    public void Start_valve_appearing() =>
        Assert.Equal(new[] { Alert("Upper DU alert: engine 1 START VALVE OPEN") }, Say(Idle, With(E(1) with { StartValve = 1 }, E(2))));

    [Fact]
    public void Low_oil_pressure_going() =>
        Assert.Equal(new[] { Alert("Upper DU alert gone: engine 1 LOW OIL PRESSURE") },
            Say(With(E(1) with { LowOilPressure = 2 }, E(2)), Idle));

    [Fact]
    public void Appearing_and_going_in_one_window_are_two_lines() =>
        Assert.Equal(new[] { Alert("Upper DU alert: engine 1 START VALVE OPEN"), Alert("Upper DU alert gone: engine 1 LOW OIL PRESSURE") },
            Say(With(E(1) with { LowOilPressure = 2 }, E(2)), With(E(1) with { StartValve = 2 }, E(2))));

    [Fact]
    public void Two_engines_one_line_prefix_once() =>
        Assert.Equal(new[] { Alert("Upper DU alert: engine 1 START VALVE OPEN, engine 2 START VALVE OPEN") },
            Say(Idle, With(E(1) with { StartValve = 1 }, E(2) with { StartValve = 1 })));

    [Fact]
    public void Flashing_to_steady_is_not_a_change() =>
        Assert.Empty(Say(With(E(1) with { StartValve = 1 }, E(2)), With(E(1) with { StartValve = 2 }, E(2))));

    [Fact]
    public void Fuel_config_and_tank_low()
    {
        Assert.Equal(new[] { Alert("Upper DU alert: fuel CONFIG, tank 1 fuel LOW") }, Say(Idle, Idle with { FuelWarning = new[] { 2, 1, 0 } }));
        Assert.Equal(new[] { Alert("Upper DU alert: tank 2 fuel IMBAL") }, Say(Idle, Idle with { FuelWarning = new[] { 0, 0, 3 } }));
    }

    [Fact]
    public void Color_up_and_back_to_normal()
    {
        var amber = With(E(1) with { Egt = 930 }, E(2));
        Assert.Equal(new[] { new EngineDisplayAnnouncement(EngineDisplayMuteRow.Colors, "Upper DU: engine 1 EGT amber") }, Say(Idle, amber));
        Assert.Equal(new[] { new EngineDisplayAnnouncement(EngineDisplayMuteRow.Colors, "Upper DU: engine 1 EGT normal") }, Say(amber, Idle));
        Assert.Equal(new[] { new EngineDisplayAnnouncement(EngineDisplayMuteRow.Colors, "Upper DU: engine 1 EGT red, engine 2 EGT amber") },
            Say(Idle, With(E(1) with { Egt = 960 }, E(2) with { Egt = 930 })));
    }

    [Fact]
    public void Secondary_gauge_colours_use_their_display() =>
        Assert.Equal(new[] { new EngineDisplayAnnouncement(EngineDisplayMuteRow.Colors, "Lower DU: engine 2 OIL TEMP amber, engine 1 VIB high") },
            Say(Idle, With(E(1) with { Vibration = 4.2 }, E(2) with { OilTemperature = 150 })));

    [Fact]
    public void Thrust_mode_change()
    {
        Assert.Equal(new[] { new EngineDisplayAnnouncement(EngineDisplayMuteRow.ThrustMode, "Upper DU: thrust mode CLB") },
            Say(Idle, Idle with { ThrustMode = 4 }));
        Assert.Equal(new[] { new EngineDisplayAnnouncement(EngineDisplayMuteRow.ThrustMode, "Upper DU: thrust mode R-TO") },
            Say(Idle, Idle with { AssumedTempShown = true }));
        Assert.Empty(Say(Idle, Idle with { AssumedTemperature = 35 }));
    }

    [Fact]
    public void Eng_pri_moves_the_prefix_to_lower_du()
    {
        // ENG PRI: upper blank, lower compact (live 2026-10-23).
        var engPri = Idle with { UpperPage = 1, LowerPage = 7 };
        Assert.Equal(new[] { Alert("Lower DU alert: engine 1 START VALVE OPEN") },
            Say(engPri, With(E(1) with { StartValve = 1 }, E(2), engPri)));
    }

    [Fact]
    public void Secondary_items_use_lower_du()
    {
        var crossBleed = With(E(1) with { CrossBleedStart = true }, E(2));
        Assert.Equal(new[] { Alert("Lower DU alert: engine 1 X-BLD START") }, Say(Idle, crossBleed));
        // Lower DU on the SYS page: N2 is drawn nowhere, still announced with its normal display.
        Assert.Equal(new[] { Alert("Lower DU alert: engine 1 X-BLD START") },
            Say(Idle with { LowerPage = 13 }, crossBleed with { LowerPage = 13 }));
        // The compact page on the upper display draws N2 there.
        Assert.Equal(new[] { Alert("Upper DU alert: engine 1 X-BLD START") },
            Say(Idle with { UpperPage = 7, LowerPage = 3 }, crossBleed with { UpperPage = 7, LowerPage = 3 }));
    }

    [Fact]
    public void A_change_of_prefix_starts_a_new_line() =>
        Assert.Equal(new[] { Alert("Upper DU alert: engine 1 START VALVE OPEN"), Alert("Lower DU alert: engine 1 X-BLD START") },
            Say(Idle, With(E(1) with { StartValve = 2, CrossBleedStart = true }, E(2))));

    [Fact]
    public void Reverser_appears_with_its_colour_changes_it_and_goes()
    {
        var amber = With(E(1) with { ReverserShown = true }, E(2));
        var green = With(E(1) with { ReverserShown = true, ReverserSecond = true }, E(2));
        Assert.Equal(new[] { Alert("Upper DU alert: engine 1 REV amber") }, Say(Idle, amber));
        Assert.Equal(new[] { Alert("Upper DU alert: engine 1 REV green") }, Say(amber, green));
        Assert.Equal(new[] { Alert("Upper DU alert gone: engine 1 REV green") }, Say(green, Idle));
    }

    [Fact]
    public void Only_the_amber_tai_is_an_alert()
    {
        Assert.Empty(Say(Idle, With(E(1) with { TaiOn = true }, E(2))));
        Assert.Equal(new[] { Alert("Upper DU alert: engine 2 TAI amber") }, Say(Idle, With(E(1), E(2) with { TaiAmber = true })));
    }

    [Fact]
    public void Engine_fail_and_oil_quantity_low()
    {
        Assert.Equal(new[] { Alert("Upper DU alert: engine 1 ENG FAIL") }, Say(Idle, With(E(1) with { EngineFail = true }, E(2))));
        var low = With(E(1), E(2) with { OilQuantity = 0.2 });
        Assert.Equal(new[] { Alert("Lower DU alert: engine 2 OIL QTY LO") }, Say(Idle, low));
        Assert.Empty(Say(Idle with { OilLowIndication = false }, low with { OilLowIndication = false }));
    }

    [Fact]
    public void Advisories_and_memos_in_the_alert_column()
    {
        Assert.Equal(new[] { Alert("Upper DU alert: NO AUTOLAND, A/T LIM, ATC, COMM") },
            Say(Idle, Idle with { AutolandAdvisory = 0, AutothrottleLimit = true, AtcMemo = true, CommMemo = true }));
        Assert.Equal(new[] { Alert("Upper DU alert: NO LAND 3"), Alert("Upper DU alert gone: NO AUTOLAND") },
            Say(Idle with { AutolandAdvisory = 0 }, Idle with { AutolandAdvisory = 1 }));
    }

    [Fact]
    public void Alerts_colours_and_thrust_mode_are_separate_lines_in_that_order() =>
        Assert.Equal(new[]
        {
            Alert("Upper DU alert: engine 1 START VALVE OPEN"),
            new EngineDisplayAnnouncement(EngineDisplayMuteRow.Colors, "Upper DU: engine 1 EGT amber"),
            new EngineDisplayAnnouncement(EngineDisplayMuteRow.ThrustMode, "Upper DU: thrust mode CLB"),
        }, Say(Idle, With(E(1) with { StartValve = 2, Egt = 930 }, E(2), Idle with { ThrustMode = 4 })));

    [Fact]
    public void Nothing_changed_gives_nothing() => Assert.Empty(Say(Idle, Idle));

    [Fact]
    public void Readings_before_the_aircraft_initialises_say_nothing()
    {
        // The constructors' values (live 2026-10-24): X-BLD START set, EGT limits 933/860.
        var junk = With(E(1) with { CrossBleedStart = true }, E(2) with { CrossBleedStart = true },
            Idle with { Initialised = false, EgtRedLine = 933, EgtMaxContinuous = 860 });
        Assert.Empty(Say(junk, Idle));
        Assert.Empty(Say(Idle, junk));
    }

    [Theory]
    [InlineData(0, 0)] // off: unpowered (live 2026-10-24, cold and dark: every unit on page 0)
    [InlineData(1, 1)] // blank
    [InlineData(0, 1)]
    public void Nothing_is_said_while_both_displays_are_dark(int upper, int lower)
    {
        // Cold and dark, PMDG's aircraft has initialised and its logic runs: A/T LIM shows while the FMC is
        // unpowered and the empty tanks read LOW, but no display shows them.
        var dark = Idle with { UpperPage = upper, LowerPage = lower };
        Assert.Empty(Say(dark, dark with { AutothrottleLimit = true, FuelWarning = new[] { 0, 1, 1 } }));
        Assert.Empty(Say(dark with { AutothrottleLimit = true }, dark));
    }

    [Fact]
    public void Displays_lighting_up_or_going_dark_say_nothing()
    {
        var dark = With(E(1) with { LowOilPressure = 2 }, E(2) with { LowOilPressure = 2 },
            Idle with { UpperPage = 0, LowerPage = 0, AutothrottleLimit = true });
        Assert.Empty(Say(dark, Idle));
        Assert.Empty(Say(Idle, dark));
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Sentences_are_invariant_under_a_comma_decimal_culture(string culture)
    {
        var after = With(E(1) with { StartValve = 2, Egt = 930 }, E(2) with { StartValve = 2 }, Idle with { ThrustMode = 4, FuelWarning = new[] { 2, 0, 0 } });
        var expected = new[]
        {
            "Upper DU alert: engine 1 START VALVE OPEN, engine 2 START VALVE OPEN, fuel CONFIG",
            "Upper DU: engine 1 EGT amber",
            "Upper DU: thrust mode CLB",
        };
        Assert.Equal(expected, TestCulture.Run(culture, () => Say(Idle, after).Select(a => a.Text).ToArray()));
    }

    [Fact]
    public void Status_lines()
    {
        Assert.Equal("Engine displays starting", Starting);
        Assert.Equal("Engine display announcements resumed", Resumed);
        Assert.Equal("Engine display announcements unavailable: PMDG file not recognised", Unavailable(FmaReasons.FileNotRecognised));
        Assert.Equal("Engine display announcements stopped: values do not match the SDK", Stopped(FmaReasons.Mismatch));
        Assert.Equal("Engine displays unavailable: no access to the simulator", ListUnavailable(FmaReasons.NoAccess));
        Assert.Equal("Engine displays stopped: aircraft memory not found, trying again", ListStopped(FmaReasons.MemoryNotFound));
    }
}
