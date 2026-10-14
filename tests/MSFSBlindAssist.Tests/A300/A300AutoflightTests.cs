using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

public class A300TypedValuesTests
{
    private static A300TypedResult Plan(string key, double value, bool isMach = false) => A300TypedValues.Plan(key, value, isMach);

    [Theory]
    [InlineData(250, false, "250 (>L:INI_Airspeed_Dial)", "Speed 250 knots", null)]
    [InlineData(0.78, true, "0.78 (>L:INI_Airspeed_Dial)", "Mach 0.78", null)]
    [InlineData(0.78, false, "0.78 (>L:INI_Airspeed_Dial)", "Mach 0.78", "1 (>B:AIRLINER_SPDMACH_Set)")]
    [InlineData(280, true, "280 (>L:INI_Airspeed_Dial)", "Speed 280 knots", "1 (>B:AIRLINER_SPDMACH_Set)")]
    public void Speed_switches_spd_mach_first_when_the_unit_differs(double value, bool isMach, string rpn, string said, string? before)
    {
        var plan = Plan(A300TypedValues.SpeedKey, value, isMach);
        Assert.Equal(rpn, plan.Rpn);
        Assert.Equal(said, plan.Confirmation);
        Assert.Equal(before, plan.Before);
    }

    [Theory]
    [InlineData(A300TypedValues.SpeedKey, 99)]
    [InlineData(A300TypedValues.SpeedKey, 400)]
    [InlineData(A300TypedValues.SpeedKey, 0.05)]
    [InlineData(A300TypedValues.HeadingKey, 361)]
    [InlineData(A300TypedValues.AltitudeKey, 49100)]
    [InlineData(A300TypedValues.AltitudeKey, 0)]
    [InlineData(A300TypedValues.VerticalSpeedKey, 6100)]
    [InlineData(A300TypedValues.Vor1FrequencyKey, 118.0)]
    [InlineData(A300TypedValues.Vor1FrequencyKey, 113.92)]
    [InlineData(A300TypedValues.IlsFrequencyKey, 113.90)]
    [InlineData(A300TypedValues.Com1StandbyKey, 117.5)]
    [InlineData(A300TypedValues.SquawkKey, 7800)]
    [InlineData(A300TypedValues.SquawkKey, 1239)]
    [InlineData(A300TypedValues.BaroCaptainKey, 500)]
    [InlineData(A300TypedValues.HeadingKey, double.NaN)]
    public void Values_outside_the_range_are_refused_with_it(string key, double value)
    {
        var plan = Plan(key, value);
        Assert.Null(plan.Rpn);
        Assert.Equal(A300TypedValues.ErrorFor(key), plan.Error);
    }

    [Theory]
    [InlineData(A300TypedValues.VerticalSpeedKey, -1.5)]
    [InlineData(A300TypedValues.VerticalSpeedKey, 1500.5)]
    [InlineData(A300TypedValues.AltitudeKey, 12.5)]
    [InlineData(A300TypedValues.AltitudeKey, 12000.4)]
    [InlineData(A300TypedValues.MinimumsKey, 1.2)]
    [InlineData(A300TypedValues.MinimumsKey, 1200.4)]
    public void A_number_with_a_fraction_is_refused_as_a_separator_typo(string key, double value)
    {
        // Both ways of typing turn a comma into a point, so "-1,500" arrives as -1.5 and "1,200" as 1.2. They used to
        // round to 0 and 1, and be written and confirmed aloud ("Vertical speed 0 feet per minute").
        var plan = Plan(key, value);
        Assert.Null(plan.Rpn);
        Assert.Null(plan.Confirmation);
        Assert.Equal(A300TypedValues.WholeNumberError, plan.Error);
    }

    [Theory]
    [InlineData(-20)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(-99)]
    public void A_vertical_speed_under_100_is_refused(double value)
    {
        // As the MD-11 and the FBW A320 refuse it. 0 is the one value under 100 that stays valid: level off.
        var plan = Plan(A300TypedValues.VerticalSpeedKey, value);
        Assert.Null(plan.Rpn);
        Assert.Equal(A300TypedValues.VerticalSpeedError, plan.Error);
    }

