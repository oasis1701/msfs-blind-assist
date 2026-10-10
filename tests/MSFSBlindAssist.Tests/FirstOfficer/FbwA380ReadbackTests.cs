using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The FBW A380X First Officer's read-back (*_CL) checklists are FlyByWire's A380X ECL normal
/// checklists (NormalProcedures.ts at 255546cb; docs/invariants/first-officer-airbus.md#foa-9), in the
/// ECL's words and order, spoken to one pilot: no "(BOTH)", no memo prefix, every blank answered SET
/// with the value the First Officer reads. No Before Takeoff, After Takeoff, After Landing or Departure
/// Change list, no separators.
/// </summary>
public class FbwA380ReadbackTests
{
    private const ChecklistItemType A = ChecklistItemType.AutoDetectable;
    private const ChecklistItemType R = ChecklistItemType.CaptainReminder;

    private static readonly (string Group, string Name, (string Id, string Label, ChecklistItemType Kind)[] Lines)[] Ecl =
    {
        ("COCKPIT_PREP_CL", "Cockpit Preparation Checklist", new[]
        {
            ("CPC_GEARPINS", "Gear pins and covers: REMOVED", R),
            ("CPC_FUEL", "Fuel quantity: SET", R),
            ("CPC_SEATBELT", "Seat belts: ON", A),
            ("CPC_ALTIMETERS", "Baro reference: SET", R),
        }),
        ("BEFORE_START_CL", "Before Start Checklist", new[]
        {
            ("BSC_PARKBRK", "Parking brake: SET", R),
            ("BSC_TOSPEEDS", "T.O speeds and thrust: SET", R),
            ("BSC_BEACON", "Beacon: ON", A),
        }),
        ("AFTER_START_CL", "After Start Checklist", new[]
        {
            ("ASC_ANTIICE", "Anti-ice: SET", R),
            ("ASC_PITCHTRIM", "Pitch trim: T.O", R),
            ("ASC_RUDDERTRIM", "Rudder trim: NEUTRAL", A),
        }),
        ("TAXI_CL", "Taxi Checklist", new[]
        {
            ("TXC_FCTEST", "Flight controls: CHECKED", R),
            ("TXC_FLAPS", "Flaps setting: SET", R),
            ("TXC_WXR", "Radar: ON", A),
            ("TXC_MEMO_SIGNS", "Seat belts: ON", A),
            ("TXC_MEMO_SPLRS", "Ground spoilers: ARM", A),
            ("TXC_MEMO_FLAPS", "Flaps: T.O", A),
            ("TXC_MEMO_AUTOBRK", "Autobrake: RTO", A),
            ("TXC_MEMO_TOCFG", "T.O config: TEST", R),
        }),
        ("LINEUP_CL", "Line-up Checklist", new[]
        {
            ("LUC_RUNWAY", "T.O runway: SET", R),
            ("LUC_PACKS", "Packs 1 and 2: ON", A),
        }),
        ("APPROACH_CL", "Approach Checklist", new[]
        {
            ("APC_ALTIMETERS", "Baro reference: SET", R),
            ("APC_SEATBELT", "Seat belts: ON", A),
            ("APC_MINIMUMS", "Minimum: SET", R),
            ("APC_AUTOBRAKE", "Autobrake: SET", R),
        }),
        ("LANDING_CL", "Landing Checklist", new[]
        {
            ("LDC_SEATBELT", "Seat belts: ON", A),
            ("LDC_GEAR", "Landing gear: DOWN", A),
            ("LDC_SPOILERS", "Ground spoilers: ARM", A),
            ("LDC_FLAPS", "Flaps: LDG", A),
        }),
        ("PARKING_CL", "Parking Checklist", new[]
        {
            ("PKC_PARKBRK", "Parking brake or chocks: SET", R),
            ("PKC_ENGINES", "Engines: OFF", A),
            ("PKC_WINGLT", "Wing lights: OFF", A),
            ("PKC_FUELPUMPS", "Fuel pumps: OFF", A),
        }),
        ("SECURING_CL", "Securing the Aircraft Checklist", new[]
        {
            ("SCC_OXY", "Oxygen: OFF", A),
            ("SCC_EMEREXIT", "Emergency exit light: OFF", A),
            ("SCC_EFB", "EFBs: OFF", R),
            ("SCC_BAT", "Batteries: OFF", A),
        }),
    };

