using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix A320's read-back (*_CL) checklists follow the Nov 2021 Airbus A320 normal
/// checklist, line for line the same ids, labels and order as the FlyByWire A32NX (the two
/// profiles differ only where the Fenix prints "signs" for the memo's seat-belt line and where
/// its rudder trim is unmeasured — N_FC_RUDDER_TRIM_DECIMAL exists but its units were never
/// measured — a Reminder here, an Auto line there).
/// </summary>
public class FenixReadbackTests
{
    private static readonly string[] CardGroups =
    {
        "COCKPIT_PREP_CL", "BEFORE_START_CL", "AFTER_START_CL", "TAXI_CL", "LINEUP_CL",
        "APPROACH_CL", "LANDING_CL", "AFTER_LANDING_CL", "PARKING_CL", "SECURING_CL",
    };

    private static readonly (string Group, string[] Ids)[] Card =
    {
        ("COCKPIT_PREP_CL", new[] { "CPC_PINS", "CPC_FUEL", "CPC_SEATBELTS", "CPC_ADIRS", "CPC_BARO" }),
        ("BEFORE_START_CL", new[] { "BSC_PARKBRK", "BSC_TOSPEEDS", "BSC_WINDOWS", "BSC_BEACON" }),
        ("AFTER_START_CL", new[] { "ASC_ANTIICE", "ASC_ECAMSTS", "ASC_PITCH", "ASC_RUDDER" }),
        ("TAXI_CL", new[] { "TXC_FCTEST", "TXC_FLAPS", "TXC_WXR", "TXC_ENGMODE", "TXC_MEMO_AUTOBRK",
                            "TXC_MEMO_SIGNS", "TXC_MEMO_CABIN", "TXC_MEMO_SPLRS", "TXC_MEMO_FLAPS", "TXC_MEMO_TOCFG" }),
        ("LINEUP_CL", new[] { "LUC_RUNWAY", "LUC_TCAS", "LUC_PACKS" }),
        ("APPROACH_CL", new[] { "APC_BARO", "APC_SEATBELTS", "APC_MINIMUM", "APC_AUTOBRAKE", "APC_ENGMODE" }),
        ("LANDING_CL", new[] { "LDC_MEMO_GEAR", "LDC_MEMO_SIGNS", "LDC_MEMO_CABIN", "LDC_MEMO_SPLRS", "LDC_MEMO_FLAPS" }),
        ("AFTER_LANDING_CL", new[] { "ALC_WXR" }),
        ("PARKING_CL", new[] { "PKC_PARKBRK", "PKC_ENGINES", "PKC_WINGLT", "PKC_FUELPUMPS" }),
        ("SECURING_CL", new[] { "SCC_OXY", "SCC_EMEREXIT", "SCC_EFBS", "SCC_BAT" }),
    };

    [Fact]
    public void Readback_groups_are_the_Airbus_card_in_order()
    {
        var groups = FenixChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")).Select(g => g.Id);
        Assert.Equal(CardGroups, groups);
    }

    [Fact]
    public void Each_readback_group_has_exactly_the_card_lines()
    {
        var built = FenixChecklistDefinitions.Build().ToDictionary(g => g.Id);
        foreach (var (group, ids) in Card)
            Assert.Equal(ids, built[group].Items.Select(i => i.Id));
    }

    [Fact]
    public void Readback_groups_are_named_as_on_the_card()
    {
        var built = FenixChecklistDefinitions.Build().ToDictionary(g => g.Id);
        Assert.Equal("Cockpit Preparation Checklist", built["COCKPIT_PREP_CL"].Name);
        Assert.Equal("Before Start Checklist", built["BEFORE_START_CL"].Name);
        Assert.Equal("After Start Checklist", built["AFTER_START_CL"].Name);
        Assert.Equal("Taxi Checklist", built["TAXI_CL"].Name);
        Assert.Equal("Line-up Checklist", built["LINEUP_CL"].Name);
        Assert.Equal("Approach Checklist", built["APPROACH_CL"].Name);
        Assert.Equal("Landing Checklist", built["LANDING_CL"].Name);
        Assert.Equal("After Landing Checklist", built["AFTER_LANDING_CL"].Name);
        Assert.Equal("Parking Checklist", built["PARKING_CL"].Name);
        Assert.Equal("Securing Checklist", built["SECURING_CL"].Name);
    }