    [Theory]
    [InlineData(A300TypedValues.AltitudeKey, 1, "100 to 49000 feet")]
    [InlineData(A300TypedValues.AltitudeKey, 12, "100 to 49000 feet")]
    [InlineData(A300TypedValues.AltitudeKey, 35, "100 to 49000 feet")]
    [InlineData(A300TypedValues.AltitudeKey, 49, "100 to 49000 feet")]
    [InlineData(A300TypedValues.VerticalSpeedKey, 1, "0, or 100 to 6000 feet per minute either way")]
    [InlineData(A300TypedValues.VerticalSpeedKey, -1, "0, or 100 to 6000 feet per minute either way")]
    [InlineData(A300TypedValues.VerticalSpeedKey, 6, "0, or 100 to 6000 feet per minute either way")]
    [InlineData(A300TypedValues.VerticalSpeedKey, -6, "0, or 100 to 6000 feet per minute either way")]
    [InlineData(A300TypedValues.MinimumsKey, 1, "0 to 2500 feet")]
    [InlineData(A300TypedValues.MinimumsKey, 2, "0 to 2500 feet")]
    [InlineData(A300TypedValues.MinimumsKey, 9, "0 to 2500 feet")]
    public void A_refused_whole_number_a_comma_thousand_could_have_made_names_the_separator(string key, double value, string range)
    {
        // A panel's box turns a comma into a point, so "12,000" arrives as 12, "-1,000" as -1 and "1,000" as 1. Refused
        // with the range alone, the pilot heard a range that holds what he typed; the decision height took it as 1 foot.
        var plan = Plan(key, value);
        Assert.Null(plan.Rpn);
        Assert.Null(plan.Confirmation);
        Assert.Equal($"type a whole number, without a thousands separator; {range}", plan.Error);
    }

    [Theory]
    [InlineData(A300TypedValues.AltitudeKey, 50)]       // "50,000" is above 49000: the range answers it
    [InlineData(A300TypedValues.AltitudeKey, -12)]      // "-12,000" is below 100
    [InlineData(A300TypedValues.VerticalSpeedKey, 7)]   // "7,000" is beyond 6000 either way
    [InlineData(A300TypedValues.VerticalSpeedKey, -7)]
    [InlineData(A300TypedValues.MinimumsKey, -1)]
    [InlineData(A300TypedValues.MinimumsKey, 2600)]
    public void A_refused_whole_number_whose_comma_thousand_is_outside_the_range_gets_the_range(string key, double value) =>
        Assert.Equal(A300TypedValues.ErrorFor(key), Plan(key, value).Error);

    [Fact]
    public void No_decision_height_of_1_to_9_feet_is_written_or_confirmed()
    {
        // No real decision height is 1 to 9 feet, and inside 0 to 2500 a comma-thousand makes only 1 ("1,000") and 2
        // ("2,000"): "Decision height 1 feet" wrote 1 foot to both minimums, and the call-out effectively never came.
        for (int feet = 1; feet <= 9; feet++)
        {
            var plan = Plan(A300TypedValues.MinimumsKey, feet);
            Assert.Null(plan.Rpn);
            Assert.Null(plan.Confirmation);
        }
    }

    [Fact]
    public void The_typed_value_ranges_carry_no_thousands_separator()
    {
        // A box's hint and refusal are what the pilot types back: "-6,000" invites "-1,500".
        Assert.Equal("type a whole number, without a thousands separator", A300TypedValues.WholeNumberError);
        Assert.Equal("100 to 49000 feet", A300TypedValues.AltitudeError);
        Assert.Equal("0, or 100 to 6000 feet per minute either way", A300TypedValues.VerticalSpeedError);
        Assert.Equal("0 to 2500 feet", A300TypedValues.MinimumsError);
        foreach (var key in new[] { A300TypedValues.AltitudeKey, A300TypedValues.VerticalSpeedKey, A300TypedValues.MinimumsKey })
            Assert.DoesNotMatch(@"\d,\d", A300TypedValues.ErrorFor(key));
    }