    private static readonly string[] NoMore = Array.Empty<string>();

    // The 20 A380 fuel pumps (8 feed, 12 transfer) as the Parking flow switches them off: the first is
    // the line's own field, these are its other 19. Written out here, not read from the code under test,
    // so the shared array every pump line uses is pinned by an independent list.
    private const string FirstFuelPump = "FUELPUMP_FEEDTK1_MAIN";

    private static readonly string[] OtherFuelPumps =
    {
        "FUELPUMP_FEEDTK1_STBY", "FUELPUMP_FEEDTK2_MAIN", "FUELPUMP_FEEDTK2_STBY",
        "FUELPUMP_FEEDTK3_MAIN", "FUELPUMP_FEEDTK3_STBY", "FUELPUMP_FEEDTK4_MAIN", "FUELPUMP_FEEDTK4_STBY",
        "FUELPUMP_OUTR_L", "FUELPUMP_MID_L_FWD", "FUELPUMP_MID_L_AFT",
        "FUELPUMP_INR_L_FWD", "FUELPUMP_INR_L_AFT",
        "FUELPUMP_OUTR_R", "FUELPUMP_MID_R_FWD", "FUELPUMP_MID_R_AFT",
        "FUELPUMP_INR_R_FWD", "FUELPUMP_INR_R_AFT",
        "FUELPUMP_TRIM_L", "FUELPUMP_TRIM_R",
    };

