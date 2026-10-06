using System;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A32NX First Officer's read-back (*_CL) checklists are the Nov 2021 Airbus A320 normal
/// checklist (docs/invariants/first-officer-airbus.md#foa-8): ten lists, the memo lines
/// expanded, no line markers, no Before/After Takeoff list. The Fenix keeps the same ids and
/// labels (A320FamilyParityTests); the A330 duplicates this profile (HwA330ParityTests).
/// </summary>
public class FbwA320ReadbackTests
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
        var groups = FbwA320ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")).Select(g => g.Id);
        Assert.Equal(CardGroups, groups);
    }

    [Fact]
    public void Each_readback_group_has_exactly_the_card_lines()
    {
        var built = FbwA320ChecklistDefinitions.Build().ToDictionary(g => g.Id);
        foreach (var (group, ids) in Card)
            Assert.Equal(ids, built[group].Items.Select(i => i.Id));
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
        }, FbwA320ChecklistDefinitions.Build().Select(g => g.Id));
    }

    [Fact]
    public void Readbacks_are_action_free_and_have_no_separators()
    {
        foreach (var g in FbwA320ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")))
            foreach (var i in g.Items)
            {
                Assert.Null(i.CheckAction);
                Assert.NotEqual(ChecklistItemType.Informational, i.Type);
            }
    }

    [Fact]
    public void Memo_lines_use_the_A32NX_ECAM_wording()
    {
        var items = FbwA320ChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id);
        Assert.Equal("Takeoff memo, seat belts: ON", items["TXC_MEMO_SIGNS"].Label);
        Assert.Equal("Landing memo, seat belts: ON", items["LDC_MEMO_SIGNS"].Label);
        Assert.Equal("Takeoff memo, T.O config: NORMAL", items["TXC_MEMO_TOCFG"].Label);
        Assert.Equal(FbwA320GearConfirmation.DownField, items["LDC_MEMO_GEAR"].StateFieldName);
    }

    [Fact]
    public void Live_lines_carry_a_live_value_and_reminders_never_auto_detect()
    {
        var items = FbwA320ChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id);
        foreach (var id in new[] { "CPC_FUEL", "CPC_BARO", "BSC_PARKBRK", "BSC_TOSPEEDS", "ASC_ANTIICE",
                                   "TXC_FLAPS", "TXC_ENGMODE", "LUC_PACKS", "APC_BARO", "APC_AUTOBRAKE",
                                   "APC_ENGMODE", "LDC_MEMO_FLAPS", "PKC_PARKBRK" })
            Assert.NotNull(items[id].LiveValue);
        foreach (var id in new[] { "CPC_PINS", "BSC_WINDOWS", "ASC_ECAMSTS", "ASC_PITCH", "TXC_FCTEST",
                                   "TXC_MEMO_CABIN", "TXC_MEMO_TOCFG", "LUC_RUNWAY", "APC_MINIMUM",
                                   "LDC_MEMO_CABIN", "SCC_EFBS" })
            Assert.False(items[id].IsAutoDetectable, id);
    }

    [Fact]
    public void Synthetic_fields_are_unknown_with_no_data()
    {
        var e = new FbwA320StateEvaluator();
        foreach (var f in new[] { "FO_WXR_ON_AUTO", "FO_WXR_PWS_OFF", "FO_SIGNS_ON", "FO_LDG_FLAPS_SET", "FO_RUDDER_TRIM_NEUTRAL" })
            Assert.True(double.IsNaN(e.GetValue(f)), f);
    }

    [Fact]
    public void Live_values_are_null_with_no_data()
    {
        var e = new FbwA320StateEvaluator();
        Assert.Null(e.BaroText());
        Assert.Null(e.FuelText());
        foreach (var item in FbwA320ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")).SelectMany(g => g.Items))
            Assert.Null(item.ReadLiveValue(e));
    }

    // A32NX_FAC_1_RUDDER_TRIM_POS is an ARINC429 degrees word: SSM in bits 32-33, a float in the low 32.
    private static double Word(uint ssm, float degrees) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits(degrees));

    [Fact]
    public void Rudder_trim_is_neutral_under_a_tenth_of_a_degree()
    {
        Assert.Equal(1.0, FbwA320StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 0.0f)));
        Assert.Equal(1.0, FbwA320StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, -0.09f)));
        Assert.Equal(0.0, FbwA320StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, 0.5f)));
        Assert.Equal(0.0, FbwA320StateEvaluator.DecodeRudderTrimNeutral(Word(0b11, -2.0f)));
    }

    [Fact]
    public void Rudder_trim_without_data_is_unknown_not_neutral()
    {
        Assert.True(double.IsNaN(FbwA320StateEvaluator.DecodeRudderTrimNeutral(double.NaN)));
        // Failure warning / no computed data: the word carries no trim, so it must not tick "NEUTRAL".
        Assert.True(double.IsNaN(FbwA320StateEvaluator.DecodeRudderTrimNeutral(Word(0b00, 0.0f))));
        Assert.True(double.IsNaN(FbwA320StateEvaluator.DecodeRudderTrimNeutral(0.0)));
        Assert.True(double.IsNaN(FbwA320StateEvaluator.DecodeRudderTrimNeutral(Word(0b01, 0.0f))));
    }
}