    [Theory]
    [InlineData(A300TypedValues.HeadingKey, 360, "0 (>L:INI_HEADING_DIAL)", "Heading 000")]
    [InlineData(A300TypedValues.HeadingKey, 5.4, "5 (>L:INI_HEADING_DIAL)", "Heading 005")]
    [InlineData(A300TypedValues.AltitudeKey, 12049, "12000 (>L:INI_Altitude_Dial)", "Altitude 12,000 feet")]
    [InlineData(A300TypedValues.VerticalSpeedKey, -1520, "-1500 (>L:INI_vvi_dial)", "Vertical speed -1,500 feet per minute")]
    [InlineData(A300TypedValues.VerticalSpeedKey, 0, "0 (>L:INI_vvi_dial)", "Vertical speed 0 feet per minute")]
    [InlineData(A300TypedValues.VerticalSpeedKey, 100, "100 (>L:INI_vvi_dial)", "Vertical speed 100 feet per minute")]
    [InlineData(A300TypedValues.VerticalSpeedKey, -6000, "-6000 (>L:INI_vvi_dial)", "Vertical speed -6,000 feet per minute")]
    [InlineData(A300TypedValues.Vor1FrequencyKey, 113.9, "113 (>L:INI_VOR1_FREQUENCY_MHZ) 90 (>L:INI_VOR1_FREQUENCY_KHZ)", "VOR 1 113.90")]
    [InlineData(A300TypedValues.Vor2FrequencyKey, 112.05, "112 (>L:INI_VOR2_FREQUENCY_MHZ) 5 (>L:INI_VOR2_FREQUENCY_KHZ)", "VOR 2 112.05")]
    [InlineData(A300TypedValues.IlsFrequencyKey, 110.3, "110 (>L:INI_ILS_FREQUENCY_MHZ) 30 (>L:INI_ILS_FREQUENCY_KHZ)", "ILS 110.30")]
    [InlineData(A300TypedValues.Vor1CourseKey, 90, "90 (>K:VOR1_SET)", "VOR 1 course 090")]
    [InlineData(A300TypedValues.IlsCourseKey, 135, "135 (>L:INI_ils_course)", "ILS course 135")]
    [InlineData(A300TypedValues.Com2StandbyKey, 121.5, "121500000 (>K:COM2_STBY_RADIO_SET_HZ)", "VHF 2 standby 121.500")]
    [InlineData(A300TypedValues.SquawkKey, 4521, "17697 (>K:XPNDR_SET)", "Squawk 4521")]
    [InlineData(A300TypedValues.SquawkKey, 22, "34 (>K:XPNDR_SET)", "Squawk 0022")]
    [InlineData(A300TypedValues.BaroCaptainKey, 1020, "1 16320 (>K:2:KOHLSMAN_SET)", "Captain altimeter 1020 hectopascals, 30.12 inches")]
    public void Values_are_written_the_way_the_aircraft_takes_them(string key, double value, string rpn, string said)
    {
        var plan = Plan(key, value);
        Assert.Null(plan.Error);
        Assert.Equal(rpn, plan.Rpn);
        Assert.Equal(said, plan.Confirmation);
    }

    [Theory]
    [InlineData(200, "200 (>L:INI_MINIMUMS_PILOT) 200 (>L:INI_MINIMUMS_FO)", "Decision height 200 feet")]
    [InlineData(10, "10 (>L:INI_MINIMUMS_PILOT) 10 (>L:INI_MINIMUMS_FO)", "Decision height 10 feet")]
    [InlineData(1200, "1200 (>L:INI_MINIMUMS_PILOT) 1200 (>L:INI_MINIMUMS_FO)", "Decision height 1,200 feet")]
    [InlineData(2500, "2500 (>L:INI_MINIMUMS_PILOT) 2500 (>L:INI_MINIMUMS_FO)", "Decision height 2,500 feet")]
    [InlineData(0, "0 (>L:INI_MINIMUMS_PILOT) 0 (>L:INI_MINIMUMS_FO)", "Decision height not set")]
    public void A_decision_height_sets_both_pilots_minimums(double value, string rpn, string said)
    {
        var plan = Plan(A300TypedValues.MinimumsKey, value);
        Assert.Null(plan.Error);
        Assert.Equal(rpn, plan.Rpn);
        Assert.Equal(said, plan.Confirmation);
    }

    [Theory]
    [InlineData(2600)]
    [InlineData(-10)]
    public void A_decision_height_out_of_range_is_refused(double value)
    {
        var plan = Plan(A300TypedValues.MinimumsKey, value);
        Assert.Null(plan.Rpn);
        Assert.Equal("0 to 2500 feet", plan.Error);
    }

    [Fact]
    public void One_altimeter_entry_sets_all_three_in_inches_or_hectopascals()
    {
        var plan = A300TypedValues.AllAltimeters(29.92);
        Assert.Equal("1 16211 (>K:2:KOHLSMAN_SET) 2 16211 (>K:2:KOHLSMAN_SET) 3 16211 (>K:2:KOHLSMAN_SET)", plan.Rpn);
        Assert.Equal("Altimeters 1013 hectopascals, 29.92 inches", plan.Confirmation);
    }