    // Each self-ticking line: its field, its other fields, a value that ticks it and one that does not.
    private static readonly (string Id, string Field, string[] More, double On, double Off)[] SelfTicking =
    {
        ("CPC_SEATBELT", "SEATBELT_SIGN_LIGHT", NoMore, 1, 0),
        ("BSC_BEACON", "LIGHT_BEACON", NoMore, 1, 0),
        ("ASC_RUDDERTRIM", "FO_RUDDER_TRIM_NEUTRAL", NoMore, 1, 0),
        ("TXC_WXR", "XMLVAR_A320_WeatherRadar_Sys", NoMore, 1, 0),
        ("TXC_MEMO_SIGNS", "SEATBELT_SIGN_LIGHT", NoMore, 1, 0),
        ("TXC_MEMO_SPLRS", "A32NX_SPOILERS_ARMED", NoMore, 1, 0),
        ("TXC_MEMO_FLAPS", "A32NX_FLAPS_HANDLE_INDEX", NoMore, 2, 0),
        ("TXC_MEMO_AUTOBRK", "A32NX_AUTOBRAKES_RTO_ARMED", NoMore, 1, 0),
        ("LUC_PACKS", "A32NX_OVHD_COND_PACK_1_PB_IS_ON", new[] { "A32NX_OVHD_COND_PACK_2_PB_IS_ON" }, 1, 0),
        ("APC_SEATBELT", "SEATBELT_SIGN_LIGHT", NoMore, 1, 0),
        ("LDC_SEATBELT", "SEATBELT_SIGN_LIGHT", NoMore, 1, 0),
        ("LDC_GEAR", "FO_GEAR_DOWN", NoMore, 1, 0),
        ("LDC_SPOILERS", "A32NX_SPOILERS_ARMED", NoMore, 1, 0),
        ("LDC_FLAPS", "FO_LDG_FLAPS_SET", NoMore, 1, 0),
        ("PKC_ENGINES", "FO_ENGINES_OFF", NoMore, 1, 0),
        ("PKC_WINGLT", "LIGHT_WING", NoMore, 0, 1),
        ("PKC_FUELPUMPS", FirstFuelPump, OtherFuelPumps, 0, 1),
        ("SCC_OXY", "PUSH_OVHD_OXYGEN_CREW", NoMore, 1, 0),
        ("SCC_EMEREXIT", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", NoMore, 2, 1),
        ("SCC_BAT", "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
            new[] { "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO" },
            0, 1),
    };

    // The lines whose ECL answer is a blank: they say SET and speak what the First Officer reads.
    private static readonly string[] SpokenLines =
    {
        "CPC_FUEL", "CPC_ALTIMETERS", "BSC_PARKBRK", "BSC_TOSPEEDS", "ASC_ANTIICE", "TXC_FLAPS",
        "APC_ALTIMETERS", "APC_MINIMUMS", "APC_AUTOBRAKE", "PKC_PARKBRK",
    };

    private static List<ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator>> Readbacks() =>
        FbwA380ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal)).ToList();

    private static Dictionary<string, ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>> ReadbackItems() =>
        Readbacks().SelectMany(g => g.Items).ToDictionary(i => i.Id);

    // An FBW ARINC 429 word: the SSM in bits 32-33, the value's float bits in the low 32. The one packer the
    // FCU tests already use, not a pasted copy of it.
    private static double Word(uint ssm, float value) => FcuValuePhrasesTests.Word(ssm, value);

    [Fact]
    public void Readback_groups_are_the_ECL_lists_in_order()
    {
        Assert.Equal(Ecl.Select(l => l.Group), Readbacks().Select(g => g.Id));
        Assert.Equal(Ecl.Select(l => l.Name), Readbacks().Select(g => g.Name));
    }

    [Fact]
    public void Each_list_has_exactly_the_ECL_lines_labels_and_kinds()
    {
        var built = Readbacks().ToDictionary(g => g.Id);
        foreach (var (group, _, lines) in Ecl)
        {
            var items = built[group].Items;
            Assert.Equal(lines.Select(l => l.Id), items.Select(i => i.Id));
            Assert.Equal(lines.Select(l => l.Label), items.Select(i => i.Label));
            Assert.Equal(lines.Select(l => l.Kind), items.Select(i => i.Type));
        }
    }

    [Fact]
    public void Tree_order_interleaves_action_groups_and_readbacks()
    {
        Assert.Equal(new[]
        {
            "COCKPIT_PREP", "COCKPIT_PREP_CL", "BEFORE_START", "BEFORE_START_CL", "ENGINE_START",
            "AFTER_START", "AFTER_START_CL", "TAXI", "TAXI_CL", "LINEUP", "LINEUP_CL", "AFTER_TAKEOFF",
            "APPROACH", "APPROACH_CL", "LANDING", "LANDING_CL", "AFTER_LANDING", "PARKING", "PARKING_CL",
            "SECURE", "SECURING_CL",
        }, FbwA380ChecklistDefinitions.Build().Select(g => g.Id));
    }

    [Fact]
    public void Readbacks_are_action_free_and_have_no_separators()
    {
        foreach (var item in Readbacks().SelectMany(g => g.Items))
        {
            Assert.Null(item.CheckAction);
            Assert.NotEqual(ChecklistItemType.Informational, item.Type);
            Assert.Equal(item.Type == A, item.IsAutoDetectable);
        }
    }

    [Fact]
    public void Self_ticking_lines_read_the_verified_fields()
    {
        var items = ReadbackItems();
        foreach (var (id, field, more, on, off) in SelfTicking)
        {
            var item = items[id];
            Assert.True(field == item.StateFieldName, $"{id} reads {item.StateFieldName}, expected {field}");
            Assert.Equal(more, item.AdditionalStateFields.ToArray());
            Assert.True(item.EvaluateState(on), $"{id} does not tick at {on}");
            Assert.False(item.EvaluateState(off), $"{id} ticks at {off}");
            Assert.Equal(RevertBehavior.RevertToState, item.RevertBehavior);
        }
        // "Flaps: T.O" is a takeoff position, 1-3, and FULL is not.
        var flaps = items["TXC_MEMO_FLAPS"];
        Assert.True(flaps.EvaluateState(1));
        Assert.True(flaps.EvaluateState(3));
        Assert.False(flaps.EvaluateState(4));
        // Every A line is in the table.
        Assert.Equal(Ecl.SelectMany(l => l.Lines).Where(l => l.Kind == A).Select(l => l.Id).OrderBy(x => x, StringComparer.Ordinal),
            SelfTicking.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>The Parking line, the Before Start step and the Parking step all mean the same 20 pumps,
    /// from one shared list (R10): a pump added to or dropped from one cannot drift from the others.</summary>
    [Fact]
    public void The_three_fuel_pump_lines_read_the_same_twenty_pumps()
    {
        var items = FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id);
        var expected = new[] { FirstFuelPump }.Concat(OtherFuelPumps).ToArray();
        Assert.Equal(20, expected.Distinct().Count());
        foreach (var id in new[] { "BS_FUELPUMPS", "PK_FUELPUMPS_OFF", "PKC_FUELPUMPS" })
            Assert.Equal(expected, new[] { items[id].StateFieldName! }.Concat(items[id].AdditionalStateFields).ToArray());
    }

    /// <summary>The flow steps write <see cref="FbwA380ChecklistDefinitions.FuelPumps"/>, the list the lines
    /// read; this pins what they write to the test's own list of 20, each at its target, so a step that
    /// stops writing that list, or writes a pump twice or to the wrong value, fails here (R10).</summary>
    [Fact]
    public void The_flows_switch_the_same_twenty_pumps_the_lines_read()
    {
        var steps = FbwA380FlowDefinitions.Build().SelectMany(f => f.Steps).ToDictionary(s => s.Id);
        var expected = new[] { FirstFuelPump }.Concat(OtherFuelPumps).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        foreach (var (id, target) in new[] { ("BS_FUELPUMPS", 1), ("PK_FUELPUMPS_OFF", 0) })
        {
            var actions = steps[id].MultiActions;
            Assert.Equal(expected, actions.Select(a => a.EventName).OrderBy(p => p, StringComparer.Ordinal).ToArray());
            Assert.All(actions, a => Assert.Equal(target, a.TargetValue));
        }
    }

    [Fact]
    public void Blank_lines_carry_a_spoken_value_and_no_other_line_does()
    {
        foreach (var item in Readbacks().SelectMany(g => g.Items))
            Assert.True(SpokenLines.Contains(item.Id) == (item.LiveValue != null),
                $"{item.Id}: LiveValue is {(item.LiveValue == null ? "missing" : "unexpected")}");
    }

    // The spoken values run under a comma-decimal culture too: CI runs en-US and would never catch a
    // "12.400 kilograms" or "QNH 29,92" (CLAUDE.md, "Check the whole of what you touched").
    [Theory, MemberData(nameof(FbwA380StateEvaluatorTests.Cultures), MemberType = typeof(FbwA380StateEvaluatorTests))]
    public void Blank_lines_speak_what_the_first_officer_reads(string culture)
        => FbwA380StateEvaluatorTests.InCulture(culture, () =>
        {
            var e = new FbwA380StateEvaluator();
            e.SetSimConnect(SeededSimConnectCache.With(
                ("A32NX_TOTAL_FUEL_QUANTITY", 12_380), ("A32NX_EFB_USING_METRIC_UNIT", 1),
                ("A32NX_FCU_LEFT_EIS_BARO_IS_STD", 0), ("A32NX_FCU_LEFT_EIS_BARO_HPA", Word(0b11, 1013.2f)),
                ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0),
                ("A32NX_PARK_BRAKE_LEVER_POS", 1),
                ("PFD_V1", 142), ("PFD_VR", 145), ("PFD_V2", 149), ("A32NX_AIRLINER_TO_FLEX_TEMP", 55),
                ("ENG_ANTI_ICE:1", 0), ("ENG_ANTI_ICE:2", 0), ("ENG_ANTI_ICE:3", 0), ("ENG_ANTI_ICE:4", 0),
                ("WING_ANTI_ICE_OVHD", 0),
                ("A32NX_FLAPS_HANDLE_INDEX", 2),
                ("AIRLINER_MINIMUM_DESCENT_ALTITUDE", 940), ("AIRLINER_DECISION_HEIGHT", -1),
                ("A32NX_AUTOBRAKES_ARMED_MODE", 3)));
            var items = ReadbackItems();
            Assert.Equal("12,400 kilograms", items["CPC_FUEL"].ReadLiveValue(e));
            Assert.Equal("QNH 1013", items["CPC_ALTIMETERS"].ReadLiveValue(e));
            Assert.Equal("on", items["BSC_PARKBRK"].ReadLiveValue(e));
            Assert.Equal("V1 142, VR 145, V2 149, flex 55", items["BSC_TOSPEEDS"].ReadLiveValue(e));
            Assert.Equal("engine anti-ice off, wing anti-ice off", items["ASC_ANTIICE"].ReadLiveValue(e));
            Assert.Equal("CONF 2", items["TXC_FLAPS"].ReadLiveValue(e));
            Assert.Equal("QNH 1013", items["APC_ALTIMETERS"].ReadLiveValue(e));
            Assert.Equal("baro 940 feet", items["APC_MINIMUMS"].ReadLiveValue(e));
            Assert.Equal("L2", items["APC_AUTOBRAKE"].ReadLiveValue(e));
            Assert.Equal("on", items["PKC_PARKBRK"].ReadLiveValue(e));
        });

    [Fact]
    public void Live_values_are_null_with_no_data()
    {
        var e = new FbwA380StateEvaluator();
        foreach (var item in Readbacks().SelectMany(g => g.Items))
            Assert.Null(item.ReadLiveValue(e));
    }

    // The ECL abbreviations the lines spell out (GND SPLRs, AUTO BRK, T.O RWY, EMER EXIT, BARO REF, "&").
    // "T.O" and "LDG" stay: they are the ECL's own words for the settings ("Flaps: T.O", "Flaps: LDG"),
    // as the A320 card keeps "Flaps: T.O".
    private static readonly Regex[] SpelledOut =
    {
        new(@"\bGND\b"), new(@"\bSPLRs?\b"), new(@"\bBRK\b"), new(@"\bRWY\b"), new(@"\bEMER\b"), new(@"\bREF\b"), new("&"),
    };

    [Fact]
    public void Readback_wording_is_the_ECL_spoken_to_one_pilot()
    {
        var bad = new List<string>();
        foreach (var g in Readbacks())
            foreach (var i in g.Items)
            {
                string l = i.Label;
                if (l.Contains("(both)", StringComparison.OrdinalIgnoreCase)) bad.Add($"{i.Id}: two-pilot marker");
                if (l.Contains("READBACK", StringComparison.OrdinalIgnoreCase)) bad.Add($"{i.Id}: READBACK");
                if (Regex.IsMatch(l, @"^(Takeoff|T\.O|Landing|LDG) memo\b", RegexOptions.IgnoreCase)) bad.Add($"{i.Id}: memo prefix");
                if (string.Equals(l, g.Name, StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(l, @"\bchecklist\b", RegexOptions.IgnoreCase)) bad.Add($"{i.Id}: list-name line");
                if (l.Contains('—')) bad.Add($"{i.Id}: separator");
                foreach (var a in SpelledOut)
                    if (a.IsMatch(l)) bad.Add($"{i.Id}: abbreviation {a}");
            }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    [Fact]
    public void No_line_or_step_carries_two_pilot_or_memo_markup_or_ANN()
    {
        var labels = FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => (Where: $"item {i.Id}", i.Label))
            .Concat(FbwA380FlowDefinitions.Build().SelectMany(f => f.Steps).Select(s => (Where: $"step {s.Id}", s.Label)));
        var bad = labels
            .Where(l => l.Label.Contains("(both)", StringComparison.OrdinalIgnoreCase)
                     || l.Label.Contains("READBACK", StringComparison.OrdinalIgnoreCase)
                     || Regex.IsMatch(l.Label, @"^(Takeoff|T\.O|Landing|LDG) memo\b", RegexOptions.IgnoreCase)
                     || Regex.IsMatch(l.Label, @"\bANN\b"))
            .Select(l => $"{l.Where}: \"{l.Label}\"")
            .ToList();
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }
}
