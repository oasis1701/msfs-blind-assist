// The Cessna 172's Ctrl+M window lists exactly what the aircraft speaks in the background. Oil
// temperature, EGT, fuel flow and bus voltage are Continuous + IsAnnounced so they ride the batch
// for the status displays, but ProcessSimVarUpdate swallows every update, so a row for one would
// mute nothing (VAR-9). The variables behind the warnings and the autopilot modes are spoken, so
// each keeps its row. Both directions are pinned, on both variants.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class CessnaC172MonitorRowsTests
{
    public static TheoryData<string> Variants => new() { "C172_CLASSIC", "C172_G1000" };

    private static CessnaC172BaseDefinition Create(string code) => code switch
    {
        "C172_CLASSIC" => new CessnaC172ClassicDefinition(),
        _ => new CessnaC172G1000Definition(),
    };

    private static HashSet<string> RowKeys(string code) =>
        MonitorRowBuilder.Build(Create(code).GetVariables()).Select(r => r.Key).ToHashSet();

    [Theory]
    [MemberData(nameof(Variants))]
    public void Silent_readouts_have_no_row(string code)
    {
        var keys = RowKeys(code);

        Assert.DoesNotContain("C172_OIL_TEMP", keys);
        Assert.DoesNotContain("C172_EGT", keys);
        Assert.DoesNotContain("C172_FUEL_FLOW", keys);
        Assert.DoesNotContain("C172_BUS_VOLTAGE", keys);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Every_spoken_variable_keeps_its_row(string code)
    {
        var keys = RowKeys(code);

        foreach (var key in new[]
        {
            "C172_FUEL_LEFT_QTY", "C172_FUEL_RIGHT_QTY", "C172_OIL_PRESSURE", "C172_VACUUM",
            "C172_ENGINE_RPM", "C172_STALL_WARNING",
            "C172_AP_MASTER_STATE", "C172_AP_HDG_STATE", "C172_AP_ALT_STATE", "C172_AP_NAV_STATE",
            "C172_AP_APR_STATE", "C172_AP_BC_STATE", "C172_AP_VS_STATE",
        })
        {
            Assert.Contains(key, keys);
        }
    }

    // The G1000's GFC 700 adds the flight director and FLC to the spoken modes, and its walkaround
    // covers are spoken when they move; the Classic has none of them.
    [Fact]
    public void The_g1000_only_spoken_variables_have_rows_on_the_g1000_alone()
    {
        string[] g1000Only =
        {
            "C172G_AP_FD_STATE", "C172G_AP_FLC_STATE",
            "C172G_COVER_ENGINE", "C172G_COVER_PITOT", "C172G_COVER_STATIC",
        };
        var g1000 = RowKeys("C172_G1000");
        var classic = RowKeys("C172_CLASSIC");

        Assert.All(g1000Only, key => Assert.Contains(key, g1000));
        Assert.All(g1000Only, key => Assert.DoesNotContain(key, classic));
    }
}