    [Fact]
    public void A_typed_inch_setting_is_confirmed_as_the_value_written()
    {
        // 29.90 inches is 1012.53 hectopascals, but the aircraft stores sixteenths, 16200 of them (1012.5), which every
        // later read-back says as 1012. The confirmation, worked out from the unrounded 1012.53, said 1013.
        var plan = A300TypedValues.Altimeter(29.90, new[] { 1 }, "Captain altimeter");
        Assert.Equal("1 16200 (>K:2:KOHLSMAN_SET)", plan.Rpn);
        Assert.Equal($"Captain altimeter {A300Readouts.Altimeter(16200 / 16.0)}", plan.Confirmation);
        Assert.Equal("Captain altimeter 1012 hectopascals, 29.90 inches", plan.Confirmation);
    }

    [Fact]
    public void Every_typed_setting_is_confirmed_as_the_aircraft_will_read_it_back()
    {
        // Every inch value the box takes (28.20 to 31.30) and every whole hectopascal (955 to 1060): about 3% of the
        // inch entries differed by one hectopascal.
        var typed = Enumerable.Range(2820, 3130 - 2820 + 1).Select(h => h / 100.0)
            .Concat(Enumerable.Range(955, 1060 - 955 + 1).Select(hpa => (double)hpa));
        var mismatches = new List<string>();
        foreach (double value in typed)
        {
            var plan = A300TypedValues.Altimeter(value, new[] { 1 }, "Captain altimeter");
            Assert.Null(plan.Error);
            double stored = double.Parse(plan.Rpn!.Split(' ')[1], CultureInfo.InvariantCulture) / 16;
            string readBack = $"Captain altimeter {A300Readouts.Altimeter(stored)}";
            if (plan.Confirmation != readBack)
                mismatches.Add($"{value.ToString(CultureInfo.InvariantCulture)}: confirmed \"{plan.Confirmation}\", read back \"{readBack}\"");
        }
        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Typed_values_read_and_write_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("0.78 (>L:INI_Airspeed_Dial)", Plan(A300TypedValues.SpeedKey, 0.78, true).Rpn);
            Assert.Equal("Mach 0.78", Plan(A300TypedValues.SpeedKey, 0.78, true).Confirmation);
            Assert.Equal("VOR 1 113.90", Plan(A300TypedValues.Vor1FrequencyKey, 113.9).Confirmation);
            Assert.Equal("Altitude 12,000 feet", Plan(A300TypedValues.AltitudeKey, 12000).Confirmation);
            Assert.Equal("VHF 2 standby 121.500", Plan(A300TypedValues.Com2StandbyKey, 121.5).Confirmation);
            Assert.Equal("Mach 0.82", A300FcuState.SpeedWindow(0.82, isMach: true));
            Assert.Equal("Decision height 1,200 feet", Plan(A300TypedValues.MinimumsKey, 1200).Confirmation);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Every_typed_row_lands_on_its_panel_with_a_set_key()
    {
        var placement = A300PanelLayout.Place(A300ControlMap.Load());
        foreach (var typed in A300TypedValues.All)
        {
            Assert.Contains("_SET", typed.Key);
            Assert.Contains(placement.RowsByPanel[typed.Panel], r => r.Key == typed.Key && r.Action == A300RowAction.Typed);
        }
    }
}

public class IniA300AutoflightBehaviourTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly List<TaskCompletionSource> _waits = new();