    [Fact]
    public void Tree_order_interleaves_action_groups_and_readbacks()
    {
        Assert.Equal(new[]
        {
            "ELEC_POWER_UP", "PREFLIGHT", "COCKPIT_PREP_CL", "BEFORE_START", "BEFORE_START_CL",
            "ENGINE_START", "AFTER_START", "AFTER_START_CL", "TAXI_CL", "BEFORE_TAKEOFF", "LINEUP_CL",
            "AFTER_TAKEOFF", "DESCENT", "APPROACH", "APPROACH_CL", "LANDING_CL", "AFTER_LANDING",
            "AFTER_LANDING_CL", "SHUTDOWN", "PARKING_CL", "SECURE", "SECURING_CL",
        }, FenixChecklistDefinitions.Build().Select(g => g.Id));
    }

    [Fact]
    public void Readbacks_are_action_free_and_have_no_separators()
    {
        foreach (var g in FenixChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")))
            foreach (var i in g.Items)
            {
                Assert.Null(i.CheckAction);
                Assert.NotEqual(ChecklistItemType.Informational, i.Type);
            }
    }

    [Fact]
    public void Memo_lines_use_the_printed_card_wording()
    {
        var items = FenixChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id);
        Assert.Equal("Signs: ON", items["TXC_MEMO_SIGNS"].Label);
        Assert.Equal("Signs: ON", items["LDC_MEMO_SIGNS"].Label);
        Assert.Equal("T.O config: NORMAL", items["TXC_MEMO_TOCFG"].Label);
        Assert.Equal("Landing gear: DOWN", items["LDC_MEMO_GEAR"].Label);
        Assert.Equal(FenixGearConfirmation.DownField, items["LDC_MEMO_GEAR"].StateFieldName);
    }

    [Fact]
    public void Rudder_trim_is_a_reminder_because_the_Fenix_rudder_trim_is_unmeasured()
    {
        var item = FenixChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == "ASC_RUDDER");
        Assert.Equal("Rudder trim: NEUTRAL", item.Label);
        Assert.Equal(ChecklistItemType.CaptainReminder, item.Type);
        Assert.False(item.IsAutoDetectable);
    }

    [Fact]
    public void Live_lines_carry_a_live_value_and_reminders_never_auto_detect()
    {
        var items = FenixChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id);
        foreach (var id in new[] { "CPC_FUEL", "CPC_BARO", "BSC_PARKBRK", "BSC_TOSPEEDS", "ASC_ANTIICE",
                                   "TXC_FLAPS", "TXC_ENGMODE", "LUC_TCAS", "LUC_PACKS", "APC_BARO",
                                   "APC_AUTOBRAKE", "APC_ENGMODE", "LDC_MEMO_FLAPS", "PKC_PARKBRK" })
            Assert.NotNull(items[id].LiveValue);
        foreach (var id in new[] { "CPC_PINS", "BSC_WINDOWS", "ASC_ECAMSTS", "ASC_PITCH", "ASC_RUDDER",
                                   "TXC_FCTEST", "TXC_MEMO_CABIN", "TXC_MEMO_TOCFG", "LUC_RUNWAY",
                                   "APC_MINIMUM", "LDC_MEMO_CABIN", "SCC_EFBS" })
            Assert.False(items[id].IsAutoDetectable, id);
    }

    [Fact]
    public void Live_values_read_nothing_when_the_sim_is_not_connected()
    {
        var state = new FenixStateEvaluator();
        foreach (var item in FenixChecklistDefinitions.Build().SelectMany(g => g.Items).Where(i => i.LiveValue != null))
            Assert.Null(item.ReadLiveValue(state));
    }

    [Fact]
    public void Synthetic_fields_are_unknown_with_no_data()
    {
        var e = new FenixStateEvaluator();
        foreach (var f in new[] { "FO_WXR_ON_AUTO", "FO_WXR_PWS_OFF", "FO_LDG_FLAPS_SET" })
            Assert.True(double.IsNaN(e.GetValue(f)), f);
    }

    [Fact]
    public void Every_auto_line_reads_unknown_with_no_data_so_nothing_ticks_or_reverts()
    {
        // FO_ENGINES_OFF is the one synthetic that reads a value with no data: the form pushes the
        // N2 readings in, and an engine never read is 0 % (that line's own long-standing contract).
        var e = new FenixStateEvaluator();
        foreach (var item in FenixChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL"))
                     .SelectMany(g => g.Items).Where(i => i.IsAutoDetectable && i.StateFieldName != "FO_ENGINES_OFF"))
        {
            Assert.True(double.IsNaN(e.GetValue(item.StateFieldName!)), item.Id);
            foreach (var extra in item.AdditionalStateFields)
                Assert.True(double.IsNaN(e.GetValue(extra)), item.Id + " " + extra);
        }
    }

    [Fact]
    public void Total_fuel_weight_is_registered_on_request_and_polled()
    {
        const string Key = "FUEL TOTAL QUANTITY WEIGHT";
        var vars = new FenixA320Definition().GetVariables();
        Assert.True(vars.TryGetValue(Key, out var def), "the stock total fuel weight must be registered");
        Assert.Equal(Key, def!.Name);
        Assert.Equal("pounds", def.Units);
        Assert.Equal(SimVarType.SimVar, def.Type);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.True(def.ExcludeFromMonitorManager);
        Assert.Contains(Key, new FenixStateEvaluator().OnRequestPollFields);
    }

    [Fact]
    public void Every_field_a_readback_reads_is_registered_and_either_polled_or_continuous()
    {
        // A readback field that nothing delivers would read unknown for the whole flight, so the
        // line would never tick. Synthetic FO_* fields are computed from the others.
        var vars = new FenixA320Definition().GetVariables();
        var polled = new FenixStateEvaluator().OnRequestPollFields;
        var fields = FenixChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")).SelectMany(g => g.Items)
            .SelectMany(i => new[] { i.StateFieldName }.Concat(i.AdditionalStateFields))
            .Where(f => f != null && !f.StartsWith("FO_"))
            .Distinct();
        foreach (var f in fields)
        {
            Assert.True(vars.TryGetValue(f!, out var def), $"{f} is not registered");
            Assert.True((def!.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced) || polled.Contains(f!),
                $"{f} is neither continuous (announced) nor in OnRequestPollFields");
        }
    }

    [Fact]
    public void Every_field_the_live_values_read_is_delivered_to_the_cache()
    {
        var vars = new FenixA320Definition().GetVariables();
        var polled = new FenixStateEvaluator().OnRequestPollFields;
        foreach (var f in new[]
                 {
                     "FUEL TOTAL QUANTITY WEIGHT", "S_OH_GPWS_LDG_FLAP3", "S_FCU_EFIS1_BARO_STD",
                     "S_FCU_EFIS1_BARO_MODE", "N_FCU_EFIS1_BARO_HPA", "N_FCU_EFIS1_BARO_INCH",
                     "FNX2PLD_speedV1", "FNX2PLD_speedVR", "FNX2PLD_speedV2", "N_MISC_PERF_TO_FLEX",
                     "I_MIP_AUTOBRAKE_LO_L", "I_MIP_AUTOBRAKE_MED_L", "I_MIP_AUTOBRAKE_MAX_L",
                     "S_MIP_PARKING_BRAKE", "S_FC_FLAPS", "S_ENG_MODE", "S_XPDR_MODE",
                     "S_OH_PNEUMATIC_PACK_1", "S_OH_PNEUMATIC_PACK_2",
                     "S_OH_PNEUMATIC_ENG1_ANTI_ICE", "S_OH_PNEUMATIC_ENG2_ANTI_ICE", "S_OH_PNEUMATIC_WING_ANTI_ICE",
                 })
        {
            Assert.True(vars.TryGetValue(f, out var def), $"{f} is not registered");
            Assert.True((def!.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced) || polled.Contains(f),
                $"{f} is neither continuous (announced) nor in OnRequestPollFields");
        }
    }
}

