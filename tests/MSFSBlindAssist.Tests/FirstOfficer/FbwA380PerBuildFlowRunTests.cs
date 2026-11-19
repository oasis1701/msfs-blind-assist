using System.Threading.Tasks;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Cockpit Preparation flow's build-dependent steps (baro unit, flight directors) run on a real
/// FlowManager against each build's OWN variables (<see cref="FbwA380FlowRun"/>): seeded with the
/// aircraft already set up on that build, both steps say "Already set" and their lines tick; seeded with
/// the OTHER build's variables only, the build in the sim reads nothing it knows, so neither step may
/// pass as set (the executor has no sim, so the write that follows fails and both lines stay open).
/// </summary>
public class FbwA380PerBuildFlowRunTests
{
    private static readonly string[] Steps = { "CP_BARO", "CP_FD" };

    private static readonly (string Key, double Value)[] Fs2020SetUp =
    {
        ("XMLVAR_Baro_Selector_HPA_1", 1),          // hPa
        (MSFSBlindAssist.Aircraft.A380BuildNames.Fs2020FdCaptKey, 1), (MSFSBlindAssist.Aircraft.A380BuildNames.Fs2020FdFoKey, 1),   // both flight directors on
    };

    private static readonly (string Key, double Value)[] Fs2024SetUp =
    {
        ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0), ("A32NX_FCU_EFIS_R_BARO_IS_INHG", 0),   // hPa
        ("A32NX_FCU_FD_LIGHT_ON", 1),                // no PRIM word: the light decides
    };

    [Theory]
    [InlineData("FS2020")]
    [InlineData("FS2024")]
    public async Task On_each_build_its_own_set_up_aircraft_reads_already_set(string sim)
    {
        var r = await FbwA380FlowRun.Run("COCKPIT_PREP", "COCKPIT_PREP", Steps,
            sim == "FS2020" ? Fs2020SetUp : Fs2024SetUp, windowHasTicked: false, simulator: sim);
        Assert.Empty(r.Unfinished);
        Assert.Contains("CP_BARO", r.TickedAfterRun);
        Assert.Contains("CP_FD", r.TickedAfterRun);
    }

    [Theory]
    [InlineData("FS2020")]
    [InlineData("FS2024")]
    public async Task The_other_builds_variables_never_pass_for_set(string sim)
    {
        var r = await FbwA380FlowRun.Run("COCKPIT_PREP", "COCKPIT_PREP", Steps,
            sim == "FS2020" ? Fs2024SetUp : Fs2020SetUp, windowHasTicked: false, simulator: sim);
        Assert.Contains("CP_BARO", r.Unfinished);
        Assert.Contains("CP_FD", r.Unfinished);
        Assert.DoesNotContain("CP_BARO", r.TickedAfterRun);
        Assert.DoesNotContain("CP_FD", r.TickedAfterRun);
    }
}