    public IniA300AutoflightBehaviourTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            TypedDelay = _ =>
            {
                var wait = new TaskCompletionSource();
                _waits.Add(wait);
                return wait.Task;
            },
        };
        _def.Attach(_sim);
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    [Fact]
    public void A_typed_heading_is_sent_and_confirmed()
    {
        Assert.True(Set(A300TypedValues.HeadingKey, 270));
        Assert.Equal(new[] { "270 (>L:INI_HEADING_DIAL)" }, _sent);
        Assert.Equal(new[] { "Heading 270" }, _speech.All);
    }

    [Fact]
    public void A_typed_mach_in_speed_mode_switches_then_waits_then_writes()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        Assert.True(Set(A300TypedValues.SpeedKey, 0.78));
        Assert.Equal(new[] { "1 (>B:AIRLINER_SPDMACH_Set)" }, _sent);
        Assert.Empty(_speech.All);
        _waits.Single().SetResult();
        Assert.Equal(new[] { "1 (>B:AIRLINER_SPDMACH_Set)", "0.78 (>L:INI_Airspeed_Dial)" }, _sent);
        Assert.Equal(new[] { "Mach 0.78" }, _speech.All);
    }

    [Fact]
    public void A_typed_value_out_of_range_says_the_range_and_sends_nothing()
    {
        Assert.True(Set(A300TypedValues.AltitudeKey, 60000));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Altitude: 100 to 49000 feet" }, _speech.All);
    }

    [Theory]
    [InlineData(A300TypedValues.VerticalSpeedKey, -1.5, "Vertical speed")]
    [InlineData(A300TypedValues.AltitudeKey, 12.5, "Altitude")]
    [InlineData(A300TypedValues.MinimumsKey, 1.2, "Decision height")]
    public void A_panel_box_value_with_a_fraction_says_why_and_sends_nothing(string key, double value, string name)
    {
        // The panel's box hands "-1,500" over as -1.5.
        Assert.True(Set(key, value));
        Assert.Empty(_sent);
        Assert.Equal(new[] { $"{name}: type a whole number, without a thousands separator" }, _speech.All);
    }

    private (bool Valid, string Message) Box(string key, string name, string typed) => _def.ValidateBoxInput(key, name, typed);

    private const string AltitudeSeparator = "Altitude: type a whole number, without a thousands separator; 100 to 49000 feet";
    private const string VerticalSpeedSeparator =
        "Vertical speed: type a whole number, without a thousands separator; 0, or 100 to 6000 feet per minute either way";
    private const string DecisionHeightSeparator = "Decision height: type a whole number, without a thousands separator; 0 to 2500 feet";

    [Theory]
    [InlineData(A300TypedValues.AltitudeKey, 12, AltitudeSeparator)]
    [InlineData(A300TypedValues.AltitudeKey, 35, AltitudeSeparator)]
    [InlineData(A300TypedValues.VerticalSpeedKey, -1, VerticalSpeedSeparator)]
    [InlineData(A300TypedValues.VerticalSpeedKey, 2, VerticalSpeedSeparator)]
    [InlineData(A300TypedValues.MinimumsKey, 1, DecisionHeightSeparator)]
    [InlineData(A300TypedValues.MinimumsKey, 2, DecisionHeightSeparator)]
    public void A_panel_box_number_a_thousands_separator_could_have_made_names_it_and_sends_nothing(string key, double value, string said)
    {
        // The panel's box hands "12,000" over as 12, "-1,000" as -1 and "1,000" as 1: only the number reaches the definition.
        Assert.True(Set(key, value));
        Assert.Empty(_sent);
        Assert.Equal(new[] { said }, _speech.All);
    }

    [Theory]
    [InlineData(A300TypedValues.AltitudeKey, 12000, "12000 (>L:INI_Altitude_Dial)", "Altitude 12,000 feet")]
    [InlineData(A300TypedValues.VerticalSpeedKey, -1500, "-1500 (>L:INI_vvi_dial)", "Vertical speed -1,500 feet per minute")]
    [InlineData(A300TypedValues.MinimumsKey, 0, "0 (>L:INI_MINIMUMS_PILOT) 0 (>L:INI_MINIMUMS_FO)", "Decision height not set")]
    [InlineData(A300TypedValues.MinimumsKey, 200, "200 (>L:INI_MINIMUMS_PILOT) 200 (>L:INI_MINIMUMS_FO)", "Decision height 200 feet")]
    public void A_panel_box_whole_number_typed_without_a_separator_is_still_sent_and_confirmed(string key, double value, string rpn, string said)
    {
        Assert.True(Set(key, value));
        Assert.Equal(new[] { rpn }, _sent);
        Assert.Equal(new[] { said }, _speech.All);
    }

    [Theory]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", "12,000")]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", "35,000")]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", " 12,000 ")]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", "1,000,000")]
    [InlineData(A300TypedValues.VerticalSpeedKey, "Vertical speed", "-1,000")]
    [InlineData(A300TypedValues.VerticalSpeedKey, "Vertical speed", "2,000")]
    [InlineData(A300TypedValues.MinimumsKey, "Decision height", "1,000")]
    [InlineData(A300TypedValues.MinimumsKey, "Decision height", "2,000")]
    public void The_value_box_refuses_a_thousands_separator_by_name(string key, string name, string typed)
    {
        // The box turns a comma into a point, so "12,000" arrived as 12 and was answered "100 to 49000 feet", a range that
        // holds the 12,000 typed, and "-1,000" as -1, answered with the vertical speed range. The text says what it was.
        Assert.Equal((false, $"{name}: type a whole number, without a thousands separator"), Box(key, name, typed));
    }

    [Theory]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", "12", AltitudeSeparator)]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", "12.000", AltitudeSeparator)]   // a point as the separator
    [InlineData(A300TypedValues.VerticalSpeedKey, "Vertical speed", "-1", VerticalSpeedSeparator)]
    [InlineData(A300TypedValues.MinimumsKey, "Decision height", "1", DecisionHeightSeparator)]
    public void The_value_box_answers_a_number_a_separator_could_have_made_as_the_panel_box_does(string key, string name, string typed,
        string said) =>
        Assert.Equal((false, said), Box(key, name, typed));

    [Theory]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", "12000")]
    [InlineData(A300TypedValues.VerticalSpeedKey, "Vertical speed", "-1500")]
    [InlineData(A300TypedValues.MinimumsKey, "Decision height", "0")]
    [InlineData(A300TypedValues.MinimumsKey, "Decision height", "200")]
    public void The_value_box_still_takes_a_whole_number_typed_without_a_separator(string key, string name, string typed) =>
        Assert.Equal((true, ""), Box(key, name, typed));

    [Theory]
    [InlineData(A300TypedValues.SpeedKey, "Speed or Mach", "0,780")]
    [InlineData(A300TypedValues.Com1StandbyKey, "VHF 1 standby frequency", "121,500")]
    [InlineData(A300TypedValues.IlsFrequencyKey, "ILS frequency", "110,300")]
    [InlineData(A300TypedValues.BaroStandbyKey, "Standby altimeter setting", "29,920")]
    public void A_comma_in_a_value_that_is_not_a_whole_number_is_still_its_decimal_point(string key, string name, string typed) =>
        // Only an altitude, a vertical speed or a decision height wants a whole number: elsewhere "121,500" is 121.5.
        Assert.Equal((true, ""), Box(key, name, typed));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_thousands_separator_is_refused_by_name_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            // The value boxes, from the text.
            Assert.Equal((false, "Altitude: type a whole number, without a thousands separator"),
                Box(A300TypedValues.AltitudeKey, "Altitude", "12,000"));
            Assert.Equal((false, "Vertical speed: type a whole number, without a thousands separator"),
                Box(A300TypedValues.VerticalSpeedKey, "Vertical speed", "-1,000"));
            Assert.Equal((false, "Decision height: type a whole number, without a thousands separator"),
                Box(A300TypedValues.MinimumsKey, "Decision height", "1,000"));
            // In these cultures "12.000" is twelve thousand, but the box reads every point as a decimal point.
            Assert.Equal((false, AltitudeSeparator), Box(A300TypedValues.AltitudeKey, "Altitude", "12.000"));
            Assert.Equal((false, VerticalSpeedSeparator), Box(A300TypedValues.VerticalSpeedKey, "Vertical speed", "-1"));
            Assert.Equal((false, DecisionHeightSeparator), Box(A300TypedValues.MinimumsKey, "Decision height", "2"));
            Assert.Equal((true, ""), Box(A300TypedValues.AltitudeKey, "Altitude", "12000"));
            Assert.Equal((true, ""), Box(A300TypedValues.MinimumsKey, "Decision height", "200"));
            // The panel's boxes, from the number.
            Assert.True(Set(A300TypedValues.AltitudeKey, 12));
            Assert.True(Set(A300TypedValues.VerticalSpeedKey, -1));
            Assert.True(Set(A300TypedValues.MinimumsKey, 1));
            Assert.Empty(_sent);
            Assert.Equal(new[] { AltitudeSeparator, VerticalSpeedSeparator, DecisionHeightSeparator }, _speech.All);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("-1,500", "Vertical speed: type a whole number, without a thousands separator")]
    [InlineData("1.500", "Vertical speed: type a whole number, without a thousands separator")]
    [InlineData("50", "Vertical speed: 0, or 100 to 6000 feet per minute either way")]
    [InlineData("6100", "Vertical speed: 0, or 100 to 6000 feet per minute either way")]
    [InlineData("abc", "Vertical speed: 0, or 100 to 6000 feet per minute either way")]
    public void The_value_box_says_why_it_refused_a_vertical_speed(string typed, string said)
    {
        // A refusal for a reason other than the range is not answered with a range the number is inside.
        var (valid, message) = Box(A300TypedValues.VerticalSpeedKey, "Vertical speed", typed);
        Assert.False(valid);
        Assert.Equal(said, message);
    }

    [Theory]
    [InlineData("-1500")]
    [InlineData("-1500.0")]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("6000")]
    public void The_value_box_takes_a_whole_vertical_speed(string typed) =>
        Assert.Equal((true, ""), Box(A300TypedValues.VerticalSpeedKey, "Vertical speed", typed));

    [Theory]
    [InlineData(A300TypedValues.SpeedKey, "Speed or Mach", "50")]
    [InlineData(A300TypedValues.HeadingKey, "Heading", "400")]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", "60000")]
    [InlineData(IniA300Definition.AllAltimetersKey, "Altimeters", "1200")]
    public void The_value_box_answers_a_number_outside_the_range_with_the_range(string key, string name, string typed)
    {
        // The refusal's own words are the range, so the box says what the panel's box says. The altimeter box's hint
        // also notes what it sets (all three), which a refusal leaves out.
        var (valid, message) = Box(key, name, typed);
        Assert.False(valid);
        Assert.Equal($"{name}: {A300TypedValues.ErrorFor(key)}", message);
    }

    [Theory]
    [InlineData(A300TypedValues.SpeedKey, "Speed or Mach", "Speed or Mach: 100 to 399 knots, or Mach 0.10 to 0.99")]
    [InlineData(A300TypedValues.HeadingKey, "Heading", "Heading: 0 to 360 degrees")]
    [InlineData(A300TypedValues.AltitudeKey, "Altitude", "Altitude: 100 to 49000 feet")]
    [InlineData(A300TypedValues.VerticalSpeedKey, "Vertical speed", "Vertical speed: 0, or 100 to 6000 feet per minute either way")]
    [InlineData(IniA300Definition.AllAltimetersKey, "Altimeters",
        "Altimeters: 28.20 to 31.30 inches, or 955 to 1060 hectopascals; sets captain, first officer and standby")]
    public void A_value_box_answers_text_that_is_no_number_with_the_range_its_plan_takes(string key, string name, string said)
    {
        // The box's hint, shown on it and said here, is its plan's own range: the vertical speed box offered "-6000 to
        // 6000 feet per minute", 1 to 99 among them, which the plan refuses, and said it for "abc" too.
        Assert.Equal((false, said), Box(key, name, "abc"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_value_with_a_fraction_is_refused_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.True(Set(A300TypedValues.VerticalSpeedKey, -1.5));
            Assert.Empty(_sent);
            Assert.Equal(new[] { "Vertical speed: type a whole number, without a thousands separator" }, _speech.All);
            Assert.Equal((false, "Altitude: type a whole number, without a thousands separator"),
                Box(A300TypedValues.AltitudeKey, "Altitude", "12,5"));
            Assert.Equal((true, ""), Box(A300TypedValues.AltitudeKey, "Altitude", "12000"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void An_fcu_lamp_is_consumed_silently_and_shown_on_its_button()
    {
        Assert.True(_def.ProcessSimVarUpdate("A300_FCU_LT_HDGSEL", 1, _speech));
        Assert.Empty(_speech.All);
        _cache["A300_FCU_LT_HDGSEL"] = 1;
        Assert.True(_def.TryDescribeControlState("A300_HDGSEL_BUTTON", out var state));
        Assert.Equal("On", state);
        Assert.Equal(new[] { "A300_FCU_LT_HDGSEL" }, _def.GetVariables()["A300_HDGSEL_BUTTON"].StateVariables);
    }

    [Fact]
    public void The_speed_window_reads_mach_in_mach_mode()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 1;
        Assert.True(_def.TryGetDisplayOverride(A300Readouts.SpeedKey, 0.78, out var text));
        Assert.Equal("Mach 0.78", text);
        _cache[A300FcuState.SpeedMachLightKey] = 0;
        Assert.True(_def.TryGetDisplayOverride(A300Readouts.SpeedKey, 250, out text));
        Assert.Equal("250 knots", text);
    }
}