/// <summary>
/// The Fenix-specific pieces behind the read-backs: which unit the captain's baro is showing,
/// the stock fuel weight in pounds turned into the card's kilograms, the autobrake read from its
/// three lamps, and the synthetic radar / landing-flap fields. The evaluator reads a live
/// SimConnect cache that cannot be seeded without a sim, so each is a pure function over a
/// reader (the seam <see cref="FenixGearConfirmation"/> uses), run here under the comma-decimal
/// cultures CI does not use.
/// </summary>
public class FenixReadbackValueTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static T InCulture<T>(string name, System.Func<T> f)
    {
        var old = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
            return f();
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }

    private static System.Func<string, double> Reader(Dictionary<string, double> values)
        => f => values.TryGetValue(f, out double v) ? v : double.NaN;

    [Theory, MemberData(nameof(Cultures))]
    public void Baro_reads_standard_when_the_STD_flag_is_set(string culture)
    {
        var read = Reader(new() { ["S_FCU_EFIS1_BARO_STD"] = 1 });
        Assert.Equal("standard", InCulture(culture, () => FenixStateEvaluator.BaroText(read)));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Baro_reads_inHg_with_two_decimals_when_the_EFIS_shows_inHg(string culture)
    {
        // S_FCU_EFIS1_BARO_MODE 0 = inHg; N_FCU_EFIS1_BARO_INCH is the value in inHg (29.92, not 2992).
        var read = Reader(new()
        {
            ["S_FCU_EFIS1_BARO_STD"] = 0, ["S_FCU_EFIS1_BARO_MODE"] = 0,
            ["N_FCU_EFIS1_BARO_INCH"] = 29.921, ["N_FCU_EFIS1_BARO_HPA"] = 1013.2,
        });
        Assert.Equal("QNH 29.92", InCulture(culture, () => FenixStateEvaluator.BaroText(read)));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Baro_reads_whole_hPa_when_the_EFIS_shows_hPa(string culture)
    {
        var read = Reader(new()
        {
            ["S_FCU_EFIS1_BARO_STD"] = 0, ["S_FCU_EFIS1_BARO_MODE"] = 1,
            ["N_FCU_EFIS1_BARO_INCH"] = 29.921, ["N_FCU_EFIS1_BARO_HPA"] = 1013.2,
        });
        Assert.Equal("QNH 1013", InCulture(culture, () => FenixStateEvaluator.BaroText(read)));
    }

    [Fact]
    public void Baro_is_null_whenever_an_input_is_unknown()
    {
        Assert.Null(FenixStateEvaluator.BaroText(Reader(new())));                                  // nothing read
        Assert.Null(FenixStateEvaluator.BaroText(Reader(new() { ["S_FCU_EFIS1_BARO_STD"] = 0 }))); // unit unknown
        Assert.Null(FenixStateEvaluator.BaroText(Reader(new()
            { ["S_FCU_EFIS1_BARO_STD"] = 0, ["S_FCU_EFIS1_BARO_MODE"] = 1 })));                    // value unknown
        Assert.Null(FenixStateEvaluator.BaroText(Reader(new()
            { ["S_FCU_EFIS1_BARO_STD"] = 0, ["S_FCU_EFIS1_BARO_MODE"] = 0, ["N_FCU_EFIS1_BARO_HPA"] = 1013 })));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Fuel_is_the_stock_pounds_spoken_in_kilograms_to_the_nearest_hundred(string culture)
    {
        // 27,000 lb = 12,247 kg -> 12,200; the thousands separator is the invariant comma.
        var read = Reader(new() { ["FUEL TOTAL QUANTITY WEIGHT"] = 27000 });
        Assert.Equal("12,200 kilograms", InCulture(culture, () => FenixStateEvaluator.FuelText(read)));
    }

    [Fact]
    public void Fuel_is_null_when_unknown()
        => Assert.Null(FenixStateEvaluator.FuelText(Reader(new())));

    [Theory]
    [InlineData(0, 0, 1, "max")]
    [InlineData(0, 1, 0, "medium")]
    [InlineData(1, 0, 0, "low")]
    [InlineData(0, 0, 0, "off")]
    public void Autobrake_is_the_lit_lamp(double lo, double med, double max, string expected)
    {
        var read = Reader(new()
        {
            ["I_MIP_AUTOBRAKE_LO_L"] = lo, ["I_MIP_AUTOBRAKE_MED_L"] = med, ["I_MIP_AUTOBRAKE_MAX_L"] = max,
        });
        Assert.Equal(expected, FenixStateEvaluator.AutobrakeText(read));
    }

    [Fact]
    public void Autobrake_is_null_when_a_lamp_is_unknown()
        => Assert.Null(FenixStateEvaluator.AutobrakeText(Reader(new()
            { ["I_MIP_AUTOBRAKE_LO_L"] = 0, ["I_MIP_AUTOBRAKE_MED_L"] = 0 })));

    // S_WR_SYS: 0 = system 1, 1 = OFF, 2 = system 2. S_WR_PRED_WS: 0 = OFF, 1 = AUTO.
    [Theory]
    [InlineData(0, 1, true)]   // system 1, PWS auto
    [InlineData(2, 1, true)]   // system 2, PWS auto
    [InlineData(1, 1, false)]  // radar off
    [InlineData(0, 0, false)]  // PWS off
    [InlineData(2, 0, false)]
    public void Radar_on_and_predictive_windshear_auto(double sys, double pws, bool expected)
        => Assert.Equal(expected, FenixStateEvaluator.WxrOnAuto(sys, pws));

    [Theory]
    [InlineData(1, 0, true)]   // radar off, PWS off
    [InlineData(0, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(2, 1, false)]
    public void Radar_and_predictive_windshear_off(double sys, double pws, bool expected)
        => Assert.Equal(expected, FenixStateEvaluator.WxrPwsOff(sys, pws));

    // Landing memo flaps: FULL (4), or 3 when the GPWS LDG FLAP 3 switch is on.
    [Theory]
    [InlineData(4, 0, true)]
    [InlineData(3, 0, false)]
    [InlineData(3, 1, true)]
    [InlineData(4, 1, false)]
    [InlineData(2, 0, false)]
    [InlineData(0, 1, false)]
    public void Landing_memo_flaps_are_full_or_three_with_the_conf3_switch(double flaps, double conf3, bool expected)
        => Assert.Equal(expected, FenixStateEvaluator.LdgFlapsSet(flaps, conf3));
}
