using System;
using System.Globalization;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// What the FBW A380X read-backs and read-only checks read (FOA-9): the evaluator's synthetic fields and
/// its three spoken texts. Every field is NaN while any input it needs is unknown.
/// </summary>
public class FbwA380StateEvaluatorTests
{
    // An FBW ARINC 429 word: the SSM in bits 32-33, the value's float bits in the low 32. The one packer the
    // FCU tests already use, not a pasted copy of it.
    private static double Word(uint ssm, float value) => FcuValuePhrasesTests.Word(ssm, value);

    private static FbwA380StateEvaluator With(params (string Key, double Value)[] values)
    {
        var e = new FbwA380StateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(values));
        return e;
    }

    // The three texts are spoken, so they run under a comma-decimal culture too: CI runs en-US and would
    // never catch a "12.400 kilograms" or "QNH 29,92" (CLAUDE.md, "Check the whole of what you touched").
    // FbwA380ReadbackTests runs the checklist lines' spoken values through these two as well.
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    internal static void InCulture(string name, Action body)
    {
        var old = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
            body();
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }

    [Fact]
    public void Rudder_trim_is_neutral_within_0_35_degrees_either_way()
    {
        Assert.Equal(1.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 0.0f), double.NaN));
        Assert.Equal(1.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 0.3f), double.NaN));
        Assert.Equal(1.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, -0.3f), double.NaN));
        Assert.Equal(0.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 0.5f), double.NaN));
        Assert.Equal(0.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, -2.0f), double.NaN));
        // The threshold itself: just inside 0.35 is neutral, just outside is not.
        Assert.Equal(1.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 0.34f), double.NaN));
        Assert.Equal(0.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 0.36f), double.NaN));
    }

    [Fact]
    public void Rudder_trim_reads_SEC_1_before_SEC_3_when_both_carry_data()
    {
        // Both words are good: SEC 1 decides, whichever way the two disagree.
        Assert.Equal(1.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 0.0f), Word(0b11, 3.0f)));
        Assert.Equal(0.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 3.0f), Word(0b11, 0.0f)));
    }

    [Fact]
    public void The_rudder_trim_field_reads_both_SEC_words_in_that_order()
    {
        // The synthetic's own wiring: SEC 1 at 2 degrees (not neutral) wins over SEC 3 at 0 (neutral).
        var e = With(("A32NX_SEC_1_RUDDER_ACTUAL_POSITION", Word(0b11, 2.0f)),
                     ("A32NX_SEC_3_RUDDER_ACTUAL_POSITION", Word(0b11, 0.0f)));
        Assert.Equal(0.0, e.GetValue("FO_RUDDER_TRIM_NEUTRAL"));
    }

    [Fact]
    public void Rudder_trim_falls_back_to_SEC_3_and_is_unknown_without_data()
    {
        // SEC 1 failed, never written or unread: SEC 3 decides, as FlyByWire's warning system does.
        Assert.Equal(1.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b00, 5.0f), Word(0b11, 0.1f)));
        Assert.Equal(0.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(0.0, Word(0b11, 3.0f)));
        Assert.Equal(1.0, FbwA380StateEvaluator.DecodeRudderTrimNeutral(double.NaN, Word(0b10, 0.0f)));
        // Neither word carries a trim: unknown, never "neutral".
        Assert.True(double.IsNaN(FbwA380StateEvaluator.DecodeRudderTrimNeutral(double.NaN, double.NaN)));
        Assert.True(double.IsNaN(FbwA380StateEvaluator.DecodeRudderTrimNeutral(Word(0b01, 0.0f), 0.0)));
    }

    [Theory]
    [InlineData(4, 0, 1)]   // FULL, CONF 3 not selected
    [InlineData(3, 0, 0)]
    [InlineData(3, 1, 1)]   // 3, CONF 3 selected on the MFD PERF APPR page
    [InlineData(4, 1, 0)]
    public void Landing_flaps_are_FULL_or_3_with_CONF_3(double flaps, double conf3, double expected)
        => Assert.Equal(expected,
            With(("A32NX_FLAPS_HANDLE_INDEX", flaps), ("A380X_FM_LANDING_CONF3", conf3)).GetValue("FO_LDG_FLAPS_SET"));

    [Fact]
    public void Landing_flaps_are_unknown_while_CONF_3_is_unread()
        => Assert.True(double.IsNaN(With(("A32NX_FLAPS_HANDLE_INDEX", 4)).GetValue("FO_LDG_FLAPS_SET")));

    [Theory]
    [InlineData(1, 1, 2, 1)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 1, 3, 1)]
    [InlineData(1, 1, 0, 0)]   // flaps up
    [InlineData(1, 1, 4, 0)]   // FULL is no takeoff setting
    [InlineData(1, 0, 2, 0)]   // ground spoilers not armed
    [InlineData(0, 1, 2, 0)]   // seat-belt sign off
    public void Takeoff_memo_needs_seat_belts_ground_spoilers_and_takeoff_flaps(
        double belts, double spoilers, double flaps, double expected)
        => Assert.Equal(expected, With(("SEATBELT_SIGN_LIGHT", belts), ("A32NX_SPOILERS_ARMED", spoilers),
            ("A32NX_FLAPS_HANDLE_INDEX", flaps)).GetValue("FO_TAXI_MEMO_OK"));

    [Theory]
    [InlineData(1, 4, 0, 1)]
    [InlineData(1, 3, 1, 1)]
    [InlineData(1, 3, 0, 0)]
    [InlineData(0, 4, 0, 0)]
    public void Landing_memo_needs_seat_belts_and_the_landing_flaps(
        double belts, double flaps, double conf3, double expected)
        => Assert.Equal(expected, With(("SEATBELT_SIGN_LIGHT", belts), ("A32NX_FLAPS_HANDLE_INDEX", flaps),
            ("A380X_FM_LANDING_CONF3", conf3)).GetValue("FO_LDG_MEMO_OK"));

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    public void The_packs_check_needs_both_packs_on(double pack1, double pack2, double expected)
        => Assert.Equal(expected, With(("A32NX_OVHD_COND_PACK_1_PB_IS_ON", pack1),
            ("A32NX_OVHD_COND_PACK_2_PB_IS_ON", pack2)).GetValue("FO_PACKS_ON"));

    [Fact]
    public void Engines_are_off_when_all_four_fuel_valves_are_closed_and_unknown_while_one_is_unread()
    {
        // All four closed: off.
        Assert.Equal(1.0, With(("ENG_VALVE_SWITCH:1", 0), ("ENG_VALVE_SWITCH:2", 0),
            ("ENG_VALVE_SWITCH:3", 0), ("ENG_VALVE_SWITCH:4", 0)).GetValue("FO_ENGINES_OFF"));
        // One open: an engine is still fed.
        Assert.Equal(0.0, With(("ENG_VALVE_SWITCH:1", 0), ("ENG_VALVE_SWITCH:2", 0),
            ("ENG_VALVE_SWITCH:3", 1), ("ENG_VALVE_SWITCH:4", 0)).GetValue("FO_ENGINES_OFF"));
        // One unread: a cold cache is no answer, never "engines running" or "off".
        Assert.True(double.IsNaN(With(("ENG_VALVE_SWITCH:1", 0), ("ENG_VALVE_SWITCH:2", 0),
            ("ENG_VALVE_SWITCH:4", 0)).GetValue("FO_ENGINES_OFF")));
    }

    [Fact]
    public void Synthetic_fields_are_unknown_with_no_data()
    {
        var e = new FbwA380StateEvaluator();
        foreach (var f in new[] { "FO_ENGINES_OFF", "FO_RUDDER_TRIM_NEUTRAL", "FO_LDG_FLAPS_SET", "FO_TAXI_MEMO_OK",
                                  "FO_LDG_MEMO_OK", "FO_PACKS_ON", FbwA380GearConfirmation.DownField })
            Assert.True(double.IsNaN(e.GetValue(f)), f);
        // One unknown input is enough.
        Assert.True(double.IsNaN(With(("SEATBELT_SIGN_LIGHT", 1), ("A32NX_SPOILERS_ARMED", 1)).GetValue("FO_TAXI_MEMO_OK")));
        Assert.True(double.IsNaN(With(("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1)).GetValue("FO_PACKS_ON")));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Fuel_baro_and_minimum_texts_read_the_A380_sources(string culture) => InCulture(culture, () =>
    {
        var e = With(("A32NX_TOTAL_FUEL_QUANTITY", 12_380), ("A32NX_EFB_USING_METRIC_UNIT", 1),
            ("A32NX_FCU_LEFT_EIS_BARO_IS_STD", 0), ("A32NX_FCU_LEFT_EIS_BARO_HPA", Word(0b11, 1013.2f)),
            ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0),
            ("AIRLINER_MINIMUM_DESCENT_ALTITUDE", 940), ("AIRLINER_DECISION_HEIGHT", -1));
        Assert.Equal("12,400 kilograms", e.FuelText());
        Assert.Equal("QNH 1013", e.BaroText());
        Assert.Equal("baro 940 feet", e.MinimumText());

        Assert.Equal("27,300 pounds",
            With(("A32NX_TOTAL_FUEL_QUANTITY", 12_380), ("A32NX_EFB_USING_METRIC_UNIT", 0)).FuelText());
        Assert.Equal("standard", With(("A32NX_FCU_LEFT_EIS_BARO_IS_STD", 1)).BaroText());
        Assert.Equal("QNH 29.92", With(("A32NX_FCU_LEFT_EIS_BARO_IS_STD", 0),
            ("A32NX_FCU_LEFT_EIS_BARO_HPA", Word(0b11, 1013.2f)), ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 1)).BaroText());
    });

    [Fact]
    public void Texts_are_null_with_no_data()
    {
        var e = new FbwA380StateEvaluator();
        Assert.Null(e.FuelText());
        Assert.Null(e.BaroText());
        Assert.Null(e.MinimumText());
    }

    /// <summary>The documented "null when the fuel or the unit is unknown": one unread input is enough, so
    /// a half-read pair never speaks a number in the wrong unit or a minimum with one half missing.</summary>
    [Fact]
    public void Texts_are_null_while_any_one_input_is_unread()
    {
        Assert.Null(With(("A32NX_TOTAL_FUEL_QUANTITY", 12_380)).FuelText());          // unit unread
        Assert.Null(With(("A32NX_EFB_USING_METRIC_UNIT", 1)).FuelText());              // fuel unread
        Assert.Null(With(("A32NX_FCU_LEFT_EIS_BARO_IS_STD", 0),
            ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0)).BaroText());                          // QNH word unread
        Assert.Null(With(("AIRLINER_MINIMUM_DESCENT_ALTITUDE", 940)).MinimumText());   // DH unread
        Assert.Null(With(("AIRLINER_DECISION_HEIGHT", -1)).MinimumText());             // MDA unread
    }

    [Fact]
    public void The_evaluator_polls_every_source_the_readbacks_and_checks_read()
    {
        var poll = new FbwA380StateEvaluator().OnRequestPollFields;
        foreach (var f in new[]
                 {
                     "A32NX_SEC_1_RUDDER_ACTUAL_POSITION", "A32NX_SEC_3_RUDDER_ACTUAL_POSITION",
                     "A32NX_GEAR_HANDLE_POSITION", "A32NX_GEAR_CENTER_POSITION", "A32NX_GEAR_LEFT_POSITION",
                     "A32NX_GEAR_RIGHT_POSITION", "A32NX_TOTAL_FUEL_QUANTITY", "A32NX_EFB_USING_METRIC_UNIT",
                     "PFD_V1", "PFD_VR", "PFD_V2", "A32NX_AIRLINER_TO_FLEX_TEMP",
                     "AIRLINER_MINIMUM_DESCENT_ALTITUDE", "AIRLINER_DECISION_HEIGHT",
                     "A32NX_FCU_LEFT_EIS_BARO_IS_STD", "A32NX_FCU_LEFT_EIS_BARO_HPA", "A32NX_FCU_EFIS_L_BARO_IS_INHG",
                     "A32NX_AUTOBRAKES_ARMED_MODE", "A380X_FM_LANDING_CONF3", "A32NX_FLAPS_HANDLE_INDEX",
                     "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4", "WING_ANTI_ICE_OVHD",
                     "SEATBELT_SIGN_LIGHT", "A32NX_SPOILERS_ARMED", "A32NX_PARK_BRAKE_LEVER_POS",
                     "A32NX_OVHD_COND_PACK_1_PB_IS_ON", "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
                 })
            Assert.Contains(f, poll);
    }
}
